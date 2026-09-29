using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
namespace PZAEC.M1
{
    public static class Chassis
    {
        public struct Frame { public int Wheels,Tracks; public float Force,PitchTorque,Speed,Pitch; public bool Supported; public ChassisRules.Stage Phase; public ChassisRules.Exit Exit; }
        sealed class State
        {
            public WheelCollider[] Wheels;
            public WheelHit[] Contacts;
            public bool[] Grounded;
            public readonly RaycastHit[] Rays=new RaycastHit[32];
            public readonly Collider[] Overlaps=new Collider[32];
            public ChassisRules.Attempt Attempt=new ChassisRules.Attempt();
            public Vector3 Start,Heading,LastPosition;
            public float LastTime=-100,YawTarget;
            public readonly Vector3[] Landing=new Vector3[2];
            public float Height,Edge,GroundY,LastLog;
            public bool Reverse;
            public TrackContacts Tracks;
            public Frame Frame;
            public int Driver=-1;
            public State(EntityVehicle v){Wheels=v.vehicleRB.GetComponentsInChildren<WheelCollider>(true);Contacts=new WheelHit[Wheels.Length];Grounded=new bool[Wheels.Length];Tracks=v.vehicleRB.GetComponent<TrackContacts>()??v.vehicleRB.gameObject.AddComponent<TrackContacts>();}
        }
        static readonly ConditionalWeakTable<EntityVehicle,State> states=new ConditionalWeakTable<EntityVehicle,State>();
        public static bool ContactTractionEnabled=true;
        public static bool Diagnostics=false;
        public static Frame ReadFrame(EntityVehicle v){return states.TryGetValue(v,out var s)?s.Frame:default(Frame);}

        public static void BuildLower(Transform parent,int layer)
        {
            // Keep the original width, middle belly and upper envelope. Only
            // bevel the overhangs so contact can slide up a curb instead of snagging.
            var contour=new[]{new Vector2(-2.45f,.285f),new Vector2(2.45f,.285f),new Vector2(3.43f,.82f),new Vector2(3.43f,1.035f),new Vector2(-3.47f,1.035f),new Vector2(-3.47f,.82f)};
            var material=new PhysicMaterial("M1BellySlide"){dynamicFriction=.15f,staticFriction=.2f,bounciness=0,frictionCombine=PhysicMaterialCombine.Minimum,bounceCombine=PhysicMaterialCombine.Minimum};
            Prism(parent,"M1Lower",layer,-1.525f,1.525f,contour,material);
            // Four native wheel rays leave the middle of each visible track
            // unsupported by collision. Continuous, bevelled track envelopes
            // catch narrow crosswise curbs without filling the central belly gap.
            var track=new[]{new Vector2(-2.45f,.035f),new Vector2(2.35f,.035f),new Vector2(3.36f,.64f),new Vector2(3.36f,1.20f),new Vector2(-3.36f,1.20f),new Vector2(-3.36f,.60f)};
            Prism(parent,"M1TrackContactL",layer,-1.64f,-1.10f,track,material);
            Prism(parent,"M1TrackContactR",layer,1.09f,1.63f,track,material);
        }

