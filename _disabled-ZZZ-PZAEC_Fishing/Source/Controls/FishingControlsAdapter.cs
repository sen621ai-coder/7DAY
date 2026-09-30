using System;
using PZAEC.Fishing.Contracts;
using SharedIntent = PZAEC.Fishing.Contracts.ControlIntent;

namespace PZAEC.Fishing.Controls
{
    // A's InputAccumulator already splits mouse/drag deltas. Do NOT place
    // ControlTickBuffer in front of this adapter or deduplicate by input.Sequence.
    public sealed class FishingControlsAdapter : IFishingControls
    {
        FishingControlMapper mapper;
        long stepSequence;
        double drag, backstep, threshold, gain, decay, maxBack, resistance, minScale, angularSpeed, radians;
        bool autoBack, released = true;
        double previousPitch;

        public void Reset(ControlConfig config, RodConfig rod)
        {
            Release();
            if (config == null || rod == null) throw new ArgumentNullException("config/rod");
            ValidateFinite(config); ValidateFinite(rod);
            if (config.RadiansPerMouseUnit <= 0 || config.LoadedResponseFloor01 <= 0 || config.LoadedResponseFloor01 > 1 ||
                config.AutoBackThreshold01 < 0 || config.AutoBackThreshold01 > 1 || config.AutoBackGain < 0 ||
                config.AutoBackDecaySeconds <= 0 || config.MaxAutoBack01 < 0 || config.MaxAutoBack01 > 1 ||
                config.PlayerResistanceNewtons <= 0 || config.MinAgainstPullScale < 0 || config.MinAgainstPullScale > 1 ||
                config.InitialDrag01 < 0 || config.InitialDrag01 > 1 || config.FeedbackIntensity01 < 0 || config.FeedbackIntensity01 > 1 ||
                rod.MinPitchRadians >= rod.MaxPitchRadians || rod.MaxYawRadians <= 0 || rod.AngularSpeedRadiansPerSecond <= 0)
                throw new ArgumentException("Invalid shared control or rod configuration.");
            radians = config.RadiansPerMouseUnit;
            resistance = config.PlayerResistanceNewtons; minScale = config.MinAgainstPullScale;
            angularSpeed = rod.AngularSpeedRadiansPerSecond;
            threshold = rod.MinPitchRadians + (rod.MaxPitchRadians - (double)rod.MinPitchRadians) * config.AutoBackThreshold01;
            gain = config.AutoBackGain; decay = config.AutoBackDecaySeconds; maxBack = config.MaxAutoBack01;
            autoBack = config.AutoBackEnabled;
            previousPitch = ControlMath.Clamp(0.35, rod.MinPitchRadians, rod.MaxPitchRadians);
            mapper = new FishingControlMapper(new ControlSettings {
                RadiansPerMouseUnit = radians, InitialPitch = previousPitch,
                MinPitch = rod.MinPitchRadians, MaxPitch = rod.MaxPitchRadians, MaxSideAngle = rod.MaxYawRadians,
                BackstepStartPitch = rod.MinPitchRadians, AutoBackstep = false,
                MinimumLoadedResponse = config.LoadedResponseFloor01,
                ReferencePullNewtons = resistance, InitialDrag = config.InitialDrag01
            });
            drag = config.InitialDrag01; backstep = 0; stepSequence = 0; released = false;
        }

