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
            var range=camera.GetComponent<FeedBackground>()??camera.gameObject.AddComponent<FeedBackground>();
            float distance=Mathf.Clamp(observer!=null?observer.farClipPlane:1000f,200f,2800f);
            if(range.WorldDistance!=distance)
            {
                for(int i=0;i<32;i++)range.LayerDistances[i]=distance;
                range.LayerDistances[Constants.cLayerBackgroundImage]=0;
                camera.layerCullDistances=range.LayerDistances;range.WorldDistance=distance;
            }
            // One deferred render keeps colour and depth coherent. Only the native sky
            // layer can reach this plane; world layers retain their normal cull range.
            camera.farClipPlane=100000f;
            camera.backgroundColor=SkyManager.SkyColor;
        }
        public static void Render(Camera camera)
        {
            camera.Render();
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
        public float WorldDistance=-1;
        public readonly float[] LayerDistances=new float[32];
    }
}
