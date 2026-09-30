using System;

namespace PZAEC.Fishing.Contracts
{
    public static class FishingContract
    {
        public const int Version = 1;
        public const string RodItem = "pzaecFishingRodBasic";
        public const string BaitItem = "pzaecFishingBaitWorm";
        public const string FishItem = "pzaecFishingFishCarp";
        public const string MealItem = "pzaecFishingMealGrilled";
        public const string FishDefinition = "carp";
        public const string ConfigFile = "Config/Fishing/settings.xml";
        public const float FixedStepSeconds = 1f / 60f;
    }

    // Absolute game-world coordinates, not Unity's floating-origin render coordinates.
    public struct Vec3
    {
        public float X, Y, Z;
        public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static Vec3 Zero => new Vec3();
        public static Vec3 Up => new Vec3(0, 1, 0);
        public float LengthSquared => X * X + Y * Y + Z * Z;
        public float Length => (float)Math.Sqrt(LengthSquared);
        public Vec3 Normalized => Length > 0.000001f ? this / Length : Zero;
        public bool IsFinite => Scalar.IsFinite(X) && Scalar.IsFinite(Y) && Scalar.IsFinite(Z);
        public static float Dot(Vec3 a, Vec3 b) => a.X*b.X+a.Y*b.Y+a.Z*b.Z;
        public static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
        public static Vec3 Lerp(Vec3 a, Vec3 b, float t) => a+(b-a)*Scalar.Clamp(t,0,1);
        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
        public static Vec3 operator -(Vec3 a) => new Vec3(-a.X,-a.Y,-a.Z);
        public static Vec3 operator *(Vec3 a, float b) => new Vec3(a.X*b,a.Y*b,a.Z*b);
        public static Vec3 operator /(Vec3 a, float b) => new Vec3(a.X/b,a.Y/b,a.Z/b);
        public override string ToString() => string.Format(System.Globalization.CultureInfo.InvariantCulture,"({0:F3},{1:F3},{2:F3})",X,Y,Z);
    }
    public static class Scalar
    {
        public static bool IsFinite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        public static float Clamp(float x,float min,float max) => Math.Max(min,Math.Min(max,x));
    }
    public enum FishingPhase { Idle, Casting, Settling, Waiting, Nibbling, BiteWindow, Hooked, Fighting, Landing, Resolved, Cancelled, LineBroken, HookLost }
    public enum FishBehavior { Cruising, Inspecting, Mouthing, Startled, Sprinting, Recovering, SideRun, LandingSurge, Exhausted }
    public enum FailureReason { None, CancelledByPlayer, EarlyStrike, LateStrike, Overload, SlackLine, InvalidWater, ObstructedLine, TooFar, Damaged, Dead, ItemChanged, MenuOpened, Vehicle, Swimming, Disconnected, WorldClosed, Timeout, InvalidInput, ModuleError }
    public enum FishingEventKind { Cast, WaterContact, Nibble, BaitTaken, Strike, Hooked, Sprint, DragSlip, Slack, LineBroken, HookLost, Landed, Cancelled }
    public enum WaterSampleStatus { Valid, Dry, Unloaded, OutOfRange, Occluded }
    public enum SurfaceAccuracy { VoxelEstimate, NativeRayHit }
    public enum AuthorityMode { Standalone, Server, PredictedClient, Observer }
    public enum SettlementStatus { Granted, AlreadyGranted, InventoryFull, Invalid, Deferred }
}
