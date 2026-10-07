using UnityEngine;
namespace PZAEC.Mecha {
 public static class Skim {
  public enum Phase{Off,Lifting,Cruise,Settling}
  public const float Height=.30f,MaxRelief=1f,Speed=13.5f,Braking=10f;
  public static bool Active(EntityVehicle v){return v!=null&&Rules.Complete(v)&&Locomotion.Get(v).SkimPhase!=Phase.Off;}
  public static float Lookahead(float speed){return speed*speed/(2*Braking)+2.2f;}
  public static bool ReliefAllowed(float from,float to){return Mathf.Abs(to-from)<=MaxRelief+.001f;}
  public static string Label(EntityVehicle v){var s=Locomotion.Get(v);return !string.IsNullOrEmpty(s.SkimReason)?s.SkimReason:s.SkimPhase==Phase.Lifting?"抬升滑行":s.SkimPhase==Phase.Cruise?"低空滑行":s.SkimPhase==Phase.Settling?"制动落地":"[Shift]低空滑行";}
  public static bool Ground(EntityVehicle v,Vector3 position,float reference,out float y){y=reference;Vector3 point;if(!GroundSupport.PointAt(v,new Vector3(position.x,reference,position.z),1.2f,1.3f,out point))return false;y=point.y;return true;}
  public static void Cancel(Locomotion.MoveState s){s.SkimPhase=Phase.Off;s.SkimLatch=true;s.SkimAge=0;s.Boost=false;s.LastSync=-100;s.AirSince=-1;s.LandingEventExpected=false;}
  public static bool Step(EntityVehicle v,Locomotion.MoveState s,bool grounded,bool input,float dt){if(!Rules.Complete(v))return false;var rb=v.vehicleRB;bool shift=input&&v.vehicle.IsTurbo;var m=v.movementInput;float throttle=input&&m!=null?m.moveForward:0,steer=input&&m!=null?m.moveStrafe:0;bool powered=Locomotion.Powered(v)&&v.timeInWater<=0;return Drive(v,s,grounded,shift,throttle,steer,powered,dt);}
  public static bool Drive(EntityVehicle v,Locomotion.MoveState s,bool grounded,bool shift,float throttle,float steer,bool powered,float dt){var rb=v.vehicleRB;
   if(!shift){s.SkimLatch=false;if(s.SkimPhase==Phase.Off)s.SkimReason=null;}
   if(Flight.AirPose(s)){if(s.SkimPhase!=Phase.Off)Cancel(s);s.SkimLatch=true;return false;}
   if(!powered){if(s.SkimPhase!=Phase.Off)Cancel(s);return false;}
   if(s.SkimPhase==Phase.Off){if(!shift||s.SkimLatch||throttle<=.1f||!grounded||Boarding.Active(v)||Traversal.Active(v)||Samurai.Braced(v))return false;if(!Ground(v,v.position,v.position.y,out s.SkimGround))return false;s.SkimPhase=Phase.Lifting;s.SkimAge=0;s.SkimTarget=s.SkimGround+Height;s.SkimStart=s.SkimRaisedAt=v.position;s.SkimRaisedHeight=s.SkimTarget;s.LastSync=-100;}
   if(s.SkimPhase!=Phase.Settling)s.SkimReason=null;s.SkimAge+=dt;var old=s.SkimPhase;var forward=rb.rotation*Vector3.forward;float speed=Vector3.ProjectOnPlane(rb.velocity,Vector3.up).magnitude;bool braking=!shift||throttle<=.1f||Boarding.Active(v)||Samurai.Get(v).SwordHeld||Samurai.Get(v).GuardHeld||s.Jump;
   float reference=s.SkimGround,groundNow;if(!Ground(v,v.position,reference,out groundNow)||!ReliefAllowed(reference,groundNow)){braking=true;s.SkimLatch=true;s.SkimReason="前方落差过大，按 Q 飞越";}else s.SkimGround=groundNow;
   float highest=s.SkimGround,allowed=Speed;float distance=Lookahead(speed);var side=rb.rotation*Vector3.right;
   for(float d=.4f;d<=distance+.01f;d+=.4f){bool safe=true;float peak=highest;for(int lane=-1;lane<=1;lane++){float y;if(!Ground(v,v.position+forward*(d+.8f)+side*(lane*.65f),s.SkimGround,out y)||!ReliefAllowed(s.SkimGround,y)){safe=false;break;}peak=Mathf.Max(peak,y);}
    // Hull sweep includes low ceilings; never teleport across a step face.
    bool terrainSafe=safe;var support=GroundSupport.Get(v);var from=v.position;var to=from+forward*d;to.y=Mathf.Max(from.y,peak+Height);
    if(safe&&support!=null&&!GroundSupport.HullClear(v,support.Shape,from,to,rb.rotation))safe=false;
    if(safe&&from.y>=s.SkimGround+.10f&&!GroundSupport.BoxClear(v,from+Vector3.up*1.6f,to+Vector3.up*1.6f,new Vector3(.65f,1.55f,.55f),rb.rotation))safe=false;
    if(!safe&&terrainSafe&&peak+Height>from.y+.05f){var raised=from;raised.y=peak+Height;
     if(support!=null&&GroundSupport.HullClear(v,support.Shape,from,raised,rb.rotation)&&GroundSupport.BoxClear(v,from+Vector3.up*1.6f,raised+Vector3.up*1.6f,new Vector3(.65f,1.55f,.55f),rb.rotation)){highest=peak;allowed=0;s.SkimReason="抬升避让前方台阶";break;}
    }
    if(!safe){allowed=Mathf.Min(allowed,Mathf.Sqrt(2*Braking*Mathf.Max(0,d-1.8f)));s.SkimReason="前方通路受限，按 Q 飞越";if(allowed<.2f){braking=true;s.SkimLatch=true;}break;}highest=peak;
   }
   if(braking)s.SkimPhase=Phase.Settling;
   if(s.SkimPhase==Phase.Lifting&&s.SkimAge>.35f&&rb.position.y+Origin.position.y>=s.SkimGround+.20f)s.SkimPhase=Phase.Cruise;
   float target=s.SkimPhase==Phase.Cruise?Mathf.Min(allowed,Speed*Mathf.Clamp01(throttle))*Mathf.Lerp(1,.35f,Mathf.Abs(steer)):0;
   float wantY=highest+Height;if(highest>s.SkimGround+.05f){s.SkimRaisedHeight=Mathf.Max(s.SkimRaisedHeight,wantY);s.SkimRaisedAt=v.position;}if(Vector3.ProjectOnPlane(v.position-s.SkimRaisedAt,Vector3.up).sqrMagnitude<16)wantY=Mathf.Max(wantY,s.SkimRaisedHeight);if(s.SkimPhase==Phase.Settling)wantY=s.SkimGround;
   s.SkimTarget=Mathf.MoveTowards(s.SkimTarget,wantY,dt*(wantY>s.SkimTarget?2f:.8f));
   GroundSupport.Suspend(v);s.Grounded=false;s.Charge=0;s.ChargeStart=-1;s.AirSince=-1;s.LandingEventExpected=false;s.LandingPendingUntil=-100;s.Boost=s.SkimPhase==Phase.Cruise;
   var planar=Vector3.ProjectOnPlane(rb.velocity,Vector3.up);var desired=forward*target;var delta=Vector3.ClampMagnitude((desired-planar)/Mathf.Max(dt,.001f),target<speed?Braking:4f);float lift=-Physics.gravity.y+Mathf.Clamp((s.SkimTarget-Origin.position.y-rb.position.y)*12-rb.velocity.y*6,-6,6);rb.AddForce((delta+Vector3.up*lift)*rb.mass,ForceMode.Force);rb.MoveRotation(Quaternion.RotateTowards(rb.rotation,rb.rotation*Quaternion.Euler(0,steer*45*dt,0),45*dt));var tilt=Vector3.Cross(rb.rotation*Vector3.up,Vector3.up);var rock=rb.angularVelocity-Vector3.up*rb.angularVelocity.y;Locomotion.AngularAcceleration(rb,Vector3.ClampMagnitude(tilt*10-rock*3,5));
   if(EntityVehicle.VehicleFuelUsageModifier!=0)v.vehicle.SetFuelLevel(Mathf.Max(0,v.vehicle.GetFuelLevel()-Rules.HoverFuelPerSecond*dt));
   if(s.SkimPhase==Phase.Settling&&speed<.2f&&Mathf.Abs(rb.position.y+Origin.position.y-s.SkimGround)<.05f&&Mathf.Abs(rb.velocity.y)<.25f){Cancel(s);s.Grounded=true;GroundSupport.Forget(v);}
   if(old!=s.SkimPhase){s.SkimStart=v.position;s.SkimAge=0;s.LastSync=-100;}Locomotion.Sync(v,s);return true;
  }
 }
}
