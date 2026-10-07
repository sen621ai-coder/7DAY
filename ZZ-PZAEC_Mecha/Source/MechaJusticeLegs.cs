using UnityEngine;
namespace PZAEC.Mecha {
 // Ground anchors remain authoritative. Decorative articulation may only use
 // reachable, collision-free poses and never moves an established support pad.
 public static class JusticeLegs {
  public const float HalfStance=.25f,ToeOut=2;
  public static Vector3 Home(Vector3 p,int side){p.x=(side==0?-1:1)*Mathf.Max(HalfStance,Mathf.Abs(p.x));return p;}
  static bool LimbClear(EntityVehicle v,Vector3 a,Vector3 b){var d=b-a;return GroundSupport.BoxClear(v,(a+b)*.5f,(a+b)*.5f,new Vector3(.08f,.08f,d.magnitude*.5f),Quaternion.FromToRotation(Vector3.forward,d.normalized));}
  public static void Pose(EntityVehicle v,Model.Rig r){
   var m=Locomotion.Get(v);var g=GroundSupport.Find(v);
   if(!m.Grounded||Skim.Active(v)||Flight.AirPose(m)||Traversal.Active(v)||g==null||!g.Initialized||g.Recovering)return;
   if(v.isEntityRemote&&!GroundNet.Fresh(v))return;
   for(int side=0;side<2;side++){
    var f=g.Feet[side];var other=g.Feet[1-side];var hip=side==0?r.HipL:r.HipR;var knee=side==0?r.KneeL:r.KneeR;var ankle=side==0?r.AnkleL:r.AnkleR;var foot=side==0?r.FootL:r.FootR;
    // Start from the final ground/network IK result, including remote interpolation.
    var sole=foot.position;var hq=hip.localRotation;var kq=knee.localRotation;var aq=ankle.localRotation;
    float phase=v.isEntityRemote?Mathf.Repeat(r.Justice.WalkPhase-side*.5f,1)*2:Mathf.Clamp01(f.Age/Mathf.Max(.01f,f.Duration));
    float swing=f.Swing?Mathf.Sin(Mathf.Clamp01(phase)*Mathf.PI):0;
    float sign=side==0?-1:1;
    float yaw=sign*(ToeOut+2*swing)+Mathf.Clamp(m.VisualTurn*.025f,-2,2)*swing;
    float pitch=f.Swing?12*Mathf.Sin(Mathf.Clamp01(phase)*Mathf.PI*2):0;
    var normal=f.Normal;var flat=Quaternion.FromToRotation(r.Mount.up,normal)*r.Mount.rotation;
    // Late support unloads at the toe before the opposite foot accepts weight.
    // Limit the sole-centre rise to 1.6 cm; the toe remains the fixed contact.
    if(!f.Swing&&other.Swing&&!v.isEntityRemote){float t=Mathf.Clamp01(other.Age/Mathf.Max(.01f,other.Duration));pitch=2*Mathf.Sin(Mathf.InverseLerp(.65f,1,t)*Mathf.PI);}
    var rotation=flat*Quaternion.Euler(pitch,yaw,0);var target=sole;
    // Do not enlarge every planned step to fit a cosmetic toe angle: a narrow
    // ledge can keep the original flat foot. Validate the actual turned pad.
    GroundSupport.Pad turned;
    if(!f.Swing&&!GroundSupport.PadAt(v,f.Position,r.Mount.rotation*Quaternion.Euler(0,yaw,0),.03f,.03f,out turned))continue;
    if(!f.Swing&&pitch>0){var pivot=Quaternion.Euler(0,yaw,0)*new Vector3(0,0,Justice.SoleDepth*.5f);target+=flat*pivot-rotation*new Vector3(0,0,Justice.SoleDepth*.5f);}
    // Keep knees tracking forward; only a small swing clearance is added.
    Gait.Solve(r,side,target,normal,-sign*(2*swing),yaw,pitch);
    // Actual posed bones, rather than the unbent reference skeleton, determine
    // clearance. Unsafe articulation falls back to the already-validated IK.
    bool clear=Vector3.Distance(foot.position,target)<.005f&&
     GroundSupport.BoxClear(v,foot.position+rotation*Vector3.up*.15f,foot.position+rotation*Vector3.up*.15f,new Vector3(Justice.SoleWidth*.5f,.125f,Justice.SoleDepth*.5f),rotation)&&
     LimbClear(v,hip.position,knee.position)&&LimbClear(v,knee.position,ankle.position);
    if(!clear){hip.localRotation=hq;knee.localRotation=kq;ankle.localRotation=aq;}
   }
  }
 }
}
