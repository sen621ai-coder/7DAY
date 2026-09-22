using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace YFAutomation.CargoDrones
{
    // This is an offline-built mesh pack, never an executable prefab or a glTF interpreter.
    // Meshes/textures are shared by all visible drones and released after the last instance.
    internal sealed class CargoBusterAssets
    {
        internal struct Pose { public Vector3 Position,Scale; public Quaternion Rotation; }
        internal sealed class Node
        {
            public string Name; public int Parent,Mesh; public Pose Rest,Flight; public Vector3 SpinAxis;
        }
        static CargoBusterAssets cached;
        static string resourceDirectory;
        internal static void Configure(string modPath)
        {
            string next=Path.Combine(Path.GetFullPath(modPath),"Resources","CargoDrone");
            if(cached!=null&&resourceDirectory!=next)throw new InvalidOperationException("Cannot change live Buster asset root");
            resourceDirectory=next;
        }
        internal Node[] Nodes;
        internal Mesh[] Meshes;
        internal int[] MeshMaterials;
        internal Material[] Materials;
        internal float Scale,Yaw;
        internal Vector3 Center;
        internal Bounds RestBounds;
        readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
        int references;
        internal static CargoBusterAssets Acquire()
        {
            if(cached==null)
            {
                var assets=new CargoBusterAssets();
                try{assets.Load();cached=assets;}catch{assets.Clear();throw;}
            }
            cached.references++;return cached;
        }
        internal void Release(){if(--references==0){if(cached==this)cached=null;Clear();}}
        void Clear(){foreach(var value in owned)if(value!=null)UnityEngine.Object.Destroy(value);owned.Clear();}
        static int Count(BinaryReader r,int maximum){int v=r.ReadInt32();if(v<0||v>maximum)throw new InvalidDataException("Invalid Buster model count");return v;}
        static float Number(BinaryReader r){float v=r.ReadSingle();if(float.IsNaN(v)||float.IsInfinity(v))throw new InvalidDataException("Invalid Buster model number");return v;}
        static Vector3 Vector(BinaryReader r){return new Vector3(Number(r),Number(r),Number(r));}
        static Pose ReadPose(BinaryReader r)
        {
            var pose=new Pose{Position=Vector(r),Rotation=new Quaternion(Number(r),Number(r),Number(r),Number(r)),Scale=Vector(r)};
            float norm=Quaternion.Dot(pose.Rotation,pose.Rotation);
            if(Mathf.Abs(norm-1)>.001f)throw new InvalidDataException("Invalid Buster pose rotation");return pose;
        }
        Texture2D Texture(string path,bool linear)
        {
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,true,linear);owned.Add(texture);
            if(!ImageConversion.LoadImage(texture,File.ReadAllBytes(path),false)||texture.width>2048||texture.height>2048)throw new InvalidDataException("Invalid Buster texture: "+path);
            texture.name=Path.GetFileNameWithoutExtension(path);texture.wrapMode=TextureWrapMode.Repeat;texture.anisoLevel=2;
            texture.Compress(false);texture.Apply(true,true);return texture;
        }
        void Load()
        {
            // 7DTD loads mod assemblies from bytes: Assembly.Location is empty in game.
            string directory=resourceDirectory;
            if(string.IsNullOrEmpty(directory))throw new InvalidOperationException("Buster asset root was not initialized by the mod");
            string file=Path.Combine(directory,"buster.yfmesh");
            if(new FileInfo(file).Length>16*1024*1024)throw new InvalidDataException("Buster model exceeds asset limit");
            using(var reader=new BinaryReader(File.OpenRead(file),Encoding.UTF8))
            {
                if(reader.ReadInt32()!=0x59464244||reader.ReadInt32()!=1)throw new InvalidDataException("Unknown Buster model version");
                Nodes=new Node[Count(reader,128)];Meshes=new Mesh[Count(reader,64)];MeshMaterials=new int[Meshes.Length];
                Scale=Number(reader);Yaw=Number(reader);Center=Vector(reader);
                if(Scale<=0||Scale>10||Nodes.Length==0||Meshes.Length==0)throw new InvalidDataException("Invalid Buster model dimensions");
                for(int i=0;i<Nodes.Length;i++)
                {
                    int length=Count(reader,256);var name=reader.ReadBytes(length);if(name.Length!=length)throw new EndOfStreamException();
                    var node=new Node{Name=Encoding.UTF8.GetString(name),Parent=reader.ReadInt32(),Mesh=reader.ReadInt32(),Rest=ReadPose(reader),Flight=ReadPose(reader),SpinAxis=Vector(reader)};
                    if(node.Parent< -1||node.Parent>=i||node.Mesh< -1||node.Mesh>=Meshes.Length)throw new InvalidDataException("Invalid Buster hierarchy");Nodes[i]=node;
                }
                var restTransforms=new Matrix4x4[Nodes.Length];var rootPose=Matrix4x4.TRS(-Center*Scale,Quaternion.Euler(0,Yaw*Mathf.Rad2Deg,0),Vector3.one*Scale);bool hasRestBounds=false;
                for(int i=0;i<Nodes.Length;i++){var p=Nodes[i].Rest;restTransforms[i]=(Nodes[i].Parent<0?rootPose:restTransforms[Nodes[i].Parent])*Matrix4x4.TRS(p.Position,p.Rotation,p.Scale);}
                for(int i=0;i<Meshes.Length;i++)
                {
                    int count=Count(reader,100000),indices=Count(reader,300000),material=Count(reader,1);
                    if(count==0||indices==0||indices%3!=0)throw new InvalidDataException("Invalid Buster mesh");
                    var vertices=new Vector3[count];var normals=new Vector3[count];var tangents=new Vector4[count];var uv=new Vector2[count];var triangles=new int[indices];
                    for(int j=0;j<count;j++){vertices[j]=Vector(reader);normals[j]=Vector(reader);tangents[j]=new Vector4(Number(reader),Number(reader),Number(reader),Number(reader));uv[j]=new Vector2(Number(reader),Number(reader));}
                    // Renderer.bounds transforms each mesh's enclosing box; rotated
                    // empty corners are not geometry. Measure actual rest vertices.
                    for(int node=0;node<Nodes.Length;node++)if(Nodes[node].Mesh==i)foreach(var vertex in vertices)
                    {var at=restTransforms[node].MultiplyPoint3x4(vertex);if(!hasRestBounds){RestBounds=new Bounds(at,Vector3.zero);hasRestBounds=true;}else RestBounds.Encapsulate(at);}
                    for(int j=0;j<indices;j++)triangles[j]=Count(reader,count-1);
                    var mesh=new Mesh{name="BusterMesh"+i,indexFormat=count>65535?IndexFormat.UInt32:IndexFormat.UInt16};owned.Add(mesh);
                    mesh.vertices=vertices;mesh.normals=normals;mesh.tangents=tangents;mesh.uv=uv;mesh.triangles=triangles;mesh.RecalculateBounds();mesh.UploadMeshData(true);
                    Meshes[i]=mesh;MeshMaterials[i]=material;
                }
                if(reader.BaseStream.Position!=reader.BaseStream.Length)throw new InvalidDataException("Trailing Buster model data");
            }
            var shader=Shader.Find("Standard");if(shader==null)throw new InvalidOperationException("Buster model requires the Standard shader");
            Materials=new Material[2];
            for(int i=0;i<2;i++)
            {
                var mat=new Material(shader){name=i==0?"BusterBody":"BusterLegs",color=Color.white};owned.Add(mat);Materials[i]=mat;
                mat.SetTexture("_MainTex",Texture(Path.Combine(directory,i+"-base.png"),false));
                mat.SetTexture("_BumpMap",Texture(Path.Combine(directory,i+"-normal.png"),true));mat.EnableKeyword("_NORMALMAP");
                mat.SetTexture("_MetallicGlossMap",Texture(Path.Combine(directory,i+"-metal.png"),true));mat.EnableKeyword("_METALLICGLOSSMAP");mat.SetFloat("_GlossMapScale",1);
                mat.SetTexture("_OcclusionMap",Texture(Path.Combine(directory,i+"-occlusion.png"),true));mat.SetFloat("_OcclusionStrength",1);
                if(i==0)
                {
                    // The source's body alpha contains grille holes; cutout keeps opaque depth sorting.
                    mat.SetFloat("_Mode",1);mat.SetFloat("_Cutoff",.2f);mat.SetOverrideTag("RenderType","TransparentCutout");mat.EnableKeyword("_ALPHATEST_ON");mat.renderQueue=(int)RenderQueue.AlphaTest;
                    mat.SetTexture("_EmissionMap",Texture(Path.Combine(directory,"0-emission.png"),false));mat.SetColor("_EmissionColor",Color.white*2);mat.EnableKeyword("_EMISSION");
                }
            }
        }
    }

    public sealed class CargoBusterRig : MonoBehaviour
    {
        CargoBusterAssets assets;
        Transform[] nodes;
        float pose,angle;
        bool flying;
        public int CargoCount { get; private set; }
        public int RotorCount { get; private set; }
        public Bounds RestBounds=>assets==null?new Bounds():assets.RestBounds;
        internal void Initialize()
        {
            assets=CargoBusterAssets.Acquire();nodes=new Transform[assets.Nodes.Length];
            var geometry=new GameObject("BusterGeometry").transform;geometry.SetParent(transform,false);
            geometry.localScale=Vector3.one*assets.Scale;geometry.localRotation=Quaternion.Euler(0,assets.Yaw*Mathf.Rad2Deg,0);geometry.localPosition=-assets.Center*assets.Scale;
            for(int i=0;i<nodes.Length;i++)
            {
                var source=assets.Nodes[i];var node=new GameObject(source.Name).transform;node.SetParent(source.Parent<0?geometry:nodes[source.Parent],false);nodes[i]=node;
                if(source.SpinAxis.sqrMagnitude>.5f)RotorCount++;
                if(source.Mesh>=0)
                {
                    node.gameObject.AddComponent<MeshFilter>().sharedMesh=assets.Meshes[source.Mesh];
                    var renderer=node.gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=assets.Materials[assets.MeshMaterials[source.Mesh]];
                    renderer.shadowCastingMode=ShadowCastingMode.On;renderer.receiveShadows=true;
                }
            }
            Sample(0,0);
        }
        public void SetFlight(bool value,int cargoCount){flying=value;CargoCount=Mathf.Clamp(cargoCount,0,6);}
        public void Advance(float seconds)
        {
            if(assets==null)return;
            float dt=Mathf.Clamp(seconds,0,.1f);pose=Mathf.MoveTowards(pose,flying?1:0,dt/1.5f);
            if(flying)angle=(angle+1500*dt)%360;
            Sample(pose,angle);
        }
        // Deterministic sampling is also used by native bounds/pose acceptance tests.
        public void Sample(float blend,float rotorDegrees)
        {
            if(assets==null)return;blend=Mathf.Clamp01(blend);
            for(int i=0;i<nodes.Length;i++)
            {
                var data=assets.Nodes[i];var node=nodes[i];node.localPosition=Vector3.Lerp(data.Rest.Position,data.Flight.Position,blend);node.localScale=Vector3.Lerp(data.Rest.Scale,data.Flight.Scale,blend);
                node.localRotation=Quaternion.Slerp(data.Rest.Rotation,data.Flight.Rotation,blend);
                if(data.SpinAxis.sqrMagnitude>.5f)node.localRotation*=Quaternion.AngleAxis(rotorDegrees*(data.Name.EndsWith("_L",StringComparison.Ordinal)?1:-1),data.SpinAxis);
            }
        }
        void OnDestroy(){if(assets!=null){assets.Release();assets=null;}}
    }
}
