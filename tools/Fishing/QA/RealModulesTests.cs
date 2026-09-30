using System;
using System.Collections.Generic;
using PZAEC.Fishing.Contracts;
using PZAEC.Fishing.Networking;
using PZAEC.Fishing.Runtime;
using PZAEC.Fishing.Controls;
using PZAEC.Fishing.Content;
using ContractFishingSimulation = PZAEC.Fishing.Simulation.ContractFishingSimulation;

internal static class RealModulesTests
{
    sealed class Water : IWaterQuery
    {
        public WaterSample SampleColumn(Vec3 p,float above,float below)
        {return new WaterSample {Status=WaterSampleStatus.Valid,SurfacePoint=new Vec3(p.X,0,p.Z),Normal=Vec3.Up,DepthMeters=4,BottomY=-4,BottomKnown=true};}
        public SegmentHit TraceSolid(Vec3 from,Vec3 to){return default(SegmentHit);}
    }
    sealed class NoGraphics : IFishingPresentation
    {
        public void Begin(SessionStart s,FishingConfig c){}
        public void Render(RenderFrame r){throw new Exception("Server must not render");}
        public void OnEvent(FishingEvent e){}
        public void Clear(){}
        public void Dispose(){}
    }
    // Test-only A router: fake world/identity/inventory, REAL simulation, controls and SessionDriver.
    // This is deliberately not a production reward router or a claim of actual dedicated-server QA.
    sealed class Router : IAuthoritySessionRouter, IDisposable
    {
        public SessionDriver Driver;
        public readonly AuthorityInputBuffer Buffer=new AuthorityInputBuffer();
        public readonly List<FishingEvent> Events=new List<FishingEvent>();
        public readonly FishingConfig Config;
        public readonly Water Water=new Water();
        public EnvironmentFrame Env;
        public double Now;
        public Router(string path)
        {
            Config=new FishingContent().Load(path);
            Env=new EnvironmentFrame {CanFish=true,IsGrounded=true,PlayerEntityId=7,ViewForward=new Vec3(0,0,1),ViewRight=new Vec3(1,0,0),
                Rod=new RodPose {Root=new Vec3(0,1.3f,0),Forward=new Vec3(0,0,1),Right=new Vec3(1,0,0),PitchRadians=.35f},Water=Water.SampleColumn(new Vec3(0,0,8),4,16)};
        }
        public bool TryStart(string player,int entity,Vec3 target,out Guid id)
        {
            id=Guid.NewGuid();Driver=new SessionDriver(new ContractFishingSimulation(),new FishingControlsAdapter(),new NoGraphics());
            Driver.Event+=Events.Add;
            Driver.Begin(new SessionStart {SessionId=id,PlayerPersistentId=player,PlayerEntityId=entity,Seed=29,FishDefinitionId=Config.Fish.Id,CastTarget=target,Authority=AuthorityMode.Server},Config,Env,Water);return true;
        }
        public bool AcceptInput(string player,NetworkInput input){return Buffer.Push(input,Now);}
        public void CancelPlayer(string player,FailureReason reason){Driver?.Cancel(reason);Buffer.Clear();}
        public void ApplySnapshot(NetworkSnapshot value){throw new Exception("Authority cannot consume client state");}
        public void Step(FishingNetwork network)
        {
            Driver.Advance(1f/60,Buffer.Consume(Now,1f/60),Env);
            network.Publish(Buffer.ForPublication(Driver.Current,Events.ToArray()));Events.Clear();
        }
        public void Dispose(){Driver?.Dispose();}
    }
    public static void Run(string path)
    {
        using(var r=new Router(path))
        {
            var t=new NetworkTests.Transport();var net=new FishingNetwork(r,t);net.Start(AuthorityMode.Server);net.RegisterPeer("remote","real-module-player",7);
            NetworkTests.Check(net.ReceiveFromPeer("remote",NetworkTests.Start()),"real modules begin through authoritative network router");
            long seq=0,serial=1;bool reached=false;
            for(int i=0;i<10000 && r.Driver.Active;i++)
            {
                r.Now=i/60.0;net.Tick(r.Now);
                var input=NetworkTests.Input(r.Driver.Current.SessionId,++seq,r.Driver.Current.Tick);
                if(r.Driver.Current.Phase==FishingPhase.BiteWindow){input.Input.MouseBackDelta=1;input.Input.StrikePressed=true;reached=true;}
                bool accepted=net.ReceiveFromPeer("remote",FishingWire.Encode(new FishingMessage {Kind=FishingMessageKind.Input,Serial=++serial,Input=input}));
                if(!accepted)throw new Exception("real input rejected at "+i);
                r.Step(net);
                if(reached)break;
            }
            NetworkTests.Check(reached && r.Driver.Current.Phase==FishingPhase.Hooked,"network → buffer → A/C/B/E achieves real bite and hook");
            NetworkTests.Check(r.Driver.Current.Rod.Tip.IsFinite && r.Driver.Current.LineTensionNewtons>=0,"network-driven simulation remains finite");
            r.Now+=1f/60;net.Tick(r.Now);r.Step(net);
            NetworkTests.Check(r.Driver.Current.Phase==FishingPhase.Fighting,"no new network packet still advances server physics");
            NetworkTests.Check(r.Buffer.LastNetworkSequence==seq,"missing input never invents network acknowledgements");
            net.DisconnectPeer("remote");
            NetworkTests.Check(!r.Driver.Active && !r.Driver.Movement.Active && r.Driver.Current.Failure==FailureReason.Disconnected,"disconnect releases real simulation and movement");
            net.Stop();
        }
        // Compare host loopback vs remote byte round trip through the same authority implementation.
        using(var a=new Router(path))using(var b=new Router(path))
        {
            var na=new FishingNetwork(a,new NetworkTests.Transport());var nb=new FishingNetwork(b,new NetworkTests.Transport());
            na.Start(AuthorityMode.Standalone);nb.Start(AuthorityMode.Server);na.RegisterPeer("host","p",7);nb.RegisterPeer("remote","p",7);
            na.ReceiveFromPeer("host",NetworkTests.Start());nb.ReceiveFromPeer("remote",NetworkTests.Start());
            for(int i=0;i<120;i++)
            {
                a.Now=b.Now=i/60.0;na.Tick(a.Now);nb.Tick(b.Now);
                var ai=NetworkTests.Input(a.Driver.Current.SessionId,i+1,a.Driver.Current.Tick);ai.Input.MouseBackDelta=.01f;
                var bi=ai;bi.SessionId=b.Driver.Current.SessionId;
                na.ReceiveFromPeer("host",FishingWire.Encode(new FishingMessage {Kind=FishingMessageKind.Input,Serial=i+2,Input=ai}));
                nb.ReceiveFromPeer("remote",FishingWire.Encode(new FishingMessage {Kind=FishingMessageKind.Input,Serial=i+2,Input=bi}));
                a.Step(na);b.Step(nb);
            }
            NetworkTests.Check(a.Driver.Current.Tick==b.Driver.Current.Tick && (a.Driver.Current.FloatPosition-b.Driver.Current.FloatPosition).Length<.00001f && a.Driver.Current.Rod.PitchRadians==b.Driver.Current.Rod.PitchRadians,"host/remote authority paths produce identical seeded real-module state");
            na.Stop();nb.Stop();
        }
    }
}
