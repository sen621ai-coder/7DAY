using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
namespace PZAEC.M1
{
    public static class Chassis
    {
        sealed class State
        {
            public WheelCollider[] Wheels;
            public WheelHit[] Contacts;
            public bool[] Grounded;
            public readonly RaycastHit[] Rays=new RaycastHit[32];
            public readonly Collider[] Overlaps=new Collider[32];
            public ChassisRules.Attempt Attempt=new ChassisRules.Attempt();
            public Vector3 Start,Heading,LastPosition;
            public float LastTime=-100;
            public int Driver=-1;
            public State(EntityVehicle v){Wheels=v.vehicleRB.GetComponentsInChildren<WheelCollider>(true);Contacts=new WheelHit[Wheels.Length];Grounded=new bool[Wheels.Length];}
        }
        static readonly ConditionalWeakTable<EntityVehicle,State> states=new ConditionalWeakTable<EntityVehicle,State>();

        public static void BuildLower(Transform parent,int layer)
        {
            // Keep the original width, middle belly and upper envelope. Only
            // bevel the overhangs so contact can slide up a curb instead of snagging.
            var contour=new[]{new Vector2(-2.45f,.285f),new Vector2(2.45f,.285f),new Vector2(3.43f,.82f),new Vector2(3.43f,1.035f),new Vector2(-3.47f,1.035f),new Vector2(-3.47f,.82f)};
            var vertices=new Vector3[12];var triangles=new List<int>();
            for(int side=0;side<2;side++)for(int i=0;i<6;i++)vertices[side*6+i]=new Vector3(side==0?-1.525f:1.525f,contour[i].y,contour[i].x);
            for(int i=1;i<5;i++){triangles.AddRange(new[]{0,i,i+1,6,6+i+1,6+i});}
            for(int i=0;i<6;i++){int j=(i+1)%6;triangles.AddRange(new[]{i,i+6,j,j,i+6,j+6});}
            var mesh=new Mesh{name="M1LowerBeveled"};mesh.vertices=vertices;mesh.triangles=triangles.ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();
            var go=new GameObject("M1Lower");go.layer=layer;go.transform.SetParent(parent,false);
            var collider=go.AddComponent<MeshCollider>();collider.sharedMesh=mesh;collider.convex=true;
            collider.sharedMaterial=new PhysicMaterial("M1BellySlide"){dynamicFriction=.15f,staticFriction=.2f,bounciness=0,frictionCombine=PhysicMaterialCombine.Minimum,bounceCombine=PhysicMaterialCombine.Minimum};
        }

