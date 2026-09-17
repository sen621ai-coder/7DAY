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
            return new Output {
                Forward = Clamp((target - forwardVelocity) * 1.5f, -accelerationLimit, accelerationLimit) * power,
                Right = Clamp(-rightVelocity * 2f, -accelerationLimit, accelerationLimit) * power,
                Up = (9.81f + Clamp((verticalTarget - verticalVelocity) * 2.5f, -6f, 6f)) * power,
                Yaw = Clamp((turn * .65f - yawVelocity) * 3f, -2f, 2f) * power
            };
        }
    }
}
