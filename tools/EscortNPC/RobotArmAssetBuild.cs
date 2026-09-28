using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
public static class RobotArmAssetBuild
{
    static Material steel,orange,dark,cargo;
    static Material Mat(string name,Color c){var m=new Material(Shader.Find("Standard"));m.color=c;m.SetFloat("_Glossiness",.35f);AssetDatabase.CreateAsset(m,"Assets/RobotArms/"+name+".mat");return m;}
    static GameObject Part(Transform parent,string name,Vector3 p,Vector3 scale,Material m,PrimitiveType type=PrimitiveType.Cube,bool solid=false){var g=GameObject.CreatePrimitive(type);g.name=name;g.transform.SetParent(parent,false);g.transform.localPosition=p;g.transform.localScale=scale;g.GetComponent<Renderer>().sharedMaterial=m;if(!solid)UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());return g;}
    static void Beam(Transform t,Vector3 a,Vector3 b,float width){t.localPosition=(a+b)*.5f;t.localRotation=Quaternion.LookRotation(b-a);t.localScale=new Vector3(width,width,Vector3.Distance(a,b));}
    public static void Build()
    {
        Directory.CreateDirectory("Assets/RobotArms");AssetDatabase.Refresh();foreach(var p in Directory.GetFiles("Assets/RobotArms","*.mat"))AssetDatabase.DeleteAsset(p);
        steel=Mat("Steel",new Color(.23f,.31f,.36f));orange=Mat("Orange",new Color(1,.56f,.08f));dark=Mat("Dark",new Color(.07f,.1f,.12f));cargo=Mat("Cargo",new Color(.63f,.36f,.14f));
        var paths=new System.Collections.Generic.List<string>();
        for(int r=1;r<=3;r++){
            var root=new GameObject("RobotArm"+r);var tr=root.transform;
            Part(tr,"Base",new Vector3(0,.12f,0),new Vector3(.76f,.24f,.76f),steel,solid:true);
            Part(tr,"Turntable",new Vector3(0,.3f,0),new Vector3(.56f,.10f,.56f),dark,PrimitiveType.Cylinder);
            Part(tr,"Motor",new Vector3(0,.51f,0),new Vector3(.36f,.30f,.38f),orange);
            foreach(int s in new[]{-1,1})Part(tr,"ShoulderJoint",new Vector3(s*.22f,.64f,0),new Vector3(.15f,.15f,.15f),steel,PrimitiveType.Sphere);
            for(int n=0;n<r;n++)Part(tr,"ReachMark",new Vector3(-.14f+n*.14f,.247f,-.29f),new Vector3(.08f,.015f,.09f),orange);
            foreach(int s in new[]{-1,1}){var arrow=Part(tr,"OutputArrow",new Vector3(s*.055f,.25f,.27f),new Vector3(.045f,.02f,.2f),orange);arrow.transform.localRotation=Quaternion.Euler(0,-s*45,0);}
            var upper=Part(tr,"UpperArm",Vector3.zero,Vector3.one,orange).transform;
            Part(upper,"ArmInset",new Vector3(0,.51f,0),new Vector3(.62f,.05f,.75f),dark);
            var forearm=Part(tr,"Forearm",Vector3.zero,Vector3.one,steel).transform;
            Part(forearm,"SlideRail",new Vector3(0,.52f,0),new Vector3(.56f,.07f,.85f),orange);
            var hand=new Vector3(0,.52f,-r);var elbow=new Vector3(0,1.2f+r*.18f,hand.z*.25f);
            Beam(upper,new Vector3(0,.65f,0),elbow,.20f);Beam(forearm,elbow,hand+Vector3.up*.13f,.14f);
            Part(tr,"Elbow",elbow,new Vector3(.27f,.27f,.27f),orange,PrimitiveType.Sphere);
            var grip=new GameObject("Gripper");grip.transform.SetParent(tr,false);grip.transform.localPosition=hand+Vector3.up*.12f;
            Part(grip.transform,"Palm",Vector3.zero,new Vector3(.40f,.12f,.20f),steel);
            foreach(int s in new[]{-1,1}){Part(grip.transform,"Finger",new Vector3(s*.18f,-.13f,0),new Vector3(.065f,.24f,.16f),orange);Part(grip.transform,"Tip",new Vector3(s*.13f,-.25f,0),new Vector3(.15f,.06f,.16f),dark);}
            var packet=Part(tr,"Cargo",hand,new Vector3(.26f,.26f,.26f),cargo);packet.SetActive(false);
            var path="Assets/RobotArms/RobotArm"+r+".prefab";PrefabUtility.SaveAsPrefabAsset(root,path);paths.Add(path);UnityEngine.Object.DestroyImmediate(root);
        }
        AssetDatabase.SaveAssets();Directory.CreateDirectory("Build/RobotArms");
        if(BuildPipeline.BuildAssetBundles("Build/RobotArms",new[]{new AssetBundleBuild{assetBundleName="automation-arms.unity3d",assetNames=paths.ToArray()}},BuildAssetBundleOptions.ChunkBasedCompression|BuildAssetBundleOptions.StrictMode,BuildTarget.StandaloneWindows64)==null)throw new Exception("Arm bundle build failed");
        var bundle=AssetBundle.LoadFromFile(Path.GetFullPath("Build/RobotArms/automation-arms.unity3d"));
        foreach(var p in paths){var g=bundle.LoadAsset<GameObject>(p);if(g==null||g.transform.Find("Cargo")==null||g.transform.Find("Gripper")==null||g.transform.Find("UpperArm")==null||g.GetComponentsInChildren<Collider>().Length!=1)throw new Exception("Invalid arm prefab "+p);}bundle.Unload(true);
        File.WriteAllText("Build/RobotArms/verified.txt","PASS: 3 robot arm prefabs, articulated parts, cargo, base-only colliders, bundle reload");
    }
    static void Render(Camera camera,string path,int size,bool alpha){var rt=new RenderTexture(size,size,24);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var image=new Texture2D(size,size,alpha?TextureFormat.RGBA32:TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,size,size),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());camera.targetTexture=null;RenderTexture.active=null;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);}
    public static void Preview()
    {
        Build();var light=new GameObject("Light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.5f;light.transform.rotation=Quaternion.Euler(45,-30,0);RenderSettings.ambientLight=new Color(.65f,.65f,.65f);
        var camera=new GameObject("Camera").AddComponent<Camera>();camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;
        for(int r=1;r<=3;r++){
            var root=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RobotArms/RobotArm"+r+".prefab"));root.transform.Find("Cargo").gameObject.SetActive(true);
            camera.transform.position=new Vector3(4,3,-5);camera.transform.LookAt(new Vector3(0,.8f,-r*.4f));camera.orthographicSize=1.25f+r*.31f;camera.backgroundColor=Color.clear;
            Render(camera,"Build/RobotArms/yfAutoArm"+r+".png",160,true);
            camera.backgroundColor=new Color(.12f,.15f,.2f);Render(camera,"Build/RobotArms/arm"+r+"-preview.png",700,false);UnityEngine.Object.DestroyImmediate(root);
        }
        UnityEngine.Object.DestroyImmediate(camera.gameObject);UnityEngine.Object.DestroyImmediate(light.gameObject);
    }
}
