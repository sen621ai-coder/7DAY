$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
$src=Get-Content "$root/ZZ-PZAEC_M1Abrams/Source/M1Service.cs" -Raw
$rules=Get-Content "$root/ZZ-PZAEC_M1Abrams/Source/M1Rules.cs" -Raw
function Extract([string]$start,[string]$end){$a=$src.IndexOf($start);$b=$src.IndexOf($end,$a);if($a -lt 0 -or $b -lt 0){throw 'Service fixture source changed'};$src.Substring($a,$b-$a)}
$migration=Extract '        static void Migrate' '        static bool BlockNativeRepair'
$clean=Extract '        static bool Clean' '        static bool CraftGuard'
$update=$src.Substring($src.IndexOf('        public static void Update'))
$stub=@'
namespace PZAEC.M1 {
 public class ItemValue{public float UseTimes;public int Meta,type=1;public ItemValue[] Modifications,CosmeticMods;public ItemClass ItemClass=new ItemClass();public int Version;public bool TryGetMetadata(string k,out int v){v=Version;return v>0;}public void SetMetadata(string k,int v){Version=v;}}
 public class ItemClass{public string Name="vehicleM1AbramsPlaceable";public string GetItemName()=>Name;public static ItemValue GetItem(string n,bool b)=>new ItemValue();}
 public static class Mathf{public static float Clamp(float v,float min,float max)=>System.Math.Max(min,System.Math.Min(max,v));}
 public class EntityPlayer{}
 public class World{public EntityPlayer GetEntity(int n)=>new EntityPlayer();}
 public static class Time{public static float time;}
 public class Bag{public int Count=3;public int DecItem(ItemValue i,int n){if(Count<n)return 0;Count-=n;return n;}}
 public class Vehicle{public int Max=1000000;public int GetMaxHealth()=>Max;}
 public class EntityVehicle{public const int cSyncItem=4,cSyncStorage=8;public int Health=100000,Syncs;public Vehicle vehicle=new Vehicle();public Bag bag=new Bag();public void SendSyncData(int n){Syncs++;}}
 public static class Weapons{public class State{public Rules.Trigger RepairTrigger=new Rules.Trigger();public float RepairStarted;public bool Eligible=true;public EntityVehicle Vehicle=new EntityVehicle();}}
 public static class ServiceFixture {
 static bool Eligible(Weapons.State s,EntityPlayer p)=>s.Eligible&&s.Vehicle.Health<s.Vehicle.vehicle.GetMaxHealth();
'@
$tests=@'
 static int checks;static void Check(bool ok,string label){checks++;if(!ok)throw new System.Exception(label);}
 public static void Run(){
  var item=new ItemValue{UseTimes=750000};Migrate(null,item);Check(item.UseTimes==500000&&item.Version==2,"old half-damaged 1.5m vehicle becomes half-damaged 1m");Migrate(null,item);Check(item.UseTimes==500000,"migration idempotent");
  item=new ItemValue{UseTimes=500000};item.ItemClass.Name="vehicleTruck4x4Placeable";Migrate(null,item);Check(item.UseTimes==500000&&item.Version==0,"other vehicles untouched");
  item=new ItemValue();Check(Clean(item),"empty new vehicle is valid upgrade input");item.UseTimes=10;Check(!Clean(item),"damaged input rejected");item.UseTimes=0;item.Meta=1;Check(!Clean(item),"fuel input rejected");item.Meta=0;item.Modifications=new[]{new ItemValue()};Check(!Clean(item),"installed modification rejected");item.Modifications=null;item.CosmeticMods=new[]{new ItemValue()};Check(!Clean(item),"cosmetic modification rejected");
  var w=new World();var s=new Weapons.State();Time.time=7.9f;s.RepairTrigger.Accept(1,1,true,7.9f);Update(w,s);Check(s.Vehicle.Health==100000&&s.Vehicle.bag.Count==3,"no early repair");
  Time.time=8;s.RepairTrigger.Accept(1,2,true,8);Update(w,s);Check(s.Vehicle.Health==1000000&&s.Vehicle.bag.Count==2&&s.Vehicle.Syncs==1,"one kit restores full health");Update(w,s);Check(s.Vehicle.Health==1000000&&s.Vehicle.bag.Count==2,"no same-frame duplicate repair");
  s=new Weapons.State();Time.time=8;s.RepairTrigger.Accept(1,1,true,7);Update(w,s);Check(s.RepairStarted==-1&&s.Vehicle.bag.Count==3,"expired lease cancels without consuming");
  s=new Weapons.State{Eligible=false};s.RepairTrigger.Accept(1,1,true,8);Update(w,s);Check(s.Vehicle.bag.Count==3&&s.RepairStarted==-1,"movement/damage/permission invalidation cancels");
  s=new Weapons.State();s.Vehicle.Health=950000;s.RepairTrigger.Accept(1,1,true,8);Update(w,s);Check(s.Vehicle.Health==1000000&&s.Vehicle.bag.Count==2,"repair caps at maximum");
  s=new Weapons.State();s.Vehicle.Health=1000000;s.RepairTrigger.Accept(1,1,true,8);Update(w,s);Check(s.Vehicle.bag.Count==3,"full health consumes no kit");
  s=new Weapons.State();s.Vehicle.bag.Count=0;s.RepairTrigger.Accept(1,1,true,8);Update(w,s);Check(s.Vehicle.Health==100000,"removed kit prevents free repair");
  foreach(var spec in Rules.Specs){s=new Weapons.State();s.Vehicle.vehicle.Max=spec.Health;s.Vehicle.Health=2;s.RepairTrigger.Accept(1,1,true,8);Update(w,s);Check(s.Vehicle.Health==spec.Health&&s.Vehicle.bag.Count==2,"all tiers fully repaired with one kit");}
  System.Console.WriteLine("PASS "+checks+" actual M1 migration/input/service checks with mocked game objects");
 }
'@
# The extracted Update suffix closes the fixture/namespace, so insert tests first.
Add-Type -TypeDefinition ('using System.Linq;'+$rules+$stub+$migration+$clean+$tests+$update)
[PZAEC.M1.ServiceFixture]::Run()
