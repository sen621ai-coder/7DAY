using System;

namespace PZAEC.Fishing.Contracts
{
    // DTOs are caller-owned values; never mutate a snapshot retained by another module.
    public struct RawInputFrame
    {
        public long Sequence;
        public double SampleTimeSeconds;
        public float DurationSeconds;
        public float MouseRightDelta, MouseBackDelta;
        public float MoveRight, MoveForward, DragAdjustDelta;
        public bool CastPressed, StrikePressed, ReelHeld, FreeLookHeld, RecenterHeld, CancelPressed;
        public bool InputAllowed;
    }
    public struct ControlIntent
    {
        public long InputSequence;
        public float RodPitchRadians, RodYawRadians;
        public float Reel01, Drag01;
        public bool Cast, Strike, Cancel, FreeLook;
        public MovementRequest Movement;
        public float PlayerEffort01,PlayerStaminaCost;
    }
    public struct MovementRequest
    {
        // Additive local-space axes in [-1,1]. Native WASD already exists; do not duplicate it.
        public float ExtraRight, ExtraForward;
        // Unit horizontal world direction pointing from player toward the fish.
        public Vec3 PullDirection;
        // Attenuates only motion opposing PullDirection. 1=no resistance, 0=no outward motion.
        public float AgainstPullScale;
        public bool Active;
        public static MovementRequest None => new MovementRequest { AgainstPullScale = 1 };
    }
    public struct RodPose
    {
        public Vec3 Root, Tip, Forward, Right;
        public float PitchRadians, YawRadians;
    }
    public struct WaterSample
    {
        public WaterSampleStatus Status;
        public SurfaceAccuracy Accuracy;
        public Vec3 SurfacePoint, Normal;
        public float BottomY, DepthMeters;
        public bool BottomKnown;
        public bool IsValid => Status == WaterSampleStatus.Valid;
    }
    public struct SegmentHit
    {
        public bool Obstructed, Unloaded;
        public Vec3 Point, Normal;
    }
    public struct EnvironmentFrame
    {
        public long Tick;
        public double TimeSeconds;
        public int PlayerEntityId;
        public Vec3 PlayerPosition, PlayerVelocity, ViewForward, ViewRight;
        public RodPose Rod;
        public WaterSample Water;
        public bool CanFish, IsGrounded;
        public bool HasPlayerStamina;
        public float PlayerStamina01;
        public float PlayerStaminaMaximum;
        public FailureReason UnavailableReason;
    }
    public struct SessionStart
    {
        public Guid SessionId;
        public string PlayerPersistentId;
        public int PlayerEntityId;
        public uint Seed;
        public Vec3 CastTarget;
        public float InitialLineLengthMeters;
        public string FishDefinitionId;
        public AuthorityMode Authority;
    }
    public struct FishingSnapshot
    {
        public Guid SessionId;
        public long Tick, LastInputSequence;
        public double TimeSeconds;
        public FishingPhase Phase;
        public FishBehavior FishBehavior;
        public AuthorityMode Authority;
        public Vec3 FishPosition, FishVelocity, FishForward;
        public float FishMassKg, FishStamina01, FishBurstForceNewtons;
        public int BurstIndex;
        public float LineLengthMeters, LineExtensionMeters, LineTensionNewtons, LineDamage01;
        public RodPose Rod;
        public Vec3 RodLoadNewtons, FloatPosition, FloatUp;
        public float FloatSubmerged01, HookQuality01, SlackSeconds, Drag01;
        public FailureReason Failure;
        public bool IsTerminal => Phase == FishingPhase.Resolved || Phase == FishingPhase.Cancelled || Phase == FishingPhase.LineBroken || Phase == FishingPhase.HookLost;
    }
    public struct FishingEvent
    {
        public Guid SessionId;
        public long Sequence, Tick;
        public FishingEventKind Kind;
        public FailureReason Reason;
        public Vec3 Position;
        public float Intensity01;
    }
    public struct RenderFrame
    {
        public FishingSnapshot Previous, Current;
        public float Alpha;
        // Subtract this offset when converting any absolute position to Unity scene coordinates.
        public Vec3 RenderOrigin;
        public bool IsLocalPlayer, IsDedicatedServer, ShowDebug;
    }
    public struct CatchResult
    {
        public Guid SessionId, SettlementId;
        public string PlayerPersistentId, FishDefinitionId;
        public float MassKg;
        public long TerminalTick;
    }
    public struct RewardSpec { public string ItemId; public int Count; public float FishMassKg; }
    public struct NetworkInput
    {
        public int ContractVersion;
        public Guid SessionId;
        public long Sequence, ClientTick;
        public RawInputFrame Input;
        // No reward, fish mass, terminal-state or tension fields are accepted from clients.
    }
    public struct NetworkSnapshot
    {
        public int ContractVersion;
        public long LastAcceptedInputSequence;
        public FishingSnapshot Snapshot;
        public FishingEvent[] Events;
    }
}
