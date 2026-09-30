using System;
using System.Collections.Generic;
using PZAEC.Fishing.Contracts;
using PZAEC.Fishing.Content;
using PZAEC.Fishing.Controls;
using PZAEC.Fishing.Runtime;
using ContractFishingSimulation = PZAEC.Fishing.Simulation.ContractFishingSimulation;

// Real A/B/C/E implementations. Only the game world and Unity presentation are replaced.
internal static class ModulesHarness
{
    static int checks;
    static void Check(bool ok,string name) { if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS "+name); }
    sealed class World : IWaterQuery
    {
        public bool Dry;
        public WaterSample SampleColumn(Vec3 p,float above,float below) => new WaterSample {
            Status=Dry?WaterSampleStatus.Dry:WaterSampleStatus.Valid,SurfacePoint=new Vec3(p.X,0,p.Z),
            Normal=Vec3.Up,DepthMeters=4,BottomY=-4,BottomKnown=true};
        public SegmentHit TraceSolid(Vec3 a,Vec3 b) => default(SegmentHit);
    }
    sealed class View : IFishingPresentation
    {
        public readonly List<FishingEvent> Events=new List<FishingEvent>();
        public bool Cleared;
        public void Begin(SessionStart s,FishingConfig c) { Cleared=false; }
        public void Render(RenderFrame frame) { }
        public void OnEvent(FishingEvent e) { Events.Add(e); }
        public void Clear() { Cleared=true; }
        public void Dispose() { }
    }
    sealed class Run : IDisposable
    {
        public readonly World World=new World();
        public readonly View View=new View();
        public readonly SessionDriver Host;
        public EnvironmentFrame Env;
        public float Stamina=100,TotalSpent;
        long sequence;
        public Run(FishingConfig config,bool dry=false,uint seed=29,bool playerStamina=false)
        {
            World.Dry=dry;
            Env=new EnvironmentFrame {CanFish=true,IsGrounded=true,PlayerEntityId=1,
                ViewForward=new Vec3(0,0,1),ViewRight=new Vec3(1,0,0),
                Rod=new RodPose {Root=new Vec3(0,1.3f,0),Forward=new Vec3(0,0,1),Right=new Vec3(1,0,0),PitchRadians=.35f},
                Water=World.SampleColumn(new Vec3(0,0,8),4,16)};
            Env.HasPlayerStamina=playerStamina;Env.PlayerStamina01=1;Env.PlayerStaminaMaximum=100;
            Host=new SessionDriver(new ContractFishingSimulation(),new FishingControlsAdapter(),View);
            Host.Begin(new SessionStart {SessionId=Guid.NewGuid(),PlayerPersistentId="integration",PlayerEntityId=1,
                Seed=seed,FishDefinitionId=config.Fish.Id,CastTarget=new Vec3(0,0,config.Line.FixedLengthMeters>0?5.5f:8),Authority=AuthorityMode.Standalone},config,Env,World);
        }
        public void Step(float dt=1f/60,float back=0,bool strike=false,bool allowed=true,bool reel=false)
        {
            Host.Advance(dt,new RawInputFrame {Sequence=++sequence,DurationSeconds=dt,InputAllowed=allowed,MouseBackDelta=back,StrikePressed=strike,ReelHeld=reel},Env);
            if(Env.HasPlayerStamina) {
                TotalSpent+=Host.FrameStaminaCost;
                // Deterministic resource fixture; native recovery varies with player buffs/food.
                Stamina=Math.Max(0,Math.Min(100,Stamina+8*dt)-Host.FrameStaminaCost);
                Env.PlayerStamina01=Stamina/100;
            }
        }
        public void Dispose() { Host.Dispose(); }
    }
    static void WaitBite(Run r)
    {
        for(int i=0;i<10000 && r.Host.Active && r.Host.Current.Phase!=FishingPhase.BiteWindow;i++)r.Step();
        Check(r.Host.Current.Phase==FishingPhase.BiteWindow,"real modules reach bite window with shipped XML");
    }
    public static int Main(string[] args)
    {
        try {
            var content=new FishingContent();var config=content.Load(args[0]);
            string error;Check(content.Validate(config,out error),"shipped configuration validates: "+error);
            Check(config.Hook.NaturalBites,"shipped gameplay enables natural mouth contact and false bites");
            foreach(var species in FishCatalog.Ids)foreach(var size in new[]{.5f,1.75f}) {
                var variant=content.Load(args[0]);variant.Fish=FishCatalog.Size(FishCatalog.Profile(variant.Fish,species),size);
                variant.Hook.NaturalBites=false;
                using(var run=new Run(variant)){
                    WaitBite(run);run.Step(strike:true);
                    Check(run.Host.Current.Phase==FishingPhase.Hooked,"species and size can hook: "+species+" / "+size);
                    for(int tick=0;tick<1200&&run.Host.Active;tick++)run.Step(back:.03f);
                    Check(Scalar.IsFinite(run.Host.Current.LineTensionNewtons)&&run.Host.Current.FishPosition.IsFinite&&Math.Abs(run.Host.Current.FishMassKg-variant.Fish.MassKg)<.00001f,"species physics remains finite and preserves weight");
                }
            }
            int naturalHooked=0,naturalEmpty=0;
            for(uint seed=1;seed<=32;seed++)using(var run=new Run(config,seed:seed)) {
                for(int i=0;i<5000&&run.Host.Active&&run.Host.Current.Phase!=FishingPhase.BiteWindow;i++)run.Step();
                Check(run.Host.Current.Phase==FishingPhase.BiteWindow,"production natural contact reaches physical float motion, seed "+seed);
                for(int i=0;i<18;i++)run.Step();
                run.Step(strike:true);
                if(run.Host.Current.Phase==FishingPhase.Hooked)naturalHooked++;
                else {Check(run.Host.Current.Failure==FailureReason.EarlyStrike,"empty natural strike resolves without granting a catch");naturalEmpty++;}
            }
            Console.WriteLine("PRODUCTION NATURAL CONTACTS hooked="+naturalHooked+" empty="+naturalEmpty);
            Check(naturalHooked>0&&naturalEmpty>0,"production XML and actual controls produce both hookups and empty strikes");
            // Obtain a deterministic hooked fish for fight/settlement regressions. Natural bites are tested separately.
            config.Hook.NaturalBites=false;
            Check(Math.Abs(FloatReadout.Marks(config.Float.RestSubmerged01)-4)<.1f,"shipped float rests near four visible antenna marks");
            var floatReadout=new FloatReadout();var floatState=new FishingSnapshot {SessionId=Guid.NewGuid(),Tick=1,TimeSeconds=1,
                Phase=FishingPhase.Waiting,FloatSubmerged01=config.Float.RestSubmerged01,FloatUp=Vec3.Up};
            floatReadout.Observe(floatState,config.Float);
            Check(floatReadout.Current.Signal==FloatSignal.Waiting&&!floatReadout.Current.CanStrike,"resting float does not claim a bite");
            floatState.Tick++;floatState.TimeSeconds+=1f/60;floatState.Phase=FishingPhase.Nibbling;floatState.FloatSubmerged01+=.04f;
            floatReadout.Observe(floatState,config.Float);
            Check(floatReadout.Current.Signal==FloatSignal.Downstroke&&!floatReadout.Current.CanStrike,"physical quick sink reads as downstroke without exposing strike eligibility");
            var beforeRepaint=floatReadout.Current;floatState.FloatSubmerged01=1;floatReadout.Observe(floatState,config.Float);
            Check(floatReadout.Current.VisibleMarks==beforeRepaint.VisibleMarks,"duplicate GUI/frame observation does not advance readout");
            floatState.Tick++;floatState.TimeSeconds+=1f/60;floatReadout.Observe(floatState,config.Float);
            Check(floatReadout.Current.Signal==FloatSignal.Submerged&&floatReadout.Current.VisibleMarks==0,"fully submerged tail reads black float with zero visible marks");
            floatState.Tick++;floatState.TimeSeconds+=1f/60;floatState.FloatSubmerged01=.55f;floatReadout.Observe(floatState,config.Float);
            Check(floatReadout.Current.Signal==FloatSignal.Rising&&floatReadout.Current.VisibleMarks>5,"physical rise reads as raised float with additional marks");
            floatReadout.Reset();Check(!floatReadout.Current.CanStrike,"world/session cleanup clears readout and strike hint");
            FloatSignal? physicalSignal=null;
            foreach(var phase in new[]{FishingPhase.Waiting,FishingPhase.Nibbling,FishingPhase.BiteWindow}) {
                var readout=new FloatReadout();var state=new FishingSnapshot {SessionId=Guid.NewGuid(),Tick=1,TimeSeconds=1,Phase=phase,FloatUp=Vec3.Up,FloatSubmerged01=config.Float.RestSubmerged01};
                readout.Observe(state,config.Float);state.Tick++;state.TimeSeconds+=1f/60;state.FloatSubmerged01+=.04f;readout.Observe(state,config.Float);
                Check(!readout.Current.CanStrike&&(!physicalSignal.HasValue||readout.Current.Signal==physicalSignal.Value),"same physical float motion conceals hidden bite phase "+phase);
                physicalSignal=readout.Current.Signal;
            }
            bool sawBlack=false,sawRise=false,sawDown=false;
            for(uint seed=1;seed<=32;seed++)using(var r=new Run(config,seed:seed)) {
                var actual=new FloatReadout();
                for(int i=0;i<1600&&r.Host.Active;i++) {
                    r.Step();actual.Observe(r.Host.Current,config.Float);
                    sawBlack|=actual.Current.Signal==FloatSignal.Submerged;sawRise|=actual.Current.Signal==FloatSignal.Rising;sawDown|=actual.Current.Signal==FloatSignal.Downstroke;
                }
            }
            Check(sawBlack&&sawRise&&sawDown,"shipped simulation produces black float, raised float and downstrokes across seeded bites");
            using(var r=new Run(config)) {
                WaitBite(r);
                r.Step(back:1,strike:true);
                Check(r.Host.Current.Phase==FishingPhase.Hooked,"mouse raise plus strike hooks through A/C/B");
                r.Step();
                Check(r.Host.Current.Phase==FishingPhase.Fighting && r.Host.Current.BurstIndex>0,"hook transitions into fish sprint");
                Check(r.Host.Current.Rod.Tip.IsFinite && r.Host.Current.LineTensionNewtons>=0,"physical snapshot remains finite");
                Check(r.View.Events.FindAll(e=>e.Kind==FishingEventKind.Hooked).Count==1,"hook event reaches presentation exactly once");
                for(int i=1;i<r.View.Events.Count;i++)
                    if(r.View.Events[i].Sequence<=r.View.Events[i-1].Sequence)throw new Exception("event order");
                Check(true,"events stay ordered across modules");
                r.Step(allowed:false);
                Check(!r.Host.Active && r.Host.Current.Failure==FailureReason.MenuOpened && !r.Host.Movement.Active && r.View.Cleared,"menu releases fighting session immediately");
            }
            using(var r=new Run(config)) {
                WaitBite(r);r.Step(strike:true);
                Check(r.Host.Current.Phase==FishingPhase.Hooked,"pole left click hooks without simultaneous mouse raise");
                for(int i=0;i<18&&r.Host.Active;i++)r.Step();
                float pitch=r.Host.Current.Rod.PitchRadians;
                r.Step(back:.3f,reel:true);
                Check(r.Host.Active&&r.Host.Current.Rod.PitchRadians>pitch,"mouse pull continues to raise pole after click animation");
                Check(Math.Abs(r.Host.Current.LineLengthMeters-config.Line.FixedLengthMeters)<.0001f,"reel input cannot shorten pole line");
            }
            using(var r=new Run(config)) {
                for(int i=0;i<1000&&r.Host.Current.Phase!=FishingPhase.Waiting;i++)r.Step();
                r.Step(strike:true);
                Check(r.Host.Active&&r.Host.Current.Phase==FishingPhase.Waiting,"premature pole click keeps session waiting without hooking");
                for(int i=0;i<1000&&r.Host.Current.Phase!=FishingPhase.Nibbling;i++)r.Step();
                r.Step(strike:true);
                Check(r.Host.Active&&r.Host.Current.Phase==FishingPhase.Hooked,"pole strike during visible nibble starts fish fight");
                var before=r.Host.Current.FishPosition;
                for(int i=0;i<30&&r.Host.Active;i++)r.Step();
                Check(r.Host.Active&&(r.Host.Current.FishPosition-before).Length>.05f&&r.Host.Current.FishVelocity.Length>.01f,"fish swims after nibble strike rather than ending session");
            }
            using(var r=new Run(config)) {
                WaitBite(r);
                for(int i=0;i<100&&r.Host.Active;i++)r.Step();
                Check(!r.Host.Active&&r.Host.Current.Failure==FailureReason.LateStrike,"pole still requires clicking within bite window");
            }
            using(var r=new Run(config,true))
                Check(r.Host.Current.IsTerminal && !r.Host.Active && r.View.Cleared && !r.Host.Movement.Active,"rejected cast releases host during Begin");
            using(var r=new Run(config)) {
                r.Env.CanFish=false;r.Env.UnavailableReason=FailureReason.ItemChanged;r.Step();
                Check(!r.Host.Active && r.Host.Current.Failure==FailureReason.ItemChanged && r.View.Cleared,"equipment change cancels actual simulation");
            }
            using(var slow=new Run(config))using(var fast=new Run(config)) {
                for(int i=0;i<60;i++)slow.Step(1f/30,.2f);
                for(int i=0;i<240;i++)fast.Step(1f/120,.05f);
                Check(slow.Host.Current.Tick==120 && fast.Host.Current.Tick==120,"30 and 120 FPS produce equal physics tick counts");
                Check(Math.Abs(slow.Host.Current.Rod.PitchRadians-fast.Host.Current.Rod.PitchRadians)<.0001f &&
                    (slow.Host.Current.FloatPosition-fast.Host.Current.FloatPosition).Length<.0001f &&
                    slow.Host.Current.Phase==fast.Host.Current.Phase,"equivalent mouse input produces equal cross-module state");
            }
            var oldStrength=content.Load(args[0]);
            oldStrength.Fish.CruiseForceNewtons=9;oldStrength.Fish.BurstForceNewtons=50;
            oldStrength.Fish.StaminaJoules=450;oldStrength.Fish.BurstSeconds=2;oldStrength.Fish.RecoverySeconds=4;
            oldStrength.Fish.CruiseSpeedMetersPerSecond=.5f;oldStrength.Fish.BurstSpeedMetersPerSecond=3;
            oldStrength.Controls.PlayerResistanceNewtons=110;oldStrength.Controls.LoadedResponseFloor01=.25f;
            oldStrength.Line.BreakForceNewtons=90;
            oldStrength.Controls.EffortEnabled=false;
            oldStrength.Hook.NaturalBites=false;
            // Compare fish strength under the same held pose; effort yielding is tested separately.
            var heldStrength=content.Load(args[0]);heldStrength.Controls.EffortEnabled=false;heldStrength.Hook.NaturalBites=false;
            foreach(uint seed in new uint[]{29,7,91})using(var oldFish=new Run(oldStrength,seed:seed))using(var strongFish=new Run(heldStrength,seed:seed)) {
                WaitBite(oldFish);WaitBite(strongFish);oldFish.Step(back:1,strike:true);strongFish.Step(back:1,strike:true);
                float oldPeak=0,newPeak=0,oldTotal=0,newTotal=0;
                for(int i=0;i<900;i++) {
                    oldFish.Step();strongFish.Step();
                    oldPeak=Math.Max(oldPeak,oldFish.Host.Current.LineTensionNewtons);newPeak=Math.Max(newPeak,strongFish.Host.Current.LineTensionNewtons);
                    oldTotal+=oldFish.Host.Current.LineTensionNewtons;newTotal+=strongFish.Host.Current.LineTensionNewtons;
                }
                Console.WriteLine("FORCE COMPARISON seed="+seed+" oldPeak="+oldPeak+" newPeak="+newPeak+" oldMean="+oldTotal/900+" newMean="+newTotal/900);
                Check(strongFish.Host.Active&&newPeak>oldPeak*1.3f&&newTotal>oldTotal*1.3f,"stronger fish raises real peak and sustained line force without unavoidable early break, seed "+seed);
            }
            // Shipped endurance checks retain the full fish strength and stamina.
            foreach(uint seed in new uint[]{29,7,91})using(var defaults=new Run(config,seed:seed)) {
                WaitBite(defaults);defaults.Step(back:1,strike:true);
                float minimumStamina=1,peakForce=0;
                for(int i=0;i<18100&&defaults.Host.Active;i++) {
                    defaults.Step(back:defaults.Host.Current.FishStamina01<.25f||defaults.Host.Current.LineTensionNewtons<5||defaults.Host.Current.Rod.PitchRadians<.35f?.5f:0,reel:false);
                    if(Math.Abs(defaults.Host.Current.LineLengthMeters-config.Line.FixedLengthMeters)>.0001f)throw new Exception("Pole line reeled or paid out");
                    minimumStamina=Math.Min(minimumStamina,defaults.Host.Current.FishStamina01);
                    peakForce=Math.Max(peakForce,defaults.Host.Current.LineTensionNewtons);
                }
                Console.WriteLine("DEFAULT LANDING seed="+seed+" phase="+defaults.Host.Current.Phase+" failure="+defaults.Host.Current.Failure+" seconds="+defaults.Host.Current.TimeSeconds+" minimumStamina="+minimumStamina+" line="+defaults.Host.Current.LineLengthMeters+" peakForce="+peakForce);
                Check(defaults.Host.Current.Phase==FishingPhase.Resolved,"shipped endurance settings permit landing before timeout, seed "+seed);
            }
            // Assisted endurance profile shortens the settlement test, not the balance checks above.
            foreach(uint seed in new uint[]{29,7,91})using(var effortRun=new Run(config,seed:seed,playerStamina:true)) {
                WaitBite(effortRun);effortRun.Step(back:1,strike:true);
                float minimum=100;int rests=0;bool resting=false;
                for(int i=0;i<17900&&effortRun.Host.Active;i++) {
                    if(effortRun.Stamina<30&&!resting){resting=true;rests++;}
                    if(effortRun.Stamina>60)resting=false;
                    var state=effortRun.Host.Current;
                    float back=resting&&state.LineTensionNewtons>12&&state.Rod.PitchRadians>.25f?-.2f:
                        state.LineTensionNewtons<8||state.Rod.PitchRadians<.35f||state.FishStamina01<.25f?.5f:0;
                    effortRun.Step(back:back);minimum=Math.Min(minimum,effortRun.Stamina);
                }
                Console.WriteLine("PLAYER EFFORT seed="+seed+" phase="+effortRun.Host.Current.Phase+" failure="+effortRun.Host.Current.Failure+" minimumStamina="+minimum+" spent="+effortRun.TotalSpent+" rests="+rests);
                Check(effortRun.Host.Current.Phase==FishingPhase.Resolved&&effortRun.TotalSpent>10,"effort profile spends player resource and still allows landing, seed "+seed);
                effortRun.Step(dt:0);Check(effortRun.Host.FrameStaminaCost==0,"no physics tick cannot replay old stamina cost");
            }
            var landingConfig=content.Load(args[0]);landingConfig.Hook.NaturalBites=false;landingConfig.Fish.StaminaJoules=70;landingConfig.Fish.RecoveryWatts=0;
            using(var r=new Run(landingConfig)) {
                WaitBite(r);r.Step(back:1,strike:true);
                for(int i=0;i<17000&&r.Host.Active;i++)r.Step(back:r.Host.Current.FishStamina01<.25f?.5f:0,reel:false);
                Check(r.Host.Current.Phase==FishingPhase.Resolved,"real controls and simulation complete assisted landing: "+r.Host.Current.Failure);
                Check(r.View.Events.FindAll(e=>e.Kind==FishingEventKind.Landed).Count==1&&!r.Host.Active,"successful landing emits once and releases host");
                RewardSpec reward;
                Check(content.TryGetReward(new CatchResult {SessionId=r.Host.Current.SessionId,SettlementId=r.Host.Current.SessionId,
                    PlayerPersistentId="integration",FishDefinitionId=landingConfig.Fish.Id,MassKg=r.Host.Current.FishMassKg,TerminalTick=r.Host.Current.Tick},out reward)&&reward.Count==FishCatalog.MeatCount(r.Host.Current.FishMassKg),
                    "actual landing maps to one content reward");
            }
            Console.WriteLine("RESULT PASS moduleChecks="+checks);return 0;
        } catch(Exception e) {Console.WriteLine("RESULT FAIL "+e);return 1;}
    }
}