        static void Prism(Transform parent,string name,int layer,float left,float right,Vector2[] contour,PhysicMaterial material)
        {
            int n=contour.Length;var vertices=new Vector3[n*2];var triangles=new List<int>();
            for(int side=0;side<2;side++)for(int i=0;i<n;i++)vertices[side*n+i]=new Vector3(side==0?left:right,contour[i].y,contour[i].x);
            for(int i=1;i<n-1;i++)triangles.AddRange(new[]{0,i,i+1,n,n+i+1,n+i});
            for(int i=0;i<n;i++){int j=(i+1)%n;triangles.AddRange(new[]{i,i+n,j,j,i+n,j+n});}
            var mesh=new Mesh{name=name};mesh.vertices=vertices;mesh.triangles=triangles.ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();
            var go=new GameObject(name);go.layer=layer;go.transform.SetParent(parent,false);
            var collider=go.AddComponent<MeshCollider>();collider.sharedMesh=mesh;collider.convex=true;
            collider.sharedMaterial=material;
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
        static bool Space(State s,EntityVehicle v,Vector3 center,Vector3 direction)
        {
            int count=Physics.OverlapBoxNonAlloc(center,new Vector3(1.7f,1.18f,.65f),s.Overlaps,Quaternion.LookRotation(direction),Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            if(count==s.Overlaps.Length)return false;
            for(int i=0;i<count;i++)if(!Own(s.Overlaps[i],v))return false;
            return true;
        }
        static bool Step(State s,EntityVehicle v,Vector3 forward,Vector3 right,float groundY,bool reverse)
        {
            float low=float.PositiveInfinity,high=0,nearest=float.PositiveInfinity,farthest=0;
            for(int index=0;index<2;index++){
                int lane=index==0?-1:1;
                var origin=v.vehicleRB.position+right*(lane*1.2f)+forward*2.2f;origin.y=groundY+.10f;
                if(!Ray(s,v,origin,forward,1.8f,out var face)||Vector3.Dot(face.normal,-forward)<.94f)return false;
                nearest=Mathf.Min(nearest,face.distance);farthest=Mathf.Max(farthest,face.distance);
                var near=face.point+forward*.35f;near.y=groundY+ChassisRules.HeightLimit(reverse)+.12f;
                if(!Ray(s,v,near,Vector3.down,.8f,out var top))return false;
                var far=near+forward*1.2f;
                if(!Ray(s,v,far,Vector3.down,.8f,out var landing))return false;
                float h=top.point.y-groundY;
                if(!ChassisRules.Landing(h,landing.point.y-groundY,Mathf.Min(top.normal.y,landing.normal.y))||h>ChassisRules.HeightLimit(reverse))return false;
                low=Mathf.Min(low,h);high=Mathf.Max(high,h);s.Landing[index]=top.point;
            }
            if(high-low>.20f||farthest-nearest>.85f)return false;
            var center=v.vehicleRB.position+forward*(2.2f+farthest+.8f);center.y=groundY+high+1.3f;
            if(!Space(s,v,center,forward))return false;
            s.Height=high;s.GroundY=groundY;s.Edge=2.2f+farthest;s.Reverse=reverse;
            return true;
        }
        static bool Continue(State s,EntityVehicle v)
        {
            // Revalidate the actual landing without needing to see the old vertical face.
            // Project lateral drift onto the cached lanes; a turn away invalidates the attempt.
            var lateral=Vector3.ProjectOnPlane(v.vehicleRB.position-s.Start,s.Heading);lateral.y=0;
            if(lateral.magnitude>.45f)return false;
            for(int i=0;i<2;i++)for(int j=0;j<2;j++){
                var at=s.Landing[i]+s.Heading*(j*1.2f)+lateral;
                if(!Ray(s,v,at+Vector3.up*.25f,Vector3.down,.50f,out var h)||Mathf.Abs(h.point.y-at.y)>.12f||h.normal.y<.9f)return false;
            }
            var center=(s.Landing[0]+s.Landing[1])*.5f+s.Heading*.6f+lateral;center.y=s.GroundY+s.Height+1.3f;
            return Space(s,v,center,s.Heading);
        }

        public static void Update(EntityVehicle __instance)
        {
            var v=__instance;if(!Weapons.IsTank(v)||v.vehicleRB==null)return;
            var s=states.GetValue(v,key=>new State(key));var rb=v.vehicleRB;
            s.Frame=default(Frame);
            bool owner=!v.isEntityRemote&&v.RBActive&&!rb.isKinematic;
            int driver=v.GetAttached(0)?.entityId??-1;
            // Owner/driver handoff, origin shift, teleport and sleep invalidate
            // the attempt; samples are rebuilt every eligible physics step.
            if(!owner||driver!=s.Driver||Time.time-s.LastTime>.15f||(rb.position-s.LastPosition).sqrMagnitude>25){s.Attempt=new ChassisRules.Attempt();s.Tracks.Clear();s.Start=rb.position;s.Heading=Vector3.zero;s.YawTarget=0;}
            s.Driver=driver;s.LastTime=Time.time;s.LastPosition=rb.position;
            if(!owner){s.Tracks.SetDriving(false);return;}
            float speed=rb.velocity.magnitude;
            // Native steering was already calculated in PhysicsFixedUpdate.
            float limit=ChassisRules.SteeringLimit(speed);
            foreach(var wheel in s.Wheels)if(wheel!=null)wheel.steerAngle=Mathf.Clamp(wheel.steerAngle,-limit,limit);
            bool left=false,rightContact=false;float groundY=0,minZ=float.PositiveInfinity,maxZ=float.NegativeInfinity;int contacts=0;
            for(int i=0;i<s.Wheels.Length;i++){
                var wheel=s.Wheels[i];s.Grounded[i]=wheel!=null&&wheel.enabled&&wheel.GetGroundHit(out s.Contacts[i])&&Ground(s.Contacts[i].collider)&&s.Contacts[i].normal.y>.5f&&s.Contacts[i].force>1;
                if(!s.Grounded[i])continue;
                if(rb.transform.InverseTransformPoint(wheel.transform.position).x<0)left=true;else rightContact=true;
                groundY+=s.Contacts[i].point.y;contacts++;
                float z=rb.transform.InverseTransformPoint(s.Contacts[i].point).z;minZ=Mathf.Min(minZ,z);maxZ=Mathf.Max(maxZ,z);
            }
            int trackCount=ContactTractionEnabled&&s.Tracks.Fresh?s.Tracks.Count:0;
            for(int i=0;i<trackCount;i++){
                var h=s.Tracks.Samples[i];var at=rb.transform.InverseTransformPoint(h.Point);
                if(at.x<0)left=true;else rightContact=true;minZ=Mathf.Min(minZ,at.z);maxZ=Mathf.Max(maxZ,at.z);
                groundY+=h.Point.y;
            }
            int supportCount=contacts+trackCount;
            var body=rb.rotation;var forward=body*Vector3.forward;var right=body*Vector3.right;
            float pitch=Mathf.Asin(Mathf.Clamp(forward.y,-1,1))*Mathf.Rad2Deg,roll=Mathf.Asin(Mathf.Clamp(right.y,-1,1))*Mathf.Rad2Deg;
            bool running=v.movementInput!=null&&v.hasDriver&&v.IsEngineRunning&&v.vehicle.GetHealth()>0&&v.timeInWater<=0&&(v.vehicle.GetFuelLevel()>0||EntityVehicle.VehicleFuelUsageModifier==0);
            bool supported=left&&rightContact&&Vector3.Dot(body*Vector3.up,Vector3.up)>.8f;
            // A rear axle may still pull during breakover. A narrow support line
            // permits real-contact traction, but never adds a pitching moment.
            bool pitchSupported=supported&&maxZ-minZ>.6f;
            s.Frame.Wheels=contacts;s.Frame.Tracks=trackCount;s.Frame.Supported=supported;
            // Damp rocking only while supported, without forcing a level pose.
            if(supported&&v.timeInWater<=0){
                var rock=right*Vector3.Dot(rb.angularVelocity,right)+forward*Vector3.Dot(rb.angularVelocity,forward);
                rb.AddTorque(Vector3.ClampMagnitude(-rock*.8f,.4f),ForceMode.Acceleration);
            }
            if(running&&supportCount>0){
                var spec=Weapons.Spec(v);float damaged=v.vehicle.GetHealthPercent()<.3f?.7f:1;
                if(damaged<1){
                    var horizontal=new Vector3(rb.velocity.x,0,rb.velocity.z);
                    float cap=(Vector3.Dot(horizontal,forward)<0?spec.Reverse:v.vehicle.IsTurbo?spec.Turbo:spec.Forward)*ModuleRules.Speed(Modules.Get(v))*damaged;
                    // Preserve the existing damaged-vehicle speed cap; this is
                    // separate from step assistance, which only applies forces.
                    if(horizontal.magnitude>cap){horizontal=horizontal.normalized*cap;rb.velocity=new Vector3(horizontal.x,rb.velocity.y,horizontal.z);}
                }
                if(supported&&Mathf.Abs(roll)<12){
                    float target=ChassisRules.YawRate(speed,Vector3.Dot(rb.velocity,forward),v.movementInput.moveForward,v.movementInput.moveStrafe,spec.Turn,damaged);
                    s.YawTarget=Mathf.MoveTowards(s.YawTarget,target,60*Mathf.Deg2Rad*Time.fixedDeltaTime);
                    rb.AddTorque(Vector3.up*ChassisRules.YawAcceleration(s.YawTarget,rb.angularVelocity.y),ForceMode.Acceleration);
                }
                else s.YawTarget=0;
            }
            else s.YawTarget=0;
            float throttle=v.movementInput==null?0:v.movementInput.moveForward,steer=v.movementInput==null?0:v.movementInput.moveStrafe;
            bool reverse=throttle<0;
            bool eligible=running&&ChassisRules.CanAssist(speed,throttle,steer,pitch,roll,supported,v.wheelBrakes>.01f);
            // A small speed overshoot drops force to zero, not the entire obstacle
            // memory. The original 8/4 km/h force cutoffs still apply below.
            if(s.Attempt.Active)eligible=running&&ChassisRules.CanContinue(speed,throttle,steer,pitch,roll,supported,v.wheelBrakes>.01f);
            var flat=Vector3.ProjectOnPlane(forward,Vector3.up).normalized*(reverse?-1:1);
            float travel=Vector3.Dot(rb.position-s.Start,s.Heading);
            s.Attempt.Retreat(travel);
            if(s.Attempt.Active&&(reverse!=s.Reverse||Vector3.Dot(flat,s.Heading)<.94f))eligible=false;
            if(!s.Attempt.Active&&!s.Attempt.Blocked&&eligible&&Step(s,v,flat,Vector3.Cross(Vector3.up,flat),groundY/Mathf.Max(1,supportCount),reverse)){
                s.Start=rb.position;s.Heading=flat;travel=0;s.Attempt.Begin((s.Edge+2.6f)/Mathf.Max(1,speed)+1);
            }
            bool climbing=s.Attempt.Advance(eligible,eligible&&s.Attempt.Active&&Continue(s,v),Time.fixedDeltaTime,travel,s.Edge);
            s.Frame.Phase=s.Attempt.Phase;
            s.Frame.Exit=s.Attempt.Reason;s.Frame.Speed=speed;s.Frame.Pitch=pitch;
            // Slope/middle-track traction cannot bypass a failed or incomplete step attempt.
            bool slope=eligible&&!s.Attempt.Active&&!s.Attempt.Blocked&&Mathf.Abs(pitch)>3&&Mathf.Abs(pitch)<=25;
            bool middle=eligible&&!s.Attempt.Active&&!s.Attempt.Blocked&&trackCount>0&&contacts<3;
            if(Diagnostics&&Time.time-s.LastLog>=.5f){s.LastLog=Time.time;Log.Out("[M1-Chassis] id="+v.entityId+" stage="+s.Attempt.Phase+" wheels="+contacts+" tracks="+trackCount+" speed="+speed.ToString("F2")+" travel="+travel.ToString("F2")+" active="+(climbing||slope||middle));}
            if(!climbing&&!slope&&!middle){s.Tracks.SetDriving(false);return;}
            float fade=ChassisRules.TractionScale(speed,steer,roll,reverse)*(v.vehicle.GetHealthPercent()<.3f?.7f:1);
            s.Tracks.SetDriving(ContactTractionEnabled&&fade>0&&trackCount>0);
            float share=rb.mass*.8f/Mathf.Max(1,supportCount)*fade;
            for(int i=0;i<s.Wheels.Length;i++)if(s.Grounded[i]){
                var h=s.Contacts[i];var tangent=Vector3.ProjectOnPlane(flat,h.normal).normalized;
                float force=Mathf.Min(share,h.force*.15f*fade);s.Frame.Force+=force;
                rb.AddForceAtPosition(tangent*force,h.point,ForceMode.Force);
            }
            for(int i=0;i<trackCount;i++){
                var h=s.Tracks.Samples[i];s.Frame.Force+=share;rb.AddForceAtPosition(Vector3.ProjectOnPlane(flat,h.Normal).normalized*share,h.Point,ForceMode.Force);
            }
            // Once the leading axle reaches the edge, allow the nose to settle
            // and load the front wheels. Continuing to lift it unloads them.
            if(climbing&&pitchSupported&&s.Attempt.Phase==ChassisRules.Stage.Approach){
                float direction=reverse?-1:1;
                float targetPitch=Mathf.Min(10,Mathf.Atan2(s.Height,3.5f)*Mathf.Rad2Deg);
                float noseUp=Mathf.Clamp((targetPitch-pitch*direction)*Mathf.Deg2Rad*1.5f+Vector3.Dot(rb.angularVelocity,right)*direction*.6f,0,.25f);
                rb.AddTorque(-right*direction*noseUp*fade,ForceMode.Acceleration);
                s.Frame.PitchTorque=noseUp*fade;
            }
        }
    }
}