        public SharedIntent Step(float dt, RawInputFrame input, FishingSnapshot previous, EnvironmentFrame environment)
        {
            if (released) return Neutral(input.Sequence, false);
            if (previous.IsTerminal) { Release(); return Neutral(input.Sequence, false); }
            if (!Scalar.IsFinite(dt) || dt <= 0 || dt > 0.25f || !input.InputAllowed || !environment.CanFish ||
                input.CancelPressed || !ValidInput(input) || !Scalar.IsFinite(previous.LineTensionNewtons) ||
                previous.LineTensionNewtons < 0 || !previous.FishPosition.IsFinite || !environment.PlayerPosition.IsFinite)
            {
                Release(); return Neutral(input.Sequence, true);
            }
            double load = ControlMath.Clamp(previous.LineTensionNewtons / resistance, 0, 1);
            // Cap requested motion before load response; excess displacement is discarded,
            // never queued as movement that could continue after the mouse stops.
            double limit = angularSpeed * dt / radians;
            var local = mapper.Sample(new ControlFrame {
                Sequence = ++stepSequence, Seconds = dt, SessionActive = true, CanControl = true, HasFocus = true,
                MousePull = ControlMath.Clamp(input.MouseBackDelta, -limit, limit),
                MouseSide = ControlMath.Clamp(input.MouseRightDelta, -limit, limit),
                FreeLook = input.FreeLookHeld, Recenter = input.RecenterHeld,
                ReelHeld = input.ReelHeld, StrikePressed = input.StrikePressed
            }, new ControlLoad { LineTaut = previous.LineTensionNewtons > 0, TensionNewtons = previous.LineTensionNewtons });

            bool clutch = input.FreeLookHeld || input.RecenterHeld;
            // The mapper drops the first resumed frame. Do the same for auto feet/drag/cast.
            bool blocked = clutch || resumePending;
            resumePending = clutch;
            if (!blocked) drag = ControlMath.Clamp(drag + input.DragAdjustDelta, 0, 1);
            if (!autoBack || blocked || !environment.IsGrounded || input.MoveForward > 0 || input.MouseBackDelta < 0)
                backstep = 0;
            else if (input.MouseBackDelta > 0 && local.PitchRadians >= threshold)
            {
                double fraction = previousPitch >= threshold ? 1 :
                    ControlMath.Clamp((local.PitchRadians - threshold) / Math.Max(1e-12, local.PitchRadians - previousPitch), 0, 1);
                backstep = Math.Min(maxBack, input.MouseBackDelta / dt * gain * fraction);
            }
            else backstep = Math.Max(0, backstep - maxBack * dt / decay);
            previousPitch = local.PitchRadians;

            var direction = HorizontalDirection(environment.PlayerPosition, previous.FishPosition);
            // Native S already supplies its own retreat; only fill any missing amount.
            double nativeBack = Math.Max(0, -ControlMath.Clamp(input.MoveForward, -1, 1));
            var movement = new MovementRequest {
                Active = environment.IsGrounded,
                ExtraForward = (float)-Math.Max(0, backstep - nativeBack), ExtraRight = 0,
                PullDirection = direction,
                AgainstPullScale = (float)Math.Max(minScale, 1 - load)
            };
            return new SharedIntent {
                InputSequence = input.Sequence, RodPitchRadians = (float)local.PitchRadians, RodYawRadians = (float)local.SideRadians,
                Reel01 = local.ReelHeld ? 1 : 0, Drag01 = (float)drag, Strike = local.StrikePressed,
                Cast = !blocked && input.CastPressed, FreeLook = input.FreeLookHeld, Movement = movement
            };
        }

        bool resumePending;
        public void Release()
        {
            if (mapper != null) mapper.Reset();
            mapper = null; released = true; backstep = 0; resumePending = false; stepSequence = 0;
        }
        SharedIntent Neutral(long sequence, bool cancel)
        {
            return new SharedIntent { InputSequence = sequence, Cancel = cancel, Drag01 = (float)drag, Movement = MovementRequest.None };
        }
        static bool ValidInput(RawInputFrame input)
        {
            return input.Sequence >= 0 && ControlMath.Finite(input.SampleTimeSeconds) && Scalar.IsFinite(input.DurationSeconds) && input.DurationSeconds >= 0 &&
                Scalar.IsFinite(input.MouseBackDelta) && Scalar.IsFinite(input.MouseRightDelta) &&
                Scalar.IsFinite(input.MoveRight) && Scalar.IsFinite(input.MoveForward) && Scalar.IsFinite(input.DragAdjustDelta);
        }
        static Vec3 HorizontalDirection(Vec3 from, Vec3 to)
        {
            double x = (double)to.X - from.X, z = (double)to.Z - from.Z;
            double length = Math.Sqrt(x * x + z * z);
            return length > 1e-8 ? new Vec3((float)(x / length), 0, (float)(z / length)) : Vec3.Zero;
        }
        static void ValidateFinite(object config)
        {
            foreach (var field in config.GetType().GetFields())
                if (field.FieldType == typeof(float) && !Scalar.IsFinite((float)field.GetValue(config)))
                    throw new ArgumentException("Non-finite shared setting: " + field.Name);
        }
    }
}
