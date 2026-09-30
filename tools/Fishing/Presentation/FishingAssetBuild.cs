using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Original procedural modelling source. No external meshes, textures or audio.
public static partial class FishingAssetBuild
{
    const string Dir = "Assets/Fishing/";
    static Material carbon, cork, steel, black, orange, cream, fish, fin, eye, pupil;
    static readonly List<string> assets = new List<string>();
    static Material Mat(string name, Color color, float metal = 0, float smooth = .3f)
    {
        var m = new Material(Shader.Find("Standard")); m.name=name; m.color=color;
        m.SetFloat("_Metallic",metal); m.SetFloat("_Glossiness",smooth);
        AssetDatabase.CreateAsset(m,Dir+name+".mat"); return m;
    }
    static Texture2D Texture(string name, Func<int,int,Color> sample)
    {
        var t = new Texture2D(256,256,TextureFormat.RGB24,true); t.name=name;
        for(int y=0;y<256;y++) for(int x=0;x<256;x++) t.SetPixel(x,y,sample(x,y));
        t.Apply(); File.WriteAllBytes(Dir+name+".png",t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(Dir+name+".png"); return AssetDatabase.LoadAssetAtPath<Texture2D>(Dir+name+".png");
    }
    static GameObject Node(string name, Transform parent, Vector3 p)
    {var g=new GameObject(name); g.transform.SetParent(parent,false);g.transform.localPosition=p;return g;}
    static Mesh Lathe(string name, float[] z, float[] rx, float[] ry, int sides=20,bool persist=true)
    {
        var v=new Vector3[z.Length*(sides+1)];var uv=new Vector2[v.Length];var tri=new List<int>();
        for(int j=0;j<z.Length;j++) for(int i=0;i<=sides;i++)
        {
            float a=i*2*Mathf.PI/sides;int k=j*(sides+1)+i;
            v[k]=new Vector3(Mathf.Cos(a)*rx[j],Mathf.Sin(a)*ry[j],z[j]);uv[k]=new Vector2((float)i/sides,(z[j]-z[0])/(z[z.Length-1]-z[0]));
            if(j>0&&i<sides){int b=k-sides-1;tri.Add(b);tri.Add(k+1);tri.Add(k);tri.Add(b);tri.Add(b+1);tri.Add(k+1);}
        }
        var m=new Mesh{name=name};m.vertices=v;m.uv=uv;m.triangles=tri.ToArray();m.RecalculateNormals();m.RecalculateBounds();
        if(persist)AssetDatabase.CreateAsset(m,Dir+name+".asset");return m;
    }
    static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material mat)
    {var g=Node(name,parent,Vector3.zero);g.AddComponent<MeshFilter>().sharedMesh=mesh;g.AddComponent<MeshRenderer>().sharedMaterial=mat;return g;}
    static GameObject Primitive(string name, Transform parent, PrimitiveType type,Vector3 p,Vector3 scale,Material mat)
    {var g=GameObject.CreatePrimitive(type);g.name=name;g.transform.SetParent(parent,false);g.transform.localPosition=p;g.transform.localScale=scale;g.GetComponent<Renderer>().sharedMaterial=mat;UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());return g;}
    static void Tube(string name,Transform parent,float start,float end,float radius,Material mat)
    {MeshObject(name,parent,Lathe(name,new[]{start,start,end,end},new[]{0,radius,radius,0},new[]{0,radius,radius,0}),mat);}
    static void Ring(string name,Transform parent,Vector3 center,float radius,float tube,Material mat)
    {
        const int n=24,s=6;var v=new Vector3[(n+1)*(s+1)];var t=new List<int>();
        for(int i=0;i<=n;i++)for(int j=0;j<=s;j++){float a=i*2*Mathf.PI/n,b=j*2*Mathf.PI/s;int k=i*(s+1)+j;
            v[k]=center+new Vector3(Mathf.Cos(a)*(radius+tube*Mathf.Cos(b)),Mathf.Sin(a)*(radius+tube*Mathf.Cos(b)),tube*Mathf.Sin(b));
            if(i<n&&j<s){t.Add(k);t.Add(k+s+1);t.Add(k+1);t.Add(k+1);t.Add(k+s+1);t.Add(k+s+2);}}
        var m=new Mesh{name=name};m.vertices=v;m.triangles=t.ToArray();m.RecalculateNormals();m.RecalculateBounds();AssetDatabase.CreateAsset(m,Dir+name+".asset");MeshObject(name,parent,m,mat);
    }
    static Transform[] Rig(Transform parent,string prefix,float start,float end,int count)
    {
        var bones=new Transform[count];
        for(int i=0;i<count;i++){bones[i]=Node(prefix+i.ToString("00"),i==0?parent:bones[i-1],new Vector3(0,0,i==0?start:(end-start)/(count-1))).transform;}
        return bones;
    }
    static void Skin(GameObject root,string name,Mesh mesh,Material mat,Transform[] bones,float start,float end)
    {
        var g=Node(name,root.transform,Vector3.zero);var r=g.AddComponent<SkinnedMeshRenderer>();var weights=new BoneWeight[mesh.vertexCount];var bind=new Matrix4x4[bones.Length];
        for(int i=0;i<bones.Length;i++)bind[i]=bones[i].worldToLocalMatrix*g.transform.localToWorldMatrix;
        var v=mesh.vertices;
        for(int i=0;i<v.Length;i++){float p=Mathf.Clamp01((v[i].z-start)/(end-start))*(bones.Length-1);int a=Mathf.Min(bones.Length-2,(int)p);float w=p-a;weights[i]=new BoneWeight{boneIndex0=a,boneIndex1=a+1,weight0=1-w,weight1=w};}
        mesh.boneWeights=weights;mesh.bindposes=bind;r.sharedMesh=mesh;r.sharedMaterial=mat;r.bones=bones;r.rootBone=bones[0];r.updateWhenOffscreen=false;r.localBounds=new Bounds(new Vector3(0,0,(start+end)/2),new Vector3(3,3,Mathf.Abs(end-start)+1));
        // Persist the fully weighted mesh atomically; creating an unweighted persistent mesh
        // then importing other assets can reload its old serialized data during repeated builds.
        AssetDatabase.CreateAsset(mesh,Dir+mesh.name+".asset");AssetDatabase.SaveAssets();
    }
    static void Save(GameObject g,string name)
    {OptimizeRigidMeshes(g);string path=Dir+name+".prefab";PrefabUtility.SaveAsPrefabAsset(g,path);assets.Add(path);UnityEngine.Object.DestroyImmediate(g);}
    static void Rod()
    {
        var root=new GameObject("FishingRod");
        var z=new float[34];var r=new float[34];for(int i=0;i<34;i++){z[i]=.24f+i*2.46f/33;r[i]=Mathf.Lerp(.011f,.0018f,(float)i/33);}r[0]=r[33]=0;
        var mesh=Lathe("RodBlank",z,r,r,12,false);var bones=Rig(root.transform,"RodBone",.24f,2.7f,13);Skin(root,"CarbonBlank",mesh,carbon,bones,.24f,2.7f);
        Tube("CorkRearGrip",root.transform,0,.19f,.017f,cork);Tube("ReelSeat",root.transform,.19f,.34f,.014f,black);Tube("CorkForeGrip",root.transform,.34f,.43f,.017f,cork);
        for(int i=0;i<4;i++)Tube("SeatRing"+i,root.transform,.20f+i*.034f,.207f+i*.034f,.0155f,steel);
        Primitive("ReelStem",root.transform,PrimitiveType.Cube,new Vector3(0,-.045f,.27f),new Vector3(.012f,.08f,.018f),steel);
        Primitive("ReelHousing",root.transform,PrimitiveType.Sphere,new Vector3(0,-.085f,.24f),new Vector3(.065f,.06f,.075f),black);
        var spool=Node("Spool",root.transform,new Vector3(0,-.082f,.295f));Tube("SpoolCore",spool.transform,0,.043f,.024f,steel);Ring("SpoolLip",spool.transform,new Vector3(0,0,.043f),.025f,.003f,steel);
        var crank=Node("Crank",root.transform,new Vector3(.032f,-.085f,.24f));Primitive("CrankArm",crank.transform,PrimitiveType.Cube,new Vector3(.02f,.012f,0),new Vector3(.05f,.008f,.009f),steel);Primitive("CrankGrip",crank.transform,PrimitiveType.Capsule,new Vector3(.045f,.012f,0),new Vector3(.013f,.016f,.013f),black);
        for(int i=2;i<13;i+=2){float rad=Mathf.Lerp(.018f,.0035f,i/12f);Ring("Guide"+i,bones[i],new Vector3(0,-rad-.006f,0),rad,.0015f,steel);Primitive("GuideFoot"+i,bones[i],PrimitiveType.Cube,new Vector3(0,-.004f,0),new Vector3(.003f,.01f,.014f),steel);}
        RodDetails(root,bones,spool.transform,crank.transform);
        Node("LineExit",bones[12],new Vector3(0,-.0095f,0));Node("GripRight",root.transform,new Vector3(0,0,.23f));Node("GripLeft",root.transform,new Vector3(0,0,.10f));Save(root,"FishingRod");
    }
    static void Float()
    {
        var root=new GameObject("FishingFloat");
        MeshObject("FloatBody",root.transform,Lathe("FloatBodyMesh",new[]{-.06f,-.055f,-.03f,0f,.028f,.04f},new[]{0f,.007f,.014f,.014f,.007f,0f},new[]{0f,.007f,.014f,.014f,.007f,0f}),cream);
        Tube("FloatKeel",root.transform,-.13f,-.052f,.0018f,black);
        for(int i=0;i<6;i++)Tube("Antenna"+i,root.transform,.025f+i*.018f,.043f+i*.018f,.0026f,i%2==0?orange:cream);
        Ring("FloatEye",root.transform,new Vector3(0,0,-.132f),.0025f,.0006f,steel);
        // Modelling axis Z; asset root +Y is up. Child geometry rotates together.
        var pivot=Node("FloatGeometry",root.transform,Vector3.zero);var children=new List<Transform>();foreach(Transform c in root.transform)if(c!=pivot.transform)children.Add(c);
        foreach(var c in children)c.SetParent(pivot.transform,false);pivot.transform.localRotation=Quaternion.Euler(-90,0,0);
        Node("Waterline",root.transform,Vector3.zero);Node("LineAttachment",root.transform,new Vector3(0,-.132f,0));Save(root,"FishingFloat");
    }
    static void Fin(string name,Transform parent,Vector3[] points)
    {
        // Separate front/back vertices: shared reversed triangles cancel normals to black.
        int n=points.Length;var v=new Vector3[n*2];for(int i=0;i<n;i++){v[i]=points[i];v[i+n]=points[i];}var t=new List<int>();
        for(int i=1;i<n-1;i++){t.Add(0);t.Add(i);t.Add(i+1);t.Add(n);t.Add(n+i+1);t.Add(n+i);}
        var m=new Mesh{name=name};m.vertices=v;m.triangles=t.ToArray();m.RecalculateNormals();m.RecalculateBounds();AssetDatabase.CreateAsset(m,Dir+name+".asset");MeshObject(name,parent,m,fin);
    }
    static void Fish()
    {
        var root=new GameObject("FishingFish");
        var z=new[]{-.19f,-.15f,-.10f,-.04f,.03f,.09f,.14f,.18f,.205f};var rx=new[]{.005f,.012f,.027f,.04f,.043f,.037f,.027f,.017f,.002f};var ry=new[]{.009f,.023f,.047f,.065f,.067f,.054f,.037f,.022f,.002f};
        var bones=Rig(root.transform,"FishBone",.15f,-.19f,6);Skin(root,"FishBody",Lathe("FishBodyMesh",z,rx,ry,32,false),fish,bones,.15f,-.19f);
        // Tail vertices are in the terminal bone's local coordinates.
        Fin("TailFin",bones[5],new[]{Vector3.zero,new Vector3(0,.070f,-.075f),new Vector3(0,.015f,-.054f),new Vector3(0,-.065f,-.075f)});
        Fin("DorsalFin",root.transform,new[]{new Vector3(0,.042f,-.10f),new Vector3(0,.11f,-.075f),new Vector3(0,.10f,.035f),new Vector3(0,.055f,.065f)});
        foreach(int side in new[]{-1,1})
        {
            Primitive("Eye"+side,bones[0],PrimitiveType.Sphere,new Vector3(side*.025f,.015f,.005f),new Vector3(.010f,.013f,.014f),eye);
            Primitive("Pupil"+side,bones[0],PrimitiveType.Sphere,new Vector3(side*.030f,.015f,.006f),new Vector3(.0025f,.007f,.008f),pupil);
            Fin("Pectoral"+side,root.transform,new[]{new Vector3(side*.034f,-.023f,.08f),new Vector3(side*.071f,-.055f,.007f),new Vector3(side*.044f,-.047f,-.01f)});
            Fin("Pelvic"+side,root.transform,new[]{new Vector3(side*.025f,-.05f,-.01f),new Vector3(side*.045f,-.084f,-.066f),new Vector3(side*.017f,-.044f,-.074f)});
        }
        Node("Mouth",bones[0],new Vector3(0,0,.055f));Save(root,"FishingFish");
    }
    static void Audio(string name,float duration,Func<float,System.Random,float> sample)
    {
        const int rate=22050;int n=(int)(duration*rate);var rng=new System.Random(7201);string path=Dir+name+".wav";
        using(var w=new BinaryWriter(File.Create(path))){w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));w.Write(36+n*2);w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));w.Write(16);w.Write((short)1);w.Write((short)1);w.Write(rate);w.Write(rate*2);w.Write((short)2);w.Write((short)16);w.Write(System.Text.Encoding.ASCII.GetBytes("data"));w.Write(n*2);
            for(int i=0;i<n;i++){float t=(float)i/rate;float fade=Mathf.Min(1,t/.008f)*Mathf.Min(1,(duration-t)/.02f);w.Write((short)(Mathf.Clamp(sample(t,rng)*fade,-.9f,.9f)*32767));}}
        AssetDatabase.ImportAsset(path);assets.Add(path);
    }
    static void Preview(string name,bool lowLight)
    {
        var scene=new GameObject("Preview");
        var rod=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Dir+"FishingRod.prefab"),scene.transform);rod.transform.position=new Vector3(-.75f,.20f,-.9f);rod.transform.rotation=Quaternion.Euler(-12,25,-12);
        var body=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Dir+"FishingFish.prefab"),scene.transform);body.transform.position=new Vector3(.38f,.36f,.13f);body.transform.rotation=Quaternion.Euler(0,55,0);body.transform.localScale=Vector3.one*1.8f;
        var fl=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Dir+"FishingFloat.prefab"),scene.transform);fl.transform.position=new Vector3(.15f,.32f,-.5f);fl.transform.localScale=Vector3.one*2;
        var groundMat=new Material(Shader.Find("Standard"));groundMat.color=new Color(.045f,.09f,.10f);groundMat.SetFloat("_Glossiness",.65f);Primitive("StudioSurface",scene.transform,PrimitiveType.Cube,new Vector3(0,-.045f,0),new Vector3(7,.05f,7),groundMat);
        var camera=Node("Camera",scene.transform,new Vector3(2.8f,2.5f,-3.4f)).AddComponent<Camera>();camera.transform.LookAt(new Vector3(0,.28f,.25f));camera.orthographic=true;camera.orthographicSize=1.3f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.025f,.04f,.055f);
        var light=Node("Key",scene.transform,Vector3.up*4).AddComponent<Light>();light.type=LightType.Directional;light.intensity=lowLight?.35f:1.4f;light.transform.rotation=Quaternion.Euler(45,-30,0);RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=lowLight?new Color(.10f,.13f,.19f):new Color(.60f,.65f,.7f);
        var rt=new RenderTexture(1440,900,24);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var image=new Texture2D(1440,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1440,900),0,0);image.Apply();File.WriteAllBytes("Build/"+name+".png",image.EncodeToPNG());RenderTexture.active=null;camera.targetTexture=null;
        UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(scene);UnityEngine.Object.DestroyImmediate(groundMat);
    }
    public static void Build()
    {
        Directory.CreateDirectory(Dir);Directory.CreateDirectory("Build");AssetDatabase.Refresh();assets.Clear();
        carbon=Mat("Carbon",new Color(.09f,.12f,.14f),.5f,.6f);cork=Mat("Cork",new Color(.72f,.53f,.30f));steel=Mat("Steel",new Color(.65f,.7f,.73f),.9f,.7f);black=Mat("Black",new Color(.025f,.03f,.035f),.3f,.4f);
        // Include the exact line/foam shader in the bundle: runtime Shader.Find may be stripped.
        File.WriteAllText(Dir+"FishingColor.shader",@"Shader ""PZAEC/FishingColor"" {
Properties { _Color (""Tint"", Color) = (1,1,1,1) }
SubShader { Tags { ""Queue""=""Transparent"" ""RenderType""=""Transparent"" } Pass {
Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#include ""UnityCG.cginc""
struct input { float4 vertex:POSITION; fixed4 color:COLOR; };
struct output { float4 vertex:SV_POSITION; fixed4 color:COLOR; };
fixed4 _Color;
output vert(input v) { output o; o.vertex=UnityObjectToClipPos(v.vertex); o.color=v.color*_Color; return o; }
fixed4 frag(output i):SV_Target { return i.color; }
ENDCG
} } }");
        AssetDatabase.ImportAsset(Dir+"FishingColor.shader");
        var lineMat=new Material(AssetDatabase.LoadAssetAtPath<Shader>(Dir+"FishingColor.shader"));lineMat.color=new Color(.70f,.76f,.68f,.8f);AssetDatabase.CreateAsset(lineMat,Dir+"Line.mat");assets.Add(Dir+"Line.mat");
        var foamMat=new Material(lineMat);foamMat.color=Color.white;AssetDatabase.CreateAsset(foamMat,Dir+"Foam.mat");assets.Add(Dir+"Foam.mat");
        orange=Mat("FloatOrange",new Color(1,.20f,.025f));orange.EnableKeyword("_EMISSION");orange.SetColor("_EmissionColor",new Color(.20f,.025f,0));cream=Mat("FloatCream",new Color(.92f,.89f,.72f),0,.5f);
        fish=Mat("FishScales",Color.white,.3f,.7f);fin=Mat("FishFin",new Color(.43f,.34f,.17f),0,.45f);eye=Mat("Eye",new Color(.75f,.58f,.15f),.1f,.8f);pupil=Mat("Pupil",new Color(.008f,.012f,.015f),0,.95f);
        fish.mainTexture=Texture("ScaleAlbedo",(x,y)=>{float a=x/255f*2*Mathf.PI,top=Mathf.Sin(a);var c=top>0?Color.Lerp(new Color(.56f,.55f,.34f),new Color(.16f,.24f,.17f),top):Color.Lerp(new Color(.56f,.55f,.34f),new Color(.83f,.80f,.64f),-top);float sx=(x+(y/12%2)*6)%12/12f,sy=y%12/12f;float edge=Mathf.Abs(Mathf.Sqrt((sx-.5f)*(sx-.5f)+sy*sy)-.6f)<.075f?.72f:1;return c*edge;});
        cork.mainTexture=Texture("CorkGrain",(x,y)=>{int hash=(x*73856093)^(y*19349663);float p=(hash&255)/255f;return Color.white*(p<.035f?.4f:.83f+p*.17f);});
        DetailedMaterials();Rod();Float();DetailedFish();
        Audio("ReelDrag",1,(t,r)=>(float)((r.NextDouble()-.5)*.12+Math.Sin(t*2*Math.PI*85)*Math.Pow(Math.Max(0,Math.Sin(t*2*Math.PI*27)),12)*.35));
        Audio("WaterSplash",.65f,(t,r)=>(float)((r.NextDouble()-.5)*Math.Exp(-t*6)*.6+Math.Sin(2*Math.PI*(280*t-130*t*t))*Math.Exp(-t*12)*.1));
        Audio("LineSnap",.13f,(t,r)=>(float)((r.NextDouble()-.5)*Math.Exp(-t*55)*.7));
        AssetDatabase.SaveAssets();
        var manifest=BuildPipeline.BuildAssetBundles("Build",new[]{new AssetBundleBuild{assetBundleName="fishing-presentation.unity3d",assetNames=assets.ToArray()}},BuildAssetBundleOptions.ChunkBasedCompression|BuildAssetBundleOptions.StrictMode,BuildTarget.StandaloneWindows64);
        if(manifest==null)throw new Exception("Bundle build failed");var bundle=AssetBundle.LoadFromFile(Path.GetFullPath("Build/fishing-presentation.unity3d"));
        int verts=0;foreach(string path in assets){var asset=bundle.LoadAsset(path);if(asset==null)throw new Exception("Missing "+path);var p=asset as GameObject;if(p!=null){if(p.GetComponentsInChildren<Collider>().Length!=0)throw new Exception("Unexpected collider "+path);foreach(var r in p.GetComponentsInChildren<Renderer>())if(r.sharedMaterial==null||r.sharedMaterial.shader==null)throw new Exception("Missing material "+path);foreach(var m in p.GetComponentsInChildren<SkinnedMeshRenderer>()){if(m.bones.Length<6||m.sharedMesh.boneWeights.Length!=m.sharedMesh.vertexCount)throw new Exception("Invalid rig "+path);verts+=m.sharedMesh.vertexCount;}}}
        bundle.Unload(true);Preview("assets-day",false);Preview("assets-low-light",true);DetailPreview();
        File.WriteAllText("Build/verified.txt","PASS: bundle reload, 3 prefabs, 3 original synthetic audio clips, materials and rigs. Skinned vertices: "+verts+". Unity "+Application.unityVersion+". Studio renders only; NOT game or hand-animation acceptance.");
    }
}
