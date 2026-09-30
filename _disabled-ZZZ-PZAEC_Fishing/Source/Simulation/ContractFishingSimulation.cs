using System;
using C = PZAEC.Fishing.Contracts;

namespace PZAEC.Fishing.Simulation
{
    /// <summary>A's public v1 boundary. Construct once per cast; internal DTOs never escape to other modules.</summary>
    public sealed class ContractFishingSimulation : C.IFishingSimulation
    {
        private FishingSimulation kernel;
        private WaterBridge water;
        private C.SessionStart start;
        private C.RodConfig rod;
        private C.HookConfig hook;
        private C.FishingSnapshot current;
        private C.EnvironmentFrame environment;
        private C.FailureReason externalFailure;
        private long sequence, inputSequence;
        private double pitch, yaw, drag, maxPlayerDistance, floatHeight, fishMass, minDepth;
        private double pendingStrikeSeconds,recentRaiseAge=1,recentRaiseSpeed;

        public C.FishingSnapshot Current { get { return current; } }

        public void Begin(C.SessionStart start, C.FishingConfig config, C.EnvironmentFrame environment,
            C.IRandomSource random, C.IWaterQuery water, C.IEventSink events)
        {
            if (kernel != null) throw new InvalidOperationException("One simulation instance per session.");
            if (events == null || random == null || water == null) throw new ArgumentNullException("Simulation dependency");
            if (start.SessionId == Guid.Empty || start.Authority == C.AuthorityMode.Observer || !start.CastTarget.IsFinite ||
                !environment.CanFish || !ValidEnvironment(environment)) throw new ArgumentException("Invalid session/environment.");
            ValidateConfig(config);
            if (!string.Equals(start.FishDefinitionId,config.Fish.Id,StringComparison.Ordinal)) throw new ArgumentException("Unknown fish definition.");
            if ((start.CastTarget-environment.PlayerPosition).Length > config.Session.MaxCastMeters) throw new ArgumentException("Cast beyond configured range.");
            this.start=start; this.environment=environment;
            rod=Copy(config.Rod); hook=Copy(config.Hook);
            maxPlayerDistance=config.Session.MaxPlayerDistanceMeters;
            floatHeight=config.Float.HeightMeters;fishMass=config.Fish.MassKg;
            minDepth=config.Session.MinDepthMeters;drag=config.Controls.InitialDrag01;
            this.water=new WaterBridge(water,minDepth);
            var p=MapParameters(config);
            kernel=new FishingSimulation(start.SessionId.ToString("D"),p,this.water,new RandomBridge(random));
            pitch=Numbers.Clamp(environment.Rod.PitchRadians,rod.MinPitchRadians,rod.MaxPitchRadians);
            yaw=Numbers.Clamp(environment.Rod.YawRadians,-rod.MaxYawRadians,rod.MaxYawRadians);
            var input=MakeInput(environment);
            kernel.BeginCast(input,ToSim(start.CastTarget),start.InitialLineLengthMeters);
            UpdateSnapshot();Flush(events);
        }

