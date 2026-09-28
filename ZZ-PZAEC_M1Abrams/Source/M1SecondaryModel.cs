using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace PZAEC.M1
{
    public static class SecondaryModel
    {
        static Transform Add(Transform p,string n,Vector3 v){var t=new GameObject(n).transform;t.SetParent(p,false);t.localPosition=v;return t;}
        static string Str(BinaryReader r){int n=r.ReadInt32();if(n<1||n>128)throw new InvalidDataException("Secondary name");return System.Text.Encoding.UTF8.GetString(r.ReadBytes(n));}
        static Vector3 Vec(BinaryReader r)=>new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
        public static Transform Find(Transform root,string name){if(root==null)return null;foreach(var t in root.GetComponentsInChildren<Transform>(true))if(t.name==name)return t;return null;}
        public static void Build(Dictionary<string,Transform> nodes,List<Renderer>[] renderers,int layer)
        {
            var anchors=Newtonsoft.Json.JsonConvert.DeserializeObject<Model.AnchorFile>(File.ReadAllText(Path.Combine(Model.Path,"secondary-anchors.json")));
            foreach(var a in anchors.nodes){var p=nodes[a.parent];nodes[a.name]=Add(p,a.name,p.InverseTransformPoint(nodes["VisualRoot"].TransformPoint(new Vector3(a.position[0],a.position[1],a.position[2]))));}
            var tex=new Texture2D(2,2,TextureFormat.RGBA32,true,false){name="M1 secondary camouflage",anisoLevel=8};
            if(!ImageConversion.LoadImage(tex,File.ReadAllBytes(Path.Combine(Model.Path,"secondary-color.png")),true))throw new InvalidDataException("Secondary texture");
            var materials=new Material[6];float[] metal={.12f,.18f,.65f,.05f,.55f,.05f},rough={.83f,.78f,.64f,.85f,.24f,.84f};
            for(int i=0;i<6;i++){materials[i]=new Material(Shader.Find("Standard")){name="M1 secondary "+i,mainTexture=tex};materials[i].SetFloat("_Metallic",metal[i]);materials[i].SetFloat("_Glossiness",1-rough[i]);}
            using(var r=new BinaryReader(File.OpenRead(Path.Combine(Model.Path,"M1Secondary.meshbin")))){
                if(new string(r.ReadChars(4))!="M1S1")throw new InvalidDataException("Secondary model version");int count=r.ReadInt32();if(count<1||count>256)throw new InvalidDataException("Secondary parts");
                for(int k=0;k<count;k++){string name=Str(r),parent=Str(r);int material=r.ReadInt32();if(material<0||material>=6||!nodes.ContainsKey(parent))throw new InvalidDataException("Secondary parent/material");
                    for(int level=0;level<3;level++){int nv=r.ReadInt32(),nf=r.ReadInt32();if(nv<0||nv>200000||nf<0||nf>100000)throw new InvalidDataException("Secondary mesh size");
                        var vs=new Vector3[nv];var ns=new Vector3[nv];var uv=new Vector2[nv];var ids=new int[nf*3];for(int j=0;j<nv;j++){vs[j]=Vec(r);ns[j]=Vec(r);uv[j]=new Vector2(r.ReadSingle(),r.ReadSingle());}for(int j=0;j<ids.Length;j++){ids[j]=r.ReadInt32();if(ids[j]<0||ids[j]>=nv)throw new InvalidDataException("Secondary index");}
                        var mesh=new Mesh{name=name+"_LOD"+level,indexFormat=IndexFormat.UInt32};mesh.vertices=vs;mesh.normals=ns;mesh.uv=uv;mesh.triangles=ids;mesh.RecalculateBounds();mesh.RecalculateTangents();var t=Add(nodes[parent],mesh.name,Vector3.zero);t.gameObject.layer=layer;t.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;var mr=t.gameObject.AddComponent<MeshRenderer>();mr.sharedMaterial=materials[material];renderers[level].Add(mr);
                    }
                }if(r.BaseStream.Position!=r.BaseStream.Length)throw new InvalidDataException("Secondary trailing bytes");
            }
            nodes["AAPitch"].localRotation=Quaternion.Euler(-20,0,0);
        }
        // Render geometry is queried independently of the coarse chassis colliders.
        // No roof collider enlarges the vehicle or traps passengers.
        public sealed class Geometry
        {
            sealed class Part{public Transform Transform;public Vector3[] Vertices;public int[] Indices;public Bounds Bounds;public string Name;}
            readonly List<Part> parts=new List<Part>();
            public Geometry(Transform root){if(root==null)return;foreach(var f in root.GetComponentsInChildren<MeshFilter>(true))if(f.name.EndsWith("_LOD0")){var m=f.sharedMesh;parts.Add(new Part{Transform=f.transform,Vertices=m.vertices,Indices=m.triangles,Bounds=m.bounds,Name=f.name});}}
            public bool Clear(Vector3 a,Vector3 b,byte weapon)
            {
                a-=Origin.position;b-=Origin.position;
                foreach(var p in parts){if(weapon==SecondaryRules.MG&&p.Name.StartsWith("MG_")||weapon==SecondaryRules.AA&&p.Name.StartsWith("AA_"))continue;
                    var x=p.Transform.InverseTransformPoint(a);var delta=p.Transform.InverseTransformPoint(b)-x;float distance=delta.magnitude;if(distance<.0001f)continue;var d=delta/distance;
                    if(!p.Bounds.IntersectRay(new Ray(x,d),out float bound)||bound>distance)continue;
                    for(int j=0;j<p.Indices.Length;j+=3){var v=p.Vertices[p.Indices[j]];var e1=p.Vertices[p.Indices[j+1]]-v;var e2=p.Vertices[p.Indices[j+2]]-v;var h=Vector3.Cross(d,e2);float det=Vector3.Dot(e1,h);if(Mathf.Abs(det)<.000001f)continue;float inv=1/det;var s=x-v;float u=inv*Vector3.Dot(s,h);if(u<0||u>1)continue;var q=Vector3.Cross(s,e1);float w=inv*Vector3.Dot(d,q);if(w<0||u+w>1)continue;float t=inv*Vector3.Dot(e2,q);if(t>.005f&&t<distance)return false;}
                }return true;
            }
        }
    }
}
