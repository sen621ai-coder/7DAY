using System;
using UnityEngine;

namespace PZAEC.Surveillance
{
    public static class FeedCamera
    {
        static GameObject template;
        public static Camera Create(Transform mount,string name)
        {
            if(template==null)template=Resources.Load("Prefabs/ElectricityCamera") as GameObject;
            if(template==null)throw new InvalidOperationException("Native ElectricityCamera prefab unavailable");
            // MotionSensorController.GetCameraTransform() returns Cone, which is normally inactive.
            // A child camera inherits that inactive state even when its own activeSelf is true.
            var go=UnityEngine.Object.Instantiate(template);go.name=name;
            try
            {
                var camera=go.GetComponent<Camera>();
                if(camera==null)throw new InvalidOperationException("Native ElectricityCamera has no root Camera");
                camera.enabled=false;
                // The prefab's decorative turret filter alters colours and reads a shared
                // back-buffer material. Surveillance needs a clean, independently sampled feed.
                var turretEffect=go.GetComponent("ImageEffect_TurretView") as Behaviour;
                if(turretEffect!=null)turretEffect.enabled=false;
                foreach(var listener in go.GetComponentsInChildren<AudioListener>(true))listener.enabled=false;
                camera.nearClipPlane=.05f;camera.farClipPlane=80;camera.fieldOfView=60;camera.aspect=4f/3;
                camera.depth=-10;
                // Match XUiC_CameraWindow's supported native world rendering path and layer mask.
                camera.renderingPath=RenderingPath.DeferredShading;camera.clearFlags=CameraClearFlags.SolidColor;
                // XUiC_CameraWindow excludes layer 9 (-513); vp_FPWeapon uses layer 10.
                // Retain our existing layer 8 exclusion too.
                camera.backgroundColor=Color.black;camera.cullingMask&=~((1<<8)|(1<<9)|(1<<10));
                go.SetActive(true);Follow(camera,mount);return camera;
            }
            catch{UnityEngine.Object.Destroy(go);throw;}
        }
        public static void Follow(Camera camera,Transform mount)
        {
            if(camera==null||mount==null)throw new InvalidOperationException("Camera mount unloaded");
            // The sensor cone can roll with the block/model hierarchy. Keep its aim,
            // but level the video horizon against the world's vertical axis.
            var forward=mount.forward;
            var up=Vector3.up;
            if(Mathf.Abs(Vector3.Dot(forward,up))>.995f)
            {
                // Looking almost straight up/down has no stable world-up projection.
                // Retain the mount's local up there, so LookRotation stays defined.
                up=Vector3.ProjectOnPlane(mount.up,forward);
                if(up.sqrMagnitude<.0001f)up=Vector3.ProjectOnPlane(mount.right,forward);
            }
            camera.transform.SetPositionAndRotation(mount.position,Quaternion.LookRotation(forward,up));
            if(!camera.gameObject.activeInHierarchy)throw new InvalidOperationException("Video camera is inactive");
        }
    }
}
