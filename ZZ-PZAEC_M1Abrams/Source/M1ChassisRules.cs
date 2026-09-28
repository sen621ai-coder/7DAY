using System;
namespace PZAEC.M1
{
    // Pure policy: metres, seconds and m/s. No biome or surface-material tables.
    public static class ChassisRules
    {
        public const float StepHeight=.60f,AssistSpeed=8f/3.6f;
        public static float SteeringLimit(float speed)
        {return 32-18*Math.Min(1,Math.Max(0,(speed-15f/3.6f)/(45f/3.6f-15f/3.6f)));}
        // radians/sec, bounded by a 4 m/s² lateral-acceleration target while
        // moving. Unlike the old assist this does not vanish at 14.4 km/h.
        public static float YawRate(float speed,float forwardSpeed,float throttle,float steer,float tierTurn,float damage)
        {
            float direction=forwardSpeed<-.5f?-1:forwardSpeed>.5f?1:throttle<-.1f?-1:1;
            float rate=Math.Min(tierTurn*1.5f*(float)Math.PI/180*damage,4f/Math.Max(1,speed));
            return Math.Max(-1,Math.Min(1,steer))*rate*direction;
        }
        public static float YawAcceleration(float target,float actual)
        {return Math.Max(-.8f,Math.Min(.8f,(target-actual)*3));}
        public static bool CanAssist(float speed,float throttle,float steer,float pitch,float roll,bool bothGrounded,bool braking)
        {return bothGrounded&&!braking&&speed<AssistSpeed&&throttle>.2f&&Math.Abs(steer)<.35f&&Math.Abs(pitch)<18&&Math.Abs(roll)<12;}
        public static bool Landing(float height,float farHeight,float normalUp)
        {return height>=.12f&&height<=StepHeight&&Math.Abs(farHeight-height)<=.12f&&normalUp>=.9f;}
        public sealed class Attempt
        {
            public bool Active,Blocked;
            float elapsed,still,best,blockedAt;
            public bool Tick(bool eligible,float dt,float travel)
            {
                if(Blocked){if(travel<=blockedAt-.5f)Blocked=false;else return false;}
                if(!eligible){if(Active)Block(travel);return false;}
                if(!Active){Active=true;elapsed=still=0;best=travel;}
                elapsed+=dt;
                if(travel>=best+.03f){best=travel;still=0;}else still+=dt;
                if(elapsed>=2||still>=.6f){Block(travel);return false;}
                return true;
            }
            void Block(float travel){Active=false;Blocked=true;blockedAt=travel;}
        }
    }
}
