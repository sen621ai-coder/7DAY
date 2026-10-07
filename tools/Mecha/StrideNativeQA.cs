using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using PZAEC.Mecha;
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
        try{foreach(string name in new[]{Rules.VehicleName,Rules.CompleteVehicle}){
            var v=EntityFactory.CreateEntity(EntityClass.FromString(name),new Vector3(0,400,0)+Origin.position) as EntityVehicle;world.SpawnEntityInWorld(v);for(var t=v.vehicleRB.transform;t!=null;t=t.parent)t.gameObject.SetActive(true);
            var floor=Box("Stride lane",new Vector3(0,399.5f,0),new Vector3(40,1,300));
            try{foreach(float speed in new[]{1.2f,4f,-2f,13.5f})Lane(v,speed,.02f);foreach(float dt in new[]{1f/30,1f/60,1f/120})Lane(v,4,dt);Turning(v);UnityEngine.Object.DestroyImmediate(floor);floor=null;Safety(v);}finally{UnityEngine.Object.DestroyImmediate(floor);v.vehicleRB.gameObject.SetActive(false);v.transform.gameObject.SetActive(false);world.RemoveEntity(v.entityId,EnumRemoveEntityReason.Despawned);GroundSupport.Forget(v);}
        }}catch(Exception e){Check("exception "+e,false);}finally{Physics.autoSimulation=auto;}
        var path=Path.Combine(GameIO.GetSaveGameDir(),"mecha-stride-qa.txt");File.WriteAllLines(path,lines);foreach(var line in lines)Log.Out("[MechaStrideQA] "+line);Log.Out("[MechaMotionQA] COMPLETE failures="+failures+" report="+path);
    }
}
