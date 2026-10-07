using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using PZAEC.Mecha;
public sealed class JusticeCarryTransitionQA:IModApi
{
    static List<string> curves=new List<string>{"model,speed,dt,time,stride,support,pelvisY,lean,leftArm,rightArm,footError"};static List<string> lines=new List<string>();static int failures;static World world;
    public void InitMod(Mod m){if(Environment.GetCommandLineArgs().Contains("-mechaMotionQA"))ModEvents.GameStartDone.RegisterHandler(Run);}
    static void Check(string label,bool ok){lines.Add((ok?"PASS ":"FAIL ")+label);if(!ok)failures++;}
    static GameObject Box(string name,Vector3 p,Vector3 size){var g=new GameObject(name);g.layer=16;g.transform.position=p;g.AddComponent<BoxCollider>().size=size;Physics.SyncTransforms();return g;}
    static void Reset(EntityVehicle v){var rig=Model.GetRig(v);rig.ResetPose();Traversal.Forget(v);GroundSupport.Suspend(v);var s=GroundSupport.Get(v);var rb=v.vehicleRB;rb.isKinematic=false;rb.useGravity=true;rb.drag=.05f;rb.rotation=Quaternion.identity;rb.position=new Vector3(0,400+s.Shape.NeutralY+Rules.SoleClearance,0);rb.velocity=rb.angularVelocity=Vector3.zero;v.SetPosition(rb.position+Origin.position);Physics.SyncTransforms();GroundSupport.Observe(v);}
    static void Step(EntityVehicle v,float dt,float target,float steer=0){var s=GroundSupport.Observe(v);s.DesiredVelocity=v.vehicleRB.rotation*Vector3.forward*target;Traversal.Get(v).SearchAt=-100;target=Traversal.LimitSpeed(v,s,target,dt);GroundSupport.Walking(s,dt,true);if(!GroundSupport.MotionClear(s,dt)){GroundSupport.StopHorizontal(s);target=0;}GroundSupport.Apply(s,dt);if(s.Grounded)Locomotion.ApplyDrive(v.vehicleRB,v.vehicleRB.rotation*Vector3.forward,Mathf.Clamp(target,-s.DriveCap,s.DriveCap),steer,false,s.Normal,dt);Physics.Simulate(dt);v.SetPosition(v.vehicleRB.position+Origin.position);Physics.SyncTransforms();Locomotion.Get(v).Grounded=s.Grounded;Gait.Update(world,v,Model.GetRig(v),dt);}
    static void Lane(EntityVehicle v,float wanted,float dt){Reset(v);var s=GroundSupport.Get(v);var rig=Model.GetRig(v);var durations=new List<float>();var strides=new List<float>();bool lost=false;float peak=0,error=0,minY=1000;int stops=0;var was=new bool[2];float minPelvis=100,maxPelvis=-100,minLeft=1000,maxLeft=-1000,minRight=1000,maxRight=-1000;int bladeBlocked=0;float minPitch=100,maxPitch=-100,maxOutward=0;int toeOutSamples=0,legSamples=0;float toeDrift=0,minSoleHeight=100;float correlationR=0,correlationL=0,bladeError=0,bladeRate=0,minBladeHeight=100;Vector3 previousBlade=Vector3.zero;
        for(int k=0;k<Mathf.CeilToInt(10/dt);k++){
            Step(v,dt,wanted);lost|=!s.Grounded;peak=Mathf.Max(peak,Mathf.Abs(v.vehicleRB.velocity.z));minY=Mathf.Min(minY,v.vehicleRB.position.y);if(s.DriveCap==0)stops++;
            for(int side=0;side<2;side++){var f=s.Feet[side];if(f.Swing&&!was[side]&&k*dt>2){durations.Add(f.Duration);strides.Add(Vector3.ProjectOnPlane(f.To-f.From,Vector3.up).magnitude);}was[side]=f.Swing;
                error=Mathf.Max(error,Vector3.Distance((side==0?rig.FootL:rig.FootR).position+Origin.position,f.Position));
                if(rig.Justice!=null&&k*dt>2){var hip=side==0?rig.HipL:rig.HipR;var knee=side==0?rig.KneeL:rig.KneeR;var ankle=side==0?rig.AnkleL:rig.AnkleR;var angles=(Quaternion.Inverse(rig.Mount.rotation)*ankle.rotation).eulerAngles;float pitch=Mathf.DeltaAngle(0,angles.x);if(f.Swing){minPitch=Mathf.Min(minPitch,pitch);maxPitch=Mathf.Max(maxPitch,pitch);}else{legSamples++;if(Mathf.Abs(Mathf.DeltaAngle(0,angles.y))>1&&Mathf.Abs(Mathf.DeltaAngle(0,angles.y))<3)toeOutSamples++;}var axis=(ankle.position-hip.position).normalized;maxOutward=Mathf.Max(maxOutward,Vector3.Dot(Vector3.ProjectOnPlane(knee.position-hip.position,axis),rig.Mount.right)*(side==0?-1:1));var foot=side==0?rig.FootL:rig.FootR;var normal=f.Normal;var flat=Quaternion.FromToRotation(rig.Mount.up,normal)*rig.Mount.rotation;var facing=Quaternion.Inverse(flat)*ankle.forward;float yaw=Mathf.Atan2(facing.x,facing.z)*Mathf.Rad2Deg;
                if(!f.Swing){var expectedToe=f.Position-Origin.position+flat*Quaternion.Euler(0,yaw,0)*Vector3.forward*(Justice.SoleDepth*.5f);var actualToe=foot.position+ankle.forward*(Justice.SoleDepth*.5f);toeDrift=Mathf.Max(toeDrift,Vector3.Distance(expectedToe,actualToe));}
                float groundY=(f.Swing?f.From.y:f.Position.y)-Rules.SoleClearance;for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2){var corner=foot.position+Origin.position+ankle.rotation*new Vector3(x*Justice.SoleWidth*.5f,0,z*Justice.SoleDepth*.5f);minSoleHeight=Mathf.Min(minSoleHeight,corner.y-groundY);}
}
}
            if(rig.Justice!=null){var j=rig.Justice;
 if(k*dt>2){float stride=j.BodyStride;float rightHand=Vector3.Dot(rig.HandR.position-rig.ShoulderR.position,rig.Mount.forward)-.22f;float leftHand=Vector3.Dot(rig.HandL.position-rig.ShoulderL.position,rig.Mount.forward)-.12f;correlationR+=stride*rightHand;correlationL+=stride*leftHand;var blade=(SwordMotion.Tip(rig)-SwordMotion.Root(rig)).normalized;bladeError=Mathf.Max(bladeError,Vector3.Angle(blade,rig.Mount.TransformDirection(new Vector3(.32f,-.22f,1))));if(previousBlade!=Vector3.zero)bladeRate=Mathf.Max(bladeRate,Vector3.Angle(blade,previousBlade)/dt);previousBlade=blade;minBladeHeight=Mathf.Min(minBladeHeight,Mathf.Min(SwordMotion.Root(rig).y,SwordMotion.Tip(rig).y)-400);}
float left=Mathf.DeltaAngle(0,rig.ShoulderL.localEulerAngles.x),right=Mathf.DeltaAngle(0,rig.ShoulderR.localEulerAngles.x);if(k*dt>2){minPelvis=Mathf.Min(minPelvis,rig.Torso.localPosition.y);maxPelvis=Mathf.Max(maxPelvis,rig.Torso.localPosition.y);minLeft=Mathf.Min(minLeft,left);maxLeft=Mathf.Max(maxLeft,left);minRight=Mathf.Min(minRight,right);maxRight=Mathf.Max(maxRight,right);if(!Justice.BladeClear(rig))bladeBlocked++;}curves.Add(string.Join(",",new object[]{Rules.DisplayName(v),wanted,dt,k*dt,j.BodyStride,j.BodySupport,rig.Torso.localPosition.y,j.BodyLean,left,right,error}));}

        }
        string label=Rules.DisplayName(v)+" speed="+wanted+" dt="+dt+" peak="+peak+" distance="+v.vehicleRB.position.z+" duration="+(durations.Count>0?durations.Average():0)+" stride="+(strides.Count>0?strides.Average():0)+" steps="+durations.Count+" stops="+stops+" footError="+error;
        Check(label+" support",!lost&&error<.02f&&minY>399);
        Check(label+" progress",peak>=Mathf.Abs(wanted)*.85f&&Mathf.Abs(v.vehicleRB.position.z)>Mathf.Abs(wanted)*3);
        if(Mathf.Abs(wanted)<=4)Check(label+" visible stride",durations.Count>3&&durations.Average()>=.14&&strides.Average()>=Mathf.Abs(wanted)*.18);
        if(rig.Justice!=null&&Mathf.Abs(wanted)<=4){Check(label+" articulated pelvis range="+(maxPelvis-minPelvis),maxPelvis-minPelvis>.012f);Check(label+" contralateral grip motion right="+correlationR+" left="+correlationL,correlationR>1&&correlationL<-.5f);Check(label+" blade direction error="+bladeError+" angular speed="+bladeRate,bladeError<3&&bladeRate<90);Check(label+" blade ground clearance="+minBladeHeight,minBladeHeight>.12f);Check(label+" blade clearance blocked="+bladeBlocked,bladeBlocked==0);}
        if(rig.Justice!=null&&Mathf.Abs(wanted)<=4){Check(label+" planted toe drift="+toeDrift,toeDrift<.002f);Check(label+" sole corners above floor="+minSoleHeight,minSoleHeight>-.002f);Check(label+" heel-to-toe swing pitch="+minPitch+".."+maxPitch,minPitch< -6&&maxPitch>6);Check(label+" forward-tracking knee lateral deviation="+maxOutward,maxOutward<.075f);Check(label+" grounded toe-out samples="+toeOutSamples+"/"+legSamples,toeOutSamples>legSamples*.65f);}
        for(int k=0;k<Mathf.CeilToInt(4/dt);k++)Step(v,dt,0);
        if(rig.Justice!=null){Check(label+" planned stance width="+Mathf.Abs(s.Shape.Home[0].x-s.Shape.Home[1].x),Mathf.Abs(s.Shape.Home[0].x-s.Shape.Home[1].x)>=.499f&&Mathf.Abs(s.Shape.Home[0].x-s.Shape.Home[1].x)<=.501f);Check(label+" settled visible stance="+Mathf.Abs(Vector3.Dot(rig.FootR.position-rig.FootL.position,rig.Mount.right)),Mathf.Abs(Vector3.Dot(rig.FootR.position-rig.FootL.position,rig.Mount.right))>.43f&&Mathf.Abs(Vector3.Dot(rig.FootR.position-rig.FootL.position,rig.Mount.right))<.57f);}

