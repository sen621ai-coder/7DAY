using System;
using System.Collections.Generic;

namespace PZAEC.Fishing.Simulation
{
    /// <summary>One session, fixed-tick SI simulation. No Unity, input, inventory or network dependencies.</summary>
    public sealed class FishingSimulation
    {
        private readonly SimulationParameters p;
        private readonly IWaterDomain water;
        private readonly ISimulationRandom random;
        private readonly string sessionId;
        private readonly Queue<FishingEvent> events = new Queue<FishingEvent>();
        private FishingPhase phase;
        private FishBehaviour behaviour;
        private BitePattern pattern;
        private FailureReason failure;
        private SimVector fish, velocity, heading, bobber, bobberVelocity, bobberUp;
        private SimVector previousTip, previousPlayer, tip, castTarget, castOrigin, rodForce, bentTip;
        private double timeInPhase, behaviourTime, waitDuration, nibbleDuration, length, tension, extension, rodDeflection;
        private double stamina = 1, hookQuality, damage, slackTime, thrust, sprintDuration;
        private bool slipping, slackEvent, nearBankBurst;
        private long tick, eventSequence;
        private int bursts;
        private BiteMotion biteMotion;
        private SimVector baitOrigin, baitDisplacement;
        private double recoveryDuration, escapeSide, burstStrength, burstEnvelope, headShake, payoutSpeed, muscleForce, dragReleaseTime;

        public FishingSimulation(string sessionId, SimulationParameters parameters, IWaterDomain water, uint seed)
            : this(sessionId, parameters, water, new SeededSimulationRandom(seed)) { }

        public FishingSimulation(string sessionId, SimulationParameters parameters, IWaterDomain water, ISimulationRandom random)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("A unique session ID is required.", "sessionId");
            if (parameters == null || water == null || random == null) throw new ArgumentNullException("Simulation dependency");
            parameters.Validate(); this.p = parameters.Copy(); this.water = water; this.random = random; this.sessionId = sessionId;
            bobberUp = new SimVector(0, 1, 0); heading = new SimVector(0, 0, 1);
        }

        public double FixedStep { get { return p.FixedStep; } }
        public bool IsTerminal { get { return phase == FishingPhase.Resolved || phase == FishingPhase.Cancelled || phase == FishingPhase.LineBroken || phase == FishingPhase.HookLost; } }

        public void BeginCast(SimulationInput input, SimVector target, double initialLineLength = 0)
        {
            if (phase != FishingPhase.Idle) throw new InvalidOperationException("Create a new session for a new cast.");
            input.Validate(); if (!target.IsFinite) throw new ArgumentException("Invalid cast target.");
            Numbers.Range(initialLineLength,0,p.MaxLineLength,"initialLineLength");
            tip = previousTip = input.UnloadedRodTip; previousPlayer = input.PlayerPosition;
            bentTip = tip; castOrigin = tip;
            if ((tip-input.PlayerPosition).Length > p.MaxRodReach) { Finish(FailureReason.OutOfRange); return; }
            WaterSample sample;
            if (!Sample(target, out sample)) { Finish(FailureReason.WaterUnavailable); return; }
            castTarget = new SimVector(target.X, sample.SurfaceY, target.Z);
            fish = new SimVector(target.X, Math.Max(sample.BottomY+p.WaterClearance, sample.SurfaceY-p.LeaderDepth), target.Z);
            if (!water.IsFishPathClear(castTarget,fish)) { Finish(FailureReason.WaterUnavailable); return; }
            length = initialLineLength > 0 ? initialLineLength : (fish-tip).Length + .08;
            if(p.FixedLineLength>0)length=p.FixedLineLength;
            if(p.FixedLineLength>0 && (fish-tip).Length>length+.08)
            { Finish(FailureReason.OutOfRange); return; }
            if (length > p.MaxLineLength || length < p.MinLineLength || !water.IsLineClear(tip, castTarget))
            { Finish(length > p.MaxLineLength || length < p.MinLineLength ? FailureReason.OutOfRange : FailureReason.LineObstructed); return; }
            bobber = castOrigin;
            waitDuration = p.WaitMinSeconds + Next()*(p.WaitMaxSeconds-p.WaitMinSeconds);
            pattern = (BitePattern)Math.Min(3, (int)(Next()*4));
            nibbleDuration = p.NibbleSeconds+Next()*(p.NibbleMaxSeconds-p.NibbleSeconds);
            biteMotion=new BiteMotion(Next,nibbleDuration);baitOrigin=fish;
            SetPhase(FishingPhase.Casting); Emit(FishingEventKind.Cast);
        }

