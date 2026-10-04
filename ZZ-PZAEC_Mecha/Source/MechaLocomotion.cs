using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace PZAEC.Mecha
{
    public static class Locomotion
    {
        public sealed class MoveState
        {
            public EntityVehicle Vehicle;
            public bool HoverOn, Boost, JumpWasHeld, Grounded=true, Toggle, Descend, Jump, InputReady;
            public float ChargeStart=-1, Charge, NextJump, AirSince=-1, LastInput=-100, LastSync=-100, LastPacket=-100, LandingAt=-100, JumpAt=-100, LastTime, Blend;
            public float AirPeakDownSpeed,LandingStrength=.7f,LandingPendingUntil=-100;public bool LandingEventExpected;
            public Flight.Phase FlightMode;
            public float HoldY, FlightAge, ContactTime, VerticalInput, WingBlend, FlightHeight=-1, VisualForward, VisualTurn, FlightLean, FlightBank, FlightSweep, LastHeightAt=-100;
            public bool ControlledLanding;
            public int FlightActor=-1;
            public int Sequence, Actor=-1;
            public WheelCollider[] Wheels;
        }
        static readonly Dictionary<int,MoveState> moves=new Dictionary<int,MoveState>();
        public static MoveState Get(EntityVehicle v)
        { MoveState s; if(!moves.TryGetValue(v.entityId,out s)||s.Vehicle!=v) {s=new MoveState{Vehicle=v,Wheels=(v.vehicleRB!=null?v.vehicleRB.transform:v.transform).GetComponentsInChildren<WheelCollider>(true)}; moves[v.entityId]=s;} return s; }
        static MoveState Local { get { var w=GameManager.Instance!=null?GameManager.Instance.World:null;var p=w!=null?w.GetPrimaryPlayer():null;var v=p!=null?p.AttachedToEntity as EntityVehicle:null;return Weapons.IsMecha(v)?Get(v):null; } }
        public static bool HoverOn { get {return Local!=null&&Local.HoverOn;} }
        public static float JumpCooldownRemaining { get {return Local!=null?Mathf.Max(0,Local.NextJump-Time.time):0;} }
        public static float JumpCharge { get {return Local!=null?Local.Charge:0;} }
        public static void Install(Harmony h)
        { h.Patch(AccessTools.Method(typeof(EntityVehicle),"FixedUpdateForces"),postfix:new HarmonyMethod(typeof(Locomotion),nameof(AfterForces))); }
        public static void FeedInput(bool toggle,bool descend,bool jump)
        { var s=Local;if(s==null)return;s.Toggle|=toggle;s.Descend=descend;s.Jump=Boarding.FilterJump(jump);s.InputReady=true;s.LastInput=Time.time; }
        public static void ReleaseInput() {var s=Local;if(s==null)return;s.Toggle=s.Descend=s.Jump=s.JumpWasHeld=s.InputReady=false;s.ChargeStart=-1;s.Charge=0;}
        public static bool Powered(EntityVehicle v)
        {return v!=null&&v.hasDriver&&!v.IsDead()&&v.IsEngineRunning&&v.vehicle.GetHealth()>0&&(v.vehicle.GetFuelLevel()>0||EntityVehicle.VehicleFuelUsageModifier==0);}
        public static bool Receive(EntityVehicle v,int actor,int sequence,Vector3 state)
        {
            if(!Weapons.IsMecha(v)||!Weapons.Finite(state.x)||!Weapons.Finite(state.y)||!Weapons.Finite(state.z)||state.x<0||state.x>127||state.x!=(int)state.x||state.y<0||state.y>1||Mathf.Abs(state.z)>1)return false;
            int bits=(int)state.x;
            bool flight=(bits&8)!=0,landing=(bits&16)!=0,takeoff=(bits&32)!=0,fault=(bits&64)!=0;
            if((!Rules.Complete(v)&&bits>7)||(landing&&!flight)||(takeoff&&!flight)||(landing&&takeoff)||(flight&&fault)||((flight||fault)&&(bits&1)!=0)||(!flight&&state.z!=0)||((flight||fault)&&state.y!=0)||((landing||takeoff||fault)&&(bits&2)!=0))return false;
            var s=Get(v); if(s.Actor==actor&&sequence<=s.Sequence)return false;
            s.Actor=actor;s.Sequence=sequence;s.LastPacket=Time.time;
            // A local physics owner already has more recent input than its echo.
            if(!v.isEntityRemote&&!Weapons.Server)return true;
            int flags=(int)state.x;bool ground=(flags&4)!=0;
            if(!s.Grounded&&ground)MarkLanding(s,Time.time,!landing&&!takeoff&&!flight&&!Flight.Active(s));
            if(s.Grounded&&!ground){s.JumpAt=Time.time;s.AirSince=Time.time;s.AirPeakDownSpeed=0;s.LandingPendingUntil=-100;s.LandingEventExpected=false;}
            s.Grounded=ground;s.HoverOn=(flags&1)!=0;s.Boost=(flags&2)!=0;s.Charge=state.y;
            s.FlightMode=fault?Flight.Phase.PowerLost:landing?Flight.Phase.Landing:takeoff?Flight.Phase.Takeoff:flight?Flight.Phase.Cruise:Flight.Phase.Ground;
            s.VerticalInput=state.z;return true;
        }
        public static void Tick(World world)
        {
            var remove=new List<int>();
            foreach(var pair in moves){var s=pair.Value;if(s.Vehicle==null||world.GetEntity(pair.Key)!=s.Vehicle){remove.Add(pair.Key);continue;}
                if(s.Vehicle.isEntityRemote&&Time.time-s.LastPacket>1f){bool flying=Flight.AirPose(s);s.HoverOn=s.Boost=false;s.Charge=0;s.FlightMode=!s.Grounded&&flying?Flight.Phase.PowerLost:Flight.Phase.Ground;s.VerticalInput=0;}
                s.WingBlend=Mathf.MoveTowards(s.WingBlend,Flight.AirPose(s)?1:0,Time.deltaTime/(Flight.AirPose(s)?Rules.FlightDeploySeconds:1f));
                s.Blend=Mathf.MoveTowards(s.Blend,(s.HoverOn||s.Boost||Flight.Active(s))?1:0,Time.deltaTime/.35f);
            }
            foreach(int id in remove){var old=moves[id].Vehicle;if(old!=null){Gait.Forget(old);Model.Forget(old);}moves.Remove(id);}
        }
        public static void AfterForces(EntityVehicle __instance)
        {
            var v=__instance;if(!Weapons.IsMecha(v)||v.isEntityRemote)return;
            var rb=v.vehicleRB;if(rb==null||rb.isKinematic||!v.RBActive)return;
            var s=Get(v);float dt=Time.fixedDeltaTime;bool grounded=v.GetWheelsOnGround()>0;
            if(Rules.Complete(v))grounded|=Flight.HullSupported(rb);
            if(!grounded)s.AirPeakDownSpeed=Mathf.Max(s.AirPeakDownSpeed,-rb.velocity.y);
            var world=GameManager.Instance.World;var driver=v.GetAttached(0) as EntityPlayerLocal;
            bool input=s.InputReady&&Time.time-s.LastInput<.5f&&driver!=null&&Weapons.UIReady(driver)&&!Boarding.Active(v);
            if(Rules.Complete(v)){if(Flight.Step(v,s,grounded,input,dt))return;input&=s.InputReady;}
            bool sustain=Powered(v)&&driver!=null&&!driver.IsDead()&&v.timeInWater<=0;
            bool powered=sustain&&input;
            if(!input){s.Toggle=s.Descend=s.Jump=s.JumpWasHeld=false;s.Charge=0;s.ChargeStart=-1;}
            var movement=v.movementInput;float throttle=powered&&movement!=null?movement.moveForward:0,steer=powered&&movement!=null?movement.moveStrafe:0;
            // XML disables native drive/steer. Colliders provide support only.
            PrepareSupport(s.Wheels,Mathf.Abs(throttle)>.01f||Mathf.Abs(steer)>.01f||rb.velocity.sqrMagnitude>.01f);
            if(!sustain){s.HoverOn=false;s.Charge=0;s.ChargeStart=-1;s.Jump=false;s.Toggle=false;}
            if(s.Toggle){s.HoverOn=powered&&!s.HoverOn;s.Toggle=false;}
            if(!grounded&&s.Grounded){s.AirSince=Time.time;s.JumpAt=Time.time;s.AirPeakDownSpeed=0;s.LandingPendingUntil=-100;s.LandingEventExpected=false;}
            bool stomp=grounded&&!s.Grounded&&MarkLanding(s,Time.time,true);
            s.Grounded=grounded;
            if(stomp){s.LastSync=-100;Sync(v,s);Weapons.SendLocalIntent(v,Weapons.Stomp,Vector3.down*s.LandingStrength,v.position);}
            var forward=Vector3.ProjectOnPlane(rb.rotation*Vector3.forward,Vector3.up).normalized;
            var planar=Vector3.ProjectOnPlane(rb.velocity,Vector3.up);float speed=planar.magnitude;
            bool boost=!Samurai.Braced(v)&&powered&&grounded&&!s.HoverOn&&throttle>.1f&&v.vehicle.IsTurbo;
            s.Boost=boost||(s.Boost&&powered&&grounded&&speed>4.2f&&!s.HoverOn);
            float target=throttle>=0?throttle*(boost?13.5f:4f):throttle*2f;
            if(s.HoverOn)target=throttle*Rules.HoverSpeed;
            if(Samurai.Braced(v)){target=Mathf.Clamp(target,-1.2f,1.2f);steer*=.55f;s.Boost=false;}
            if(grounded||s.HoverOn)
            {
                var normal=Vector3.up;
                if(grounded){var sum=Vector3.zero;int count=0;foreach(var wheel in s.Wheels)if(wheel!=null&&wheel.GetGroundHit(out var contact)){sum+=contact.normal;count++;}if(count>0)normal=sum.normalized;}
                ApplyDrive(rb,forward,target,steer,s.Boost,normal,dt);
            }
            if(s.HoverOn)
            {
                float targetY=rb.position.y-dt;
                if(Weapons.Trace(v,v.position+Vector3.up*.2f,Vector3.down,6f,out var hit))targetY=hit.hit.pos.y-Origin.position.y+Rules.HoverHeight;
                if(Weapons.Trace(v,v.position+Vector3.up*2.6f,Vector3.up,3f,out var ceiling))targetY=Mathf.Min(targetY,ceiling.hit.pos.y-Origin.position.y-3.2f);
                float a=9.81f+Mathf.Clamp((targetY-rb.position.y)*4f-rb.velocity.y*1.5f,-6f,6f)-(s.Descend?5:0);
                rb.AddForce(Vector3.up*a*rb.mass,ForceMode.Force);
            }
            if((s.HoverOn||boost)&&EntityVehicle.VehicleFuelUsageModifier!=0)v.vehicle.SetFuelLevel(Mathf.Max(0,v.vehicle.GetFuelLevel()-Rules.HoverFuelPerSecond*dt));
            if(s.Jump&&!s.JumpWasHeld&&powered&&grounded)s.ChargeStart=Time.time;
            s.Charge=s.Jump&&s.ChargeStart>=0?Mathf.Clamp01((Time.time-s.ChargeStart)/Rules.JumpChargeSeconds):0;
            if(!s.Jump&&s.JumpWasHeld&&s.ChargeStart>=0&&powered&&grounded&&Time.time>=s.NextJump)
            {
                float charge=Mathf.Clamp((Time.time-s.ChargeStart)/Rules.JumpChargeSeconds,Rules.JumpMinCharge,1);
                rb.AddForce(Vector3.up*(charge*Rules.JumpMaxSpeed),ForceMode.VelocityChange);s.NextJump=Time.time+Rules.JumpCooldown;s.ChargeStart=-1;
            }
            s.JumpWasHeld=s.Jump;
            Sync(v,s);
            if(powered&&grounded&&speed>=Rules.TrampleSpeedThreshold&&Time.time-s.LastTime>=Rules.TrampleTickSeconds){s.LastTime=Time.time;Weapons.SendLocalIntent(v,Weapons.Trample,Vector3.down,v.position);}
        }
        public static Vector3 Snapshot(MoveState s)
        {
            int flags=(s.HoverOn?1:0)|(s.Boost?2:0)|(s.Grounded?4:0)|(Flight.Active(s)?8:0)|(s.FlightMode==Flight.Phase.Landing?16:0)|(s.FlightMode==Flight.Phase.Takeoff?32:0)|(s.FlightMode==Flight.Phase.PowerLost?64:0);
            return new Vector3(flags,s.Charge,Flight.Active(s)?s.VerticalInput:0);
        }
        public static void Sync(EntityVehicle v,MoveState s)
        {if(Time.time-s.LastSync>=.2f){s.LastSync=Time.time;Weapons.SendLocalIntent(v,Weapons.Motion,Snapshot(s),Vector3.zero);}}
        // One airborne interval grants at most one damaging landing. The
        // owner sends the grounded snapshot before its stomp intent so the
        // server can consume the same contact without creating an explosion.
        public static bool MarkLanding(MoveState s,float now,bool allowStomp)
        {
            bool stomp=allowStomp&&s.AirSince>=0&&now-s.AirSince>=Rules.StompAirborneSeconds;
            s.LandingAt=now;s.LandingStrength=Mathf.Clamp(.7f+s.AirPeakDownSpeed/60f,.7f,1f);
            s.LandingPendingUntil=stomp?now+.75f:-100;s.LandingEventExpected=stomp;s.AirSince=-1;s.AirPeakDownSpeed=0;return stomp;
        }
        // PhysX vehicle sticky-tire constraints can lock a stationary wheel even
        // with zero tire friction. A negligible 1Nm wake torque releases that
        // constraint; propulsion/braking/turning are still provided by ApplyDrive.
        public static void PrepareSupport(WheelCollider[] wheels,bool moving)
        {foreach(var wheel in wheels)if(wheel!=null){wheel.motorTorque=moving?1f:0;wheel.brakeTorque=0;wheel.steerAngle=0;}}
        // Shared native-physics controller, also exercised by the isolated QA fixture.
        public static void ApplyDrive(Rigidbody rb,Vector3 forward,float target,float steer,bool boost,Vector3 groundNormal,float dt)
        {
            var tangent=Vector3.ProjectOnPlane(forward,groundNormal).normalized;
            var velocity=Vector3.ProjectOnPlane(rb.velocity,groundNormal);
            float cap=Mathf.Abs(target)>Mathf.Abs(Vector3.Dot(velocity,tangent))?2f:4f;
            var acceleration=Vector3.ClampMagnitude((tangent*target-velocity)/Mathf.Max(dt,.001f),cap);
            // Cancel slope gravity only on walkable supports; collisions still block obstacles.
            if(groundNormal.y>=.7f)acceleration-=Vector3.ProjectOnPlane(Physics.gravity,groundNormal);
            rb.AddForce(acceleration*rb.mass,ForceMode.Force);
            float turn=boost?25f:velocity.magnitude>.3f?40f:60f;
            AngularAcceleration(rb,Vector3.up*Mathf.Clamp((steer*turn*Mathf.Deg2Rad-rb.angularVelocity.y)*6f,-4f,4f));
            var tilt=Vector3.Cross(rb.rotation*Vector3.up,Vector3.up);var rock=rb.angularVelocity-Vector3.up*rb.angularVelocity.y;
            AngularAcceleration(rb,Vector3.ClampMagnitude(tilt*10f-rock*3f,5f));
        }
        public static void AngularAcceleration(Rigidbody rb,Vector3 acceleration)
        {var axes=rb.rotation*rb.inertiaTensorRotation;rb.AddTorque(axes*Vector3.Scale(Quaternion.Inverse(axes)*acceleration,rb.inertiaTensor),ForceMode.Force);}
        public static void Clear(){moves.Clear();}
    }
}
