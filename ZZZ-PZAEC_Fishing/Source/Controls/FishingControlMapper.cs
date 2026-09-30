using System;

namespace PZAEC.Fishing.Controls
{
    /// <summary>
    /// One instance per local fishing session. Called ONCE per rendered input frame.
    /// No Unity, Harmony, physics mutation, inventory mutation or player teleportation.
    /// Runtime carries absolute targets/held states into ticks, consuming edges once.
    /// </summary>
    public sealed class FishingControlMapper
    {
        private readonly ControlSettings settings;
        private double pitch, side, drag, backstep;
        private bool rearm, requireNeutralActions;
        private long lastSequence = -1;

        public FishingControlMapper(ControlSettings settings = null)
        {
            this.settings = (settings ?? new ControlSettings()).CopyValidated();
            Reset();
        }

        // Call on new session, world exit, death, equipment change or cancellation.
        // Runtime must separately release all game input overrides it owns.
        public void Reset()
        {
            pitch = settings.InitialPitch;
            side = backstep = 0;
            drag = settings.InitialDrag;
            rearm = requireNeutralActions = false;
            lastSequence = -1;
        }

        public ControlIntent Sample(ControlFrame input, ControlLoad load)
        {
            if (input.Sequence < 0 || input.Sequence <= lastSequence)
                throw new ArgumentException("Input frames must have unique increasing sequence numbers.");
            lastSequence = input.Sequence;
            var result = new ControlIntent { Sequence = input.Sequence, MovementLoadMultiplier = 1 };
            bool timeValid = ControlMath.Finite(input.Seconds) && input.Seconds > 0 && input.Seconds <= settings.MaxFrameSeconds;
            bool enabled = input.SessionActive && input.CanControl && input.HasFocus && !input.MenuOrChatOpen && timeValid;
            if (!enabled || input.CancelPressed)
            {
                result.CancelRequested = input.SessionActive && (input.CancelPressed || !input.CanControl);
                backstep = 0;
                rearm = requireNeutralActions = true;
                return WithPose(result);
            }

            result.OwnsRodInput = true;
            bool clutch = input.FreeLook || input.Recenter;
            result.SuppressLook = !input.FreeLook;
            bool justResumed = rearm;
            if (clutch)
            {
                backstep = 0;
                rearm = requireNeutralActions = true;
            }
            else
            {
                // Drop the first resumed displacement to prevent focus/menu/clutch jumps.
                double pull = justResumed ? 0 : ControlMath.Clean(input.MousePull) * settings.Sensitivity;
                double horizontal = justResumed ? 0 : ControlMath.Clean(input.MouseSide) * settings.Sensitivity;
                double loadRatio = LoadRatio(load);
                double response = 1 - loadRatio * (1 - settings.MinimumLoadedResponse);
                double oldPitch = pitch;
                pitch = ControlMath.Clamp(pitch + pull * settings.RadiansPerMouseUnit * (pull > 0 ? response : 1), settings.MinPitch, settings.MaxPitch);
                side = ControlMath.Clamp(side + horizontal * settings.RadiansPerMouseUnit * response, -settings.MaxSideAngle, settings.MaxSideAngle);

                // Only the portion of this mouse stroke above the threshold drives feet.
                // This makes crossing the threshold consistent across frame rates.
                double usedBelow = Math.Max(0, settings.BackstepStartPitch - oldPitch) / (settings.RadiansPerMouseUnit * response);
                double footPull = Math.Max(0, pull - usedBelow);
                if (!settings.AutoBackstep || input.MoveForward > 0 || pull < 0)
                    backstep = 0;
                else if (footPull > 0)
                    backstep = ControlMath.Clamp(footPull / input.Seconds / settings.FullBackstepMouseUnitsPerSecond, 0, 1);
                else
                    backstep = Math.Max(0, backstep - input.Seconds / settings.BackstepReleaseSeconds);

                // Held actions must be released once after UI/focus/clutch suspension.
                bool blockActions = justResumed || requireNeutralActions;
                if (!input.ReelHeld && !input.StrikePressed && input.DragAxis == 0)
                    requireNeutralActions = false;
                rearm = false;
                result.ReelHeld = !blockActions && input.ReelHeld;
                result.StrikePressed = !blockActions && input.StrikePressed;
                if (!blockActions)
                    drag = ControlMath.Clamp(drag + ControlMath.Clamp(ControlMath.Clean(input.DragAxis), -1, 1) * settings.DragChangePerSecond * input.Seconds, 0, 1);
            }

            double right = ControlMath.Clamp(ControlMath.Clean(input.MoveRight), -1, 1);
            double forward = ControlMath.Clamp(ControlMath.Clean(input.MoveForward), -1, 1);
            // Combine, don't add two full-strength backstep requests.
            if (forward <= 0) forward = Math.Min(forward, -backstep);
            double length = Math.Sqrt(right * right + forward * forward);
            if (length > 1) { right /= length; forward /= length; }
            double fishRight = ControlMath.Clean(load.PullRight), fishForward = ControlMath.Clean(load.PullForward);
            double fishLength = Math.Sqrt(fishRight * fishRight + fishForward * fishForward);
            double moveLength = Math.Sqrt(right * right + forward * forward);
            if (fishLength > 1e-8 && ControlMath.Finite(fishLength) && moveLength > 1e-8)
            {
                double opposition = Math.Max(0, -(right * fishRight + forward * fishForward) / (fishLength * moveLength));
                result.MovementLoadMultiplier = 1 - LoadRatio(load) * opposition * (1 - settings.MinimumLoadedResponse);
            }
            result.MoveRight = right * result.MovementLoadMultiplier;
            result.MoveForward = forward * result.MovementLoadMultiplier;
            result.AutoBackstep = backstep;
            return WithPose(result);
        }

        private double LoadRatio(ControlLoad load)
        {
            return load.LineTaut ? ControlMath.Clamp(ControlMath.Clean(load.TensionNewtons) / settings.ReferencePullNewtons, 0, 1) : 0;
        }

        private ControlIntent WithPose(ControlIntent result)
        {
            result.PitchRadians = pitch;
            result.SideRadians = side;
            result.DragFraction = drag;
            return result;
        }
    }
}
