using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
public static class ConveyorAssetBuild
{
 static Material steel,rubber,yellow,cargo;
 static Material Mat(string name,Color color){var m=new Material(Shader.Find("Standard"));m.color=color;m.SetFloat("_Glossiness",0.25f);string p="Assets/Conveyor/"+name+".mat";AssetDatabase.CreateAsset(m,p);return m;}
 static GameObject Box(Transform parent,string name,Vector3 pos,Vector3 scale,Material mat){var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(parent,false);g.transform.localPosition=pos;g.transform.localScale=scale;g.GetComponent<Renderer>().sharedMaterial=mat;return g;}
 static Vector3 Point(string kind,float t){
  if(kind=="Left")return t<.5f?new Vector3(0,0,t-.5f):new Vector3(-t+.5f,0,0);
  if(kind=="Right")return t<.5f?new Vector3(0,0,t-.5f):new Vector3(t-.5f,0,0);
  return new Vector3(0,kind=="Up"?t:kind=="Down"?-t:0,t-.5f);
 }
 public static void Build(){
  Directory.CreateDirectory("Assets/Conveyor");AssetDatabase.Refresh();
  foreach(string path in Directory.GetFiles("Assets/Conveyor","*.mat"))AssetDatabase.DeleteAsset(path);
  steel=Mat("Frame",new Color(.16f,.23f,.28f));rubber=Mat("Belt",new Color(.055f,.065f,.075f));yellow=Mat("Safety",new Color(1,.68f,.08f));cargo=Mat("Parcel",new Color(.62f,.36f,.13f));
  var paths=new System.Collections.Generic.List<string>();
  foreach(string kind in new[]{"Straight","Left","Right","Up","Down"}){
   var root=new GameObject("Conveyor"+kind);var tr=root.transform;
   for(int i=0;i<10;i++){
    float t=(i+.5f)/10;var a=Point(kind,Math.Max(0,t-.04f));var b=Point(kind,Math.Min(1,t+.04f));var p=Point(kind,t)+Vector3.up*.22f;
    var tread=Box(tr,"Tread"+i,p,new Vector3(.68f,.08f,kind=="Up"||kind=="Down"?.145f:.105f),rubber);tread.transform.localRotation=Quaternion.LookRotation(b-a);
    var side=Vector3.Cross(Vector3.up,(b-a).normalized).normalized;
    foreach(int sign in new[]{-1,1}){var rail=Box(tr,"Rail",p+side*.39f*sign,new Vector3(.085f,.14f,.11f),steel);rail.transform.localRotation=Quaternion.LookRotation(b-a);}
    if(i%3==0){var stripe=Box(tr,"Direction",p+Vector3.up*.049f,new Vector3(.035f,.01f,.08f),yellow);stripe.transform.localRotation=Quaternion.LookRotation(b-a);}
   }
   foreach(float x in new[]{-.3f,.3f})foreach(float z in new[]{-.35f,.35f})Box(tr,"Foot",new Vector3(x,Point(kind,z+.5f).y+.10f,z),new Vector3(.09f,.20f,.13f),steel);
   var rotation=Quaternion.LookRotation(Point(kind,.84f)-Point(kind,.76f));var arrowPos=Point(kind,.8f)+Vector3.up*.29f;
   foreach(int sign in new[]{-1,1}){var head=Box(tr,"ArrowHead",arrowPos+rotation*new Vector3(sign*.05f,0,-.05f),new Vector3(.035f,.015f,.16f),yellow);head.transform.localRotation=rotation*Quaternion.Euler(0,-sign*45,0);}
   var packet=Box(tr,"Cargo",new Vector3(0,.43f,0),new Vector3(.28f,.28f,.28f),cargo);UnityEngine.Object.DestroyImmediate(packet.GetComponent<Collider>());packet.SetActive(false);
   string path="Assets/Conveyor/Conveyor"+kind+".prefab";PrefabUtility.SaveAsPrefabAsset(root,path);paths.Add(path);UnityEngine.Object.DestroyImmediate(root);
  }
  AssetDatabase.SaveAssets();Directory.CreateDirectory("Build/Windows");
  var manifest=BuildPipeline.BuildAssetBundles("Build/Windows",new[]{new AssetBundleBuild{assetBundleName="automation-conveyors.unity3d",assetNames=paths.ToArray()}},BuildAssetBundleOptions.ChunkBasedCompression|BuildAssetBundleOptions.StrictMode,BuildTarget.StandaloneWindows64);
  if(manifest==null)throw new Exception("bundle build failed");
  var bundle=AssetBundle.LoadFromFile(Path.GetFullPath("Build/Windows/automation-conveyors.unity3d"));
  foreach(var path in paths){var p=bundle.LoadAsset<GameObject>(path);if(p==null||p.transform.Find("Cargo")==null||p.GetComponentsInChildren<Collider>().Length==0)throw new Exception("invalid prefab: "+path);}
  bundle.Unload(true);File.WriteAllText("Build/Windows/verified.txt","PASS: five conveyor prefabs, materials, cargo anchors, colliders, bundle reload. Unity "+Application.unityVersion);
 }
 public static void Preview(){
  Build();var scene=new GameObject("PreviewScene");int i=0;
  foreach(string kind in new[]{"Straight","Left","Right","Up","Down"}){var p=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Conveyor/Conveyor"+kind+".prefab");var g=UnityEngine.Object.Instantiate(p,scene.transform);g.transform.position=new Vector3((i-2)*1.6f,kind=="Down"?1:0,0);var packet=g.transform.Find("Cargo");packet.gameObject.SetActive(true);packet.localPosition=Point(kind,.5f)+Vector3.up*(kind=="Up"||kind=="Down"?.55f:.43f);i++;}
  var camera=new GameObject("Camera").AddComponent<Camera>();camera.transform.position=new Vector3(5,6,-9);camera.transform.LookAt(new Vector3(0,.3f,0));camera.orthographic=true;camera.orthographicSize=3;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.15f,.2f);
  var light=new GameObject("Light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.6f;light.transform.rotation=Quaternion.Euler(50,-30,0);RenderSettings.ambientLight=new Color(.6f,.6f,.65f);
  var rt=new RenderTexture(1600,800,24);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var image=new Texture2D(1600,800,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1600,800),0,0);image.Apply();File.WriteAllBytes("Build/Windows/conveyors-preview.png",image.EncodeToPNG());RenderTexture.active=null;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(scene);UnityEngine.Object.DestroyImmediate(camera.gameObject);UnityEngine.Object.DestroyImmediate(light.gameObject);
 }
}
