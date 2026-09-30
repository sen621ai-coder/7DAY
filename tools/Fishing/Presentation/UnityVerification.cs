using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using PZAEC.Fishing.Contracts;
using PZAEC.Fishing.Presentation;
using Object=UnityEngine.Object;

public static class FishingUnityVerification
{
    static int tests;
    static void Check(bool condition,string message){if(!condition)throw new Exception("FAIL: "+message);tests++;Debug.Log("PASS: "+message);}
    static Vec3 V(float x,float y,float z)=>new Vec3(x,y,z);
    static RenderFrame Frame(Guid id)
    {
        var s=new FishingSnapshot{SessionId=id,Tick=3,TimeSeconds=.05,Phase=FishingPhase.Fighting,FishBehavior=FishBehavior.Sprinting,
            FishPosition=V(10001,-.25f,20003),FishForward=V(-1,0,0),FishVelocity=V(-2,0,0),FishMassKg=3,FishStamina01=.7f,
            Rod=new RodPose{Root=V(10000,1,20000),Tip=V(10000,.7f,20002.25f),Forward=V(0,.3f,1).Normalized,Right=V(1,0,0)},
            FloatPosition=V(10000.8f,0,20002.7f),FloatUp=Vec3.Up,LineLengthMeters=2,LineTensionNewtons=40,FloatSubmerged01=.4f};
        return new RenderFrame{Previous=s,Current=s,Alpha=1,RenderOrigin=V(10000,0,20000),IsLocalPlayer=true};
    }
    public static void Run()
    {
        tests=0;var id=Guid.NewGuid();var config=new FishingConfig();var start=new SessionStart{SessionId=id};string path=Path.GetFullPath("Build/TestMod");
        int count=Object.FindObjectsOfType<GameObject>().Length;
        using(var headless=new FishingPresentation(path,true))
        {headless.Begin(start,config);headless.OnEvent(new FishingEvent{SessionId=id,Sequence=1,Kind=FishingEventKind.Sprint});headless.Render(Frame(id));Check(!headless.IsReady&&Object.FindObjectsOfType<GameObject>().Length==count,"dedicated server creates no scene objects");}
        Vector3 feedback=Vector3.zero;var p=new FishingPresentation(path,false,new PresentationOptions{AudioEnabled=false},v=>feedback=v);
        var frame=Frame(id);p.Begin(start,config);p.Render(frame);Check(p.IsReady&&p.LastError==null,"production renderer instantiates actual bundle");
        var root=GameObject.Find("PZAEC.Fishing.Presentation");Check(root!=null,"session root exists");
        var line=root.GetComponentsInChildren<LineRenderer>().First(x=>x.name=="FishingLine");
        var tip=root.GetComponentsInChildren<Transform>().First(x=>x.name=="LineExit");
        Check(Vector3.Distance(line.GetPosition(0),tip.position)<.0001f,"line begins at animated tip guide");
        var leader=root.GetComponentsInChildren<LineRenderer>().First(x=>x.name=="FishingLeader");
        var attachment=root.GetComponentsInChildren<Transform>().First(x=>x.name=="LineAttachment");
        var fishMouth=root.GetComponentsInChildren<Transform>().First(x=>x.name=="Mouth");
        Check(Vector3.Distance(line.GetPosition(line.positionCount-1),attachment.position)<.0001f&&Vector3.Distance(leader.GetPosition(0),attachment.position)<.0001f,"main line and leader visibly pass through float eye");
        Check(Vector3.Distance(leader.GetPosition(leader.positionCount-1),fishMouth.position)<.0001f,"leader terminates at fish mouth");
        Check(root.transform.Find("fishingfish").GetComponentsInChildren<Renderer>().Length<32,"detailed fish rigid meshes batched below 32 renderers");
        Check(root.transform.Find("fishingfloat").position.x<2,"floating origin applied to float");
        var renderers=root.GetComponentsInChildren<Renderer>();Check(renderers.All(r=>r.sharedMaterial!=null&&r.sharedMaterial.shader!=null),"all runtime renderers have materials");
        p.OnEvent(new FishingEvent{SessionId=id,Sequence=1,Kind=FishingEventKind.WaterContact,Position=frame.Current.FloatPosition,Intensity01=1});
        p.OnEvent(new FishingEvent{SessionId=id,Sequence=1,Kind=FishingEventKind.WaterContact,Position=frame.Current.FloatPosition,Intensity01=1});p.Render(frame);
        Check(root.GetComponentsInChildren<LineRenderer>().Count(x=>x.name.StartsWith("Ripple")&&x.enabled)==1,"duplicate events do not duplicate ripples");
        var shifted=frame;shifted.RenderOrigin=V(10010,0,20000);p.Render(shifted);
        Check(Mathf.Abs(root.transform.Find("fishingfloat").position.x+9.2f)<.01f,"origin shift moves float exactly with scene");
        Check(Vector3.Distance(line.GetPosition(0),tip.position)<.0001f,"origin shift keeps line attached to guide");p.Render(frame);
        var invalid=frame;var bad=invalid.Current;bad.LineTensionNewtons=float.NaN;invalid.Current=bad;p.Render(invalid);Check(!root.activeSelf,"invalid frame hides scene safely");p.Render(frame);Check(root.activeSelf,"valid subsequent frame recovers visibility");
        var second=new FishingPresentation(path,false,new PresentationOptions{AudioEnabled=false});var id2=Guid.NewGuid();second.Begin(new SessionStart{SessionId=id2},config);second.Render(Frame(id2));Check(second.IsReady&&second.LastError==null,"two observers share bundle successfully");second.Dispose();p.Render(frame);Check(p.IsReady&&p.LastError==null,"disposing observer preserves other assets");
        var camera=new GameObject("TestCamera").AddComponent<Camera>();camera.transform.position=new Vector3(3.6f,2.4f,-2.9f);camera.transform.LookAt(new Vector3(.4f,.1f,1.6f));camera.fieldOfView=48;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.025f,.055f,.07f);
        var light=new GameObject("TestLight").AddComponent<Light>();light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(40,-30,0);light.intensity=1.2f;RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.5f,.55f,.6f);
        // Transparent flat proxy is NOT the game's waves/refraction/underwater shader.
        var water=GameObject.CreatePrimitive(PrimitiveType.Plane);water.name="WaterProxy";water.transform.position=new Vector3(0,0,2);water.transform.localScale=new Vector3(2,1,2);var waterMat=new Material(Shader.Find("Standard"));waterMat.color=new Color(.035f,.12f,.14f,.35f);waterMat.SetFloat("_Mode",3);waterMat.SetInt("_SrcBlend",(int)BlendMode.SrcAlpha);waterMat.SetInt("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);waterMat.SetInt("_ZWrite",0);waterMat.EnableKeyword("_ALPHABLEND_ON");waterMat.renderQueue=3000;water.GetComponent<Renderer>().sharedMaterial=waterMat;
        Capture(camera,"runtime-day");light.intensity=.18f;RenderSettings.ambientLight=new Color(.10f,.14f,.21f);Capture(camera,"runtime-low-light");
        p.OnEvent(new FishingEvent{SessionId=id,Sequence=2,Kind=FishingEventKind.Sprint,Intensity01=1});p.Render(frame);Check(feedback.sqrMagnitude>0,"bounded camera kick emitted through callback");
        frame.IsLocalPlayer=false;p.Render(frame);Check(feedback==Vector3.zero,"observer clears camera feedback");
        int sceneCount=Object.FindObjectsOfType<GameObject>().Length;var timer=System.Diagnostics.Stopwatch.StartNew();
        for(int i=0;i<1000;i++){var s=frame.Current;s.Tick++;s.TimeSeconds+=1.0/60;frame.Previous=frame.Current;frame.Current=s;p.Render(frame);}timer.Stop();
        Check(Object.FindObjectsOfType<GameObject>().Length==sceneCount,"1000 renders do not grow scene objects");
        frame.IsDedicatedServer=true;p.Render(frame);Check(!p.IsReady,"dedicated render clears existing scene");
        p.Begin(new SessionStart{SessionId=Guid.NewGuid()},config);p.Render(frame);Check(!p.IsReady,"old session frame cannot recreate scene");p.Dispose();p.Dispose();
        Check(GameObject.Find("PZAEC.Fishing.Presentation")==null,"clear immediately disables all session roots");
        using(var pole=new FishingPresentation(path,false,new PresentationOptions{AudioEnabled=false})) {
            var poleConfig=new FishingConfig();poleConfig.Rod.LengthMeters=4.5f;poleConfig.Line.FixedLengthMeters=4.5f;
            pole.Begin(start,poleConfig);var poleFrame=Frame(id);var poleState=poleFrame.Current;
            poleState.Rod.Tip=poleState.Rod.Root+V(0,1.2f,4.15f);poleState.Rod.PitchRadians=.28f;
            poleState.LineLengthMeters=4.5f;poleFrame.Previous=poleFrame.Current=poleState;
            pole.Render(poleFrame);Check(pole.IsReady&&pole.LastError==null,"hand pole prefab renders without reel rig");
            Check(pole.RightGrip.root.GetComponentsInChildren<Transform>(true).All(t=>t.name!="Spool"&&t.name!="Crank"),"hand pole has no reel geometry");
            camera.transform.position=new Vector3(4,3,-3);camera.transform.LookAt(new Vector3(0,1,2));
            light.intensity=1.2f;RenderSettings.ambientLight=new Color(.5f,.55f,.6f);Capture(camera,"hand-pole");
        }
        Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(light.gameObject);Object.DestroyImmediate(water);Object.DestroyImmediate(waterMat);
        MotionPreview(path);
        File.WriteAllText("Build/presentation-verified.txt","PASS: "+tests+" Unity renderer/lifecycle checks. Production sources + Contracts v1. Renders are editor simulations, not in-game acceptance.\n1000 Render calls (editor, no camera draws): "+timer.Elapsed.TotalMilliseconds.ToString("F2")+" ms total. Not game performance measurement.\n");
    }
    static void Capture(Camera camera,string name)
    {
        var rt=new RenderTexture(1440,900,24){antiAliasing=4};camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var t=new Texture2D(1440,900,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,1440,900),0,0);t.Apply();File.WriteAllBytes("Build/"+name+".png",t.EncodeToPNG());RenderTexture.active=null;camera.targetTexture=null;Object.DestroyImmediate(t);Object.DestroyImmediate(rt);
    }
    static void MotionPreview(string path)
    {
        Directory.CreateDirectory("Build/MotionFrames");var id=Guid.NewGuid();var c=new FishingConfig();var p=new FishingPresentation(path,false,new PresentationOptions{AudioEnabled=false});p.Begin(new SessionStart{SessionId=id},c);
        var camera=new GameObject("MotionCamera").AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.063f,.071f);camera.fieldOfView=42;
        var light=new GameObject("MotionKey").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.1f;light.transform.rotation=Quaternion.Euler(45,-55,0);RenderSettings.ambientLight=new Color(.4f,.44f,.5f);
        var fill=new GameObject("MotionFill").AddComponent<Light>();fill.type=LightType.Directional;fill.intensity=.85f;fill.color=new Color(.8f,.9f,1);fill.transform.rotation=Quaternion.Euler(-10,90,0);
        var frame=Frame(id);var rt=new RenderTexture(1024,576,24){antiAliasing=4};var image=new Texture2D(1024,576,TextureFormat.RGB24,false);camera.targetTexture=rt;
        for(int i=0;i<144;i++)
        {
            float time=i/24f;var s=frame.Current;s.Tick=i;s.TimeSeconds=time;s.FishVelocity=V(-(.3f+1.8f*(.5f+.5f*Mathf.Sin(time*1.6f))),0,0);s.FishBehavior=i<60?FishBehavior.Recovering:FishBehavior.Sprinting;
            s.FloatPosition=V(10000.8f,-.006f+Mathf.Sin(time*4)*.016f,20002.7f);s.FloatSubmerged01=.5f-s.FloatPosition.Y/.18f;
            s.Rod.PitchRadians=.35f;s.Rod.Tip=V(10000,.7f+Mathf.Sin(time*1.6f)*.05f,20002.25f);s.LineLengthMeters=1.8f-time*.025f;
            frame.Previous=frame.Current;frame.Current=s;frame.Alpha=1;p.Render(frame);
            if(i<72){camera.transform.position=new Vector3(1,.03f,3.72f);camera.transform.LookAt(new Vector3(1,-.25f,2.96f));}
            else{camera.transform.position=new Vector3(3.2f,1.4f,-.45f);camera.transform.LookAt(new Vector3(.4f,.2f,1.7f));}
            fill.transform.rotation=camera.transform.rotation;
            camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1024,576),0,0);image.Apply();File.WriteAllBytes("Build/MotionFrames/frame-"+i.ToString("0000")+".png",image.EncodeToPNG());
        }
        RenderTexture.active=null;camera.targetTexture=null;Object.DestroyImmediate(image);Object.DestroyImmediate(rt);p.Dispose();Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(light.gameObject);Object.DestroyImmediate(fill.gameObject);
    }
    public static void ExportMotion(){MotionPreview(Path.GetFullPath("Build/TestMod"));File.WriteAllText("Build/motion-verified.txt","Exported 144 editor preview frames; synthetic snapshots, not game capture.");}
}
