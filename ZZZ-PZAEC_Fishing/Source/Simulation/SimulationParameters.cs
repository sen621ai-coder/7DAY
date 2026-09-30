using System;

namespace PZAEC.Fishing.Simulation
{
    // Provisional reference defaults for B tests. E owns the eventual shipped configuration.
    public sealed class SimulationParameters
    {
        public double FixedStep = 1.0 / 120;
        public double FixedLineLength=0;
        public double FishMass = 2.5, CruiseForce = 4, BurstForce = 22, WaterResistance = 5;
        public double MaxFishSpeed = 7, BurstSeconds = 1.8, RecoverySeconds = 2.4;
        public double CruiseSwimSpeed = 30, BurstSwimSpeed = 30, NearBankSurgeDistance = 3.96;
        public double StaminaCapacityJoules = 500, RecoveryPerSecond = .025;
        public double LineStiffness = 180, LineDamping = 4, LineStrength = 65, PeakBreakMultiplier = 1.7;
        public double FatigueSeconds = 2, SlackLossSeconds = 3, SlackThreshold = .12;
        public double DamageStartFraction = 1, InitialHookQuality = 1;
        public double RodStiffness = 40, RodLength = 2.4, RodMaxBendRadians = .6;
        public double MinLineLength = .7, MaxLineLength = 65, ReelSpeed = .8, ReelStallForce = 55;
        public double DragMinForce = 6, DragMaxForce = 75, MaxPayoutSpeed = 8;
        public double CastSeconds = .45, SettleSeconds = .65, WaitMinSeconds = 2, WaitMaxSeconds = 5;
        public double NibbleSeconds = 2, BiteWindowSeconds = 1.3;
        public double NibbleMaxSeconds = 2, FloatRestOffset = 0;
        public double BobberMass = .025, BobberBuoyancy = 3, BobberDamping = .5, BobberPull = .42;
        public double WaterClearance = .15, LeaderDepth = .6;
        public double LandingRadius = 1.8, LandingMaxHeight = 3, LandingStamina = .2, LandingSeconds = .6;
        public double MaxAnchorSpeed = 35, MaxRodReach = 5, SessionTimeoutSeconds = 600;
        public double TurnRateRadians = 1.8, HeadShakeFraction = .13, BurstRiseSeconds = .22;
        public double DragStartRatio = 1.03, DragStopRatio = .97, HookWearPerSecond = .015;

