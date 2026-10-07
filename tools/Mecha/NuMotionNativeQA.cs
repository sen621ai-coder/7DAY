using System;using System.IO;using System.Linq;using System.Collections.Generic;using UnityEngine;using HarmonyLib;using PZAEC.Mecha;
public sealed class MechaStrideQA:IModApi
{
    static List<string> lines=new List<string>();static int failures;static World world;
    public void InitMod(Mod m){if(Environment.GetCommandLineArgs().Contains("-mechaMotionQA"))ModEvents.GameStartDone.RegisterHandler(Run);}
    static void Check(string label,bool ok){lines.Add((ok?"PASS ":"FAIL ")+label);if(!ok)failures++;}
    static GameObject Box(string name,Vector3 p,Vector3 size){var g=new GameObject(name);g.layer=16;g.transform.position=p;g.AddComponent<BoxCollider>().size=size;Physics.SyncTransforms();return g;}
    static void Reset(EntityVehicle v){var rig=Model.GetRig(v);rig.ResetPose();Traversal.Forget(v);GroundSupport.Suspend(v);var s=GroundSupport.Get(v);var rb=v.vehicleRB;rb.isKinematic=false;rb.useGravity=true;rb.drag=.05f;rb.rotation=Quaternion.identity;rb.position=new Vector3(0,400+s.Shape.NeutralY+Rules.SoleClearance,0);rb.velocity=rb.angularVelocity=Vector3.zero;v.SetPosition(rb.position+Origin.position);Physics.SyncTransforms();GroundSupport.Observe(v);}
    static void Step(EntityVehicle v,float dt,float target,float steer=0){var s=GroundSupport.Observe(v);s.DesiredVelocity=v.vehicleRB.rotation*Vector3.forward*target;Traversal.Get(v).SearchAt=-100;target=Traversal.LimitSpeed(v,s,target,dt);GroundSupport.Walking(s,dt,true);if(!GroundSupport.MotionClear(s,dt)){GroundSupport.StopHorizontal(s);target=0;}GroundSupport.Apply(s,dt);if(s.Grounded)Locomotion.ApplyDrive(v.vehicleRB,v.vehicleRB.rotation*Vector3.forward,Mathf.Clamp(target,-s.DriveCap,s.DriveCap),steer,false,s.Normal,dt);Physics.Simulate(dt);v.SetPosition(v.vehicleRB.position+Origin.position);Physics.SyncTransforms();Locomotion.Get(v).Grounded=s.Grounded;Gait.Update(world,v,Model.GetRig(v),dt);}
    static void Lane(EntityVehicle v,float wanted,float dt){Reset(v);var s=GroundSupport.Get(v);var rig=Model.GetRig(v);var durations=new List<float>();var strides=new List<float>();bool lost=false;float peak=0,error=0,minY=1000;int stops=0;var was=new bool[2];
        for(int k=0;k<Mathf.CeilToInt(10/dt);k++){
            Step(v,dt,wanted);lost|=!s.Grounded;peak=Mathf.Max(peak,Mathf.Abs(v.vehicleRB.velocity.z));minY=Mathf.Min(minY,v.vehicleRB.position.y);if(s.DriveCap==0)stops++;
            for(int side=0;side<2;side++){var f=s.Feet[side];if(f.Swing&&!was[side]&&k*dt>2){durations.Add(f.Duration);strides.Add(Vector3.ProjectOnPlane(f.To-f.From,Vector3.up).magnitude);}was[side]=f.Swing;
                error=Mathf.Max(error,Vector3.Distance((side==0?rig.FootL:rig.FootR).position+Origin.position,f.Position));}
        }
        string label=Rules.DisplayName(v)+" speed="+wanted+" dt="+dt+" peak="+peak+" distance="+v.vehicleRB.position.z+" duration="+(durations.Count>0?durations.Average():0)+" stride="+(strides.Count>0?strides.Average():0)+" steps="+durations.Count+" stops="+stops+" footError="+error;
        Check(label+" support",!lost&&error<.02f&&minY>399);
        Check(label+" progress",peak>=Mathf.Abs(wanted)*.85f&&Mathf.Abs(v.vehicleRB.position.z)>Mathf.Abs(wanted)*3);
        if(Mathf.Abs(wanted)<=4)Check(label+" visible stride",durations.Count>3&&durations.Average()>=.14&&strides.Average()>=Mathf.Abs(wanted)*.18);
        for(int k=0;k<Mathf.CeilToInt(4/dt);k++)Step(v,dt,0);
        Check(label+" stop",s.Grounded&&Mathf.Abs(v.vehicleRB.velocity.z)<.1f);
        lines.Add("DETAIL "+GroundSupport.Diagnostics(v));
    }
    static void Turning(EntityVehicle v){Reset(v);bool lost=false;float error=0;var rig=Model.GetRig(v);
        for(int k=0;k<150;k++){Step(v,.02f,0,1);var s=GroundSupport.Get(v);lost|=!s.Grounded;for(int side=0;side<2;side++)error=Mathf.Max(error,Vector3.Distance((side==0?rig.FootL:rig.FootR).position+Origin.position,s.Feet[side].Position));}
        float yaw=Mathf.Abs(Mathf.DeltaAngle(0,v.vehicleRB.rotation.eulerAngles.y));Check(Rules.DisplayName(v)+" standing turn yaw="+yaw+" error="+error,!lost&&yaw>60&&error<.02f);
    }
    static void Safety(EntityVehicle v){foreach(bool wall in new[]{true,false}){
        var floor=Box("Stride ledge",new Vector3(0,399.5f,-1.5f),new Vector3(30,1,10));var block=wall?Box("Stride wall",new Vector3(0,402,3),new Vector3(30,4,.25f)):null;
        try{Reset(v);bool lost=false;for(int k=0;k<300;k++){Step(v,.02f,4);lost|=!GroundSupport.Get(v).Grounded;}var s=GroundSupport.Get(v);var root=v.vehicleRB.position+Origin.position;
            Check(Rules.DisplayName(v)+(wall?" wall":" cliff")+" stops safely z="+v.vehicleRB.position.z,!lost&&v.vehicleRB.position.z<2.9f&&Mathf.Abs(v.vehicleRB.velocity.z)<.2f&&GroundSupport.HullClear(v,s.Shape,root,root,v.vehicleRB.rotation));
        }finally{if(block!=null)UnityEngine.Object.DestroyImmediate(block);UnityEngine.Object.DestroyImmediate(floor);}
    }}
    static void Run(ref ModEvents.SGameStartDoneData d){world=GameManager.Instance.World;bool auto=Physics.autoSimulation;Physics.autoSimulation=false;
        try{foreach(string name in new[]{Rules.VehicleName,Rules.CompleteVehicle,Rules.UltimateVehicle}){
            var v=EntityFactory.CreateEntity(EntityClass.FromString(name),new Vector3(0,400,0)+Origin.position) as EntityVehicle;world.SpawnEntityInWorld(v);for(var t=v.vehicleRB.transform;t!=null;t=t.parent)t.gameObject.SetActive(true);
            var floor=Box("Stride lane",new Vector3(0,399.5f,0),new Vector3(40,1,300));
            try{foreach(float speed in new[]{1.2f,4f,-2f,13.5f})Lane(v,speed,.02f);foreach(float dt in new[]{1f/30,1f/60,1f/120})Lane(v,4,dt);Turning(v);UnityEngine.Object.DestroyImmediate(floor);floor=null;Safety(v);}finally{UnityEngine.Object.DestroyImmediate(floor);v.vehicleRB.gameObject.SetActive(false);v.transform.gameObject.SetActive(false);world.RemoveEntity(v.entityId,EnumRemoveEntityReason.Despawned);GroundSupport.Forget(v);}
        }}catch(Exception e){Check("exception "+e,false);}finally{Physics.autoSimulation=auto;}
        var path=Path.Combine(GameIO.GetSaveGameDir(),"mecha-stride-qa.txt");File.WriteAllLines(path,lines);foreach(var line in lines)Log.Out("[MechaStrideQA] "+line);NuSuite.Count+=failures;Log.Out("[NuSubQA] COMPLETE failures="+failures+" report="+path);
    }
}

