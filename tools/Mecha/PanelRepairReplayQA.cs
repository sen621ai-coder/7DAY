using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using PZAEC.Mecha;
// Replays measured native matrices solely for inspection. Never ships in the mod.
public sealed class PanelRepairReplayQA:IModApi
{
 public void InitMod(Mod mod){if(Environment.GetCommandLineArgs().Contains("-mechaMotionQA"))ModEvents.GameStartDone.RegisterHandler(Run);}
 static bool Pause(){return false;}
 static void Run(ref ModEvents.SGameStartDoneData data){if(GamePrefs.GetString(EnumGamePrefs.GameName)!="MechaQA_Isolated")return;int fail=0;
 try{
  var h=new Harmony("mecha.articulation.replay");h.Patch(AccessTools.Method(typeof(Weapons),"Update"),prefix:new HarmonyMethod(typeof(PanelRepairReplayQA),nameof(Pause)));
  var world=GameManager.Instance.World;AccessTools.Field(typeof(Weapons),"currentWorld").SetValue(null,world);
  var v=EntityFactory.CreateEntity(EntityClass.FromString(Rules.CompleteVehicle),new Vector3(0,300,0)+Origin.position) as EntityVehicle;world.SpawnEntityInWorld(v);v.vehicle.SetItemValue(ItemClass.GetItem(Rules.CompleteItem,false));
  for(var t=v.vehicleRB.transform;t!=null;t=t.parent)t.gameObject.SetActive(true);v.vehicleRB.isKinematic=true;
  var rig=Model.GetRig(v);var skins=rig.Mount.GetComponentsInChildren<SkinnedMeshRenderer>();var bones=skins[0].bones;
  var originalUV=new Dictionary<string,Vector2[]>();
  var asset=JObject.Parse(File.ReadAllText(Path.Combine(Model.Path,"samurai_style_gundam_mecha_rig.json")));
  using(var reader=new BinaryReader(File.OpenRead(Path.Combine(Model.Path,"samurai_style_gundam_mecha_rig.bin"))))foreach(JObject part in asset["parts"]){
   if((string)part["role"]!="SwordBlade")continue;reader.BaseStream.Position=(long)part["offset"];var uv=new Vector2[(int)part["vertices"]];
   for(int i=0;i<uv.Length;i++){reader.BaseStream.Position+=24;uv[i]=new Vector2(reader.ReadSingle(),reader.ReadSingle());reader.BaseStream.Position+=16;}
   originalUV[(string)part["nodeName"]+"_"+(string)part["joint"]]=uv;
  }
  var bodyMaterial=skins.First(s=>s.name.StartsWith("Complete_Panel_Arm")).sharedMaterial;
  var camera=new GameObject("articulation inspection").AddComponent<Camera>();var rt=new RenderTexture(1100,900,24);camera.targetTexture=rt;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.09f,.12f,.16f);camera.fieldOfView=36;camera.cullingMask=1<<30;
  var lamp=new GameObject("inspection light").AddComponent<Light>();lamp.type=LightType.Directional;lamp.intensity=1.1f;lamp.transform.rotation=Quaternion.Euler(35,-30,0);RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.6f,.6f,.6f);var ambient=new UnityEngine.Rendering.SphericalHarmonicsL2();ambient.AddAmbientLight(new Color(.6f,.6f,.6f));RenderSettings.ambientProbe=ambient;lamp.cullingMask=1<<30;
  var folder=Path.Combine(GameIO.GetSaveGameDir(),"articulation-review");Directory.CreateDirectory(folder);var lines=new List<string>();
  var interiors=skins.Where(s=>s.name.StartsWith("Complete_Liner_")).ToArray();
  if(interiors.Length>0){bool finish=interiors.Length>=5&&interiors.All(s=>!s.sharedMaterial.IsKeywordEnabled("_METALLICGLOSSMAP")&&Mathf.Abs(s.sharedMaterial.GetFloat("_Glossiness")-.15f)<.001f);lines.Add((finish?"PASS ":"FAIL ")+"graphite interiors use effective low-reflection material controls");if(!finish)fail++;}
  var poses=JArray.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("MECHA_REVIEW_POSES")));
  foreach(JObject pose in poses){rig.ResetPose();var matrices=(JArray)pose["bones"];
   // Create the native cockpit helpers too. The matrix replay then restores the
   // exact measured chest transforms; an intentional open cabin is not a hole defect.
   int door=Array.IndexOf(bones,rig.ChestDoor);var da=matrices[door].ToObject<float[]>();var doorRotation=Quaternion.LookRotation(new Vector3(da[2],da[6],da[10]),new Vector3(da[1],da[5],da[9]));
   int torso=Array.IndexOf(bones,rig.Torso);var ta=matrices[torso].ToObject<float[]>();var torsoRotation=Quaternion.LookRotation(new Vector3(ta[2],ta[6],ta[10]),new Vector3(ta[1],ta[5],ta[9]));
   float open=Mathf.Clamp01(Quaternion.Angle(Quaternion.Inverse(torsoRotation)*doorRotation,rig.RestRot[rig.ChestDoor])/80f);RobotPresentation.Update(v,rig,0,pose["hatch"]!=null?(float)pose["hatch"]:(open>.001f?.25f+.75f*open:0));

   foreach(var helper in rig.Mount.GetComponentsInChildren<MeshRenderer>(true))helper.gameObject.layer=30;
   for(int b=0;b<bones.Length;b++){var a=matrices[b].ToObject<float[]>();var p=new Vector3(a[3],a[7],a[11]);var up=new Vector3(a[1],a[5],a[9]);var forward=new Vector3(a[2],a[6],a[10]);bones[b].SetPositionAndRotation(rig.Mount.TransformPoint(p),rig.Mount.rotation*Quaternion.LookRotation(forward,up));}
   // Re-evaluate production hatch kinematics after restoring body transforms.
   RobotPresentation.Update(v,rig,0,pose["hatch"]!=null?(float)pose["hatch"]:(open>.001f?.25f+.75f*open:0));
   foreach(var chest in new[]{rig.ChestL,rig.ChestR,rig.ChestDoor}){int k=Array.IndexOf(bones,chest);var matrix=rig.Mount.worldToLocalMatrix*chest.localToWorldMatrix;var values=new JArray();for(int row=0;row<3;row++)for(int col=0;col<4;col++)values.Add(matrix[row,col]);matrices[k]=values;}
   var objects=new List<GameObject>();var materials=new List<Material>();float error=0;
   foreach(var skin in skins){
    if((Environment.GetEnvironmentVariable("MECHA_REVIEW_HIDE_INTERIORS")=="1"||(bool?)pose["originalMaterial"]==true)&&(skin.name.StartsWith("Complete_Liner_")||skin.name.StartsWith("Complete_InnerShell_"))){skin.enabled=false;continue;}
    var mesh=new Mesh();skin.BakeMesh(mesh);var points=mesh.vertices;var rest=skin.sharedMesh.vertices;var weights=skin.sharedMesh.boneWeights;var ix=mesh.triangles;
    for(int j=0;j<points.Length;j+=101){var expected=Vector3.zero;for(int w=0;w<2;w++){int b=w==0?weights[j].boneIndex0:weights[j].boneIndex1;float weight=w==0?weights[j].weight0:weights[j].weight1;var a=matrices[b].ToObject<float[]>();var local=skin.sharedMesh.bindposes[b].MultiplyPoint3x4(rest[j]);expected+=weight*new Vector3(a[0]*local.x+a[1]*local.y+a[2]*local.z+a[3],a[4]*local.x+a[5]*local.y+a[6]*local.z+a[7],a[8]*local.x+a[9]*local.y+a[10]*local.z+a[11]);}error=Mathf.Max(error,Vector3.Distance(rig.Mount.InverseTransformPoint(skin.transform.TransformPoint(points[j])),expected));}
    Vector2[] sourceUV;bool useOriginal=(bool?)pose["originalMaterial"]==true&&originalUV.TryGetValue(skin.name,out sourceUV);
    if(useOriginal)mesh.uv=originalUV[skin.name];
    var go=new GameObject("solid audited skin");go.layer=30;go.transform.SetPositionAndRotation(skin.transform.position,skin.transform.rotation);go.transform.localScale=skin.transform.lossyScale;go.AddComponent<MeshFilter>().sharedMesh=mesh;
    var groups=new Dictionary<int,List<int>>();for(int j=0;j<ix.Length;j+=3){int b=weights[ix[j]].boneIndex0;if(!groups.ContainsKey(b))groups[b]=new List<int>();groups[b].AddRange(new[]{ix[j],ix[j+1],ix[j+2]});}
    mesh.subMeshCount=groups.Count;var mats=new List<Material>();int sub=0;foreach(var group in groups){mesh.SetTriangles(group.Value,sub++);var mat=Environment.GetEnvironmentVariable("MECHA_REVIEW_TEXTURED")=="1"?new Material(useOriginal?bodyMaterial:skin.sharedMaterial):new Material(Shader.Find("Standard"));if(Environment.GetEnvironmentVariable("MECHA_REVIEW_TEXTURED")!="1"){mat.color=Color.HSVToRGB((group.Key*.618034f)%1,.35f,.75f);mat.SetFloat("_Glossiness",.15f);}materials.Add(mat);mats.Add(mat);}go.AddComponent<MeshRenderer>().sharedMaterials=mats.ToArray();objects.Add(go);skin.enabled=false;
   }
   string name=string.Join("-",pose["auditPair"].ToObject<string[]>());lines.Add((error<.001f?"PASS ":"FAIL ")+name+" replay maximum sampled vertex error="+error+" action="+pose["action"]+" frame="+pose["frame"]);if(error>=.001f)fail++;
   var focusArray=pose["focus"].ToObject<float[]>();var close=rig.Mount.TransformPoint(new Vector3(focusArray[0],focusArray[1],focusArray[2]));
   var offsets=pose["cameraOffsets"]==null?new[]{new Vector3(0,.3f,6),new Vector3(6,.3f,0),new Vector3(0,.3f,-6),new Vector3(1.8f,.2f,2.2f),new Vector3(-1.8f,.2f,2.2f)}:pose["cameraOffsets"].Select(a=>new Vector3((float)a[0],(float)a[1],(float)a[2])).ToArray();
   int view=0;foreach(var offset in offsets){var focus=pose["cameraOffsets"]==null&&view<3?rig.Mount.position+Vector3.up*1.5f:close;camera.transform.position=focus+offset;camera.transform.LookAt(focus);camera.Render();var old=RenderTexture.active;RenderTexture.active=rt;var png=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);png.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);png.Apply();File.WriteAllBytes(Path.Combine(folder,name+"-"+view+++".png"),png.EncodeToPNG());UnityEngine.Object.DestroyImmediate(png);RenderTexture.active=old;}
   foreach(var skin in skins)skin.enabled=true;foreach(var go in objects){UnityEngine.Object.DestroyImmediate(go.GetComponent<MeshFilter>().sharedMesh);UnityEngine.Object.DestroyImmediate(go);}foreach(var mat in materials)UnityEngine.Object.DestroyImmediate(mat);
  }
  File.WriteAllText(Path.Combine(folder,"replayed-poses.json"),poses.ToString());
  File.WriteAllLines(Path.Combine(folder,"replay-report.txt"),lines);Log.Out("[ArticulationReplay] "+folder+" poses="+poses.Count);
 }catch(Exception e){fail++;Log.Error("[ArticulationReplay] "+e);}Log.Out("[MechaMotionQA] COMPLETE failures="+fail);
 }
}
