using System;

namespace PZAEC.Fishing.Controls
{
    /// <summary>Single-thread render-to-simulation bridge; never replays mouse deltas.</summary>
    public sealed class ControlTickBuffer
    {
        private ControlIntent latest;
        private bool hasValue, strikePending, cancelPending;

        public void Reset()
        {
            latest = default(ControlIntent);
            hasValue = strikePending = cancelPending = false;
        }

        public void Publish(ControlIntent intent)
        {
            if (hasValue && intent.Sequence <= latest.Sequence)
                throw new ArgumentException("Cannot publish the same or an older input frame twice.");
            latest = intent;
            hasValue = true;
            // A control loss invalidates a queued strike, not a queued cancellation.
            strikePending = intent.OwnsRodInput && (strikePending || intent.StrikePressed);
            cancelPending |= intent.CancelRequested;
        }

        public ControlIntent Consume()
        {
            if (!hasValue) return new ControlIntent { MovementLoadMultiplier = 1 };
            var result = latest;
            result.StrikePressed = strikePending && !cancelPending;
            result.CancelRequested = cancelPending;
            strikePending = cancelPending = false;
            return result;
        }
    }
}
