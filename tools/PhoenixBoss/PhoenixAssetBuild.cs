using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
public static class PhoenixAssetBuild {
 public static void Build(){
  const string path="Assets/Phoenix/fly.fbx";
  AssetDatabase.Refresh();var importer=(ModelImporter)AssetImporter.GetAtPath(path);
  importer.animationType=ModelImporterAnimationType.Legacy;importer.importAnimation=true;importer.isReadable=true;importer.SaveAndReimport();
  var clips=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).ToArray();
  if(clips.Length==0)throw new Exception("FBX has no animation");
  var root=new GameObject("PhoenixBoss");var alignment=new GameObject("Alignment");alignment.transform.SetParent(root.transform,false);alignment.transform.localRotation=Quaternion.Euler(0,90,0);var model=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path),alignment.transform);
  var renderers=model.GetComponentsInChildren<SkinnedMeshRenderer>();if(renderers.Length==0)throw new Exception("No skin");
  foreach(var r in renderers){r.updateWhenOffscreen=true;r.sharedMaterials=r.sharedMaterials.Select(old=>{
   string part=old.name.Contains("01b")?"01b":"01a";string stem="Assets/Phoenix/Tex_Ride_FengHuang_"+part;
   var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(stem+"_D_A.tga.png");var emission=AssetDatabase.LoadAssetAtPath<Texture2D>(stem+"_E.tga.png");if(texture==null||emission==null)throw new Exception("Missing phoenix texture "+part);
   string dest="Assets/Phoenix/"+part+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(dest);if(mat==null){mat=new Material(Shader.Find("YF/PhoenixFeathers"));AssetDatabase.CreateAsset(mat,dest);}mat.shader=Shader.Find("YF/PhoenixFeathers");mat.mainTexture=texture;mat.SetFloat("_Glossiness",.12f);mat.SetFloat("_Mode",1);mat.SetInt("_SrcBlend",1);mat.SetInt("_DstBlend",0);mat.SetInt("_ZWrite",1);mat.EnableKeyword("_ALPHATEST_ON");mat.SetFloat("_Cutoff",.3f);mat.renderQueue=2450;mat.EnableKeyword("_EMISSION");mat.SetTexture("_EmissionMap",emission);mat.SetColor("_EmissionColor",Color.white*1.8f);return mat;
  }).ToArray();}
  var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
  // Runtime carrier uses SizeScale 2; normalize this model to a 2.5m wingspan.
  float scale=2.5f/bounds.size.x;alignment.transform.localScale*=scale;
  var pelvis=model.GetComponentsInChildren<Transform>().Single(t=>t.name=="B_Pelvis");alignment.transform.position-=pelvis.position;
  var head=model.GetComponentsInChildren<Transform>().Single(t=>t.name=="b_Head");var forward=head.position-pelvis.position;forward.y=0;if(Vector3.Dot(forward.normalized,Vector3.forward)<.99f)throw new Exception("Phoenix facing does not match native forward");
  foreach(var anim in model.GetComponentsInChildren<Animation>())UnityEngine.Object.DestroyImmediate(anim);
  var animation=model.AddComponent<Animation>();animation.AddClip(clips[0],"Fly");animation.clip=clips[0];animation.wrapMode=WrapMode.Loop;animation.playAutomatically=true;animation.cullingType=AnimationCullingType.AlwaysAnimate;
  PrefabUtility.SaveAsPrefabAsset(root,"Assets/Phoenix/PhoenixBoss.prefab");
  var before=renderers[0].bones.Select(b=>b.localRotation).ToArray();clips[0].SampleAnimation(model,0);clips[0].SampleAnimation(model,clips[0].length*.37f);
  if(!renderers[0].bones.Where((b,i)=>Quaternion.Angle(before[i],b.localRotation)>.1f).Any())throw new Exception("Animation does not move bones");
  UnityEngine.Object.DestroyImmediate(root);
  var fire=GameObject.CreatePrimitive(PrimitiveType.Sphere);fire.name="PhoenixFireball";fire.transform.localScale=Vector3.one*.45f;UnityEngine.Object.DestroyImmediate(fire.GetComponent<Collider>());
  string firePath="Assets/Phoenix/fireCore.mat";var fireMat=AssetDatabase.LoadAssetAtPath<Material>(firePath);if(fireMat==null){fireMat=new Material(Shader.Find("Unlit/Color"));AssetDatabase.CreateAsset(fireMat,firePath);}fireMat.color=new Color(1,.25f,.015f);fire.GetComponent<Renderer>().sharedMaterial=fireMat;
  var flame=new GameObject("FlameTrail");flame.transform.SetParent(fire.transform,false);var ps=flame.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
  var main=ps.main;main.loop=true;main.duration=1;main.startLifetime=.4f;main.startSpeed=.4f;main.startSize=.8f;main.maxParticles=100;main.simulationSpace=ParticleSystemSimulationSpace.World;main.scalingMode=ParticleSystemScalingMode.Shape;main.startColor=new Color(1,.4f,.03f,1);
  var emission=ps.emission;emission.rateOverTime=90;var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Sphere;shape.radius=.2f;
  var color=ps.colorOverLifetime;color.enabled=true;var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(new Color(1,.85f,.2f),0),new GradientColorKey(new Color(1,.05f,0),1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(0,1)});color.color=gradient;
  var texture=new Texture2D(32,32,TextureFormat.RGBA32,false);for(int y=0;y<32;y++)for(int x=0;x<32;x++){float alpha=Mathf.Clamp01(1-Vector2.Distance(new Vector2(x,y),new Vector2(15.5f,15.5f))/15.5f);texture.SetPixel(x,y,new Color(1,1,1,alpha*alpha));}texture.Apply();File.WriteAllBytes("Assets/Phoenix/fire.png",texture.EncodeToPNG());AssetDatabase.ImportAsset("Assets/Phoenix/fire.png");
  var trailMat=AssetDatabase.LoadAssetAtPath<Material>("Assets/Phoenix/fire.mat");if(trailMat==null){trailMat=new Material(Shader.Find("Particles/Standard Unlit"));AssetDatabase.CreateAsset(trailMat,"Assets/Phoenix/fire.mat");}trailMat.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Phoenix/fire.png");trailMat.SetFloat("_Mode",2);trailMat.SetInt("_SrcBlend",5);trailMat.SetInt("_DstBlend",1);trailMat.SetInt("_ZWrite",0);trailMat.EnableKeyword("_ALPHABLEND_ON");trailMat.renderQueue=3000;ps.GetComponent<ParticleSystemRenderer>().sharedMaterial=trailMat;
  PrefabUtility.SaveAsPrefabAsset(fire,"Assets/Phoenix/PhoenixFireball.prefab");UnityEngine.Object.DestroyImmediate(fire);AssetDatabase.SaveAssets();Directory.CreateDirectory("Build");
  var result=BuildPipeline.BuildAssetBundles("Build",new[]{new AssetBundleBuild{assetBundleName="phoenix.unity3d",assetNames=new[]{"Assets/Phoenix/PhoenixBoss.prefab"}},new AssetBundleBuild{assetBundleName="phoenix-fire.unity3d",assetNames=new[]{"Assets/Phoenix/PhoenixFireball.prefab"}}},BuildAssetBundleOptions.ChunkBasedCompression|BuildAssetBundleOptions.StrictMode|BuildAssetBundleOptions.ForceRebuildAssetBundle,BuildTarget.StandaloneWindows64);if(result==null)throw new Exception("Bundle failed");
  foreach(string filename in new[]{"phoenix.unity3d","phoenix-fire.unity3d"}){var loaded=AssetBundle.LoadFromFile(Path.GetFullPath("Build/"+filename));if(loaded==null)throw new Exception("Bundle reload failed: "+filename);if(loaded.LoadAllAssets<GameObject>().Length!=1)throw new Exception("Prefab missing in "+filename);loaded.Unload(true);}
  File.WriteAllText("Build/verified.txt","PASS textured skinned phoenix; animated bones; clip="+clips[0].name+" seconds="+clips[0].length+" originalBounds="+bounds.size);
 }
 public static void Preview(){
  var model=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Phoenix/PhoenixBoss.prefab"));
  File.WriteAllLines("Build/bones.txt",model.GetComponentsInChildren<Transform>().Select(t=>t.name+" "+t.position));
  var anim=model.GetComponentInChildren<Animation>();anim.clip.SampleAnimation(anim.gameObject,.8f);
  RenderSettings.ambientLight=new Color(.55f,.55f,.6f);var light=new GameObject("Key").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.5f;light.transform.rotation=Quaternion.Euler(35,-35,0);
  var camera=new GameObject("Camera").AddComponent<Camera>();camera.backgroundColor=new Color(.035f,.045f,.065f);camera.clearFlags=CameraClearFlags.SolidColor;var renderers=model.GetComponentsInChildren<Renderer>();var frame=renderers[0].bounds;foreach(var r in renderers)frame.Encapsulate(r.bounds);camera.transform.position=frame.center+new Vector3(3,2.5f,4);camera.transform.LookAt(frame.center);camera.orthographic=true;camera.orthographicSize=frame.extents.magnitude*1.05f;
  var target=new RenderTexture(1200,900,24);camera.targetTexture=target;camera.Render();RenderTexture.active=target;var image=new Texture2D(1200,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1200,900),0,0);image.Apply();File.WriteAllBytes("Build/phoenix-preview.png",image.EncodeToPNG());
 }
}