        static bool Own(Collider c,EntityVehicle v)
        {
            if(c.attachedRigidbody==v.vehicleRB||c.transform.IsChildOf(v.transform))return true;
            var entity=c.GetComponentInParent<Entity>();return entity!=null&&(entity==v||entity==v.GetAttached(0)||entity==v.GetAttached(1));
        }
        static bool Ground(Collider c)=>c!=null&&c.attachedRigidbody==null&&c.GetComponentInParent<Entity>()==null;
        static bool Ray(State s,EntityVehicle v,Vector3 start,Vector3 direction,float distance,out RaycastHit hit)
        {
            hit=default(RaycastHit);int count=Physics.RaycastNonAlloc(start,direction,s.Rays,distance,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            if(count==s.Rays.Length)return false;float nearest=float.PositiveInfinity;
            for(int i=0;i<count;i++){var h=s.Rays[i];if(h.collider==null||Own(h.collider,v)||h.distance>=nearest)continue;hit=h;nearest=h.distance;}
            // Entities and dynamic props obstruct a probe; never skip through them
            // to accept terrain behind them as a supporting surface.
            return nearest<float.PositiveInfinity&&Ground(hit.collider);
        }
        static bool Step(State s,EntityVehicle v,Vector3 forward,Vector3 right,float groundY,out float height)
        {
            height=0;float nearest=float.PositiveInfinity,farthest=0;
            // Both track lanes plus the hull centre must agree on a finite step.
            for(int lane=-1;lane<=1;lane++){
                var origin=v.vehicleRB.position+right*(lane*1.2f)+forward*2.2f;origin.y=groundY+.10f;
                if(!Ray(s,v,origin,forward,1.8f,out var face)||Vector3.Dot(face.normal,-forward)<.85f)return false;
                nearest=Mathf.Min(nearest,face.distance);farthest=Mathf.Max(farthest,face.distance);
                var near=face.point+forward*.35f;near.y=groundY+ChassisRules.StepHeight+.12f;
                if(!Ray(s,v,near,Vector3.down,.8f,out var top))return false;
                var far=near+forward*1.2f;
                if(!Ray(s,v,far,Vector3.down,.8f,out var landing))return false;
                float h=top.point.y-groundY;
                if(!ChassisRules.Landing(h,landing.point.y-groundY,Mathf.Min(top.normal.y,landing.normal.y)))return false;
                if(lane>-1&&Mathf.Abs(h-height)>.12f)return false;height=h;
            }
            if(farthest-nearest>.35f)return false;
            // Reject overhangs/walls above the landing. Existing body colliders
            // remain enabled throughout the attempt and provide final collision.
            var center=v.vehicleRB.position+forward*(2.2f+nearest+.8f);center.y=groundY+height+1.3f;
            int count=Physics.OverlapBoxNonAlloc(center,new Vector3(1.7f,1.18f,.65f),s.Overlaps,Quaternion.LookRotation(forward),Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            if(count==s.Overlaps.Length)return false;
            for(int i=0;i<count;i++)if(!Own(s.Overlaps[i],v))return false;
            return true;
        }

        public static void Update(EntityVehicle __instance)
        {
            var v=__instance;if(!Weapons.IsTank(v)||v.vehicleRB==null)return;
            var s=states.GetValue(v,key=>new State(key));var rb=v.vehicleRB;
            bool owner=!v.isEntityRemote&&v.RBActive&&!rb.isKinematic;
            int driver=v.GetAttached(0)?.entityId??-1;
            // Owner/driver handoff, origin shift, teleport and sleep invalidate
            // the attempt; samples are rebuilt every eligible physics step.
            if(!owner||driver!=s.Driver||Time.time-s.LastTime>.15f||(rb.position-s.LastPosition).sqrMagnitude>25){s.Attempt=new ChassisRules.Attempt();s.Start=rb.position;s.Heading=Vector3.zero;}
            s.Driver=driver;s.LastTime=Time.time;s.LastPosition=rb.position;
            if(!owner)return;
            float speed=rb.velocity.magnitude;
            // Native steering was already calculated in PhysicsFixedUpdate.
            float limit=ChassisRules.SteeringLimit(speed);
            foreach(var wheel in s.Wheels)if(wheel!=null)wheel.steerAngle=Mathf.Clamp(wheel.steerAngle,-limit,limit);
            bool left=false,rightContact=false;float groundY=0;int contacts=0;
            for(int i=0;i<s.Wheels.Length;i++){
                var wheel=s.Wheels[i];s.Grounded[i]=wheel!=null&&wheel.enabled&&wheel.GetGroundHit(out s.Contacts[i])&&Ground(s.Contacts[i].collider)&&s.Contacts[i].normal.y>.5f&&s.Contacts[i].force>1;
                if(!s.Grounded[i])continue;
                if(rb.transform.InverseTransformPoint(wheel.transform.position).x<0)left=true;else rightContact=true;
                groundY+=s.Contacts[i].point.y;contacts++;
            }
            var body=rb.rotation;var forward=body*Vector3.forward;var right=body*Vector3.right;
            float pitch=Mathf.Asin(Mathf.Clamp(forward.y,-1,1))*Mathf.Rad2Deg,roll=Mathf.Asin(Mathf.Clamp(right.y,-1,1))*Mathf.Rad2Deg;
            bool running=v.movementInput!=null&&v.hasDriver&&v.IsEngineRunning&&v.vehicle.GetHealth()>0&&v.timeInWater<=0&&(v.vehicle.GetFuelLevel()>0||EntityVehicle.VehicleFuelUsageModifier==0);
            bool supported=left&&rightContact&&Vector3.Dot(body*Vector3.up,Vector3.up)>.8f;
            // Damp rocking only while supported, without forcing a level pose.
            if(supported&&v.timeInWater<=0){
                var rock=right*Vector3.Dot(rb.angularVelocity,right)+forward*Vector3.Dot(rb.angularVelocity,forward);
                rb.AddTorque(Vector3.ClampMagnitude(-rock*.8f,.4f),ForceMode.Acceleration);
            }
            if(running&&contacts>0){
                var spec=Weapons.Spec(v);float damaged=v.vehicle.GetHealthPercent()<.3f?.7f:1;
                if(damaged<1){
                    var horizontal=new Vector3(rb.velocity.x,0,rb.velocity.z);
                    float cap=(Vector3.Dot(horizontal,forward)<0?spec.Reverse:v.vehicle.IsTurbo?spec.Turbo:spec.Forward)*damaged;
                    // Preserve the existing damaged-vehicle speed cap; this is
                    // separate from step assistance, which only applies forces.
                    if(horizontal.magnitude>cap){horizontal=horizontal.normalized*cap;rb.velocity=new Vector3(horizontal.x,rb.velocity.y,horizontal.z);}
                }
                if(supported&&Mathf.Abs(roll)<12){
                    float target=v.movementInput.moveStrafe*spec.Turn*Mathf.Deg2Rad*damaged;
                    rb.AddTorque(Vector3.up*Mathf.Clamp((target-rb.angularVelocity.y)*2,-.8f,.8f)*Mathf.Clamp01(1-speed/4),ForceMode.Acceleration);
                }
            }
            bool eligible=running&&ChassisRules.CanAssist(speed,v.movementInput.moveForward,v.movementInput.moveStrafe,pitch,roll,supported,v.wheelBrakes>.01f);
            float height=0;var flat=Vector3.ProjectOnPlane(forward,Vector3.up).normalized;
            eligible=eligible&&Step(s,v,flat,Vector3.Cross(Vector3.up,flat),groundY/Mathf.Max(1,contacts),out height);
            if(!s.Attempt.Active&&!s.Attempt.Blocked){s.Start=rb.position;s.Heading=flat;}
            if(!s.Attempt.Tick(eligible,Time.fixedDeltaTime,Vector3.Dot(rb.position-s.Start,s.Heading)))return;
            float fade=Mathf.Clamp01((ChassisRules.AssistSpeed-speed)/.6f)*(v.vehicle.GetHealthPercent()<.3f?.7f:1);
            // Small, load-limited traction at REAL wheel contacts. No extra
            // support rays, upward force, position writes or airborne assistance.
            for(int i=0;i<s.Wheels.Length;i++)if(s.Grounded[i]){
                var h=s.Contacts[i];var tangent=Vector3.ProjectOnPlane(flat,h.normal).normalized;
                rb.AddForceAtPosition(tangent*Mathf.Min(rb.mass*.8f/contacts,h.force*.15f)*fade,h.point,ForceMode.Force);
            }
            float targetPitch=Mathf.Min(10,Mathf.Atan2(height,3.5f)*Mathf.Rad2Deg);
            float noseUp=Mathf.Clamp((targetPitch-pitch)*Mathf.Deg2Rad*1.5f+Vector3.Dot(rb.angularVelocity,right)*.6f,0,.25f);
            rb.AddTorque(-right*noseUp*fade,ForceMode.Acceleration);
        }
    }
}
