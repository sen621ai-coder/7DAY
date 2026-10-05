using UnityEngine;

namespace PZAEC.Mecha
{
    // Complete Form flight. Hull physics stays upright; banking is visual only.
    public static class Flight
    {
        public enum Phase { Ground, Takeoff, Cruise, Landing, PowerLost }
        public static bool Active(Locomotion.MoveState s)
        { return s.FlightMode==Phase.Takeoff||s.FlightMode==Phase.Cruise||s.FlightMode==Phase.Landing; }
        public static bool AirPose(Locomotion.MoveState s)
        { return Active(s)||(s.FlightMode==Phase.PowerLost&&!s.Grounded); }
        public static string Status(Locomotion.MoveState s)
        { return s.FlightMode==Phase.Takeoff?"起飞展翼":s.FlightMode==Phase.Landing?"降落":s.FlightMode==Phase.PowerLost?"动力中断":s.FlightMode==Phase.Cruise?(Mathf.Abs(s.VerticalInput)<.01f?"飞行 / 定高":"飞行 / 升降"):"地面 / 推进"; }
        // Absolute world Y is deliberately used: floating-origin shifts cannot alter the held altitude.
        public static void Advance(Locomotion.MoveState s,bool powered,bool toggle,bool grounded,float vertical,float worldY,float dt)
        {
            if(!powered&&Active(s)){s.FlightMode=Phase.PowerLost;s.Boost=false;s.VerticalInput=0;s.ControlledLanding=false;}
            if(s.FlightMode==Phase.PowerLost&&grounded)s.FlightMode=Phase.Ground;
            if(toggle&&powered)
            {
                if(!Active(s)){
                    s.FlightMode=grounded?Phase.Takeoff:Phase.Cruise;s.FlightAge=0;
                    s.HoldY=worldY+(grounded?Rules.FlightTakeoffHeight:0);s.ContactTime=0;
                    s.Charge=0;s.ChargeStart=-1;s.JumpWasHeld=false;s.ControlledLanding=true;
                }
                else if(s.FlightMode==Phase.Landing){s.FlightMode=Phase.Cruise;s.HoldY=worldY;s.ContactTime=0;}
                else {s.FlightMode=Phase.Landing;s.ContactTime=0;}
            }
            if(!Active(s)){s.VerticalInput=0;return;}
            s.FlightAge+=dt;
            if(s.FlightMode==Phase.Takeoff&&s.FlightAge>=Rules.FlightDeploySeconds)s.FlightMode=Phase.Cruise;
            if(s.FlightMode==Phase.Takeoff)s.VerticalInput=0;
            else s.VerticalInput=s.FlightMode==Phase.Landing?-1:Mathf.Clamp(vertical,-1,1);
            bool touching=grounded&&(s.FlightMode==Phase.Landing||(s.FlightMode==Phase.Cruise&&s.VerticalInput<0));
            s.ContactTime=touching?s.ContactTime+dt:0;
            if(s.ContactTime>=Rules.FlightContactSeconds){s.FlightMode=Phase.Ground;s.Boost=false;s.VerticalInput=0;s.Jump=false;s.JumpWasHeld=false;}
        }
        public static bool Step(EntityVehicle v,Locomotion.MoveState s,bool grounded,bool input,float dt)
        {
            var rb=v.vehicleRB;var driver=v.GetAttached(0);
            int actor=driver!=null?driver.entityId:-1;
            bool changed=(s.FlightActor>=0&&s.FlightActor!=actor)||(s.FlightActor<0&&s.Actor>=0&&s.Actor!=actor&&Active(s));
            if(changed){s.FlightMode=grounded?Phase.Ground:Phase.PowerLost;s.Toggle=s.Jump=s.Descend=s.JumpWasHeld=s.InputReady=false;s.ChargeStart=-1;s.Charge=0;s.ControlledLanding=false;input=false;}
            s.FlightActor=actor;
            bool powered=Locomotion.Powered(v)&&driver!=null&&!driver.IsDead()&&v.timeInWater<=0&&!Boarding.Active(v);
            float vertical=input?((s.Jump?1:0)-(s.Descend?1:0)):0;
            bool wasActive=Active(s);
            Advance(s,powered,input&&s.Toggle,grounded,vertical,v.position.y,dt);s.Toggle=false;s.HoverOn=false;
            if(grounded&&!s.Grounded&&wasActive&&s.ControlledLanding){s.LandingAt=Time.time;s.AirSince=-1;s.LandingEventExpected=false;}
            if(!Active(s))return false;
            Locomotion.PrepareSupport(s.Wheels,true);
            if(!grounded&&s.Grounded){s.AirSince=Time.time;s.JumpAt=Time.time;s.LandingEventExpected=false;}
            s.Grounded=grounded;s.Charge=0;s.ChargeStart=-1;s.JumpWasHeld=s.Jump;
            var movement=v.movementInput;
            float throttle=input&&movement!=null?movement.moveForward:0,steer=input&&movement!=null?movement.moveStrafe:0;
            s.Boost=s.FlightMode==Phase.Cruise&&input&&throttle>.1f&&v.vehicle.IsTurbo;
            float target=throttle>=0?throttle*(s.Boost?Rules.FlightBoostSpeed:Rules.FlightSpeed):throttle*Rules.FlightReverseSpeed;
            if(s.FlightMode==Phase.Takeoff){target=0;steer=0;}
            if(s.FlightMode==Phase.Landing)target=Mathf.Clamp(target,-2,2);
            float clearance; s.FlightHeight=Clearance(v,out clearance)?clearance:-1;
            float verticalSpeed=0;
            if(s.FlightMode==Phase.Takeoff){verticalSpeed=0;}
            else if(s.FlightMode==Phase.Landing)verticalSpeed=LandingSpeed(s.FlightHeight);
            else if(Mathf.Abs(s.VerticalInput)>.01f)verticalSpeed=s.VerticalInput>0?s.VerticalInput*Rules.FlightRiseSpeed:s.VerticalInput*Rules.FlightDescendSpeed;
            if(verticalSpeed!=0)s.HoldY=v.position.y;
            float roofY=Weapons.Trace(v,v.position+Vector3.up*2.6f,Vector3.up,6f,out var roof)?roof.hit.pos.y:float.PositiveInfinity;
            float desiredY=VerticalTarget(s,v.position.y,verticalSpeed,roofY);
            ApplyControl(rb,target,steer,s.Boost,desiredY,dt);
            if(EntityVehicle.VehicleFuelUsageModifier!=0)v.vehicle.SetFuelLevel(Mathf.Max(0,v.vehicle.GetFuelLevel()-(s.Boost?Rules.FlightBoostFuel:Rules.FlightFuel)*dt));
            Locomotion.Sync(v,s);return true;
        }
        public static float LandingSpeed(float clearance){return clearance>=0&&clearance<3?-.8f:-2f;}
        public static bool HullSupported(Rigidbody rb)
        {
            var support=GroundSupport.Find(rb);
            return support!=null&&GroundSupport.Observe(support.Vehicle).Grounded;
        }
        public static float VerticalTarget(Locomotion.MoveState s,float worldY,float command,float roofY)
        {
            float target=s.FlightMode==Phase.Takeoff?0:command!=0?command:Mathf.Clamp((s.HoldY-worldY)*3,-Rules.FlightDescendSpeed,Rules.FlightRiseSpeed);
            if(!float.IsPositiveInfinity(roofY)){
                float cap=roofY-3.35f;s.HoldY=Mathf.Min(s.HoldY,cap);
                target=Mathf.Min(target,Mathf.Clamp((cap-worldY)*3,-Rules.FlightDescendSpeed,0));
            }
            return target;
        }
        public static bool Clearance(EntityVehicle v,out float height)
        {
            if(Weapons.Trace(v,v.position+Vector3.up*.2f,Vector3.down,160f,out var hit)){height=Mathf.Max(0,v.position.y-hit.hit.pos.y);return true;}
            height=-1;return false;
        }
        // Same controller is used by isolated PhysX acceptance tests.
        public static void ApplyControl(Rigidbody rb,float target,float steer,bool boost,float verticalSpeed,float dt)
        {
            var forward=Vector3.ProjectOnPlane(rb.rotation*Vector3.forward,Vector3.up).normalized;
            var planar=Vector3.ProjectOnPlane(rb.velocity,Vector3.up);
            float cap=Mathf.Abs(target)>Mathf.Abs(Vector3.Dot(planar,forward))?Rules.FlightAcceleration:Rules.FlightBraking;
            var acceleration=Vector3.ClampMagnitude((forward*target-planar)/Mathf.Max(dt,.001f),cap);
            acceleration.y=-Physics.gravity.y+Mathf.Clamp((verticalSpeed-rb.velocity.y)*6,-Rules.FlightVerticalAcceleration,Rules.FlightVerticalAcceleration);
            // Cancel native Rigidbody drag, so the requested speeds and altitude remain exact.
            acceleration+=rb.velocity*rb.drag;
            rb.AddForce(acceleration*rb.mass,ForceMode.Force);
            float yaw=steer*(boost?30:45)*Mathf.Deg2Rad;
            var tilt=Vector3.Cross(rb.rotation*Vector3.up,Vector3.up);
            var rock=rb.angularVelocity-Vector3.up*rb.angularVelocity.y;
            Locomotion.AngularAcceleration(rb,Vector3.up*Mathf.Clamp((yaw-rb.angularVelocity.y)*6,-4,4)+Vector3.ClampMagnitude(tilt*10-rock*3,5));
        }
        public static void Pose(EntityVehicle v,Model.Rig r,float dt)
        {
            if(!Rules.Complete(v)||r.WingL==null||r.WingR==null)return;
            var s=Locomotion.Get(v);float deployed=s.WingBlend;
            // Remote bodies can be kinematic; Gait supplies their interpolated speed and turn.
            float along=s.VisualForward,turn=s.VisualTurn;
            float speed=Mathf.Clamp01(Mathf.Abs(along)/Rules.FlightSpeed);
            float cruise=s.FlightMode==Phase.Landing?0:speed;
            float bank=Mathf.Clamp(turn/45,-1,1)*12;
            float lean=s.FlightMode==Phase.Landing?0:(s.Boost?22:15)*Mathf.Clamp(along/Rules.FlightSpeed,-.35f,1);
            s.FlightBank=Mathf.MoveTowards(s.FlightBank,bank,dt*45);
            s.FlightLean=Mathf.MoveTowards(s.FlightLean,lean,dt*35);
            s.FlightSweep=Mathf.MoveTowards(s.FlightSweep,Mathf.Lerp(0,24,cruise)+(s.Boost?12:0),dt*60);
            if(deployed>.001f)r.Torso.localRotation=r.RestRot[r.Torso]*Quaternion.Euler(s.FlightLean*deployed,0,-s.FlightBank*deployed);
            float sweep=s.FlightSweep;
            // Folded long fins point back/down; unfolding rolls them outward about their real roots.
            r.WingL.localRotation=r.RestRot[r.WingL]*Quaternion.Euler(-8*deployed,(35-sweep)*deployed,(-58+s.FlightBank*.6f)*deployed);
            r.WingR.localRotation=r.RestRot[r.WingR]*Quaternion.Euler(-8*deployed,(-35+sweep)*deployed,(58+s.FlightBank*.6f)*deployed);
        }
    }
}