        public SimulationParameters Copy() { return (SimulationParameters)MemberwiseClone(); }
        public void Validate()
        {
            Numbers.Range(FixedStep, 1.0/240, 1.0/30, "FixedStep");
            Numbers.Range(FixedLineLength,0,MaxLineLength,"FixedLineLength");
            Numbers.Range(FishMass, .05, 100, "FishMass");
            Numbers.Range(CruiseForce, 0, 1000, "CruiseForce");
            Numbers.Range(BurstForce, 0, 2000, "BurstForce");
            Numbers.Range(WaterResistance, .1, 1000, "WaterResistance");
            Numbers.Range(MaxFishSpeed, .1, 30, "MaxFishSpeed");
            Numbers.Range(CruiseSwimSpeed, .01, 30, "CruiseSwimSpeed");
            Numbers.Range(BurstSwimSpeed, .01, 30, "BurstSwimSpeed");
            Numbers.Range(NearBankSurgeDistance, .1, 20, "NearBankSurgeDistance");
            Numbers.Range(BurstSeconds, .1, 30, "BurstSeconds");
            Numbers.Range(RecoverySeconds, .1, 60, "RecoverySeconds");
            Numbers.Range(StaminaCapacityJoules, 1, 100000, "StaminaCapacityJoules");
            Numbers.Range(RecoveryPerSecond, 0, 1000, "RecoveryPerSecond");
            Numbers.Range(LineStiffness, 1, 10000, "LineStiffness");
            Numbers.Range(LineDamping, 0, 1000, "LineDamping");
            Numbers.Range(LineStrength, .1, 10000, "LineStrength");
            Numbers.Range(PeakBreakMultiplier, 1, 10, "PeakBreakMultiplier");
            Numbers.Range(DamageStartFraction, .1, 1, "DamageStartFraction");
            Numbers.Range(InitialHookQuality, .01, 1, "InitialHookQuality");
            Numbers.Range(FatigueSeconds, .01, 300, "FatigueSeconds");
            Numbers.Range(SlackLossSeconds, .01, 300, "SlackLossSeconds");
            Numbers.Range(SlackThreshold, 0, 10, "SlackThreshold");
            Numbers.Range(RodStiffness, 1, 10000, "RodStiffness");
            Numbers.Range(RodLength, .1, 10, "RodLength");
            Numbers.Range(RodMaxBendRadians, .0001, Math.PI/2-.00001, "RodMaxBendRadians");
            Numbers.Range(MinLineLength, .1, 10, "MinLineLength");
            Numbers.Range(MaxLineLength, MinLineLength, 200, "MaxLineLength");
            Numbers.Range(ReelSpeed, 0, 10, "ReelSpeed");
            Numbers.Range(ReelStallForce, .1, 10000, "ReelStallForce");
            Numbers.Range(DragMinForce, .01, 10000, "DragMinForce");
            Numbers.Range(DragMaxForce, DragMinForce, 10000, "DragMaxForce");
            Numbers.Range(MaxPayoutSpeed, .01, 50, "MaxPayoutSpeed");
            Numbers.Range(CastSeconds, .01, 10, "CastSeconds");
            Numbers.Range(SettleSeconds, .01, 10, "SettleSeconds");
            Numbers.Range(WaitMinSeconds, .01, 120, "WaitMinSeconds");
            Numbers.Range(WaitMaxSeconds, WaitMinSeconds, 120, "WaitMaxSeconds");
            Numbers.Range(NibbleSeconds, .1, 60, "NibbleSeconds");
            Numbers.Range(NibbleMaxSeconds, NibbleSeconds, 60, "NibbleMaxSeconds");
            Numbers.Range(FloatRestOffset, -1, 1, "FloatRestOffset");
            Numbers.Range(BiteWindowSeconds, .1, 10, "BiteWindowSeconds");
            Numbers.Range(BobberMass, .001, 1, "BobberMass");
            Numbers.Range(BobberBuoyancy, .1, 100, "BobberBuoyancy");
            Numbers.Range(BobberDamping, .01, 10, "BobberDamping");
            Numbers.Range(BobberPull, .01, 10, "BobberPull");
            Numbers.Range(WaterClearance, .01, 1, "WaterClearance");
            Numbers.Range(LeaderDepth, WaterClearance, 5, "LeaderDepth");
            Numbers.Range(LandingRadius, .2, 5, "LandingRadius");
            Numbers.Range(LandingMaxHeight, .2, 5, "LandingMaxHeight");
            Numbers.Range(LandingStamina, 0, .5, "LandingStamina");
            Numbers.Range(LandingSeconds, .1, 5, "LandingSeconds");
            Numbers.Range(MaxAnchorSpeed, 1, 100, "MaxAnchorSpeed");
            Numbers.Range(MaxRodReach, RodLength, 15, "MaxRodReach");
            Numbers.Range(SessionTimeoutSeconds, 10, 3600, "SessionTimeoutSeconds");
            Numbers.Range(TurnRateRadians, .1, 8, "TurnRateRadians");
            Numbers.Range(HeadShakeFraction, 0, .4, "HeadShakeFraction");
            Numbers.Range(BurstRiseSeconds, .05, 1, "BurstRiseSeconds");
            Numbers.Range(DragStartRatio, 1, 1.2, "DragStartRatio");
            Numbers.Range(DragStopRatio, .8, 1, "DragStopRatio");
            Numbers.Range(HookWearPerSecond, 0, .1, "HookWearPerSecond");
        }
    }
}