public sealed class JusticeCombatNativeQA:IModApi {
 static int failures;static List<string> lines=new List<string>();
 public void InitMod(Mod m){if(Environment.GetCommandLineArgs().Contains("-mechaMotionQA"))ModEvents.GameStartDone.RegisterHandler(Run);}
 static void Check(string label,bool pass){lines.Add((pass?"PASS ":"FAIL ")+label);if(!pass)failures++;}
 static void Run(ref ModEvents.SGameStartDoneData d){try{var w=GameManager.Instance.World;var v=EntityFactory.CreateEntity(EntityClass.FromString(Rules.UltimateVehicle),new Vector3(0,400,0)+Origin.position) as EntityVehicle;w.SpawnEntityInWorld(v);v.vehicle.SetItemValue(ItemClass.GetItem(Rules.UltimateItem,false));for(var t=v.vehicleRB.transform;t!=null;t=t.parent)t.gameObject.SetActive(true);v.vehicleRB.isKinematic=true;var r=Model.GetRig(v);var s=Samurai.Get(v);
 Check("124 bones and four influences",r.Justice.Bones.Length==124&&r.Justice.Parts.All(p=>((SkinnedMeshRenderer)p).quality==SkinQuality.Bone4));
 foreach(bool heavy in new[]{false,true})foreach(int combo in new[]{0,1}){int blocked=0;float drift=0,grip=0;for(int i=0;i<=90;i++){r.ResetPose();Justice.Walk(v,r,0,0,.02f);s.Swing=true;s.Heavy=heavy;s.Combo=combo;s.Started=Time.time-i*(heavy?Samurai.HeavyDuration:Samurai.NormalDuration)/90;Justice.Combat(v,r,s,Time.time);var tipBeforeWeight=SwordMotion.Tip(r);var finish=typeof(Justice).Assembly.GetType("PZAEC.Mecha.JusticeFinish");if(finish!=null){float phase=SwordMotion.Phase(s,Time.time);if(phase>=SwordMotion.WindEnd&&phase<=SwordMotion.CutEnd){finish.GetMethod("Weight").Invoke(null,new object[]{v,r,.02f});Justice.Combat(v,r,s,Time.time);if(Vector3.Distance(tipBeforeWeight,SwordMotion.Tip(r))>.0001f)throw new Exception("Polish changed the active cut trajectory");}}if(!Justice.BladeClear(r))blocked++;grip=Mathf.Max(grip,Vector3.Distance(r.Sword.position,r.HandR.position));var tip=SwordMotion.Tip(r);SwordMotion.Pose(v,r,s,Time.time);drift=Mathf.Max(drift,Vector3.Distance(tip,SwordMotion.Tip(r)));}
 Check("sword heavy="+heavy+" combo="+combo+" deterministic drift="+drift,drift<.001f);Check("sword mounted grip distance="+grip,grip<.2f);Check("sword body clearance blocked frames="+blocked,blocked==0);}
 s.Swing=false;s.LaserCharge=1;r.ResetPose();Justice.Combat(v,r,s,Time.time);Check("rifle replaces sword",r.Justice.Parts.Where((p,i)=>r.Justice.Roles[i]=="Weapon64").All(p=>p.gameObject.activeSelf)&&r.Justice.Parts.Where((p,i)=>r.Justice.Roles[i]=="Weapon54").All(p=>!p.gameObject.activeSelf));Check("rifle muzzle finite",Weapons.Finite(r.Justice.GunMuzzle.position.x)&&Vector3.Distance(r.HandR.position,r.Justice.GunMuzzle.position)<2.5f);
 s.LaserCharge=0;s.Guarding=true;s.GuardBlend=0;r.ResetPose();Samurai.Pose(v,r,.11f,Time.time);Check("guard raises progressively",Mathf.Abs(s.GuardBlend-.5f)<.001f);s.Guarding=false;Samurai.Pose(v,r,.11f,Time.time);Check("guard returns progressively",s.GuardBlend==0);s.Guarding=true;r.ResetPose();Justice.Combat(v,r,s,Time.time);Check("shield remains visible",r.Justice.Parts.Where((p,i)=>r.Justice.Roles[i]=="Weapon63").All(p=>p.gameObject.activeSelf));r.ResetPose();Check("scale restored after combat",r.Justice.Bones.Select((b,i)=>(b.localScale-r.Justice.BaseScale[i]).sqrMagnitude).All(x=>x<.000001f));
 s.Guarding=false;s.LaserCharge=1;r.ResetPose();Justice.Combat(v,r,s,Time.time);r.ResetPose();Justice.Board(r,1);Check("boarding clears rifle and beam visibility",r.Justice.Parts.Where((p,i)=>r.Justice.Roles[i]=="Weapon64"||r.Justice.Roles[i]=="Weapon54").All(p=>!p.gameObject.activeSelf));Check("boarding kneels without moving physical root",Mathf.Abs((r.Torso.localPosition-r.TorsoBasePosition).y+.45f)<.001f&&Mathf.Abs(v.vehicleRB.position.y-400)<.001f);s.RifleBlend=s.GuardBlend=1;Samurai.Stop(v);Check("boarding stop clears presentation blends",s.RifleBlend==0&&s.GuardBlend==0);v.vehicleRB.gameObject.SetActive(false);v.gameObject.SetActive(false);w.RemoveEntity(v.entityId,EnumRemoveEntityReason.Despawned);
 }catch(Exception e){Check("exception "+e,false);}var path=Path.Combine(GameIO.GetSaveGameDir(),"justice-combat-qa.txt");File.WriteAllLines(path,lines);foreach(var l in lines)Log.Out("[JusticeCombatQA] "+l);NuSuite.Count+=failures;Log.Out("[NuSubQA] COMPLETE failures="+failures+" report="+path);}
}

