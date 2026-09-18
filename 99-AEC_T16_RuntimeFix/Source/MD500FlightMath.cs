using System;

namespace AECT16RuntimeFix
{
    // Pure control law: SI units, independent of Unity and render frame rate.
    public static class MD500FlightMath
    {
        public const float ClimbSpeed = 4f;
        public const float DescentSpeed = 3f;
        public const float Ceiling = 280f;

        public struct Output
        {
            public float Forward, Right, Up, Yaw;
        }

        public struct Attitude
        {
            public float Pitch, Bank;
        }

        public static Attitude TargetAttitude(float forwardVelocity, float yawVelocity,
            float forwardAcceleration, bool airborne, bool turbo)
        {
            if (!airborne) return new Attitude();
            // Positive pitch lowers the nose; positive bank leans right.
            // Acceleration creates a transient tilt; cruise retains a smaller lean.
            float cruise = Clamp(forwardVelocity / 25f, -1f, 1f) * (turbo ? 9f : 6f);
            return new Attitude {
                Pitch = Clamp(cruise + forwardAcceleration * 2f, -10f, turbo ? 14f : 12f),
                // Bank into actual turns, smoothly reducing bank near a hover.
                Bank = Clamp(yawVelocity * Math.Abs(forwardVelocity) * 1.2f, -14f, 14f)
            };
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
            float power = Clamp(rotorPower, 0f, 1f);
            if (power <= 0f) return new Output();
            forward = Clamp(forward, -1f, 1f);
            turn = Clamp(turn, -1f, 1f);
            float verticalTarget = up == down ? 0f : (up ? ClimbSpeed : -DescentSpeed);
            // Ease out a climb before the author's original absolute ceiling.
            if (verticalTarget > 0f)
                verticalTarget *= Clamp((Ceiling - altitude) / 4f, 0f, 1f);
            if (altitude > Ceiling)
                verticalTarget = Math.Min(verticalTarget, -Math.Min(DescentSpeed, altitude - Ceiling));
            float target = forward * Math.Max(0f, forward >= 0f ? maxForward : maxBackward);
            float accelerationLimit = 4f * Clamp(torqueScale, .25f, 3f);
            bool coasting = Math.Abs(forward) < .01f;
            float forwardLimit = coasting ? accelerationLimit * .3f : accelerationLimit;
            return new Output {
                Forward = Clamp((target - forwardVelocity) * (coasting ? .45f : 1.5f), -forwardLimit, forwardLimit) * power,
                Right = Clamp(-rightVelocity * 2f, -accelerationLimit, accelerationLimit) * power,
                Up = (9.81f + Clamp((verticalTarget - verticalVelocity) * 2.5f, -6f, 6f)) * power,
                Yaw = Clamp((turn * .65f - yawVelocity) * 3f, -2f, 2f) * power
            };
        }
    }
}
