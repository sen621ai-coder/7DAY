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
        camera.targetTexture=rt;camera.Render();var previous=RenderTexture.active;
        var read=new Texture2D(1,1,TextureFormat.RGBA32,false);
        try{RenderTexture.active=rt;read.ReadPixels(new Rect(rt.width/2,rt.height/2,1,1),0,0);read.Apply();return read.GetPixel(0,0);}
        finally{RenderTexture.active=previous;UnityEngine.Object.Destroy(read);}
    }
    static Color32[] Snapshot(Camera camera,RenderTexture rt)
    {
        camera.targetTexture=rt;camera.Render();var previous=RenderTexture.active;
        var read=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);
        try{RenderTexture.active=rt;read.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);read.Apply();return read.GetPixels32();}
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
            var mount=new GameObject("QA inactive sensor cone");objects.Add(mount);mount.transform.position=new Vector3(0,500,0);mount.SetActive(false);
            var camera=FeedCamera.Create(mount.transform,"QA detached camera");objects.Add(camera.gameObject);
            Check(camera.transform.parent==null&&camera.gameObject.activeInHierarchy&&!camera.enabled,"Feed inherited inactive mount or automatic rendering");
            Check(!mount.activeSelf,"Feed activated native sensor cone");
            // Read back two manually rendered frames; an inactive parent must not suppress either.
            camera.cullingMask=0;camera.backgroundColor=Color.red;
            var red=Pixel(camera,rt);camera.backgroundColor=Color.green;var green=Pixel(camera,rt);
            Check(red.r>.7&&red.g<.2&&green.g>.7&&green.r<.2,"Two changing frames did not reach render texture: "+red+" / "+green);
            mount.transform.SetPositionAndRotation(new Vector3(8,501,-7),Quaternion.Euler(5,75,0));FeedCamera.Follow(camera,mount.transform);
            Check(Vector3.Distance(camera.transform.position,mount.transform.position)<.001&&
                Vector3.Dot(camera.transform.forward,mount.transform.forward)>.999f,"Camera pose did not follow inactive mount");
            Log.Out("[SurveillanceQA] PASS inactive mount, two changing GPU frames, detached pose tracking");
            // The native sensor cone may be rolled 90 degrees. Gravity must stay up
            // both in the feed camera and after its RenderTexture reaches the panel.
            mount.transform.SetPositionAndRotation(new Vector3(0,500,0),Quaternion.Euler(0,0,90));FeedCamera.Follow(camera,mount.transform);
            Check(Vector3.Dot(camera.transform.forward,mount.transform.forward)>.999f&&
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
