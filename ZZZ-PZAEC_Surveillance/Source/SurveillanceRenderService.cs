using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PZAEC.Surveillance
{
    public static class SurveillanceRenderService
    {
        sealed class Stream
        {
            public Guid Id;
            public Vector3i Position;
            public GameObject Object;
            public Camera Camera;
            public RenderTexture Texture;
            public Transform Parent;
            public float LastUse,LastRender;
        }
        static readonly HashSet<ScreenView> views=new HashSet<ScreenView>();
        static readonly Dictionary<Guid,Stream> streams=new Dictionary<Guid,Stream>();
        static WorldBase world;
        static int renderCursor;
        public static void Register(ScreenView view){if(view!=null)views.Add(view);}
        public static void Unregister(ScreenView view){if(view!=null)views.Remove(view);}
        static void Destroy(Stream stream)
        {
            if(stream==null)return;
            if(stream.Camera!=null)stream.Camera.targetTexture=null;
            if(stream.Object!=null)UnityEngine.Object.Destroy(stream.Object);
            if(stream.Texture!=null){stream.Texture.Release();UnityEngine.Object.Destroy(stream.Texture);}
        }
        public static void Clear()
        {
            foreach(var s in streams.Values)Destroy(s);streams.Clear();views.Clear();world=null;renderCursor=0;
        }
        static bool TryCamera(SurveillanceDevice device,out TileEntityPoweredTrigger tile,out Transform parent)
        {
            tile=null;parent=null;if(device==null||device.Kind!=SurveillanceDeviceKind.Camera||world==null)return false;
            tile=world.GetTileEntity(device.Position) as TileEntityPoweredTrigger;if(tile==null||!tile.IsPowered||tile.BlockTransform==null)return false;
            var controller=tile.BlockTransform.GetComponent<MotionSensorController>()??tile.BlockTransform.GetComponentInChildren<MotionSensorController>(true);
            if(controller==null)return false;parent=controller.GetCameraTransform();return parent!=null;
        }
        static Stream Acquire(SurveillanceDevice device,Transform parent)
        {
            Stream stream;
            if(streams.TryGetValue(device.Id,out stream)&&stream.Parent==parent&&stream.Camera!=null)return stream;
            if(stream!=null){Destroy(stream);streams.Remove(device.Id);}
            var template=Resources.Load("Prefabs/ElectricityCamera") as GameObject;if(template==null)return null;
            var go=UnityEngine.Object.Instantiate(template,parent) as GameObject;if(go==null)return null;
            go.name="PZAEC Surveillance Camera "+device.Id;go.transform.localPosition=Vector3.zero;go.transform.localRotation=Quaternion.identity;
            foreach(var listener in go.GetComponentsInChildren<AudioListener>(true))listener.enabled=false;
            var camera=go.GetComponent<Camera>()??go.GetComponentInChildren<Camera>(true);if(camera==null){UnityEngine.Object.Destroy(go);return null;}
            var texture=new RenderTexture(768,576,16,RenderTextureFormat.ARGB32){name="Surveillance feed "+device.Id,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};texture.Create();
            camera.nearClipPlane=.05f;camera.farClipPlane=80;camera.fieldOfView=60;camera.depth=-10;camera.renderingPath=RenderingPath.Forward;
            camera.targetTexture=texture;camera.enabled=false;
            stream=new Stream{Id=device.Id,Position=device.Position,Object=go,Camera=camera,Texture=texture,Parent=parent,LastUse=Time.realtimeSinceStartup};streams[device.Id]=stream;return stream;
        }
        static void Render(Stream stream)
        {
            var renderers=new List<Renderer>();
            try
            {
                foreach(var view in views.ToArray())
                {
                    if(view==null)continue;
                    if(view.Surface!=null&&view.Surface.enabled){view.Surface.enabled=false;renderers.Add(view.Surface);}
                    if(view.Status!=null){var r=view.Status.GetComponent<Renderer>();if(r!=null&&r.enabled){r.enabled=false;renderers.Add(r);}}
                }
                stream.Camera.Render();stream.LastRender=Time.realtimeSinceStartup;
            }
            catch(Exception e){Log.Warning("[Surveillance] Camera render deferred: "+e.Message);}
            finally{foreach(var r in renderers)if(r!=null)r.enabled=true;}
        }
        public static void Tick()
        {
            if(GameManager.IsDedicatedServer||SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)return;
            var current=GameManager.Instance?.World;if(current==null)return;
            if(world!=current){Clear();world=current;}
            var player=current.GetPrimaryPlayer();if(player==null)return;
            var active=new List<Tuple<ScreenView,Stream,string>>();float now=Time.realtimeSinceStartup;
            var nearby=new List<Tuple<ScreenView,float>>();
            foreach(var view in views.ToArray())
            {
                if(view==null||view.World!=world){views.Remove(view);continue;}
                float distance=(player.position-new Vector3(view.Position.x+.5f,view.Position.y+1.5f,view.Position.z+.5f)).sqrMagnitude;
                if(distance>100){view.Show(null,"待机");continue;}
                nearby.Add(Tuple.Create(view,distance));
            }
            var admitted=new HashSet<Guid>();
            foreach(var candidate in nearby.OrderBy(v=>v.Item2))
            {
                var view=candidate.Item1;
                if(!view.IsScreenOn()){view.Show(null,"监控屏无电或已关闭");continue;}
                string channel;Guid cameraId=view.SelectedCamera(out channel);
                if(cameraId==Guid.Empty){view.Show(null,"请选择摄像头");continue;}
                var device=SurveillanceClient.ById(cameraId);
                if(device==null){view.Show(null,channel+"\n摄像头已移除");continue;}
                TileEntityPoweredTrigger tile;Transform parent;
                if(!TryCamera(device,out tile,out parent))
                {
                    var loaded=world.GetTileEntity(device.Position)!=null;
                    view.Show(null,channel+" · "+device.Label+"\n"+(loaded?"摄像头无电":"监控区域未加载"));continue;
                }
                if(!admitted.Contains(cameraId)&&admitted.Count>=2){view.Show(null,"待机：观看名额已满");continue;}
                var stream=Acquire(device,parent);if(stream==null){view.Show(null,"正在连接画面");continue;}
                admitted.Add(cameraId);stream.LastUse=now;active.Add(Tuple.Create(view,stream,channel+" · "+device.Label));
            }
            if(active.Count>0)
            {
                var distinct=active.Select(v=>v.Item2).Distinct().ToArray();
                if(distinct.Length>0)
                {
                    renderCursor%=distinct.Length;var stream=distinct[renderCursor];
                    if(now-stream.LastRender>=.095f){Render(stream);renderCursor=(renderCursor+1)%distinct.Length;}
                }
                foreach(var item in active)item.Item1.Show(item.Item2.Texture,item.Item3);
            }
            foreach(var pair in streams.ToArray())if(now-pair.Value.LastUse>5){Destroy(pair.Value);streams.Remove(pair.Key);}
        }
    }
}
