using System;

namespace PZAEC.Fishing.Controls
{
    // Module-local DTOs, not substitutes for A's shared Contracts.
    public sealed class ControlSettings
    {
        public double RadiansPerMouseUnit = 0.003;
        public double Sensitivity = 1;
        public double MinPitch = -0.15, MaxPitch = 1.25, InitialPitch = 0.35;
        public double MaxSideAngle = 1.1;
        public double BackstepStartPitch = 1.05;
        public double FullBackstepMouseUnitsPerSecond = 400;
        public double BackstepReleaseSeconds = 0.12;
        public double ReferencePullNewtons = 100;
        public double MinimumLoadedResponse = 0.2;
        public double DragChangePerSecond = 0.4, InitialDrag = 0.5;
        public double MaxFrameSeconds = 0.25;
        public bool AutoBackstep = true;

        internal ControlSettings CopyValidated()
        {
            var copy = (ControlSettings)MemberwiseClone();
            foreach (var field in typeof(ControlSettings).GetFields())
                if (field.FieldType == typeof(double) && !ControlMath.Finite((double)field.GetValue(copy)))
                    throw new ArgumentException("Non-finite setting: " + field.Name);
            if (RadiansPerMouseUnit <= 0 || Sensitivity <= 0 || MinPitch >= MaxPitch ||
                InitialPitch < MinPitch || InitialPitch > MaxPitch || MaxSideAngle <= 0 ||
                BackstepStartPitch < MinPitch || BackstepStartPitch >= MaxPitch ||
                FullBackstepMouseUnitsPerSecond <= 0 || BackstepReleaseSeconds <= 0 ||
                ReferencePullNewtons <= 0 || MinimumLoadedResponse <= 0 || MinimumLoadedResponse > 1 ||
                DragChangePerSecond < 0 || InitialDrag < 0 || InitialDrag > 1 || MaxFrameSeconds <= 0)
                throw new ArgumentException("Invalid fishing control settings.");
            return copy;
        }
    }

    public struct ControlFrame
    {
        public long Sequence;
        public double Seconds;
        // Unscaled displacement since previous frame, NOT velocity or delta * dt.
        // Positive Pull raises the rod; positive Side presses right.
        public double MousePull, MouseSide;
        public double MoveRight, MoveForward;
        public double DragAxis;
        public bool SessionActive, CanControl, MenuOrChatOpen, HasFocus;
        public bool FreeLook, Recenter, ReelHeld, StrikePressed, CancelPressed;
    }

    public struct ControlLoad
    {
        public bool LineTaut;
        public double TensionNewtons;
        // Horizontal direction from player to fish, in player-local coordinates.
        public double PullRight, PullForward;
        public bool DirectionalSideResistance;
        public double Fatigue01,PitchDropRadians,SideDriftRadians;
    }

    public struct ControlIntent
    {
        public long Sequence;
        public bool OwnsRodInput, SuppressLook, ReelHeld, StrikePressed, CancelRequested;
        public double PitchRadians, SideRadians, DragFraction;
        // Final normalized movement intention. Runtime applies its EXISTING speed
        // and collision system once. Do not multiply fishing resistance a second time.
        public double MoveRight, MoveForward;
        public double AutoBackstep, MovementLoadMultiplier;
    }

    internal static class ControlMath
    {
        internal static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }
        internal static double Clamp(double x, double low, double high) { return Math.Max(low, Math.Min(high, x)); }
        internal static double Clean(double x) { return Finite(x) ? x : 0; }
    }
}
