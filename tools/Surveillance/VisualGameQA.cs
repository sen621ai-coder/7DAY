using System;
using System.Linq;
using System.Collections.Generic;
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
            Check(Vector3.Distance(camera.transform.position,mount.transform.position)<.001&&Quaternion.Angle(camera.transform.rotation,mount.transform.rotation)<.001,"Camera pose did not follow inactive mount");
            Log.Out("[SurveillanceQA] PASS inactive mount, two changing GPU frames, detached pose tracking");

            var value=Block.GetBlockValue(SurveillanceState.ScreenBlock);
            var prefab=(Transform)AccessTools.Method(typeof(BlockShapeModelEntity),"getPrefab").Invoke(value.Block.shape,null);
            var screen=UnityEngine.Object.Instantiate(prefab).gameObject;objects.Add(screen);screen.SetActive(true);screen.transform.position=new Vector3(0,500,0);
            foreach(var t in screen.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
            var view=screen.GetComponent<ScreenView>();Check(view!=null&&view.Surface!=null,"Native screen prefab has no display");
            view.Bind(GameManager.Instance.World,new Vector3i(0,500,0));
            camera.cullingMask=1<<31;camera.backgroundColor=Color.blue;
            camera.transform.SetPositionAndRotation(view.Surface.transform.position+Vector3.forward*4,Quaternion.LookRotation(Vector3.back));
            view.Show(null,"");var black=Pixel(camera,rt);
            Check(black.r<.15&&black.g<.15&&black.b<.15,"Unbound screen is not black: "+black);
            var texture=new Texture2D(1,1,TextureFormat.RGBA32,false);objects.Add(texture);texture.SetPixel(0,0,Color.red);texture.Apply();view.Show(texture,"");
            var displayed=Pixel(camera,rt);Check(displayed.r>.5&&displayed.g<.25&&displayed.b<.25,"Bound video texture not visible on front: "+displayed);
            texture.SetPixel(0,0,Color.green);texture.Apply();var changed=Pixel(camera,rt);
            Check(changed.g>.5&&changed.r<.25&&changed.b<.25,"Screen did not update with source frames: "+changed);
            var collider=screen.GetComponent<BoxCollider>();Check(collider!=null&&collider.size.x>3.9&&collider.size.y>2.9,"Screen root collider missing");
            Log.Out("[SurveillanceQA] PASS screen GPU readback black/red/green and full screen collider; shader="+view.Surface.sharedMaterial.shader.name);
            Log.Out("[SurveillanceQA] VISUAL PASS");
        }
        catch(Exception e){Log.Error("[SurveillanceQA] VISUAL FAIL "+e);}
        finally{RenderTexture.active=null;rt.Release();foreach(var obj in objects)UnityEngine.Object.Destroy(obj);}
    }
}