        // Caller provides exactly one sample per FixedStep. Rendering dt never enters this solver.
        // Rod tip must be interpolated by A at the same fixed tick, not teleported once per rendered frame.
        public void Step(SimulationInput input)
        {
            input.Validate();
            if (phase == FishingPhase.Idle || IsTerminal) return;
            tick++; timeInPhase += p.FixedStep;
            if (input.Cancel) { Finish(FailureReason.UserCancelled); return; }
            if (tick*p.FixedStep > p.SessionTimeoutSeconds) { Finish(FailureReason.SessionTimeout); return; }
            tip = input.UnloadedRodTip;
            if (phase != FishingPhase.Hooked && phase != FishingPhase.Fighting && phase != FishingPhase.Landing) bentTip=tip;
            SimVector tipVelocity = (tip-previousTip)/p.FixedStep;
            if (tipVelocity.Length > p.MaxAnchorSpeed || (input.PlayerPosition-previousPlayer).Length/p.FixedStep > p.MaxAnchorSpeed)
            { Finish(FailureReason.AnchorDiscontinuity); return; }
            previousTip = tip; previousPlayer = input.PlayerPosition;
            if ((tip-input.PlayerPosition).Length > p.MaxRodReach || (fish-tip).Length > p.MaxLineLength+5)
            { Finish(FailureReason.OutOfRange); return; }
            WaterSample sample;
            if (!Sample(fish, out sample)) { Finish(FailureReason.WaterUnavailable); return; }
            if (!water.IsLineClear(tip, phase == FishingPhase.Casting ? castTarget : bobber) ||
                (phase != FishingPhase.Casting && !water.IsLineClear(bobber, fish)))
            { Finish(FailureReason.LineObstructed); return; }

            if (phase == FishingPhase.Casting)
            {
                double a = Numbers.Clamp(timeInPhase/p.CastSeconds, 0, 1);
                bobber = SimVector.Lerp(castOrigin, castTarget, a) + new SimVector(0, Math.Sin(a*Math.PI)*.8, 0);
                if (a >= 1) { SetPhase(FishingPhase.Settling); Emit(FishingEventKind.Splash); }
                return;
            }
            if (phase == FishingPhase.Hooked) { SetPhase(FishingPhase.Fighting); StartBurst(false); }
            if (phase == FishingPhase.Fighting || phase == FishingPhase.Landing)
            {
                Fight(input, tipVelocity, sample);
                if (!IsTerminal) UpdateBobber(sample, true);
                return;
            }

            // For a hand pole, visible bait inspection is already a valid strike.
            // Empty-water clicks do not destroy the cast or consume another bait.
            if (input.Strike && (p.FixedLineLength<=0 || phase==FishingPhase.Nibbling || phase==FishingPhase.BiteWindow))
            {
                bool poleNibble=p.FixedLineLength>0 && phase==FishingPhase.Nibbling;
                if (phase != FishingPhase.BiteWindow && !poleNibble) { Finish(FailureReason.EarlyStrike); return; }
                if (!poleNibble && timeInPhase > p.BiteWindowSeconds) { Finish(FailureReason.MissedBite); return; }
                if (input.StrikeStrength < .15) { Finish(FailureReason.WeakStrike); return; }
                double timing = poleNibble?.65:1-Math.Abs(2*timeInPhase/p.BiteWindowSeconds-1);
                double strength = 1-Math.Abs(input.StrikeStrength-.6);
                hookQuality = Numbers.Clamp((.2 + .45*timing + .35*strength)*p.InitialHookQuality, .01, 1);
                SetPhase(FishingPhase.Hooked); behaviour = FishBehaviour.Startled; Emit(FishingEventKind.Hooked);
                return;
            }
            if (phase == FishingPhase.Settling && timeInPhase >= p.SettleSeconds) SetPhase(FishingPhase.Waiting);
            else if (phase == FishingPhase.Waiting && timeInPhase >= waitDuration)
            { SetPhase(FishingPhase.Nibbling); behaviour = FishBehaviour.Sampling; Emit(FishingEventKind.Nibble); }
            else if (phase == FishingPhase.Nibbling && timeInPhase >= nibbleDuration)
            { SetPhase(FishingPhase.BiteWindow); behaviour = FishBehaviour.HoldingBait; Emit(FishingEventKind.Bite); }
            else if (phase == FishingPhase.BiteWindow && timeInPhase >= p.BiteWindowSeconds)
            { Finish(FailureReason.MissedBite); return; }
            UpdateBobber(sample, false);
        }

