using System;

namespace PZAEC.Fishing.Contracts
{
    // Constructors provide probe defaults only. E supplies the production file and validation.
    public sealed class FishingConfig
    {
        public int Version = FishingContract.Version;
        public RodConfig Rod = new RodConfig();
        public LineConfig Line = new LineConfig();
        public FloatConfig Float = new FloatConfig();
        public HookConfig Hook = new HookConfig();
        public FishConfig Fish = new FishConfig();
        public ControlConfig Controls = new ControlConfig();
        public SessionConfig Session = new SessionConfig();
    }
    public sealed class RodConfig
    {
        public float LengthMeters=2.4f, StiffnessNewtonsPerMeter=100, DampingNewtonSecondsPerMeter=8;
        public float MaxDeflectionMeters=0.7f, MinPitchRadians=-0.35f, MaxPitchRadians=1.3f, MaxYawRadians=1.2f;
        public float AngularSpeedRadiansPerSecond=2.5f;
    }
    public sealed class LineConfig
    {
        public float FixedLengthMeters=0; // Positive: hand pole, no reel or drag payout.
        public float MaxLengthMeters=35, StiffnessNewtonsPerMeter=180, DampingNewtonSecondsPerMeter=3;
        public float BreakForceNewtons=90, DamageStartFraction=0.8f, DamagePerSecond=0.4f;
        public float DragMinNewtons=5, DragMaxNewtons=65, ReelSpeedMetersPerSecond=0.8f, MaxPayoutMetersPerSecond=6;
    }
    public sealed class FloatConfig
    {
        public float MassKg=0.012f, BuoyancyNewtonsPerMeter=2.5f, DampingNewtonSecondsPerMeter=0.3f;
        public float HeightMeters=0.18f, RestSubmerged01=0.4f;
    }
    public sealed class HookConfig
    {
        public float BiteWindowSeconds=1.2f, SlackLossSeconds=2.5f, MinimumStrikeSpeedRadiansPerSecond=0.6f;
        public float MaxSafeStrikeSpeedRadiansPerSecond=4, InitialQuality01=0.8f;
    }
    public sealed class FishConfig
    {
        public string Id=FishingContract.FishDefinition;
        public float MassKg=3, CruiseForceNewtons=9, BurstForceNewtons=50, DragCoefficient=3;
        public float StaminaJoules=450, RecoveryWatts=8, BurstSeconds=2, RecoverySeconds=4;
        public float CruiseSpeedMetersPerSecond=0.5f, BurstSpeedMetersPerSecond=3;
        public float NibbleMinSeconds=1, NibbleMaxSeconds=3, BiteWaitMinSeconds=3, BiteWaitMaxSeconds=12;
        public float NearShoreSurgeMeters=3, LandingStamina01=0.12f, LandingDistanceMeters=1.8f;
    }
    public sealed class ControlConfig
    {
        public float RadiansPerMouseUnit=0.04f, LoadedResponseFloor01=0.25f, AutoBackThreshold01=0.85f;
        public float AutoBackGain=0.15f, AutoBackDecaySeconds=0.08f, MaxAutoBack01=0.6f;
        public float PlayerResistanceNewtons=110, MinAgainstPullScale=0.1f, InitialDrag01=0.5f, FeedbackIntensity01=0.3f;
        public bool AutoBackEnabled=true;
        public bool ClickStrike=false;
        public string CastKey="Mouse0", StrikeKey="Mouse0", ReelKey="Mouse1", FreeLookKey="LeftAlt", RecenterKey="LeftControl", CancelKey="Escape";
        public string DragIncreaseKey="Equals", DragDecreaseKey="Minus";
    }
    public sealed class SessionConfig
    {
        public float MinDepthMeters=0.6f, MaxCastMeters=25, MaxPlayerDistanceMeters=40, TimeoutSeconds=300;
        public int MaxCatchUpTicks=8;
    }
}
