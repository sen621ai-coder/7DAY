// Lightweight scene/lifecycle fixture; does not simulate Unity rendering or networking.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using PZAEC.BasementLight;
namespace UnityEngine {
 public class Object {public string name; public static void DontDestroyOnLoad(Object o){} public static void Destroy(Object o){DestroyImmediate(o);} public static void DestroyImmediate(Object o){var c=o as Component;if(c!=null)c.gameObject.parts.Remove(c);}}
 public class Component:Object {public GameObject gameObject;public Transform transform{get{return gameObject.transform;}} public string tag{get{return gameObject.tag;}} public T GetComponent<T>() where T:Component{return gameObject.GetComponent<T>();} public T GetComponentInChildren<T>(bool all) where T:Component{return GetComponentsInChildren<T>(all).FirstOrDefault();} public T[] GetComponentsInChildren<T>(bool all) where T:Component{return gameObject.GetComponentsInChildren<T>(all);} }
 public class MonoBehaviour:Component{}
 public class Transform:Component {public Transform parent; public List<Transform> children=new List<Transform>();public Vector3 localPosition,localScale;public void SetParent(Transform p,bool stay){parent=p;p.children.Add(this);} public Transform Find(string name){return children.FirstOrDefault(t=>t.gameObject.name==name);} }
 public class GameObject:Object {public int layer;public string tag="Untagged";public bool activeSelf=true;public Transform transform;public List<Component> parts=new List<Component>();public GameObject(string name){this.name=name;transform=AddComponent<Transform>();}public void SetActive(bool active){activeSelf=active;} public T AddComponent<T>() where T:Component,new(){var c=new T{gameObject=this};parts.Add(c);return c;} public T GetComponent<T>() where T:Component{return parts.OfType<T>().FirstOrDefault();}public T[] GetComponentsInChildren<T>(bool all) where T:Component{return parts.OfType<T>().Concat(transform.children.SelectMany(t=>t.gameObject.GetComponentsInChildren<T>(all))).ToArray();}public static GameObject CreatePrimitive(PrimitiveType type){var g=new GameObject("Cube");g.AddComponent<BoxCollider>();g.AddComponent<Renderer>();return g;} }
 public class Collider:Component{public bool enabled=true;}public class BoxCollider:Collider{public Vector3 center,size;}
 public class Renderer:Component{public Material[] sharedMaterials=new Material[0];public Material sharedMaterial{get{return sharedMaterials.FirstOrDefault();}set{sharedMaterials=new[]{value};}}public Rendering.ShadowCastingMode shadowCastingMode;}
 public class Shader:Object{public bool isSupported=true;public static Shader Find(string name){return new Shader{name=name};}}
 public class Material:Object{public Shader shader=new Shader();public int renderQueue;public Color color;public object mainTexture;public Vector2 mainTextureScale,mainTextureOffset;public Dictionary<string,object> maps=new Dictionary<string,object>();public Material(){}public Material(Shader shader){this.shader=shader;}public void SetTexture(string p,object texture){maps[p]=texture;}public Material(Material source){shader=source.shader;maps=new Dictionary<string,object>(source.maps);} public bool HasProperty(string p){return true;}public void SetFloat(string p,float x){}public void SetColor(string p,Color c){}public void EnableKeyword(string k){} }
 public class Texture2D:Object{public static Texture2D whiteTexture=new Texture2D();}
 public class Cubemap:Object{public TextureWrapMode wrapMode;public FilterMode filterMode;public Cubemap(int size,TextureFormat f,bool mip){}public void SetPixels(Color[] c,CubemapFace f){}public void Apply(bool mip,bool discard){} }
 public class Light:Component{public bool enabled;public LightType type;public float range,intensity,shadowStrength,shadowBias,shadowNormalBias;public Color color;public LightRenderMode renderMode;public LightShadows shadows;public Cubemap cookie;}
 public struct Vector2{public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}public static Vector2 one=new Vector2(1,1),zero=new Vector2();}
 public struct Vector3{public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;} }
 public struct Color{public float r,g,b,a;public Color(float r,float g,float b,float a=1){this.r=r;this.g=g;this.b=b;this.a=a;}public static Color black=new Color(0,0,0);}
 public class SerializeField:Attribute{}
 public static class SystemInfo{public static Rendering.GraphicsDeviceType graphicsDeviceType=Rendering.GraphicsDeviceType.Mock;}
 public static class Time{public static float time;}
 public enum PrimitiveType{Cube}public enum LightType{Point}public enum LightRenderMode{ForcePixel}public enum LightShadows{Soft}public enum TextureFormat{RGBA32}public enum TextureWrapMode{Clamp}public enum FilterMode{Bilinear}public enum CubemapFace{PX,NX,PY,NY,PZ,NZ}
}
namespace UnityEngine.Rendering{public enum GraphicsDeviceType{Null,Mock}public enum ShadowCastingMode{Off}}
namespace HarmonyLib{public class Harmony{public Harmony(string id){}public void Patch(MethodInfo m,HarmonyMethod prefix=null,HarmonyMethod postfix=null){}}public class HarmonyMethod{public HarmonyMethod(Type t,string n){}}public static class AccessTools{public static MethodInfo Method(Type t,string n,Type[] p=null){return null;}}}
public interface IModApi{void InitMod(Mod mod);}public class Mod{}public static class Log{public static void Out(string s){}public static void Error(string s){throw new Exception(s);}}
public class BlockPoweredLight{public void OnBlockEntityTransformAfterActivated(){}}
public class Block{public string name="pzaecBasementPanelLight";public string GetBlockName(){return name;}}
public class BlockShapeModelEntity{public Block block=new Block();}public class GameObjectPool{}
public struct Vector3i{public int x,y,z;}
public class BlockValue{public Block Block=new Block();public int meta;}
public class WorldBase{public BlockValue value=new BlockValue();public TileEntityPoweredBlock tile=new TileEntityPoweredBlock();public BlockValue GetBlock(Vector3i p){return value;}public object GetTileEntity(Vector3i p){return tile;}}
public class TileEntityPoweredBlock{public bool IsToggled;}
public class BlockEntityData{public Transform transform;}
public class RootTransformRefParent:Component{public Transform RootTransform;}
public static class DataLoader{
 public static Transform native;
 public static T LoadAsset<T>(string path,bool flag) where T:class {
  if(native==null){var g=new GameObject("NativeWorkbench");g.tag="T_Mesh_B";g.layer=19;g.AddComponent<BoxCollider>();var m=new Material();m.maps["nativeAuxMask"]=new object();g.AddComponent<Renderer>().sharedMaterial=m;native=g.transform;}
  return native as T;
 }
}
public static class BasementLifecycleTests {
 static int count;static void Check(bool b,string s){count++;if(!b)throw new Exception(s);}
 static void Call(PanelView v,string name){typeof(PanelView).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(v,null);}
 public static void Run(){
  Transform prefab=null;Check(!ModApi.GetPrefab(new BlockShapeModelEntity(),ref prefab),"Custom model not intercepted");
  var root=prefab.gameObject;var view=root.GetComponent<PanelView>();
  var colliders=root.GetComponentsInChildren<Collider>(true);
  Check(colliders.Length==1 && colliders[0].gameObject==root && colliders[0].enabled,"Hits must land on registered root only");
  Check(root.GetComponent<RootTransformRefParent>().RootTransform==prefab,"Explicit root reference missing");
  Check(root.tag==DataLoader.native.tag && root.layer==DataLoader.native.gameObject.layer,"Native hit tag/layer not preserved");
  var bounds=(BoxCollider)colliders[0];Check(bounds.center.y-bounds.size.y/2>=.835f && bounds.center.y+bounds.size.y/2<=1,"Ceiling fixture bounds leave voxel");
  Check(prefab.Find("WireOffset")!=null && Math.Abs(prefab.Find("WireOffset").localPosition.y-.836f)<.0001f,"Native wire anchor missing");
  Check(prefab.Find("MilkDiffuser")!=null && prefab.Find("PanelFrame")!=null && prefab.Find("MainLight")==null,"Native tube remains in custom prefab");
  var renderers=root.GetComponentsInChildren<Renderer>(true);
  Check(renderers.Length==6,"Unexpected visible geometry");
  foreach(var r in renderers){Check(r.sharedMaterial.maps.ContainsKey("nativeAuxMask"),"Shader auxiliary mask erased");Check(r.sharedMaterial.mainTexture==Texture2D.whiteTexture,"Opaque albedo missing");}
  foreach(string field in new[]{"lamp","fill","diffuser"})Check(typeof(PanelView).GetField(field,BindingFlags.NonPublic|BindingFlags.Instance).IsDefined(typeof(SerializeField),true),"Cloned runtime reference not serialized");
  var lights=root.GetComponentsInChildren<Light>(true);Check(lights.Length==2,"Expected main and fill only");var light=lights.Single(l=>l.gameObject.name=="BasementSoftlight");var fill=lights.Single(l=>l.gameObject.name=="BasementWallCeilingFill");Check(fill.intensity==.14f && fill.range==14f && fill.shadowStrength==1,"Fill brightness/occlusion changed");Check(!light.enabled,"Preview starts illuminated");
  Check(light.intensity==1f && light.range==20f,"Runtime did not use revised brightness and range");
  var world=new WorldBase();view.Bind(world,new Vector3i());Call(view,"Update");Check(!light.enabled,"Unpowered lamp emits light");
  world.value.meta=2;world.tile.IsToggled=true;Time.time=1;Call(view,"Update");Check(light.enabled && fill.enabled,"Powered and toggled lamp remains dark");Check(prefab.Find("MilkDiffuser").GetComponent<Renderer>().sharedMaterial.shader.name=="Unlit/Color","Powered diffuser still depends on room lighting");
  world.tile.IsToggled=false;Time.time=2;Call(view,"Update");Check(!light.enabled && !fill.enabled,"Switch off ignored");Check(prefab.Find("MilkDiffuser").GetComponent<Renderer>().sharedMaterial.name=="Basement diffuser off","Off diffuser still glows");
  world.tile.IsToggled=true;Time.time=3;Call(view,"Update");Call(view,"OnDisable");Check(!light.enabled && !fill.enabled && view.World==null,"Pooled lamp keeps stale world or light");
  var world2=new WorldBase();view.Bind(world2,new Vector3i());Time.time=4;Call(view,"Update");Check(!light.enabled && view.World==world2,"Reactivated lamp uses stale power");
  Transform same=null;ModApi.GetPrefab(new BlockShapeModelEntity(),ref same);Check(same==prefab,"Prefab cache not reused");
  var other=new BlockShapeModelEntity();other.block.name="ceilingLight07_player";Check(ModApi.GetPrefab(other,ref same),"Other lamps intercepted");
  var retained=renderers[0].sharedMaterial;ModApi.BeforePoolDestroy(root);Check(renderers.All(r=>r.sharedMaterials.Length==0)&&retained.maps.ContainsKey("nativeAuxMask"),"Pool cleanup can destroy shared material");
  Call(view,"OnDisable");Check(renderers.All(r=>r.sharedMaterials.Length==0),"Retirement rebound shared materials before native cleanup");SystemInfo.graphicsDeviceType=UnityEngine.Rendering.GraphicsDeviceType.Null;
  var server=new GameObject("DedicatedPanel");server.AddComponent<PanelView>().Build();
  Check(server.GetComponentsInChildren<Collider>(true).Length==1 && server.transform.Find("WireOffset")!=null,"Headless server lacks collision or anchor");
  Check(server.GetComponentsInChildren<Light>(true).Length==0,"Headless server creates rendering resources");
  Console.WriteLine("PASS: "+count+" actual-source geometry, root hit reference, anchor, shader bindings, power and pooling assertions (mock Unity scene; not in-game rendering).");
 }
}

