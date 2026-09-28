using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
namespace HarmonyLib {public class Harmony{public void Patch(object a,HarmonyMethod prefix=null,HarmonyMethod postfix=null,HarmonyMethod finalizer=null){}}public class HarmonyMethod{public HarmonyMethod(Type t,string n){}}public static class AccessTools{public static object Method(Type t,string n)=>null;}}
namespace UnityEngine {public struct Vector3{public float sqrMagnitude;public static Vector3 operator-(Vector3 a,Vector3 b)=>new Vector3{ sqrMagnitude=a.sqrMagnitude+b.sqrMagnitude};}public static class Time{public static float time=100;}public static class Mathf{public static float Max(float a,float b)=>Math.Max(a,b);}}
public class ItemClass{public string Name;public string GetItemName()=>Name;}
public class ItemValue{
 public int type=1;public ItemClass ItemClass;public ItemValue[] Modifications=new ItemValue[0],CosmeticMods=new ItemValue[0];public int Q;public float UseTimes;public int Meta;
 public ItemValue(string n="empty"){ItemClass=new ItemClass{Name=n};if(n=="empty")type=0;}
 public static ItemValue None=>new ItemValue();public bool TryGetMetadata(string k,out int q){q=Q;return Q!=0;}public void SetMetadata(string k,int q){Q=q;}
 public ItemValue Clone()=>new ItemValue(ItemClass.Name){Modifications=(ItemValue[])Modifications.Clone(),CosmeticMods=(ItemValue[])CosmeticMods.Clone(),Q=Q,UseTimes=UseTimes,Meta=Meta};
}
public class ItemStack{public ItemValue itemValue;public ItemStack(ItemValue i){itemValue=i;}}
public class Body{public UnityEngine.Vector3 velocity;}
public class Persistent{public string PrimaryId="owner";}
public class EntityPlayer{public object AttachedToEntity;public bool Dead;public UnityEngine.Vector3 position;public Persistent PersistentPlayerData=new Persistent();public bool IsDead()=>Dead;}
public class EntityVehicle{
 public const ushort cSyncItem=4;public Vehicle vehicle;public int Syncs;public bool hasDriver,Dead,Gunner;public UnityEngine.Vector3 position;public Body vehicleRB=new Body();public string Owner="owner";
 public EntityVehicle(string name="vehicleM1Abrams"){vehicle=new Vehicle(this,name);}
 public bool IsDead()=>Dead;public object GetAttached(int i)=>Gunner?new object():null;public object GetOwner()=>Owner;public bool IsUserAllowed(string id)=>id==Owner;public void SendSyncData(ushort flags){Syncs++;}
}
public class Vehicle{public EntityVehicle entity;public ItemValue itemValue;public string Name;public float EffectMotorTorquePer,EffectVelocityMaxPer,EffectFuelUsePer;public int Health=1000000;public Vehicle(EntityVehicle e,string name){entity=e;Name=name;itemValue=new ItemValue(name+"Placeable");}public string GetName()=>Name;public int GetHealth()=>Health;}
public class World{public EntityPlayer Player=new EntityPlayer();public EntityPlayer GetPrimaryPlayer()=>Player;public object GetEntity(int n)=>Player;}
public class GameManager{public static GameManager Instance=new GameManager();public World World=new World();public static void ShowTooltip(EntityPlayer p,string s){}}
public static class Log{public static void Warning(string s){}}
public class PlayerUI{public EntityPlayer entityPlayer=GameManager.Instance.World.Player;}
public class Assemble{public ItemStack CurrentItem;}
public class XUi{public Assemble AssembleItem=new Assemble();public PlayerUI playerUI=new PlayerUI();}
public class Window{public object Controller;}
public class XUiC_BasePartStack{public XUi xui=new XUi();public Window WindowGroup;public int SlotNumber;}
public class XUiC_ItemPartStack:XUiC_BasePartStack{}
public class XUiC_ItemCosmeticStack:XUiC_BasePartStack{}
public class XUiC_VehicleWindowGroup{public EntityVehicle CurrentVehicleEntity;}
namespace PZAEC.M1 {
 public static class Weapons{
  public static bool Server=true;static Dictionary<EntityVehicle,State> states=new Dictionary<EntityVehicle,State>();
  public class State{public Rules.Trigger Trigger=new Rules.Trigger();public float NextFire,LastShot=-100,LastDamage=-100,LastWeaponActivity=-100;}
  public static bool IsTank(EntityVehicle v)=>v!=null&&Rules.Index(v.vehicle.GetName())>=0;
  public static int Tier(EntityVehicle v)=>Rules.Index(v.vehicle.GetName());public static Rules.Spec Spec(EntityVehicle v)=>Rules.Specs[Tier(v)];
  public static State Register(EntityVehicle v){if(!states.TryGetValue(v,out var s)){s=new State();states[v]=s;}return s;}
 }
 public static class ModuleTests{
  static int n;static void Check(bool ok,string label){n++;if(!ok)throw new Exception(label);}static void Near(float a,float b,string label)=>Check(Math.Abs(a-b)<.0001,label);
  static object Call(string name,params object[] args)=>typeof(Modules).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);
  static ItemValue[] Items(params int[] indexes)=>indexes.Select(i=>new ItemValue(ModuleRules.Names[i])).ToArray();
  static void Effects(Vehicle v){object[] args={v,null};Call("BeforeEffects",args);v.EffectMotorTorquePer=2;v.EffectVelocityMaxPer=1;v.EffectFuelUsePer=2;Call("AfterEffects",v,args[1]);Call("RestoreEffects",v,args[1],null);}
  public static void Run(){
   for(int tier=0;tier<4;tier++)for(int bits=0;bits<64;bits++){
    var indexes=Enumerable.Range(0,6).Where(i=>(bits&(1<<i))!=0).ToArray();bool expected=indexes.Length<=(tier<2?2:3)&&(bits&3)!=3&&(bits&48)!=48;
    bool valid=ModuleRules.Validate(tier,indexes.Select(i=>ModuleRules.Names[i]).ToArray(),out int mask);Check(valid==expected,"combination legality");Check(mask==(valid?bits:0),"invalid combination has zero effects");
    if(valid){for(int r=0;r<5;r++){float armor=ModuleRules.Protection(tier,r,false,mask);Check(armor<=.800001f,"armor cap");Check(ModuleRules.Protection(tier,r,true,mask)<=armor,"acid never stronger than normal");Check(ModuleRules.Damage(0,tier,r,false,mask)==0,"zero damage");}}
   }
   Check(!ModuleRules.Validate(0,new[]{ModuleRules.Names[0],ModuleRules.Names[0]},out int dummy),"duplicate denied");Check(!ModuleRules.Validate(0,new[]{"modVehicleSuperCharger"},out dummy),"generic denied");
   Near(ModuleRules.Reload(32),.75f,"reload");Near(ModuleRules.Tracking(16),1.5f,"tracking");Near(ModuleRules.Recoil(16),.4f,"recoil");Near(ModuleRules.Fuel(2),.6f,"economy");
   Check(ModuleRules.Damage(100000,3,4,true,12)==20000,"T19 combined roof acid");Near(Rules.Specs[0].Reload*ModuleRules.Reload(32),3.6f,"T16 reload");
   var e=new EntityVehicle();e.vehicle.itemValue.Modifications=Items(0);e.vehicle.itemValue.UseTimes=123;e.vehicle.itemValue.Meta=42;var original=e.vehicle.itemValue;
   Effects(e.vehicle);Check(ReferenceEquals(e.vehicle.itemValue,original),"native item restored");Near(e.vehicle.EffectMotorTorquePer,2.6f,"actual torque hook");Near(e.vehicle.EffectVelocityMaxPer,1.25f,"actual speed hook");Near(e.vehicle.EffectFuelUsePer,2,"power no fuel penalty");
   Effects(e.vehicle);Near(e.vehicle.EffectMotorTorquePer,2.6f,"repeated calc no accumulation");Check(original.UseTimes==123&&original.Meta==42,"health fuel untouched");
   e.vehicle.itemValue.Modifications=Items(1);Effects(e.vehicle);Near(e.vehicle.EffectFuelUsePer,1.2f,"actual fuel multiplier");Near(e.vehicle.EffectMotorTorquePer,2,"economy no torque penalty");
   e.vehicle.itemValue.Modifications=Items(0,1);Effects(e.vehicle);Check(Modules.Get(e)==0&&Modules.Invalid(e),"invalid modules disabled retained");Check(e.vehicle.itemValue.Modifications.Length==2,"invalid modules not deleted");
   object[] bare={e.vehicle,null};Call("BeforeEffects",bare);Check(e.vehicle.itemValue.Modifications.Length==0,"native effects cannot import invalid mods");Call("RestoreEffects",e.vehicle,bare[1],new Exception());Check(ReferenceEquals(e.vehicle.itemValue,original),"exception restores item");
   for(int tier=0;tier<4;tier++){var item=new ItemValue("vehicleM1Abrams"+(tier==0?"":"T"+(16+tier))+"Placeable"){UseTimes=222,Meta=333};Call("ReadItem",item);Check(item.Modifications.Length==(tier<2?2:3),"old item slot expansion");Check(item.UseTimes==222&&item.Meta==333,"migration preserves health/fuel");Call("ReadItem",item);Check(item.Modifications.All(x=>x!=null),"empty slots usable");}
   e.vehicle.itemValue.Modifications=Items(0,1,2,3);Call("ReadItem",e.vehicle.itemValue);Check(e.vehicle.itemValue.Modifications.Length==4,"legacy overcapacity preserved");
   var other=new EntityVehicle("vehicleTruck4x4");Effects(other.vehicle);Near(other.vehicle.EffectMotorTorquePer,2,"non M1 untouched");
   var v=new EntityVehicle();Effects(v.vehicle);var incoming=new ItemValue("vehicleM1AbramsPlaceable"){Modifications=Items(5)};
   object[] begin={7,null};Call("BeginSync",begin);Call("LoadItems",v.vehicle,new[]{new ItemStack(incoming)});Check(incoming.Q==0,"authorized parked server update");
   v.hasDriver=true;Call("LoadItems",v.vehicle,new[]{new ItemStack(incoming)});Check(incoming.Q==1,"occupied remote update quarantined");Check(incoming.Modifications.Length==1,"quarantine preserves items");v.hasDriver=false;
   incoming.Q=0;GameManager.Instance.World.Player.PersistentPlayerData.PrimaryId="stranger";Call("LoadItems",v.vehicle,new[]{new ItemStack(incoming)});Check(incoming.Q==1,"unauthorized remote update quarantined");GameManager.Instance.World.Player.PersistentPlayerData.PrimaryId="owner";
   Call("EndSync",v,(ushort)4,begin[1],null);Check(v.Syncs==1,"server correction includes original sender");
   v.vehicle.itemValue.Modifications=Items(5);Effects(v.vehicle);Near(Modules.Reload(v),3.6f,"actual reload adapter");Check(Weapons.Register(v).NextFire>=103.6f,"module change resets reload");
   var slot=new XUiC_ItemPartStack{SlotNumber=0,WindowGroup=new Window{Controller=new XUiC_VehicleWindowGroup{CurrentVehicleEntity=v}}};slot.xui.AssembleItem.CurrentItem=new ItemStack(v.vehicle.itemValue);v.hasDriver=true;object[] swap={slot,new ItemStack(new ItemValue(ModuleRules.Names[0])),true};Check(!(bool)Call("CanSwap",swap)&&!(bool)swap[2],"occupied UI blocked before transfer");v.hasDriver=false;
   var cosmetic=new XUiC_ItemCosmeticStack();cosmetic.xui.AssembleItem.CurrentItem=new ItemStack(v.vehicle.itemValue);object[] cosmeticArgs={cosmetic,new ItemStack(new ItemValue(ModuleRules.Names[0])),true};Check(!(bool)Call("CosmeticSwap",cosmeticArgs)&&!(bool)cosmeticArgs[2],"cosmetic slot cannot bypass limits");
   Console.WriteLine("PASS "+n+" actual module rules, hooks, migration and server/UI validation checks");
  }
 }
}
