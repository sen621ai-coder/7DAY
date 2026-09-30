using System;
using PZAEC.Fishing.Contracts;

namespace PZAEC.Fishing.Presentation
{
    // Display state only. Integration avoids absolute-time * changing-frequency discontinuities.
    public sealed class PresentationMotion
    {
        double lastTime;
        float lastLength;
        bool initialized;
        public float SwimPhase {get;private set;}
        public float SwimAmplitudeDegrees {get;private set;}
        public float HandleDegrees {get;private set;}
        public float SpoolDegrees {get;private set;}
        public float TakeupMetersPerSecond {get;private set;}
        public float PayoutMetersPerSecond {get;private set;}
        public void Reset(){initialized=false;lastTime=0;lastLength=0;SwimPhase=SwimAmplitudeDegrees=HandleDegrees=SpoolDegrees=TakeupMetersPerSecond=PayoutMetersPerSecond=0;}
        public void Step(double time,float speed,bool burst,float length)
        {
            if(double.IsNaN(time)||double.IsInfinity(time)||!Scalar.IsFinite(speed)||!Scalar.IsFinite(length))return;
            if(!initialized){initialized=true;lastTime=time;lastLength=length;return;}
            double elapsed=time-lastTime;
            if(elapsed<=0){if(elapsed<0){lastTime=time;lastLength=length;}return;}
            float dt=(float)Math.Min(.1,elapsed),v=Scalar.Clamp(speed,0,8);
            SwimPhase=(SwimPhase+dt*(.6f+v*1.4f)*(float)(Math.PI*2))%(float)(Math.PI*2);
            float amplitude=Scalar.Clamp(v*2.4f+(burst?3:0),.5f,16);
            SwimAmplitudeDegrees+=(amplitude-SwimAmplitudeDegrees)*(1-(float)Math.Exp(-dt*8));
            float delta=lastLength-length;
            // Network corrections/teleports must not spin the handle several turns instantly.
            bool continuous=elapsed<=.25&&Math.Abs(delta)<=Math.Max(.05,elapsed*8);
            if(continuous)
            {
                float take=Math.Max(0,delta),pay=Math.Max(0,-delta);
                HandleDegrees=(HandleDegrees+take/.78f*360)%360;
                SpoolDegrees=(SpoolDegrees-pay/.15f*360)%360;
                TakeupMetersPerSecond=take/(float)elapsed;PayoutMetersPerSecond=pay/(float)elapsed;
            }
            else TakeupMetersPerSecond=PayoutMetersPerSecond=0;
            lastTime=time;lastLength=length;
        }
    }
}