        public C.FishingSnapshot Step(float dt, C.ControlIntent intent, C.EnvironmentFrame environment, C.IEventSink events)
        {
            if (kernel == null) throw new InvalidOperationException("Begin is required.");
            if (events == null) throw new ArgumentNullException("events");
            if (current.IsTerminal) return current;
            if (!C.Scalar.IsFinite(dt) || Math.Abs(dt-C.FishingContract.FixedStepSeconds)>1e-6 || !ValidEnvironment(environment) ||
                !C.Scalar.IsFinite(intent.RodPitchRadians) || !C.Scalar.IsFinite(intent.RodYawRadians) ||
                !C.Scalar.IsFinite(intent.Reel01) || intent.Reel01<0 || intent.Reel01>1 ||
                !C.Scalar.IsFinite(intent.Drag01) || intent.Drag01<0 || intent.Drag01>1 || intent.InputSequence<inputSequence)
                return Cancel(C.FailureReason.InvalidInput,events);
            if (!environment.CanFish) return Cancel(environment.UnavailableReason == C.FailureReason.None ? C.FailureReason.InvalidWater : environment.UnavailableReason,events);
            if (intent.Cancel) return Cancel(C.FailureReason.CancelledByPlayer,events);
            if ((environment.PlayerPosition-current.FishPosition).Length>maxPlayerDistance) return Cancel(C.FailureReason.TooFar,events);
            this.environment=environment;inputSequence=intent.InputSequence;drag=intent.Drag01;water.SawUnloaded=false;
            double oldPitch=pitch;
            pitch=MoveTowards(pitch,Numbers.Clamp(intent.RodPitchRadians,rod.MinPitchRadians,rod.MaxPitchRadians),rod.AngularSpeedRadiansPerSecond*dt);
            yaw=MoveTowards(yaw,Numbers.Clamp(intent.RodYawRadians,-rod.MaxYawRadians,rod.MaxYawRadians),rod.AngularSpeedRadiansPerSecond*dt);
            var input=MakeInput(environment);
            input.Strike=intent.Strike;
            double strikeSpeed=Math.Max(0,(pitch-oldPitch)/dt);
            if(strikeSpeed>=hook.MinimumStrikeSpeedRadiansPerSecond){recentRaiseSpeed=strikeSpeed;recentRaiseAge=0;}
            else recentRaiseAge+=dt;
            // A physical mouse stroke and button edge often arrive on neighbouring render frames.
            // Accept a short measured raise around the edge, still entirely inside the bite window.
            if(current.Phase==C.FishingPhase.BiteWindow) {
                if(intent.Strike)pendingStrikeSeconds=.2;
                if(pendingStrikeSeconds>0) {
                    strikeSpeed=Math.Max(strikeSpeed,recentRaiseAge<=.12?recentRaiseSpeed:0);
                    pendingStrikeSeconds=Math.Max(0,pendingStrikeSeconds-dt);
                    input.Strike=strikeSpeed>=hook.MinimumStrikeSpeedRadiansPerSecond||pendingStrikeSeconds==0;
                    if(input.Strike)pendingStrikeSeconds=0;
                }
            } else pendingStrikeSeconds=0;
            input.StrikeStrength=strikeSpeed<hook.MinimumStrikeSpeedRadiansPerSecond ? 0 :
                Numbers.Clamp(.15+.6*(strikeSpeed-hook.MinimumStrikeSpeedRadiansPerSecond)/
                    Math.Max(.001,hook.MaxSafeStrikeSpeedRadiansPerSecond-hook.MinimumStrikeSpeedRadiansPerSecond),.15,1);
            input.Reel=intent.Reel01>0;
            input.ReelFraction=intent.Reel01;
            // Contract v1 has no separate landing button: reeling near a tired fish requests landing.
            input.RequestLanding=intent.Reel01>0;
            kernel.Step(input);UpdateSnapshot();Flush(events);return current;
        }

        public C.FishingSnapshot Cancel(C.FailureReason reason, C.IEventSink events)
        {
            if (kernel==null) throw new InvalidOperationException("Begin is required.");
            if (events==null) throw new ArgumentNullException("events");
            if (current.IsTerminal) return current;
            externalFailure=reason==C.FailureReason.None ? C.FailureReason.CancelledByPlayer : reason;
            kernel.Cancel();UpdateSnapshot();Flush(events);return current;
        }

        private SimulationInput MakeInput(C.EnvironmentFrame env)
        {
            SimVector forward=ToSim(env.Rod.Forward).Horizontal.Normalized;
            if (forward.LengthSquared<.01)forward=ToSim(env.ViewForward).Horizontal.Normalized;
            if (forward.LengthSquared<.01)throw new ArgumentException("Rod/view basis has no horizontal forward.");
            SimVector right=new SimVector(forward.Z,0,-forward.X);
            SimVector direction=(forward*Math.Cos(yaw)+right*Math.Sin(yaw))*Math.Cos(pitch)+new SimVector(0,Math.Sin(pitch),0);
            return new SimulationInput { PlayerPosition=ToSim(env.PlayerPosition),UnloadedRodTip=ToSim(env.Rod.Root)+direction*rod.LengthMeters,
                DragSetting=drag,StrikeStrength=.6,ReelFraction=1 };
        }

