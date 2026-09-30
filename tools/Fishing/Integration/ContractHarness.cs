using System;
using PZAEC.Fishing.Contracts;
using PZAEC.Fishing.Runtime;

public static class ContractHarness
{
    static int checks;
    static void Check(bool condition,string name){if(!condition)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
    static bool Near(float a,float b)=>Math.Abs(a-b)<0.0001f;
    static RawInputFrame Frame(long sequence,float dx=0)=>new RawInputFrame{Sequence=sequence,DurationSeconds=1f/60,InputAllowed=true,MouseRightDelta=dx};
    public static int Main()
    {
        try
        {
            var randomA=new SeededRandom(23);var randomB=new SeededRandom(23);
            for(int i=0;i<100;i++)if(randomA.NextUInt()!=randomB.NextUInt())throw new Exception("random replay");
            Check(true,"seed replay");
            var buffer=new InputAccumulator();var first=Frame(1,12);first.CastPressed=true;buffer.Push(first);
            var a=buffer.Consume(3,1f/60);var b=buffer.Consume(2,1f/60);var c=buffer.Consume(1,1f/60);
            Check(Near(a.MouseRightDelta+b.MouseRightDelta+c.MouseRightDelta,12),"mouse conserved across catch-up ticks");
            Check(a.CastPressed&&!b.CastPressed&&!c.CastPressed,"one-shot cast not repeated");
            Check(!buffer.Push(first),"duplicate input rejected");
            Check(!buffer.Push(Frame(2,float.NaN)),"NaN input rejected");
            Check(buffer.Consume(1,1f/60).MouseRightDelta==0,"mouse not replayed without new input");
            buffer.Clear();buffer.Push(Frame(1,1));buffer.Push(Frame(2,2));Check(buffer.Consume(1,1f/60).MouseRightDelta==3,"subtick frames accumulate");

            float right=0,forward=-1;var pull=new MovementRequest{Active=true,PullDirection=new Vec3(0,0,1),AgainstPullScale=.25f};
            MovementBridge.Apply(ref right,ref forward,new Vec3(1,0,0),new Vec3(0,0,1),pull);
            Check(Near(forward,-.25f),"opposing movement gets load resistance");
            right=0;forward=1;MovementBridge.Apply(ref right,ref forward,new Vec3(1,0,0),new Vec3(0,0,1),pull);
            Check(Near(forward,1),"moving toward fish not slowed");
            right=1;forward=0;MovementBridge.Apply(ref right,ref forward,new Vec3(1,0,0),new Vec3(0,0,1),pull);
            Check(Near(right,1),"lateral movement preserved");
            right=0;forward=0;pull.ExtraForward=-.6f;MovementBridge.Apply(ref right,ref forward,new Vec3(1,0,0),new Vec3(0,0,1),pull);
            Check(Near(forward,-.15f),"mouse backstep goes through same resistance");
            right=0;forward=-.5f;MovementBridge.Apply(ref right,ref forward,new Vec3(1,0,0),new Vec3(0,0,1),MovementRequest.None);
            Check(Near(forward,-.5f),"inactive bridge does not alter native movement");
            var straight=SessionDriver.BuildStraightRod(new RodPose{Root=new Vec3(0,2,0),Forward=new Vec3(0,0,1),Right=new Vec3(1,0,0)},new ControlIntent{RodPitchRadians=(float)Math.PI/2},2);
            Check(Near(straight.Tip.Y,4),"control pitch maps to unbent rod tip exactly once");

            var sim=new FakeSimulation();var controls=new FakeControls();var view=new FakePresentation();
            var host=new SessionDriver(sim,controls,view);var start=new SessionStart{SessionId=Guid.NewGuid(),Seed=7};
            var env=new EnvironmentFrame{CanFish=true};host.Begin(start,new FishingConfig(),env,new FakeWater());
            host.Advance(1f/120,Frame(1,1),env);Check(sim.Current.Tick==0,"no tick before fixed interval");
            host.Advance(1f/120,Frame(2,2),env);Check(sim.Current.Tick==1&&Near(controls.MouseSum,3),"fixed tick consumes accumulated input once");
            host.Advance(2,Frame(3,8),env);Check(sim.Current.Tick==9,"long stall has bounded catch-up");
            Check(Near(controls.MouseSum,11),"bounded catch-up conserves accepted mouse delta");
            env.PlayerPosition=new Vec3(2,0,0);env.Rod.Root=new Vec3(2,2,0);sim.RootSamples.Clear();
            host.Advance(2f/60,Frame(4),env);
            Check(sim.RootSamples.Count==2&&Near(sim.RootSamples[0].X,1)&&Near(sim.RootSamples[1].X,2),"observed frame movement interpolated across substeps");
            host.Emit(new FishingEvent{SessionId=start.SessionId,Sequence=1});host.Emit(new FishingEvent{SessionId=start.SessionId,Sequence=1});
            host.Advance(1f/60,Frame(5),env);Check(view.Events==1,"event deduplication");
            host.Render(Vec3.Zero,true);Check(view.Renders==0,"dedicated path skips rendering");
            host.Cancel(FailureReason.ItemChanged);Check(!host.Active&&!host.Movement.Active&&controls.Released&&view.Cleared,"cancel releases controls movement and visuals");
            host.Begin(start,new FishingConfig(),env,new FakeWater());host.Advance(1f/60,new RawInputFrame{Sequence=1},env);
            Check(!host.Active&&host.Current.Failure==FailureReason.MenuOpened,"menu input disallowed cancels immediately");
            host.Begin(start,new FishingConfig(),env,new FakeWater());sim.Throw=true;
            try{host.Advance(1f/60,Frame(1),env);}catch(InvalidOperationException){}
            Check(!host.Active&&!host.Movement.Active&&controls.Released,"module failure does not leave movement active");
            sim.Throw=false;host.Begin(start,new FishingConfig(),env,new FakeWater());
            env.CanFish=false;env.UnavailableReason=FailureReason.Dead;host.Advance(1f/60,Frame(1),env);
            Check(!host.Active&&host.Current.Failure==FailureReason.Dead,"environment cancellation reason preserved");
            host.Dispose();Check(view.Disposed,"presentation disposed");
            Console.WriteLine("RESULT PASS checks="+checks);return 0;
        }
        catch(Exception e){Console.WriteLine("RESULT FAIL "+e);return 1;}
    }
    sealed class FakeWater:IWaterQuery
    {
        public WaterSample SampleColumn(Vec3 p,float above,float below)=>new WaterSample{Status=WaterSampleStatus.Valid,SurfacePoint=p,DepthMeters=3,BottomY=p.Y-3,BottomKnown=true};
        public SegmentHit TraceSolid(Vec3 a,Vec3 b)=>default(SegmentHit);
    }
    sealed class FakeSimulation:IFishingSimulation
    {
        public bool Throw;public FishingSnapshot Current{get;private set;}
        public readonly System.Collections.Generic.List<Vec3> RootSamples=new System.Collections.Generic.List<Vec3>();
        public void Begin(SessionStart s,FishingConfig c,EnvironmentFrame e,IRandomSource r,IWaterQuery w,IEventSink sink){Current=new FishingSnapshot{SessionId=s.SessionId,Phase=FishingPhase.Casting};}
        public FishingSnapshot Step(float dt,ControlIntent i,EnvironmentFrame e,IEventSink sink){if(Throw)throw new InvalidOperationException("fake failure");RootSamples.Add(e.Rod.Root);var s=Current;s.Tick++;s.TimeSeconds+=dt;return Current=s;}
        public FishingSnapshot Cancel(FailureReason reason,IEventSink sink){var s=Current;s.Phase=FishingPhase.Cancelled;s.Failure=reason;return Current=s;}
    }
    sealed class FakeControls:IFishingControls
    {
        public float MouseSum;public bool Released;
        public void Reset(ControlConfig c,RodConfig r){Released=false;MouseSum=0;}
        public ControlIntent Step(float dt,RawInputFrame i,FishingSnapshot s,EnvironmentFrame e){MouseSum+=i.MouseRightDelta;return new ControlIntent{Movement=new MovementRequest{Active=true,ExtraForward=-.5f,AgainstPullScale=.5f}};}
        public void Release(){Released=true;}
    }
    sealed class FakePresentation:IFishingPresentation
    {
        public int Events,Renders;public bool Cleared,Disposed;
        public void Begin(SessionStart s,FishingConfig c){Cleared=false;}
        public void Render(RenderFrame f){Renders++;}
        public void OnEvent(FishingEvent e){Events++;}
        public void Clear(){Cleared=true;}
        public void Dispose(){Disposed=true;}
    }
}