        private void Fight(SimulationInput input, SimVector tipVelocity, WaterSample sample)
        {
            double h = p.FixedStep;
            behaviourTime += h;
            double distanceToPlayer = (fish-input.PlayerPosition).Horizontal.Length;
            if (!nearBankBurst && distanceToPlayer < p.NearBankSurgeDistance && stamina > .18)
            { nearBankBurst = true; StartBurst(true); }
            else if ((behaviour == FishBehaviour.Sprinting || behaviour == FishBehaviour.NearBankSurge) && behaviourTime >= sprintDuration)
            { behaviour = FishBehaviour.Recovering; behaviourTime = 0; }
            else if ((behaviour == FishBehaviour.Recovering || behaviour == FishBehaviour.SideRun || behaviour == FishBehaviour.Exhausted) && behaviourTime >= recoveryDuration)
            {
                if (stamina > .16) StartBurst(false);
                else { behaviour = FishBehaviour.Exhausted; behaviourTime = 0; }
            }
            if (behaviour == FishBehaviour.Recovering && behaviourTime >= recoveryDuration*.5 && stamina > .3)
                behaviour = FishBehaviour.SideRun;

            bool burst = behaviour == FishBehaviour.Sprinting || behaviour == FishBehaviour.NearBankSurge;
            // A long pole tip can be farther offshore than the fish. Escaping from that tip
            // incorrectly makes the fish swim towards the angler; escape from the angler instead.
            SimVector away = (fish-input.PlayerPosition).Horizontal.Normalized;
            if (away.LengthSquared < .01) away = new SimVector(0, 0, 1);
            SimVector side = new SimVector(-away.Z, 0, away.X);
            SimVector desired = (away*(burst?1:.25) + side*(escapeSide*(burst ? .65 : 2))).Normalized;
            // A fish turns through an arc; it cannot instantly reverse at the start of each run.
            double angle=Math.Atan2(heading.X,heading.Z), targetAngle=Math.Atan2(desired.X,desired.Z);
            double turn=Math.Atan2(Math.Sin(targetAngle-angle),Math.Cos(targetAngle-angle));
            angle+=Numbers.Clamp(turn,-p.TurnRateRadians*h,p.TurnRateRadians*h);
            heading=new SimVector(Math.Sin(angle),0,Math.Cos(angle));
            double energyScale = .15+.85*stamina;
            burstEnvelope=burst ? BiteMotion.Smooth(behaviourTime/Math.Min(p.BurstRiseSeconds,sprintDuration*.25))*
                BiteMotion.Smooth((sprintDuration-behaviourTime)/Math.Min(.35,sprintDuration*.3)) : 0;
            double targetForce=(p.CruiseForce+(p.BurstForce-p.CruiseForce)*burstEnvelope*burstStrength)*energyScale;
            muscleForce+=(targetForce-muscleForce)*(1-Math.Exp(-h/.08));
            thrust=muscleForce;
            // Swimming target speed limits propulsion, not externally imposed line/rod velocity.
            double swimSpeed = burst ? p.BurstSwimSpeed : p.CruiseSwimSpeed;
            thrust *= Numbers.Clamp(1-Math.Max(0,SimVector.Dot(velocity,heading))/swimSpeed,0,1);
            double targetY = Numbers.Clamp(sample.SurfaceY-p.LeaderDepth, sample.BottomY+p.WaterClearance, sample.SurfaceY-p.WaterClearance);
            // Short head shakes are lateral physical effort, strongest while resisting a taut line.
            double shakeWave=Math.Sin(behaviourTime*2*Math.PI*3.2);
            double shakeGate=burstEnvelope*Numbers.Clamp(tension/Math.Max(1,p.BurstForce),0,1);
            headShake=Math.Abs(shakeWave)*shakeGate*Numbers.Clamp(p.HeadShakeFraction/.13,0,1);
            SimVector shakeForce=new SimVector(-heading.Z,0,heading.X)*(thrust*p.HeadShakeFraction*shakeWave*shakeGate);
            SimVector swim = heading*thrust+shakeForce + new SimVector(0, (targetY-fish.Y)*p.FishMass*2, 0);
            // Semi-implicit water drag; spring uses an implicit radial denominator below.
            SimVector freeVelocity = (velocity+swim*(h/p.FishMass))/(1+h*p.WaterResistance/p.FishMass);
            SimVector radial = fish-tip;
            double distance = radial.Length;
            SimVector axis = radial.Normalized;
            double separationSpeed = SimVector.Dot(freeVelocity-tipVelocity, axis);
            if (p.FixedLineLength<=0 && input.Reel && !slipping)
                length = Math.Max(p.MinLineLength, length-p.ReelSpeed*(input.ReelFraction>0 ? input.ReelFraction : 1)*h*Numbers.Clamp(1-tension/p.ReelStallForce,0,1));

            double k = p.LineStiffness*p.RodStiffness/(p.LineStiffness+p.RodStiffness);
            double maxDeflection = p.RodLength*Math.Sin(p.RodMaxBendRadians);
            double predictedExtension = Math.Max(0,distance+h*separationSpeed-length);
            // After the rod reaches its bend limit, only the line remains compliant.
            double rodLimitForce = p.RodStiffness*maxDeflection;
            double rodLimitExtension = maxDeflection+rodLimitForce/p.LineStiffness;
            bool rodAtLimit = predictedExtension > rodLimitExtension;
            double activeK = rodAtLimit ? p.LineStiffness : k;
            double elastic = rodAtLimit ? p.LineStiffness*(predictedExtension-maxDeflection) : k*predictedExtension;
            double denominator = 1+h*p.LineDamping/p.FishMass+h*h*activeK/p.FishMass;
            double raw = predictedExtension > 0 ? Math.Max(0,elastic+p.LineDamping*separationSpeed)/denominator : 0;
            double dragLimit = p.DragMinForce+input.DragSetting*(p.DragMaxForce-p.DragMinForce);
            dragReleaseTime=raw<dragLimit*p.DragStopRatio ? dragReleaseTime+h : 0;
            // The drag clutch stays released through sub-stroke troughs; stop only after a sustained
            // load reduction. This prevents repeated slip sounds and reel/drag fighting at tick rate.
            bool dragEngaged=p.FixedLineLength<=0 && (raw>dragLimit*p.DragStartRatio || (slipping && dragReleaseTime<.15));
            double payout = dragEngaged ? Math.Min(Math.Min(p.MaxPayoutSpeed*h,p.MaxLineLength-length),Math.Max(0,(raw-dragLimit)*denominator/activeK)) : 0;
            bool newSlip = dragEngaged && length<p.MaxLineLength-1e-8;
            if (newSlip && !slipping) Emit(FishingEventKind.DragReleased);
            slipping = newSlip;
            length += payout;
            payoutSpeed=payout/h;
            tension = Math.Max(0,raw-payout*activeK/denominator);
            rodDeflection = Math.Min(maxDeflection,tension/p.RodStiffness);
            rodForce = axis*tension;
            bentTip = tip+axis*rodDeflection;
            extension = Math.Max(0,distance-rodDeflection-length);
            velocity = freeVelocity-axis*(tension*h/p.FishMass);
            if (velocity.Length > p.MaxFishSpeed) velocity = velocity.Normalized*p.MaxFishSpeed;
            SimVector oldFish = fish;
            ConstrainFish(fish+velocity*h);
            if (IsTerminal) return;

            // Energy depends on swimming effort and physical work, not burst count or time alone.
            double effortPower = thrust*(.18+Math.Abs(SimVector.Dot(velocity,heading)))+shakeForce.Length*(.1+Math.Abs(SimVector.Dot(velocity,side)));
            double lineWork = tension*Math.Max(0,-SimVector.Dot((fish-oldFish)/h,axis));
            double recovery = burst ? 0 : p.RecoveryPerSecond*(1-Numbers.Clamp(tension/p.LineStrength,0,1));
            stamina = Numbers.Clamp(stamina+h*(recovery-(effortPower+lineWork*.3)/p.StaminaCapacityJoules),0,1);
            if (tension > p.LineStrength*p.PeakBreakMultiplier) { Finish(FailureReason.PeakOverload); return; }
            damage += Math.Max(0,tension/p.LineStrength-p.DamageStartFraction)*h/p.FatigueSeconds;
            damage = Math.Min(1,damage);
            if (damage >= 1) { Finish(FailureReason.FatigueOverload); return; }
            // No random instant hook loss: high-load shaking gradually enlarges the hook hold,
            // lowering tolerance to later slack. Controlled tension leaves hook quality intact.
            hookQuality=Math.Max(.01,hookQuality-p.HookWearPerSecond*headShake*
                Numbers.Clamp((tension/p.LineStrength-.5)*2,0,1)*h);
            bool slack = tension < p.SlackThreshold;
            if (slack && !slackEvent) Emit(FishingEventKind.Slack);
            slackEvent = slack;
            slackTime = slack ? slackTime+h : Math.Max(0,slackTime-h*2);
            if (slackTime > p.SlackLossSeconds*(.35+.65*hookQuality)) { Finish(FailureReason.SlackLine); return; }

            bool landable = distanceToPlayer <= p.LandingRadius && Math.Abs(input.PlayerPosition.Y-sample.SurfaceY) <= p.LandingMaxHeight &&
                stamina <= p.LandingStamina && tension > p.SlackThreshold && tension < p.LineStrength && !burst;
            if (phase == FishingPhase.Landing)
            {
                if (!input.RequestLanding || !landable) SetPhase(FishingPhase.Fighting);
                else if (timeInPhase >= p.LandingSeconds) { SetPhase(FishingPhase.Resolved); ReleaseLine(); Emit(FishingEventKind.Landed); }
            }
            else if (input.RequestLanding && landable) { SetPhase(FishingPhase.Landing); Emit(FishingEventKind.Landing); }
        }

