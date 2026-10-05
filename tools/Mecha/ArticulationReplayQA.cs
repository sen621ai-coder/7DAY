using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using PZAEC.Mecha;
// Replays measured native matrices solely for inspection. Never ships in the mod.
public sealed class ArticulationReplayQA:IModApi
{
 public void InitMod(Mod mod){if(Environment.GetCommandLineArgs().Contains("-mechaMotionQA"))ModEvents.GameStartDone.RegisterHandler(Run);}
 static bool Pause(){return false;}
 static void Run(ref ModEvents.SGameStartDoneData data){if(GamePrefs.GetString(EnumGamePrefs.GameName)!="MechaQA_Isolated")return;int fail=0;
 try{
  var h=new Harmony("mecha.articulation.replay");h.Patch(AccessTools.Method(typeof(Weapons),"Update"),prefix:new HarmonyMethod(typeof(ArticulationReplayQA),nameof(Pause)));
  var world=GameManager.Instance.World;AccessTools.Field(typeof(Weapons),"currentWorld").SetValue(null,world);
  var v=EntityFactory.CreateEntity(EntityClass.FromString(Rules.CompleteVehicle),new Vector3(0,300,0)+Origin.position) as EntityVehicle;world.SpawnEntityInWorld(v);v.vehicle.SetItemValue(ItemClass.GetItem(Rules.CompleteItem,false));
  for(var t=v.vehicleRB.transform;t!=null;t=t.parent)t.gameObject.SetActive(true);v.vehicleRB.isKinematic=true;
  var rig=Model.GetRig(v);var skins=rig.Mount.GetComponentsInChildren<SkinnedMeshRenderer>();var bones=skins[0].bones;
  var camera=new GameObject("articulation inspection").AddComponent<Camera>();var rt=new RenderTexture(1100,900,24);camera.targetTexture=rt;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.09f,.12f,.16f);camera.fieldOfView=36;
  var lamp=new GameObject("inspection light").AddComponent<Light>();lamp.type=LightType.Directional;lamp.intensity=1.1f;lamp.transform.rotation=Quaternion.Euler(35,-30,0);RenderSettings.ambientLight=new Color(.6f,.6f,.6f);
  var folder=Path.Combine(GameIO.GetSaveGameDir(),"articulation-review");Directory.CreateDirectory(folder);var lines=new List<string>();
  var poses=JArray.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("MECHA_REVIEW_POSES")));
  foreach(JObject pose in poses){rig.ResetPose();var matrices=(JArray)pose["bones"];
   // Create the native cockpit helpers too. The matrix replay then restores the
   // exact measured chest transforms; an intentional open cabin is not a hole defect.
   int door=Array.IndexOf(bones,rig.ChestDoor);var da=matrices[door].ToObject<float[]>();var doorRotation=Quaternion.LookRotation(new Vector3(da[2],da[6],da[10]),new Vector3(da[1],da[5],da[9]));
   int torso=Array.IndexOf(bones,rig.Torso);var ta=matrices[torso].ToObject<float[]>();var torsoRotation=Quaternion.LookRotation(new Vector3(ta[2],ta[6],ta[10]),new Vector3(ta[1],ta[5],ta[9]));
   float open=Mathf.Clamp01(Quaternion.Angle(Quaternion.Inverse(torsoRotation)*doorRotation,rig.RestRot[rig.ChestDoor])/80f);RobotPresentation.Update(v,rig,0,open>.001f?.25f+.75f*open:0);

   for(int b=0;b<bones.Length;b++){var a=matrices[b].ToObject<float[]>();var p=new Vector3(a[3],a[7],a[11]);var up=new Vector3(a[1],a[5],a[9]);var forward=new Vector3(a[2],a[6],a[10]);bones[b].SetPositionAndRotation(rig.Mount.TransformPoint(p),rig.Mount.rotation*Quaternion.LookRotation(forward,up));}
   var objects=new List<GameObject>();var materials=new List<Material>();float error=0;
   foreach(var skin in skins){var mesh=new Mesh();skin.BakeMesh(mesh);var points=mesh.vertices;var rest=skin.sharedMesh.vertices;var weights=skin.sharedMesh.boneWeights;var ix=mesh.triangles;
    for(int j=0;j<points.Length;j+=101){int b=weights[j].boneIndex0;var a=matrices[b].ToObject<float[]>();var local=skin.sharedMesh.bindposes[b].MultiplyPoint3x4(rest[j]);var expected=new Vector3(a[0]*local.x+a[1]*local.y+a[2]*local.z+a[3],a[4]*local.x+a[5]*local.y+a[6]*local.z+a[7],a[8]*local.x+a[9]*local.y+a[10]*local.z+a[11]);error=Mathf.Max(error,Vector3.Distance(rig.Mount.InverseTransformPoint(skin.transform.TransformPoint(points[j])),expected));}
    var go=new GameObject("solid audited skin");go.transform.SetPositionAndRotation(skin.transform.position,skin.transform.rotation);go.transform.localScale=skin.transform.lossyScale;go.AddComponent<MeshFilter>().sharedMesh=mesh;
    var groups=new Dictionary<int,List<int>>();for(int j=0;j<ix.Length;j+=3){int b=weights[ix[j]].boneIndex0;if(!groups.ContainsKey(b))groups[b]=new List<int>();groups[b].AddRange(new[]{ix[j],ix[j+1],ix[j+2]});}
    mesh.subMeshCount=groups.Count;var mats=new List<Material>();int sub=0;foreach(var group in groups){mesh.SetTriangles(group.Value,sub++);var mat=new Material(Shader.Find("Standard"));mat.color=Color.HSVToRGB((group.Key*.618034f)%1,.35f,.75f);mat.SetFloat("_Glossiness",.15f);materials.Add(mat);mats.Add(mat);}go.AddComponent<MeshRenderer>().sharedMaterials=mats.ToArray();objects.Add(go);skin.enabled=false;
   }
   string name=string.Join("-",pose["auditPair"].ToObject<string[]>());lines.Add((error<.001f?"PASS ":"FAIL ")+name+" replay maximum sampled vertex error="+error+" action="+pose["action"]+" frame="+pose["frame"]);if(error>=.001f)fail++;
   var focusArray=pose["focus"].ToObject<float[]>();var close=rig.Mount.TransformPoint(new Vector3(focusArray[0],focusArray[1],focusArray[2]));
   int view=0;foreach(var offset in new[]{new Vector3(0,.3f,6),new Vector3(6,.3f,0),new Vector3(0,.3f,-6),new Vector3(1.8f,.2f,2.2f),new Vector3(-1.8f,.2f,2.2f)}){var focus=view<3?rig.Mount.position+Vector3.up*1.5f:close;camera.transform.position=focus+offset;camera.transform.LookAt(focus);camera.Render();var old=RenderTexture.active;RenderTexture.active=rt;var png=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);png.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);png.Apply();File.WriteAllBytes(Path.Combine(folder,name+"-"+view+++".png"),png.EncodeToPNG());UnityEngine.Object.DestroyImmediate(png);RenderTexture.active=old;}
   foreach(var skin in skins)skin.enabled=true;foreach(var go in objects){UnityEngine.Object.DestroyImmediate(go.GetComponent<MeshFilter>().sharedMesh);UnityEngine.Object.DestroyImmediate(go);}foreach(var mat in materials)UnityEngine.Object.DestroyImmediate(mat);
  }
  File.WriteAllLines(Path.Combine(folder,"replay-report.txt"),lines);Log.Out("[ArticulationReplay] "+folder+" poses="+poses.Count);
 }catch(Exception e){fail++;Log.Error("[ArticulationReplay] "+e);}Log.Out("[MechaMotionQA] COMPLETE failures="+fail);
 }
}
