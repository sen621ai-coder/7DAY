using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// Layout preview only. Machines/belts are the shipped models; utility props are diagrams.
public static class FactoryScenePreview
{
 static Material steel,dark,blue,yellow,ground,wire;
 static Transform root;
 static Material Mat(string name,Color color){string path="Assets/FactoryScene/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(m==null){m=new Material(Shader.Find("Standard"));AssetDatabase.CreateAsset(m,path);}m.color=color;m.SetFloat("_Glossiness",.2f);return m;}
 static GameObject Box(string name,Vector3 p,Vector3 size,Material m){var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(root,false);g.transform.localPosition=p;g.transform.localScale=size;g.GetComponent<Renderer>().sharedMaterial=m;return g;}
 static GameObject Model(string path,int x,int z,float yaw=0){var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(prefab==null)throw new Exception("Missing "+path);var g=(GameObject)PrefabUtility.InstantiatePrefab(prefab);g.transform.SetParent(root,false);g.transform.localPosition=new Vector3(x,0,z);g.transform.localRotation=Quaternion.Euler(0,yaw,0);return g;}
 static void Crate(string name,int x,int z,Material stripe){Box(name,new Vector3(x,.43f,z),new Vector3(.86f,.86f,.86f),steel);foreach(float offset in new[]{-.29f,.29f})Box("Crate brace",new Vector3(x+offset,.43f,z-.44f),new Vector3(.05f,.86f,.035f),dark);Box("Crate label",new Vector3(x,.52f,z-.465f),new Vector3(.36f,.22f,.015f),stripe);}
 static void Belt(string kind,int x,int z,float yaw,bool cargo){var g=Model("Assets/Conveyor/Conveyor"+kind+".prefab",x,z,yaw);g.transform.Find("Cargo").gameObject.SetActive(cargo);}
 static void Cable(Vector3 a,Vector3 b){var g=new GameObject("Power cable (diagram)");g.transform.SetParent(root,false);var line=g.AddComponent<LineRenderer>();line.sharedMaterial=wire;line.startWidth=.025f;line.endWidth=.025f;line.positionCount=3;line.SetPositions(new[]{a,(a+b)/2+Vector3.down*.15f,b});}
 static void Port(int x,int z){Box("Power connector (diagram)",new Vector3(x,.20f,z),new Vector3(.28f,.4f,.28f),blue);Box("Power terminal",new Vector3(x,.43f,z),new Vector3(.12f,.08f,.12f),yellow);Cable(new Vector3(11,.9f,11),new Vector3(x,.47f,z));}
 static void Label(string text,Vector3 pos,Camera camera){var g=new GameObject("Stage "+text);g.transform.SetParent(root,false);g.transform.position=pos;g.transform.rotation=camera.transform.rotation;var tm=g.AddComponent<TextMesh>();tm.text=text;tm.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");tm.fontSize=64;tm.characterSize=.065f;tm.anchor=TextAnchor.MiddleCenter;tm.color=Color.white;g.GetComponent<MeshRenderer>().sharedMaterial=tm.font.material;}
 public static void Build(){
  EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);Directory.CreateDirectory("Assets/FactoryScene");AssetDatabase.Refresh();root=new GameObject("IronFactoryLayout").transform;
  steel=Mat("Steel",new Color(.38f,.47f,.54f));dark=Mat("Frame",new Color(.10f,.15f,.19f));blue=Mat("Input",new Color(.08f,.64f,.77f));yellow=Mat("Output",new Color(1,.63f,.08f));ground=Mat("Floor",new Color(.20f,.25f,.29f));wire=Mat("Wire",new Color(.96f,.6f,.05f));
  Box("Factory foundation",new Vector3(7.5f,-.18f,7.5f),new Vector3(14,.35f,14),ground);
  for(int i=1;i<=14;i++){Box("Floor joint",new Vector3(i,-.001f,7.5f),new Vector3(.016f,.006f,14),dark);Box("Floor joint",new Vector3(7.5f,-.001f,i),new Vector3(14,.006f,.016f),dark);}
  Crate("01 Iron input",3,3,blue);Model("Assets/Machines/MachineSmelter.prefab",3,4);Crate("03 Ingot output",3,5,yellow);
  Belt("Straight",3,6,0,true);Belt("Right",3,7,0,false);for(int x=4;x<=7;x++)Belt("Straight",x,7,90,x%2==0);Belt("Left",8,7,90,false);Belt("Straight",8,8,0,true);
  Crate("05 Iron and clay input",8,9,blue);Model("Assets/Machines/MachineForge.prefab",9,9);Crate("07 Finished iron",10,9,yellow);
  Box("Generator (diagram)",new Vector3(11,.48f,11),new Vector3(1.4f,.82f,.9f),dark);Box("Generator tank",new Vector3(11,.95f,11),new Vector3(1.1f,.18f,.75f),yellow);for(int i=0;i<7;i++)Box("Generator grille",new Vector3(10.48f+i*.17f,.5f,10.54f),new Vector3(.065f,.55f,.03f),steel);
  Port(2,4);Port(6,8);Port(9,10);
  Box("Workbench top (diagram)",new Vector3(11,.83f,3),new Vector3(1.8f,.16f,.8f),steel);foreach(float x in new[]{10.3f,11.7f})foreach(float z in new[]{2.7f,3.3f})Box("Workbench leg",new Vector3(x,.4f,z),new Vector3(.09f,.8f,.09f),dark);Box("Tool cabinet",new Vector3(11,.45f,3.1f),new Vector3(.75f,.55f,.45f),blue);
  var cam=new GameObject("OverviewCamera").AddComponent<Camera>();cam.transform.position=new Vector3(17,17,-10);cam.transform.LookAt(new Vector3(7,.2f,7));cam.orthographic=true;cam.orthographicSize=9.0f;cam.backgroundColor=new Color(.055f,.075f,.10f);cam.clearFlags=CameraClearFlags.SolidColor;cam.nearClipPlane=.1f;cam.farClipPlane=100;
  Label("01",new Vector3(3,1.35f,3),cam);Label("02",new Vector3(3,1.7f,4),cam);Label("03",new Vector3(3,1.35f,5),cam);Label("04",new Vector3(5.5f,.85f,7),cam);Label("05",new Vector3(8,1.35f,9),cam);Label("06",new Vector3(9,1.7f,9),cam);Label("07",new Vector3(10,1.35f,9),cam);Label("POWER",new Vector3(11,1.8f,11.8f),cam);Label("WORKBENCH",new Vector3(11,1.5f,3),cam);
  Label("IRON PROCESSING LINE",cam.ViewportToWorldPoint(new Vector3(.5f,.94f,20)),cam);Label("UNITY SCENE PREVIEW",cam.ViewportToWorldPoint(new Vector3(.5f,.89f,20)),cam);
  var light=new GameObject("Sun").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.6f;light.shadows=LightShadows.Soft;light.transform.rotation=Quaternion.Euler(55,-35,0);RenderSettings.ambientLight=new Color(.6f,.65f,.7f);QualitySettings.shadowDistance=80;QualitySettings.shadows=ShadowQuality.All;
  AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(),"Assets/FactoryScene/CompleteIronFactory.unity");Directory.CreateDirectory("Build/Windows");
  var rt=new RenderTexture(2400,1700,24);rt.antiAliasing=4;cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;var tex=new Texture2D(2400,1700,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,2400,1700),0,0);tex.Apply();File.WriteAllBytes("Build/Windows/full-factory-preview.png",tex.EncodeToPNG());RenderTexture.active=null;cam.targetTexture=null;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tex);
  File.WriteAllText("Build/Windows/full-factory-preview.txt","Unity layout preview: actual Mod smelter/forge/conveyor prefabs; crates, generator, workbench and wiring are schematic props. Scene matches FactoryPhotoQA's 8-belt route in a single 16x16 chunk. Not an in-game screenshot or proof of a running factory.");
 }
}
