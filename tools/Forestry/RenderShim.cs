// Offline geometry adapter only. Compiles the actual prefab builders without a
// Unity player; no game DLL is replaced. Primitives are equivalent low-poly meshes.
using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using N=System.Numerics;
namespace UnityEngine.Rendering { public enum IndexFormat { UInt32 } }
namespace UnityEngine {
 [Flags] public enum HideFlags {None=0,DontUnloadUnusedAsset=32}
 public class Object {
  static int nextId;readonly int id=--nextId;public bool destroyed;public HideFlags hideFlags;public int GetInstanceID()=>id;
  public virtual string name{get;set;}
  public static bool operator ==(Object a,Object b){bool na=ReferenceEquals(a,null)||a.destroyed,nb=ReferenceEquals(b,null)||b.destroyed;return na||nb?na&&nb:ReferenceEquals(a,b);}
  public static bool operator !=(Object a,Object b)=>!(a==b);public override bool Equals(object o)=>ReferenceEquals(this,o);public override int GetHashCode()=>id;
  public static void DestroyImmediate(Object o){if(ReferenceEquals(o,null))return;o.destroyed=true;if(o is Component c)c.gameObject.components.Remove(c);}
  public static void Destroy(Object o)=>DestroyImmediate(o);public static void DontDestroyOnLoad(Object o){}
  public static T Instantiate<T>(T o)where T:Object{
   if(o is Mesh m)return new Mesh{name=m.name,vertices=(Vector3[])m.vertices.Clone(),normals=(Vector3[])m.normals.Clone(),uv=(Vector2[])m.uv.Clone(),triangles=(int[])m.triangles.Clone()} as T;
   throw new NotSupportedException("Only mesh recovery cloning is modeled here");
  }
 }
 public static class Time{public static float unscaledTime;}
 public struct Vector2 {public float x,y;public Vector2(float a,float b){x=a;y=b;}public static Vector2 zero=>new Vector2();public static Vector2 right=>new Vector2(1,0);public static Vector2 up=>new Vector2(0,1);}
 public struct Vector3 {
  public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}
  public N.Vector3 N=>new N.Vector3(x,y,z);public static Vector3 From(N.Vector3 v)=>new Vector3(v.X,v.Y,v.Z);
  public float magnitude=>N.Length();public Vector3 normalized=>magnitude<1e-8?new Vector3():this/magnitude;
  public static Vector3 up=>new Vector3(0,1,0);public static Vector3 left=>new Vector3(-1,0,0);public static Vector3 right=>new Vector3(1,0,0);
  public static Vector3 operator +(Vector3 a,Vector3 b)=>From(a.N+b.N);public static Vector3 operator -(Vector3 a,Vector3 b)=>From(a.N-b.N);
  public static Vector3 operator *(Vector3 a,float f)=>From(a.N*f);public static Vector3 operator /(Vector3 a,float f)=>From(a.N/f);
  public static float Distance(Vector3 a,Vector3 b)=>(a-b).magnitude;
 }
 public struct Quaternion {
  public N.Quaternion q;public Quaternion(N.Quaternion v){q=v;}public static Quaternion identity=>new Quaternion(N.Quaternion.Identity);
  public static Quaternion Euler(float x,float y,float z)=>new Quaternion(N.Quaternion.CreateFromYawPitchRoll(y*Mathf.PI/180,x*Mathf.PI/180,z*Mathf.PI/180));
  public static Quaternion FromToRotation(Vector3 a,Vector3 b){var v=a.normalized.N;var w=b.normalized.N;float d=N.Vector3.Dot(v,w);if(d<-.99999f)return new Quaternion(N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitX,Mathf.PI));return new Quaternion(N.Quaternion.Normalize(new N.Quaternion(N.Vector3.Cross(v,w),1+d)));}
 }
 public struct Matrix4x4 {
  public N.Matrix4x4 m;public Matrix4x4(N.Matrix4x4 v){m=v;}public static Matrix4x4 operator *(Matrix4x4 a,Matrix4x4 b)=>new Matrix4x4(b.m*a.m);
  public Vector3 Point(Vector3 p)=>Vector3.From(N.Vector3.Transform(p.N,m));
  public Vector3 Normal(Vector3 n){N.Matrix4x4.Invert(m,out var inv);return Vector3.From(N.Vector3.TransformNormal(n.N,N.Matrix4x4.Transpose(inv))).normalized;}
 }
 public static class Mathf {public const float PI=(float)Math.PI;public static float Sin(float a)=>(float)Math.Sin(a);public static float Cos(float a)=>(float)Math.Cos(a);public static float Sqrt(float a)=>(float)Math.Sqrt(a);public static float Max(float a,float b)=>Math.Max(a,b);}
 public struct Color {public float r,g,b,a;public Color(float x,float y,float z,float w=1){r=x;g=y;b=z;a=w;}public static Color white=>new Color(1,1,1);public static Color black=>new Color(0,0,0);}
 public class Component:Object {public GameObject gameObject;public Transform transform=>gameObject.transform;public override string name{get=>gameObject.name;set=>gameObject.name=value;}public T GetComponent<T>() where T:Component=>gameObject.GetComponent<T>();public T[] GetComponentsInChildren<T>(bool includeInactive=false) where T:Component=>gameObject.GetComponentsInChildren<T>(includeInactive);public T GetComponentInChildren<T>(bool includeInactive=false) where T:Component=>GetComponentsInChildren<T>(includeInactive).FirstOrDefault();}
 public class Transform:Component,IEnumerable<Transform> {
  public Vector3 localPosition,localScale=new Vector3(1,1,1);public Quaternion localRotation=Quaternion.identity;public Transform parent;public List<Transform> children=new List<Transform>();
  public void SetParent(Transform p,bool world){parent?.children.Remove(this);parent=p;p?.children.Add(this);}
  public Matrix4x4 localToWorldMatrix {get {var l=new Matrix4x4(N.Matrix4x4.CreateScale(localScale.N)*N.Matrix4x4.CreateFromQuaternion(localRotation.q)*N.Matrix4x4.CreateTranslation(localPosition.N));return parent==null?l:parent.localToWorldMatrix*l;}}
  public Matrix4x4 worldToLocalMatrix {get {N.Matrix4x4.Invert(localToWorldMatrix.m,out var inv);return new Matrix4x4(inv);}}
  public IEnumerator<Transform> GetEnumerator()=>children.GetEnumerator();IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
 }
 public enum PrimitiveType {Cube,Cylinder} public class GameObject:Object {
  public Transform transform;public int layer;public string tag;public bool activeSelf=true;public bool activeInHierarchy=>activeSelf&&(transform.parent==null||transform.parent.gameObject.activeInHierarchy);
  public List<Component> components=new List<Component>();public GameObject(string n=""){name=n;transform=new Transform{gameObject=this};components.Add(transform);}
  public T AddComponent<T>()where T:Component,new(){var c=new T{gameObject=this};components.Add(c);return c;}
  public T GetComponent<T>()where T:Component=>components.OfType<T>().FirstOrDefault();
  public T[] GetComponentsInChildren<T>(bool includeInactive=false)where T:Component {var r=new List<T>(components.OfType<T>());foreach(var t in transform.children)if(includeInactive||t.gameObject.activeInHierarchy)r.AddRange(t.gameObject.GetComponentsInChildren<T>(includeInactive));return r.ToArray();}
  public void SetActive(bool a){activeSelf=a;}
  public static GameObject CreatePrimitive(PrimitiveType t){var g=new GameObject(t.ToString());g.AddComponent<MeshFilter>().sharedMesh=PrimitiveMesh.Create(t);g.AddComponent<MeshRenderer>();g.AddComponent<BoxCollider>();return g;}
 }
 public class Collider:Component{}public class MeshCollider:Collider{public Mesh sharedMesh;}public class BoxCollider:Collider{public Vector3 center,size;public bool isTrigger;}public class CapsuleCollider:Collider{public int direction;public float radius,height;}
 public class MonoBehaviour:Component{}
 public class Renderer:Component{public Material sharedMaterial;public Material[] sharedMaterials{get=>sharedMaterial==null?new Material[0]:new[]{sharedMaterial};set=>sharedMaterial=value.Length==0?null:value[0];}}public class MeshRenderer:Renderer{}public class MeshFilter:Component{public Mesh sharedMesh;}
 public class Shader:Object{public bool isSupported=true;public static Shader Find(string n)=>new Shader{name=n};}
 public static class SystemInfo{public static string graphicsDeviceType=>"OfflineStub";}
 public enum TextureFormat{RGBA32}public enum TextureWrapMode{Repeat}public enum FilterMode{Trilinear}
 public class Texture:Object{}
 public class Texture2D:Texture{public static readonly Texture2D whiteTexture=new Texture2D(1,1,TextureFormat.RGBA32,false,false){name="White"};public Texture2D(int w,int h,TextureFormat f,bool mip,bool lin){}public TextureWrapMode wrapMode;public int anisoLevel;public FilterMode filterMode;}
 public static class ImageConversion{public static bool LoadImage(Texture2D t,byte[] b,bool r)=>true;}
 public class Material:Object {
  public Color color=Color.white;public Texture mainTexture;public Vector2 mainTextureScale=new Vector2(1,1),mainTextureOffset;public int renderQueue;
  public Dictionary<string,Texture> maps=new Dictionary<string,Texture>();
  public Shader shader; public string[] shaderKeywords=new string[0];public bool HasProperty(string n)=>true;public string[] GetTexturePropertyNames()=>maps.Keys.Concat(new[]{"_MainTex"}).Distinct().ToArray();
  public Texture GetTexture(string n)=>n=="_MainTex"?mainTexture:maps.TryGetValue(n,out var t)?t:null;
  public Material(Shader s){shader=s;}public Material(Material m){if(m==null)throw new ArgumentNullException("source");shader=m.shader;shaderKeywords=m.shaderKeywords;color=m.color;mainTexture=m.mainTexture;mainTextureScale=m.mainTextureScale;mainTextureOffset=m.mainTextureOffset;maps=new Dictionary<string,Texture>(m.maps);}
  public void SetFloat(string n,float f){}public void SetColor(string n,Color c){}public void SetTexture(string n,Texture t){maps[n]=t;if(n=="_MainTex")mainTexture=t;}public void EnableKeyword(string k){}public void SetOverrideTag(string k,string v){}
 }
 public struct CombineInstance{public Mesh mesh;public Matrix4x4 transform;}
 public class Mesh:Object {
  public Rendering.IndexFormat indexFormat;public Vector3[] vertices=new Vector3[0],normals=new Vector3[0];public Vector2[] uv=new Vector2[0];public int[] triangles=new int[0];
  public void SetVertices(List<Vector3> a){vertices=a.ToArray();}public void SetNormals(List<Vector3> a){normals=a.ToArray();}public void SetUVs(int n,List<Vector2> a){uv=a.ToArray();}public void SetTriangles(List<int> a,int n){triangles=a.ToArray();}
  public void RecalculateBounds(){}public void RecalculateTangents(){}
  public void RecalculateNormals(){var nn=new N.Vector3[vertices.Length];for(int i=0;i<triangles.Length;i+=3){int a=triangles[i],b=triangles[i+1],c=triangles[i+2];var n=N.Vector3.Cross(vertices[b].N-vertices[a].N,vertices[c].N-vertices[a].N);nn[a]+=n;nn[b]+=n;nn[c]+=n;}normals=nn.Select(v=>Vector3.From(v.LengthSquared()>1e-16?N.Vector3.Normalize(v):N.Vector3.UnitY)).ToArray();}
  public void CombineMeshes(CombineInstance[] parts,bool merge,bool transform){var v=new List<Vector3>();var n=new List<Vector3>();var u=new List<Vector2>();var f=new List<int>();foreach(var p in parts){int off=v.Count;v.AddRange(p.mesh.vertices.Select(p.transform.Point));n.AddRange(p.mesh.normals.Select(p.transform.Normal));u.AddRange(p.mesh.uv);f.AddRange(p.mesh.triangles.Select(i=>i+off));}vertices=v.ToArray();normals=n.ToArray();uv=u.ToArray();triangles=f.ToArray();}
 }
 public static class PrimitiveMesh {
  public static Mesh Create(PrimitiveType type){var m=new Mesh{name=type.ToString()};var v=new List<Vector3>();var uv=new List<Vector2>();var f=new List<int>();
   if(type==PrimitiveType.Cube){
    var axes=new[]{new Vector3(1,0,0),new Vector3(-1,0,0),new Vector3(0,1,0),new Vector3(0,-1,0),new Vector3(0,0,1),new Vector3(0,0,-1)};
    foreach(var n in axes){var u=Vector3.From(N.Vector3.Cross(Math.Abs(n.y)>.5f?N.Vector3.UnitZ:N.Vector3.UnitY,n.N));var w=Vector3.From(N.Vector3.Cross(n.N,u.N));int o=v.Count;v.Add(n*.5f-u*.5f-w*.5f);v.Add(n*.5f+u*.5f-w*.5f);v.Add(n*.5f+u*.5f+w*.5f);v.Add(n*.5f-u*.5f+w*.5f);uv.AddRange(new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(1,1),new Vector2(0,1)});f.AddRange(new[]{o,o+1,o+2,o,o+2,o+3});}
   }else{
    const int s=24;for(int ring=0;ring<2;ring++)for(int i=0;i<=s;i++){float a=i*Mathf.PI*2/s;v.Add(new Vector3(Mathf.Cos(a)*.5f,ring*2-1,Mathf.Sin(a)*.5f));uv.Add(new Vector2(i/(float)s,ring));}
    for(int i=0;i<s;i++){int a=i,b=i+s+1;f.AddRange(new[]{a,b,a+1,a+1,b,b+1});}
    for(int end=0;end<2;end++){int o=v.Count;v.Add(new Vector3(0,end*2-1,0));uv.Add(new Vector2(.5f,.5f));for(int i=0;i<=s;i++){float a=i*Mathf.PI*2/s;v.Add(new Vector3(Mathf.Cos(a)*.5f,end*2-1,Mathf.Sin(a)*.5f));uv.Add(new Vector2(Mathf.Cos(a)*.5f+.5f,Mathf.Sin(a)*.5f+.5f));if(i<s)f.AddRange(end==0?new[]{o,o+i+1,o+i+2}:new[]{o,o+i+2,o+i+1});}}
   }m.vertices=v.ToArray();m.uv=uv.ToArray();m.triangles=f.ToArray();m.RecalculateNormals();return m;
  }
 }
}
namespace HarmonyLib{public class Harmony{public Harmony(string id){}public void UnpatchSelf(){}public void Patch(object m,HarmonyMethod prefix=null){}}public class HarmonyMethod{public HarmonyMethod(Type t,string n){}}public static class AccessTools{public static object Method(Type t,string n,Type[] args=null)=>null;}}
public class GameObjectPool{}
public class BlockCollector{}
public class BlockShapeModelEntity{public Block block;}public class Block{public Props Properties=new Props();public string GetBlockName()=>"yfAutoForestry";}public class Props{public Dictionary<string,string> values=new Dictionary<string,string>();public string GetValue(string n)=>values.TryGetValue(n,out var v)?v:"";}
public class RootTransformRefParent : UnityEngine.Component {
 public UnityEngine.Transform RootTransform;
 // Mirrors the installed game's FindRoot: without a reference it returns the hit child.
 public static UnityEngine.Transform FindRoot(UnityEngine.Transform hit){
  for(var t=hit;t!=null;t=t.parent){var r=t.GetComponent<RootTransformRefParent>();if(r!=null)return r.RootTransform;}
  return hit;
 }
}
public static class GameManager{public static bool IsDedicatedServer=>false;}
public static class Log{public static void Out(string s){}public static void Error(string s){Console.WriteLine(s);}}
public static class DataLoader{public static T LoadAsset<T>(string s,bool b)where T:class{var g=new UnityEngine.GameObject("NativeColliderReference");g.AddComponent<UnityEngine.BoxCollider>();var m=new UnityEngine.Material(UnityEngine.Shader.Find("NativeWorkstation")){name="NativeWorkstationMaterial"};m.SetTexture("_NativeAuxiliaryMask",UnityEngine.Texture2D.whiteTexture);g.AddComponent<UnityEngine.MeshRenderer>().sharedMaterial=m;return g.transform as T;}}
namespace AECT16RuntimeFix{public static class AutoForestryActivity{public static void Install(HarmonyLib.Harmony h){}}}
public static class ForestryRenderExport {
 static void String(BinaryWriter w,string s){var b=System.Text.Encoding.UTF8.GetBytes(s??"");w.Write(b.Length);w.Write(b);}
 public static void Run(string assets,string output){
  var type=typeof(AECT16RuntimeFix.AutoForestryModel);type.GetField("assetPath",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).SetValue(null,assets);
  var root=(UnityEngine.Transform)type.GetMethod("CreatePrefab",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).Invoke(null,null);
  root.SetParent(null,false);
  foreach(var collider in root.GetComponentsInChildren<UnityEngine.Collider>(true)){
   if(RootTransformRefParent.FindRoot(collider.transform)!=root || collider.gameObject.tag!="T_Block")
    throw new InvalidOperationException("Forestry collider cannot resolve its block: "+collider.name);
  }
  Console.WriteLine("PASS: all forestry collider hits resolve to the block root, including inactive upgrades.");
  // Deliberately show full inventory/all three upgrades, rather than imply a save state.
  foreach(var t in root.GetComponentsInChildren<UnityEngine.Transform>(true))if(t.name=="ForestrySpeed"||t.name=="ForestryPacker"||t.name=="ForestrySiren")t.gameObject.SetActive(true);
  var renderers=root.GetComponentsInChildren<UnityEngine.MeshRenderer>().Where(r=>r.sharedMaterial!=null).ToArray();var materials=renderers.Select(r=>r.sharedMaterial).Distinct().ToArray();
  foreach(var m in materials)if(m.shader==null||m.shader.name!="NativeWorkstation"||!m.shader.isSupported)
   throw new InvalidOperationException("Forestry surface did not inherit the native shader: "+m.name);
  Console.WriteLine("PASS: every exported material uses the native workstation shader; no standalone Standard lookup.");
  foreach(var m in materials)if(!m.maps.TryGetValue("_NativeAuxiliaryMask",out var mask)||mask==null)
   throw new InvalidOperationException("Native auxiliary texture was cleared: "+m.name);
  Console.WriteLine("PASS: native auxiliary texture bindings survive all forestry material copies.");
  using(var w=new BinaryWriter(File.Create(output))){w.Write(new byte[]{89,70,82,49});w.Write(materials.Length);w.Write(renderers.Length);
   foreach(var m in materials){String(w,m.name);String(w,m.mainTexture==UnityEngine.Texture2D.whiteTexture?null:m.mainTexture?.name.Replace("Forestry_",""));w.Write(m.color.r);w.Write(m.color.g);w.Write(m.color.b);w.Write(m.color.a);w.Write(m.mainTextureScale.x);w.Write(m.mainTextureScale.y);w.Write(m.mainTextureOffset.x);w.Write(m.mainTextureOffset.y);}
   foreach(var r in renderers){var mesh=r.GetComponent<UnityEngine.MeshFilter>().sharedMesh;var mat=r.transform.localToWorldMatrix;String(w,r.name);w.Write(Array.IndexOf(materials,r.sharedMaterial));w.Write(mesh.vertices.Length);w.Write(mesh.triangles.Length);for(int i=0;i<mesh.vertices.Length;i++){var v=mat.Point(mesh.vertices[i]);var n=mat.Normal(mesh.normals[i]);w.Write(v.x);w.Write(v.y);w.Write(v.z);w.Write(n.x);w.Write(n.y);w.Write(n.z);w.Write(mesh.uv[i].x);w.Write(mesh.uv[i].y);}foreach(int i in mesh.triangles)w.Write(i);}
  }
  Console.WriteLine("Exported {0} renderers, {1} materials, {2} triangles",renderers.Length,materials.Length,renderers.Sum(r=>r.GetComponent<UnityEngine.MeshFilter>().sharedMesh.triangles.Length/3));
  // Exercise the production prefix with two users of one runtime material.
  // Mirror native cleanup: every runtime material still attached is destroyed.
  var cleanup=type.GetMethod("BeforePoolDestroy",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
  var shared=renderers[0].sharedMaterial;
  var destroyed=new HashSet<UnityEngine.Material>();
  for(int pass=0;pass<3;pass++){
   var retiring=new UnityEngine.GameObject("RetiringForestry");retiring.AddComponent<AECT16RuntimeFix.ForestrySharedMaterialOwner>();
   retiring.AddComponent<UnityEngine.MeshRenderer>().sharedMaterial=shared;
   var child=new UnityEngine.GameObject("InactiveStock");child.transform.SetParent(retiring.transform,false);child.AddComponent<UnityEngine.MeshRenderer>().sharedMaterial=shared;child.SetActive(false);
   cleanup.Invoke(null,new object[]{retiring});
   foreach(var r in retiring.GetComponentsInChildren<UnityEngine.Renderer>(true))foreach(var m in r.sharedMaterials)destroyed.Add(m);
   if(destroyed.Contains(shared)||renderers[0].sharedMaterial!=shared)throw new Exception("Pool cleanup destroyed live forestry material");
  }
  var other=new UnityEngine.GameObject("UnrelatedBlock");var otherRenderer=other.AddComponent<UnityEngine.MeshRenderer>();otherRenderer.sharedMaterial=shared;
  cleanup.Invoke(null,new object[]{other});
  if(otherRenderer.sharedMaterial!=shared)throw new Exception("Cleanup guard affected unrelated block");
  Console.WriteLine("PASS: repeated pool disposal preserves live shared materials; inactive children protected; unrelated blocks unchanged.");
  // Inject lost GPU objects into the real recovery code; two instances share them.
  var live=new UnityEngine.GameObject("LiveForestry");
  var liveRenderer=live.AddComponent<UnityEngine.MeshRenderer>();liveRenderer.sharedMaterial=shared;
  var originalMesh=renderers[0].GetComponent<UnityEngine.MeshFilter>().sharedMesh;
  live.AddComponent<UnityEngine.MeshFilter>().sharedMesh=originalMesh;
  live.AddComponent<UnityEngine.MeshCollider>().sharedMesh=originalMesh;
  var liveOwner=live.AddComponent<AECT16RuntimeFix.ForestrySharedMaterialOwner>();liveOwner.Capture();
  var originalTexture=shared.mainTexture;int triangleCount=originalMesh.triangles.Length;
  UnityEngine.Object.Destroy(originalTexture);UnityEngine.Object.Destroy(shared);UnityEngine.Object.Destroy(originalMesh);
  UnityEngine.Time.unscaledTime=10;
  if(!AECT16RuntimeFix.ForestryResources.Check(true))throw new Exception("Recovery failed");
  root.GetComponent<AECT16RuntimeFix.ForestrySharedMaterialOwner>().Restore();liveOwner.Restore();
  if(liveRenderer.sharedMaterial==null||liveRenderer.sharedMaterial!=renderers[0].sharedMaterial
      ||liveRenderer.sharedMaterial.mainTexture==null)throw new Exception("Live instances did not recover shared material/texture");
  var repairedMesh=live.GetComponent<UnityEngine.MeshFilter>().sharedMesh;
  if(repairedMesh==null||repairedMesh.triangles.Length!=triangleCount||live.GetComponent<UnityEngine.MeshCollider>().sharedMesh!=repairedMesh)
      throw new Exception("Render/collision mesh recovery diverged");
  liveRenderer.sharedMaterials=new UnityEngine.Material[0];liveOwner.Restore();
  if(liveRenderer.sharedMaterial==null)throw new Exception("Empty renderer slots were not rebound");
  var checkpoint=AECT16RuntimeFix.ForestryResources.Begin();
  var failedAsset=AECT16RuntimeFix.ForestryResources.Own(new UnityEngine.Material(UnityEngine.Shader.Find("Failure")));
  AECT16RuntimeFix.ForestryResources.MaterialId(failedAsset);
  AECT16RuntimeFix.ForestryResources.Rollback(checkpoint);
  if(failedAsset!=null||liveRenderer.sharedMaterial==null)throw new Exception("Rollback destroyed committed resources or leaked failed allocation");
  cleanup.Invoke(null,new object[]{live});liveOwner.Restore();
  if(liveRenderer.sharedMaterials.Length!=0)throw new Exception("Retiring instance rebound materials during native cleanup");
  Console.WriteLine("PASS: destroyed material+texture+mesh repaired across two instances; collider restored; empty slots rebound; allocation rollback isolated; retiring instance stays detached.");
  var survivor=renderers[0].sharedMaterial;
  UnityEngine.Object.Destroy(survivor.shader);UnityEngine.Time.unscaledTime=20;
  if(!AECT16RuntimeFix.ForestryResources.Check(true)||survivor.shader==null||!survivor.shader.isSupported)
   throw new Exception("Destroyed native shader was not reacquired");
  Console.WriteLine("PASS: lost native shader reacquired from the workstation template.");
  var getPrefab=type.GetMethod("GetPrefab",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
  var shape=new BlockShapeModelEntity{block=new Block()};shape.block.Properties.values["Model"]="yfAutoForestryRuntime.prefab";
  shape.block.Properties.values["MultiBlockDim"]="10,4,6";object[] args={shape,null};getPrefab.Invoke(null,args);var large=(UnityEngine.Transform)args[1];
  shape.block.Properties.values["MultiBlockDim"]="6,4,4";args[1]=null;getPrefab.Invoke(null,args);var compact=(UnityEngine.Transform)args[1];
  shape.block.Properties.values["MultiBlockDim"]="10,4,6";args[1]=null;getPrefab.Invoke(null,args);
  if(!Object.ReferenceEquals(args[1],large)||compact==large||Math.Abs(compact.GetComponent<UnityEngine.BoxCollider>().size.x-5.97f)>.001f
     ||Math.Abs(large.GetComponent<UnityEngine.BoxCollider>().size.x-9.95f)>.001f)throw new Exception("Mode cache reused wrong footprint");
  Console.WriteLine("PASS: large/compact/large mode switching preserves independent caches and collision sizes.");
 }
}
