using System;
namespace PZAEC.M1
{
    // Pure policy: metres, seconds and m/s. No biome or surface-material tables.
    public static class ChassisRules
    {
        public const float StepHeight=.60f,AssistSpeed=8f/3.6f,ReverseSpeed=4f/3.6f;
        public static float HeightLimit(bool reverse)=>reverse?.30f:StepHeight;
        public static float SpeedLimit(bool reverse)=>reverse?ReverseSpeed:AssistSpeed;
        public static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
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
        {return Finite(speed)&&Finite(throttle)&&Finite(steer)&&Finite(pitch)&&Finite(roll)&&bothGrounded&&!braking&&speed>=0&&speed<SpeedLimit(throttle<0)&&Math.Abs(throttle)>.2f&&Math.Abs(steer)<.6f&&Math.Abs(pitch)<28&&Math.Abs(roll)<15;}
        public static bool Landing(float height,float farHeight,float normalUp)
        {return height>=.12f&&height<=StepHeight&&Math.Abs(farHeight-height)<=.12f&&normalUp>=.9f;}
        public static bool CanContinue(float speed,float throttle,float steer,float pitch,float roll,bool grounded,bool braking)
        {return Finite(speed)&&speed>=0&&CanAssist(0,throttle,steer,pitch,roll,grounded,braking);}
        public static float TractionScale(float speed,float steer,float roll,bool reverse)
        {return Math.Max(0,Math.Min(1,(SpeedLimit(reverse)-speed)/.6f))*Math.Max(0,1-Math.Abs(steer)/.6f)*Math.Max(0,Math.Min(1,(15-Math.Abs(roll))/5))*(reverse?.6f:1);}
        public enum Stage { Approach, Climbing, Crossing, Leaving, Complete, Failed }
        public enum Exit { None, Unsafe, LandingLost, Stalled, Timeout }
        public sealed class Attempt
        {
            public bool Active,Blocked;
            public Stage Phase;
            public Exit Reason;
            float elapsed,window,windowStart,blockedAt,missing,limit=6;
            public void Begin(float duration){Active=true;Blocked=false;Phase=Stage.Approach;Reason=Exit.None;elapsed=window=windowStart=missing=0;limit=Math.Max(3,Math.Min(6,duration));}
            public void Abort(float travel){if(Active)Block(travel);}
            public bool Advance(bool safe,bool geometry,float dt,float travel,float edge)
            {
                if(!Active)return false;
                if(!Finite(dt)||dt<=0||dt>.15f||!Finite(travel)||!safe){Reason=Exit.Unsafe;Block(travel);return false;}
                elapsed+=dt;window+=dt;
                if(!geometry){missing+=dt;if(missing>=.15f){Reason=Exit.LandingLost;Block(travel);}return false;}
                missing=0;
                Phase=travel<edge-2.15f?Stage.Approach:travel<edge?Stage.Climbing:travel<edge+2.15f?Stage.Crossing:Stage.Leaving;
                if(travel>=edge+2.6f){Active=false;Phase=Stage.Complete;return false;}
                if(window>=.8f){if(travel-windowStart<.03f){Reason=Exit.Stalled;Block(travel);return false;}window=0;windowStart=travel;}
                if(elapsed>=limit){Reason=Exit.Timeout;Block(travel);return false;}return true;
            }
            public void Retreat(float travel){if(Blocked&&travel<=blockedAt-.5f){Blocked=false;Phase=Stage.Approach;}}
            public bool Tick(bool eligible,float dt,float travel)
            {
                Retreat(travel);if(Blocked)return false;if(!Active&&eligible)Begin(6);
                return Advance(eligible,eligible,dt,travel,100);
            }
            void Block(float travel){Active=false;Blocked=true;blockedAt=Finite(travel)?travel:0;Phase=Stage.Failed;}
        }
    }
}
