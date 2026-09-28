using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace PZAEC.Surveillance
{
    public static class SurveillanceRenderService
    {
        sealed class Stream
        {
            public Guid Id;
            public GameObject Object;
            public Camera Camera;
            public RenderTexture Texture,Retired;
            public Transform Parent;
            public readonly FeedClock Clock=new FeedClock();
            public readonly QualityGate Quality=new QualityGate();
            public float LastUse,AdmittedAt,LogAt,WakeAt;
            public bool Active,WasActive,Focused;
            public int RequestedTier,Hz,Frames,Errors;
            public double CpuMs,ActualHz;
            public string Status="";
            public float StatusAt;
            public bool Markers;
            public TargetMarkerDetector Detector;
            public double MarkerFrame=-1;
            public float MarkerLogAt;
        }
        sealed class Watch
        {
            public ScreenView View;
            public bool Focused;
            public readonly ViewGate Gate=new ViewGate();
            public float Distance,Height,Area,StateAt;
            public Guid Id;
            public Transform Parent;
            public string Caption="",Error="";
            public string Channel,Label;
            public int Tier=-1;
            public bool Markers;
            public Stream Feed;
        }
        static readonly List<Watch> views=new List<Watch>();
        static readonly List<Watch> candidates=new List<Watch>();
        static readonly Dictionary<Guid,Stream> streams=new Dictionary<Guid,Stream>();
        static readonly List<Stream> active=new List<Stream>(4);
        static readonly List<FeedClock> clocks=new List<FeedClock>(4);
        static readonly List<Guid> removed=new List<Guid>();
        static readonly List<Renderer> hidden=new List<Renderer>();
        static readonly Plane[] planes=new Plane[6];
        static readonly StringBuilder samples=new StringBuilder();
        static readonly Comparison<Watch> compare=Compare;
        static WorldBase world;
        static GameObject cameraTemplate;
        static ScreenView focus,pendingFocus;
        static float focusSince,statsAt,statsSince,sortNow,badSince=-1,goodSince=-1;
        static int pressure,frameCount;
        static double frameSum;
        static float frameMax,frameEma;
        static bool createdThisFrame;
        public static bool AutomaticDegrade;
        public static bool Diagnostics;
        public static string Summary="";
        public static string LastExport="";
        public static void Register(ScreenView view)
        {if(view==null)return;foreach(var w in views)if(w.View==view)return;views.Add(new Watch{View=view});}
        public static void Unregister(ScreenView view)
        {for(int i=views.Count-1;i>=0;i--)if(views[i].View==view)views.RemoveAt(i);}
        static void Release(RenderTexture texture)
        {if(texture!=null){texture.Release();UnityEngine.Object.Destroy(texture);}}
        static void Destroy(Stream s)
        {
            if(s.Camera!=null)s.Camera.targetTexture=null;
            if(s.Object!=null)UnityEngine.Object.Destroy(s.Object);
            Release(s.Texture);Release(s.Retired);
        }
        static void ClearStreams(){foreach(var s in streams.Values)Destroy(s);streams.Clear();active.Clear();clocks.Clear();}
        public static void Clear()
        {
            Export();ClearStreams();views.Clear();world=null;focus=pendingFocus=null;
            pressure=frameCount=0;badSince=goodSince=-1;frameSum=0;frameMax=frameEma=0;statsAt=0;Summary="";
        }
        public static void Export()
        {
            if(samples.Length==0&&!RenderDiagnostics.HasData)return;
            try
            {
                string directory=Path.Combine(GameIO.GetSaveGameDir(),"SurveillanceDiagnostics");Directory.CreateDirectory(directory);
                LastExport=Path.Combine(directory,"render-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".csv");
                File.WriteAllText(LastExport,"seconds,id,active,totalStreams,width,targetHz,successfulFrames,failures,frameAgeMs,renderCpuMs,gameFrameMeanMs,gameFrameMaxMs,pressure\n"+samples,Encoding.UTF8);
                RenderDiagnostics.Export(Path.ChangeExtension(LastExport,"events.csv"));
                samples.Length=0;Log.Out("[Surveillance] CPU/render diagnostics exported: "+LastExport);
            }
            catch(Exception e){Log.Warning("[Surveillance] Diagnostics export failed: "+e.Message);}
        }
        // All geometry is in Unity render coordinates, including floating-origin shifts.
        static bool Observe(Watch w,Camera observer,float now)
        {
            var surface=w.View.Surface;if(surface==null)return false;
            var t=surface.transform;Vector3 eye=observer.transform.position;
            var local=t.InverseTransformPoint(eye);
            Vector3 nearest=t.TransformPoint(new Vector3(Mathf.Clamp(local.x,-.5f,.5f),Mathf.Clamp(local.y,-.5f,.5f),.5f));
            w.Distance=Vector3.Distance(eye,nearest);
            if(w.Distance>(w.Gate.Watching?10:8))return w.Gate.Update(w.Distance,false,now);
            bool visible=Vector3.Dot(t.forward,eye-t.position)>0&&GeometryUtility.TestPlanesAABB(planes,surface.bounds);
            w.Focused=false;
            if(visible)
            {
                float minX=1,maxX=0,minY=1,maxY=0;bool near=false;
                for(int i=0;i<4;i++)
                {
                    var p=observer.WorldToViewportPoint(t.TransformPoint(new Vector3((i&1)==0?-.5f:.5f,(i&2)==0?-.5f:.5f,.5f)));
                    if(p.z<=observer.nearClipPlane){near=true;continue;}
                    minX=Mathf.Min(minX,p.x);maxX=Mathf.Max(maxX,p.x);minY=Mathf.Min(minY,p.y);maxY=Mathf.Max(maxY,p.y);
                }
                w.Height=near?observer.pixelHeight:Mathf.Max(0,Mathf.Min(1,maxY)-Mathf.Max(0,minY))*observer.pixelHeight;
                w.Area=near?observer.pixelHeight:Mathf.Max(0,Mathf.Min(1,maxX)-Mathf.Max(0,minX))*w.Height;
                var ray=observer.ViewportPointToRay(new Vector3(.5f,.5f,0));float hit;
                var plane=new Plane(t.forward,t.TransformPoint(new Vector3(0,0,.5f)));
                if(plane.Raycast(ray,out hit))
                {var point=t.InverseTransformPoint(ray.GetPoint(hit));w.Focused=Mathf.Abs(point.x)<=.5f&&Mathf.Abs(point.y)<=.5f;}
            }
            return w.Gate.Update(w.Distance,visible,now);
        }
        static void Refresh(Watch w,float now)
        {
            if(now<w.StateAt)return;w.StateAt=now+.1f;w.Error="";w.Parent=null;
            if(!w.View.IsScreenOn()){w.Error="监控屏无电或已关闭";return;}
            if(!SurveillanceClient.IsFresh(now)){w.Error="设备状态同步中断";return;}
            var settings=SurveillanceClient.At(w.View.Position);w.Markers=settings!=null&&settings.TargetMarkers;
            string channel;w.Id=w.View.SelectedCamera(out channel);
            if(w.Id==Guid.Empty){w.Error="请选择摄像头";return;}
            var device=SurveillanceClient.ById(w.Id);
            if(device==null){w.Error="摄像头已移除";return;}
            if(w.Channel!=channel||w.Label!=device.Label){w.Channel=channel;w.Label=device.Label;w.Caption=channel+" · "+device.Label;}
            var tile=world.GetTileEntity(device.Position) as TileEntityPoweredTrigger;
            if(tile==null||tile.BlockTransform==null){w.Error="监控区域未加载";return;}
            if(!tile.IsPowered){w.Error="摄像头无电";return;}
            var controller=tile.BlockTransform.GetComponent<MotionSensorController>()??tile.BlockTransform.GetComponentInChildren<MotionSensorController>(true);
            w.Parent=controller==null?null:controller.GetCameraTransform();
            if(w.Parent==null)w.Error="正在连接画面";
        }
        static int Compare(Watch a,Watch b)
        {
            int result=(b.View==focus).CompareTo(a.View==focus);if(result!=0)return result;
            Stream sa,sb;float now=sortNow;
            bool holdA=streams.TryGetValue(a.Id,out sa)&&sa.WasActive&&now-sa.AdmittedAt<.5f;
            bool holdB=streams.TryGetValue(b.Id,out sb)&&sb.WasActive&&now-sb.AdmittedAt<.5f;
            result=holdB.CompareTo(holdA);if(result!=0)return result;
            result=b.Area.CompareTo(a.Area);if(result!=0)return result;
            result=a.Distance.CompareTo(b.Distance);return result!=0?result:a.Id.CompareTo(b.Id);
        }
        static Stream Acquire(Watch w,float now)
        {
            Stream s;if(streams.TryGetValue(w.Id,out s))
            {
                if(s.Parent==w.Parent&&s.Camera!=null)return s;
                Destroy(s);streams.Remove(w.Id);
            }
            if(cameraTemplate==null)cameraTemplate=Resources.Load("Prefabs/ElectricityCamera") as GameObject;
            if(cameraTemplate==null)return null;
            if(createdThisFrame)return null;
            createdThisFrame=true;
            if(streams.Count>=RenderPolicy.MaxActive+RenderPolicy.MaxIdle)
            {
                Stream oldest=null;
                foreach(var item in streams.Values)if(!item.Active&&(oldest==null||item.LastUse<oldest.LastUse))oldest=item;
                if(oldest!=null){Destroy(oldest);streams.Remove(oldest.Id);}
            }
            var go=UnityEngine.Object.Instantiate(cameraTemplate,w.Parent);go.name="PZAEC Surveillance Camera "+w.Id;
            go.transform.localPosition=Vector3.zero;go.transform.localRotation=Quaternion.identity;
            foreach(var listener in go.GetComponentsInChildren<AudioListener>(true))listener.enabled=false;
            var camera=go.GetComponent<Camera>()??go.GetComponentInChildren<Camera>(true);
            if(camera==null){UnityEngine.Object.Destroy(go);return null;}
            camera.enabled=false;camera.nearClipPlane=.05f;camera.farClipPlane=80;camera.fieldOfView=60;camera.aspect=4f/3;
            camera.depth=-10;camera.renderingPath=RenderingPath.Forward;
            s=new Stream{Id=w.Id,Object=go,Camera=camera,Parent=w.Parent,AdmittedAt=now,LastUse=now};
            // Join the due queue once, behind already overdue feeds.
            s.Clock.Due=now;streams.Add(w.Id,s);return s;
        }
        static void Render(Stream s,float now)
        {
            RenderTexture replacement=null;hidden.Clear();long started=Stopwatch.GetTimestamp();
            double previous=s.Clock.LastSuccess;bool success=false;
            try
            {
                int width=RenderPolicy.Width(s.Quality.Tier);
                if(s.Texture==null||s.Texture.width!=width)
                {
                    replacement=new RenderTexture(width,width*3/4,16,RenderTextureFormat.ARGB32)
                    {name="Surveillance feed "+s.Id,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,antiAliasing=1,useMipMap=false};
                    if(!replacement.Create())throw new InvalidOperationException("Render texture allocation failed");
                }
                s.Camera.targetTexture=replacement??s.Texture;
                foreach(var w in views)
                {
                    if(w.View==null)continue;
                    var surface=w.View.Surface;var label=w.View.StatusRenderer;
                    if(surface!=null&&surface.enabled){surface.enabled=false;hidden.Add(surface);}
                    if(label!=null&&label.enabled){label.enabled=false;hidden.Add(label);}
                    var markers=w.View.MarkerRenderer;if(markers!=null&&markers.enabled){markers.enabled=false;hidden.Add(markers);}
                }
                s.Camera.Render();s.Clock.Success(Time.realtimeSinceStartup,s.Hz);s.Frames++;success=true;
                if(replacement!=null){s.Retired=s.Texture;s.Texture=replacement;replacement=null;}
                s.MarkerFrame=-1;
                if(s.Markers)
                {
                    try
                    {
                        if(s.Detector==null)s.Detector=new TargetMarkerDetector();
                        s.Detector.Capture((World)world,s.Camera,s.Texture.width,s.Texture.height);s.MarkerFrame=s.Clock.LastSuccess;
                    }
                    catch(Exception e)
                    {
                        s.Detector?.Clear();
                        if(now>=s.MarkerLogAt){s.MarkerLogAt=now+5;Log.Warning("[Surveillance] Target detection deferred: "+e.Message);}
                    }
                }
            }
            catch(Exception e)
            {
                s.Errors++;s.Clock.Failure(Time.realtimeSinceStartup);
                if(now>=s.LogAt){s.LogAt=now+5;Log.Warning("[Surveillance] Render retry: "+e.Message);}
                if(s.Camera!=null)s.Camera.targetTexture=s.Texture;
            }
            finally
            {
                Release(replacement);foreach(var r in hidden)if(r!=null)r.enabled=true;hidden.Clear();
                double cpu=(Stopwatch.GetTimestamp()-started)*1000d/Stopwatch.Frequency;s.CpuMs+=cpu;
                float finished=Time.realtimeSinceStartup;
                RenderDiagnostics.Record(finished,success?1:2,s.Id,cpu,previous<0?-1:(finished-previous)*1000,
                    success&&previous<0?(finished-s.WakeAt)*1000:-1,s.Texture==null?0:s.Texture.width,s.Hz);
            }
        }
        static void Trim(float now)
        {
            removed.Clear();int idle=0;
            foreach(var pair in streams)
            {
                var s=pair.Value;if(s.Active)continue;
                if(now-s.LastUse>5||s.Parent==null)removed.Add(pair.Key);else idle++;
            }
            foreach(var id in removed){Destroy(streams[id]);streams.Remove(id);}
            while(idle>RenderPolicy.MaxIdle)
            {
                Stream oldest=null;foreach(var s in streams.Values)if(!s.Active&&(oldest==null||s.LastUse<oldest.LastUse))oldest=s;
                if(oldest==null)break;Destroy(oldest);streams.Remove(oldest.Id);idle--;
            }
        }
        static void Stats(float now)
        {
            frameCount++;float ms=Time.unscaledDeltaTime*1000;frameSum+=ms;frameMax=Mathf.Max(frameMax,ms);
            RenderDiagnostics.Record(now,0,Guid.Empty,ms,-1,-1,0,0);
            frameEma=frameEma==0?ms:Mathf.Lerp(frameEma,ms,.03f);
            if(!AutomaticDegrade){pressure=0;badSince=goodSince=-1;}
            else
            {
                bool bad=frameEma>35;foreach(var s in active)if(s.Clock.LastSuccess>=0&&now-s.Clock.LastSuccess>.3)bad=true;
                if(bad){goodSince=-1;if(badSince<0)badSince=now;if(now-badSince>=2){pressure=Math.Min(3,pressure+1);badSince=now;}}
                else{badSince=-1;if(frameEma<30){if(goodSince<0)goodSince=now;if(now-goodSince>=10){pressure=Math.Max(0,pressure-1);goodSince=now;}}else goodSince=-1;}
            }
            if(now<statsAt)return;statsAt=now+1;
            Summary="活动 "+active.Count+"/4 · 缓存 "+(streams.Count-active.Count)+" · "+(AutomaticDegrade?"自动保底 "+pressure:"固定策略");
            foreach(var s in streams.Values)
            {
                s.ActualHz=s.Frames/Math.Max(.001,now-statsSince);
                if(Diagnostics)
                {
                    samples.AppendFormat(CultureInfo.InvariantCulture,"{0:F3},{1},{2},{3},{4},{5},{6},{7},{8:F1},{9:F3},{10:F3},{11:F3},{12}\n",
                        now,s.Id,s.Active?1:0,streams.Count,s.Texture==null?0:s.Texture.width,s.Hz,s.Frames,s.Errors,
                        s.Clock.LastSuccess<0?-1:(now-s.Clock.LastSuccess)*1000,s.CpuMs,frameCount==0?0:frameSum/frameCount,frameMax,pressure);
                }
                s.Frames=s.Errors=0;s.CpuMs=0;
            }
            frameCount=0;frameSum=0;frameMax=0;statsSince=now;
            if(samples.Length>1024*1024)Export();
        }
        public static void Tick()
        {
            if(GameManager.IsDedicatedServer||SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)return;
            var current=GameManager.Instance?.World;
            if(current==null){if(world!=null)Clear();return;}
            if(world!=current){ClearStreams();world=current;focus=pendingFocus=null;statsSince=Time.realtimeSinceStartup;}
            var player=current.GetPrimaryPlayer();var observer=player==null?null:player.playerCamera;
            float now=Time.realtimeSinceStartup;
            candidates.Clear();active.Clear();clocks.Clear();
            createdThisFrame=false;
            foreach(var s in streams.Values){s.WasActive=s.Active;s.Active=false;s.Focused=false;s.RequestedTier=0;s.Markers=false;}
            if(observer!=null&&views.Count>0)GeometryUtility.CalculateFrustumPlanes(observer,planes);
            ScreenView picked=null;float closest=float.MaxValue;
            for(int i=views.Count-1;i>=0;i--)
            {
                var w=views[i];w.Feed=null;
                if(w.View==null||w.View.World!=world){views.RemoveAt(i);continue;}
                bool wasWatching=w.Gate.Watching;
                if(observer==null){w.Gate.Update(float.MaxValue,false,now);w.View.Show(null,"待机");continue;}
                if(!Observe(w,observer,now)){w.View.Show(null,"待机");continue;}
                if(!wasWatching)w.StateAt=0;
                Refresh(w,now);
                if(w.Error.Length>0||w.Parent==null){w.View.Show(null,w.Error);continue;}
                w.Tier=RenderPolicy.Tier(w.Height,w.Tier);candidates.Add(w);
                if(w.Focused&&w.Distance<closest){picked=w.View;closest=w.Distance;}
            }
            if(picked!=pendingFocus){pendingFocus=picked;focusSince=now;}
            if(now-focusSince>=.3f)focus=pendingFocus;
            sortNow=now;candidates.Sort(compare);
            foreach(var w in candidates)
            {
                Stream s;streams.TryGetValue(w.Id,out s);
                if((s==null||!s.Active)&&active.Count>=RenderPolicy.MaxActive){w.View.Show(null,"待机：观看名额已满");continue;}
                s=Acquire(w,now);if(s==null){w.View.Show(null,"正在连接画面");continue;}
                if(!s.Active)
                {
                    if(!s.WasActive){s.AdmittedAt=s.WakeAt=now;s.Clock.LastSuccess=-1;s.Clock.Due=now;}
                    s.Active=true;active.Add(s);clocks.Add(s.Clock);
                }
                s.Focused|=w.View==focus;s.RequestedTier=Math.Max(s.RequestedTier,w.Tier);s.LastUse=now;w.Feed=s;
                s.Markers|=w.Markers;
            }
            bool anyFocus=false;foreach(var s in active)anyFocus|=s.Focused;
            foreach(var s in active)
            {
                s.Hz=RenderPolicy.Rate(active.Count,s.Focused,anyFocus,AutomaticDegrade?pressure:0);
                int tier=s.RequestedTier;
                if(AutomaticDegrade&&pressure>0&&!s.Focused)tier=Math.Max(0,tier-1);
                if(AutomaticDegrade&&pressure>=2&&s.Focused)tier=Math.Min(1,tier);
                s.Quality.Update(tier,now);
            }
            int chosen=RenderPolicy.Pick(clocks,now);if(chosen>=0)Render(active[chosen],now);
            now=Time.realtimeSinceStartup;
            foreach(var w in candidates)
            {
                var s=w.Feed;if(s==null)continue;
                if(!s.Clock.Fresh(now)){w.View.Show(null,s.Clock.LastSuccess<0?"正在连接画面":"画面中断：性能受限或连接异常");continue;}
                string suffix="";
                if(now-s.Clock.LastSuccess>.3)
                {
                    if(now>=s.StatusAt){s.StatusAt=now+.1f;s.Status=" · 画面延迟 "+Mathf.RoundToInt((float)(now-s.Clock.LastSuccess)*1000)+"ms / "+s.ActualHz.ToString("F1")+"Hz";}
                    suffix=s.Status;
                }
                w.View.Show(s.Texture,suffix.Length==0?w.Caption:w.Caption+suffix);
                w.View.ShowMarkers(w.Markers&&s.MarkerFrame==s.Clock.LastSuccess?s.Detector:null,s.Texture.width,s.Texture.height);
            }
            // Every watcher has switched before its former texture is released.
            foreach(var s in streams.Values)if(s.Retired!=null){Release(s.Retired);s.Retired=null;}
            Trim(now);Stats(now);
        }
    }
}