        private void UpdateSnapshot()
        {
            var s=kernel.Snapshot();var pose=environment.Rod;
            pose.Tip=ToContract(s.BentRodTip);pose.PitchRadians=(float)pitch;pose.YawRadians=(float)yaw;
            double surface=environment.Water.IsValid ? environment.Water.SurfacePoint.Y : start.CastTarget.Y;
            // FloatPosition is center; rest immersion follows FloatConfig.RestSubmerged01.
            C.WaterSample sampled=water.Query.SampleColumn(ToContract(s.BobberPosition),4,(float)Math.Max(16,minDepth+1));
            if(sampled.IsValid && sampled.SurfacePoint.IsFinite)surface=sampled.SurfacePoint.Y;
            current=new C.FishingSnapshot {
                SessionId=start.SessionId,Tick=s.Tick,LastInputSequence=inputSequence,TimeSeconds=s.Tick*C.FishingContract.FixedStepSeconds,
                Phase=(C.FishingPhase)(int)s.Phase,FishBehavior=MapBehaviour(s.Behaviour),Authority=start.Authority,
                FishPosition=ToContract(s.FishPosition),FishVelocity=ToContract(s.FishVelocity),FishForward=ToContract(s.FishHeading),
                FishMassKg=(float)fishMass,FishStamina01=(float)s.Stamina,FishBurstForceNewtons=(float)s.FishThrust,BurstIndex=s.BurstCount,
                LineLengthMeters=(float)s.LineLength,LineExtensionMeters=(float)s.LineExtension,LineTensionNewtons=(float)s.Tension,LineDamage01=(float)s.LineDamage,
                Rod=pose,RodLoadNewtons=ToContract(s.RodForce),FloatPosition=ToContract(s.BobberPosition),FloatUp=ToContract(s.BobberUp),
                FloatSubmerged01=(float)Numbers.Clamp(.5+(surface-s.BobberPosition.Y)/floatHeight,0,1),
                HookQuality01=(float)s.HookQuality,SlackSeconds=(float)s.SlackSeconds,Drag01=(float)drag,
                Failure=externalFailure!=C.FailureReason.None ? externalFailure : MapFailure(s.Failure)
            };
        }

        private void Flush(C.IEventSink sink)
        {
            foreach(var e in kernel.DrainEvents())
            {
                C.FishingEventKind kind;
                switch(e.Kind)
                {
                    case FishingEventKind.Cast:kind=C.FishingEventKind.Cast;break;
                    case FishingEventKind.Splash:kind=C.FishingEventKind.WaterContact;break;
                    case FishingEventKind.Nibble:kind=C.FishingEventKind.Nibble;break;
                    case FishingEventKind.Bite:kind=C.FishingEventKind.BaitTaken;break;
                    case FishingEventKind.Hooked:Emit(sink,C.FishingEventKind.Strike,e.Tick);kind=C.FishingEventKind.Hooked;break;
                    case FishingEventKind.Sprint:kind=C.FishingEventKind.Sprint;break;
                    case FishingEventKind.DragReleased:kind=C.FishingEventKind.DragSlip;break;
                    case FishingEventKind.Slack:kind=C.FishingEventKind.Slack;break;
                    case FishingEventKind.LineBroken:kind=C.FishingEventKind.LineBroken;break;
                    case FishingEventKind.HookLost:kind=C.FishingEventKind.HookLost;break;
                    case FishingEventKind.Cancelled:kind=C.FishingEventKind.Cancelled;break;
                    case FishingEventKind.Landed:
                        if(start.Authority!=C.AuthorityMode.Server && start.Authority!=C.AuthorityMode.Standalone)continue;
                        kind=C.FishingEventKind.Landed;break;
                    default:continue; // Landing has no v1 event; state already carries it.
                }
                Emit(sink,kind,e.Tick);
            }
        }
        private void Emit(C.IEventSink sink,C.FishingEventKind kind,long tick)
        { sink.Emit(new C.FishingEvent {SessionId=start.SessionId,Sequence=++sequence,Tick=tick,Kind=kind,Reason=current.Failure,
            Position=kind==C.FishingEventKind.Cast || kind==C.FishingEventKind.WaterContact ? current.FloatPosition : current.FishPosition,
            Intensity01=1}); }

        private C.FailureReason MapFailure(FailureReason reason)
        {
            switch(reason)
            {
                case FailureReason.None:return C.FailureReason.None;
                case FailureReason.UserCancelled:return C.FailureReason.CancelledByPlayer;
                case FailureReason.EarlyStrike:return C.FailureReason.EarlyStrike;
                case FailureReason.MissedBite:return C.FailureReason.LateStrike;
                case FailureReason.WeakStrike:return C.FailureReason.InvalidInput;
                case FailureReason.SlackLine:return C.FailureReason.SlackLine;
                case FailureReason.PeakOverload:case FailureReason.FatigueOverload:return C.FailureReason.Overload;
                case FailureReason.WaterUnavailable:return C.FailureReason.InvalidWater;
                case FailureReason.LineObstructed:return water.SawUnloaded ? C.FailureReason.InvalidWater : C.FailureReason.ObstructedLine;
                case FailureReason.OutOfRange:return C.FailureReason.TooFar;
                case FailureReason.AnchorDiscontinuity:return C.FailureReason.InvalidInput;
                default:return C.FailureReason.Timeout;
            }
        }
        private static C.FishBehavior MapBehaviour(FishBehaviour value)
        {
            switch(value)
            {
                case FishBehaviour.Sampling:return C.FishBehavior.Inspecting;
                case FishBehaviour.HoldingBait:return C.FishBehavior.Mouthing;
                case FishBehaviour.NearBankSurge:return C.FishBehavior.LandingSurge;
                default:return (C.FishBehavior)(int)value;
            }
        }
        private static bool ValidEnvironment(C.EnvironmentFrame env)
        { return env.PlayerPosition.IsFinite && env.Rod.Root.IsFinite && env.Rod.Forward.IsFinite && env.ViewForward.IsFinite &&
            C.Scalar.IsFinite(env.Rod.PitchRadians) && C.Scalar.IsFinite(env.Rod.YawRadians); }
        private static SimVector ToSim(C.Vec3 v) {return new SimVector(v.X,v.Y,v.Z);}
        private static C.Vec3 ToContract(SimVector v) {return new C.Vec3((float)v.X,(float)v.Y,(float)v.Z);}
        private static double MoveTowards(double a,double b,double maxDelta) {return a+Numbers.Clamp(b-a,-maxDelta,maxDelta);}
        private static T Copy<T>(T source) where T:new()
        {var copy=new T();foreach(var f in typeof(T).GetFields())f.SetValue(copy,f.GetValue(source));return copy;}

