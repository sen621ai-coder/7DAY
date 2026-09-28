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
                foreach(var listener in go.GetComponentsInChildren<AudioListener>(true))listener.enabled=false;
                camera.nearClipPlane=.05f;camera.farClipPlane=80;camera.fieldOfView=60;camera.aspect=4f/3;
                camera.depth=-10;
                // Match XUiC_CameraWindow's supported native world rendering path and layer mask.
                camera.renderingPath=RenderingPath.DeferredShading;camera.clearFlags=CameraClearFlags.SolidColor;
                camera.backgroundColor=Color.black;camera.cullingMask&=~(1<<8);
                go.SetActive(true);Follow(camera,mount);return camera;
            }
            catch{UnityEngine.Object.Destroy(go);throw;}
        }
        public static void Follow(Camera camera,Transform mount)
        {
            if(camera==null||mount==null)throw new InvalidOperationException("Camera mount unloaded");
            camera.transform.SetPositionAndRotation(mount.position,mount.rotation);
            if(!camera.gameObject.activeInHierarchy)throw new InvalidOperationException("Video camera is inactive");
        }
    }
}
