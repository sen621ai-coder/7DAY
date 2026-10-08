using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using PZAEC.Mecha;

// Render real SkinnedMeshRenderers; baked MeshRenderers hide the original bug.
public sealed class JusticeRenderNativeQA : IModApi {
 public void InitMod(Mod mod) {
  if(Environment.GetCommandLineArgs().Contains("-mechaMotionQA"))
   ModEvents.GameStartDone.RegisterHandler(Start);
 }
 static bool Pause(){return false;}
 static void Start(ref ModEvents.SGameStartDoneData data) {
  new Harmony("justice.render.qa").Patch(AccessTools.Method(typeof(Weapons),"Update"),prefix:new HarmonyMethod(typeof(JusticeRenderNativeQA),nameof(Pause)));
  new GameObject("Justice render QA").AddComponent<JusticeRenderProbe>();
 }
}

public sealed class JusticeRenderProbe : MonoBehaviour {
 JusticeRig rig; Camera near,far; Mesh[] before; int failures,renders; bool reported;
 readonly HashSet<int> levels=new HashSet<int>();
 void Error(string text){failures++;Log.Error("[JusticeRenderQA] "+text);}
 void Message(string text,string stack,LogType type) {
  if(!text.StartsWith("[JusticeRenderQA]") && (text.Contains("SkinnedMeshRenderer:") || text.Contains("vertex stride"))) { failures++; }
 }
 Camera MakeCamera(string name,float distance,bool main) {
  var c=new GameObject(name).AddComponent<Camera>();if(main)c.tag="MainCamera";
  c.targetTexture=new RenderTexture(256,256,24);c.cullingMask=1<<30;c.fieldOfView=38;
  c.clearFlags=CameraClearFlags.SolidColor;c.backgroundColor=Color.black;
  Place(c,distance);return c;
 }
 void Place(Camera c,float distance){var focus=rig.transform.position+Vector3.up*1.6f;c.transform.position=focus+new Vector3(0,0,-distance);c.transform.LookAt(focus);}
 void Before(Camera c){if(c!=near&&c!=far)return;before=rig.Parts.Select(p=>((SkinnedMeshRenderer)p).sharedMesh).ToArray();}
 void After(Camera c){if(c!=near&&c!=far)return;renders++;for(int i=0;i<before.Length;i++)if(before[i]!=((SkinnedMeshRenderer)rig.Parts[i]).sharedMesh){Error("mesh changed during camera render: "+rig.Roles[i]);break;}}
 IEnumerator Start() {
  Application.logMessageReceived+=Message;
  try {
   var w=GameManager.Instance.World;AccessTools.Field(typeof(Weapons),"currentWorld").SetValue(null,w);
   var v=EntityFactory.CreateEntity(EntityClass.FromString(Rules.CompleteVehicle),new Vector3(0,400,0)+Origin.position) as EntityVehicle;
   w.SpawnEntityInWorld(v);v.vehicle.SetItemValue(ItemClass.GetItem(Rules.CompleteItem,false));
   for(var t=v.vehicleRB.transform;t!=null;t=t.parent)t.gameObject.SetActive(true);
   v.vehicleRB.isKinematic=true;var r=Model.GetRig(v);r.ResetPose();rig=r.Justice;
   foreach(var t in r.Mount.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
   foreach(var p in rig.Parts)((SkinnedMeshRenderer)p).updateWhenOffscreen=true;
   near=MakeCamera("Justice near QA",6,true);far=MakeCamera("Justice far QA",100,false);far.depth=near.depth+1;
   Camera.onPreCull+=Before;Camera.onPostRender+=After;
  } catch(Exception e){Error(e.ToString());Finish();yield break;}
  for(int frame=0;frame<180;frame++) {
   // Automatic camera rendering drives Unity's real skinning lifecycle.
   Place(near,frame<60?6:frame<120?30:100);
   yield return null;
   levels.Add(rig.DetailLevel);
  }
  if(renders<180)Error("insufficient camera renders: "+renders);
  if(levels.Count!=3)Error("not all LODs exercised: "+string.Join(",",levels));
  Log.Out("[JusticeRenderQA] camera renders="+renders+" LODs="+string.Join(",",levels));Finish();
 }
 void Finish(){if(reported)return;reported=true;Camera.onPreCull-=Before;Camera.onPostRender-=After;Application.logMessageReceived-=Message;Log.Out("[MechaMotionQA] COMPLETE failures="+failures);}
 void OnDestroy(){Finish();}
}
