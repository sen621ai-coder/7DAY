using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using PZAEC.Fishing.Simulation;

internal sealed class Pool : IWaterDomain
{
    public bool Available=true, Obstructed, FishBlocked;
    public double Radius=150, Surface=0, Bottom=-4;
    public bool TrySample(SimVector v,out WaterSample sample)
    { sample=new WaterSample(Surface,Bottom); return Available && v.Horizontal.Length < Radius; }
    public bool IsFishPathClear(SimVector from,SimVector to) { return !FishBlocked; }
    public bool IsLineClear(SimVector from,SimVector to) { return !Obstructed; }
}
internal sealed class FixedRandom : ISimulationRandom
{
    private readonly double value;
    public FixedRandom(double value) { this.value=value; }
    public double NextUnit() { return value; }
}
internal static class SimulationTests
{
    private static int passed, failed;
    private static readonly List<string> results=new List<string>();
    private static string artifacts;
    private static void Assert(bool b,string message) { if(!b) throw new Exception(message); }
    private static void Near(double a,double b,double tol,string message) { Assert(Math.Abs(a-b)<=tol,message+" actual="+a+" expected="+b); }
    private static void Test(string name,Action test)
    {
        try { test(); passed++; results.Add("PASS "+name); }
        catch(Exception e) { failed++; results.Add("FAIL "+name+": "+e.Message); }
        Console.WriteLine(results[results.Count-1]);
    }
    private static SimulationParameters Fast()
    { return new SimulationParameters { CastSeconds=.05,SettleSeconds=.05,WaitMinSeconds=.05,WaitMaxSeconds=.05,NibbleSeconds=.2 }; }
    private static SimulationInput Input()
    { return new SimulationInput { PlayerPosition=new SimVector(0,0,0),UnloadedRodTip=new SimVector(0,1.5,1),DragSetting=.3,StrikeStrength=.6 }; }
    private static FishingSimulation Cast(SimulationParameters p,Pool pool=null,uint seed=29)
    {
        var s=new FishingSimulation("test-"+seed,p,pool ?? new Pool(),seed);
        s.BeginCast(Input(),new SimVector(0,0,8)); return s;
    }
    private static FishingSimulation Hook(SimulationParameters p,Pool pool=null,uint seed=29)
    {
        var s=Cast(p,pool,seed); var input=Input();
        for(int i=0;i<20000 && s.Snapshot().BiteProgress<.48;i++)
        { s.Step(input); Assert(!s.IsTerminal,"Failed before hooking: "+s.Snapshot().Failure); }
        Assert(s.Snapshot().Phase==FishingPhase.BiteWindow,"Bite window missing");
        input.Strike=true; s.Step(input);
        Assert(s.Snapshot().Phase==FishingPhase.Hooked,"Expected Hooked"); return s;
    }
    private static void Run(FishingSimulation s,SimulationInput input,double seconds)
    { for(int i=0;i<(int)Math.Round(seconds/s.FixedStep);i++)s.Step(input); }
    private static SimulationParameters Durable()
    { var p=Fast();p.LineStrength=1000;p.SlackLossSeconds=300;p.SessionTimeoutSeconds=300;return p; }
    private static double PullPeak(double speed,double seconds,double drag=1)
    {
        var p=Durable();var s=Hook(p);var input=Input();input.DragSetting=drag;
        double peak=0;
        for(int i=0;i<(int)(seconds/p.FixedStep);i++)
        {
            var delta=new SimVector(0,0,-speed*p.FixedStep);
            input.PlayerPosition+=delta;input.UnloadedRodTip+=delta;s.Step(input);
            peak=Math.Max(peak,s.Snapshot().Tension);
        }
        return peak;
    }
    public static int Main(string[] args)
    {
        artifacts=args.Length>0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("artifacts");Directory.CreateDirectory(artifacts);
        Test("cast -> visible bite -> hook; early/late/weak strike fail",()=>{
            var p=Fast();var good=Hook(p);Assert(good.Snapshot().HookQuality>.8,"Good timing quality");
            var early=Cast(p);var input=Input();input.Strike=true;Run(early,input,.2);Assert(early.Snapshot().Failure==FailureReason.EarlyStrike,"Early strike");
            var late=Cast(p);Run(late,Input(),4);Assert(late.Snapshot().Failure==FailureReason.MissedBite,"Late strike");
            var weak=Cast(p);input=Input();while(weak.Snapshot().Phase!=FishingPhase.BiteWindow)weak.Step(input);
            input.Strike=true;input.StrikeStrength=.05;weak.Step(input);Assert(weak.Snapshot().Failure==FailureReason.WeakStrike,"Weak strike");
        });
        Test("four bite patterns have distinct physical float motion",()=>{
            double[] y=new double[4];double[] x=new double[4];
            for(int j=0;j<4;j++){
                var s=new FishingSimulation("float-"+j,Fast(),new Pool(),new FixedRandom((j+.1)/4));s.BeginCast(Input(),new SimVector(0,0,8));
                while(s.Snapshot().BiteProgress<.6 && !s.IsTerminal)s.Step(Input());
                Assert((int)s.Snapshot().BitePattern==j,"Pattern selection");y[j]=s.Snapshot().BobberPosition.Y;x[j]=s.Snapshot().BobberPosition.X;
            }
            Assert(y[0]<-.04 && y[1]>.04 && x[2]>.05 && y[3]<y[0],"dip/lift/travel/dive not distinguishable");
        });
        Test("same seed and inputs reproduce all ticks and events",()=>{
            var a=Hook(Durable());var b=Hook(Durable());var input=Input();
            for(int i=0;i<3600;i++){
                input.Reel=i%400<200;a.Step(input);b.Step(input);var sa=a.Snapshot();var sb=b.Snapshot();
                Near((sa.FishPosition-sb.FishPosition).Length,0,0,"Fish replay");Near(sa.Tension,sb.Tension,0,"Tension replay");
                Near(sa.Stamina,sb.Stamina,0,"Stamina replay");Assert(sa.Phase==sb.Phase,"Phase replay");
            }
            var ea=a.DrainEvents();var eb=b.DrainEvents();Assert(ea.Length==eb.Length,"Event count");
            for(int i=0;i<ea.Length;i++)Assert(ea[i].Kind==eb[i].Kind && ea[i].Tick==eb[i].Tick && ea[i].Sequence==eb[i].Sequence,"Event replay");
        });
        Test("slack line generates zero force before tightening",()=>{
            var p=Durable();p.CruiseForce=0;p.BurstForce=0;var s=Hook(p);var input=Input();
            // Approach the fish using actual anchors, producing additional slack rather than tension.
            for(int i=0;i<120;i++){
                input.PlayerPosition+=new SimVector(0,0,.008);input.UnloadedRodTip+=new SimVector(0,0,.008);s.Step(input);
                Near(s.Snapshot().Tension,0,1e-9,"Slack force");
            }
        });
        Test("backward movement loads line; faster equal-distance pull has higher peak",()=>{
            double stationary=PullPeak(0,.5),slow=PullPeak(1,1),fast=PullPeak(8,.125);
            Assert(slow>stationary,"Backward motion must load line");Assert(fast>slow,"Fast impulse must exceed gentle pull");
            Console.WriteLine("  peak N stationary="+stationary+" slow="+slow+" fast="+fast);
        });
        Test("drag pays out and reduces sustained load",()=>{
            var low=Hook(Durable());var locked=Hook(Durable());var a=Input();var b=Input();a.DragSetting=0;b.DragSetting=1;
            Run(low,a,5);Run(locked,b,5);var x=low.Snapshot();var y=locked.Snapshot();
            Assert(x.LineLength>y.LineLength+.1,"Drag must pay out");Assert(x.Tension<y.Tension,"Drag must reduce load");
            Assert(Array.Exists(low.DrainEvents(),e=>e.Kind==FishingEventKind.DragReleased),"Drag event");
        });
        Test("limited spool payout cannot suppress extreme jerk",()=>{
            var p=Fast();p.LineStrength=5;p.MaxPayoutSpeed=.05;var s=Hook(p);var input=Input();input.DragSetting=0;
            for(int i=0;i<240 && !s.IsTerminal;i++){
                input.PlayerPosition+=new SimVector(0,0,-.08);input.UnloadedRodTip+=new SimVector(0,0,-.08);s.Step(input);
            }
            Assert(s.Snapshot().Phase==FishingPhase.LineBroken,"Expected overload break");Near(s.Snapshot().RodForce.Length,0,0,"Force released");
        });
        Test("sustained moderate overload accumulates damage",()=>{
            var p=Fast();p.LineStrength=9;p.PeakBreakMultiplier=10;p.FatigueSeconds=.1;var s=Hook(p);var input=Input();input.DragSetting=1;
            for(int i=0;i<1200 && !s.IsTerminal;i++)s.Step(input);
            Assert(s.Snapshot().Failure==FailureReason.FatigueOverload,"Expected fatigue failure, got "+s.Snapshot().Failure);
        });
        Test("persistent slack loses hook",()=>{
            var p=Fast();p.CruiseForce=0;p.BurstForce=0;p.SlackLossSeconds=.2;var s=Hook(p);Run(s,Input(),1);
            Assert(s.Snapshot().Failure==FailureReason.SlackLine,"Expected slack loss");
        });
        Test("three distinct bursts and bounded effort-driven stamina",()=>{
            var p=Durable();var s=Hook(p);var input=Input();int burstEvents=0;double min=1;
            for(int i=0;i<3600;i++){
                s.Step(input);var st=s.Snapshot();min=Math.Min(min,st.Stamina);
                Assert(st.Stamina>=0 && st.Stamina<=1,"Energy bounds");
                foreach(var e in s.DrainEvents())if(e.Kind==FishingEventKind.Sprint)burstEvents++;
            }
            Assert(burstEvents>=3,"Expected three bursts; got "+burstEvents);Assert(min<.95,"Fish expends energy");
            var rest=Durable();rest.CruiseForce=0;rest.BurstForce=0;var idle=Hook(rest);Run(idle,input,15);
            Near(idle.Snapshot().Stamina,1,1e-9,"No thrust/work must not consume stamina merely with time");
        });
        Test("side pressure changes fish trajectory",()=>{
            var a=Hook(Durable());var b=Hook(Durable());var input=Input();var moved=Input();
            for(int i=0;i<480;i++){
                if(i<120)moved.UnloadedRodTip+=new SimVector(.01,0,0);
                a.Step(input);b.Step(moved);
            }
            Assert((a.Snapshot().FishPosition-b.Snapshot().FishPosition).Length>.1,"Side pressure ignored");
        });
        Test("shoreline and bottom contain fish; solid sweep blocks movement",()=>{
            var pool=new Pool { Radius=9,Bottom=-.7 };var s=Hook(Durable(),pool);Run(s,Input(),25);var st=s.Snapshot();
            Assert(st.FishPosition.Horizontal.Length<9,"Escaped water");Assert(st.FishPosition.Y>=-.55-1e-9 && st.FishPosition.Y<=-.15+1e-9,"Depth bounds");
            pool=new Pool();s=Hook(Durable(),pool);pool.FishBlocked=true;SimVector start=s.Snapshot().FishPosition;Run(s,Input(),2);
            Near((s.Snapshot().FishPosition-start).Length,0,1e-9,"Fish passed solid obstacle");
        });
        Test("unloaded water and obstructed line cancel with explicit reasons",()=>{
            var pool=new Pool();var s=Hook(Fast(),pool);pool.Available=false;s.Step(Input());Assert(s.Snapshot().Failure==FailureReason.WaterUnavailable,"Water unload");
            pool=new Pool();s=Hook(Fast(),pool);pool.Obstructed=true;s.Step(Input());Assert(s.Snapshot().Failure==FailureReason.LineObstructed,"Line obstruction");
        });
        Test("terminal state is inert; events unique and drainable",()=>{
            var s=Hook(Fast());var input=Input();input.Cancel=true;s.Step(input);long tick=s.Snapshot().Tick;Run(s,input,1);
            Assert(s.Snapshot().Tick==tick,"Terminal advanced");var events=s.DrainEvents();int cancelled=0;
            for(int i=0;i<events.Length;i++){Assert(events[i].Sequence==i+1,"Sequence not monotonic");if(events[i].Kind==FishingEventKind.Cancelled)cancelled++;}
            Assert(cancelled==1 && s.DrainEvents().Length==0,"Duplicate terminal event");
        });
        Test("invalid inputs/config fail and teleports release control",()=>{
            var p=Fast();p.LineStiffness=double.NaN;bool threw=false;try{Cast(p);}catch(ArgumentOutOfRangeException){threw=true;}Assert(threw,"NaN config accepted");
            var s=Hook(Fast());var input=Input();input.DragSetting=double.PositiveInfinity;threw=false;try{s.Step(input);}catch(ArgumentOutOfRangeException){threw=true;}Assert(threw,"Infinite input accepted");
            input=Input();input.UnloadedRodTip+=new SimVector(20,0,0);s.Step(input);Assert(s.Snapshot().Failure==FailureReason.AnchorDiscontinuity,"Teleport must cancel");
        });
        Test("configuration is copied at session creation",()=>{
            var p=Durable();var s=Hook(p);p.LineStrength=.1;p.BurstForce=2000;Run(s,Input(),5);Assert(!s.IsTerminal,"External config mutated running session");
        });
        Test("30/60/120/240 Hz solver stable and convergent",()=>{
            var values=new List<SimulationSnapshot>();
            foreach(int hz in new[]{30,60,120,240}){
                var p=Durable();p.FixedStep=1.0/hz;var s=Hook(p);Run(s,Input(),6);var st=s.Snapshot();values.Add(st);
                Assert(st.FishPosition.IsFinite && st.FishVelocity.IsFinite && !double.IsNaN(st.Tension) && st.Tension<1000,"Unstable solver at "+hz);
                Assert(!st.IsTerminal,"Unexpected terminal at "+hz);
            }
            Assert((values[2].FishPosition-values[3].FishPosition).Length<.35,"120/240 fish convergence");
            Assert((values[0].FishPosition-values[3].FishPosition).Length<1.2,"30/240 drift too large");
        });
        Test("fixed tick scheduling gives identical 30 and 144 fps result",()=>{
            Func<int,SimulationSnapshot> renderLoop=fps=>{
                var s=Hook(Durable());double accumulated=0;int ticks=0;
                for(int frame=0;frame<fps*8;frame++){
                    accumulated+=1.0/fps;
                    while(accumulated+1e-10>=s.FixedStep){s.Step(Input());accumulated-=s.FixedStep;ticks++;}
                }
                Assert(ticks==960,"Tick count "+fps);return s.Snapshot();
            };
            var a=renderLoop(30);var b=renderLoop(144);Near((a.FishPosition-b.FishPosition).Length,0,0,"Render FPS drift");
        });
        Test("energetic fish makes a near-bank counter surge",()=>{
            var s=Hook(Durable());var input=Input();bool near=false;
            for(int i=0;i<600 && !s.IsTerminal;i++){
                SimVector approach=(s.Snapshot().FishPosition-input.PlayerPosition).Horizontal.Normalized*(4*s.FixedStep);
                input.PlayerPosition+=approach;input.UnloadedRodTip+=approach;s.Step(input);
                if(s.Snapshot().Behaviour==FishBehaviour.NearBankSurge){near=true;break;}
            }
            Assert(near,"Near-bank counter surge absent");
        });
        Test("tired fish can be landed exactly once",()=>{
            var p=Durable();p.StaminaCapacityJoules=70;p.RecoveryPerSecond=0;p.ReelSpeed=2;p.ReelStallForce=100;
            var s=Hook(p);var input=Input();input.DragSetting=.4;input.Reel=true;input.RequestLanding=true;int landed=0;
            for(int i=0;i<24000 && !s.IsTerminal;i++){
                s.Step(input);
                foreach(var e in s.DrainEvents())if(e.Kind==FishingEventKind.Landed)landed++;
            }
            Assert(s.Snapshot().Phase==FishingPhase.Resolved,"Cannot land: "+s.Snapshot().Phase+" reason="+s.Snapshot().Failure+" distance="+s.Snapshot().FishPosition.Horizontal.Length+" stamina="+s.Snapshot().Stamina);
            Near(s.Snapshot().RodForce.Length,0,0,"Landing must release load");Run(s,input,1);Assert(landed==1 && s.DrainEvents().Length==0,"Duplicate landing");
        });
        Test("irregular bite taps contain pauses and seed-dependent shapes",()=>{
            var p=Fast();p.NibbleSeconds=2;p.NibbleMaxSeconds=2;
            var a=Cast(p,null,29);var b=Cast(p,null,91);var input=Input();
            var rows=new List<string>{"tick,baitDisplacementM,floatY"};
            double max=0,minAfterPeak=1,difference=0;bool hadPeak=false;
            while(a.Snapshot().Phase!=FishingPhase.Nibbling)a.Step(input);
            while(b.Snapshot().Phase!=FishingPhase.Nibbling)b.Step(input);
            for(int i=0;i<230;i++){
                a.Step(input);b.Step(input);var x=a.Snapshot();var y=b.Snapshot();double d=x.BaitDisplacement.Length;
                max=Math.Max(max,d);if(d>.015)hadPeak=true;if(hadPeak)minAfterPeak=Math.Min(minAfterPeak,d);
                difference+=Math.Abs(d-y.BaitDisplacement.Length);
                rows.Add(string.Format(CultureInfo.InvariantCulture,"{0},{1:R},{2:R}",x.Tick,d,x.BobberPosition.Y));
            }
            Assert(max>.015 && minAfterPeak<max*.3,"No distinct nibble/rest pattern");Assert(difference>.1,"Seeds produce identical bite shapes");
            File.WriteAllLines(Path.Combine(artifacts,"bite-motion-trace.csv"),rows);
        });
        Test("blocked bait motion cannot produce unconstrained float bite",()=>{
            var p=Fast();p.NibbleSeconds=2;p.NibbleMaxSeconds=2;
            var pool=new Pool();var blocked=Cast(p,pool);var free=Cast(p);var input=Input();
            while(blocked.Snapshot().Phase!=FishingPhase.Nibbling){blocked.Step(input);free.Step(input);}
            pool.FishBlocked=true;double blockedMotion=0,freeMotion=0;
            for(int i=0;i<220;i++){
                blocked.Step(input);free.Step(input);blockedMotion=Math.Max(blockedMotion,Math.Abs(blocked.Snapshot().BobberPosition.Y));
                freeMotion=Math.Max(freeMotion,Math.Abs(free.Snapshot().BobberPosition.Y));
            }
            Assert(freeMotion>.005 && blockedMotion<freeMotion*.2,"Float moved without causal bait movement: blocked="+blockedMotion+" free="+freeMotion);
        });
        Test("burst force rises gradually, crests, then eases",()=>{
            var p=Durable();var s=Hook(p);var input=Input();double first=0,crest=0,late=1,firstForce=0;
            for(int i=0;i<400;i++){
                s.Step(input);var x=s.Snapshot();if(i==0){first=x.BurstEnvelope;firstForce=x.FishThrust;}
                if(x.Behaviour!=FishBehaviour.Sprinting)break;
                crest=Math.Max(crest,x.BurstEnvelope);late=x.BurstEnvelope;
            }
            Assert(first<.05 && crest>.95 && late<.05,"Abrupt/flat sprint envelope");Assert(firstForce<p.BurstForce*.2,"Instant full muscle force");
        });
        Test("fish heading respects finite turn rate in open water",()=>{
            var p=Durable();var s=Hook(p);var input=Input();SimVector last=s.Snapshot().FishHeading;
            for(int i=0;i<3000;i++){
                s.Step(input);var now=s.Snapshot().FishHeading;
                double angle=Math.Acos(Math.Max(-1,Math.Min(1,SimVector.Dot(last,now))));
                Assert(angle<=p.TurnRateRadians*p.FixedStep+1e-6,"Fish snapped direction");last=now;
            }
        });
        Test("head shaking changes physical trajectory without random hook loss",()=>{
            var p=Durable();var without=Durable();without.HeadShakeFraction=0;
            var a=Hook(p);var b=Hook(without);var input=Input();double shake=0;double q=a.Snapshot().HookQuality;
            for(int i=0;i<1800;i++){
                a.Step(input);b.Step(input);shake=Math.Max(shake,a.Snapshot().HeadShake01);
                Assert(b.Snapshot().HeadShake01==0,"Disabled shaking still active");
            }
            Assert(shake>.05,"No loaded shaking");Assert((a.Snapshot().FishPosition-b.Snapshot().FishPosition).Length>.001,"Shaking cosmetic only");
            Near(a.Snapshot().HookQuality,q,1e-9,"Safe tension must not randomly wear hook");
        });
        Test("head shaking under high load weakens hook hold gradually",()=>{
            var p=Durable();p.LineStrength=25;p.PeakBreakMultiplier=10;p.FatigueSeconds=300;
            var s=Hook(p);var input=Input();input.DragSetting=1;double initial=s.Snapshot().HookQuality,last=initial;
            for(int i=0;i<1800;i++){
                s.Step(input);double quality=s.Snapshot().HookQuality;
                Assert(last-quality<.001,"Abrupt random hook damage");last=quality;
            }
            Assert(initial-last>1e-6 && !s.IsTerminal,"High-load head shakes should gradually weaken hold");
        });
        Test("drag starts are bounded and payout respects spool speed",()=>{
            var s=Hook(Durable());var input=Input();input.DragSetting=0;int transitions=0;double payout=0;
            s.DrainEvents();
            for(int i=0;i<2400;i++){
                s.Step(input);var x=s.Snapshot();payout=Math.Max(payout,x.PayoutMetersPerSecond);
                Assert(x.PayoutMetersPerSecond>=0 && x.PayoutMetersPerSecond<=8+1e-9,"Unphysical spool rate");
                foreach(var e in s.DrainEvents())if(e.Kind==FishingEventKind.DragReleased)transitions++;
            }
            Assert(payout>0 && transitions>0 && transitions<60,"Drag chatters every tick: "+transitions);
        });
        Test("long pole beyond fish still lets hooked fish escape offshore",()=>{
            var p=Durable();p.FixedLineLength=4.5;p.MaxRodReach=6;p.CruiseSwimSpeed=.5;p.BurstSwimSpeed=3;
            var input=Input();input.UnloadedRodTip=new SimVector(0,1.3,4.5);
            var s=new FishingSimulation("offshore",p,new Pool(),new FixedRandom(.5));
            s.BeginCast(input,new SimVector(0,0,3.5));
            for(int i=0;i<10000&&s.Snapshot().Phase!=FishingPhase.BiteWindow;i++)s.Step(input);
            input.Strike=true;s.Step(input);input.Strike=false;
            double start=s.Snapshot().FishPosition.Z;
            Run(s,input,1);
            Assert(!s.IsTerminal,"offshore swim ended session: "+s.Snapshot().Failure);
            Assert(s.Snapshot().FishPosition.Z>start+.3,"fish failed to swim farther away from player");
            Assert(s.Snapshot().FishHeading.Z>.8,"long pole reversed escape towards shore");
        });
        ContractTests.Run(Test);
        Test("export seeded 30-second physical trace",()=>{
            var s=Hook(Durable());var input=Input();var rows=new List<string>{"tick,phase,behaviour,fishX,fishY,fishZ,tensionN,lineM,stamina,bursts,bobberY,burstEnvelope,headShake01,payoutMps"};
            for(int i=0;i<3600;i++){
                s.Step(input);if(i%12!=0)continue;var st=s.Snapshot();
                rows.Add(string.Format(CultureInfo.InvariantCulture,"{0},{1},{2},{3:R},{4:R},{5:R},{6:R},{7:R},{8:R},{9},{10:R},{11:R},{12:R},{13:R}",st.Tick,st.Phase,st.Behaviour,st.FishPosition.X,st.FishPosition.Y,st.FishPosition.Z,st.Tension,st.LineLength,st.Stamina,st.BurstCount,st.BobberPosition.Y,st.BurstEnvelope,st.HeadShake01,st.PayoutMetersPerSecond));
            }
            File.WriteAllLines(Path.Combine(artifacts,"seed-29-trace.csv"),rows);
        });
        results.Add("RESULT "+passed+" passed, "+failed+" failed");Console.WriteLine(results[results.Count-1]);
        File.WriteAllLines(Path.Combine(artifacts,"test-results.txt"),results);return failed==0 ? 0 : 1;
    }
}
