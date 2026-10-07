using System;using System.IO;using System.Linq;using System.Collections.Generic;using UnityEngine;using HarmonyLib;using PZAEC.Mecha;
public sealed class SkimNativeQA:IModApi {
 static int failures;static List<string> lines=new List<string>();
 public void InitMod(Mod m){if(Environment.GetCommandLineArgs().Contains("-mechaMotionQA"))ModEvents.GameStartDone.RegisterHandler(Run);}
 static void Check(string name,bool ok){lines.Add((ok?"PASS ":"FAIL ")+name);if(!ok)failures++;}
 static GameObject Box(Vector3 pos,Vector3 size){var o=GameObject.CreatePrimitive(PrimitiveType.Cube);o.layer=16;o.transform.position=pos;o.transform.localScale=size;return o;}
 static void Run(ref ModEvents.SGameStartDoneData data){bool automatic=Physics.autoSimulation;Physics.autoSimulation=false;try{
 Check("remote gait phase wraps forward without a jump",Mathf.Abs(Mathf.DeltaAngle(0,GroundNet.BlendPhase(.9f,.1f,.5f)*360))<.01f);
 var w=GameManager.Instance.World;AccessTools.Field(typeof(Weapons),"currentWorld").SetValue(null,w);
 foreach(bool complete in new[]{false,true}){
 var v=EntityFactory.CreateEntity(EntityClass.FromString(complete?Rules.CompleteVehicle:Rules.VehicleName),new Vector3(0,400,0)+Origin.position) as EntityVehicle;w.SpawnEntityInWorld(v);v.vehicle.SetItemValue(ItemClass.GetItem(complete?Rules.CompleteItem:Rules.PlaceableItem,false));v.vehicle.SetFuelLevel(100);
 var rb=v.vehicleRB;for(var t=rb.transform;t!=null;t=t.parent)t.gameObject.SetActive(true);rb.isKinematic=false;rb.useGravity=true;rb.drag=.05f;rb.rotation=Quaternion.identity;
 var floor=Box(new Vector3(0,399.5f,50),new Vector3(30,1,240));Physics.SyncTransforms();var s=Locomotion.Get(v);s.Grounded=true;
 foreach(float dt in new[]{1f/30,1f/50,1f/60,1f/120})foreach(float start in new[]{0f,4f}){
 rb.position=new Vector3(0,400,0);rb.rotation=Quaternion.identity;rb.velocity=Vector3.forward*start;rb.angularVelocity=Vector3.zero;
 v.SetPosition(rb.position+Origin.position);GroundSupport.Forget(v);s.SkimPhase=Skim.Phase.Off;s.SkimLatch=false;s.FlightMode=Flight.Phase.Ground;s.Jump=false;
 rb.position=new Vector3(0,400+GroundSupport.Get(v).Shape.NeutralY+Rules.SoleClearance,0);v.SetPosition(rb.position+Origin.position);Physics.SyncTransforms();
 float t90=-1,peak=0;var curve=new List<string>();
 for(int tick=0;tick<Mathf.CeilToInt(2/dt);tick++){v.SetPosition(rb.position+Origin.position);Skim.Drive(v,s,tick==0,true,1,0,true,dt);Physics.Simulate(dt);float speed=Vector3.ProjectOnPlane(rb.velocity,Vector3.up).magnitude;peak=Mathf.Max(peak,speed);if(t90<0&&speed>=Skim.Speed*.9f)t90=(tick+1)*dt;curve.Add(((tick+1)*dt).ToString("F4")+":"+speed.ToString("F3"));}
 Check(Rules.DisplayName(v)+" start="+start+" Hz="+(1/dt)+" t90="+t90+" peak="+peak+" reason="+s.SkimReason,t90>0&&t90<=1.2f&&peak<=Skim.Speed+.05f);lines.Add("CURVE "+string.Join(",",curve));
 }
 rb.position=new Vector3(0,400,0);rb.velocity=Vector3.zero;v.SetPosition(rb.position+Origin.position);GroundSupport.Forget(v);s.SkimPhase=Skim.Phase.Off;s.SkimLatch=false;
 for(int i=0;i<500;i++){v.SetPosition(rb.position+Origin.position);Skim.Drive(v,s,i==0,true,1,0,true,.02f);Physics.Simulate(.02f);}
 Check("physical hover height="+(rb.position.y-400),Mathf.Abs(rb.position.y-400-Skim.Height)<.04f);Check("cruise speed="+rb.velocity.z,rb.velocity.z>13&&rb.velocity.z<13.6f);
 for(int i=0;i<500&&s.SkimPhase!=Skim.Phase.Off;i++){v.SetPosition(rb.position+Origin.position);Skim.Drive(v,s,false,false,0,0,true,.02f);Physics.Simulate(.02f);}
 Check("release settles phase="+s.SkimPhase+" height="+(rb.position.y-400),s.SkimPhase==Skim.Phase.Off&&Mathf.Abs(rb.position.y-400)<.08f);
 rb.position=new Vector3(0,400.3f,0);rb.velocity=Vector3.forward*Skim.Speed;rb.rotation=Quaternion.identity;v.SetPosition(rb.position+Origin.position);s.SkimPhase=Skim.Phase.Cruise;s.SkimGround=400+Origin.position.y;s.SkimTarget=s.SkimGround+Skim.Height;s.SkimLatch=false;
 for(int i=0;i<50;i++){v.SetPosition(rb.position+Origin.position);Skim.Drive(v,s,false,true,1,1,true,.02f);Physics.Simulate(.02f);}
 Check(Rules.DisplayName(v)+" steering turns upright and reduces speed",Mathf.Abs(Mathf.DeltaAngle(0,rb.rotation.eulerAngles.y))>35&&Vector3.Dot(rb.rotation*Vector3.up,Vector3.up)>.99f&&Vector3.ProjectOnPlane(rb.velocity,Vector3.up).magnitude<6);
 rb.rotation=Quaternion.identity;
 foreach(float relief in new[]{.25f,.5f,1f,1.1f,-1f,-1.1f})Check("relief "+relief,Skim.ReliefAllowed(400,400+relief)==(Mathf.Abs(relief)<=1));
 Check("braking horizon 13.5",Skim.Lookahead(13.5f)>=13.5f*13.5f/20+2.2f-.001f);
 floor.transform.localScale=new Vector3(30,1,140);Physics.SyncTransforms();
 rb.position=new Vector3(0,400.3f,105);rb.velocity=Vector3.forward*13.5f;v.SetPosition(rb.position+Origin.position);s.SkimPhase=Skim.Phase.Cruise;s.SkimGround=400+Origin.position.y;s.SkimTarget=s.SkimGround+.3f;s.SkimLatch=false;
 for(int i=0;i<500&&s.SkimPhase!=Skim.Phase.Off;i++){v.SetPosition(rb.position+Origin.position);Skim.Drive(v,s,false,true,1,0,true,.02f);Physics.Simulate(.02f);}
 Check("cliff brakes before rim z="+rb.position.z,rb.position.z<119&&rb.velocity.z<.3f&&s.SkimLatch);
 Skim.Cancel(s);Check("cancel clears drive",s.SkimPhase==Skim.Phase.Off&&!s.Boost);
 Check("motion rejects skim plus flight",!Locomotion.Receive(v,123,300,new Vector3(136,0,0)));
 Check("motion rejects skim on ground",!Locomotion.Receive(v,123,301,new Vector3(132,0,0)));
 UnityEngine.Object.DestroyImmediate(floor);
 foreach(float relief in new[]{.25f,.5f,1f,1.1f,-.25f,-.5f,-1f,-1.1f}){
  var near=Box(new Vector3(0,399.5f,-44),new Vector3(30,1,112));var far=Box(new Vector3(0,399.5f+relief,62),new Vector3(30,1,100));rb.position=new Vector3(0,400.3f,0);rb.velocity=Vector3.forward*13.5f;rb.rotation=Quaternion.identity;v.SetPosition(rb.position+Origin.position);GroundSupport.Forget(v);GroundSupport.Get(v);s.SkimPhase=Skim.Phase.Cruise;s.SkimGround=400+Origin.position.y;s.SkimTarget=s.SkimGround+.3f;s.SkimLatch=false;s.SkimAge=0;Physics.SyncTransforms();
  for(int i=0;i<350&&s.SkimPhase!=Skim.Phase.Off;i++){v.SetPosition(rb.position+Origin.position);Skim.Drive(v,s,false,true,1,0,true,.02f);Physics.Simulate(.02f);}
  Check("physical relief="+relief+" z="+rb.position.z+" y="+rb.position.y,Mathf.Abs(relief)<=1?rb.position.z>18&&rb.position.y>=399.9f+relief:rb.position.z<11&&rb.velocity.z<.3f);
  UnityEngine.Object.DestroyImmediate(near);UnityEngine.Object.DestroyImmediate(far);
 }
 var pad=Box(new Vector3(0,399.5f,0),new Vector3(30,1,30));var roof=Box(new Vector3(0,403.4f,0),new Vector3(30,1,30));rb.position=new Vector3(0,399.9f,0);rb.velocity=Vector3.zero;v.SetPosition(rb.position+Origin.position);s.SkimPhase=Skim.Phase.Off;s.SkimLatch=false;Physics.SyncTransforms();Skim.Drive(v,s,true,true,1,0,true,.02f);Check("low roof brakes before ascent phase="+s.SkimPhase,s.SkimPhase==Skim.Phase.Settling||s.SkimPhase==Skim.Phase.Off);UnityEngine.Object.DestroyImmediate(roof);
 s.SkimPhase=Skim.Phase.Cruise;s.Boost=true;Check("power loss cancels drive",!Skim.Drive(v,s,false,true,1,0,false,.02f)&&s.SkimPhase==Skim.Phase.Off&&!s.Boost);
 s.SkimPhase=Skim.Phase.Cruise;Skim.Cancel(s);Flight.Advance(s,true,true,false,0,400.3f,.02f);Check("skim to Q cruise preserves release latch",s.SkimPhase==Skim.Phase.Off&&s.FlightMode==Flight.Phase.Cruise&&s.SkimLatch);UnityEngine.Object.DestroyImmediate(pad);
 v.vehicleRB.gameObject.SetActive(false);v.transform.gameObject.SetActive(false);w.RemoveEntity(v.entityId,EnumRemoveEntityReason.Despawned);GroundSupport.Forget(v);
 }
 using(var stream=new MemoryStream()){var wire=new GroundWire{Sequence=1,WalkPhase=.73f};var writer=new PooledBinaryWriter();writer.SetBaseStream(stream);wire.Write(writer);writer.Flush();Check("ground message length",stream.Length==81);stream.Position=0;var reader=new PooledBinaryReader();reader.SetBaseStream(stream);var copy=new GroundWire();copy.Read(reader);Check("ground gait round trip",Mathf.Abs(copy.WalkPhase-.73f)<.00001f);}
 }catch(Exception e){Check("exception "+e,false);}finally{Physics.autoSimulation=automatic;}
 var path=Path.Combine(GameIO.GetSaveGameDir(),"mecha-skim-qa.txt");File.WriteAllLines(path,lines);foreach(var l in lines)Log.Out("[JusticeSkimQA] "+l);Log.Out("[MechaMotionQA] COMPLETE failures="+failures+" report="+path);
 }
}
