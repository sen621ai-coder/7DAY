using UnityEngine;
namespace PZAEC.Mecha {
 // Presentation only: no forces, clocks, damage windows or network fields change.
 public static class JusticeMotion {
  static float Pulse(float t){float s=Mathf.Sin(Mathf.Clamp01(t)*Mathf.PI);return s*s;}
  public static void Body(EntityVehicle v,Model.Rig r,float dt){
   var j=r.Justice;var m=Locomotion.Get(v);var c=Samurai.Get(v);
   float forward=m.VisualForward;
   float acceleration=j.MotionReady?Mathf.Clamp((forward-j.PreviousForward)/Mathf.Max(.001f,dt),-16,16):0;
   j.MotionReady=true;j.PreviousForward=forward;
   float a=1-Mathf.Exp(-14*dt);
   j.BodyLean=Mathf.Lerp(j.BodyLean,Mathf.Clamp(forward*.9f+acceleration*.38f,-8,10),a);
   j.BodyTurn=Mathf.Lerp(j.BodyTurn,Mathf.Clamp(m.VisualTurn*.06f,-5,5),a);
   if(!j.Ultimate&&Traversal.Active(v)){r.Torso.localPosition=r.TorsoBasePosition;r.Torso.localRotation=r.RestRot[r.Torso];return;}
   // Keep the existing attack reference throughout the damage window, including
   // its 0.035 phase tail. Added compression has zero velocity at its boundaries.
   if(c.Swing||c.Charging){
    float t=Mathf.Clamp01(SwordMotion.Phase(c,Time.time));
    float weight=c.Swing?(t<SwordMotion.WindEnd?Mathf.Lerp(Pulse(t/SwordMotion.WindEnd),1-Mathf.SmoothStep(0,1,t/SwordMotion.WindEnd),c.Chained?0:c.StartCharge):t>SwordMotion.CutEnd+.035f?Pulse((t-SwordMotion.CutEnd-.035f)/(1-SwordMotion.CutEnd-.035f)):0):Mathf.SmoothStep(0,1,Mathf.Clamp01((Time.time-c.PressedAt)/Samurai.HeavyCharge));
    float side=c.Combo==1?-1:1;
    float original=c.Swing?(t<SwordMotion.WindEnd?Mathf.Sin(t/SwordMotion.WindEnd*Mathf.PI):t>SwordMotion.CutEnd?Mathf.Sin((t-SwordMotion.CutEnd)/(1-SwordMotion.CutEnd)*Mathf.PI):0):0;
    r.Torso.localRotation*=Quaternion.Euler(-1.5f*original,0,side*1.2f*original);
    r.ShoulderL.localRotation*=Quaternion.Euler(-3*original,0,-2*original);
    bool recovering=c.Swing&&t>SwordMotion.CutEnd+.035f;
    r.Torso.localPosition+=new Vector3(side*(recovering?-.035f:.035f),-.065f,recovering?.035f:-.025f)*weight;
    r.Torso.localRotation*=Quaternion.Euler((recovering?7:-5)*weight,side*(recovering?7:-7)*weight,side*2*weight);
    return;
   }
   bool ground=m.Grounded&&!Flight.AirPose(m)&&!Skim.Active(v)&&!Traversal.Active(v);
   var support=GroundSupport.Find(v);float stride=0,bearing=0,lift=0;
   if(ground&&!v.isEntityRemote&&support!=null&&support.Initialized){
    // Project onto the body heading: reverse and pivot steps remain in phase.
    stride=Mathf.Clamp(Vector3.Dot(support.Feet[0].Position-support.Feet[1].Position,r.Mount.forward)/.8f,-1,1);
    for(int side=0;side<2;side++)if(support.Feet[side].Swing){
     float t=Mathf.Clamp01(support.Feet[side].Age/Mathf.Max(.01f,support.Feet[side].Duration));
     lift=Pulse(t);bearing=side==0?1:-1;
    }
   }else if(ground&&v.isEntityRemote&&GroundNet.Fresh(v)){stride=-Mathf.Cos(j.WalkPhase*Mathf.PI*2);bearing=Mathf.Sin(j.WalkPhase*Mathf.PI*2);lift=Mathf.Abs(bearing);}
   float walk=ground?j.WalkBlend:0;
   float gaitBlend=ground&&!j.Ultimate?1:a;
   j.BodyStride=Mathf.Lerp(j.BodyStride,stride*walk,gaitBlend);
   j.BodySupport=Mathf.Lerp(j.BodySupport,bearing*lift*walk,gaitBlend);
   j.BodyLift=Mathf.Lerp(j.BodyLift,lift*walk,gaitBlend);
   if(!ground)return;
   // Lowering the pelvis bends both knees against the final planted-foot IK.
   // Transfer toward the planted side; chest and shield oppose pelvis rotation.
   float landing=Mathf.Sin(Mathf.Clamp01(1-(Time.time-m.LandingAt)/.5f)*Mathf.PI)*.12f;
   r.Torso.localPosition=r.TorsoBasePosition+new Vector3(j.BodySupport*.055f,
    -.14f*walk+.065f*j.BodyLift-m.Charge*.22f-landing,0);
   r.Torso.localRotation=r.RestRot[r.Torso]*Quaternion.Euler(j.BodyLean,j.BodyStride*4,-j.BodySupport*2-j.BodyTurn);
   for(int bone=0;bone<j.Bones.Length;bone++)if(j.Document.bones[bone].sourceNode==12)j.Bones[bone].localRotation*=Quaternion.Euler(-j.BodyLean*.35f,-j.BodyStride*7,j.BodySupport*1.5f+j.BodyTurn*.5f);
  }
  // Run once after SafePose, which resamples the held weapon. Applying this
  // before Combat silently loses the right arm and head a second time per frame.
  public static void Finish(EntityVehicle v,Model.Rig r,float dt=.02f){
   var j=r.Justice;var c=Samurai.Get(v);var m=Locomotion.Get(v);
   if(!j.Ultimate&&JusticeCarry.Apply(v,r,dt))return;
   if(c.Swing||c.Charging||Traversal.Active(v))return;
   float airborne=Mathf.Max(j.PackBlend,Flight.AirPose(m)?1:0);
   float stride=j.BodyStride*(1-airborne);
   float brace=Mathf.Abs(j.BodyLean)*.65f;
   float left=1-c.GuardBlend,right=1-Mathf.Max(c.RifleBlend,Mathf.Clamp01(c.LaserCharge*3));
   if(c.BeamSpent)right=0;
   r.ShoulderL.localRotation*=Quaternion.Euler((stride*14+brace+airborne*8)*left,0,-airborne*4*left);
   r.ShoulderR.localRotation*=Quaternion.Euler((-stride*15+brace+airborne*8)*right,0,airborne*4*right);
   r.ElbowL.localRotation*=Quaternion.Euler((-Mathf.Abs(stride)*6-brace-airborne*10)*left,0,0);
   r.ElbowR.localRotation*=Quaternion.Euler((-Mathf.Abs(stride)*4-brace-airborne*8)*right,0,0);
   r.Head.localRotation*=Quaternion.Euler(-j.BodyLean*.45f*(1-airborne)-j.AirLean*.35f*airborne,stride*3,j.BodyTurn*.5f*(1-airborne));
   if(right>0)Justice.ClearBlade(r);
  }
 }
}
