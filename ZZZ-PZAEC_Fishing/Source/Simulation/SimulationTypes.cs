using System;

namespace PZAEC.Fishing.Simulation
{
    // B-local boundary types, NOT the unpublished A/Contracts interface.
    // Coordinates: right handed world convention supplied by adapter, +Y up. SI units.
    public struct SimVector
    {
        public readonly double X, Y, Z;
        public SimVector(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static SimVector Zero { get { return new SimVector(0, 0, 0); } }
        public double LengthSquared { get { return X * X + Y * Y + Z * Z; } }
        public double Length { get { return Math.Sqrt(LengthSquared); } }
        public bool IsFinite { get { return Numbers.Finite(X) && Numbers.Finite(Y) && Numbers.Finite(Z); } }
        public SimVector Normalized { get { double n = Length; return n > 1e-9 ? this / n : Zero; } }
        public SimVector Horizontal { get { return new SimVector(X, 0, Z); } }
        public static double Dot(SimVector a, SimVector b) { return a.X*b.X + a.Y*b.Y + a.Z*b.Z; }
        public static SimVector Lerp(SimVector a, SimVector b, double t) { return a + (b-a)*t; }
        public static SimVector operator +(SimVector a, SimVector b) { return new SimVector(a.X+b.X, a.Y+b.Y, a.Z+b.Z); }
        public static SimVector operator -(SimVector a, SimVector b) { return new SimVector(a.X-b.X, a.Y-b.Y, a.Z-b.Z); }
        public static SimVector operator -(SimVector a) { return a * -1; }
        public static SimVector operator *(SimVector a, double b) { return new SimVector(a.X*b, a.Y*b, a.Z*b); }
        public static SimVector operator /(SimVector a, double b) { return a * (1/b); }
    }

    internal static class Numbers
    {
        internal static bool Finite(double n) { return !double.IsNaN(n) && !double.IsInfinity(n); }
        internal static double Clamp(double n, double lo, double hi) { return Math.Max(lo, Math.Min(hi, n)); }
        internal static void Range(double n, double lo, double hi, string name)
        { if (!Finite(n) || n < lo || n > hi) throw new ArgumentOutOfRangeException(name); }
    }

    public enum FishingPhase { Idle, Casting, Settling, Waiting, Nibbling, BiteWindow, Hooked, Fighting, Landing, Resolved, Cancelled, LineBroken, HookLost }
    public enum FishBehaviour { Cruising, Sampling, HoldingBait, Startled, Sprinting, Recovering, SideRun, NearBankSurge, Exhausted }
    public enum BitePattern { Dip, Lift, Travel, Dive }
    public enum FailureReason { None, UserCancelled, EarlyStrike, MissedBite, WeakStrike, SlackLine, PeakOverload, FatigueOverload, WaterUnavailable, LineObstructed, OutOfRange, AnchorDiscontinuity, SessionTimeout }
    public enum FishingEventKind { Cast, Splash, Nibble, Bite, Hooked, Sprint, DragReleased, Slack, LineBroken, HookLost, Landing, Landed, Cancelled }

    public struct WaterSample
    {
        public readonly double SurfaceY, BottomY;
        public WaterSample(double surfaceY, double bottomY) { SurfaceY = surfaceY; BottomY = bottomY; }
        public bool IsValid { get { return Numbers.Finite(SurfaceY) && Numbers.Finite(BottomY) && SurfaceY > BottomY; } }
    }

    public interface IWaterDomain
    {
        // Return false for dry/unloaded/unknown regions, never invent water for unloaded chunks.
        bool TrySample(SimVector position, out WaterSample sample);
        // Fish sweep must include terrain/solid obstacles. Line test ignores water, rod and fish themselves.
        bool IsFishPathClear(SimVector from, SimVector to);
        bool IsLineClear(SimVector from, SimVector to);
    }

    public interface ISimulationRandom { double NextUnit(); }

    public sealed class SeededSimulationRandom : ISimulationRandom
    {
        private uint state;
        public SeededSimulationRandom(uint seed) { state = seed == 0 ? 0x6D2B79F5u : seed; }
        public double NextUnit()
        {
            uint x = state; x ^= x << 13; x ^= x >> 17; x ^= x << 5; state = x;
            return x / 4294967296.0;
        }
    }

    public struct SimulationInput
    {
        // Actual world position and unloaded tip pose from A, including real movement from previous tick.
        public SimVector PlayerPosition, UnloadedRodTip;
        public bool Strike, Reel, RequestLanding, Cancel;
        public double StrikeStrength; // [0,1], sampled only on Strike; no mouse API here.
        public double DragSetting; // [0,1], maps to configured min/max N.
        public double ReelFraction; // [0,1]; 0 with Reel=true preserves legacy full-rate core callers.
        internal void Validate()
        {
            if (!PlayerPosition.IsFinite || !UnloadedRodTip.IsFinite) throw new ArgumentException("Non-finite anchor.");
            Numbers.Range(StrikeStrength, 0, 1, "StrikeStrength");
            Numbers.Range(DragSetting, 0, 1, "DragSetting");
            Numbers.Range(ReelFraction, 0, 1, "ReelFraction");
        }
    }

    public struct FishingEvent
    {
        public readonly string SessionId;
        public readonly long Sequence, Tick;
        public readonly FishingEventKind Kind;
        public readonly FailureReason Reason;
        public FishingEvent(string sessionId, long sequence, long tick, FishingEventKind kind, FailureReason reason)
        { SessionId=sessionId; Sequence=sequence; Tick=tick; Kind=kind; Reason=reason; }
    }

    public sealed class SimulationSnapshot
    {
        public string SessionId { get; internal set; }
        public long Tick { get; internal set; }
        public FishingPhase Phase { get; internal set; }
        public FishBehaviour Behaviour { get; internal set; }
        public BitePattern BitePattern { get; internal set; }
        public FailureReason Failure { get; internal set; }
        public SimVector FishPosition { get; internal set; }
        public SimVector FishVelocity { get; internal set; }
        public SimVector FishHeading { get; internal set; }
        public SimVector BobberPosition { get; internal set; }
        public SimVector BobberVelocity { get; internal set; }
        public SimVector BobberUp { get; internal set; }
        public SimVector BentRodTip { get; internal set; }
        public SimVector RodForce { get; internal set; }
        public double LineLength { get; internal set; }
        public double LineExtension { get; internal set; }
        public double Tension { get; internal set; }
        public double RodDeflection { get; internal set; }
        public double RodBendRadians { get; internal set; }
        public double Stamina { get; internal set; }
        public double HookQuality { get; internal set; }
        public double LineDamage { get; internal set; }
        public double SlackSeconds { get; internal set; }
        public double BiteProgress { get; internal set; }
        public double FishThrust { get; internal set; }
        public int BurstCount { get; internal set; }
        public bool DragSlipping { get; internal set; }
        public double BurstEnvelope { get; internal set; }
        public double HeadShake01 { get; internal set; }
        public double PayoutMetersPerSecond { get; internal set; }
        public SimVector BaitDisplacement { get; internal set; }
        public bool IsTerminal { get { return Phase == FishingPhase.Resolved || Phase == FishingPhase.Cancelled || Phase == FishingPhase.LineBroken || Phase == FishingPhase.HookLost; } }
    }
}
