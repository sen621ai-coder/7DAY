using System;

namespace AECT16RuntimeFix
{
    // Bounded presentation and a balanced, main-weapon-only angular pulse.
    public static class ApacheFeedbackMath
    {
        public const float CameraScale=.0006f, Duration=.25f;
        public const float AimRollDegrees=.045f;
        public static float AimRoll(float age)
        {return age<0||age>Duration?0:AimRollDegrees*(float)(Math.Sin(age*70)*Math.Exp(-age/.055));}
        public const float RecoilDuration=.18f, RecoilCooldown=.22f, RecoilAcceleration=.45f;
        private static float RecoilVelocity(float age)
        {
            if(age<=0 || age>=RecoilDuration)return 0;
            double frequency=2*Math.PI/RecoilDuration;
            return (float)(RecoilAcceleration/frequency*Math.Sin(frequency*age));
        }
        // Integrate each fixed step exactly, including the final partial step.
        // The waveform returns both angular velocity and (continuously) angle to zero.
        public static float RecoilStep(float age,float dt)
        {return dt<=0 || age<0 || age>=RecoilDuration?0:(RecoilVelocity(age+dt)-RecoilVelocity(age))/dt;}
        public static float CameraEnvelope(float energy,float age)
        {return age<0||age>Duration?0f:Math.Max(0f,Math.Min(1f,energy))*(float)Math.Exp(-age/.05f);}
    }
}
