using UnityEngine;
namespace PZAEC.Mecha {
 // Local presentation state only. The authoritative attack and aiming poses are
 // sampled first; carried weapons are solved as a hand target plus a direction.
 public static class JusticeCarry {
  public sealed class State {
   public Vector3 Grip;public float Clearance,Phase;public bool Ready,Action,Swing;
   public Quaternion Shoulder,Elbow,Hand;
   public Quaternion FromShoulder,FromElbow,FromHand;
  }
  static void Save(State s,Model.Rig r){s.Grip=r.Mount.InverseTransformPoint(r.HandR.position);s.Shoulder=r.ShoulderR.localRotation;s.Elbow=r.ElbowR.localRotation;s.Hand=r.HandR.localRotation;s.Ready=true;}
  static void LeftArm(Model.Rig r,Vector3 target){
   var shoulder=r.ShoulderL;var elbow=r.ElbowL;var hand=r.HandL;
   shoulder.localRotation=r.RestRot[shoulder];elbow.localRotation=r.RestRot[elbow];hand.localRotation=r.RestRot[hand];
   float a=Vector3.Distance(shoulder.position,elbow.position),b=Vector3.Distance(elbow.position,hand.position);
   var delta=target-shoulder.position;float d=Mathf.Clamp(delta.magnitude,Mathf.Abs(a-b)+.015f,a+b-.015f);var axis=delta.normalized;
   var pole=Vector3.ProjectOnPlane(r.Mount.forward-r.Mount.up*.3f,axis).normalized;
   float along=(a*a-b*b+d*d)/(2*d);var bend=shoulder.position+axis*along+pole*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
   shoulder.rotation=Quaternion.FromToRotation(elbow.position-shoulder.position,bend-shoulder.position)*shoulder.rotation;
   elbow.rotation=Quaternion.FromToRotation(hand.position-elbow.position,shoulder.position+axis*d-elbow.position)*elbow.rotation;
  }
  static void RightArm(Model.Rig r,float stride,float clearance,float dt){
   // Left foot forward -> right hand forward. The blade stays outside the right
   // leg, pitched forward/down instead of sweeping across the chest each step.
   var target=r.ShoulderR.position+r.Mount.TransformDirection(new Vector3(.10f+clearance,-.48f,.22f+stride*.11f));
   if(r.Justice.Carry.Ready)target=r.Mount.TransformPoint(Vector3.MoveTowards(r.Justice.Carry.Grip,r.Mount.InverseTransformPoint(target),dt*4));
   SwordMotion.Arm(r,target,r.Mount.right-r.Mount.up*.25f);
   var current=(SwordMotion.Tip(r)-SwordMotion.Root(r)).normalized;
   var direction=r.Mount.TransformDirection(new Vector3(.32f,-.22f+stride*.025f,1)).normalized;
   r.HandR.rotation=Quaternion.FromToRotation(current,direction)*r.HandR.rotation;
  }
  public static bool Apply(EntityVehicle v,Model.Rig r,float dt){
   var j=r.Justice;var s=j.Carry;var c=Samurai.Get(v);var m=Locomotion.Get(v);
   bool action=c.Swing||c.Charging;
   if(action){
    float phase=c.Swing?SwordMotion.Phase(c,Time.time):0;
    if((!s.Action||s.Swing!=c.Swing||phase<s.Phase-.001f)&&s.Ready){s.FromShoulder=s.Shoulder;s.FromElbow=s.Elbow;s.FromHand=s.Hand;}
    s.Action=true;s.Swing=c.Swing;s.Phase=phase;
    // Reach the unchanged authored attack before its first damage sample.
    float t=c.Swing?SwordMotion.Phase(c,Time.time)/SwordMotion.WindEnd:(Time.time-c.PressedAt)/.16f;
    if(s.Ready&&t<1){float a=Mathf.SmoothStep(0,1,Mathf.Clamp01(t));var q=r.ShoulderR.localRotation;var e=r.ElbowR.localRotation;var h=r.HandR.localRotation;
     r.ShoulderR.localRotation=Quaternion.Slerp(s.FromShoulder,q,a);r.ElbowR.localRotation=Quaternion.Slerp(s.FromElbow,e,a);r.HandR.localRotation=Quaternion.Slerp(s.FromHand,h,a);
     if(!Justice.BladeClear(r)){r.ShoulderR.localRotation=q;r.ElbowR.localRotation=e;r.HandR.localRotation=h;}
    }
    Save(s,r);return true;
   }
   s.Action=false;
   bool ground=m.Grounded&&!Flight.AirPose(m)&&!Skim.Active(v)&&!Traversal.Active(v);
   if(!ground){s.Ready=false;return false;}
   float rifle=Mathf.Max(c.RifleBlend,Mathf.Clamp01(c.LaserCharge*3));if(c.BeamSpent)rifle=1;
   var q0=r.ShoulderR.localRotation;var e0=r.ElbowR.localRotation;var h0=r.HandR.localRotation;
   var lq=r.ShoulderL.localRotation;var le=r.ElbowL.localRotation;var lh=r.HandL.localRotation;
   if(rifle<=0){
    RightArm(r,j.BodyStride,s.Clearance,dt);
    // Search grip space rather than snapping the shoulder in five-degree steps.
    if(!Justice.BladeClear(r)){for(int i=1;i<=6;i++){RightArm(r,j.BodyStride,i*.04f,dt);if(Justice.BladeClear(r)){s.Clearance=i*.04f;break;}}}
    if(!Justice.BladeClear(r)){r.ShoulderR.localRotation=q0;r.ElbowR.localRotation=e0;r.HandR.localRotation=h0;}
   }else{
    // During draw/holster, blend the grip trajectory; the muzzle keeps the
    // authoritative aim, and the gun is attached again after solving the arm.
    var weapon=j.GunMuzzle.parent;var rotation=weapon.rotation;var wrist=r.HandR.rotation;
    var carry=r.ShoulderR.position+r.Mount.TransformDirection(new Vector3(.10f+s.Clearance,-.48f,.22f+j.BodyStride*.11f));
    var target=Vector3.Lerp(carry,r.HandR.position,Mathf.SmoothStep(0,1,rifle));
    if(r.Justice.Carry.Ready)target=r.Mount.TransformPoint(Vector3.MoveTowards(r.Justice.Carry.Grip,r.Mount.InverseTransformPoint(target),dt*4));
   SwordMotion.Arm(r,target,r.Mount.right-r.Mount.up*.25f);r.HandR.rotation=wrist;
    var grip=j.Document.gunGrip;weapon.rotation=rotation;weapon.position=r.HandR.position-rotation*new Vector3(grip[0],grip[1],grip[2]);
   }
   // The shield arm has a smaller counter-swing and yields to the guard pose.
   LeftArm(r,r.ShoulderL.position+r.Mount.TransformDirection(new Vector3(-.10f,-.62f,.12f-j.BodyStride*.055f)));
   float guard=c.GuardBlend;r.ShoulderL.localRotation=Quaternion.Slerp(r.ShoulderL.localRotation,lq,guard);r.ElbowL.localRotation=Quaternion.Slerp(r.ElbowL.localRotation,le,guard);r.HandL.localRotation=Quaternion.Slerp(r.HandL.localRotation,lh,guard);
   r.Head.localRotation*=Quaternion.Euler(-j.BodyLean*.35f,0,j.BodyTurn*.35f);
   Save(s,r);return true;
  }
 }
}
