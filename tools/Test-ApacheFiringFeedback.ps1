$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$fixture=@'
using System;
using UnityEngine;
namespace UnityEngine {
 public static class Time {public static float time,fixedDeltaTime=.02f;}
 public enum ForceMode {Acceleration}
 public struct Vector3 {
  public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
  public static Vector3 zero=>new Vector3();public static Vector3 up=>new Vector3(0,1,0);
  public float sqrMagnitude=>x*x+y*y+z*z;public float magnitude=>(float)Math.Sqrt(sqrMagnitude);
  public Vector3 normalized=>magnitude>0?this/magnitude:zero;
  public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
  public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
  public static Vector3 operator -(Vector3 a)=>zero-a;
  public static Vector3 operator *(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);
  public static Vector3 operator /(Vector3 a,float b)=>a*(1/b);
  public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
  public static Vector3 Cross(Vector3 a,Vector3 b)=>new Vector3(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
  public static Vector3 ProjectOnPlane(Vector3 a,Vector3 n)=>a-n*Dot(a,n);
  public static Vector3 ClampMagnitude(Vector3 a,float m)=>a.magnitude>m?a.normalized*m:a;
 }
 public struct Quaternion {
  public float x,y,z,w;public bool Inverted;
  public static Quaternion identity=>new Quaternion{w=1};
  static System.Numerics.Quaternion N(Quaternion q)=>new System.Numerics.Quaternion(q.x,q.y,q.z,q.w);
  static Quaternion U(System.Numerics.Quaternion q)=>new Quaternion{x=q.X,y=q.Y,z=q.Z,w=q.W};
  public static Quaternion Euler(float x,float y,float z)=>U(System.Numerics.Quaternion.CreateFromYawPitchRoll(y*(float)Math.PI/180,x*(float)Math.PI/180,z*(float)Math.PI/180));
  public static float Dot(Quaternion a,Quaternion b)=>System.Numerics.Quaternion.Dot(N(a),N(b));
  public static Quaternion Inverse(Quaternion q)=>U(System.Numerics.Quaternion.Inverse(N(q)));
  public static Quaternion operator *(Quaternion a,Quaternion b)=>U(N(a)*N(b));
  public static Vector3 operator *(Quaternion q,Vector3 v){
   if(q.Inverted)return new Vector3(v.x,-v.y,-v.z);
   var n=System.Numerics.Vector3.Transform(new System.Numerics.Vector3(v.x,v.y,v.z),N(q));return new Vector3(n.X,n.Y,n.Z);
  }
 }
 public struct Ray {public Vector3 origin,direction;}
 public class Transform {
  public Vector3 localPosition;public Transform parent;public float Scale=1;public Quaternion localRotation=Quaternion.identity;
  public Quaternion rotation=>parent!=null?parent.rotation*localRotation:localRotation;
  public Vector3 position=>parent!=null?parent.position+parent.TransformVector(localPosition):localPosition;
  public Vector3 TransformVector(Vector3 v)=>parent!=null?parent.TransformVector(localRotation*(v*Scale)):localRotation*(v*Scale);
 }
 public class Camera {public Transform transform=new Transform();}
 public class Rigidbody {
  public Vector3 worldCenterOfMass,Torque;public Quaternion rotation=Quaternion.identity;public bool isKinematic;
  public void AddTorque(Vector3 t,ForceMode mode){Torque+=t;}
 }
}
namespace HarmonyLib {
 public class Harmony {public void Patch(object o,HarmonyMethod postfix=null){} }
 public class HarmonyMethod {public HarmonyMethod(Type t,string n){} }
 public static class AccessTools {public static object Method(Type t,string n)=>null;}
}
public static class Log {public static void Warning(string s){} }
public static class Origin {public static Vector3 position;}
public class Vehicle {public float Fuel=100;public int Health=100;public int GetHealth()=>Health;public float GetFuelLevel()=>Fuel;}
public class EntityPlayerLocal {public EntityVehicle AttachedToEntity;public int entityId=10;public Camera playerCamera=new Camera();}
public class EntityVehicle {
 public bool Apache=true,isEntityRemote,RBActive=true,hasDriver=true,IsEngineRunning=true,Dead;
 public Rigidbody vehicleRB=new Rigidbody();public Vehicle vehicle=new Vehicle();public float timeInWater;public int Wheels;
 public EntityPlayerLocal Pilot=new EntityPlayerLocal(),Gunner=new EntityPlayerLocal{entityId=11};
 public static float VehicleFuelUsageModifier=1;public bool IsDead()=>Dead;public int GetWheelsOnGround()=>Wheels;
 public EntityPlayerLocal GetAttached(int seat)=>seat==0?Pilot:Gunner;
}
namespace AECT16RuntimeFix {
 public static class MD500FlightControls {public static bool Enabled=true;public static bool Applies(Vehicle v)=>Enabled;}
 public static class ApacheWeapons {
  public const byte CannonEvent=1,RocketEvent=2;
  public static bool IsApache(EntityVehicle v)=>v!=null&&v.Apache;
  public static int Seat(EntityVehicle v,int id)=>v.Pilot!=null&&v.Pilot.entityId==id?0:v.Gunner!=null&&v.Gunner.entityId==id?1:-1;
 }
 public static class ApacheWeaponRules {public static bool Finite(float x)=>!float.IsInfinity(x)&&!float.IsNaN(x);}
 public static class FeedbackTests {
  static int checks;
  static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
  static void Near(float a,float b,float e,string why){Check(Math.Abs(a-b)<=e,why+" got="+a+" expected="+b);}
  static void Shot(EntityVehicle v,int seq=1,byte kind=1,float guided=0,Vector3? direction=null){
   Vector3 start=new Vector3(0,-1,2);var d=direction??new Vector3(0,0,1);
   ApacheFiringFeedback.Receive(v,seq,kind,start,kind==1?start+d*100:d*100,guided);
  }
  static float SampleBurst(int count,byte kind=1,float guided=0,bool duplicate=false){
   ApacheFiringFeedback.Clear();Time.time=0;var v=new EntityVehicle();v.Gunner.AttachedToEntity=v;
   for(int i=1;i<=count;i++)Shot(v,i,kind,guided);
   if(duplicate){Time.time=.01f;Shot(v,count,kind,guided);Shot(v,count-1,kind,guided);}
   Time.time=.018f;ApacheFiringFeedback.UpdateCamera(v.Gunner,true,false);
   return v.Gunner.playerCamera.transform.localPosition.magnitude;
  }
  public static void Run(){
   ApacheFiringFeedback.Install(new HarmonyLib.Harmony());
   float single=SampleBurst(1);
   Check(single>0&&single<.0005f,"very weak single-shot visual feedback");
   Near(SampleBurst(1000),single,.00000001f,"1000 overlapping shots are no stronger than one");
   Near(SampleBurst(1,1,0,true),single,.00000001f,"duplicate and old events do not refresh the pulse");
   float rocket=SampleBurst(1,2,0),guided=SampleBurst(1,2,1);
   Check(guided<rocket&&rocket<single,"launch vibration has distinct lighter amplitudes");
   ApacheFiringFeedback.Clear();Time.time=0;var mix=new EntityVehicle();mix.Gunner.AttachedToEntity=mix;
   Shot(mix);Shot(mix,2,2);Time.time=.018f;ApacheFiringFeedback.UpdateCamera(mix.Gunner,true,false);
   Near(mix.Gunner.playerCamera.transform.localPosition.magnitude,rocket,.00000001f,"different weapons replace rather than sum effects");
   foreach(float dt in new[]{.008333f,.016667f,.033333f}){
    ApacheFiringFeedback.Clear();Time.time=0;var v=new EntityVehicle();v.Gunner.AttachedToEntity=v;
    var torque=new Vector3(1,2,3);v.vehicleRB.Torque=torque;float next=0,peak=0;int seq=0;
    for(int i=0;i<(int)(30/dt);i++){
     Time.time=i*dt;if(Time.time<20&&Time.time>=next){Shot(v,++seq);next+=1f/6f;}
     ApacheFiringFeedback.AfterForces(v);ApacheFiringFeedback.UpdateCamera(v.Gunner,true,false);peak=Math.Max(peak,v.Gunner.playerCamera.transform.localPosition.magnitude);
    }
    Check(peak<.0005f,"20-second continuous fire remains sub-millimetre at different render rates");
    Near(v.Gunner.playerCamera.transform.localPosition.magnitude,0,0,"camera returns exactly to baseline after firing stops");
    Near((v.vehicleRB.Torque-torque).magnitude,0,0,"cannon firing never changes rigidbody torque");
   }
   ApacheFiringFeedback.Clear();Time.time=0;var md=new EntityVehicle{Apache=false};md.Gunner.AttachedToEntity=md;
   Shot(md);Time.time=.018f;ApacheFiringFeedback.UpdateCamera(md.Gunner,true,false);
   Near(md.Gunner.playerCamera.transform.localPosition.magnitude,0,0,"MD500 has no firing feedback");
   CameraTests();
   AimingFeedbackTests();
   PhysicsTests();
   Console.WriteLine("PASS: "+checks+" bounded main recoil, authority, non-accumulating feedback, steady aim, camera restoration and MD500 isolation checks.");
  }
  static Vector3 PhysicsStep(EntityVehicle v,float dt){
   Time.fixedDeltaTime=dt;v.vehicleRB.Torque=Vector3.zero;
   ApacheFiringFeedback.AfterForces(v);return v.vehicleRB.Torque;
  }
  static void PhysicsTests(){
   foreach(float dt in new[]{.01f,.02f,.033333f,.05f}){
    foreach(bool spam in new[]{false,true}){
     ApacheFiringFeedback.Clear();Time.time=0;var v=new EntityVehicle();Shot(v,1,2);
     Vector3 velocity=Vector3.zero;float peak=0;int seq=1;
     for(int i=0;i< (int)(.21f/dt);i++){
      Time.time=i*dt;
      if(spam){for(int j=0;j<100;j++)Shot(v,++seq,2);Shot(v,++seq,1);}
      var a=PhysicsStep(v,dt);velocity+=a*dt;peak=Math.Max(peak,velocity.magnitude);
      Check(a.magnitude<=ApacheFeedbackMath.RecoilAcceleration+.000001f,"physical torque capped including event floods");
      Near(a.y,0,0,"no yaw torque");Near(a.z,0,0,"level launch does not introduce roll");
     }
     Check(peak>0&&peak<.014f,"tiny but real angular reaction");
     Near(velocity.magnitude,0,.000001f,"whole pulse leaves zero angular impulse regardless of spam or timestep");
    }
   }
   ApacheFiringFeedback.Clear();Time.time=0;var regular=new EntityVehicle();var guided=new EntityVehicle();
   Shot(regular,1,2);Shot(guided,1,2,1);
   var first=PhysicsStep(regular,.02f);var softer=PhysicsStep(guided,.02f);
   Check(first.x<0,"main weapon initially gives a tiny nose-up reaction");
   Near(softer.x,first.x*.55f,.000001f,"guided launch has 55 percent of rocket torque");
   Action<EntityVehicle>[] deny={v=>v.Apache=false,v=>v.isEntityRemote=true,v=>v.RBActive=false,
    v=>v.vehicleRB.isKinematic=true,v=>v.hasDriver=false,v=>v.Pilot=null,v=>v.IsEngineRunning=false,
    v=>v.Dead=true,v=>v.vehicle.Health=0,v=>v.timeInWater=1,v=>v.Wheels=1,v=>v.vehicle.Fuel=0,
    v=>v.vehicleRB.rotation=new Quaternion{Inverted=true}};
   foreach(var change in deny){
    ApacheFiringFeedback.Clear();Time.time=0;var v=new EntityVehicle();change(v);Shot(v,1,2);
    Near(PhysicsStep(v,.02f).magnitude,0,0,"ineligible vehicle gets no physical feedback");
    v=new EntityVehicle();Shot(v,1,2);change(v);Near(PhysicsStep(v,.02f).magnitude,0,0,"state change cancels queued recoil");
   }
   ApacheFiringFeedback.Clear();Time.time=0;var handoff=new EntityVehicle();Shot(handoff,1,2);handoff.Pilot.entityId++;
   Near(PhysicsStep(handoff,.02f).magnitude,0,0,"seat handoff cancels previous pilot recoil");
   handoff.Pilot.entityId--;Near(PhysicsStep(handoff,.02f).magnitude,0,0,"cancelled pulse cannot replay");
   var stale=new EntityVehicle();Shot(stale,1,2);Time.time=1;Near(PhysicsStep(stale,.02f).magnitude,0,0,"delayed physics cannot replay old recoil");
   ApacheFiringFeedback.Clear();Time.time=0;var disabled=new EntityVehicle();Shot(disabled,1,2);MD500FlightControls.Enabled=false;
   Near(PhysicsStep(disabled,.02f).magnitude,0,0,"disabled flight adapter cancels body feedback");MD500FlightControls.Enabled=true;
   var cleaned=new EntityVehicle();Shot(cleaned,1,2);ApacheFiringFeedback.Clear();Near(PhysicsStep(cleaned,.02f).magnitude,0,0,"world cleanup discards physical pulse");
   // Integrate real feedback with the actual Apache flight-profile damping/gain.
   // This is a small-angle simulation, not a live Unity physics acceptance test.
   var profile=MD500FlightMath.Handling(true);
   foreach(float dt in new[]{.01f,.02f,.033333f,.05f}){
    foreach(float interval in new[]{.15f,.001f}){
     ApacheFiringFeedback.Clear();Time.time=0;var v=new EntityVehicle();float angle=0,speed=0,peak=0,earlyPeak=0,latePeak=0,next=0;int seq=0;
     for(int i=0;i<(int)(35/dt);i++){
      Time.time=i*dt;
      if(Time.time<30 && Time.time>=next){Shot(v,++seq,2);next=Time.time+interval;}
      Shot(v,++seq,1); // Simultaneous cannon cannot sustain or reset the rocket pulse.
      float recoil=PhysicsStep(v,dt).x;
      speed+=(recoil-profile.AttitudeGain*angle-profile.AttitudeDamping*speed)*dt;angle+=speed*dt;
      peak=Math.Max(peak,Math.Abs(angle));if(Time.time<10)earlyPeak=Math.Max(earlyPeak,Math.Abs(angle));if(Time.time>20&&Time.time<30)latePeak=Math.Max(latePeak,Math.Abs(angle));
     }
     Check(peak*180/Math.PI<.08,"30-second continuous launch peak stays below 0.08 degrees in damped model");
     Check(latePeak<=earlyPeak*1.25f+.00001f,"late continuous-fire amplitude stays close to early amplitude");
     Check(Math.Abs(angle)*180/Math.PI<.001&&Math.Abs(speed)<.0001,"flight stabilization clears residual after stop");
     Console.WriteLine("  Main recoil dt="+dt+" interval="+interval+" peakDegrees="+(peak*180/Math.PI).ToString("F5"));
    }
   }
  }
  static void CameraTests(){
   ApacheFiringFeedback.Clear();Time.time=0;var v=new EntityVehicle();v.Gunner.AttachedToEntity=v;v.Pilot.AttachedToEntity=v;
   var p=v.Gunner;var cam=p.playerCamera;cam.transform.localPosition=new Vector3(1,2,3);Shot(v);
   Time.time=.018f;ApacheFiringFeedback.UpdateCamera(p,true,false);var full=cam.transform.localPosition-new Vector3(1,2,3);
   Check(full.magnitude>0&&full.magnitude<.0005f,"gunner feedback is sub-millimetre scale");
   var direction=new Vector3(.2f,-.4f,.9f);var ray=ApacheFiringFeedback.StabilizeRay(cam,new Ray{origin=cam.transform.localPosition,direction=direction});
   Near((ray.origin-new Vector3(1,2,3)).magnitude,0,.000001f,"presentation offset excluded from sight origin");
   Near((ray.direction-direction).magnitude,0,0,"presentation does not rotate sight ray");
   for(int i=0;i<100;i++)ApacheFiringFeedback.UpdateCamera(p,true,false);
   Near((cam.transform.localPosition-new Vector3(1,2,3)-full).magnitude,0,.000001f,"camera offset does not accumulate");
   ApacheFiringFeedback.UpdateCamera(p,true,true);Near((cam.transform.localPosition-new Vector3(1,2,3)).magnitude,0,0,"aiming feedback does not translate camera");
   ApacheFiringFeedback.UpdateCamera(v.Pilot,true,false);Near(v.Pilot.playerCamera.transform.localPosition.magnitude,full.magnitude*.45f,.000001f,"pilot feedback lighter than gunner");
   Near((cam.transform.localPosition-new Vector3(1,2,3)).magnitude,0,.000001f,"switching seat/camera restores former camera");
   ApacheFiringFeedback.UpdateCamera(v.Pilot,false,false);Near(v.Pilot.playerCamera.transform.localPosition.magnitude,0,0,"unusable/menu state restores camera");
   ApacheFiringFeedback.UpdateCamera(p,true,false);ApacheFiringFeedback.RestoreCamera();Near((cam.transform.localPosition-new Vector3(1,2,3)).magnitude,0,.000001f,"native camera hook sees original position");
   ApacheFiringFeedback.UpdateCamera(p,true,false);Time.time=1;ApacheFiringFeedback.UpdateCamera(p,true,false);
   Near((cam.transform.localPosition-new Vector3(1,2,3)).magnitude,0,.000001f,"effect expires after stop");
   Time.time=0;Shot(v,2);Time.time=.018f;ApacheFiringFeedback.UpdateCamera(p,true,false);ApacheFiringFeedback.Clear();
   Near((cam.transform.localPosition-new Vector3(1,2,3)).magnitude,0,.000001f,"world cleanup restores camera");
   Shot(v,3);Time.time=.036f;ApacheFiringFeedback.UpdateCamera(p,true,false);cam.transform.localPosition=new Vector3(4,5,6);ApacheFiringFeedback.RestoreCamera();
   Near((cam.transform.localPosition-new Vector3(4,5,6)).magnitude,0,0,"cleanup respects external camera change");
  }
  static float RotationDistance(Quaternion a,Quaternion b){
   var d=Quaternion.Inverse(a)*b;
   return (float)(2*Math.Atan2(Math.Sqrt(d.x*d.x+d.y*d.y+d.z*d.z),Math.Abs(d.w))*180/Math.PI);
  }
  static void AimingFeedbackTests(){
   ApacheFiringFeedback.Clear();Time.time=0;var v=new EntityVehicle();v.Gunner.AttachedToEntity=v;v.Pilot.AttachedToEntity=v;
   var p=v.Gunner;var cam=p.playerCamera;
   cam.transform.parent=new Transform{localRotation=Quaternion.Euler(17,43,-8),localPosition=new Vector3(10,20,30),Scale=2};
   cam.transform.localPosition=new Vector3(1,2,3);cam.transform.localRotation=Quaternion.Euler(-21,32,6);
   var baseline=cam.transform.localRotation;var worldBaseline=cam.transform.rotation;Shot(v);
   Time.time=.018f;ApacheFiringFeedback.UpdateCamera(p,true,true);var shaken=cam.transform.localRotation;
   float roll=RotationDistance(baseline,shaken);
   Check(roll>.005f&&roll<.045f,"gunner aiming has perceptible but tightly bounded visual roll");
   foreach(var direction in new[]{new Vector3(0,0,1),new Vector3(.4f,-.3f,1),new Vector3(-.5f,.6f,1)}){
    var d=direction.normalized;var near=d*.3f;
    var ray=new Ray{origin=cam.transform.position+cam.transform.rotation*near,direction=cam.transform.rotation*d};
    ray=ApacheFiringFeedback.StabilizeRay(cam,ray);
    Near((ray.direction-worldBaseline*d).magnitude,0,.000001f,"off-center aim direction excludes roll under rotated parent");
    Near((ray.origin-(cam.transform.position+worldBaseline*near)).magnitude,0,.00001f,"near-plane ray origin also excludes roll");
   }
   for(int i=0;i<1000;i++)ApacheFiringFeedback.UpdateCamera(p,true,true);
   Near(RotationDistance(cam.transform.localRotation,shaken),0,.00001f,"aiming roll never accumulates over repeated camera hooks");
   Shot(v,2,2);ApacheFiringFeedback.UpdateCamera(p,true,true);
   Near(RotationDistance(cam.transform.localRotation,shaken),0,.00001f,"main fire cannot restart cannon sight vibration");
   Time.time=.025f;Shot(v,1,1);ApacheFiringFeedback.UpdateCamera(p,true,true);
   Near(RotationDistance(baseline,cam.transform.localRotation),Math.Abs(ApacheFeedbackMath.AimRoll(.025f)),.00002f,"old cannon event cannot refresh feedback");
   ApacheFiringFeedback.UpdateCamera(v.Pilot,true,true);
   Near(RotationDistance(cam.transform.localRotation,baseline),0,.00001f,"seat switch restores gunner camera rotation");
   Near(RotationDistance(v.Pilot.playerCamera.transform.localRotation,Quaternion.identity),0,0,"pilot sight gets no gunner vibration");
   ApacheFiringFeedback.UpdateCamera(p,true,true);ApacheFiringFeedback.UpdateCamera(p,false,true);
   Near(RotationDistance(cam.transform.localRotation,baseline),0,.00001f,"menu or unusable state restores sight");
   ApacheFiringFeedback.UpdateCamera(p,true,true);ApacheFiringFeedback.RestoreCamera();
   Near(RotationDistance(cam.transform.localRotation,baseline),0,.00001f,"native camera update receives unshaken rotation");
   ApacheFiringFeedback.UpdateCamera(p,true,true);var external=Quaternion.Euler(9,25,4);cam.transform.localRotation=external;ApacheFiringFeedback.RestoreCamera();
   Near(RotationDistance(cam.transform.localRotation,external),0,.00001f,"restoration respects external rotation changes");
   cam.transform.localRotation=baseline;Time.time=.3f;Shot(v,3,2);ApacheFiringFeedback.UpdateCamera(p,true,true);
   Near(RotationDistance(cam.transform.localRotation,baseline),0,.00001f,"rockets alone cannot animate expired cannon feedback");
   Shot(v,4);Time.time=.318f;ApacheFiringFeedback.UpdateCamera(p,true,true);ApacheFiringFeedback.Clear();
   Near(RotationDistance(cam.transform.localRotation,baseline),0,.00001f,"world cleanup restores sight rotation");
   foreach(float dt in new[]{1f/30,1f/60,1f/120}){
    ApacheFiringFeedback.Clear();Time.time=0;v=new EntityVehicle();p=v.Gunner;p.AttachedToEntity=v;cam=p.playerCamera;
    float peak=0,next=0;int seq=0;
    for(int i=0;i<(int)(21/dt);i++){
     Time.time=i*dt;
     if(Time.time<20&&Time.time>=next){for(int j=0;j<100;j++)Shot(v,++seq);next=Time.time+1f/6;}
     ApacheFiringFeedback.UpdateCamera(p,true,true);
     peak=Math.Max(peak,RotationDistance(Quaternion.identity,cam.transform.localRotation));
     ApacheFiringFeedback.AfterForces(v);
    }
    Check(peak>.005f&&peak<=.045f,"20-second aimed cannon burst remains bounded across render rates and event floods");
    Near(RotationDistance(Quaternion.identity,cam.transform.localRotation),0,0,"stopping aimed fire returns exactly to original rotation");
    Near(v.vehicleRB.Torque.magnitude,0,0,"aimed cannon has no physical airframe recoil");
   }
  }
 }
}
'@
$runtime=Get-Content "$root/99-AEC_T16_RuntimeFix/Source/ApacheFiringFeedback.cs" -Raw
$math=Get-Content "$root/99-AEC_T16_RuntimeFix/Source/ApacheFeedbackMath.cs" -Raw
$flightMath=Get-Content "$root/99-AEC_T16_RuntimeFix/Source/MD500FlightMath.cs" -Raw
# All using directives precede the fixture; remove only duplicated math using.
Add-Type -TypeDefinition ($runtime+"`n"+($math -replace '^using System;','')+"`n"+($flightMath -replace '^using System;','')+"`n"+($fixture -replace '^using System;\s*using UnityEngine;',''))
[AECT16RuntimeFix.FeedbackTests]::Run()
