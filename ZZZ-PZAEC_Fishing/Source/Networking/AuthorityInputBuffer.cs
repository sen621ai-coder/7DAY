using System;
using PZAEC.Fishing.Contracts;

namespace PZAEC.Fishing.Networking
{
    /// <summary>
    /// A's router queues accepted network input here. Consume ONCE per server update, then pass
    /// the result to SessionDriver.Advance with actual server dt. Never Advance once per packet.
    /// </summary>
    public sealed class AuthorityInputBuffer
    {
        RawInputFrame pending;
        long lastNetworkSequence, serverFrameSequence;
        double receivedAt;
        bool received;
        public long LastNetworkSequence {get {return lastNetworkSequence;}}
        public long LastConsumedNetworkSequence {get;private set;}
        public bool Push(NetworkInput value,double serverNow)
        {
            if(!FishingWire.ValidInput(value) || value.Sequence<=lastNetworkSequence || !FishingWire.Finite(serverNow) ||
                (received && serverNow<receivedAt))return false;
            var n=value.Input;
            // Bound aggregate deltas too: many individually valid messages cannot bypass limits.
            float right=pending.MouseRightDelta+n.MouseRightDelta,back=pending.MouseBackDelta+n.MouseBackDelta,drag=pending.DragAdjustDelta+n.DragAdjustDelta;
            if(Math.Abs(right)>256 || Math.Abs(back)>256 || Math.Abs(drag)>1)return false;
            n.MouseRightDelta=right;n.MouseBackDelta=back;n.DragAdjustDelta=drag;
            n.StrikePressed|=pending.StrikePressed;n.CastPressed|=pending.CastPressed;n.CancelPressed|=pending.CancelPressed;
            pending=n;lastNetworkSequence=value.Sequence;receivedAt=serverNow;received=true;return true;
        }
        public RawInputFrame Consume(double serverNow,float dt)
        {
            if(!FishingWire.Finite(serverNow) || (received && serverNow<receivedAt) || !Scalar.IsFinite(dt) || dt<0 || dt>1)
                throw new ArgumentOutOfRangeException(nameof(serverNow));
            var value=pending;
            // Packet loss never leaves reel, movement or a strike latched indefinitely.
            if(!received || serverNow-receivedAt>.25)
                value=new RawInputFrame {InputAllowed=true};
            value.Sequence=checked(++serverFrameSequence);value.SampleTimeSeconds=serverNow;value.DurationSeconds=dt;
            LastConsumedNetworkSequence=lastNetworkSequence;
            pending.MouseRightDelta=pending.MouseBackDelta=pending.DragAdjustDelta=0;
            pending.CastPressed=pending.StrikePressed=pending.CancelPressed=false;
            return value;
        }
        // SessionDriver needs a new sequence for each server frame even without a new packet.
        // Convert that INTERNAL frame sequence back to the network sequence before publishing.
        public NetworkSnapshot ForPublication(FishingSnapshot value,FishingEvent[] events)
        {
            value.LastInputSequence=LastConsumedNetworkSequence;
            return new NetworkSnapshot {ContractVersion=FishingContract.Version,LastAcceptedInputSequence=lastNetworkSequence,Snapshot=value,Events=events};
        }
        public void Clear() {pending=default(RawInputFrame);lastNetworkSequence=serverFrameSequence=LastConsumedNetworkSequence=0;received=false;receivedAt=0;}
    }

    /// <summary>A feeds real authoritative player positions, never client-requested positions.</summary>
    public sealed class FishingMovementGuard
    {
        Vec3 previous;
        double at;
        bool initialized;
        double allowance;
        public bool Observe(Vec3 position,double serverNow,float maxSpeedMetersPerSecond,float slackMeters=.75f)
        {
            if(!position.IsFinite || !FishingWire.Finite(serverNow) || !Scalar.IsFinite(maxSpeedMetersPerSecond) || maxSpeedMetersPerSecond<0 ||
                !Scalar.IsFinite(slackMeters) || slackMeters<0)return false;
            if(!initialized){previous=position;at=serverNow;allowance=slackMeters;initialized=true;return true;}
            double dt=serverNow-at;if(dt<0)return false;
            double allowed=Math.Min(slackMeters+maxSpeedMetersPerSecond,allowance+maxSpeedMetersPerSecond*dt);
            double distance=(position-previous).Length;
            if(distance>allowed)return false;
            allowance=allowed-distance;previous=position;at=serverNow;return true;
        }
        // Respawn, teleport or world change must cancel fishing before resetting this baseline.
        public void Reset() {initialized=false;}
    }
}