        private static void ValidateConfig(C.FishingConfig c)
        {
            if(c==null || c.Version!=C.FishingContract.Version)throw new ArgumentException("Invalid config/version.");
            foreach(var section in typeof(C.FishingConfig).GetFields())
            {
                if(section.Name=="Version")continue;
                object value=section.GetValue(c);if(value==null)throw new ArgumentException("Missing config section "+section.Name);
                foreach(var field in value.GetType().GetFields())
                    if(field.FieldType==typeof(float) && !C.Scalar.IsFinite((float)field.GetValue(value)))throw new ArgumentException("Non-finite config "+field.Name);
            }
            Numbers.Range(c.Rod.MaxDeflectionMeters,.001,c.Rod.LengthMeters-.00001,"Rod.MaxDeflectionMeters");
            Numbers.Range(c.Rod.MinPitchRadians,-1.57,0,"Rod.MinPitchRadians");
            Numbers.Range(c.Rod.MaxPitchRadians,.01,1.57,"Rod.MaxPitchRadians");
            Numbers.Range(c.Rod.MaxYawRadians,.01,Math.PI,"Rod.MaxYawRadians");
            Numbers.Range(c.Rod.AngularSpeedRadiansPerSecond,.01,10,"Rod.AngularSpeed");
            Numbers.Range(c.Rod.DampingNewtonSecondsPerMeter,0,1000,"Rod.Damping");
            Numbers.Range(c.Hook.MinimumStrikeSpeedRadiansPerSecond,.001,20,"Hook.MinimumStrikeSpeed");
            Numbers.Range(c.Hook.MaxSafeStrikeSpeedRadiansPerSecond,c.Hook.MinimumStrikeSpeedRadiansPerSecond+.001,30,"Hook.MaxSafeStrikeSpeed");
            Numbers.Range(c.Float.HeightMeters,.01,1,"Float.Height");Numbers.Range(c.Float.RestSubmerged01,0,1,"Float.RestSubmerged");
            Numbers.Range(c.Session.MinDepthMeters,.1,10,"Session.MinDepth");Numbers.Range(c.Session.MaxCastMeters,1,c.Line.MaxLengthMeters,"Session.MaxCast");
            Numbers.Range(c.Session.MaxPlayerDistanceMeters,1,200,"Session.MaxPlayerDistance");
            Numbers.Range(c.Controls.InitialDrag01,0,1,"Controls.InitialDrag");
            Numbers.Range(c.Fish.NibbleMaxSeconds,c.Fish.NibbleMinSeconds,60,"Fish.NibbleMax");
            Numbers.Range(c.Line.DamagePerSecond,.001,100,"Line.DamagePerSecond");
        }

