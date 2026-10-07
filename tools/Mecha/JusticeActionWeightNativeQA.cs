using System;using System.IO;using System.Linq;using System.Collections.Generic;using UnityEngine;using PZAEC.Mecha;
// Compare the entire damage interval, including the sweep tail, with 0.20.0's
// body overlay. Outside it, check full-body compression, planted feet and clearance.
public sealed class JusticeActionWeightNativeQA:IModApi {
 static List<string> lines=new List<string>();static int failures;
 public void InitMod(Mod m){if(Environment.GetCommandLineArgs().Contains("-mechaMotionQA"))ModEvents.GameStartDone.RegisterHandler(Run);}
 static void Check(string text,bool pass){lines.Add((pass?"PASS ":"FAIL ")+text);if(!pass)failures++;}
 static void OriginalWeight(Model.Rig r,Samurai.State s){float t=Mathf.Clamp01(SwordMotion.Phase(s,Time.time));float weight=t<SwordMotion.WindEnd?Mathf.Sin(t/SwordMotion.WindEnd*Mathf.PI):t>SwordMotion.CutEnd?Mathf.Sin((t-SwordMotion.CutEnd)/(1-SwordMotion.CutEnd)*Mathf.PI):0;r.Torso.localRotation*=Quaternion.Euler(-1.5f*weight,0,(s.Combo==1?-1:1)*1.2f*weight);r.ShoulderL.localRotation*=Quaternion.Euler(-3*weight,0,-2*weight);}
 static void Run(ref ModEvents.SGameStartDoneData d){try{
  var world=GameManager.Instance.World;var v=EntityFactory.CreateEntity(EntityClass.FromString(Rules.CompleteVehicle),new Vector3(0,400,0)+Origin.position) as EntityVehicle;world.SpawnEntityInWorld(v);v.vehicle.SetItemValue(ItemClass.GetItem(Rules.CompleteItem,false));for(var t=v.vehicleRB.transform;t!=null;t=t.parent)t.gameObject.SetActive(true);v.vehicleRB.isKinematic=true;var r=Model.GetRig(v);var s=Samurai.Get(v);
  foreach(float dt in new[]{1f/30,1f/60,1f/120})foreach(bool heavy in new[]{false,true})foreach(int combo in new[]{0,1}){
   s.Swing=true;s.Heavy=heavy;s.Combo=combo;s.StartCharge=heavy?1:0;s.Charging=false;float drift=0,error=0,compression=0;int blocked=0,samples=0;
   float duration=heavy?Samurai.HeavyDuration:Samurai.NormalDuration;
   for(float age=0;age<=duration;age+=dt){s.Started=Time.time-age;r.ResetPose();Justice.Walk(v,r,0,0,dt);var left=r.FootL.position;var right=r.FootR.position;var basePosition=r.Torso.localPosition;OriginalWeight(r,s);Justice.Combat(v,r,s,Time.time);var oldRoot=SwordMotion.Root(r);var oldTip=SwordMotion.Tip(r);
    r.ResetPose();Justice.Walk(v,r,0,0,dt);JusticeFinish.Weight(v,r,dt);Justice.Combat(v,r,s,Time.time);Gait.Solve(r,0,left,Vector3.up);Gait.Solve(r,1,right,Vector3.up);JusticeMotion.Finish(v,r);
    if(SwordMotion.DamagePhase(SwordMotion.Phase(s,Time.time))){samples++;drift=Mathf.Max(drift,Vector3.Distance(oldRoot,SwordMotion.Root(r)),Vector3.Distance(oldTip,SwordMotion.Tip(r)));}
    compression=Mathf.Max(compression,basePosition.y-r.Torso.localPosition.y);error=Mathf.Max(error,Vector3.Distance(left,r.FootL.position),Vector3.Distance(right,r.FootR.position));if(!Justice.BladeClear(r))blocked++;
   }
   string label="dt="+dt+" heavy="+heavy+" combo="+combo;
   Check(label+" damage path drift="+drift+" samples="+samples,samples>=3&&drift<.0001f);
   Check(label+" anticipatory compression="+compression,compression>.05f);
   Check(label+" planted foot error="+error,error<.02f);
   Check(label+" whole-action blade blocked="+blocked,blocked==0);
  }
  s.Swing=false;s.Charging=true;s.PressedAt=Time.time-Samurai.HeavyCharge;r.ResetPose();Justice.Walk(v,r,0,0,.02f);float before=r.Torso.localPosition.y;JusticeFinish.Weight(v,r,.02f);Justice.Combat(v,r,s,Time.time);Check("charged stance compresses before release",before-r.Torso.localPosition.y>.06f);
 }catch(Exception e){Check("exception "+e,false);}var path=Path.Combine(GameIO.GetSaveGameDir(),"justice-action-weight-qa.txt");File.WriteAllLines(path,lines);foreach(var l in lines)Log.Out("[JusticeActionWeightQA] "+l);Log.Out("[MechaMotionQA] COMPLETE failures="+failures+" report="+path);}
}
