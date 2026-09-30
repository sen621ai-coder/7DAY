using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class FishingAssetBuild
{
    static Material lipMat,rayMat,ceramicMat,threadMat;
    static Texture2D Bake(string name,int width,int height,Func<float,float,Color> sample,bool normal=false)
    {
        var texture=new Texture2D(width,height,TextureFormat.RGBA32,true,normal);
        var pixels=new Color[width*height];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)pixels[y*width+x]=sample((float)x/width,(float)y/height);
        texture.SetPixels(pixels);texture.Apply();string path=Dir+name+".png";File.WriteAllBytes(path,texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);AssetDatabase.ImportAsset(path);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.wrapMode=TextureWrapMode.Repeat;importer.anisoLevel=4;importer.maxTextureSize=1024;importer.sRGBTexture=!normal;
        if(normal){importer.textureType=TextureImporterType.NormalMap;importer.convertToNormalmap=false;}importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
    static float ScaleRidge(float u,float v)
    {
        float rows=v*34,cellU=u*24+((int)Mathf.Floor(rows)%2)*.5f;
        float x=cellU-Mathf.Floor(cellU)-.5f,y=rows-Mathf.Floor(rows);
        float arc=.27f+.53f*Mathf.Sqrt(Mathf.Max(0,1-x*x*4));
        return Mathf.Exp(-Mathf.Pow((y-arc)*24,2));
    }
    static void DetailedMaterials()
    {
        fish.SetFloat("_Metallic",.045f);fish.SetFloat("_Glossiness",.48f);
        fish.mainTexture=Bake("CarpAlbedo",512,1024,(u,v)=>
        {
            float top=Mathf.Sin(u*Mathf.PI*2),noise=Mathf.PerlinNoise(u*91,v*123);
            Color flank=new Color(.43f,.405f,.26f),back=new Color(.105f,.16f,.105f),belly=new Color(.69f,.67f,.50f);
            Color c=top>0?Color.Lerp(flank,back,Mathf.Pow(top,.7f)):Color.Lerp(flank,belly,Mathf.Pow(-top,1.5f));
            float head=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.75f,.92f,v)),ridge=ScaleRidge(u,v)*(1-head);
            return c*(.91f+noise*.16f-ridge*.19f);
        });
        var normal=Bake("CarpScaleNormal",512,1024,(u,v)=>
        {
            float head=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.75f,.92f,v));
            float dx=(ScaleRidge(u+1f/512,v)-ScaleRidge(u-1f/512,v))*.32f*head;
            float dy=(ScaleRidge(u,v+1f/1024)-ScaleRidge(u,v-1f/1024))*.32f*head;
            var n=new Vector3(-dx,-dy,1).normalized;return new Color(n.x*.5f+.5f,n.y*.5f+.5f,n.z*.5f+.5f,1);
        },true);
        fish.SetTexture("_BumpMap",normal);fish.SetFloat("_BumpScale",.65f);fish.EnableKeyword("_NORMALMAP");EditorUtility.SetDirty(fish);
        fin.color=Color.white;fin.SetFloat("_Glossiness",.28f);
        fin.mainTexture=Bake("FinMembrane",256,256,(u,v)=>
        {float rays=Mathf.Pow(Mathf.Abs(Mathf.Cos(u*Mathf.PI*23)),12);var c=Color.Lerp(new Color(.35f,.31f,.17f),new Color(.64f,.49f,.26f),v);return c*(.77f+.23f*rays);});
        carbon.mainTexture=Bake("CarbonWeave",256,256,(u,v)=>
        {int x=(int)(u*48),y=(int)(v*48);float w=((x/3+y/3)%2==0?Mathf.Sin(u*48*Mathf.PI):Mathf.Sin(v*48*Mathf.PI));return Color.white*(.55f+.25f*w);});
        carbon.mainTextureScale=new Vector2(2,24);carbon.SetFloat("_Metallic",.22f);carbon.SetFloat("_Glossiness",.48f);
        lipMat=Mat("CarpLip",new Color(.39f,.32f,.205f),0,.45f);rayMat=Mat("FinRays",new Color(.39f,.29f,.13f),0,.28f);
        ceramicMat=Mat("GuideCeramic",new Color(.018f,.023f,.022f),.05f,.68f);threadMat=Mat("SpoolLine",new Color(.47f,.53f,.38f),0,.42f);
        // Fluorescent paint catches available light; this is not a permanently glowing float.
        orange.DisableKeyword("_EMISSION");orange.SetColor("_EmissionColor",Color.black);
        foreach(var m in new[]{fin,carbon,orange})EditorUtility.SetDirty(m);
    }
    static Vector3 Smooth(Vector3[] points,float t)
    {
        float p=Mathf.Clamp01(t)*(points.Length-1);int i=Mathf.Min(points.Length-2,(int)p);float f=p-i;
        Vector3 a=points[Mathf.Max(0,i-1)],b=points[i],c=points[i+1],d=points[Mathf.Min(points.Length-1,i+2)];
        return .5f*((2*b)+(-a+c)*f+(2*a-5*b+4*c-d)*f*f+(-a+3*b-3*c+d)*f*f*f);
    }
    static void Stroke(string name,Transform parent,Vector3[] path,float radius,Material material,int sections=32)
    {
        const int sides=6;var vertices=new Vector3[(sections+1)*(sides+1)];var triangles=new List<int>();
        for(int i=0;i<=sections;i++)
        {
            float t=(float)i/sections;Vector3 center=Smooth(path,t),axis=(Smooth(path,Mathf.Min(1,t+.01f))-Smooth(path,Mathf.Max(0,t-.01f))).normalized;
            Vector3 right=Vector3.Cross(axis,Mathf.Abs(axis.y)>.9f?Vector3.forward:Vector3.up).normalized,up=Vector3.Cross(right,axis);
            for(int j=0;j<=sides;j++){int k=i*(sides+1)+j;float angle=j*2*Mathf.PI/sides;vertices[k]=center+(right*Mathf.Cos(angle)+up*Mathf.Sin(angle))*radius;
                if(i<sections&&j<sides){triangles.Add(k);triangles.Add(k+1);triangles.Add(k+sides+1);triangles.Add(k+1);triangles.Add(k+sides+2);triangles.Add(k+sides+1);}}
        }
        var mesh=new Mesh{name=name};mesh.vertices=vertices;mesh.triangles=triangles.ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,Dir+name+".asset");MeshObject(name,parent,mesh,material);
    }
    static void RibbedFin(string name,Transform parent,Vector3 origin,Vector3[] edge,int rays=18)
    {
        const int radial=5;int n=(rays+1)*(radial+1);var verts=new Vector3[n*2];var uv=new Vector2[n*2];var tri=new List<int>();
        for(int i=0;i<=rays;i++)for(int j=0;j<=radial;j++)
        {
            float u=(float)i/rays,v=(float)j/radial;var tip=Smooth(edge,u);var p=Vector3.Lerp(origin,tip,v);
            p.x+=Mathf.Sin(u*Mathf.PI)*Mathf.Sin(v*Mathf.PI)*.0025f;int k=i*(radial+1)+j;
            verts[k]=verts[k+n]=parent.InverseTransformPoint(p);uv[k]=uv[k+n]=new Vector2(u,v);
            if(i<rays&&j<radial){int b=k+radial+1;tri.Add(k);tri.Add(b);tri.Add(k+1);tri.Add(k+1);tri.Add(b);tri.Add(b+1);tri.Add(k+n);tri.Add(k+1+n);tri.Add(b+n);tri.Add(k+1+n);tri.Add(b+1+n);tri.Add(b+n);}
        }
        var m=new Mesh{name=name};m.vertices=verts;m.uv=uv;m.triangles=tri.ToArray();m.RecalculateNormals();m.RecalculateBounds();AssetDatabase.CreateAsset(m,Dir+name+".asset");MeshObject(name,parent,m,fin);
        // Raised fine rays provide a silhouette and light response instead of a flat triangle.
        for(int i=0;i<=rays;i+=2){var tip=Smooth(edge,(float)i/rays);Stroke(name+"Ray"+i,parent,new[]{parent.InverseTransformPoint(Vector3.Lerp(origin,tip,.15f)),parent.InverseTransformPoint(Vector3.Lerp(origin,tip,.55f)),parent.InverseTransformPoint(tip)},.00035f,rayMat,8);}
    }
    static void DetailedFish()
    {
        var root=new GameObject("FishingFish");
        var profiles=new[]{new Vector3(.005f,.012f,-.19f),new Vector3(.012f,.023f,-.15f),new Vector3(.027f,.046f,-.10f),new Vector3(.039f,.064f,-.04f),new Vector3(.043f,.070f,.025f),new Vector3(.037f,.059f,.085f),new Vector3(.026f,.037f,.14f),new Vector3(.018f,.022f,.177f),new Vector3(.008f,.011f,.204f),new Vector3(.001f,.003f,.210f)};
        const int rings=56;var z=new float[rings];var rx=new float[rings];var ry=new float[rings];
        for(int i=0;i<rings;i++){var p=Smooth(profiles,(float)i/(rings-1));z[i]=p.z;rx[i]=Mathf.Max(.0004f,p.x);ry[i]=Mathf.Max(.0004f,p.y);}
        var bones=Rig(root.transform,"FishBone",.15f,-.19f,6);var mesh=Lathe("CarpDetailedBody",z,rx,ry,48,false);
        // Offset the dorsal ridge slightly forward; belly stays rounded.
        var vertices=mesh.vertices;for(int i=0;i<vertices.Length;i++)vertices[i].y+=.005f*Mathf.Exp(-Mathf.Pow((vertices[i].z-.045f)/.09f,2));mesh.vertices=vertices;mesh.RecalculateNormals();mesh.RecalculateTangents();Skin(root,"FishBody",mesh,fish,bones,.15f,-.19f);
        RibbedFin("TailFin",bones[5],new Vector3(0,0,-.181f),new[]{new Vector3(0,.059f,-.275f),new Vector3(0,.05f,-.281f),new Vector3(0,.008f,-.244f),new Vector3(0,-.05f,-.281f),new Vector3(0,-.059f,-.267f)},24);
        RibbedFin("DorsalFin",bones[2],new Vector3(0,.047f,-.06f),new[]{new Vector3(0,.043f,-.136f),new Vector3(0,.076f,-.105f),new Vector3(0,.091f,.015f),new Vector3(0,.097f,.05f),new Vector3(0,.069f,.073f)},28);
        RibbedFin("AnalFin",bones[3],new Vector3(0,-.038f,-.085f),new[]{new Vector3(0,-.072f,-.137f),new Vector3(0,-.081f,-.111f),new Vector3(0,-.052f,-.061f)},12);
        foreach(int side in new[]{-1,1})
        {
            string suffix=side<0?"L":"R";
            var pivot=Node("PectoralPivot"+suffix,bones[1],Vector3.zero).transform;
            RibbedFin("Pectoral"+suffix,pivot,new Vector3(side*.032f,-.022f,.084f),new[]{new Vector3(side*.041f,-.020f,.053f),new Vector3(side*.079f,-.037f,.003f),new Vector3(side*.068f,-.043f,-.021f),new Vector3(side*.039f,-.039f,.004f)},14);
            RibbedFin("Pelvic"+suffix,bones[2],new Vector3(side*.020f,-.05f,-.013f),new[]{new Vector3(side*.030f,-.063f,-.024f),new Vector3(side*.046f,-.078f,-.072f),new Vector3(side*.024f,-.065f,-.082f)},12);
            // Slightly sunk eyes, a dark rim and bright iris; no oversized toy-like eyeballs.
            Primitive("EyeRim"+suffix,bones[0],PrimitiveType.Sphere,new Vector3(side*.024f,.014f,.004f),new Vector3(.008f,.010f,.011f),black);
            Primitive("Eye"+suffix,bones[0],PrimitiveType.Sphere,new Vector3(side*.0272f,.014f,.005f),new Vector3(.003f,.008f,.008f),eye);
            Primitive("Pupil"+suffix,bones[0],PrimitiveType.Sphere,new Vector3(side*.0285f,.014f,.005f),new Vector3(.0018f,.0048f,.0048f),pupil);
            var gill=Node("GillPivot"+suffix,bones[0],Vector3.zero).transform;
            Stroke("GillSeam"+suffix,gill,new[]{new Vector3(side*.025f,.040f,-.053f),new Vector3(side*.035f,.025f,-.052f),new Vector3(side*.035f,-.01f,-.045f),new Vector3(side*.022f,-.038f,-.041f)},.00075f,lipMat);
            Stroke("Barbel"+suffix,bones[0],new[]{new Vector3(side*.008f,-.004f,.052f),new Vector3(side*.017f,-.013f,.052f),new Vector3(side*.017f,-.019f,.040f)},.0007f,lipMat,14);
        }
        var mouth=Node("MouthVisual",bones[0],new Vector3(0,-.002f,.058f));mouth.transform.localScale=new Vector3(1,.7f,1);Ring("CarpLips",mouth.transform,Vector3.zero,.0065f,.0019f,lipMat);
        Primitive("MouthCavity",mouth.transform,PrimitiveType.Sphere,new Vector3(0,0,-.002f),new Vector3(.011f,.011f,.003f),black);
        Node("Mouth",bones[0],new Vector3(0,-.002f,.060f));Save(root,"FishingFish");
    }
    static void RodDetails(GameObject root,Transform[] bones,Transform spool,Transform crank)
    {
        for(int i=2;i<13;i+=2)
        {
            float rad=Mathf.Lerp(.018f,.0035f,i/12f);Ring("GuideInsert"+i,bones[i],new Vector3(0,-rad-.006f,0),rad-.001f,.00065f,ceramicMat);
            Node("GuideLine"+i,bones[i],new Vector3(0,-rad-.006f,0));
        }
        for(int i=0;i<20;i++)Ring("SpoolWinding"+i,spool,new Vector3(0,0,.004f+i*.0017f),.0245f,.00065f,threadMat);
        var bail=Node("Bail",root.transform,spool.localPosition).transform;
        Stroke("BailWire",bail,new[]{new Vector3(-.034f,0,0),new Vector3(-.04f,.012f,.027f),new Vector3(0,.033f,.043f),new Vector3(.04f,.012f,.027f),new Vector3(.034f,0,0)},.0017f,steel);
        foreach(var name in new[]{"CrankArm","CrankGrip"}){var old=crank.Find(name);if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);}
        Stroke("CrankArmDetailed",crank,new[]{Vector3.zero,new Vector3(.01f,-.013f,0),new Vector3(.025f,-.036f,0),new Vector3(.036f,-.039f,0)},.004f,steel,16);
        var handle=Primitive("CrankGrip",crank,PrimitiveType.Capsule,new Vector3(.04f,-.039f,0),new Vector3(.012f,.016f,.012f),black);handle.transform.localRotation=Quaternion.Euler(0,0,90);
        Node("ReelHandTarget",crank,new Vector3(.043f,-.039f,0));
    }
    static void OptimizeRigidMeshes(GameObject root)
    {
        // Preserve every moving bone/anchor; batch only leaf meshes sharing a rigid parent/material.
        foreach(var parent in root.GetComponentsInChildren<Transform>(true))
        {
            if(parent==null)continue;
            var groups=new Dictionary<Material,List<MeshFilter>>();
            foreach(Transform child in parent)
            {
                var filter=child.GetComponent<MeshFilter>();var renderer=child.GetComponent<MeshRenderer>();
                if(filter==null||renderer==null||child.childCount!=0)continue;
                if(!groups.TryGetValue(renderer.sharedMaterial,out var list)){list=new List<MeshFilter>();groups.Add(renderer.sharedMaterial,list);}list.Add(filter);
            }
            foreach(var group in groups)
            {
                if(group.Value.Count<2)continue;var instances=new CombineInstance[group.Value.Count];
                for(int i=0;i<instances.Length;i++){var f=group.Value[i];instances[i]=new CombineInstance{mesh=f.sharedMesh,transform=parent.worldToLocalMatrix*f.transform.localToWorldMatrix};}
                string name=root.name+"_"+parent.name+"_"+group.Key.name+"_Batch";var mesh=new Mesh{name=name};mesh.CombineMeshes(instances,true,true);mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,Dir+name+".asset");
                MeshObject(name,parent,mesh,group.Key);foreach(var f in group.Value)UnityEngine.Object.DestroyImmediate(f.gameObject);
            }
        }
    }
    static void DetailPreview()
    {
        var root=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Dir+"FishingFish.prefab"));
        var camera=new GameObject("DetailCamera").AddComponent<Camera>();camera.transform.position=new Vector3(.75f,.11f,.03f);camera.transform.LookAt(new Vector3(0,0,-.035f));camera.orthographic=true;camera.orthographicSize=.155f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.028f,.039f,.045f);
        var key=new GameObject("DetailKey").AddComponent<Light>();key.type=LightType.Directional;key.intensity=1.0f;key.transform.rotation=Quaternion.Euler(48,-65,0);RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.40f,.43f,.47f);
        var fill=new GameObject("DetailFill").AddComponent<Light>();fill.type=LightType.Directional;fill.intensity=.6f;fill.color=new Color(.63f,.77f,1);fill.transform.rotation=Quaternion.Euler(-15,80,0);
        var rt=new RenderTexture(1600,900,24){antiAliasing=4};camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var image=new Texture2D(1600,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1600,900),0,0);image.Apply();File.WriteAllBytes("Build/fish-detail.png",image.EncodeToPNG());RenderTexture.active=null;camera.targetTexture=null;
        UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(camera.gameObject);UnityEngine.Object.DestroyImmediate(key.gameObject);UnityEngine.Object.DestroyImmediate(fill.gameObject);
    }
}
