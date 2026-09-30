using UnityEngine;

namespace PZAEC.Fishing.Runtime
{
    public static class NativeHandProjection
    {
        // Native FP hands use CameraMatrixOverride; our world-space rod uses ordinary projection.
        // Keep depth, but transform transverse coordinates to preserve the hand's screen position.
        public static Vector3 ToWorld(Camera camera,float handFieldOfView,Vector3 hand)
        {
            if(camera==null||handFieldOfView<=1||handFieldOfView>=179)return hand;
            var local=camera.transform.InverseTransformPoint(hand);
            if(local.z<=0)return hand;
            var native=Matrix4x4.Perspective(handFieldOfView,camera.aspect,camera.nearClipPlane,camera.farClipPlane);
            var world=camera.projectionMatrix;
            local.x*=native.m00/world.m00;local.y*=native.m11/world.m11;
            return camera.transform.TransformPoint(local);
        }
    }
}
