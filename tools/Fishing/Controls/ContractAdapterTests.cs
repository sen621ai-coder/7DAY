using System;
using PZAEC.Fishing.Contracts;
using PZAEC.Fishing.Controls;
using PZAEC.Fishing.Runtime;

internal static class ContractAdapterTests
{
    static int passed;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Near(float a, float b) { Check(Math.Abs(a-b)<0.00001, a+" != "+b); }
    static void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS adapter: "+name); }
    static RawInputFrame Input() { return new RawInputFrame { Sequence=10, InputAllowed=true, DurationSeconds=1f/60 }; }
    static EnvironmentFrame Environment() { return new EnvironmentFrame { CanFish=true, IsGrounded=true, Rod=new RodPose { Forward=new Vec3(0,0,1),Right=new Vec3(1,0,0) } }; }
    static FishingSnapshot Snapshot(float tension=0) { return new FishingSnapshot { Phase=FishingPhase.Fighting,FishPosition=new Vec3(0,0,5),LineTensionNewtons=tension }; }
    static IFishingControls Create(ControlConfig c=null, RodConfig r=null) { var a=new FishingControlsAdapter(); a.Reset(c??new ControlConfig(),r??new RodConfig()); return a; }
    public static void Run()
    {
        Test("Subtick free look/recenter cannot leak motion or actions after release",()=>{
            foreach(bool free in new[]{true,false}) {
                var accum=new InputAccumulator();var controls=Create(new ControlConfig{AutoBackThreshold01=0});
                var i=Input();i.FreeLookHeld=free;i.RecenterHeld=!free;i.MouseBackDelta=100;
                i.MouseRightDelta=100;i.DragAdjustDelta=.2f;i.StrikePressed=true;i.ReelHeld=true;
                accum.Push(i);
                i.Sequence++;i.FreeLookHeld=i.RecenterHeld=false;i.MouseBackDelta=i.MouseRightDelta=0;
                i.DragAdjustDelta=0;i.StrikePressed=false;accum.Push(i);
                var result=controls.Step(1f/60,accum.Consume(1,1f/60),Snapshot(),Environment());
                Near(result.RodPitchRadians,.35f);Near(result.RodYawRadians,0);Near(result.Drag01,.5f);
                Near(result.Movement.ExtraForward,0);Check(!result.Strike&&result.Reel01==0,"suspended action leaked");
                i.Sequence++;i.ReelHeld=false;accum.Push(i);
                controls.Step(1f/60,accum.Consume(1,1f/60),Snapshot(),Environment());
                i.Sequence++;i.MouseBackDelta=.1f;i.StrikePressed=true;i.ReelHeld=true;accum.Push(i);
                result=controls.Step(1f/60,accum.Consume(1,1f/60),Snapshot(),Environment());
                Check(result.RodPitchRadians>.35f&&result.Strike&&result.Reel01==1,"control failed to resume");
            }
        });
        Test("Implements shared interface, same sequence allowed across substeps",()=>{
            var a=Create(); var i=Input(); i.MouseBackDelta=.1f;
            var first=a.Step(1f/60,i,Snapshot(),Environment()); var second=a.Step(1f/60,i,Snapshot(),Environment());
            Check(second.RodPitchRadians>first.RodPitchRadians,"second substep lost"); Check(second.InputSequence==10,"source sequence lost");
        });
        Test("Actual accumulator divides mouse and drag exactly once",()=>{
            var accum=new InputAccumulator(); var i=Input(); i.MouseBackDelta=.4f; i.DragAdjustDelta=.2f; i.StrikePressed=true;
            Check(accum.Push(i),"push failed"); var a=Create();
            var first=a.Step(1f/60,accum.Consume(2,1f/60),Snapshot(),Environment());
            var second=a.Step(1f/60,accum.Consume(1,1f/60),Snapshot(),Environment());
            Near(second.RodPitchRadians,.35f+.4f*.04f); Near(second.Drag01,.7f);
            Check(first.Strike&&!second.Strike,"strike repeated/lost");
        });
        Test("No tick render frame retains mouse through actual accumulator",()=>{
            var accum=new InputAccumulator(); var i=Input(); i.MouseRightDelta=.1f; accum.Push(i);
            i.Sequence++; i.MouseRightDelta=.2f; accum.Push(i);
            var result=Create().Step(1f/60,accum.Consume(1,1f/60),Snapshot(),Environment()); Near(result.RodYawRadians,.012f);
        });
        Test("Movement contains extras only; native diagonal perpendicular component preserved",()=>{
            var i=Input(); i.MoveRight=1; i.MoveForward=-1;
            var result=Create().Step(1f/60,i,Snapshot(55),Environment());
            Near(result.Movement.ExtraRight,0); Near(result.Movement.ExtraForward,0);
            float right=1,forward=-1; MovementBridge.Apply(ref right,ref forward,new Vec3(1,0,0),new Vec3(0,0,1),result.Movement);
            Near(right,(float)(1/Math.Sqrt(2))); Near(forward,-.5f*(float)(1/Math.Sqrt(2)));
        });
        Test("Auto retreat bounded, respects S/W and decays",()=>{
            var cfg=new ControlConfig { AutoBackThreshold01=0,MaxAutoBack01=.6f }; var a=Create(cfg); var i=Input(); i.MouseBackDelta=.1f;
            var result=a.Step(1f/60,i,Snapshot(),Environment()); Near(result.Movement.ExtraForward,-.6f);
            i.MoveForward=-1; result=a.Step(1f/60,i,Snapshot(),Environment()); Near(result.Movement.ExtraForward,0);
            i.MoveForward=1; result=a.Step(1f/60,i,Snapshot(),Environment()); Near(result.Movement.ExtraForward,0);
            i.MoveForward=0; a.Step(1f/60,i,Snapshot(),Environment()); i.MouseBackDelta=0;
            for(int n=0;n<6;n++) result=a.Step(1f/60,i,Snapshot(),Environment()); Near(result.Movement.ExtraForward,0);
        });
        Test("World direction independent of camera and vertical fish position",()=>{
            var s=Snapshot(110);s.FishPosition=new Vec3(5,80,0);var e=Environment();e.ViewForward=new Vec3(0,1,0);
            var result=Create().Step(1f/60,Input(),s,e); Near(result.Movement.PullDirection.X,1);Near(result.Movement.PullDirection.Y,0);Near(result.Movement.AgainstPullScale,.1f);
        });
        Test("Angular speed cap and drag displacement semantics",()=>{
            var i=Input();i.MouseBackDelta=100;i.DragAdjustDelta=.1f;
            var result=Create().Step(1f/60,i,Snapshot(),Environment()); Near(result.RodPitchRadians,.35f+2.5f/60);Near(result.Drag01,.6f);
        });
        Test("Free look/recenter discard mouse, feet, drag and resume jump",()=>{
            foreach(bool free in new[]{true,false}) {
                var a=Create(new ControlConfig{AutoBackThreshold01=0});var i=Input();i.FreeLookHeld=free;i.RecenterHeld=!free;i.MouseBackDelta=100;i.DragAdjustDelta=.2f;
                var result=a.Step(1f/60,i,Snapshot(),Environment());Near(result.RodPitchRadians,.35f);Near(result.Drag01,.5f);Near(result.Movement.ExtraForward,0);Check(result.FreeLook==free,"free look");
                i.FreeLookHeld=i.RecenterHeld=false;result=a.Step(1f/60,i,Snapshot(),Environment());Near(result.RodPitchRadians,.35f);Near(result.Movement.ExtraForward,0);Near(result.Drag01,.5f);
            }
        });
        Test("Input loss releases until explicit Reset",()=>{
            var a=Create();var i=Input();i.InputAllowed=false;var result=a.Step(1f/60,i,Snapshot(),Environment());Check(result.Cancel&&!result.Movement.Active,"cancel on loss");
            i.InputAllowed=true;i.MouseBackDelta=100;result=a.Step(1f/60,i,Snapshot(),Environment());Check(!result.Movement.Active&&!result.Strike,"released must stay inactive");
            a.Reset(new ControlConfig(),new RodConfig());Check(a.Step(1f/60,i,Snapshot(),Environment()).Movement.Active,"reset should reactivate");
        });
        Test("Terminal, invalid data and airborne handling",()=>{
            var s=Snapshot();s.Phase=FishingPhase.Resolved;Check(!Create().Step(1f/60,Input(),s,Environment()).Movement.Active,"terminal active");
            var i=Input();i.MouseBackDelta=float.NaN;Check(Create().Step(1f/60,i,Snapshot(),Environment()).Cancel,"NaN accepted");
            var e=Environment();e.IsGrounded=false;Check(!Create().Step(1f/60,Input(),Snapshot(100),e).Movement.Active,"airborne movement");
        });
        Test("Config rejected and copied; shared objects not retained",()=>{
            var c=new ControlConfig();var a=Create(c);c.RadiansPerMouseUnit=100;var i=Input();i.MouseBackDelta=.1f;Near(a.Step(1f/60,i,Snapshot(),Environment()).RodPitchRadians,.354f);
            bool threw=false;try{Create(new ControlConfig{PlayerResistanceNewtons=0});}catch(ArgumentException){threw=true;}Check(threw,"invalid config accepted");
        });
        Test("Sensitivity mapping invariant across fixed dt choices below cap",()=>{
            float baseline=0;foreach(int hz in new[]{30,60,120}){var a=Create();PZAEC.Fishing.Contracts.ControlIntent result=default(PZAEC.Fishing.Contracts.ControlIntent);
                for(int n=0;n<hz;n++){var i=Input();i.MouseBackDelta=2f/hz;result=a.Step(1f/hz,i,Snapshot(),Environment());}
                if(hz==30)baseline=result.RodPitchRadians;else Near(result.RodPitchRadians,baseline);
            }
        });
        Console.WriteLine("Passed "+passed+" shared contract adapter tests.");
    }
}