        private void ConstrainFish(SimVector proposed)
        {
            SimVector delta = proposed-fish;
            int checks = Math.Max(1,(int)Math.Ceiling(delta.Length/.1));
            SimVector valid = fish;
            for (int i=1;i<=checks;i++)
            {
                SimVector next = fish+delta*((double)i/checks);
                WaterSample sample;
                if (!Sample(next,out sample) || !water.IsFishPathClear(valid,next))
                {
                    // Remain at last valid wet position; reflect away from blocked shoreline/obstacle.
                    velocity = -velocity*.2; heading = -heading; fish = valid; return;
                }
                double y = Numbers.Clamp(next.Y,sample.BottomY+p.WaterClearance,sample.SurfaceY-p.WaterClearance);
                SimVector bounded = new SimVector(next.X,y,next.Z);
                if (!water.IsFishPathClear(valid,bounded)) { velocity = SimVector.Zero; fish = valid; return; }
                if (y != next.Y) velocity = new SimVector(velocity.X,0,velocity.Z);
                valid = bounded;
            }
            fish = valid;
        }

        private void UpdateBobber(WaterSample sample, bool fighting)
        {
            double h = p.FixedStep;
            double pullY = 0;
            SimVector pullXZ = SimVector.Zero;
            if (fighting)
            {
                // Simplified leader-connected float: tow force follows the actual fish displacement.
                pullXZ = (fish-bobber).Horizontal*.7;
                pullY = -Math.Min(p.BobberPull*2,tension*.015);
            }
            else if (phase == FishingPhase.Nibbling || phase == FishingPhase.BiteWindow)
            {
                double pulse=biteMotion.Sample(timeInPhase,phase==FishingPhase.BiteWindow);
                double vertical=pattern==BitePattern.Lift ? .65 : -(pattern==BitePattern.Dive ? 1.6 : .7);
                SimVector mouthTarget=baitOrigin+new SimVector(pattern==BitePattern.Travel ? .12*pulse : 0,
                    .12*pulse*vertical,pattern==BitePattern.Travel ? .06*pulse : 0);
                SimVector oldFish=fish;
                ConstrainFish(SimVector.Lerp(fish,mouthTarget,1-Math.Exp(-h/.09)));
                velocity=(fish-oldFish)/h;
                baitDisplacement=fish-baitOrigin;
                // Fish moves the bait first. The constrained leader transmits this displacement
                // to the float; shallows/solid obstacles can therefore weaken a visible bite.
                SimVector leaderForce=baitDisplacement*(p.BobberPull/.12);
                pullY=leaderForce.Y;pullXZ=leaderForce.Horizontal;
            }
            SimVector target = (fighting ? new SimVector(fish.X,sample.SurfaceY,fish.Z) : castTarget)+new SimVector(0,p.FloatRestOffset,0);
            // Implicit spring and damping remain stable with very light floats.
            double stiffness = p.BobberBuoyancy;
            SimVector force = (target-bobber)*stiffness+pullXZ+new SimVector(0,pullY,0);
            bobberVelocity = (bobberVelocity+force*(h/p.BobberMass))/(1+h*p.BobberDamping/p.BobberMass+h*h*stiffness/p.BobberMass);
            SimVector nextBobber = bobber+bobberVelocity*h;
            WaterSample floatWater;
            if (!Sample(nextBobber,out floatWater))
            { nextBobber = new SimVector(fish.X,sample.SurfaceY,fish.Z); bobberVelocity = SimVector.Zero; }
            bobber = nextBobber;
            bobberUp = (new SimVector(0,1,0)-bobberVelocity.Horizontal*.4).Normalized;
        }

