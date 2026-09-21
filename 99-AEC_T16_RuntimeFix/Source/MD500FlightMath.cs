using System;

namespace AECT16RuntimeFix
{
    // Pure control law: SI units, independent of Unity and render frame rate.
    public static class MD500FlightMath
    {
        public const float ClimbSpeed = 4f;
        public const float DescentSpeed = 3f;
        public const float Ceiling = 280f;
        public const float FullLiftRotorFraction = .75f;

        public struct Output
        {
            public float Forward, Right, Up, Yaw;
        }

        public struct Attitude
        {
            public float Pitch, Bank;
        }

        // These are handling profiles, not weapon/aiming modes.
        public struct Profile
        {
            public float Acceleration, LateralAcceleration, Jerk, LateralJerk, YawRate, YawJerk, VerticalJerk;
            public float PitchMax, BankMax, CruisePitch, PitchGain, AttitudeGain, AttitudeDamping;
        }

        public static Profile Handling(bool apache)
        {
            return apache ? new Profile {
                Acceleration=4f, LateralAcceleration=6f, Jerk=3f, LateralJerk=10f, YawRate=.78f, YawJerk=4f, VerticalJerk=8f,
                PitchMax=12f, BankMax=30f, CruisePitch=3.5f, PitchGain=.38f, AttitudeGain=5f, AttitudeDamping=3.6f
            } : new Profile {
                Acceleration=4f, LateralAcceleration=7f, Jerk=4f, LateralJerk=12f, YawRate=.95f, YawJerk=5f, VerticalJerk=10f,
                PitchMax=14f, BankMax=34f, CruisePitch=4f, PitchGain=.42f, AttitudeGain=6f, AttitudeDamping=3.4f
            };
        }

        public static float SmoothAxis(float previous, float target, float seconds, float limit, float jerk)
        {
            limit = Math.Max(0f, limit);
            float step = Math.Max(0f, jerk) * Clamp(seconds, 0f, .1f);
            return Clamp(previous + Clamp(target - previous, -step, step), -limit, limit);
        }

        public static float LateralLimit(float power,float torqueScale,Profile profile)
        {
            return Math.Min(profile.LateralAcceleration * Clamp(torqueScale,.25f,3f) * Clamp(power,0f,1f),
                9.81f * (float)Math.Tan(profile.BankMax * Math.PI / 180));
        }

        public static float TurnRateLimit(float horizontalSpeed, float power, float torqueScale, Profile profile)
        {
            // Reserve lateral authority for existing slip and transient disturbances.
            return Math.Min(profile.YawRate, .85f * LateralLimit(power,torqueScale,profile) / Math.Max(1f, Math.Abs(horizontalSpeed)));
        }

        public static float DragAcceleration(float velocity, float perStepDrag, float seconds)
        {
            // The native simulation multiplies velocity before our force callback.
            float drag = Clamp(perStepDrag,.01f,1f);
            return velocity * (1f / drag - 1f) / Math.Max(.001f, seconds);
        }

        public static Attitude TargetAttitude(float forwardVelocity, float forwardInput, float forwardThrust,
            float netForwardAcceleration, float lateralThrust, float climbVelocity, bool airborne, bool turbo, Profile profile)
        {
            if (!airborne) return new Attitude();
            float speedFade = Clamp(Math.Abs(forwardVelocity)/2f,0f,1f);
            float brake = forwardVelocity * forwardThrust < 0f ? Clamp(Math.Abs(forwardThrust)/.6f,0f,1f) : 0f;
            float cruise = Clamp(forwardVelocity/25f,-1f,1f) * profile.CruisePitch * (turbo ? 1.35f : 1f);
            cruise *= (Math.Abs(forwardInput)<.01f ? .15f : 1f) * (1f-brake);
            // Climb softens sustained cruise lean; it never commands nose-up by itself.
            cruise *= 1f - .25f * Clamp(climbVelocity/ClimbSpeed,0f,1f);
            float pitch = cruise + (float)(Math.Atan(netForwardAcceleration/9.81f)*180/Math.PI)*profile.PitchGain;
            // Fade braking near rest, without suppressing the next reverse acceleration.
            if (brake>0f) pitch *= .25f + .75f*speedFade;
            return new Attitude {
                Pitch=Clamp(pitch,-10f,turbo ? profile.PitchMax : profile.PitchMax-2f),
                Bank=Clamp((float)(Math.Atan(lateralThrust/9.81f)*180/Math.PI),-profile.BankMax,profile.BankMax)
            };
        }

        public static Attitude TargetAttitude(float forwardVelocity, float yawVelocity,
            float forwardAcceleration, bool airborne, bool turbo)
        {
            if (!airborne) return new Attitude();
            return TargetAttitude(forwardVelocity,1f,forwardAcceleration,forwardAcceleration,
                yawVelocity*forwardVelocity,0f,airborne,turbo,Handling(false));
        }

