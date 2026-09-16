using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Original procedural assets. Geometry stays inside the existing one-block footprint.
public static class MachineAssetBuild
{
 public static readonly string[] Kinds={"Sorter","Kitchen","Smelter","Forge","Recycler","Farm","Miner","Transfer","WaterPump","WaterTank","AmmoFeed"};
 static Material frame,metal,dark,accent,hot,blue,green;
 static Material Mat(string n,Color c){var m=new Material(Shader.Find("Standard"));m.color=c;m.SetFloat("_Glossiness",.32f);AssetDatabase.CreateAsset(m,"Assets/Machines/"+n+".mat");return m;}
 static GameObject Part(Transform p,string n,Vector3 at,Vector3 size,Material m,PrimitiveType type=PrimitiveType.Cube){var g=GameObject.CreatePrimitive(type);g.name=n;g.transform.SetParent(p,false);g.transform.localPosition=at;g.transform.localScale=size;g.GetComponent<Renderer>().sharedMaterial=m;UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());return g;}
 static void B(Transform p,string n,float x,float y,float z,float sx,float sy,float sz,Material m){Part(p,n,new Vector3(x,y,z),new Vector3(sx,sy,sz),m);}
 static GameObject C(Transform p,string n,float x,float y,float z,float radius,float height,Material m){return Part(p,n,new Vector3(x,y,z),new Vector3(radius*2,height/2,radius*2),m,PrimitiveType.Cylinder);}
 static void Pipe(Transform p,Vector3 a,Vector3 b,float radius,Material m){var g=Part(p,"Pipe",(a+b)/2,new Vector3(radius*2,(b-a).magnitude/2,radius*2),m,PrimitiveType.Cylinder);g.transform.localRotation=Quaternion.FromToRotation(Vector3.up,b-a);}
 static void Control(Transform p){B(p,"ControlPanel",.25f,.29f,-.423f,.28f,.18f,.035f,dark);B(p,"Display",.22f,.31f,-.447f,.17f,.06f,.012f,blue);B(p,"Switch",.33f,.26f,-.447f,.045f,.035f,.014f,accent);}
 static GameObject Create(string kind){
  var g=new GameObject("Machine"+kind);var p=g.transform;
  B(p,"Base",0,.06f,0,.9f,.12f,.9f,frame);
  foreach(float x in new[]{-.34f,.34f})B(p,"SafetyRail",x,.13f,-.39f,.14f,.04f,.06f,accent);
  switch(kind){
   case "Miner":
    foreach(float x in new[]{-.34f,.34f})B(p,"Mast",x,.51f,.18f,.10f,.76f,.12f,frame);
    B(p,"Crosshead",0,.85f,.18f,.78f,.14f,.25f,accent);C(p,"Motor",0,.71f,.14f,.17f,.22f,metal);
    C(p,"DrillShaft",0,.43f,.14f,.06f,.42f,metal);
    for(int i=0;i<7;i++){var tooth=Part(p,"DrillFlight",new Vector3(0,.20f+i*.065f,.14f),new Vector3(.30f,.035f,.09f),metal);tooth.transform.localRotation=Quaternion.Euler(0,i*48,12);}
    B(p,"PowerPack",0,.28f,-.27f,.55f,.3f,.25f,frame);break;
   case "Recycler":
    B(p,"CrusherBody",0,.35f,0,.78f,.46f,.72f,frame);
    foreach(float x in new[]{-.18f,.18f}){var r=C(p,"CrusherRoller",x,.62f,0,.14f,.52f,metal);r.transform.localRotation=Quaternion.Euler(90,0,0);for(int i=0;i<5;i++)B(p,"Tooth",x,.75f,-.22f+i*.11f,.20f,.055f,.055f,accent);}
    foreach(float x in new[]{-.39f,.39f})B(p,"HopperWall",x,.70f,0,.06f,.32f,.77f,metal);
    B(p,"Outlet",0,.24f,-.38f,.43f,.17f,.10f,dark);break;
   case "Smelter":
    C(p,"Furnace",0,.46f,.05f,.32f,.67f,frame);C(p,"Rim",0,.79f,.05f,.35f,.06f,metal);
    B(p,"FireDoor",0,.42f,-.275f,.39f,.33f,.10f,dark);B(p,"HotWindow",0,.43f,-.332f,.25f,.21f,.018f,hot);
    C(p,"Flue",.25f,.80f,.25f,.07f,.27f,metal);break;
   case "Forge":
    foreach(float x in new[]{-.32f,.32f})B(p,"PressColumn",x,.5f,.16f,.12f,.76f,.16f,metal);
    B(p,"PressTop",0,.86f,.12f,.81f,.14f,.43f,frame);C(p,"Ram",0,.67f,.05f,.08f,.25f,metal);
    B(p,"PressDie",0,.53f,.04f,.4f,.12f,.32f,accent);B(p,"Anvil",0,.29f,.04f,.50f,.22f,.37f,metal);break;
   case "Kitchen":
    B(p,"Oven",0,.38f,0,.80f,.52f,.75f,metal);B(p,"OvenWindow",0,.36f,-.382f,.52f,.26f,.016f,dark);
    B(p,"Handle",0,.52f,-.41f,.45f,.035f,.055f,frame);
    foreach(float x in new[]{-.21f,.21f}){C(p,"Burner",x,.66f,0,.15f,.025f,dark);C(p,"CookingPot",x,.75f,0,.12f,.16f,metal);C(p,"Lid",x,.84f,0,.14f,.025f,frame);}
    break;
   case "Farm":
    B(p,"GrowTray",0,.25f,0,.8f,.23f,.7f,metal);B(p,"Soil",0,.38f,0,.68f,.05f,.59f,dark);
    foreach(float x in new[]{-.32f,.32f}){B(p,"IrrigationPost",x,.61f,.24f,.045f,.53f,.045f,frame);}
    Pipe(p,new Vector3(-.32f,.87f,.24f),new Vector3(.32f,.87f,.24f),.035f,blue);
    foreach(float x in new[]{-.19f,0,.19f}){Pipe(p,new Vector3(x,.85f,.24f),new Vector3(x,.85f,-.18f),.018f,metal);B(p,"Seedling",x,.49f,0,.04f,.19f,.035f,green);var leaf=Part(p,"Leaves",new Vector3(x,.53f,0),new Vector3(.17f,.04f,.10f),green,PrimitiveType.Sphere);}
    break;
   case "WaterTank":
    C(p,"Tank",0,.49f,0,.34f,.71f,blue);foreach(float y in new[]{.23f,.71f})C(p,"TankBand",0,y,0,.35f,.055f,metal);
    C(p,"Cap",0,.87f,0,.12f,.06f,metal);B(p,"LevelGauge",0,.48f,-.345f,.055f,.43f,.025f,dark);
    Pipe(p,new Vector3(.20f,.23f,-.15f),new Vector3(.20f,.23f,-.44f),.05f,metal);break;
   case "WaterPump":
    var motor=C(p,"PumpMotor",-.12f,.38f,.08f,.20f,.45f,frame);motor.transform.localRotation=Quaternion.Euler(0,0,90);
    C(p,"PumpHousing",.22f,.34f,.08f,.15f,.31f,blue);
    Pipe(p,new Vector3(.22f,.35f,.08f),new Vector3(.22f,.75f,.08f),.07f,metal);Pipe(p,new Vector3(.22f,.75f,.08f),new Vector3(.22f,.75f,-.42f),.07f,metal);
    for(int i=0;i<5;i++)B(p,"CoolingFin",-.30f+i*.08f,.39f,.08f,.025f,.44f,.43f,metal);break;
   case "AmmoFeed":
    B(p,"Magazine",-.12f,.47f,.1f,.52f,.68f,.57f,frame);B(p,"FeedHousing",.25f,.47f,0,.22f,.23f,.55f,metal);
    for(int i=0;i<5;i++){C(p,"Round",-.29f+i*.085f,.57f,-.24f,.03f,.26f,accent);}
    B(p,"AmmoBelt",0,.39f,-.26f,.49f,.045f,.055f,dark);break;
   default:
    B(p,"Drive",0,.26f,0,.73f,.26f,.73f,frame);
    for(int i=0;i<6;i++){var roller=C(p,"Roller",0,.45f,-.31f+i*.125f,.055f,.68f,metal);roller.transform.localRotation=Quaternion.Euler(0,0,90);}
    if(kind=="Sorter"){foreach(float x in new[]{-.37f,.37f})B(p,"ScannerPost",x,.63f,.12f,.065f,.48f,.08f,accent);B(p,"ScannerBeam",0,.85f,.12f,.80f,.07f,.12f,frame);B(p,"Sensor",0,.80f,.12f,.40f,.02f,.05f,blue);}
    else {B(p,"LiftColumn",.31f,.62f,.25f,.10f,.47f,.12f,metal);B(p,"TransferArm",.05f,.82f,.25f,.60f,.08f,.10f,accent);}
    break;
  }
  Control(p);
  // One simple collision volume gives consistent selection and avoids catching on tiny parts.
  var collider=g.AddComponent<BoxCollider>();collider.center=new Vector3(0,.48f,0);collider.size=new Vector3(.94f,.96f,.94f);
  return g;
 }
 public static void Build(){
  Directory.CreateDirectory("Assets/Machines");AssetDatabase.Refresh();foreach(var f in Directory.GetFiles("Assets/Machines","*.mat"))AssetDatabase.DeleteAsset(f);
  frame=Mat("Frame",new Color(.12f,.19f,.23f));metal=Mat("Steel",new Color(.49f,.58f,.62f));dark=Mat("Dark",new Color(.035f,.05f,.06f));accent=Mat("Safety",new Color(1,.65f,.09f));hot=Mat("Heat",new Color(1,.23f,.025f));blue=Mat("Water",new Color(.08f,.52f,.67f));green=Mat("Plant",new Color(.23f,.55f,.13f));
  var paths=Kinds.Select(k=>"Assets/Machines/Machine"+k+".prefab").ToArray();for(int i=0;i<Kinds.Length;i++){var g=Create(Kinds[i]);PrefabUtility.SaveAsPrefabAsset(g,paths[i]);UnityEngine.Object.DestroyImmediate(g);}
  AssetDatabase.SaveAssets();Directory.CreateDirectory("Build/Windows");
  if(BuildPipeline.BuildAssetBundles("Build/Windows",new[]{new AssetBundleBuild{assetBundleName="automation-machines.unity3d",assetNames=paths}},BuildAssetBundleOptions.ChunkBasedCompression|BuildAssetBundleOptions.StrictMode,BuildTarget.StandaloneWindows64)==null)throw new Exception("Machine bundle build failed");
  var bundle=AssetBundle.LoadFromFile(Path.GetFullPath("Build/Windows/automation-machines.unity3d"));
  foreach(string path in paths){var g=UnityEngine.Object.Instantiate(bundle.LoadAsset<GameObject>(path));if(g.GetComponentsInChildren<Collider>().Length!=1)throw new Exception("Invalid collider: "+path);foreach(var r in g.GetComponentsInChildren<Renderer>()){if(r.sharedMaterial==null||r.sharedMaterial.shader==null)throw new Exception("Missing material: "+path);var b=r.bounds;if(b.min.x<-.5f||b.max.x>.5f||b.min.z<-.5f||b.max.z>.5f||b.min.y<0||b.max.y>1)throw new Exception("Outside block footprint: "+path+" "+r.name);}UnityEngine.Object.DestroyImmediate(g);}
  bundle.Unload(true);File.WriteAllText("Build/Windows/machines-verified.txt","PASS: 11 distinct machine prefabs, materials, single selection/collision volume, one-block bounds, bundle reload.");
 }
 public static void Preview(){
  Build();var scene=new GameObject("MachinePreview");int i=0;foreach(string k in Kinds){var g=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Machines/Machine"+k+".prefab"),scene.transform);g.transform.position=new Vector3((i%4-1.5f)*1.7f,0,(i/4)*2f);i++;}
  var camera=new GameObject("Camera").AddComponent<Camera>();camera.transform.position=new Vector3(4,7,-10);camera.transform.LookAt(new Vector3(0,.35f,2));camera.orthographic=true;camera.orthographicSize=4.1f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.09f,.12f,.16f);
  var light=new GameObject("Light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.6f;light.transform.rotation=Quaternion.Euler(50,-30,0);RenderSettings.ambientLight=new Color(.65f,.65f,.65f);
  var rt=new RenderTexture(1800,1200,24);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var tex=new Texture2D(1800,1200,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1800,1200),0,0);tex.Apply();File.WriteAllBytes("Build/Windows/machines-preview.png",tex.EncodeToPNG());RenderTexture.active=null;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tex);UnityEngine.Object.DestroyImmediate(scene);UnityEngine.Object.DestroyImmediate(camera.gameObject);UnityEngine.Object.DestroyImmediate(light.gameObject);
 }
}