        private void StartBurst(bool near)
        {
            behaviour = near ? FishBehaviour.NearBankSurge : FishBehaviour.Sprinting;
            behaviourTime = 0; bursts++;
            sprintDuration = p.BurstSeconds*(.8+.4*Next())*(.5+.5*stamina);
            recoveryDuration=p.RecoverySeconds*(.8+.4*Next())*(1+.4*(1-stamina));
            escapeSide=2*Next()-1;
            burstStrength=.85+.3*Next();
            Emit(FishingEventKind.Sprint);
        }
        private bool Sample(SimVector position, out WaterSample sample)
        { return water.TrySample(position,out sample) && sample.IsValid && sample.SurfaceY-sample.BottomY >= p.WaterClearance*2; }
        private double Next()
        { double n = random.NextUnit(); if (!Numbers.Finite(n) || n < 0 || n >= 1) throw new InvalidOperationException("Random source must return [0,1)."); return n; }
        private void SetPhase(FishingPhase next) { phase=next; timeInPhase=0; }
        private void Emit(FishingEventKind kind) { events.Enqueue(new FishingEvent(sessionId,++eventSequence,tick,kind,failure)); }
        private void Finish(FailureReason reason)
        {
            if (IsTerminal) return;
            failure=reason;
            if (reason == FailureReason.PeakOverload || reason == FailureReason.FatigueOverload)
            { SetPhase(FishingPhase.LineBroken); Emit(FishingEventKind.LineBroken); }
            else if (reason == FailureReason.EarlyStrike || reason == FailureReason.MissedBite || reason == FailureReason.WeakStrike || reason == FailureReason.SlackLine)
            { SetPhase(FishingPhase.HookLost); Emit(FishingEventKind.HookLost); }
            else { SetPhase(FishingPhase.Cancelled); Emit(FishingEventKind.Cancelled); }
            // Failure ends the physical connection immediately, even if the terminal snapshot is rendered again.
            ReleaseLine();
        }
        private void ReleaseLine()
        { tension=0; extension=0; rodForce=SimVector.Zero; rodDeflection=0; bentTip=tip; slipping=false; payoutSpeed=0; }
        public void Cancel() { if (phase != FishingPhase.Idle) Finish(FailureReason.UserCancelled); }
        public FishingEvent[] DrainEvents()
        { FishingEvent[] result=events.ToArray(); events.Clear(); return result; }
        public SimulationSnapshot Snapshot()
        {
            return new SimulationSnapshot {
                SessionId=sessionId, Tick=tick, Phase=phase, Behaviour=behaviour, BitePattern=pattern, Failure=failure,
                FishPosition=fish, FishVelocity=velocity, FishHeading=heading, BobberPosition=bobber, BobberVelocity=bobberVelocity,
                BobberUp=bobberUp, BentRodTip=bentTip, RodForce=rodForce, LineLength=length, LineExtension=extension,
                Tension=tension, RodDeflection=rodDeflection, RodBendRadians=Math.Asin(Numbers.Clamp(rodDeflection/p.RodLength,0,1)),
                Stamina=stamina, HookQuality=hookQuality, LineDamage=damage, SlackSeconds=slackTime,
                BiteProgress=phase==FishingPhase.BiteWindow ? Numbers.Clamp(timeInPhase/p.BiteWindowSeconds,0,1) : 0,
                FishThrust=thrust, BurstCount=bursts, DragSlipping=slipping,
                BurstEnvelope=burstEnvelope,HeadShake01=headShake,PayoutMetersPerSecond=payoutSpeed,BaitDisplacement=baitDisplacement
            };
        }
    }
}
