using System;using System.IO;using System.Linq;using System.Collections.Generic;using UnityEngine;using HarmonyLib;using PZAEC.Mecha;
public sealed class FlightWalkNativeQA:IModApi {
 static List<string> report=new List<string>();static int failures;
 public void InitMod(Mod m){if(Environment.GetCommandLineArgs().Contains("-mechaMotionQA"))ModEvents.GameStartDone.RegisterHandler(Run);}
 static void Check(string label,bool ok){report.Add((ok?"PASS ":"FAIL ")+label);if(!ok)failures++;}
 static void Run(ref ModEvents.SGameStartDoneData d){bool auto=Physics.autoSimulation;Physics.autoSimulation=false;
 try{var world=GameManager.Instance.World;AccessTools.Field(typeof(Weapons),"currentWorld").SetValue(null,world);
 foreach(string name in new[]{Rules.VehicleName,Rules.CompleteVehicle,Rules.UltimateVehicle}){
 var v=EntityFactory.CreateEntity(EntityClass.FromString(name),new Vector3(0,400,0)+Origin.position) as EntityVehicle;world.SpawnEntityInWorld(v);
 for(var t=v.vehicleRB.transform;t!=null;t=t.parent)t.gameObject.SetActive(true);
 var rb=v.vehicleRB;var slots=AccessTools.Field(typeof(Entity),"attachedEntities");var saved=slots.GetValue(v);var pilot=EntityFactory.CreateEntity(EntityClass.FromString("playerMale"),v.position+Vector3.right*100) as EntityPlayer;world.SpawnEntityInWorld(pilot);pilot.Health=pilot.GetMaxHealth();pilot.AttachedToEntity=v;slots.SetValue(v,new Entity[]{pilot});v.hasDriver=v.IsEngineRunning=true;v.vehicle.SetFuelLevel(1000);
 var floor=new GameObject("Flight walk floor");floor.layer=16;floor.transform.position=new Vector3(0,399.5f,0);floor.AddComponent<BoxCollider>().size=new Vector3(80,1,100);
 try{foreach(float slope in new[]{0f,12f})foreach(float direction in new[]{4f,-2f})foreach(bool manual in new[]{false,true}){
 floor.transform.rotation=Quaternion.Euler(0,0,slope);Model.GetRig(v).ResetPose();Traversal.Forget(v);GroundSupport.Suspend(v);var s=Locomotion.Get(v);s.FlightActor=-1;s.FlightMode=manual?Flight.Phase.Cruise:Flight.Phase.Landing;s.ControlledLanding=true;s.ContactTime=0;s.Grounded=false;s.Toggle=s.Jump=false;s.Descend=manual;
 rb.isKinematic=false;rb.useGravity=true;Locomotion.PrepareSupport(s.Wheels,true);Physics.SyncTransforms();Physics.Simulate(.02f);rb.position=new Vector3(0,404,0);rb.rotation=Quaternion.identity;rb.velocity=rb.angularVelocity=Vector3.zero;v.SetPosition(rb.position+Origin.position);Physics.SyncTransforms();
 int ticks=0;for(;ticks<800&&Flight.Active(s);ticks++){
 var support=GroundSupport.Observe(v);Flight.Step(v,s,support.Grounded,true,.02f);
 Physics.Simulate(.02f);v.SetPosition(rb.position+Origin.position);Physics.SyncTransforms();Gait.Update(world,v,Model.GetRig(v),.02f);
 }
 string label=name+" slope="+slope+" drive="+direction+" manual="+manual;Check(label+" lands ticks="+ticks,s.FlightMode==Flight.Phase.Ground);report.Add("TOUCHDOWN "+GroundSupport.Diagnostics(v));
 float start=rb.position.z;int blocked=0;
 for(int tick=0;tick<250;tick++){
 var support=GroundSupport.Observe(v);support.DesiredVelocity=rb.rotation*Vector3.forward*direction;Traversal.Get(v).SearchAt=-100;float target=Traversal.LimitSpeed(v,support,direction,.02f);GroundSupport.Walking(support,.02f,true);if(!GroundSupport.MotionClear(support,.02f)){GroundSupport.StopHorizontal(support);target=0;}GroundSupport.Apply(support,.02f);if(support.Grounded)Locomotion.ApplyDrive(rb,rb.rotation*Vector3.forward,Mathf.Clamp(target,-support.DriveCap,support.DriveCap),0,false,support.Normal,.02f);if(support.DriveCap==0)blocked++;
 Physics.Simulate(.02f);v.SetPosition(rb.position+Origin.position);Physics.SyncTransforms();s.Grounded=support.Grounded;Gait.Update(world,v,Model.GetRig(v),.02f);
 }
 float distance=(rb.position.z-start)*Mathf.Sign(direction);Check(label+" walks without jump distance="+distance+" blocked="+blocked,distance>2);report.Add("WALK "+GroundSupport.Diagnostics(v));
 }}finally{slots.SetValue(v,saved);pilot.AttachedToEntity=null;world.RemoveEntity(pilot.entityId,EnumRemoveEntityReason.Despawned);UnityEngine.Object.DestroyImmediate(floor);v.vehicleRB.gameObject.SetActive(false);v.transform.gameObject.SetActive(false);world.RemoveEntity(v.entityId,EnumRemoveEntityReason.Despawned);GroundSupport.Forget(v);}
 }
 }catch(Exception e){Check("exception "+e,false);}finally{Physics.autoSimulation=auto;}
 File.WriteAllLines(Path.Combine(GameIO.GetSaveGameDir(),"flight-walk-qa.txt"),report);foreach(var line in report)Log.Out("[FlightWalkQA] "+line);Log.Out("[MechaMotionQA] COMPLETE failures="+failures);
 }
}