        private static SimulationParameters MapParameters(C.FishingConfig c)
        {
            double sum=c.Line.StiffnessNewtonsPerMeter+c.Rod.StiffnessNewtonsPerMeter;
            // Effective damping for series compliances; neither damping parameter is discarded.
            double lineShare=c.Rod.StiffnessNewtonsPerMeter/sum,rodShare=c.Line.StiffnessNewtonsPerMeter/sum;
            return new SimulationParameters {
                FixedStep=C.FishingContract.FixedStepSeconds,FishMass=c.Fish.MassKg,CruiseForce=c.Fish.CruiseForceNewtons,BurstForce=c.Fish.BurstForceNewtons,
                WaterResistance=c.Fish.DragCoefficient,MaxFishSpeed=Math.Min(30,Math.Max(7,c.Fish.BurstSpeedMetersPerSecond+3)),
                CruiseSwimSpeed=c.Fish.CruiseSpeedMetersPerSecond,BurstSwimSpeed=c.Fish.BurstSpeedMetersPerSecond,
                BurstSeconds=c.Fish.BurstSeconds,RecoverySeconds=c.Fish.RecoverySeconds,StaminaCapacityJoules=c.Fish.StaminaJoules,
                RecoveryPerSecond=c.Fish.RecoveryWatts/c.Fish.StaminaJoules,NearBankSurgeDistance=c.Fish.NearShoreSurgeMeters,
                LineStiffness=c.Line.StiffnessNewtonsPerMeter,LineDamping=c.Line.DampingNewtonSecondsPerMeter*lineShare*lineShare+c.Rod.DampingNewtonSecondsPerMeter*rodShare*rodShare,
                LineStrength=c.Line.BreakForceNewtons,PeakBreakMultiplier=1,DamageStartFraction=c.Line.DamageStartFraction,FatigueSeconds=1/c.Line.DamagePerSecond,
                SlackLossSeconds=c.Hook.SlackLossSeconds,InitialHookQuality=c.Hook.InitialQuality01,
                RodStiffness=c.Rod.StiffnessNewtonsPerMeter,RodLength=c.Rod.LengthMeters,RodMaxBendRadians=Math.Asin(c.Rod.MaxDeflectionMeters/c.Rod.LengthMeters),
                MaxLineLength=c.Line.MaxLengthMeters,ReelSpeed=c.Line.ReelSpeedMetersPerSecond,ReelStallForce=c.Line.BreakForceNewtons,
                DragMinForce=c.Line.DragMinNewtons,DragMaxForce=c.Line.DragMaxNewtons,MaxPayoutSpeed=c.Line.MaxPayoutMetersPerSecond,
                WaitMinSeconds=c.Fish.BiteWaitMinSeconds,WaitMaxSeconds=c.Fish.BiteWaitMaxSeconds,NibbleSeconds=c.Fish.NibbleMinSeconds,NibbleMaxSeconds=c.Fish.NibbleMaxSeconds,
                BiteWindowSeconds=c.Hook.BiteWindowSeconds,BobberMass=c.Float.MassKg,BobberBuoyancy=c.Float.BuoyancyNewtonsPerMeter,BobberDamping=c.Float.DampingNewtonSecondsPerMeter,
                FloatRestOffset=(.5-c.Float.RestSubmerged01)*c.Float.HeightMeters,LandingRadius=c.Fish.LandingDistanceMeters,LandingStamina=c.Fish.LandingStamina01,
                MaxRodReach=c.Rod.LengthMeters+3,SessionTimeoutSeconds=c.Session.TimeoutSeconds
            };
        }
        private sealed class RandomBridge : ISimulationRandom
        {
            private readonly C.IRandomSource source;
            public RandomBridge(C.IRandomSource source){this.source=source;}
            public double NextUnit(){return source.Next01();}
        }
        private sealed class WaterBridge : IWaterDomain
        {
            public readonly C.IWaterQuery Query;
            private readonly double minimumDepth;
            public bool SawUnloaded;
            public WaterBridge(C.IWaterQuery query,double minimumDepth){Query=query;this.minimumDepth=minimumDepth;}
            public bool TrySample(SimVector position,out WaterSample sample)
            {
                var value=Query.SampleColumn(ToContract(position),4,(float)Math.Max(16,minimumDepth+1));
                SawUnloaded|=value.Status==C.WaterSampleStatus.Unloaded;
                // Unknown bottom: use the verified sampled column extent as a conservative SIMULATION boundary,
                // not as a claim that terrain exists there. Never let the fish dive beyond verified wet volume.
                double bottom=value.BottomKnown ? value.BottomY : value.SurfacePoint.Y-value.DepthMeters;
                sample=new WaterSample(value.SurfacePoint.Y,bottom);
                return value.IsValid && value.SurfacePoint.IsFinite && C.Scalar.IsFinite(value.DepthMeters) && value.DepthMeters>=minimumDepth && sample.IsValid;
            }
            public bool IsFishPathClear(SimVector from,SimVector to){return Clear(from,to);}
            public bool IsLineClear(SimVector from,SimVector to){return Clear(from,to);}
            private bool Clear(SimVector from,SimVector to)
            {var hit=Query.TraceSolid(ToContract(from),ToContract(to));SawUnloaded|=hit.Unloaded;return !hit.Obstructed && !hit.Unloaded;}
        }
    }
}
