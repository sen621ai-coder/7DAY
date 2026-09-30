using System;
using System.Collections.Generic;
using PZAEC.Fishing.Simulation;
using C = PZAEC.Fishing.Contracts;

internal static class ContractTests
{
    private sealed class World : C.IWaterQuery
    {
        public bool BottomKnown=true, Unloaded;
        public C.WaterSample SampleColumn(C.Vec3 position,float above,float below)
        {return new C.WaterSample {Status=Unloaded?C.WaterSampleStatus.Unloaded:C.WaterSampleStatus.Valid,
            SurfacePoint=new C.Vec3(position.X,0,position.Z),BottomY=-4,DepthMeters=4,BottomKnown=BottomKnown};}
        public C.SegmentHit TraceSolid(C.Vec3 from,C.Vec3 to){return new C.SegmentHit {Unloaded=Unloaded};}
    }
    private sealed class Random : C.IRandomSource
    {
        private readonly SeededSimulationRandom r=new SeededSimulationRandom(29);
        public uint NextUInt(){return (uint)(r.NextUnit()*uint.MaxValue);}
        public float Next01(){return (NextUInt()>>8)/16777216f;}
    }
    private sealed class Sink : C.IEventSink
    {
        public readonly List<C.FishingEvent> Events=new List<C.FishingEvent>();
        public void Emit(C.FishingEvent e){Events.Add(e);}
    }
    private static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    private static C.EnvironmentFrame Environment()
    {return new C.EnvironmentFrame {CanFish=true,IsGrounded=true,PlayerEntityId=1,
        PlayerPosition=C.Vec3.Zero,ViewForward=new C.Vec3(0,0,1),ViewRight=new C.Vec3(1,0,0),
        Rod=new C.RodPose {Root=new C.Vec3(0,1.3f,0),Forward=new C.Vec3(0,0,1),Right=new C.Vec3(1,0,0),PitchRadians=.2f}};}
    private static C.FishingConfig Config()
    {var c=new C.FishingConfig();c.Fish.NibbleMinSeconds=.2f;c.Fish.NibbleMaxSeconds=.2f;c.Fish.BiteWaitMinSeconds=.1f;c.Fish.BiteWaitMaxSeconds=.1f;return c;}
    private static C.SessionStart Start(C.AuthorityMode mode=C.AuthorityMode.Standalone)
    {return new C.SessionStart {SessionId=Guid.NewGuid(),PlayerEntityId=1,PlayerPersistentId="test",FishDefinitionId="carp",Seed=29,CastTarget=new C.Vec3(0,0,8),Authority=mode};}
    private static C.ControlIntent Intent(){return new C.ControlIntent {RodPitchRadians=.2f,Drag01=.3f};}
    private static ContractFishingSimulation Begin(Sink sink,World world=null,C.FishingConfig config=null,C.AuthorityMode mode=C.AuthorityMode.Standalone)
    {var s=new ContractFishingSimulation();s.Begin(Start(mode),config??Config(),Environment(),new Random(),world??new World(),sink);return s;}
    private static void Hook(ContractFishingSimulation s,Sink sink,ref C.ControlIntent intent)
    {
        int guard=0;
        while(s.Current.Phase!=C.FishingPhase.BiteWindow && !s.Current.IsTerminal && guard++<2000)s.Step(C.FishingContract.FixedStepSeconds,intent,Environment(),sink);
        Assert(s.Current.Phase==C.FishingPhase.BiteWindow,"No shared bite window");
        for(int i=0;i<25;i++)s.Step(C.FishingContract.FixedStepSeconds,intent,Environment(),sink);
        intent.RodPitchRadians=.8f;intent.Strike=true;
        s.Step(C.FishingContract.FixedStepSeconds,intent,Environment(),sink);intent.Strike=false;
        Assert(s.Current.Phase==C.FishingPhase.Hooked,"Shared contract failed hook: "+s.Current.Failure);
    }
    internal static void Run(Action<string,Action> test)
    {
        test("v1 strike edge may precede measured mouse raise by one tick",()=>{
            var sink=new Sink();var s=Begin(sink);var intent=Intent();
            while(s.Current.Phase!=C.FishingPhase.BiteWindow&&!s.Current.IsTerminal)s.Step(C.FishingContract.FixedStepSeconds,intent,Environment(),sink);
            for(int i=0;i<25;i++)s.Step(C.FishingContract.FixedStepSeconds,intent,Environment(),sink);
            intent.Strike=true;s.Step(C.FishingContract.FixedStepSeconds,intent,Environment(),sink);
            Assert(s.Current.Phase==C.FishingPhase.BiteWindow,"Button edge prematurely rejected");
            intent.Strike=false;intent.RodPitchRadians=.8f;s.Step(C.FishingContract.FixedStepSeconds,intent,Environment(),sink);
            Assert(s.Current.Phase==C.FishingPhase.Hooked,"Neighbouring mouse frame failed hook");
        });
        test("v1 strike without measured raise still fails after short grace",()=>{
            var sink=new Sink();var s=Begin(sink);var intent=Intent();
            while(s.Current.Phase!=C.FishingPhase.BiteWindow&&!s.Current.IsTerminal)s.Step(C.FishingContract.FixedStepSeconds,intent,Environment(),sink);
            for(int i=0;i<25;i++)s.Step(C.FishingContract.FixedStepSeconds,intent,Environment(),sink);
            intent.Strike=true;s.Step(C.FishingContract.FixedStepSeconds,intent,Environment(),sink);intent.Strike=false;
            for(int i=0;i<15&&!s.Current.IsTerminal;i++)s.Step(C.FishingContract.FixedStepSeconds,intent,Environment(),sink);
            Assert(s.Current.IsTerminal&&s.Current.Phase!=C.FishingPhase.Resolved,"Button-only strike falsely succeeded");
        });
        test("v1 recent measured raise may precede strike edge by one tick",()=>{
            var sink=new Sink();var s=Begin(sink);var intent=Intent();
            while(s.Current.Phase!=C.FishingPhase.BiteWindow&&!s.Current.IsTerminal)s.Step(C.FishingContract.FixedStepSeconds,intent,Environment(),sink);
            for(int i=0;i<25;i++)s.Step(C.FishingContract.FixedStepSeconds,intent,Environment(),sink);
            intent.RodPitchRadians=.24f;s.Step(C.FishingContract.FixedStepSeconds,intent,Environment(),sink);
            intent.Strike=true;s.Step(C.FishingContract.FixedStepSeconds,intent,Environment(),sink);
            Assert(s.Current.Phase==C.FishingPhase.Hooked,"Recent raise discarded at button edge");
        });
        test("v1 adapter casts/hooks and supplies physical snapshot + ordered events",()=>{
            var sink=new Sink();var s=Begin(sink);var intent=Intent();Hook(s,sink,ref intent);
            double peakLoad=0;
            for(int i=0;i<180;i++){s.Step(C.FishingContract.FixedStepSeconds,intent,Environment(),sink);peakLoad=Math.Max(peakLoad,s.Current.RodLoadNewtons.Length);}
            Assert(s.Current.Tick>0 && peakLoad>1 && !s.Current.IsTerminal,"Missing load or unexpected failure: "+s.Current.Failure);
            Assert(s.Current.Rod.Tip.IsFinite && s.Current.FishMassKg==3,"Invalid physical output");
            Assert(sink.Events.Exists(e=>e.Kind==C.FishingEventKind.Strike) && sink.Events.Exists(e=>e.Kind==C.FishingEventKind.Hooked),"Missing strike/hook events");
            for(int i=0;i<sink.Events.Count;i++)Assert(sink.Events[i].Sequence==i+1 && sink.Events[i].SessionId==s.Current.SessionId,"Event identity/order");
        });
        test("v1 adapter enforces rod angle and angular speed limits",()=>{
            var sink=new Sink();var s=Begin(sink);var intent=Intent();intent.RodPitchRadians=100;intent.RodYawRadians=100;
            s.Step(C.FishingContract.FixedStepSeconds,intent,Environment(),sink);
            Assert(s.Current.Rod.PitchRadians<=.2+2.5*C.FishingContract.FixedStepSeconds+1e-5,"Angular speed ignored");
            for(int i=0;i<90;i++)s.Step(C.FishingContract.FixedStepSeconds,intent,Environment(),sink);
            Assert(s.Current.Rod.PitchRadians<=1.3+1e-6 && s.Current.Rod.YawRadians<=1.2+1e-6,"Angle clamp ignored");
        });
        test("v1 adapter preserves external cancel cause and terminal idempotence",()=>{
            var sink=new Sink();var s=Begin(sink);s.Cancel(C.FailureReason.Damaged,sink);int count=sink.Events.Count;
            s.Cancel(C.FailureReason.WorldClosed,sink);s.Step(C.FishingContract.FixedStepSeconds,Intent(),Environment(),sink);
            Assert(s.Current.Failure==C.FailureReason.Damaged && sink.Events.Count==count && s.Current.RodLoadNewtons.Length==0,"Cancel semantics");
        });
        test("v1 adapter rejects variable dt, stale input, invalid numeric configuration",()=>{
            var sink=new Sink();var s=Begin(sink);s.Step(.1f,Intent(),Environment(),sink);Assert(s.Current.Failure==C.FailureReason.InvalidInput,"Variable dt accepted");
            s=Begin(sink);var intent=Intent();intent.InputSequence=5;s.Step(C.FishingContract.FixedStepSeconds,intent,Environment(),sink);intent.InputSequence=4;
            s.Step(C.FishingContract.FixedStepSeconds,intent,Environment(),sink);Assert(s.Current.Failure==C.FailureReason.InvalidInput,"Stale input accepted");
            var c=Config();c.Float.HeightMeters=float.NaN;bool threw=false;try{Begin(sink,null,c);}catch(ArgumentException){threw=true;}Assert(threw,"NaN config accepted");
        });
        test("v1 unknown bottom stays within sampled wet column; unload cancels",()=>{
            var sink=new Sink();var world=new World {BottomKnown=false};var s=Begin(sink,world);var intent=Intent();Hook(s,sink,ref intent);
            for(int i=0;i<180;i++)s.Step(C.FishingContract.FixedStepSeconds,intent,Environment(),sink);
            Assert(s.Current.FishPosition.Y>=-4 && !s.Current.IsTerminal,"Unknown bottom incorrectly invented/rejected");
            world.Unloaded=true;s.Step(C.FishingContract.FixedStepSeconds,intent,Environment(),sink);Assert(s.Current.Failure==C.FailureReason.InvalidWater,"Unload reason");
        });
        test("v1 config copy isolates running session from external edits",()=>{
            var sink=new Sink();var c=Config();var s=Begin(sink,null,c);c.Rod.LengthMeters=999;c.Fish.MassKg=99;c.Hook.MinimumStrikeSpeedRadiansPerSecond=999;
            var intent=Intent();Hook(s,sink,ref intent);Assert(s.Current.FishMassKg==3 && s.Current.Rod.Tip.Length<6,"Config reference leaked");
        });
        test("v1 player distance and unavailable environment fail closed",()=>{
            var sink=new Sink();var s=Begin(sink);var env=Environment();env.PlayerPosition=new C.Vec3(100,0,0);
            s.Step(C.FishingContract.FixedStepSeconds,Intent(),env,sink);Assert(s.Current.Failure==C.FailureReason.TooFar,"Distance ignored");
            s=Begin(sink);env=Environment();env.CanFish=false;env.UnavailableReason=C.FailureReason.Dead;
            s.Step(C.FishingContract.FixedStepSeconds,Intent(),env,sink);Assert(s.Current.Failure==C.FailureReason.Dead,"Environment cause lost");
        });
        test("v1 landing emits once on authority and never on predicted client",()=>{
            foreach(var mode in new[]{C.AuthorityMode.Standalone,C.AuthorityMode.PredictedClient}){
                var sink=new Sink();var config=Config();config.Fish.StaminaJoules=70;config.Fish.RecoveryWatts=0;
                var s=Begin(sink,null,config,mode);var intent=Intent();Hook(s,sink,ref intent);intent.Reel01=1;intent.Drag01=.5f;
                for(int i=0;i<17000 && !s.Current.IsTerminal;i++){
                    if(s.Current.FishStamina01<.25f)intent.RodPitchRadians=1.2f;
                    s.Step(C.FishingContract.FixedStepSeconds,intent,Environment(),sink);
                }
                Assert(s.Current.Phase==C.FishingPhase.Resolved,"Shared landing failed: "+s.Current.Phase+"/"+s.Current.Failure);
                int count=sink.Events.FindAll(e=>e.Kind==C.FishingEventKind.Landed).Count;
                Assert(count==(mode==C.AuthorityMode.Standalone?1:0),"Incorrect authority event count");
            }
        });
    }
}