public sealed class SkimNativeQA:IModApi {
 static int failures;static List<string> lines=new List<string>();
 public void InitMod(Mod m){if(Environment.GetCommandLineArgs().Contains("-mechaMotionQA"))ModEvents.GameStartDone.RegisterHandler(Run);}
 static void Check(string name,bool ok){lines.Add((ok?"PASS ":"FAIL ")+name);if(!ok)failures++;}
 static GameObject Box(Vector3 pos,Vector3 size){var o=GameObject.CreatePrimitive(PrimitiveType.Cube);o.layer=16;o.transform.position=pos;o.transform.localScale=size;return o;}
 static void Run(ref ModEvents.SGameStartDoneData data){bool automatic=Physics.autoSimulation;Physics.autoSimulation=false;try{
 Check("remote gait phase wraps forward without a jump",Mathf.Abs(Mathf.DeltaAngle(0,GroundNet.BlendPhase(.9f,.1f,.5f)*360))<.01f);
 var w=GameManager.Instance.World;AccessTools.Field(typeof(Weapons),"currentWorld").SetValue(null,w);
 foreach(bool complete in new[]{false,true}){
 var v=EntityFactory.CreateEntity(EntityClass.FromString(complete?Rules.UltimateVehicle:Rules.VehicleName),new Vector3(0,400,0)+Origin.position) as EntityVehicle;w.SpawnEntityInWorld(v);v.vehicle.SetItemValue(ItemClass.GetItem(complete?Rules.UltimateItem:Rules.PlaceableItem,false));v.vehicle.SetFuelLevel(100);
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
 var path=Path.Combine(GameIO.GetSaveGameDir(),"mecha-skim-qa.txt");File.WriteAllLines(path,lines);foreach(var l in lines)Log.Out("[JusticeSkimQA] "+l);NuSuite.Count+=failures;Log.Out("[MechaMotionQA] COMPLETE failures="+NuSuite.Count+" report="+path);
 }
}

static class NuSuite{public static int Count;}
