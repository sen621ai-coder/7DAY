$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$source=Get-Content "$root/99-AEC_T16_RuntimeFix/Source/ApachePilotWeapons.cs" -Raw
$base=Get-Content "$root/99-AEC_T16_RuntimeFix/Source/ApacheWeapons.cs" -Raw
$start=$base.IndexOf('        public static void RocketKinematics(')
$end=$base.IndexOf('        public static bool PredictRocket(',$start)
$kinematics=$base.Substring($start,$end-$start)
$fixture=@'
using System;
using System.Collections.Generic;
using UnityEngine;
namespace UnityEngine {
 public struct Vector3 {
  public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
  public static Vector3 zero=>new Vector3();public static Vector3 up=>new Vector3(0,1,0);public static Vector3 forward=>new Vector3(0,0,1);
  public float sqrMagnitude=>x*x+y*y+z*z;public float magnitude=>(float)Math.Sqrt(sqrMagnitude);
  public Vector3 normalized=>magnitude>0?this*(1/magnitude):zero;public void Normalize(){this=normalized;}
  public static Vector3 operator+(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
  public static Vector3 operator-(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
  public static Vector3 operator*(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);
  public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
  public static float Distance(Vector3 a,Vector3 b)=>(a-b).magnitude;
  public static Vector3 ClampMagnitude(Vector3 a,float max)=>a.magnitude>max?a.normalized*max:a;
  public static Vector3 RotateTowards(Vector3 a,Vector3 b,float angle,float max){float t=(float)Math.Acos(Math.Max(-1,Math.Min(1,Dot(a.normalized,b.normalized))));if(t<angle)return b;return (a*(float)Math.Sin(t-angle)+b*(float)Math.Sin(angle))*(1/(float)Math.Sin(t));}
 }
 public struct Vector2{float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}public float magnitude=>(float)Math.Sqrt(x*x+y*y);}
 public struct Quaternion{public static Quaternion Inverse(Quaternion q)=>q;public static Vector3 operator*(Quaternion q,Vector3 v)=>v;}
 public class Transform{public Vector3 position;}
 public class Rigidbody{public Vector3 velocity;}
 public static class Time{public static float time;}
 public static class Mathf{public const float Rad2Deg=57.29578f,Deg2Rad=.0174532925f;public static float Atan2(float a,float b)=>(float)Math.Atan2(a,b);public static float Abs(float x)=>Math.Abs(x);public static float Max(float a,float b)=>Math.Max(a,b);public static float Clamp01(float x)=>Math.Max(0,Math.Min(1,x));public static float Sqrt(float x)=>(float)Math.Sqrt(x);}
 public enum KeyCode{R,Mouse1,G} public static class Input{public static bool GetKey(KeyCode k)=>false;public static bool GetKeyDown(KeyCode k)=>false;}
 public struct Ray{public Vector3 origin,direction;}
}
public class EntityAlive{public int entityId=7;public Vector3 position;public bool Dead;public bool IsDead()=>Dead;}
public class EntityZombie:EntityAlive{}
public class EntityPlayerLocal:EntityAlive{}
public class EntityVehicle:EntityAlive{public EntityAlive Occupant=new EntityAlive();public EntityAlive GetAttached(int i)=>Occupant;public Rigidbody vehicleRB=new Rigidbody();}
public class ExplosionData{public float EntityDamage;public int EntityRadius,BlockRadius;}
public static class Origin{public static Vector3 position;}
namespace AECT16RuntimeFix {
 public static class ApacheFlightAssist{public static KeyCode Key(EntityVehicle v,string s,KeyCode k)=>k;}
 public static partial class ApacheWeapons {
  const byte Stop=2;static float nextAim,nextInput;static bool inputHeld;
  class World{public EntityAlive Target;public EntityAlive GetEntity(int i)=>Target!=null&&Target.entityId==i?Target:null;}
  static World currentWorld=new World();static bool ready=true,blocked,hitTarget=true;static int traceCalls,shots;static string consumed;
  public sealed partial class State{
   public EntityVehicle Vehicle=new EntityVehicle();public Transform Left,Right;public ApacheWeaponRules.Gate Gate=new ApacheWeaponRules.Gate();public int SalvoRemaining,Ammo=10;
   public ApacheWeaponRules.TriggerLease[] Triggers={new ApacheWeaponRules.TriggerLease(),new ApacheWeaponRules.TriggerLease()};
  }
  class Rocket{public bool Guided;public EntityAlive Target;public Vector3 Position,Velocity,TargetOffset;}
  class Hit{public Vector3Point hit=new Vector3Point();public EntityAlive Entity;}
  class Vector3Point{public Vector3 pos;}
  static class ItemActionAttack{public static EntityAlive FindHitEntity(Hit h)=>h.Entity;}
  static bool Trace(EntityVehicle v,Vector3 p,Vector3 d,float length,out Hit hit){traceCalls++;hit=new Hit();if(traceCalls%5==1){hit.hit.pos=new Vector3(0,-20,100);hit.Entity=hitTarget?currentWorld.Target:null;return true;}hit.hit.pos=new Vector3(0,0,10);return blocked;}
  static Quaternion BodyRotation(EntityVehicle v)=>new Quaternion();
  static bool ValidSight(EntityVehicle v,Vector3 p,Vector3 d)=>ApacheWeaponRules.ValidDirection(d.x,d.y,d.z)&&(p-v.position).sqrMagnitude<=1600;
  static bool ReadyOperator(State s,int actor,int seat)=>ready&&s.Vehicle.Occupant.entityId==actor;
  static bool Consume(State s,string item){if(s.Ammo<=0)return false;s.Ammo--;consumed=item;return true;}
  static int Ammo(State s,string item)=>s.Ammo;
  static ExplosionData RocketExplosion()=>new ExplosionData();
  static void Launch(State s,int actor){shots++;}
  static void Broadcast(int v,int id,byte k,Vector3 a,Vector3 b,float f){}
  static void SendIntent(EntityPlayerLocal p,EntityVehicle v,byte op,Ray r){}
  static Ray SightRay(EntityPlayerLocal p)=>new Ray();static KeyCode FireKey(EntityVehicle v,int s)=>KeyCode.G;
  static int checks;static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
  static void UpdateAt(State s,float now){Time.time=now;traceCalls=0;UpdatePilot(s,now);}
  public static void Test(){
   currentWorld.Target=new EntityZombie{entityId=90};var s=new State();
   Check(PilotArc(s.Vehicle,new Vector3(0,-1,.2f).normalized),"aim below helicopter without pitching");
   Check(!PilotArc(s.Vehicle,new Vector3(0,0,-1)),"rearward launch prohibited");
   Check(!PilotArc(s.Vehicle,new Vector3(0,1,0)),"upward launch prohibited");
   Time.time=0;PilotRequest(s,7,GuidedAim,Vector3.zero,Vector3.forward,1);UpdateAt(s,0);
   Check(s.PilotReason==5&&!PilotCanFire(s,0),"cannot fire before lock acquisition");
   for(int i=1;i<=20;i++){Time.time=i*.1f;PilotRequest(s,7,GuidedAim,Vector3.zero,Vector3.forward,i+1);UpdateAt(s,Time.time);}
   Check(PilotCanFire(s,2)&&s.LockTarget==currentWorld.Target,"continuous server-confirmed target locks in two seconds");
   Check(s.LockOffset.y==-20&&s.LockOffset.z==100,"lock follows selected impact height, not target feet");
   s.Ammo=0;FireGuided(s,7,2);Check(shots==0&&s.NextGuided==0,"empty cargo cannot start guided cooldown");s.Ammo=10;
   FireGuided(s,7,2);Check(shots==1&&s.Ammo==9&&consumed==ApacheWeaponRules.GuidedAmmo,"one guided round debited");
   Check(!PilotCanFire(s,2.1f),"held trigger cannot repeat guided shot");
   s.GuidedSpent=false;FireGuided(s,7,3);Check(shots==1&&s.Ammo==9,"guided cooldown blocks debit");
   blocked=true;UpdateAt(s,2.1f);Check(s.PilotReason==2&&s.LockTarget==null&&!PilotCanFire(s,2.1f),"cover breaks lock and blocks fire");blocked=false;
   UpdateAt(s,2.6f);Check(!s.PilotAiming,"lost heartbeat clears lock");
   Time.time=3;PilotRequest(s,7,PilotAim,Vector3.zero,Vector3.forward,30);UpdateAt(s,3);Check(PilotCanFire(s,3),"unguided aimed rockets need no lock");
   s.Vehicle.Occupant.entityId=8;UpdateAt(s,3.1f);Check(!s.PilotAiming,"seat change clears pilot state");s.Vehicle.Occupant.entityId=7;
   Time.time=4;PilotRequest(s,7,PilotAim,new Vector3(100,0,0),Vector3.forward,31);Check(!s.PilotAiming,"remote camera origin spoof rejected");
   PilotRequest(s,7,PilotAim,Vector3.zero,Vector3.forward,30);Check(!s.PilotAiming,"replayed input rejected");
   ready=false;PilotRequest(s,7,PilotFire,Vector3.zero,Vector3.forward,32);Check(!s.PilotAiming,"storage/operator gate blocks pilot");ready=true;
   hitTarget=false;PilotRequest(s,7,GuidedAim,Vector3.zero,Vector3.forward,33);UpdateAt(s,4);Check(s.PilotReason==4,"terrain cannot be locked as an entity");hitTarget=true;
   currentWorld.Target.Dead=true;UpdateAt(s,4.1f);Check(s.PilotReason==4,"dead zombies are not lock candidates");currentWorld.Target.Dead=false;
   UpdateAt(s,4.2f);Check(s.LockProgress==0,"reacquisition starts from zero");
   s.PilotAiming=true;s.PilotPoint=new Vector3(0,-20,100);s.Vehicle.vehicleRB.velocity=new Vector3(30,0,0);
   for(int side=0;side<2;side++){RocketKinematics(s,side==1,out var origin,out var velocity);Check(Vector3.Dot(velocity.normalized,(s.PilotPoint-origin).normalized)>.99999f,"both launchers converge despite inherited crosswind");}
   var target=currentWorld.Target;target.position=new Vector3(20,0,30);
   var rocket=new Rocket{Guided=true,Target=target,TargetOffset=new Vector3(0,2,0),Velocity=Vector3.forward*70};GuideRocket(rocket,.1f);
   Check(rocket.Velocity.x>0&&rocket.Velocity.x<6,"guidance turns gradually with angular limit");
   target.Dead=true;var before=rocket.Velocity;GuideRocket(rocket,.1f);Check(rocket.Target==null&&Vector3.Distance(before,rocket.Velocity)<.001,"dead target abandons guidance and preserves trajectory");
   var ordinary=new Rocket{Guided=false,Target=target,Velocity=Vector3.forward*70};GuideRocket(ordinary,1);Check(ordinary.Velocity.x==0,"ordinary rockets never guide");
   Console.WriteLine("PASS: "+checks+" actual pilot aim/lock/debit/cooldown/authority/convergence/guidance checks with mocked physics.");
  }
 }
}
'@
$rules=Get-Content "$root/99-AEC_T16_RuntimeFix/Source/ApacheWeaponRules.cs" -Raw
Add-Type -TypeDefinition ($fixture+$source.Replace('using UnityEngine;','')+$rules.Replace('using System;','')+"namespace AECT16RuntimeFix { public static partial class ApacheWeapons { $kinematics } }") -CompilerOptions '/nowarn:0649'
[AECT16RuntimeFix.ApacheWeapons]::Test()
