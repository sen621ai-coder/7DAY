using System;
namespace AECT16RuntimeFix
{
    public struct ForestrySlice { public float x,z,length; }
    // Physical distances are shared by the visual cycle and its offline checks.
    public static class ForestryMotion
    {
        public const float FeedSpeed=.207f, RollerRadius=.065f, SawDegrees=720f;
        public const float Entry=-4.25f, Exit=-1.35f, Cut=-2.7f, BoardLength=1.1f;
        public const float Cycle=Exit-Entry+BoardLength;
        public static float Approach(float current,float target,float dt)
        {
            float step=Math.Max(0,dt)*(target>current?1.25f:.85f);
            return current<target?Math.Min(target,current+step):Math.Max(target,current-step);
        }
        public static float RollerDegrees(float metres) { return -metres/RollerRadius*180f/(float)Math.PI; }
        public static float DriveDegrees(float sawDegrees) { return sawDegrees*.14f/.11f; }
        public static ForestrySlice Slice(float travel,int board,int part)
        {
            float offset=(travel+board*Cycle*.5f)%Cycle;if(offset<0)offset+=Cycle;
            float head=Entry+offset,tail=head-BoardLength;
            float lo=Math.Max(tail,part==0?Entry:Cut),hi=Math.Min(head,part==0?Cut:Exit);
            float length=Math.Max(0,hi-lo),x=(lo+hi)*.5f;
            float separation=.0675f+.02f*Math.Max(0,Math.Min(1,(x-Cut)/(Exit-Cut)));
            return new ForestrySlice{x=x,z=-2.16f+(part==0?0:part==1?-separation:separation),length=length};
        }
    }
}