        if(rig.Justice!=null)Check(label+" stationary pose settles",Mathf.Abs(rig.Justice.BodySupport)<.005f&&Mathf.Abs(rig.Justice.BodyStride)<.005f&&Mathf.Abs(rig.Justice.BodyLean)<.1f);
        Check(label+" stop",s.Grounded&&Mathf.Abs(v.vehicleRB.velocity.z)<.1f);
        lines.Add("DETAIL "+GroundSupport.Diagnostics(v));
    }
    static void ObstructedToe(EntityVehicle v){if(!Rules.Complete(v))return;Reset(v);for(int k=0;k<40;k++)Step(v,.02f,0);var rig=Model.GetRig(v);var support=GroundSupport.Get(v);var sole=support.Feet[1].Position-Origin.position;Gait.Solve(rig,1,sole,Vector3.up);var flat=rig.AnkleR.rotation;
        var block=Box("Only turned toe obstructed",sole+rig.Mount.right*(Justice.SoleWidth*.5f+.009f)+rig.Mount.forward*(Justice.SoleDepth*.5f-.035f)+Vector3.up*.10f,new Vector3(.008f,.18f,.03f));
        try{JusticeLegs.Pose(v,rig);Check("obstructed decorative toe returns to validated flat IK",Quaternion.Angle(flat,rig.AnkleR.rotation)<.1f&&Vector3.Distance(sole,rig.FootR.position)<.002f);}finally{UnityEngine.Object.DestroyImmediate(block);Physics.SyncTransforms();}
        JusticeLegs.Pose(v,rig);Check("toe articulation resumes after obstacle removed",Quaternion.Angle(flat,rig.AnkleR.rotation)>1);
    }
    static void Weapons(EntityVehicle v){if(!Rules.Complete(v))return;foreach(float dt in new[]{1f/30,1f/60,1f/120}){
 Reset(v);var r=Model.GetRig(v);var c=Samurai.Get(v);float aimError=0,gripError=0,handSpeed=0;int blocked=0;Vector3 last=Vector3.zero;bool ready=false;
 for(int k=0;k<Mathf.CeilToInt(8/dt);k++){float t=k*dt;c.LaserCharge=t>=2&&t<4?Mathf.Clamp01((t-2)/.3f):0;c.BeamSpent=false;c.Guarding=t>=5&&t<6;c.AimYaw=12;c.AimPitch=-8;Step(v,dt,2);
  var relative=r.Mount.InverseTransformPoint(r.HandR.position);if(ready&&t>1)handSpeed=Mathf.Max(handSpeed,Vector3.Distance(last,relative)/dt);last=relative;ready=true;
  if(c.RifleBlend>0||c.LaserCharge>0){var gun=r.Justice.GunMuzzle.parent;var grip=r.Justice.Document.gunGrip;var muzzle=r.Justice.GunMuzzle.position;var contact=gun.TransformPoint(new Vector3(grip[0],grip[1],grip[2]));gripError=Mathf.Max(gripError,Vector3.Distance(contact,r.HandR.position));var aim=r.Mount.rotation*Quaternion.Euler(-c.AimPitch,c.AimYaw,0)*Vector3.forward;aimError=Mathf.Max(aimError,Vector3.Angle(muzzle-contact,aim));}
  else if(t>1&&!Justice.BladeClear(r))blocked++;
 }
 Check("walk draw/holster/guard dt="+dt+" aim="+aimError+" grip="+gripError,aimError<.1f&&gripError<.002f);Check("weapon transition continuity dt="+dt+" hand speed="+handSpeed,handSpeed<12);Check("walk sword/guard clearance dt="+dt+" blocked="+blocked,blocked==0);c.LaserCharge=c.RifleBlend=c.GuardBlend=0;c.Guarding=false;c.AimYaw=c.AimPitch=0;
 }}
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
            try{Weapons(v);}finally{UnityEngine.Object.DestroyImmediate(floor);v.vehicleRB.gameObject.SetActive(false);v.transform.gameObject.SetActive(false);world.RemoveEntity(v.entityId,EnumRemoveEntityReason.Despawned);GroundSupport.Forget(v);}
        }}catch(Exception e){Check("exception "+e,false);}finally{Physics.autoSimulation=auto;}
        var path=Path.Combine(GameIO.GetSaveGameDir(),"justice-carry-transition-qa.txt");File.WriteAllLines(path,lines);File.WriteAllLines(Path.Combine(GameIO.GetSaveGameDir(),"justice-carry-curves.csv"),curves);foreach(var line in lines)Log.Out("[JusticeCarryTransitionQA] "+line);Log.Out("[MechaMotionQA] COMPLETE failures="+failures+" report="+path);
    }
}
