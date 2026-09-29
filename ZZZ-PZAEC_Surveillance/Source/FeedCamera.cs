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
                camera.nearClipPlane=.05f;camera.fieldOfView=60;camera.aspect=4f/3;
                camera.depth=-10;
                // The native turret window deliberately excludes its background. A world
                // monitor needs that background, including layer 9's sky and cloud meshes.
                camera.renderingPath=RenderingPath.DeferredShading;camera.clearFlags=CameraClearFlags.SolidColor;
                ApplyEnvironment(camera,null);
                go.SetActive(true);Follow(camera,mount);return camera;
            }
            catch{UnityEngine.Object.Destroy(go);throw;}
        }
        public static void ApplyEnvironment(Camera camera,Camera observer)
        {
            int mask=observer!=null?observer.cullingMask:~0;
            mask|=(1<<Constants.cLayerNoShadow)|(1<<Constants.cLayerBackgroundImage);
            camera.cullingMask=mask&~((1<<Constants.cLayerHoldingItem)|(1<<Constants.cLayerNGUI)|(1<<Constants.cLayerRenderInTexture));
            camera.farClipPlane=Mathf.Clamp(observer!=null?observer.farClipPlane:1000f,200f,2800f);
            camera.backgroundColor=SkyManager.SkyColor;
        }
        public static void Render(Camera camera)
        {
            int skyMask=1<<Constants.cLayerBackgroundImage;
            if((camera.cullingMask&skyMask)==0){camera.Render();return;}
            // Native sky spheres are ~45 km in radius. Render only their layer with a
            // long clip plane, then the world at normal range into the SAME texture.
            var background=camera.GetComponent<FeedBackground>()??camera.gameObject.AddComponent<FeedBackground>();
            if(background.Sky==null)
            {
                var go=new GameObject("Surveillance sky");go.transform.SetParent(camera.transform,false);
                background.Sky=go.AddComponent<Camera>();background.Sky.enabled=false;
            }
            var sky=background.Sky;int mask=camera.cullingMask;var clear=camera.clearFlags;
            var previous=RenderTexture.active;
            try
            {
                sky.CopyFrom(camera);sky.enabled=false;sky.targetTexture=camera.targetTexture;sky.cullingMask=skyMask;
                sky.nearClipPlane=.3f;sky.farClipPlane=100000f;sky.renderingPath=RenderingPath.Forward;
                sky.clearFlags=CameraClearFlags.SolidColor;sky.Render();
                camera.cullingMask=mask&~skyMask;camera.clearFlags=CameraClearFlags.Depth;
                camera.Render();
            }
            finally{sky.targetTexture=null;camera.cullingMask=mask;camera.clearFlags=clear;RenderTexture.active=previous;}
        }
        public static void Follow(Camera camera,Transform mount)
        {
            if(camera==null||mount==null)throw new InvalidOperationException("Camera mount unloaded");
            // Native turret_cone mesh extends along local -Z, towards the lens front.
            // Transform.forward (+Z) points behind this sensor. Keep the horizon level
            // while following the actual cone aim through yaw, pitch and block rotation.
            var forward=-mount.forward;
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
    public sealed class FeedBackground : MonoBehaviour
    {
        public Camera Sky;
    }
}
