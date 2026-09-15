using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using SakuraPreview;
public static class SakuraPoseCheck {
 public static void Run(){
  var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/SakuraPreview.prefab");
  var root=UnityEngine.Object.Instantiate(prefab);
  foreach(var a in root.GetComponentsInChildren<Animator>())a.enabled=false;
  File.WriteAllText("Build/pose-bones.txt",string.Join("\n",System.Array.ConvertAll(root.GetComponentsInChildren<Transform>(),t=>t.name)));
  foreach(var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>())renderer.updateWhenOffscreen=true;
  var report=new System.Text.StringBuilder();
  foreach(var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>()){
    report.AppendLine(skin.name+" submeshes="+skin.sharedMesh.subMeshCount+" uv="+skin.sharedMesh.uv.Length);
    foreach(var mat in skin.sharedMaterials)report.AppendLine(mat.name+" texture="+(mat.mainTexture==null?"NONE":mat.mainTexture.name)+" color="+mat.color);
  }
  File.WriteAllText("Build/material-report.txt",report.ToString());
  var pose=new SakuraPose(root.transform);
  root.transform.rotation=Quaternion.Euler(0,90,0);
  pose.Apply(0,0,0);
  if(Quaternion.Angle(root.transform.rotation,Quaternion.Euler(0,90,0))>.01f)throw new Exception("Pose overwrote entity facing");
  root.transform.rotation=Quaternion.identity;
  var cam=new GameObject("PreviewCamera").AddComponent<Camera>();
  cam.transform.position=new Vector3(2,1.8f,5);
  cam.transform.LookAt(new Vector3(0,.9f,0));
  cam.orthographic=true;cam.orthographicSize=1.05f;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.12f,.14f,.17f);
  RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.3f,.3f,.3f);
  var light=new GameObject("Key").AddComponent<Light>();light.type=LightType.Directional;light.intensity=.9f;light.transform.rotation=Quaternion.Euler(35,-35,0);
  light.transform.position=new Vector3(2,4,5);light.transform.LookAt(new Vector3(0,1,0));
  var rt=new RenderTexture(680,900,24);cam.targetTexture=rt;
  Directory.CreateDirectory("Build/Poses");
  for(int i=0;i<3;i++){
   pose.Apply(i==2?1:0,i==2?1.57f:0,i==1?Time.realtimeSinceStartup+10:0);
      var bakedObjects=new System.Collections.Generic.List<GameObject>();
   foreach(var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>()){
    var mesh=new Mesh();skin.BakeMesh(mesh);
    var go=new GameObject("Baked");go.transform.SetPositionAndRotation(skin.transform.position,skin.transform.rotation);go.transform.localScale=Vector3.one;
    go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;
    skin.enabled=false;bakedObjects.Add(go);
   }
   cam.Render();RenderTexture.active=rt;
   foreach(var go in bakedObjects){UnityEngine.Object.DestroyImmediate(go.GetComponent<MeshFilter>().sharedMesh);UnityEngine.Object.DestroyImmediate(go);}
   foreach(var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>())skin.enabled=true;
   var tex=new Texture2D(680,900,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,680,900),0,0);tex.Apply();
   File.WriteAllBytes("Build/Poses/"+new[]{"idle","wave","walk"}[i]+".png",tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);
  }
  RenderTexture.active=null;cam.targetTexture=null;UnityEngine.Object.DestroyImmediate(rt);
  UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(cam.gameObject);UnityEngine.Object.DestroyImmediate(light.gameObject);
  File.WriteAllText("Build/Poses/verified.txt","PASS: shared runtime pose code instantiated and rendered in Unity.");
 }
}








