using UnityEngine;
namespace PZAEC.Mecha
{
    public static class SwordMotion
    {
        static readonly RaycastHit[] environmentHits=new RaycastHit[48];
        static readonly Collider[] environmentOverlaps=new Collider[48];
        public static Vector3 Root(Model.Rig r){return r.Sword!=null?r.Sword.TransformPoint(r.SwordRootAnchor):r.HandR.TransformPoint(r.SwordRootAnchor);}
        public static Vector3 Tip(Model.Rig r){return r.Sword!=null?r.Sword.TransformPoint(r.SwordTipAnchor):r.HandR.TransformPoint(r.SwordTipAnchor);}
        public const float WindEnd=.22f,CutEnd=.44f,BrakeEnd=.56f;
        public static bool DamagePhase(float phase){return phase>=WindEnd&&phase<=CutEnd+.035f;}
        public static string Stage(Samurai.State s,float now){if(s.Blocked)return "受阻收招";if(!s.Swing)return s.Charging?"重劈蓄力":s.Guarding?"正面举盾":"待机";float t=(now-s.Started)/Samurai.Duration(s);return t<WindEnd?"承重起手":t<=CutEnd?"发力挥砍":t<=BrakeEnd?"制动停剑":"回收";}
        static float Ease(float t){return Mathf.SmoothStep(0,1,Mathf.Clamp01(t));}
        struct Frame {public Vector3 Grip,Direction,Normal,Shift,Euler;public float PoleAngle;}
        static Frame Blend(Frame a,Frame b,float t)
        {t=Mathf.Clamp01(t);return new Frame{Grip=Vector3.Lerp(a.Grip,b.Grip,t),Direction=Vector3.Slerp(a.Direction,b.Direction,t).normalized,Normal=Vector3.Slerp(a.Normal,b.Normal,t).normalized,Shift=Vector3.Lerp(a.Shift,b.Shift,t),Euler=Vector3.Lerp(a.Euler,b.Euler,t),PoleAngle=Mathf.Lerp(a.PoleAngle,b.PoleAngle,t)};}
        static Frame FrameAt(Samurai.State s,Locomotion.MoveState m,float now)
        {
            var ready=new Frame{Grip=Vector3.Lerp(new Vector3(1.22f,2.12f,.60f),new Vector3(1.24f,2.22f,.61f),s.Alert),Direction=new Vector3(.08f,-.79f,.60f).normalized,Normal=Vector3.right};
            bool other=s.Combo!=1;float sign=other?-1:1;
            bool lifting=s.Heavy||s.Charging;
            var wind=ready;wind.Grip=lifting?new Vector3(1.00f,2.73f,.64f):other?new Vector3(.90f,2.47f,.79f):new Vector3(1.08f,2.38f,.62f);
            wind.Direction=(lifting?new Vector3(.18f,.94f,.29f):other?new Vector3(-.75f,.55f,.36f):new Vector3(.12f,.90f,.42f)).normalized;
            wind.Shift=new Vector3(sign*.055f,-.045f,-.045f);wind.Euler=new Vector3(lifting?-7:-3,-sign*18,sign*2);wind.PoleAngle=60;
            var cut=ready;cut.Grip=s.Heavy?new Vector3(1.28f,2.36f,.60f):other?new Vector3(1.28f,2.65f,.57f):new Vector3(.90f,2.10f,.91f);
            cut.Direction=(s.Heavy?new Vector3(.08f,-.79f,.60f):other?new Vector3(.80f,-.35f,.48f):new Vector3(-.70f,-.45f,.55f)).normalized;
            cut.Shift=new Vector3(-sign*.035f,s.Heavy?-.10f:-.07f,.08f);cut.Euler=new Vector3(s.Heavy?13:5,sign*(s.Heavy?10:22),-sign*2);cut.PoleAngle=s.Heavy||!other?0:60;
            var cuttingNormal=Vector3.Cross(wind.Direction,cut.Direction).normalized;if(cuttingNormal.x<0)cuttingNormal=-cuttingNormal;wind.Normal=cut.Normal=cuttingNormal;
            var brake=cut;brake.Grip=s.Heavy?new Vector3(1.30f,2.25f,.58f):other?new Vector3(1.30f,2.68f,.53f):new Vector3(.84f,1.98f,.84f);brake.Euler.y+=sign*3;brake.Shift.y-=.015f;
            var charged=ready;charged.Grip=new Vector3(1.00f,2.73f,.64f);charged.Direction=new Vector3(.18f,.94f,.29f).normalized;charged.Normal=Vector3.Cross(charged.Direction,new Vector3(.08f,-.79f,.60f).normalized).normalized;
            charged.Shift=new Vector3(-.055f,-.045f,-.045f);charged.Euler=new Vector3(-7,18,-2);charged.PoleAngle=60;
            var result=ready;
            if(s.Charging)result=Blend(ready,charged,Ease((now-s.PressedAt)/Samurai.HeavyCharge));
            if(s.Swing)
            {
                float t=Mathf.Clamp01((now-s.Started)/Samurai.Duration(s));
                if(t<WindEnd)result=Blend(s.StartCharge>0?Blend(ready,charged,Ease(s.StartCharge)):s.Heavy?wind:ready,wind,Ease(t/WindEnd));
                else if(t<CutEnd){float q=(t-WindEnd)/(CutEnd-WindEnd);result=Blend(wind,cut,q*q);}
                else if(t<BrakeEnd){float q=(t-CutEnd)/(BrakeEnd-CutEnd);result=Blend(cut,brake,1-(1-q)*(1-q));}
                else result=Blend(brake,ready,Ease((t-BrakeEnd)/(1-BrakeEnd)));
            }
            if(s.Blocked){float q=Ease((now-s.BlockedAt)/.38f);result.Grip=Vector3.Lerp(s.RecoveryGrip,ready.Grip,q);result.Direction=Vector3.Slerp(s.RecoveryDirection,ready.Direction,q).normalized;result.Normal=Vector3.Slerp(s.RecoveryNormal,ready.Normal,q).normalized;result.Shift=Vector3.Lerp(s.RecoveryShift,Vector3.zero,q);result.Euler=Vector3.Lerp(s.RecoveryEuler,Vector3.zero,q);result.PoleAngle=Mathf.Lerp(s.RecoveryPoleAngle,0,q);}
            float flight=Flight.AirPose(m)||m.WingBlend>.01f?m.WingBlend:m.Blend;
            if(flight>0){var carry=ready;carry.Grip=new Vector3(1.30f,2.10f,.55f);carry.Direction=new Vector3(.65f,-.40f,-.64f).normalized;result=Blend(result,carry,flight);result.Shift=result.Euler=Vector3.zero;}
            return result;
        }
        public static void Path(Samurai.State s,Locomotion.MoveState m,float now,out Vector3 grip,out Vector3 direction)
        {
            var frame=FrameAt(s,m,now);grip=frame.Grip;direction=frame.Direction;
        }
        public static float PoleAt(Samurai.State s,Locomotion.MoveState m,float now){return FrameAt(s,m,now).PoleAngle;}
        public static void Arm(Model.Rig r,Vector3 target,Vector3 poleDirection,float poleAngle=0)
        {
            r.ShoulderR.localRotation=r.RestRot[r.ShoulderR];r.ElbowR.localRotation=r.RestRot[r.ElbowR];r.HandR.localRotation=r.RestRot[r.HandR];
            var shoulder=r.ShoulderR;var elbow=r.ElbowR;var hand=r.HandR;float a=r.ArmUpper,b=r.ArmLower;var delta=target-shoulder.position;float d=Mathf.Clamp(delta.magnitude,Mathf.Abs(a-b)+.015f,a+b-.015f);var axis=delta.normalized;
            // The elbow has an authored outward plane, independent of wrist roll.
            var pole=Vector3.ProjectOnPlane(poleDirection,axis).normalized;if(pole.sqrMagnitude<.01f)pole=Vector3.ProjectOnPlane(-r.Mount.up,axis).normalized;
            pole=Quaternion.AngleAxis(poleAngle,axis)*pole;
            float along=(a*a-b*b+d*d)/(2*d);var bend=shoulder.position+axis*along+pole*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
            var q=Quaternion.FromToRotation(elbow.position-shoulder.position,bend-shoulder.position)*shoulder.rotation;shoulder.rotation=q;shoulder.localRotation=Quaternion.RotateTowards(r.RestRot[shoulder],shoulder.localRotation,r.ShoulderLimit);
            elbow.rotation=Quaternion.FromToRotation(hand.position-elbow.position,shoulder.position+axis*d-elbow.position)*elbow.rotation;elbow.localRotation=Quaternion.RotateTowards(r.RestRot[elbow],elbow.localRotation,r.ElbowLimit);
        }
        public static void Blade(Model.Rig r,Vector3 direction,Vector3 normal)
        {
            var axis=r.SwordTipAnchor.normalized;var restNormal=Vector3.ProjectOnPlane(r.SwordRestNormal,axis).normalized;
            var targetNormal=Vector3.ProjectOnPlane(normal,direction).normalized;if(targetNormal.sqrMagnitude<.01f)targetNormal=Vector3.ProjectOnPlane(r.Mount.up,direction).normalized;
            r.HandR.rotation=Quaternion.LookRotation(direction.normalized,targetNormal)*Quaternion.Inverse(Quaternion.LookRotation(axis,restNormal));
            r.HandR.localRotation=Quaternion.RotateTowards(r.RestRot[r.HandR],r.HandR.localRotation,r.WristLimit);
        }
        static float SegmentSegments(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
        {
            var u=b-a;var v=d-c;var w=a-c;float aa=Vector3.Dot(u,u),bb=Vector3.Dot(u,v),cc=Vector3.Dot(v,v),dd=Vector3.Dot(u,w),ee=Vector3.Dot(v,w),den=aa*cc-bb*bb;
            float s=den>.000001f?Mathf.Clamp01((bb*ee-cc*dd)/den):0;float t=cc>.000001f?Mathf.Clamp01((bb*s+ee)/cc):0;s=aa>.000001f?Mathf.Clamp01((bb*t-dd)/aa):0;t=cc>.000001f?Mathf.Clamp01((bb*s+ee)/cc):0;return Vector3.Distance(a+u*s,c+v*t);
        }
        static bool Near(Model.Rig r,Vector3 a,Vector3 b,Vector3 c,Vector3 d,float radius){return SegmentSegments(a,b,c,d)<radius+.065f;}
        public static bool SelfClear(Model.Rig r)
        {
            var a=Root(r);var b=Tip(r);var torso=r.Torso.position;
            if(Near(r,a,b,torso+Vector3.up*.05f,torso+Vector3.up*.55f,.37f)||Near(r,a,b,r.Head.position,r.Head.position+Vector3.up*.18f,.27f))return false;
            if(Near(r,a,b,r.HipL.position,r.KneeL.position,.19f)||Near(r,a,b,r.HipR.position,r.KneeR.position,.19f))return false;
            if(Near(r,a,b,r.ShoulderL.position,r.ElbowL.position,.18f))return false;
            for(int i=0;i<=8;i++){var p=r.HandL.InverseTransformPoint(Vector3.Lerp(a,b,i/8f));if(Mathf.Abs(p.x+.09f)<.19f&&p.y>-.92f&&p.y<1.17f&&p.z>-.63f&&p.z<.56f)return false;}
            if(r.WingL!=null&&Near(r,a,b,r.WingL.position,r.WingL.TransformPoint(new Vector3(0,.85f,-.12f)),.16f))return false;
            if(r.WingR!=null&&Near(r,a,b,r.WingR.position,r.WingR.TransformPoint(new Vector3(0,.85f,-.12f)),.16f))return false;
            // Full hilt/guard capsule is checked separately from the blade.
            var hilt=r.HandR.TransformPoint(r.HiltAnchor);if(Near(r,r.HandR.position,hilt,torso,torso+Vector3.up*.95f,.53f))return false;
            var counterweight=Vector3.Lerp(r.HandR.position,hilt,.4f);
            if(Near(r,counterweight,hilt,r.ShoulderR.position,r.ElbowR.position,.18f)||Near(r,counterweight,hilt,r.ElbowR.position,Vector3.Lerp(r.ElbowR.position,r.HandR.position,.6f),.16f))return false;
            if(Near(r,counterweight,hilt,r.ShoulderR.TransformPoint(new Vector3(.10f,.12f,.08f)),r.ShoulderR.TransformPoint(new Vector3(.18f,.30f,.12f)),.16f))return false;
            return true;
        }
        static void GroundBlade(EntityVehicle v,Model.Rig r,Vector3 direction)
        {
            var tip=Tip(r)+Origin.position;if(Gait.Ground(v,tip,out var floor)&&tip.y<floor.y+.12f){float len=r.SwordTipAnchor.magnitude,minY=Mathf.Clamp((floor.y+.12f-Origin.position.y-r.HandR.position.y)/len,-.9f,.9f);var flat=Vector3.ProjectOnPlane(direction,Vector3.up).normalized;Blade(r,flat*Mathf.Sqrt(1-minY*minY)+Vector3.up*minY,r.HandR.TransformDirection(r.SwordRestNormal));}
        }
        public static void CaptureBase(Model.Rig r)
        {
            r.ActionBaseTorsoPosition=r.Torso.localPosition;r.ActionBaseTorsoRotation=r.Torso.localRotation;
            r.ActionSoleL=r.Mount.InverseTransformPoint(r.FootL.position);r.ActionSoleR=r.Mount.InverseTransformPoint(r.FootR.position);
            r.ActionNormalL=r.Mount.InverseTransformDirection(r.FootL.up);r.ActionNormalR=r.Mount.InverseTransformDirection(r.FootR.up);r.ActionBaseReady=true;
        }
        public static void CacheFeet(Model.Rig r,Vector3 soleL,Vector3 soleR,Vector3 normalL,Vector3 normalR)
        {r.ActionSoleL=r.Mount.InverseTransformPoint(soleL);r.ActionSoleR=r.Mount.InverseTransformPoint(soleR);r.ActionNormalL=r.Mount.InverseTransformDirection(normalL);r.ActionNormalR=r.Mount.InverseTransformDirection(normalR);}
        static void ResetJoint(Model.Rig r,Transform joint){joint.localRotation=r.RestRot[joint];}
        static void Body(Model.Rig r,Frame frame)
        {
            r.Torso.localPosition=r.ActionBaseTorsoPosition+frame.Shift;r.Torso.localRotation=r.ActionBaseTorsoRotation*Quaternion.Euler(frame.Euler);
            ResetJoint(r,r.HipL);ResetJoint(r,r.HipR);ResetJoint(r,r.KneeL);ResetJoint(r,r.KneeR);ResetJoint(r,r.AnkleL);ResetJoint(r,r.AnkleR);
            Gait.Solve(r,0,r.Mount.TransformPoint(r.ActionSoleL),r.Mount.TransformDirection(r.ActionNormalL));Gait.Solve(r,1,r.Mount.TransformPoint(r.ActionSoleR),r.Mount.TransformDirection(r.ActionNormalR));
        }
        public static void Pose(EntityVehicle v,Model.Rig r,Samurai.State s,float now)
        {
            if(!r.ActionBaseReady)CaptureBase(r);var motion=Locomotion.Get(v);var frame=FrameAt(s,motion,now);Vector3 grip,direction,normal;
            bool ceremony=Boarding.SwordTarget(v,out grip,out direction,out normal);
            if(!ceremony){grip=frame.Grip;direction=frame.Direction;normal=frame.Normal;if(!Flight.AirPose(motion)&&motion.WingBlend<.01f&&motion.Blend<.01f)Body(r,frame);else {r.ActionBaseTorsoPosition=r.Torso.localPosition;r.ActionBaseTorsoRotation=r.Torso.localRotation;}}
            // The same deterministic torso, foot, arm and sword path is used by
            // presentation and the server's 60 Hz contact samples. Correction
            // stays on this path; boarding is never replaced by an idle path.
            for(int attempt=0;attempt<9;attempt++){
              for(int bend=0;bend<7;bend++){
                // Swivel follows the continuous authored action. Local safety
                // corrections stay within fifteen degrees and never pick an
                // unrelated elbow plane merely to minimize a wrist score.
                float angle=(ceremony?0:frame.PoleAngle)+(bend==0?0:((bend+1)/2)*5*(bend%2==1?1:-1));
                var shifted=grip+new Vector3((attempt%3)*.06f,attempt<3?0:attempt<6?.12f:-.12f,(attempt%3)*.06f);Arm(r,r.Torso.TransformPoint(shifted-r.TorsoBasePosition),r.Torso.TransformDirection(direction*2+Vector3.right*.35f),angle);Blade(r,r.Torso.TransformDirection(direction),r.Torso.TransformDirection(normal));GroundBlade(v,r,r.Torso.TransformDirection(direction));if(SelfClear(r))return;
              }
            }
            // Keep the final nearest authored correction instead of jumping
            // to an unrelated straight-forward blade when armour is tight.
        }
        public static void SafePose(EntityVehicle v,Model.Rig r){Pose(v,r,Samurai.Get(v),Time.time);}
        public static bool Environment(EntityVehicle v,Vector3 a,Vector3 b,out Vector3 point)
        {
            point=b;var d=b-a;if(d.sqrMagnitude<.0001f)return false;
            // Voxel trace supplies block contacts; capsule queries include blade width
            // and starting overlaps with native terrain / environment colliders.
            if(Weapons.Trace(v,a+Origin.position,d.normalized,d.magnitude,out var hit)){var entity=ItemActionAttack.FindHitEntity(hit);if(!(entity is EntityAlive&&Weapons.Hostile(entity as EntityAlive))){point=hit.hit.pos-Origin.position;return true;}}
            int count=Physics.OverlapCapsuleNonAlloc(a,b,.065f,environmentOverlaps,-538750997,QueryTriggerInteraction.Ignore);
            for(int i=0;i<count;i++)if(EnvironmentCollider(v,environmentOverlaps[i])){point=environmentOverlaps[i].ClosestPoint((a+b)*.5f);return true;}
            count=Physics.SphereCastNonAlloc(a,.065f,d.normalized,environmentHits,d.magnitude,-538750997,QueryTriggerInteraction.Ignore);
            float nearest=float.MaxValue;bool blocked=false;for(int i=0;i<count;i++)if(environmentHits[i].distance<nearest&&EnvironmentCollider(v,environmentHits[i].collider)){nearest=environmentHits[i].distance;point=environmentHits[i].point;blocked=true;}return blocked;
        }
        static bool EnvironmentCollider(EntityVehicle v,Collider c){if(c==null)return false;var t=c.transform;var crew=v.GetAttached(0);var entity=GameUtils.GetHitRootEntity(c.tag,t);if(entity==v||(crew!=null&&entity==crew)||t.IsChildOf(v.transform)||(v.vehicleRB!=null&&t.IsChildOf(v.vehicleRB.transform))||(crew!=null&&t.IsChildOf(crew.transform)))return false;var alive=entity as EntityAlive??c.GetComponentInParent<EntityAlive>();if(c.tag.StartsWith("E_BP_")&&alive==null)return false;return alive==null||!Weapons.Hostile(alive);}
    }
}
