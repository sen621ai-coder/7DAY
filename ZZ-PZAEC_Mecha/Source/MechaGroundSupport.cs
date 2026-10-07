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
            public bool Justice;public Bounds Backpack,BackpackAir;public Bounds[] BackpackPoses;public Bounds[][] BackpackParts;
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
            public bool Planted,Swing,Recovery;public float Age,Duration,Retry,Lift=Rules.TraverseToeClearance;
        }
        public sealed class State
        {
            public EntityVehicle Vehicle;public Profile Shape;
            public Foot[] Feet={new Foot(),new Foot()};
            public bool Grounded,Initialized;public int Next,Actor=-1;
            public Vector3 Normal=Vector3.up,LastRoot,DesiredVelocity;
            public float TargetY,Queries,PreparedHeight=float.NaN;public double SteppedAt=-100;
            public string WalkReason="",StopReason="",Failure="";public bool Recovering,Cautious,QueuedToggle;public float Clock,DriveCap=13.5f,StepSearchAt=-100,RecoverySoundAt=-100;
        }
        static readonly Dictionary<int,State> states=new Dictionary<int,State>();
        static readonly List<int> expired=new List<int>();
        static readonly RaycastHit[] hits=new RaycastHit[64];
        static readonly Collider[] overlaps=new Collider[64];
        static readonly Vector2[] corners={Vector2.zero,new Vector2(-1,-1),new Vector2(-1,1),new Vector2(1,-1),new Vector2(1,1)};
        static readonly Vector3[] samplePoints=new Vector3[5],sampleNormals=new Vector3[5];
        public static bool DrawProbes;
        public static string Blocked,PadFailure;
        public static Vector3 LastSurfaceNormal;
        public static void Install(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(EntityVehicle),"GetWheelsOnGround"),postfix:new HarmonyMethod(typeof(GroundSupport),nameof(NativeGround)));
            h.Patch(AccessTools.Method(typeof(EntityVehicle),"FixedUpdate"),postfix:new HarmonyMethod(typeof(GroundSupport),nameof(PassiveFixed)));
            h.Patch(AccessTools.Method(typeof(EntityVehicle),"PhysicsFixedUpdate"),prefix:new HarmonyMethod(typeof(GroundSupport),nameof(PassivePhysics)));
        }
        static bool PassivePhysics(EntityVehicle __instance)
        {
            var v=__instance;if(!Weapons.IsMecha(v)||v.isEntityRemote||v.vehicleRB==null||v.IsDead())return true;
            // Native PhysicsFixedUpdate adds -9.81 * mass itself. Parked mecha
            // bypass it and use Unity gravity instead; hand ownership back on boarding.
            // Otherwise both gravity sources survive and overpower hover/flight thrust.
            if(v.hasDriver){v.vehicleRB.useGravity=false;return true;}
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
            var motion=Locomotion.Get(v);bool air=Flight.AirPose(motion);if(motion.SkimPhase!=Skim.Phase.Off)Skim.Cancel(motion);
            motion.Grounded=support.Grounded;motion.HoverOn=motion.Boost=motion.InputReady=motion.Toggle=motion.Jump=motion.Descend=false;motion.Charge=0;motion.ChargeStart=-1;
            motion.FlightMode=support.Grounded?Flight.Phase.Ground:air?Flight.Phase.PowerLost:Flight.Phase.Ground;motion.VerticalInput=0;
            if(Traversal.Active(v))Traversal.Cancel(v,"驾驶已中断");
            if(support.SteppedAt==Time.fixedTimeAsDouble)return;
            support.SteppedAt=Time.fixedTimeAsDouble;Apply(support,Time.fixedDeltaTime);
            if(support.Grounded)Locomotion.ApplyDrive(rb,Vector3.ProjectOnPlane(rb.rotation*Vector3.forward,Vector3.up).normalized,0,0,false,support.Normal,Time.fixedDeltaTime);
            GroundNet.Publish(support);
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
            if(r.Justice!=null){p.Justice=true;p.Backpack=Justice.BackpackEnvelope(r);p.BackpackAir=Justice.BackpackEnvelope(r,true);p.BackpackPoses=new Bounds[11];p.BackpackParts=new Bounds[11][];for(int pose=0;pose<=10;pose++){p.BackpackPoses[pose]=Justice.BackpackEnvelope(r,false,pose/10f);p.BackpackParts[pose]=Justice.BackpackBoxes(r,pose/10f);}}
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
            // up/down bound the sole CENTER. Sloping corners can be higher/lower
            // by half a foot diagonal, including during the final 8 cm recheck.
            float cornerHeight=Mathf.Sqrt(Rules.SoleWidth(v)*Rules.SoleWidth(v)+Rules.SoleDepth(v)*Rules.SoleDepth(v))*.5f*Mathf.Tan(Rules.MaxWalkSlope*Mathf.Deg2Rad)+Rules.FootResidual;
            float rayUp=up+cornerHeight,rayDown=down+cornerHeight;
            for(int i=0;i<5;i++){
                var q=at+yaw*new Vector3(corners[i].x*Rules.SoleWidth(v)*.5f,0,corners[i].y*Rules.SoleDepth(v)*.5f);
                int n=Physics.RaycastNonAlloc(q-Origin.position+Vector3.up*rayUp,Vector3.down,hits,rayUp+rayDown,~0,QueryTriggerInteraction.Ignore);
                if(n==hits.Length)return false;
                float nearest=float.PositiveInfinity;RaycastHit best=new RaycastHit();
                for(int j=0;j<n;j++)if(!Own(v,hits[j].collider)&&hits[j].distance<nearest){best=hits[j];nearest=best.distance;}
                if(float.IsPositiveInfinity(nearest)||!StaticSurface(v,best.collider)||best.normal.y+.0001f<Mathf.Cos(Rules.MaxWalkSlope*Mathf.Deg2Rad))continue;
                var point=best.point+Origin.position;samplePoints[count]=point;sampleNormals[count]=best.normal;
                center+=point;normal+=best.normal;count++;if(surface==null)surface=best.collider;
                if(DrawProbes)Debug.DrawLine(q-Origin.position+Vector3.up*up,best.point,Color.cyan,Time.fixedDeltaTime);
            }
            if(count<4){PadFailure="可靠接触点不足或坡面过陡";return false;}center/=count;normal.Normalize();float residual=0;
            // Height variation on a slope is expected; reject variation *off* its plane.
            for(int i=0;i<count;i++)residual=Mathf.Max(residual,Mathf.Abs(Vector3.Dot(samplePoints[i]-center,normal))/Mathf.Max(.01f,normal.y));
            if(count==5&&residual>Rules.FootResidual){
                // Four reliable contacts may bridge one low corner at a rim.
                // A high outlier is still caught by the whole-sole volume below.
                int outlier=0;float worst=-1;for(int i=0;i<5;i++){float value=Mathf.Abs(Vector3.Dot(samplePoints[i]-center,normal));if(value>worst){worst=value;outlier=i;}}
                samplePoints[outlier]=samplePoints[4];sampleNormals[outlier]=sampleNormals[4];count=4;center=normal=Vector3.zero;
                for(int i=0;i<count;i++){center+=samplePoints[i];normal+=sampleNormals[i];}center/=count;normal.Normalize();residual=0;
                for(int i=0;i<count;i++)residual=Mathf.Max(residual,Mathf.Abs(Vector3.Dot(samplePoints[i]-center,normal))/Mathf.Max(.01f,normal.y));
            }
            if(residual>Rules.FootResidual){PadFailure="足底局部起伏过大 "+residual;return false;}
            float y=center.y-(normal.x*(at.x-center.x)+normal.z*(at.z-center.z))/normal.y;
            float rise=0;for(int i=0;i<count;i++)rise=Mathf.Max(rise,Vector3.Dot(samplePoints[i]-center,normal)/normal.y);
            // A rigid sole rests above convex bumps, never through the fitted mean plane.
            y+=rise;
            // The five support samples can miss a narrow ridge between corners.
            // Sweep the entire thin sole onto the fitted plane before accepting it.
            var sole=new Vector3(at.x,y+Rules.SoleClearance,at.z);var soleRotation=Quaternion.FromToRotation(Vector3.up,normal)*yaw;
            int swept=Physics.BoxCastNonAlloc(sole-Origin.position+normal*.5f,new Vector3(Rules.SoleWidth(v)*.5f,.002f,Rules.SoleDepth(v)*.5f),-normal,hits,soleRotation,1f,~0,QueryTriggerInteraction.Ignore);
            if(swept==hits.Length)return false;float nearestSole=float.PositiveInfinity;Collider touch=null;
            for(int i=0;i<swept;i++)if(!Own(v,hits[i].collider)&&hits[i].distance<nearestSole){nearestSole=hits[i].distance;touch=hits[i].collider;}
            if(touch==null||!StaticSurface(v,touch))return false;
            float extra=Mathf.Max(0,.5f-nearestSole-.002f+Rules.SoleClearance*normal.y)/normal.y;
            if(extra>Rules.FootResidual+.001f){PadFailure="足底存在凸脊 "+extra;return false;}y+=extra;
            if(y+Rules.SoleClearance-at.y>up+.001f||at.y-y-Rules.SoleClearance>down+.001f)return false;
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
            if(!BoxClear(v,foot.Position+foot.Normal*.15f,foot.Position+foot.Normal*.15f,Rules.Complete(v)?new Vector3(Rules.SoleWidth(v)*.5f,.125f,Rules.SoleDepth(v)*.5f):new Vector3(.20f,.125f,.29f),rotation))return false;
            var c=foot.Contact;
            if(c.Surface!=null&&(Vector3.Distance(c.Surface.transform.position+Origin.position,c.SurfacePosition)>.002f||Quaternion.Angle(c.Surface.transform.rotation,c.SurfaceRotation)>.1f))return false;
            Pad next;
            if(!PadAt(v,foot.Position,v.vehicleRB.rotation,.45f,.45f,out next)||Vector3.Distance(next.Point,foot.Position)>.05f)return false;
            foot.Contact=next;foot.Normal=next.Normal;return true;
        }
        public static bool HullClear(EntityVehicle v,Profile shape,Vector3 a,Vector3 b,Quaternion yaw,float packBlend=-1)
        {
            if(!BoxClear(v,a+yaw*shape.HullCenter,b+yaw*shape.HullCenter,shape.HullHalf,yaw))return false;
            if(!shape.Justice)return true;
            if(packBlend<0&&Traversal.Active(v)){var traversal=Traversal.Get(v);packBlend=Mathf.Clamp01(traversal.Age/traversal.Current.Duration/.12f);}
            bool air=Skim.Active(v)||Flight.AirPose(Locomotion.Get(v));float sample=Mathf.Clamp01(packBlend)*10;int lo=air?0:Mathf.FloorToInt(sample),hi=air?10:Mathf.Min(10,Mathf.CeilToInt(sample));
            for(int part=0;part<8;part++){var box=shape.BackpackParts[lo][part];if(box.size==Vector3.zero)continue;for(int pose=lo+1;pose<=hi;pose++)box.Encapsulate(shape.BackpackParts[pose][part]);if(!BoxClear(v,a+yaw*box.center,b+yaw*box.center,box.extents,yaw))return false;}
            return true;
        }
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
        {var s=Find(v);if(s==null)return;s.Grounded=s.Recovering=s.Cautious=s.QueuedToggle=false;s.PreparedHeight=float.NaN;s.DriveCap=13.5f;s.StepSearchAt=-100;s.StopReason="";foreach(var f in s.Feet){f.Planted=f.Swing=f.Recovery=false;}s.Initialized=false;}
        public static State Observe(EntityVehicle v)
        {
            var s=Get(v);if(s==null)return null;var rb=v.vehicleRB;var root=rb.position+Origin.position;
            if(v.isEntityRemote&&GroundNet.Fresh(v))return s;
            int actor=v.GetAttached(0)!=null?v.GetAttached(0).entityId:-1;
            if(actor!=s.Actor){s.Actor=actor;s.QueuedToggle=false;for(int i=0;i<2;i++)if(s.Feet[i].Swing&&!Traversal.Active(v))FootPlanner.Release(s,i);}
            if(Vector3.Distance(s.LastRoot,root)>3){Suspend(v);}s.LastRoot=root;
            if(rb.velocity.y>1.0f&&!Traversal.Active(v)&&!s.Grounded){Suspend(v);return s;}
            if(!s.Initialized){
                for(int i=0;i<2;i++){
                    var f=s.Feet[i];var home=root+rb.rotation*s.Shape.Home[i];Pad p;
                    if(!f.Planted&&!f.Swing){f.Position=FootPlanner.ReachableSole(s,i,home,Vector3.up);f.Normal=Vector3.up;}
                    if(PadAt(v,home,rb.rotation,.80f,.40f,out p)&&Mathf.Abs(root.y-(p.Point.y+s.Shape.NeutralY))<.40f&&s.Shape.Reach(root,rb.rotation,i,p.Point,p.Normal)){
                        f.Position=p.Point;f.Normal=p.Normal;f.Contact=p;f.Planted=true;f.Swing=f.Recovery=false;
                    }
                }
                s.Initialized=s.Feet[0].Planted||s.Feet[1].Planted;
                if(s.Initialized){s.StopReason="";s.DriveCap=13.5f;s.PreparedHeight=float.NaN;}
            }
            s.Grounded=false;var normal=Vector3.zero;
            for(int i=0;i<2;i++){var f=s.Feet[i];if(f.Planted&&(!Recheck(v,f)||(!Traversal.Active(v)&&!s.Shape.Reach(root,rb.rotation,i,f.Position,f.Normal)))){f.Planted=false;if(!Traversal.Active(v))FootPlanner.Release(s,i);}if(f.Planted){s.Grounded=true;normal+=f.Normal;}}
            s.Recovering=s.Grounded&&(s.Feet[0].Recovery||s.Feet[1].Recovery);
            s.Normal=normal.sqrMagnitude>.01f?normal.normalized:Vector3.up;
            if(!s.Grounded&&!s.Feet[0].Swing&&!s.Feet[1].Swing)s.Initialized=false;
            return s;
        }
        public static void Plant(State s,int side,Pad p,bool sound=true)
        {
            var f=s.Feet[side];bool recovery=f.Recovery;f.Position=p.Point;f.Normal=p.Normal;f.Contact=p;f.Planted=true;f.Swing=f.Recovery=false;
            s.Recovering=s.Feet[0].Recovery||s.Feet[1].Recovery;
            if(!s.Recovering)s.StopReason="";
            if(sound)RobotAudio.ContactEvent(s.Vehicle,side==0?"step-left":"step-right",RobotAudio.NextPresentationSerial(),p.Point,.85f);
            if(sound&&recovery&&!s.Recovering)RobotAudio.Event(s.Vehicle,"stand-lock",RobotAudio.NextPresentationSerial(),.32f);
            s.Next=1-side;
        }
        public static void Walking(State s,float dt,bool enabled)
        {
            if(s==null||!s.Grounded)return;var v=s.Vehicle;var rb=v.vehicleRB;var root=rb.position+Origin.position;
            s.Clock+=dt;s.PreparedHeight=float.NaN;s.DriveCap=s.StopReason!=""?0:s.Cautious?Rules.RoughWalkSpeed:13.5f;
            var planar=Vector3.ProjectOnPlane(rb.velocity,Vector3.up);float speed=planar.magnitude;
            FootPlanner.Recovery(s,dt,enabled);
            for(int i=0;i<2;i++){
                var f=s.Feet[i];if(f.Swing){
                    float age=Mathf.Min(f.Duration,f.Age+dt),t=age/f.Duration;var proposed=Traversal.Swing(f.From,f.To,t,f.Lift);
                    var normal=Vector3.Slerp(f.FromNormal,f.ToNormal,t).normalized;
                    float height;var left=i==0?proposed:s.Feet[0].Position;var right=i==1?proposed:s.Feet[1].Position;
                    if(!FootPlanner.Pelvis(s,root,left,right,i==0?normal:s.Feet[0].Normal,i==1?normal:s.Feet[1].Normal,FootPlanner.Preferred(s,left,right,i==0?normal:s.Feet[0].Normal,i==1?normal:s.Feet[1].Normal),out height)){
                        FootPlanner.Release(s,i);s.DriveCap=0;s.WalkReason=s.Failure="pelvis / step interval lost";continue;
                    }
                    var predicted=root;predicted.y=height;
                    // Wait for the physical pelvis to lower/raise into reach, rather
                    // than moving the foot ahead of the body or deleting its anchor.
                    if(!s.Shape.Reach(root,rb.rotation,i,proposed,normal)){s.PreparedHeight=height;s.DriveCap=0;s.WalkReason="waiting for constrained pelvis";continue;}
                    if(!Traversal.FootPath(v,s.Shape,root,rb.rotation,i,proposed,f.Position,0,normal)){
                        FootPlanner.Release(s,i);s.DriveCap=0;s.WalkReason=s.Failure="foot path blocked "+Traversal.PathFailure;continue;
                    }
                    f.Age=age;f.Position=proposed;f.Normal=normal;
                    if(t>=1){Pad pad;if(PadAt(v,f.To,rb.rotation,.08f,.08f,out pad)&&Vector3.Distance(pad.Point,f.To)<.05f)Plant(s,i,pad);else FootPlanner.Release(s,i);}
                    continue;
                }
                if(!enabled||s.Recovering||s.Feet[1-i].Swing||!s.Feet[1-i].Planted||i!=s.Next)continue;
                var home=root+rb.rotation*s.Shape.Home[i];float duration=s.Cautious?Rules.RoughStepSeconds:WalkDuration(speed);
                bool attack=Rules.Complete(v)&&Samurai.Get(v).Swing&&!Samurai.Get(v).Blocked&&SwordMotion.Phase(Samurai.Get(v),Time.time)<SwordMotion.WindEnd;
                if(attack)duration=Mathf.Max(duration,.20f);
                var ahead=home+Vector3.ClampMagnitude(planar*(duration+.035f),s.Cautious?.28f:.85f);
                if(speed<.30f&&s.DesiredVelocity.sqrMagnitude>.01f)ahead+=Vector3.ClampMagnitude(s.DesiredVelocity,.8f)*(duration+.035f);
                if(attack&&Vector3.Dot(s.DesiredVelocity,rb.rotation*Vector3.forward)>0)ahead+=rb.rotation*Vector3.forward*.15f;
                float error=Vector3.ProjectOnPlane(ahead-f.Position,Vector3.up).magnitude;
                if(error<(speed>.10f?.10f:.18f)&&Mathf.Abs(rb.angularVelocity.y)<.06f)continue;
                // Retry a refused candidate at 10 Hz, not twelve expensive searches per frame.
                if(s.Clock-s.StepSearchAt<.10f&&s.StopReason!="")continue;s.StepSearchAt=s.Clock;
                Pad target;if(!FootPlanner.Select(s,i,ahead,duration,planar,false,out target)){
                    s.DriveCap=0;s.StopReason="前方缺少完整落脚路径";s.WalkReason="no complete walking plan "+FootPlanner.Failure;continue;
                }
                s.StopReason="";s.WalkReason="swing side="+i;
                f.From=f.Position;f.To=target.Point;f.FromNormal=f.Normal;f.ToNormal=target.Normal;f.Age=0;f.Duration=duration;f.Lift=FootPlanner.SelectedLift;f.Swing=true;f.Planted=false;
            }
            if(s.Recovering)s.DriveCap=0;
        }
        // Normal walking needs a visible swing, not a one-frame foot shuffle.
        // At boosted speeds bound body travel while the opposite foot bears load.
        public static float WalkDuration(float speed)
        {
            float travel=Mathf.Lerp(.65f,.40f,Mathf.InverseLerp(4,8,speed));
            return Mathf.Min(.30f/(1+speed*.15f),travel/Mathf.Max(speed,.1f));
        }
        public static bool MotionClear(State s,float dt)
        {
            if(s==null||!s.Grounded)return true;var v=s.Vehicle;var rb=v.vehicleRB;var a=rb.position+Origin.position;
            var delta=rb.velocity*dt;int count=Mathf.Max(1,Mathf.CeilToInt(delta.magnitude/.05f));var previous=a;
            for(int k=1;k<=count;k++){
                var b=a+delta*(k/(float)count);float height;
                for(int side=0;side<2;side++)if(s.Feet[side].Planted&&!s.Shape.Reach(b,rb.rotation,side,s.Feet[side].Position,s.Feet[side].Normal)){s.WalkReason="bearing leg reach boundary";return false;}
                if(!FootPlanner.TargetHeight(s,b,out height)){s.WalkReason="no shared pelvis interval";return false;}b.y=height;
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
            float target;
            if(plannedRoot.HasValue)target=plannedRoot.Value.y;
            else if(!float.IsNaN(s.PreparedHeight))target=s.PreparedHeight;
            else if(!FootPlanner.TargetHeight(s,root,out target)){
                // Retain only genuinely reachable bearing legs; stop the drive,
                // then seek a stationary recovery instead of holding stretched feet.
                s.DriveCap=0;s.StopReason="正在重新落稳";
                for(int i=0;i<2;i++)if(s.Feet[i].Planted&&!s.Shape.Reach(root,rb.rotation,i,s.Feet[i].Position,s.Feet[i].Normal))FootPlanner.Release(s,i);
                float low,high;int side=s.Feet[0].Planted?0:1;
                if(!s.Feet[side].Planted||!FootPlanner.HeightRange(s.Shape,root,rb.rotation,side,s.Feet[side].Position,s.Feet[side].Normal,out low,out high))return;
                target=Mathf.Clamp(root.y,low,high);
            }
            if(!plannedRoot.HasValue&&float.IsNaN(s.PreparedHeight)){
                var future=root+Vector3.ProjectOnPlane(rb.velocity,Vector3.up)*Mathf.Max(.04f,dt*2);float upcoming;
                if(FootPlanner.TargetHeight(s,future,out upcoming))target=Mathf.Min(target,upcoming);
            }
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
        {var s=Find(v);if(s==null)return "uninitialized";return "ground="+s.Grounded+" normal="+s.Normal+" targetY="+s.TargetY+" left="+s.Feet[0].Position+" planted="+s.Feet[0].Planted+" right="+s.Feet[1].Position+" planted="+s.Feet[1].Planted+" lengths="+s.Shape.Upper+"/"+s.Shape.Lower+" reserve=5% queries="+s.Queries+" walking="+s.WalkReason+" recovery="+s.Recovering+" cautious="+s.Cautious+" speedCap="+s.DriveCap+" stop="+s.StopReason+" lastFailure="+s.Failure+" swingAge="+s.Feet[0].Age+"/"+s.Feet[1].Age;}
        public static void Forget(EntityVehicle v){states.Remove(v.entityId);GroundNet.Forget(v);Traversal.Forget(v);}
        public static void Cleanup(World world)
        {
            expired.Clear();foreach(var pair in states)if(pair.Value.Vehicle==null||world.GetEntity(pair.Key)!=pair.Value.Vehicle)expired.Add(pair.Key);
            foreach(int id in expired){var v=states[id].Vehicle;if(!ReferenceEquals(v,null))Model.Forget(v);else states.Remove(id);}
        }
        public static void Clear(){states.Clear();GroundNet.Clear();Traversal.Clear();DrawProbes=false;}
    }
}
