using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using PZAEC.Surveillance;

public sealed class SurveillanceVisualQA : IModApi
{
    static bool pending;
    public void InitMod(Mod mod)
    {
        if(!Environment.GetCommandLineArgs().Contains("-surveillanceVisualQA"))return;
        ModEvents.GameStartDone.RegisterHandler(Ready);ModEvents.GameUpdate.RegisterHandler(Tick);
    }
    static void Ready(ref ModEvents.SGameStartDoneData data){pending=true;}
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static Color Pixel(Camera camera,RenderTexture rt)
    {
        camera.targetTexture=rt;FeedCamera.Render(camera);var previous=RenderTexture.active;
        var read=new Texture2D(1,1,TextureFormat.RGBA32,false);
        try{RenderTexture.active=rt;read.ReadPixels(new Rect(rt.width/2,rt.height/2,1,1),0,0);read.Apply();return read.GetPixel(0,0);}
        finally{RenderTexture.active=previous;UnityEngine.Object.Destroy(read);}
    }
    static Color32[] Snapshot(Camera camera,RenderTexture rt)
    {
        camera.targetTexture=rt;FeedCamera.Render(camera);var previous=RenderTexture.active;
        var read=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);
        try{RenderTexture.active=rt;read.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);read.Apply();return read.GetPixels32();}
        finally{RenderTexture.active=previous;UnityEngine.Object.Destroy(read);}
    }
    static Color StoredPixel(RenderTexture rt)
    {
        var previous=RenderTexture.active;var read=new Texture2D(1,1,TextureFormat.RGBA32,false);
        try{RenderTexture.active=rt;read.ReadPixels(new Rect(rt.width/2,rt.height/2,1,1),0,0);read.Apply();return read.GetPixel(0,0);}
        finally{RenderTexture.active=previous;UnityEngine.Object.Destroy(read);}
    }
    static void Quadrant(Color32[] pixels,int width,int x,int y,char expected)
    {
        var c=pixels[y*width+x];
        bool ok=expected=='R'?c.r>160&&c.g<80&&c.b<80:
            expected=='G'?c.g>160&&c.r<80&&c.b<80:
            expected=='B'?c.b>160&&c.r<80&&c.g<80:c.r>160&&c.g>160&&c.b<80;
        Check(ok,"Screen image UV orientation "+expected+" at "+x+","+y+" was "+c);
    }
    static int[] VerticalLandmarks(Color32[] pixels,int width,int height)
    {
        var counts=new int[4];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            var c=pixels[y*width+x];int half=y>=height/2?0:2;
            if(c.r>135&&c.g<85&&c.b<85)counts[half]++;
            if(c.b>135&&c.r<85&&c.g<85)counts[half+1]++;
        }
        return counts; // red top, blue top, red bottom, blue bottom
    }
    static void Tick(ref ModEvents.SGameUpdateData data)
    {
        if(!pending)return;pending=false;var objects=new List<UnityEngine.Object>();
        var rt=new RenderTexture(128,96,24);objects.Add(rt);rt.Create();
        try
        {
            Check(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null,"Graphics device is null");
            var sensorValue=Block.GetBlockValue(SurveillanceState.CameraBlock);
            var sensorPrefab=(Transform)AccessTools.Method(typeof(BlockShapeModelEntity),"getPrefab").Invoke(sensorValue.Block.shape,null);
            var sensor=UnityEngine.Object.Instantiate(sensorPrefab).gameObject;objects.Add(sensor);
            sensor.transform.position=new Vector3(0,550,0);
            var controller=sensor.GetComponentInChildren<MotionSensorController>(true);
            Check(controller!=null,"Real sensor controller missing");
            controller.Init(sensorValue.Block.Properties);controller.enabled=false;
            var cone=controller.GetCameraTransform();
            var coneMesh=cone.GetComponent<MeshFilter>().sharedMesh;
            Check(coneMesh.bounds.center.z<-.1f,"Native cone geometry no longer aims along negative Z");
            var actualFeed=FeedCamera.Create(cone,"QA real sensor feed");objects.Add(actualFeed.gameObject);actualFeed.cullingMask=1<<31;
            var frontTarget=GameObject.CreatePrimitive(PrimitiveType.Cube);objects.Add(frontTarget);frontTarget.layer=31;
            var rearTarget=GameObject.CreatePrimitive(PrimitiveType.Cube);objects.Add(rearTarget);rearTarget.layer=31;
            var frontMaterial=new Material(Shader.Find("Sprites/Default")){color=Color.green,mainTexture=Texture2D.whiteTexture};objects.Add(frontMaterial);
            var rearMaterial=new Material(frontMaterial){color=Color.red};objects.Add(rearMaterial);
            frontTarget.GetComponent<Renderer>().sharedMaterial=frontMaterial;rearTarget.GetComponent<Renderer>().sharedMaterial=rearMaterial;
            for(int rotation=0;rotation<4;rotation++)for(int aim=-1;aim<=1;aim++)
            {
                sensor.transform.rotation=Quaternion.Euler(0,rotation*90,0);
                controller.YawController.Yaw=aim*35;controller.YawController.SetYaw();
                controller.PitchController.Pitch=aim*15;controller.PitchController.SetPitch();
                // Derive the front independently from the real preview mesh volume.
                var expected=(cone.TransformPoint(coneMesh.bounds.center)-cone.position).normalized;
                frontTarget.transform.position=cone.position+expected*3;rearTarget.transform.position=cone.position-expected*3;
                actualFeed.transform.SetPositionAndRotation(cone.position,Quaternion.LookRotation(cone.forward,Vector3.up));
                var backwards=Pixel(actualFeed,rt);
                Check(backwards.r>.8f&&backwards.g<.2f,"Old positive-Z direction did not see rear control");
                FeedCamera.Follow(actualFeed,cone);var forwards=Pixel(actualFeed,rt);
                Check(Vector3.Dot(actualFeed.transform.forward,expected)>.999f&&forwards.g>.8f&&forwards.r<.2f,"Real sensor feed looks behind lens at rotation="+rotation+" aim="+aim+" pixel="+forwards);
            }
            frontTarget.SetActive(false);rearTarget.SetActive(false);
            Log.Out("[SurveillanceQA] PASS real sensor front/rear GPU controls: four rotations x three native yaw/pitch settings");
            sensor.SetActive(false);
            var mount=new GameObject("QA inactive sensor cone");objects.Add(mount);mount.transform.SetPositionAndRotation(new Vector3(0,500,0),Quaternion.Euler(0,180,0));mount.SetActive(false);
            var camera=FeedCamera.Create(mount.transform,"QA detached camera");objects.Add(camera.gameObject);
            Log.Out("[SurveillanceQA] Feed components="+string.Join(",",camera.GetComponents<Component>().Select(c=>c==null?"missing":c.GetType().FullName)));
            var turretEffect=camera.GetComponent("ImageEffect_TurretView") as Behaviour;
            Check(turretEffect==null||!turretEffect.enabled,"Decorative turret filter still active");
            Check(camera.transform.parent==null&&camera.gameObject.activeInHierarchy&&!camera.enabled,"Feed inherited inactive mount or automatic rendering");
            Check(!mount.activeSelf,"Feed activated native sensor cone");
            Check((camera.cullingMask&((1<<10)|(1<<12)))==0,"First-person/UI layers enter surveillance feed");
            Check((camera.cullingMask&((1<<8)|(1<<9)|(1<<28)))==((1<<8)|(1<<9)|(1<<28)),"World/sky layers missing");
            int environmentMask=camera.cullingMask;
            camera.transform.rotation=Quaternion.Euler(-25,0,0);camera.backgroundColor=Color.magenta;
            camera.cullingMask=0;var noSky=Pixel(camera,rt);
            camera.cullingMask=1<<Constants.cLayerBackgroundImage;var realSky=Pixel(camera,rt);
            Log.Out("[SurveillanceQA] Native sky background off/on="+noSky+" / "+realSky);
            Check(Mathf.Abs(noSky.r-realSky.r)+Mathf.Abs(noSky.g-realSky.g)+Mathf.Abs(noSky.b-realSky.b)>.2f,"Native sky spheres missing from feed");
            Check(camera.clearFlags==CameraClearFlags.SolidColor&&camera.cullingMask==(1<<9)&&camera.GetComponent<FeedBackground>().Sky.targetTexture==null&&!camera.GetComponent<FeedBackground>().Sky.enabled,"Sky pass leaked camera state");
            FeedCamera.Follow(camera,mount.transform);camera.backgroundColor=Color.black;
            var distant=GameObject.CreatePrimitive(PrimitiveType.Cube);objects.Add(distant);
            distant.transform.position=camera.transform.position+camera.transform.forward*250;distant.transform.localScale=Vector3.one*40;
            var distantMaterial=new Material(Shader.Find("Sprites/Default")){color=Color.green,mainTexture=Texture2D.whiteTexture};objects.Add(distantMaterial);
            distant.GetComponent<Renderer>().sharedMaterial=distantMaterial;
            foreach(int layer in new[]{Constants.cLayerNoShadow,Constants.cLayerTerrain})
            {
                distant.layer=layer;camera.cullingMask=1<<layer;camera.farClipPlane=80;
                var clipped=Pixel(camera,rt);camera.farClipPlane=1000;var visible=Pixel(camera,rt);
                Check(clipped.g<.1f&&visible.g>.8f&&visible.r<.2f,"250m world background missing on layer "+layer);
            }
            distant.SetActive(false);camera.cullingMask=environmentMask;
            Log.Out("[SurveillanceQA] PASS native sky background, 250m no-shadow/terrain layers, sky camera state restoration");
            var weapon=GameObject.CreatePrimitive(PrimitiveType.Cube);objects.Add(weapon);
            weapon.layer=10;weapon.transform.position=mount.transform.position+Vector3.forward*2;
            var weaponMaterial=new Material(Shader.Find("Sprites/Default")){mainTexture=Texture2D.whiteTexture};objects.Add(weaponMaterial);
            weaponMaterial.color=Color.red;weapon.GetComponent<Renderer>().sharedMaterial=weaponMaterial;
            int worldMask=camera.cullingMask;camera.cullingMask=1<<10;
            Check(Pixel(camera,rt).r>.8f,"Weapon-layer test control not visible");
            camera.cullingMask=worldMask&(1<<10);
            Check(Pixel(camera,rt).r<.1f,"Weapon layer visible in world feed");
            weapon.SetActive(false);
            // Exercise the production render path, then read the retained texture without
            // rendering again. Also ensure subsequent game drawing cannot target this feed.
            var streamType=typeof(SurveillanceRenderService).GetNestedType("Stream",System.Reflection.BindingFlags.NonPublic);
            var stream=Activator.CreateInstance(streamType,true);
            streamType.GetField("Camera").SetValue(stream,camera);streamType.GetField("Parent").SetValue(stream,mount.transform);
            streamType.GetField("Hz").SetValue(stream,2);
            var render=typeof(SurveillanceRenderService).GetMethod("Render",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
            camera.cullingMask=0;camera.backgroundColor=Color.red;RenderTexture.active=rt;
            render.Invoke(null,new[]{stream,(object)Time.realtimeSinceStartup});
            var stored=(RenderTexture)streamType.GetField("Texture").GetValue(stream);objects.Add(stored);
            Check(stored!=null,"First production texture missing");
            var initial=StoredPixel(stored);
            Log.Out("[SurveillanceQA] Initial pixel="+initial+" background="+camera.backgroundColor+" mask="+camera.cullingMask+" size="+stored.width+"x"+stored.height);
            camera.backgroundColor=Color.green;render.Invoke(null,new[]{stream,(object)Time.realtimeSinceStartup});
            camera.backgroundColor=Color.blue;
            var retained=StoredPixel(stored);
            Log.Out("[SurveillanceQA] Retained pixel="+retained+" sameTexture="+(stored==(RenderTexture)streamType.GetField("Texture").GetValue(stream))+" frames="+streamType.GetField("Frames").GetValue(stream));
            bool restored=camera.targetTexture==null&&RenderTexture.active==rt;
            camera.backgroundColor=Color.red;var directRed=Pixel(camera,rt);
            camera.backgroundColor=Color.green;var directGreen=Pixel(camera,rt);
            Log.Out("[SurveillanceQA] Direct reference pixels="+directRed+" / "+directGreen);
            var depth16=new RenderTexture(384,288,16,RenderTextureFormat.ARGB32);objects.Add(depth16);depth16.Create();
            if(turretEffect!=null)turretEffect.enabled=true;
            camera.backgroundColor=Color.red;var depth16Red=Pixel(camera,depth16);
            camera.backgroundColor=Color.green;var depth16Green=Pixel(camera,depth16);
            Log.Out("[SurveillanceQA] Legacy depth16/turret-filter control pixels="+depth16Red+" / "+depth16Green);
            if(turretEffect!=null)turretEffect.enabled=false;
            camera.targetTexture=null;RenderTexture.active=rt;
            Check(initial.r>.7f&&initial.g<.2f&&retained.g>.7f&&retained.r<.2f,"Production retained frames invalid: "+initial+" / "+retained);
            Check(restored,"Production render leaked its target");
            Log.Out("[SurveillanceQA] PASS weapon-layer exclusion with visible control; retained last frame and render-target restoration");
            // Read back two manually rendered frames; an inactive parent must not suppress either.
            camera.cullingMask=0;camera.backgroundColor=Color.red;
            var red=Pixel(camera,rt);camera.backgroundColor=Color.green;var green=Pixel(camera,rt);
            Check(red.r>.7&&red.g<.2&&green.g>.7&&green.r<.2,"Two changing frames did not reach render texture: "+red+" / "+green);
            mount.transform.SetPositionAndRotation(new Vector3(8,501,-7),Quaternion.Euler(5,75,0));FeedCamera.Follow(camera,mount.transform);
            Check(Vector3.Distance(camera.transform.position,mount.transform.position)<.001&&
                Vector3.Dot(camera.transform.forward,-mount.transform.forward)>.999f,"Camera pose did not follow inactive mount");
            Log.Out("[SurveillanceQA] PASS inactive mount, two changing GPU frames, detached pose tracking");
            // The native sensor cone may be rolled 90 degrees. Gravity must stay up
            // both in the feed camera and after its RenderTexture reaches the panel.
            mount.transform.SetPositionAndRotation(new Vector3(0,500,0),Quaternion.Euler(0,180,90));FeedCamera.Follow(camera,mount.transform);
            Check(Vector3.Dot(camera.transform.forward,-mount.transform.forward)>.999f&&
                Vector3.Dot(camera.transform.up,Vector3.up)>.99f,"Rolled sensor mount tilted the camera horizon");
            var landmarkShader=Shader.Find("Sprites/Default");Check(landmarkShader!=null&&landmarkShader.isSupported,"Landmark shader missing");
            var upper=GameObject.CreatePrimitive(PrimitiveType.Cube);var lower=GameObject.CreatePrimitive(PrimitiveType.Cube);
            objects.Add(upper);objects.Add(lower);
            upper.name="QA world-up landmark";lower.name="QA world-down landmark";
            upper.layer=lower.layer=31;
            upper.transform.position=mount.transform.position+Vector3.forward*12+Vector3.up*2.4f;
            lower.transform.position=mount.transform.position+Vector3.forward*12-Vector3.up*2.4f;
            upper.transform.localScale=lower.transform.localScale=new Vector3(2,2,1);
            var redMaterial=new Material(landmarkShader){color=Color.red,mainTexture=Texture2D.whiteTexture};
            var blueMaterial=new Material(landmarkShader){color=Color.blue,mainTexture=Texture2D.whiteTexture};
            objects.Add(redMaterial);objects.Add(blueMaterial);
            upper.GetComponent<Renderer>().sharedMaterial=redMaterial;lower.GetComponent<Renderer>().sharedMaterial=blueMaterial;
            var feedRt=new RenderTexture(128,96,24);objects.Add(feedRt);feedRt.Create();
            camera.cullingMask=1<<31;camera.backgroundColor=Color.black;
            var feedPixels=Snapshot(camera,feedRt);var feedLandmarks=VerticalLandmarks(feedPixels,feedRt.width,feedRt.height);
            Log.Out("[SurveillanceQA] Rolled mount feed landmarks redTop/blueTop/redBottom/blueBottom="+string.Join(",",feedLandmarks));
            Check(feedLandmarks[0]>20&&feedLandmarks[3]>20&&feedLandmarks[1]<feedLandmarks[3]/4&&feedLandmarks[2]<feedLandmarks[0]/4,
                "Rolled sensor feed has a tilted or inverted horizon");
            upper.SetActive(false);lower.SetActive(false);

            var value=Block.GetBlockValue(SurveillanceState.ScreenBlock);
            var prefab=(Transform)AccessTools.Method(typeof(BlockShapeModelEntity),"getPrefab").Invoke(value.Block.shape,null);
            var screen=UnityEngine.Object.Instantiate(prefab).gameObject;objects.Add(screen);screen.SetActive(true);screen.transform.position=new Vector3(0,500,0);
            foreach(var t in screen.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
            var view=screen.GetComponent<ScreenView>();Check(view!=null&&view.Surface!=null,"Native screen prefab has no display");
            view.Bind(GameManager.Instance.World,new Vector3i(0,500,0));
            camera.cullingMask=1<<31;camera.backgroundColor=Color.blue;
            camera.transform.SetPositionAndRotation(view.Surface.transform.position+Vector3.forward*4,Quaternion.LookRotation(Vector3.back));
            view.Show(null,"");var black=Pixel(camera,rt);
            var cabinet=screen.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r=>r.name=="MonitorBody");
            view.Surface.enabled=false;var cabinetOnly=Pixel(camera,rt);view.Surface.enabled=true;
            if(cabinet!=null)cabinet.enabled=false;var faceOnly=Pixel(camera,rt);if(cabinet!=null)cabinet.enabled=true;
            var faceMesh=view.Surface.GetComponent<MeshFilter>()?.sharedMesh;
            Log.Out("[SurveillanceQA] Front diagnostic black="+black+" cabinetOnly="+cabinetOnly+" faceOnly="+faceOnly+
                " faceShader="+view.Surface.sharedMaterial.shader.name+" cabinetShader="+(cabinet==null?"missing":cabinet.sharedMaterial.shader.name)+
                " faceActive="+view.Surface.gameObject.activeInHierarchy+" faceEnabled="+view.Surface.enabled+" faceLayer="+view.Surface.gameObject.layer+
                " faceMesh="+(faceMesh==null?"missing":faceMesh.vertexCount+"v/"+faceMesh.triangles.Length+"i")+
                " faceBounds="+view.Surface.bounds+" camera="+camera.transform.position+" facePosition="+view.Surface.transform.position);
            foreach(var name in new[]{"Unlit/Texture","Unlit/Color","Standard","Legacy Shaders/Diffuse","Mobile/Diffuse","Game/Entity","Game/Entity Tint Mask"})
            {var shader=Shader.Find(name);Log.Out("[SurveillanceQA] shader candidate "+name+" = "+(shader==null?"missing":shader.isSupported?"supported":"unsupported"));}
            if(cabinet!=null)
            {
                var mat=cabinet.sharedMaterial;
                Log.Out("[SurveillanceQA] cabinet material="+mat.name+" mainTex="+(mat.mainTexture==null?"none":mat.mainTexture.name)+
                    " color="+(mat.HasProperty("_Color")?mat.color.ToString():"none")+" zWrite="+(mat.HasProperty("_ZWrite")?mat.GetInt("_ZWrite").ToString():"not exposed")+
                    " queue="+mat.renderQueue+" properties="+string.Join(",",mat.GetTexturePropertyNames()));
                foreach(var candidate in new[]{"Legacy Shaders/Diffuse","Standard","Sprites/Default"})
                {
                    var altShader=Shader.Find(candidate);if(altShader==null||!altShader.isSupported)continue;
                    var alt=new Material(altShader){color=Color.black,mainTexture=Texture2D.blackTexture};objects.Add(alt);
                    cabinet.sharedMaterial=alt;Log.Out("[SurveillanceQA] cabinet candidate "+candidate+" pixel="+Pixel(camera,rt)+" queue="+alt.renderQueue);
                }
                cabinet.sharedMaterial=mat;
            }
            Check(black.r<.15&&black.g<.15&&black.b<.15,"Unbound screen is not black: "+black);
            var texture=new Texture2D(1,1,TextureFormat.RGBA32,false);objects.Add(texture);texture.SetPixel(0,0,Color.red);texture.Apply();view.Show(texture,"");
            var displayed=Pixel(camera,rt);Check(displayed.r>.5&&displayed.g<.25&&displayed.b<.25,"Bound video texture not visible on front: "+displayed);
            texture.SetPixel(0,0,Color.green);texture.Apply();var changed=Pixel(camera,rt);
            Check(changed.g>.5&&changed.r<.25&&changed.b<.25,"Screen did not update with source frames: "+changed);
            var corners=new Texture2D(2,2,TextureFormat.RGBA32,false){filterMode=FilterMode.Point};objects.Add(corners);
            corners.SetPixel(0,0,Color.red);corners.SetPixel(1,0,Color.green);
            corners.SetPixel(0,1,Color.blue);corners.SetPixel(1,1,Color.yellow);corners.Apply();
            view.Show(corners,"");camera.orthographic=true;camera.orthographicSize=2.35f;
            var oriented=Snapshot(camera,rt);
            Log.Out("[SurveillanceQA] Corner diagnostic BL="+oriented[24*rt.width+32]+" BR="+oriented[24*rt.width+96]+
                " TL="+oriented[72*rt.width+32]+" TR="+oriented[72*rt.width+96]);
            Log.Out("[SurveillanceQA] Projected lower-left="+camera.WorldToScreenPoint(view.Surface.transform.TransformPoint(new Vector3(-.35f,-.35f,.5f)))+
                " upper-left="+camera.WorldToScreenPoint(view.Surface.transform.TransformPoint(new Vector3(-.35f,.35f,.5f))));
            var capture=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);objects.Add(capture);
            capture.SetPixels32(oriented);capture.Apply();
            var capturePath=Path.Combine(GameIO.GetSaveGameDir(),"surveillance-corners.png");
            File.WriteAllBytes(capturePath,capture.EncodeToPNG());Log.Out("[SurveillanceQA] Corner capture="+capturePath);
            var diagnosticMesh=view.Surface.GetComponent<MeshFilter>().sharedMesh;
            var originalUv=diagnosticMesh.uv;var normals=diagnosticMesh.normals;
            for(int side=0;side<2;side++)
            {
                var trial=(Vector2[])originalUv.Clone();
                for(int i=0;i<trial.Length;i++)
                    if(side==0?normals[i].z>.9f:normals[i].z<-.9f)trial[i].y=1f-trial[i].y;
                diagnosticMesh.uv=trial;var checkedPixels=Snapshot(camera,rt);
                Log.Out("[SurveillanceQA] UV flip "+(side==0?"+Z":"-Z")+" BL="+checkedPixels[24*rt.width+32]+
                    " BR="+checkedPixels[24*rt.width+96]+" TL="+checkedPixels[72*rt.width+32]+" TR="+checkedPixels[72*rt.width+96]);
            }
            diagnosticMesh.uv=originalUv;
            Quadrant(oriented,rt.width,32,24,'R');Quadrant(oriented,rt.width,96,24,'G');
            Quadrant(oriented,rt.width,32,72,'B');Quadrant(oriented,rt.width,96,72,'Y');
            Log.Out("[SurveillanceQA] PASS monitor UV corners upright");
            view.Show(texture,"");
            var detector=new TargetMarkerDetector();
            detector.Boxes.Add(new MarkerRect{Left=.1f,Bottom=.1f,Right=.4f,Top=.4f});
            view.ShowMarkers(detector,128,96);
            var marked=Snapshot(camera,rt);var redCounts=new int[4];
            for(int y=0;y<rt.height;y++)for(int x=0;x<rt.width;x++)
            {
                var color=marked[y*rt.width+x];
                if(color.r>110&&color.g<90&&color.b<90)redCounts[(y>=rt.height/2?2:0)+(x>=rt.width/2?1:0)]++;
            }
            Log.Out("[SurveillanceQA] Marker quadrants BL/BR/TL/TR="+string.Join(",",redCounts));
            Check(redCounts[0]>20&&redCounts[0]>redCounts[1]+redCounts[2]+redCounts[3],
                "Target red box not aligned with video lower-left");
            Log.Out("[SurveillanceQA] PASS target marker aligns with corrected video UV");
            view.ShowMarkers(null,0,0);camera.backgroundColor=Color.black;
            view.Show(feedRt,"");var livePixels=Snapshot(camera,rt);var liveLandmarks=VerticalLandmarks(livePixels,rt.width,rt.height);
            Log.Out("[SurveillanceQA] Monitor live landmarks redTop/blueTop/redBottom/blueBottom="+string.Join(",",liveLandmarks));
            capture.SetPixels32(livePixels);capture.Apply();
            var liveCapturePath=Path.Combine(GameIO.GetSaveGameDir(),"surveillance-live-feed.png");
            File.WriteAllBytes(liveCapturePath,capture.EncodeToPNG());Log.Out("[SurveillanceQA] Live capture="+liveCapturePath);
            Check(liveLandmarks[0]>20&&liveLandmarks[3]>20&&liveLandmarks[1]<liveLandmarks[3]/4&&liveLandmarks[2]<liveLandmarks[0]/4,
                "Live camera scene is rotated or inverted on monitor");
            Log.Out("[SurveillanceQA] PASS upright world landmarks through camera RenderTexture and monitor");
            camera.transform.SetPositionAndRotation(view.Surface.transform.position-Vector3.forward*4,Quaternion.LookRotation(Vector3.forward));
            view.FaceViewer(camera);
            view.Show(null,"");var rearBlank=Snapshot(camera,rt);
            view.Show(null,"待机");var rearStatus=Snapshot(camera,rt);int changedRear=0;
            for(int i=0;i<rearBlank.Length;i++)
                if(Math.Abs(rearBlank[i].r-rearStatus[i].r)+Math.Abs(rearBlank[i].g-rearStatus[i].g)+Math.Abs(rearBlank[i].b-rearStatus[i].b)>45)changedRear++;
            Check(changedRear<4,"Monitor status text leaked through back: changed pixels="+changedRear);
            var body=screen.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r=>r.name=="MonitorBody");
            Check(body!=null,"Monitor cabinet renderer missing");
            Log.Out("[SurveillanceQA] PASS no monitor status visible from rear; changedPixels="+changedRear+" bodyShader="+body.sharedMaterial.shader.name+" queue="+body.sharedMaterial.renderQueue);
            camera.transform.SetPositionAndRotation(view.Surface.transform.position+Vector3.forward*4,Quaternion.LookRotation(Vector3.back));
            view.FaceViewer(camera);
            view.Show(null,"");var frontBlank=Snapshot(camera,rt);
            view.Show(null,"待机");var frontStatus=Snapshot(camera,rt);int changedFront=0;
            for(int i=0;i<frontBlank.Length;i++)
                if(Math.Abs(frontBlank[i].r-frontStatus[i].r)+Math.Abs(frontBlank[i].g-frontStatus[i].g)+Math.Abs(frontBlank[i].b-frontStatus[i].b)>45)changedFront++;
            Check(changedFront>8,"Monitor status text missing from front: changed pixels="+changedFront);
            Log.Out("[SurveillanceQA] PASS monitor front status visible; changedPixels="+changedFront);
            var collider=screen.GetComponent<BoxCollider>();Check(collider!=null&&collider.size.x>3.9&&collider.size.y>2.9,"Screen root collider missing");
            Log.Out("[SurveillanceQA] PASS screen GPU readback black/red/green and full screen collider; shader="+view.Surface.sharedMaterial.shader.name);
            Log.Out("[SurveillanceQA] VISUAL PASS");
        }
        catch(Exception e){Log.Error("[SurveillanceQA] VISUAL FAIL "+e);}
        finally{RenderTexture.active=null;rt.Release();foreach(var obj in objects)UnityEngine.Object.Destroy(obj);}
    }
}
