using System;
using PZAEC.Fishing.Contracts;

namespace PZAEC.Fishing.Runtime
{
    public static class MovementBridge
    {
        // Preserve the native locomotion pipeline; only change its normalized desired axes.
        public static void Apply(ref float right,ref float forward,Vec3 worldRight,Vec3 worldForward,MovementRequest request)
        {
            if(!request.Active)return;
            if(!Scalar.IsFinite(request.ExtraRight)||!Scalar.IsFinite(request.ExtraForward)||!Scalar.IsFinite(request.AgainstPullScale)||!request.PullDirection.IsFinite)return;
            float r=right+Scalar.Clamp(request.ExtraRight,-1,1), f=forward+Scalar.Clamp(request.ExtraForward,-1,1);
            float magnitude=(float)Math.Sqrt(r*r+f*f);
            if(magnitude>1){r/=magnitude;f/=magnitude;}
            var pull=new Vec3(request.PullDirection.X,0,request.PullDirection.Z).Normalized;
            float pr=Vec3.Dot(worldRight,pull),pf=Vec3.Dot(worldForward,pull);
            float opposing=r*pr+f*pf;
            if(opposing<0)
            {
                float removed=opposing*(1-Scalar.Clamp(request.AgainstPullScale,0,1));
                r-=pr*removed;f-=pf*removed;
            }
            right=r;forward=f;
        }
    }
}
