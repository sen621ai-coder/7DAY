$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
$source=Get-Content "$root/ZZ-PZAEC_M1Abrams/Source/M1Weapons.cs" -Raw
$start=$source.IndexOf('        static void Shoot(State s)');$end=$source.IndexOf('        public static void RecoilImpulse',$start)
$actual=$source.Substring($start,$end-$start)
$rules=Get-Content "$root/ZZ-PZAEC_M1Abrams/Source/M1Rules.cs" -Raw
$stub=@'
namespace PZAEC.M1 {
public static class FireFixture {
 public const byte ShotEvent=1;public static int Events,Noises,Impulses;
 public struct Vector3{public float x,y,z;public static Vector3 zero=>new Vector3();public static Vector3 operator*(Vector3 v,float f)=>v;public static Vector3 operator+(Vector3 a,Vector3 b)=>a;public static Vector3 ClampMagnitude(Vector3 v,float m)=>v;}
 public static class Time{public static float time;}
 public static class Model{public const float BarrelLength=4.2058f;}
 public class Item{public int type=1;}
 public static class ItemClass{public static Item GetItem(string n,bool b)=>new Item();}
 public class Bag{public int Count=3,Decrements;public int GetItemCount(Item i)=>Count;public int DecItem(Item i,int n){if(Count<n)return 0;Count-=n;Decrements++;return n;}}
 public class Body{public Vector3 velocity;}
 public class EntityVehicle{public const int cSyncStorage=1;public Bag bag=new Bag();public Body vehicleRB;public int Syncs;public void SendSyncData(int i){Syncs++;}}
 public class State{public Rules.Trigger Trigger=new Rules.Trigger();public bool Allowed=true,AP;public int Reason,Epoch=1,Shot;public float NextFire,LastShot;public EntityVehicle Vehicle=new EntityVehicle();}
 public class Shell{public EntityVehicle Vehicle;public int Epoch,Id,Actor,Tier;public bool AP;public Vector3 Position,Velocity;}
 static int tier;static Rules.Spec Spec(EntityVehicle v)=>Rules.Specs[tier];static int Tier(EntityVehicle v)=>tier;
 static System.Collections.Generic.List<Shell> shells=new System.Collections.Generic.List<Shell>();
 static bool Ready(State s,int actor)=>s.Allowed;
 static Vector3 Direction(State s)=>Vector3.zero;static Vector3 Pivot(State s)=>Vector3.zero;
 static void Broadcast(State s,byte k,int shot,Vector3 a,Vector3 b,float x,float y){Events++;}
 static void RecoilImpulse(EntityVehicle v,Vector3 d){Impulses++;}
 public static class Audio{public static class Manager{public static void SignalAI(EntityVehicle v,Vector3 p,string n,float volume){Noises++;}}}
'@
$tests=@'
 static int n;static void Check(bool x,string s){n++;if(!x)throw new System.Exception(s);}
 public static void Run(){
  var s=new State();Time.time=0;s.Trigger.Accept(7,1,true,0);Shoot(s);Check(s.Vehicle.bag.Count==2&&s.Vehicle.bag.Decrements==1&&s.Shot==1&&shells.Count==1,"one legal shot consumes one shell");Check(Events==1&&Noises==1&&Impulses==1,"one event/noise/recoil");
  Shoot(s);Check(s.Shot==1,"same-frame repeated intent cannot double-fire");Time.time=1;s.Trigger.Accept(8,1,true,1);Shoot(s);Check(s.Shot==1,"seat change cannot skip reload");
  Time.time=4.8f;Shoot(s);Check(s.Shot==1,"expired lease cannot shoot after cooldown");s.Trigger.Accept(8,2,true,4.8f);s.Reason=2;Shoot(s);Check(s.Shot==1&&s.Vehicle.bag.Count==2,"obstruction does not consume ammo");
  s.Reason=0;s.Allowed=false;Shoot(s);Check(s.Shot==1,"lost permission does not fire");s.Allowed=true;Shoot(s);Check(s.Shot==2&&s.Vehicle.bag.Count==1,"ready authorized shot succeeds");
  Time.time=10;s.Vehicle.bag.Count=0;s.Trigger.Accept(8,3,true,10);Shoot(s);Check(s.Shot==2&&Events==2&&shells.Count==2,"empty bag creates no projectile/effect");
  s.Vehicle.bag.Count=1;s.Trigger.Stop();Shoot(s);Check(s.Shot==2,"release cancels firing");
  for(tier=0;tier<4;tier++){s=new State{AP=true};Time.time=20;s.Trigger.Accept(7,1,true,20);Shoot(s);var shell=shells[shells.Count-1];Check(shell.Tier==tier&&shell.AP,"shell snapshots tier/type at fire time");Check(System.Math.Abs(s.NextFire-20-Rules.Specs[tier].Reload)<.0001,"per tier reload");}
  Console.WriteLine("PASS "+n+" actual M1 server firing checks with mocked game inventory/authority");
 }
}}
'@
Add-Type -TypeDefinition ($rules+$stub+$actual+$tests)
[PZAEC.M1.FireFixture]::Run()
