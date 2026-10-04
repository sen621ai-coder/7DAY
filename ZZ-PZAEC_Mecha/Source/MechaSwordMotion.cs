using UnityEngine;
namespace PZAEC.Mecha
{
    public static class SwordMotion
    {
        static readonly RaycastHit[] environmentHits=new RaycastHit[48];
        static readonly Collider[] environmentOverlaps=new Collider[48];
        public static Vector3 Root(Model.Rig r){return r.Sword!=null?r.Sword.TransformPoint(r.SwordRootAnchor):r.HandR.TransformPoint(r.SwordRootAnchor);}
        public static Vector3 Tip(Model.Rig r){return r.Sword!=null?r.Sword.TransformPoint(r.SwordTipAnchor):r.HandR.TransformPoint(r.SwordTipAnchor);}
        public static string Stage(Samurai.State s,float now){if(s.Blocked)return "受阻收招";if(!s.Swing)return s.Charging?"重劈蓄力":s.Guarding?"正面举盾":"待机";float t=(now-s.Started)/Samurai.Duration(s);return t<.18f?"起手":t<=.70f?"有效挥砍":"收招";}
        static float Ease(float t){return Mathf.SmoothStep(0,1,Mathf.Clamp01(t));}
        public static void Path(Samurai.State s,Locomotion.MoveState m,float now,out Vector3 grip,out Vector3 direction)
        {
            grip=Vector3.Lerp(new Vector3(1.22f,2.05f,.56f),new Vector3(1.30f,2.20f,.58f),s.Alert);direction=new Vector3(.12f,-.40f,.70f);
            var ready=grip;var idle=direction;
            if(s.Charging){float q=Ease((now-s.PressedAt)/Samurai.HeavyCharge);grip=Vector3.Lerp(grip,new Vector3(1.30f,2.28f,.56f),q);direction=Vector3.Slerp(direction,new Vector3(.22f,.93f,.28f),q);}
            if(s.Swing){float t=Mathf.Clamp01((now-s.Started)/Samurai.Duration(s)),wind=s.Heavy?1:Ease(t/.18f),cut=Ease((t-.18f)/.50f),recover=Ease((t-.70f)/.30f);
                var a=s.Heavy?new Vector3(1.30f,2.28f,.56f):new Vector3(1.30f,2.22f,.56f);var b=new Vector3(1.30f,2.18f,.56f);
                var da=s.Heavy||s.Combo==1?new Vector3(.22f,.93f,.28f):new Vector3(-.82f,.12f,.78f);
                var db=s.Heavy?new Vector3(.35f,-.65f,.72f):s.Combo==1?new Vector3(-.82f,-.15f,.78f):new Vector3(.83f,.45f,.72f);
                grip=Vector3.Lerp(Vector3.Lerp(ready,Vector3.Lerp(a,b,cut),wind),ready,recover);direction=Vector3.Slerp(Vector3.Slerp(idle,Vector3.Slerp(da,db,cut),wind),idle,recover);
            }
            if(s.Blocked){float recover=Ease((now-s.BlockedAt)/.22f);grip=Vector3.Lerp(s.RecoveryGrip,ready,recover);direction=Vector3.Slerp(s.RecoveryDirection,idle,recover);}
            float flight=Flight.AirPose(m)||m.WingBlend>.01f?m.WingBlend:m.Blend;
            grip=Vector3.Lerp(grip,new Vector3(1.26f,2.16f,.56f),flight);
            if(flight>0){float azimuth=Mathf.Lerp(Mathf.Atan2(direction.x,direction.z),Mathf.Atan2(.08f,.38f),flight),y=Mathf.Lerp(direction.normalized.y,-.92f,flight),horizontal=Mathf.Sqrt(Mathf.Max(0,1-y*y));direction=new Vector3(Mathf.Sin(azimuth)*horizontal,y,Mathf.Cos(azimuth)*horizontal);}
            direction.Normalize();
            // When the blade travels outwards, its long counterweight points
            // back into the chest. Move the grip out and up before that phase.
            float outwards=Mathf.Max(0,direction.x);grip.x+=outwards*.05f;grip.y+=outwards*.04f;
            // Lowering the long blade raises its counterweight. Keep that
            // counterweight in front of the raised shoulder armour.
            grip.z+=Mathf.Max(0,-direction.y)*.05f*(1-flight);
            // The heavy cut pivots around an elevated, outboard grip. Dropping
            // the grip together with the blade puts the counterweight into the
            // shoulder shell and produces a discontinuous emergency correction.
            var heavyGrip=new Vector3(1.30f,2.28f,.56f);
            if(s.Charging)grip=Vector3.Lerp(grip,heavyGrip,Ease((now-s.PressedAt)/Samurai.HeavyCharge)*(1-flight));
            if(s.Swing&&s.Heavy)grip=Vector3.Lerp(heavyGrip,grip,Ease(((now-s.Started)/Samurai.Duration(s)-.70f)/.30f));
        }
        static void Arm(Model.Rig r,Vector3 target,Vector3 bladeDirection,float poleAngle=0)
        {
            r.ShoulderR.localRotation=r.RestRot[r.ShoulderR];r.ElbowR.localRotation=r.RestRot[r.ElbowR];r.HandR.localRotation=r.RestRot[r.HandR];
            var shoulder=r.ShoulderR;var elbow=r.ElbowR;var hand=r.HandR;float a=r.ArmUpper,b=r.ArmLower;var delta=target-shoulder.position;float d=Mathf.Clamp(delta.magnitude,Mathf.Abs(a-b)+.015f,a+b-.015f);var axis=delta.normalized;
            // Bend toward the blade, including its vertical component. A
            // horizontal-only pole puts the forearm inside the counterweight
            // while raising / lowering the sword.
            var pole=Vector3.ProjectOnPlane(bladeDirection*2+r.Mount.right*.35f,axis).normalized;if(pole.sqrMagnitude<.01f)pole=Vector3.ProjectOnPlane(-r.Mount.up,axis).normalized;
            pole=Quaternion.AngleAxis(poleAngle,axis)*pole;
            float along=(a*a-b*b+d*d)/(2*d);var bend=shoulder.position+axis*along+pole*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
            var q=Quaternion.FromToRotation(elbow.position-shoulder.position,bend-shoulder.position)*shoulder.rotation;shoulder.rotation=q;shoulder.localRotation=Quaternion.RotateTowards(r.RestRot[shoulder],shoulder.localRotation,r.ShoulderLimit);
            elbow.rotation=Quaternion.FromToRotation(hand.position-elbow.position,shoulder.position+axis*d-elbow.position)*elbow.rotation;elbow.localRotation=Quaternion.RotateTowards(r.RestRot[elbow],elbow.localRotation,r.ElbowLimit);
        }
        static void Blade(Model.Rig r,Vector3 direction){r.HandR.rotation=Quaternion.FromToRotation(r.HandR.TransformDirection(r.SwordTipAnchor),direction.normalized)*r.HandR.rotation;r.HandR.localRotation=Quaternion.RotateTowards(r.RestRot[r.HandR],r.HandR.localRotation,r.WristLimit);}
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
            var tip=Tip(r)+Origin.position;if(Gait.Ground(v,tip,out var floor)&&tip.y<floor.y+.12f){float len=Vector3.Distance(Samurai.Grip,Samurai.BladeTip),minY=Mathf.Clamp((floor.y+.12f-Origin.position.y-r.HandR.position.y)/len,-.9f,.9f);var flat=Vector3.ProjectOnPlane(direction,Vector3.up).normalized;Blade(r,flat*Mathf.Sqrt(1-minY*minY)+Vector3.up*minY);}
        }
        public static void Pose(EntityVehicle v,Model.Rig r,Samurai.State s,float now)
        {
            Path(s,Locomotion.Get(v),now,out var grip,out var direction);var offset=r.Torso.position-r.Mount.TransformPoint(r.TorsoBasePosition);
            // Resolve armour clearance by changing the elbow plane first. The
            // grip and blade direction stay on the authored path, avoiding a
            // sudden jump to a different sword pose at a capsule boundary.
            for(int attempt=0;attempt<9;attempt++)for(int bend=0;bend<7;bend++){
                float angle=bend==0?0:((bend+1)/2)*20*(bend%2==1?1:-1);
                var shifted=grip+new Vector3((attempt%3)*.06f,attempt<3?0:attempt<6?.12f:-.12f,(attempt%3)*.06f);Arm(r,r.Mount.TransformPoint(shifted)+offset,r.Mount.TransformDirection(direction),angle);Blade(r,r.Mount.TransformDirection(direction));GroundBlade(v,r,r.Mount.TransformDirection(direction));if(SelfClear(r))return;
            }
            Arm(r,r.Mount.TransformPoint(new Vector3(1.06f,1.91f,.75f))+offset,r.Mount.forward);Blade(r,r.Mount.TransformDirection(new Vector3(.08f,-.04f,.997f)));GroundBlade(v,r,r.Mount.TransformDirection(new Vector3(.08f,-.04f,.997f)));
        }
        public static void SafePose(EntityVehicle v,Model.Rig r){var m=Locomotion.Get(v);if(Boarding.Active(v)||Flight.AirPose(m)||m.WingBlend>.01f)Pose(v,r,Samurai.Get(v),Time.time);if(SelfClear(r)){GroundBlade(v,r,Tip(r)-r.HandR.position);return;}var offset=r.Torso.position-r.Mount.TransformPoint(r.TorsoBasePosition);Arm(r,r.Mount.TransformPoint(new Vector3(1.06f,1.91f,.75f))+offset,r.Mount.forward);Blade(r,r.Mount.TransformDirection(new Vector3(.08f,-.04f,.997f)));GroundBlade(v,r,Tip(r)-r.HandR.position);}
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
