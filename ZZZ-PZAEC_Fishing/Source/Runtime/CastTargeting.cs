using PZAEC.Fishing.Contracts;

namespace PZAEC.Fishing.Runtime
{
    public static class CastTargeting
    {
        public static bool TryFind(IWaterQuery water,Vec3 origin,Vec3 direction,Vec3 player,float maxDistance,float minDepth,out Vec3 target)
        {
            target=default(Vec3);
            if(water==null||!origin.IsFinite||!direction.IsFinite||!player.IsFinite||!Scalar.IsFinite(maxDistance)||maxDistance<=0||maxDistance>100||!Scalar.IsFinite(minDepth)||minDepth<=0)return false;
            direction=direction.Normalized;if(direction.Y>=-.01f)return false;
            Vec3 previous=origin;
            for(float distance=.25f;distance<=maxDistance;distance+=.25f) {
                var point=origin+direction*distance;
                var hit=water.TraceSolid(previous,point);if(hit.Obstructed||hit.Unloaded)return false;
                var sample=water.SampleColumn(point,.5f,1);
                if(sample.IsValid&&point.Y<=sample.SurfacePoint.Y&&previous.Y>=sample.SurfacePoint.Y) {
                    float t=(sample.SurfacePoint.Y-origin.Y)/direction.Y;target=origin+direction*t;
                    var depth=water.SampleColumn(target,.5f,16);
                    return depth.IsValid&&depth.DepthMeters>=minDepth&&(target-player).Length<=maxDistance;
                }
                previous=point;
            }
            return false;
        }
    }
}
