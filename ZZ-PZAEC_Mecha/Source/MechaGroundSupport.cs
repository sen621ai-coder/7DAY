using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace PZAEC.Mecha
{
    // All persistent positions are absolute world coordinates (including Origin).
    // This is the only ground-bearing controller; render IK never applies forces.
    public static class GroundSupport
    {
        public sealed class Profile
        {
            public Vector3[] Hip=new Vector3[2], Home=new Vector3[2], AnkleOffset=new Vector3[2];
            public float Upper,Lower,NeutralY;
            public Vector3[] WingRoot=new Vector3[2];public Bounds[] WingBounds=new Bounds[2];public bool Equipment;
            public Vector3 HullCenter=new Vector3(0,2.18f,0),HullHalf=new Vector3(.75f,.95f,.50f);
            public bool Reach(Vector3 root,Quaternion rotation,int side,Vector3 sole,Vector3 normal)
            {
                var ankle=sole-Quaternion.FromToRotation(Vector3.up,normal)*rotation*AnkleOffset[side];
                float d=Vector3.Distance(root+rotation*Hip[side],ankle);
                return d<=(Upper+Lower)*.95f+.001f&&d>=Mathf.Abs(Upper-Lower)+.01f;
            }
        }
        public struct Pad
        {
            public Vector3 Point,Normal;
            public Collider Surface;
            public Vector3 SurfacePosition;public Quaternion SurfaceRotation;
            public int Points;public float Residual;
        }
        public sealed class Foot
        {
            public Vector3 Position,From,To,Normal=Vector3.up,FromNormal=Vector3.up,ToNormal=Vector3.up;
            public Pad Contact;
            public bool Planted,Swing;public float Age,Duration;
        }
        public sealed class State
        {
            public EntityVehicle Vehicle;public Profile Shape;
            public Foot[] Feet={new Foot(),new Foot()};
            public bool Grounded,Initialized;public int Next,Actor=-1;
            public Vector3 Normal=Vector3.up,LastRoot;
            public float TargetY,Queries;public double SteppedAt=-100;
            public string WalkReason="";
        }
        static readonly Dictionary<int,State> states=new Dictionary<int,State>();
        static readonly List<int> expired=new List<int>();
        static readonly RaycastHit[] hits=new RaycastHit[64];
        static readonly Collider[] overlaps=new Collider[64];
        static readonly Vector2[] corners={Vector2.zero,new Vector2(-1,-1),new Vector2(-1,1),new Vector2(1,-1),new Vector2(1,1)};
        static readonly Vector3[] samplePoints=new Vector3[5],sampleNormals=new Vector3[5];
        public static bool DrawProbes;
        public static string Blocked;
        public static Vector3 LastSurfaceNormal;
        public static void Install(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(EntityVehicle),"GetWheelsOnGround"),postfix:new HarmonyMethod(typeof(GroundSupport),nameof(NativeGround)));
            h.Patch(AccessTools.Method(typeof(EntityVehicle),"FixedUpdate"),postfix:new HarmonyMethod(typeof(GroundSupport),nameof(PassiveFixed)));
            h.Patch(AccessTools.Method(typeof(EntityVehicle),"PhysicsFixedUpdate"),prefix:new HarmonyMethod(typeof(GroundSupport),nameof(PassivePhysics)));
        }
        static bool PassivePhysics(EntityVehicle __instance)
        {
            var v=__instance;if(!Weapons.IsMecha(v)||v.isEntityRemote||v.hasDriver||v.vehicleRB==null||v.IsDead())return true;
            // Skip the parked-car branch that resets velocity/position every frame.
            v.RBActive=true;v.vehicleRB.isKinematic=false;v.vehicleRB.useGravity=true;
            v.SetPosition(v.vehicleRB.position+Origin.position);return false;
        }
        static void PassiveFixed(EntityVehicle __instance)
        {
            var v=__instance;if(!Weapons.IsMecha(v)||v.isEntityRemote||v.hasDriver||v.vehicleRB==null||v.IsDead())return;
            // Native parked vehicles are kinematic. Feet must remain live even without a pilot.
            var rb=v.vehicleRB;v.RBActive=true;rb.isKinematic=false;rb.useGravity=true;
            var support=Observe(v);if(support==null)return;
            var motion=Locomotion.Get(v);bool air=Flight.AirPose(motion);
            motion.Grounded=support.Grounded;motion.HoverOn=motion.Boost=motion.InputReady=motion.Toggle=motion.Jump=motion.Descend=false;motion.Charge=0;motion.ChargeStart=-1;
            motion.FlightMode=support.Grounded?Flight.Phase.Ground:air?Flight.Phase.PowerLost:Flight.Phase.Ground;motion.VerticalInput=0;
            if(Traversal.Active(v))Traversal.Cancel(v,"驾驶已中断");
            if(support.SteppedAt==Time.fixedTimeAsDouble)return;
            support.SteppedAt=Time.fixedTimeAsDouble;Apply(support,Time.fixedDeltaTime);
            if(support.Grounded)Locomotion.ApplyDrive(rb,Vector3.ProjectOnPlane(rb.rotation*Vector3.forward,Vector3.up).normalized,0,0,false,support.Normal,Time.fixedDeltaTime);
        }
        static void NativeGround(EntityVehicle __instance,ref int __result)
        {if(Weapons.IsMecha(__instance))__result=IsGrounded(__instance)?2:0;}
        public static bool IsGrounded(EntityVehicle v)
        {if(v!=null&&v.isEntityRemote)Observe(v);State s;return v!=null&&states.TryGetValue(v.entityId,out s)&&s.Vehicle==v&&s.Grounded;}
        public static State Find(EntityVehicle v)
        {State s;return v!=null&&states.TryGetValue(v.entityId,out s)&&s.Vehicle==v?s:null;}
        public static State Find(Rigidbody body){foreach(var s in states.Values)if(s.Vehicle.vehicleRB==body)return s;return null;}
        public static State Get(EntityVehicle v)
        {
            var s=Find(v);if(s!=null)return s;
            var r=Model.GetRig(v);if(r==null||r.HipL==null)return null;
            var rb=v.vehicleRB;var p=new Profile{Upper=r.LegUpper,Lower=r.LegLower};
            for(int i=0;i<2;i++){
                var hip=i==0?r.HipL:r.HipR;var ankle=i==0?r.AnkleL:r.AnkleR;var foot=i==0?r.FootL:r.FootR;
                p.Hip[i]=rb.transform.InverseTransformPoint(hip.position);
                p.Home[i]=rb.transform.InverseTransformPoint(foot.position);p.Home[i].y=0;
                p.AnkleOffset[i]=Quaternion.Inverse(rb.rotation)*(foot.position-ankle.position);
            }
            // Five percent extension reserve, plus enough crouch for continuous planted gait.
            float lateral=Mathf.Abs(p.Home[0].x-p.Hip[0].x);
            float vertical=Mathf.Sqrt(Mathf.Max(.1f,Mathf.Pow((p.Upper+p.Lower)*.95f,2)-lateral*lateral-.40f*.40f));
            p.NeutralY=Mathf.Min(-.06f,vertical-p.Hip[0].y-p.AnkleOffset[0].y);
            if(Rules.Complete(v)){
                var skins=r.Mount.GetComponentsInChildren<SkinnedMeshRenderer>();
                for(int i=0;i<2;i++){
                    var wing=i==0?r.WingL:r.WingR;p.WingRoot[i]=rb.transform.InverseTransformPoint(wing.position);
                    foreach(var skin in skins){var role=skin.GetComponent<MechaRenderPart>();if(role==null||role.Role!=(i==0?"WingL":"WingR"))continue;
                        var mesh=new Mesh();skin.BakeMesh(mesh);bool first=true;var bounds=new Bounds();
                        foreach(var point in mesh.vertices){var local=rb.transform.InverseTransformPoint(skin.transform.TransformPoint(point))-p.WingRoot[i];if(first){bounds=new Bounds(local,Vector3.zero);first=false;}else bounds.Encapsulate(local);}
                        p.WingBounds[i]=bounds;UnityEngine.Object.Destroy(mesh);p.Equipment=true;
                    }
                }
            }
            s=new State{Vehicle=v,Shape=p,LastRoot=rb.position+Origin.position};states[v.entityId]=s;return s;
        }
        public static bool Own(EntityVehicle v,Collider c)
        {
            if(c==null)return true;var t=c.transform;var rb=v.vehicleRB;var driver=v.GetAttached(0);
            return GameUtils.GetHitRootEntity(c.tag,t)==v||(rb!=null&&c.attachedRigidbody==rb)||t.IsChildOf(v.transform)||(rb!=null&&t.IsChildOf(rb.transform))||(driver!=null&&t.IsChildOf(driver.transform));
        }
        public static bool StaticSurface(EntityVehicle v,Collider c)
        {
            if(c==null||c.isTrigger||Own(v,c)||c.attachedRigidbody!=null)return false;
            if(GameUtils.GetHitRootEntity(c.tag,c.transform)!=null||c.GetComponentInParent<Entity>()!=null)return false;
            // Doors/elevators may be animated without a Rigidbody. Never bear weight on them.
            for(var t=c.transform;t!=null;t=t.parent){string n=t.name;if(n.IndexOf("door",StringComparison.OrdinalIgnoreCase)>=0||n.IndexOf("elevator",StringComparison.OrdinalIgnoreCase)>=0)return false;}
            return true;
        }
        public static bool PadAt(EntityVehicle v,Vector3 at,Quaternion yaw,float up,float down,out Pad pad)
        {
            pad=new Pad();int count=0;Vector3 center=Vector3.zero,normal=Vector3.zero;Collider surface=null;
            for(int i=0;i<5;i++){
                var q=at+yaw*new Vector3(corners[i].x*Rules.FootWidth*.5f,0,corners[i].y*Rules.FootDepth*.5f);
                int n=Physics.RaycastNonAlloc(q-Origin.position+Vector3.up*up,Vector3.down,hits,up+down,~0,QueryTriggerInteraction.Ignore);
                if(n==hits.Length)return false;
                float nearest=float.PositiveInfinity;RaycastHit best=new RaycastHit();
                for(int j=0;j<n;j++)if(!Own(v,hits[j].collider)&&hits[j].distance<nearest){best=hits[j];nearest=best.distance;}
                if(float.IsPositiveInfinity(nearest)||!StaticSurface(v,best.collider)||best.normal.y+.0001f<Mathf.Cos(Rules.MaxWalkSlope*Mathf.Deg2Rad))continue;
                var point=best.point+Origin.position;samplePoints[count]=point;sampleNormals[count]=best.normal;
                center+=point;normal+=best.normal;count++;if(surface==null)surface=best.collider;
                if(DrawProbes)Debug.DrawLine(q-Origin.position+Vector3.up*up,best.point,Color.cyan,Time.fixedDeltaTime);
            }
            if(count<4)return false;center/=count;normal.Normalize();float residual=0;
            // Height variation on a slope is expected; reject variation *off* its plane.
            for(int i=0;i<count;i++)residual=Mathf.Max(residual,Mathf.Abs(Vector3.Dot(samplePoints[i]-center,normal))/Mathf.Max(.01f,normal.y));
            if(residual>Rules.FootResidual)return false;
            float y=center.y-(normal.x*(at.x-center.x)+normal.z*(at.z-center.z))/normal.y;
            pad=new Pad{Point=new Vector3(at.x,y+Rules.SoleClearance,at.z),Normal=normal,Surface=surface,SurfacePosition=surface.transform.position+Origin.position,SurfaceRotation=surface.transform.rotation,Points=count,Residual=residual};
            var state=Find(v);if(state!=null)state.Queries+=5;return true;
        }
        public static bool PointAt(EntityVehicle v,Vector3 at,float up,float down,out Vector3 point)
        {
            point=at;int count=Physics.RaycastNonAlloc(at-Origin.position+Vector3.up*up,Vector3.down,hits,up+down,~0,QueryTriggerInteraction.Ignore);
            if(count==hits.Length)return false;float nearest=float.PositiveInfinity;RaycastHit best=new RaycastHit();
            for(int i=0;i<count;i++)if(!Own(v,hits[i].collider)&&hits[i].distance<nearest){best=hits[i];nearest=best.distance;}
            if(float.IsPositiveInfinity(nearest)||!StaticSurface(v,best.collider)||best.normal.y+.0001f<Mathf.Cos(Rules.MaxWalkSlope*Mathf.Deg2Rad))return false;point=best.point+Origin.position;LastSurfaceNormal=best.normal;return true;
        }
        public static float GapWidth(EntityVehicle v,Vector3 near,Vector3 far)
        {
            var delta=far-near;float length=delta.magnitude;
            int count=Physics.RaycastNonAlloc(near-Origin.position-Vector3.up*.06f,delta.normalized,hits,length+.08f,~0,QueryTriggerInteraction.Ignore);
            if(count==hits.Length)return length;
            for(int i=0;i<count;i++)if(StaticSurface(v,hits[i].collider)&&Mathf.Abs(hits[i].normal.y)<.3f)return Mathf.Abs(Vector3.Dot(delta,hits[i].normal));
            return length;
        }
        public static bool Recheck(EntityVehicle v,Foot foot)
        {
            if(!foot.Planted)return false;
            var rotation=Quaternion.FromToRotation(Vector3.up,foot.Normal)*v.vehicleRB.rotation;
            if(!BoxClear(v,foot.Position+foot.Normal*.15f,foot.Position+foot.Normal*.15f,new Vector3(.20f,.125f,.29f),rotation))return false;
            var c=foot.Contact;
            if(c.Surface!=null&&(Vector3.Distance(c.Surface.transform.position+Origin.position,c.SurfacePosition)>.002f||Quaternion.Angle(c.Surface.transform.rotation,c.SurfaceRotation)>.1f))return false;
            Pad next;
            if(!PadAt(v,foot.Position,v.vehicleRB.rotation,.45f,.45f,out next)||Vector3.Distance(next.Point,foot.Position)>.05f)return false;
            foot.Contact=next;foot.Normal=next.Normal;return true;
        }
        public static bool HullClear(EntityVehicle v,Profile shape,Vector3 a,Vector3 b,Quaternion yaw)
        {return BoxClear(v,a+yaw*shape.HullCenter,b+yaw*shape.HullCenter,shape.HullHalf,yaw);}
        public static bool BoxClear(EntityVehicle v,Vector3 a,Vector3 b,Vector3 half,Quaternion yaw)
        {
            int count=Physics.OverlapBoxNonAlloc(b-Origin.position,half,overlaps,yaw,~0,QueryTriggerInteraction.Ignore);
            if(count==overlaps.Length)return false;
            for(int i=0;i<count;i++)if(!Own(v,overlaps[i])){Blocked=overlaps[i].name+" bounds="+overlaps[i].bounds;return false;}
            var delta=b-a;if(delta.sqrMagnitude<.000001f)return true;
            count=Physics.BoxCastNonAlloc(a-Origin.position,half,delta.normalized,hits,yaw,delta.magnitude,~0,QueryTriggerInteraction.Ignore);
            if(count==hits.Length)return false;
            for(int i=0;i<count;i++)if(!Own(v,hits[i].collider)){Blocked=hits[i].collider.name+" bounds="+hits[i].collider.bounds;return false;}
            return true;
        }
        public static void Suspend(EntityVehicle v)
        {var s=Find(v);if(s==null)return;s.Grounded=false;foreach(var f in s.Feet){f.Planted=f.Swing=false;}s.Initialized=false;}
        public static State Observe(EntityVehicle v)
        {
            var s=Get(v);if(s==null)return null;var rb=v.vehicleRB;var root=rb.position+Origin.position;
            if(Vector3.Distance(s.LastRoot,root)>3){Suspend(v);}s.LastRoot=root;
            if(rb.velocity.y>1.0f&&!Traversal.Active(v)&&!s.Grounded){Suspend(v);return s;}
            if(!s.Initialized){
                for(int i=0;i<2;i++){
                    var f=s.Feet[i];var home=root+rb.rotation*s.Shape.Home[i];Pad p;
                    if(PadAt(v,home,rb.rotation,.80f,.40f,out p)&&Mathf.Abs(root.y-(p.Point.y+s.Shape.NeutralY))<.40f&&s.Shape.Reach(root,rb.rotation,i,p.Point,p.Normal)){
                        f.Position=p.Point;f.Normal=p.Normal;f.Contact=p;f.Planted=true;
                    }
                }
                s.Initialized=s.Feet[0].Planted||s.Feet[1].Planted;
            }
            s.Grounded=false;var normal=Vector3.zero;
            for(int i=0;i<2;i++){var f=s.Feet[i];if(f.Planted&&(!Recheck(v,f)||(!Traversal.Active(v)&&!s.Shape.Reach(root,rb.rotation,i,f.Position,f.Normal))))f.Planted=false;if(f.Planted){s.Grounded=true;normal+=f.Normal;}}
            s.Normal=normal.sqrMagnitude>.01f?normal.normalized:Vector3.up;
            if(!s.Grounded&&!s.Feet[0].Swing&&!s.Feet[1].Swing)s.Initialized=false;
            return s;
        }
        public static void Plant(State s,int side,Pad p,bool sound=true)
        {
            var f=s.Feet[side];f.Position=p.Point;f.Normal=p.Normal;f.Contact=p;f.Planted=true;f.Swing=false;
            if(sound)RobotAudio.ContactEvent(s.Vehicle,side==0?"step-left":"step-right",RobotAudio.NextPresentationSerial(),p.Point,.85f);
            s.Next=1-side;
        }
        public static void Walking(State s,float dt,bool enabled)
        {
            if(s==null||!s.Grounded)return;var v=s.Vehicle;var rb=v.vehicleRB;var root=rb.position+Origin.position;
            var planar=Vector3.ProjectOnPlane(rb.velocity,s.Normal);float speed=planar.magnitude;
            for(int i=0;i<2;i++){
                var f=s.Feet[i];if(f.Swing){
                    f.Age+=dt;float t=Mathf.Clamp01(f.Age/f.Duration),ease=t*t*(3-2*t);
                    var proposed=Traversal.Swing(f.From,f.To,t,.12f);
                    f.Normal=Vector3.Slerp(f.FromNormal,f.ToNormal,ease).normalized;
                    var rotation=Quaternion.FromToRotation(Vector3.up,f.Normal)*rb.rotation;
                    if(!Traversal.FootPath(v,s.Shape,root,rb.rotation,i,proposed,f.Position,0,f.Normal)){s.WalkReason="foot path blocked "+Blocked;f.Swing=false;continue;}
                    f.Position=proposed;
                    if(t>=1){Pad p;if(PadAt(v,f.To,rb.rotation,.45f,.45f,out p))Plant(s,i,p);else f.Swing=false;}
                    continue;
                }
                if(!enabled||s.Feet[1-i].Swing||!s.Feet[1-i].Planted||i!=s.Next)continue;
                var home=root+rb.rotation*s.Shape.Home[i];
                float duration=Mathf.Clamp(.16f/(1+speed*1.1f),.008f,.20f);
                var ahead=home+Vector3.ClampMagnitude(planar*(duration+.035f),.45f);ahead.y=f.Position.y;
                var error=Vector3.ProjectOnPlane(ahead-f.Position,s.Normal).magnitude;
                if(error<(speed>.10f?.12f:.18f)&&Mathf.Abs(rb.angularVelocity.y)<.06f)continue;
                Pad target;
                bool found=PadAt(v,ahead,rb.rotation,.80f,.80f,out target);
                if(!found&&speed>.10f){for(float extra=.05f;extra<=.45f;extra+=.05f){if(PadAt(v,ahead+planar.normalized*extra,rb.rotation,.80f,.80f,out target)){found=true;break;}}}
                if(!found){s.WalkReason="no pad";continue;}
                float planeHeight=f.Position.y-(s.Normal.x*(target.Point.x-f.Position.x)+s.Normal.z*(target.Point.z-f.Position.z))/Mathf.Max(.01f,s.Normal.y);
                if(Mathf.Abs(target.Point.y-planeHeight)>Rules.AutoStepHeight+.02f){s.WalkReason="height delta="+(target.Point.y-planeHeight);continue;}
                if(!s.Shape.Reach(root+planar*duration,rb.rotation,i,target.Point,target.Normal)){s.WalkReason="predicted reach";continue;}
                s.WalkReason="swing side="+i;
                f.From=f.Position;f.To=target.Point;f.FromNormal=f.Normal;f.ToNormal=target.Normal;f.Age=0;f.Duration=duration;f.Swing=true;f.Planted=false;
            }
        }
        public static bool MotionClear(State s,float dt)
        {
            if(s==null||!s.Grounded)return true;var v=s.Vehicle;var rb=v.vehicleRB;var a=rb.position+Origin.position;
            var delta=rb.velocity*dt;int count=Mathf.Max(1,Mathf.CeilToInt(delta.magnitude/.05f));var previous=a;
            for(int k=1;k<=count;k++){
                var b=a+delta*(k/(float)count);
                if(!HullClear(v,s.Shape,previous,b,rb.rotation)){s.WalkReason="body path blocked "+Blocked;return false;}
                for(int side=0;side<2;side++)if(!Traversal.FootPath(v,s.Shape,b,rb.rotation,side,s.Feet[side].Position,s.Feet[side].Position,0,s.Feet[side].Normal)){s.WalkReason="leg motion blocked "+Blocked;return false;}
                previous=b;
            }return true;
        }
        public static void StopHorizontal(State s)
        {var rb=s.Vehicle.vehicleRB;rb.AddForce(-Vector3.ProjectOnPlane(rb.velocity,Vector3.up),ForceMode.VelocityChange);}
        public static void Apply(State s,float dt,Vector3? plannedRoot=null)
        {
            if(s==null||!s.Grounded)return;var rb=s.Vehicle.vehicleRB;var root=rb.position+Origin.position;
            float target=float.PositiveInfinity;
            for(int i=0;i<2;i++)if(s.Feet[i].Planted){
                var f=s.Feet[i];float offset=(Quaternion.FromToRotation(Vector3.up,f.Normal)*rb.rotation*s.Shape.AnkleOffset[i]).y;
                target=Mathf.Min(target,f.Position.y+s.Shape.NeutralY+s.Shape.AnkleOffset[i].y-offset);
            }
            if(plannedRoot.HasValue)target=plannedRoot.Value.y;
            else target-=.15f*Mathf.Clamp01(Mathf.Max(Vector3.ProjectOnPlane(rb.velocity,Vector3.up).magnitude/.8f,Mathf.Abs(rb.angularVelocity.y)/.3f));
            // No support from distant ground: all load comes from a live planted pad.
            s.TargetY=target;
            float acceleration=-Physics.gravity.y+(target-root.y)*400-rb.velocity.y*40+rb.velocity.y*rb.drag;
            rb.AddForce(Vector3.up*Mathf.Clamp(acceleration,-100,180)*rb.mass,ForceMode.Force);
            if(!plannedRoot.HasValue){
                for(int i=0;i<2;i++)if(s.Feet[i].Planted&&!s.Shape.Reach(root,rb.rotation,i,s.Feet[i].Position,s.Feet[i].Normal)){
                    // A foot outside real joint reach cannot hold an arbitrary distant body.
                    if(Vector3.Distance(root+rb.rotation*s.Shape.Hip[i],s.Feet[i].Position)>s.Shape.Upper+s.Shape.Lower+.4f)s.Feet[i].Planted=false;
                }
            }
        }
        public static string Diagnostics(EntityVehicle v)
        {var s=Find(v);if(s==null)return "uninitialized";return "ground="+s.Grounded+" normal="+s.Normal+" targetY="+s.TargetY+" left="+s.Feet[0].Position+" planted="+s.Feet[0].Planted+" right="+s.Feet[1].Position+" planted="+s.Feet[1].Planted+" lengths="+s.Shape.Upper+"/"+s.Shape.Lower+" reserve=5% queries="+s.Queries+" walking="+s.WalkReason;}
        public static void Forget(EntityVehicle v){states.Remove(v.entityId);Traversal.Forget(v);}
        public static void Cleanup(World world)
        {
            expired.Clear();foreach(var pair in states)if(pair.Value.Vehicle==null||world.GetEntity(pair.Key)!=pair.Value.Vehicle)expired.Add(pair.Key);
            foreach(int id in expired){var v=states[id].Vehicle;if(!ReferenceEquals(v,null))Model.Forget(v);else states.Remove(id);}
        }
        public static void Clear(){states.Clear();Traversal.Clear();DrawProbes=false;}
    }
}