        public static float Clamp(float value, float min, float max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        public static float FuelSpeed(float speed, float maxForward)
        {
            // Native idle already charges this minimum. Also charge it while
            // accelerating vertically or pushing against a wall at zero speed.
            return Math.Max(speed, Math.Max(0f, maxForward) * .1f);
        }

        public static float LiftPower(float rotorPower)
        {
            // Rotor RPM is an animation/engine fraction rather than a calibrated
            // lift coefficient. Flight RPM begins below the nominal rpmMax value.
            return Clamp(rotorPower / FullLiftRotorFraction, 0f, 1f);
        }

        public static float SmoothThrust(float previous, float target, float seconds,
            float power, float torqueScale)
        {
            // Limit acceleration change to 4 m/s^3 at base motor torque.
            // Power loss remains immediate rather than preserving stored thrust.
            float scale = Clamp(torqueScale, .25f, 3f);
            float limit = 4f * scale * Clamp(power, 0f, 1f);
            float step = 4f * scale * Clamp(seconds, 0f, .1f);
            return Clamp(previous + Clamp(target - previous, -step, step), -limit, limit);
        }

        public static Output Calculate(float forward, float turn, bool up, bool down,
            float forwardVelocity, float rightVelocity, float verticalVelocity,
            float yawVelocity, float altitude, float rotorPower,
            float maxForward, float maxBackward, float torqueScale)
        {
            return Calculate(forward, turn, up, down, forwardVelocity, rightVelocity,
                verticalVelocity, yawVelocity, altitude, altitude, false, rotorPower,
                maxForward, maxBackward, torqueScale);
        }

        public static Output Calculate(float forward, float turn, bool up, bool down,
            float forwardVelocity, float rightVelocity, float verticalVelocity,
            float yawVelocity, float altitude, float holdAltitude, bool altitudeHold,
            float rotorPower, float maxForward, float maxBackward, float torqueScale)
        {
            return Calculate(forward,turn,up,down,forwardVelocity,rightVelocity,verticalVelocity,
                yawVelocity,altitude,holdAltitude,altitudeHold,rotorPower,maxForward,maxBackward,torqueScale,Handling(false));
        }

        public static Output Calculate(float forward, float turn, bool up, bool down,
            float forwardVelocity, float rightVelocity, float verticalVelocity,
            float yawVelocity, float altitude, float holdAltitude, bool altitudeHold,
            float rotorPower, float maxForward, float maxBackward, float torqueScale, Profile profile)
        {
            float power = Clamp(rotorPower, 0f, 1f);
            if (power <= 0f) return new Output();
            float liftPower = LiftPower(power);
            forward = Clamp(forward, -1f, 1f);
            turn = Clamp(turn, -1f, 1f);
            float verticalTarget = up == down ? 0f : (up ? ClimbSpeed : -DescentSpeed);
            if (altitudeHold && up == down)
                verticalTarget = Clamp((holdAltitude - altitude) * .8f, -1.5f, 1.5f);
            // Ease out a climb before the author's original absolute ceiling.
            if (verticalTarget > 0f)
                verticalTarget *= Clamp((Ceiling - altitude) / 4f, 0f, 1f);
            if (altitude > Ceiling)
                verticalTarget = Math.Min(verticalTarget, -Math.Min(DescentSpeed, altitude - Ceiling));
            float target = forward * Math.Max(0f, forward >= 0f ? maxForward : maxBackward);
            float accelerationLimit = profile.Acceleration * Clamp(torqueScale, .25f, 3f);
            float lateralLimit = LateralLimit(power,torqueScale,profile);
            float horizontalSpeed = (float)Math.Sqrt(forwardVelocity*forwardVelocity + rightVelocity*rightVelocity);
            float yawTarget = turn * TurnRateLimit(horizontalSpeed,power,torqueScale,profile);
            bool coasting = Math.Abs(forward) < .01f;
            float forwardLimit = coasting ? accelerationLimit * .3f : accelerationLimit;
            return new Output {
                Forward = Clamp((target - forwardVelocity) * (coasting ? .45f : 1.5f), -forwardLimit, forwardLimit) * power,
                // Signed forward velocity also gives the correct centripetal force in reverse.
                Right = Clamp(forwardVelocity*yawVelocity - rightVelocity * 2f, -lateralLimit, lateralLimit),
                Up = (9.81f + Clamp((verticalTarget - verticalVelocity) * 2.5f, -6f, 6f)) * liftPower,
                Yaw = Clamp((yawTarget - yawVelocity) * 4f, -2f, 2f) * power
            };
        }
    }
}
