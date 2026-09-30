using System;

namespace PZAEC.Fishing.Contracts
{
    public interface IRandomSource { uint NextUInt(); float Next01(); }
    public interface IWaterQuery
    {
        WaterSample SampleColumn(Vec3 position, float searchAboveMeters, float searchBelowMeters);
        SegmentHit TraceSolid(Vec3 from, Vec3 to);
    }
    public interface IEventSink { void Emit(FishingEvent value); }
    // B: one instance per session; it alone owns physical and behavioral state.
    public interface IFishingSimulation
    {
        void Begin(SessionStart start, FishingConfig config, EnvironmentFrame environment, IRandomSource random, IWaterQuery water, IEventSink events);
        FishingSnapshot Step(float dt, ControlIntent intent, EnvironmentFrame environment, IEventSink events);
        FishingSnapshot Cancel(FailureReason reason, IEventSink events);
        FishingSnapshot Current { get; }
    }
    // C: mouse deltas are consumed once, not multiplied by dt a second time.
    public interface IFishingControls
    {
        void Reset(ControlConfig config, RodConfig rod);
        ControlIntent Step(float dt, RawInputFrame input, FishingSnapshot previous, EnvironmentFrame environment);
        void Release();
    }
    // D: the controller passes one-shot events separately from interpolated snapshots.
    public interface IFishingPresentation : IDisposable
    {
        void Begin(SessionStart start, FishingConfig config);
        void Render(RenderFrame frame);
        void OnEvent(FishingEvent value);
        void Clear();
    }
    // E: never writes inventory. Null config / invalid definitions must fail closed.
    public interface IFishingContent
    {
        FishingConfig Load(string modDirectory);
        bool Validate(FishingConfig config, out string error);
        bool TryGetReward(CatchResult result, out RewardSpec reward);
    }
    // A: caller must already be authoritative; never expose this directly to untrusted messages.
    public interface ICatchSettlement { SettlementStatus TrySettle(CatchResult result); }
    // F: identity derives from the transport, never from payload fields.
    public interface IFishingNetwork
    {
        void Start(AuthorityMode mode);
        void SendInput(NetworkInput input);
        void Publish(NetworkSnapshot snapshot);
        void Tick(double nowSeconds);
        void Stop();
    }
    public interface IAuthoritySessionRouter
    {
        bool TryStart(string authenticatedPlayerId, int playerEntityId, Vec3 requestedTarget, out Guid sessionId);
        bool AcceptInput(string authenticatedPlayerId, NetworkInput input);
        void CancelPlayer(string authenticatedPlayerId, FailureReason reason);
        void ApplySnapshot(NetworkSnapshot snapshot);
    }
}
