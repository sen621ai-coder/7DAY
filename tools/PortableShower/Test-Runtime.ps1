$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
$source=Get-Content "$root/ZZZ-PZAEC_PortableShower/Source/PortableShower.cs" -Raw
$fixture=@'
namespace UnityEngine {public static class Time {public static float time;}}
namespace PZAEC.PortableShower {public static class Visuals {public static void Set(EntityPlayerLocal p,bool active){}}}
public class Mod {} public interface IModApi {void InitMod(Mod mod);} public static class Log {public static void Out(string s){}}
public class MinEventParams {public EntityAlive Self;} public class MinEventActionBase {public virtual void Execute(MinEventParams p){}}
public class TagGroup {public class Global {}}
public struct FastTags<T> {public string Tags; public static FastTags<T> Parse(string s)=>new FastTags<T>{Tags=s};public bool Test_AnySet(FastTags<T> b)=>Tags==b.Tags;}
public class ItemClass {public string Name;public FastTags<TagGroup.Global> ItemTags;public string GetItemName()=>Name;public static ItemValue GetItem(string n,bool b)=>new ItemValue{type=1,ItemClass=new ItemClass{Name=n}};}
public class ItemValue {public int type=1;public ItemClass ItemClass;public ItemValue[] Modifications;public bool IsEmpty()=>type==0;}
public class Equipment {public ItemValue[] Items;public ItemValue[] GetItems()=>Items;}
public class Bag {public int Count,Spent;public bool Fail;public int GetItemCount(ItemValue i)=>Count;public int DecItem(ItemValue i,int n){if(Fail||Count<n)return 0;Count-=n;Spent+=n;return n;}}
public class EntityAlive {public EntityBuffs Buffs=new EntityBuffs();}
public class EntityPlayer:EntityAlive {}
public class EntityPlayerLocal:EntityPlayer {public object world=new object(),AttachedToEntity;public bool Dead,IsRunning,MovementRunning,Swimming;public Equipment equipment=new Equipment();public Bag bag=new Bag();public bool IsDead()=>Dead;public bool CalcIfSwimming()=>Swimming;}
public class EntityBuffs {
 public int RefillNotifications;
 public System.Collections.Generic.Dictionary<string,float> Vars=new System.Collections.Generic.Dictionary<string,float>();
 public System.Collections.Generic.HashSet<string> Buffs=new System.Collections.Generic.HashSet<string>();
 public float GetCustomVar(string s)=>Vars.TryGetValue(s,out var v)?v:0;public void SetCustomVar(string s,float v,bool n)=>Vars[s]=v;
 public bool HasBuff(string s)=>Buffs.Contains(s);public void AddBuff(string s){Buffs.Add(s);if(s=="buffPZAECShowerRefilled")RefillNotifications++;}public void RemoveBuff(string s)=>Buffs.Remove(s);
}
public static class ShowerTests {
 static int count;static void Check(bool b,string s){count++;if(!b)throw new System.Exception(s);}
 static void Near(float a,float b,string s){Check(System.Math.Abs(a-b)<.002,s+": "+a+" != "+b);}
 static EntityPlayerLocal New(int water=10){var p=new EntityPlayerLocal();p.bag.Count=water;p.Buffs.SetCustomVar("$HygieneTotal",100,true);Equip(p);PZAEC.PortableShower.Cleaner.Tick(p);return p;}
 static void Equip(EntityPlayerLocal p){p.equipment.Items=new[]{new ItemValue{ItemClass=new ItemClass{ItemTags=FastTags<TagGroup.Global>.Parse("armorHands")},Modifications=new[]{ItemClass.GetItem("modPZAECWristShower",false)}}};}
 static void Tick(EntityPlayerLocal p,int n=1){for(int i=0;i<n;i++){UnityEngine.Time.time+=1;PZAEC.PortableShower.Cleaner.Tick(p);}}
 public static void Run(){
  var p=New();Tick(p,29);Check(p.bag.Spent==0,"login requires full 30 second delay");Tick(p);Check(p.bag.Spent==1,"one refill after delay");Near(p.Buffs.GetCustomVar("$PZAECShowerWaterSeconds"),59,"first second charged");
  Tick(p,19);Near(p.Buffs.GetCustomVar("$PZAECShowerWaterSeconds"),40,"twenty seconds use");
  PZAEC.PortableShower.Cleaner.Combat(p);Check(!p.Buffs.HasBuff("buffPZAECShowerActive"),"combat clears active immediately");Tick(p,29);Near(p.Buffs.GetCustomVar("$PZAECShowerWaterSeconds"),40,"combat preserves water");Tick(p);Near(p.Buffs.GetCustomVar("$PZAECShowerWaterSeconds"),39,"resume after full delay");
  foreach(var mode in new[]{"run","moveRun","swim","vehicle","dead","bath","unequip"}){
   float before=p.Buffs.GetCustomVar("$PZAECShowerWaterSeconds");int spent=p.bag.Spent;
   p.IsRunning=mode=="run";p.MovementRunning=mode=="moveRun";p.Swimming=mode=="swim";p.Dead=mode=="dead";p.AttachedToEntity=mode=="vehicle"?new object():null;
   if(mode=="bath")p.Buffs.AddBuff("buffBathStatus");if(mode=="unequip")p.equipment.Items=new ItemValue[0];
   Tick(p,3);Near(p.Buffs.GetCustomVar("$PZAECShowerWaterSeconds"),before,mode+" keeps water");Check(p.bag.Spent==spent&&!p.Buffs.HasBuff("buffPZAECShowerActive"),mode+" no cleaning");
   p.IsRunning=p.MovementRunning=p.Swimming=p.Dead=false;p.AttachedToEntity=null;p.Buffs.RemoveBuff("buffBathStatus");Equip(p);
  }
  float stored=p.Buffs.GetCustomVar("$PZAECShowerWaterSeconds");var restored=New();restored.Buffs.Vars=new System.Collections.Generic.Dictionary<string,float>(p.Buffs.Vars);Tick(restored,29);Near(restored.Buffs.GetCustomVar("$PZAECShowerWaterSeconds"),stored,"relogin retains water");Tick(restored);Near(restored.Buffs.GetCustomVar("$PZAECShowerWaterSeconds"),stored-1,"saved water used before bottles");Check(restored.bag.Spent==0,"no refill on relog");
  var reserve=New(3);Tick(reserve,30);Tick(reserve,60);Check(reserve.bag.Count==2&&reserve.bag.Spent==1,"last two reserved");Check(reserve.Buffs.HasBuff("buffPZAECShowerEmpty"),"water shortage UI");
  var failed=New(3);failed.bag.Fail=true;Tick(failed,35);Near(failed.Buffs.GetCustomVar("$HygieneStatus"),0,"failed removal grants no cleaning");Near(failed.Buffs.GetCustomVar("$PZAECShowerWaterSeconds"),0,"failed removal grants no water");
  var clean=New();clean.Buffs.SetCustomVar("$HygieneStatus",100,true);Tick(clean,35);Check(clean.bag.Spent==0,"clean body does not refill");
  clean.Buffs.SetCustomVar("$HygieneStatus",99.9f,true);Tick(clean);Near(clean.Buffs.GetCustomVar("$HygieneStatus"),100,"caps hygiene");Near(clean.Buffs.GetCustomVar("$PZAECShowerWaterSeconds"),59.7f,"partial last second costs only useful water");Tick(clean,5);Near(clean.Buffs.GetCustomVar("$PZAECShowerWaterSeconds"),59.7f,"full body retains remainder");
  var full=New();Tick(full,329);Near(full.Buffs.GetCustomVar("$HygieneStatus"),100,"300 seconds restores full bar without natural dirt");Check(full.bag.Spent==5,"five bottles full clean");
  var lag=New();Tick(lag,30);float h=lag.Buffs.GetCustomVar("$HygieneStatus");UnityEngine.Time.time+=3600;PZAEC.PortableShower.Cleaner.Tick(lag);Near(lag.Buffs.GetCustomVar("$HygieneStatus"),h+100f/300,"no lag/offline catchup");
  var remote=new EntityPlayer();new MinEventActionPZAECShowerTick().Execute(new MinEventParams{Self=remote});new MinEventActionPZAECShowerCombat().Execute(new MinEventParams{Self=remote});Check(remote.Buffs.Vars.Count==0,"remote/server replicas never write");
  var dup=New();dup.equipment.Items[0].Modifications=new[]{ItemClass.GetItem("modPZAECWristShower",false),ItemClass.GetItem("modPZAECWristShower",false)};Tick(dup,30);Near(dup.Buffs.GetCustomVar("$HygieneStatus"),100f/300,"duplicate mods do not stack");
  Check(PZAEC.PortableShower.Rules.Clamp(float.NaN,60)==0,"invalid stored water rejected");
  Check(full.Buffs.RefillNotifications==5,"exactly one notification per consumed bottle across five refills");
  Check(p.Buffs.RefillNotifications==1,"combat, movement and equipment pauses do not repeat notification");
  Check(restored.Buffs.RefillNotifications==0,"resuming saved water never notifies");
  Check(reserve.Buffs.RefillNotifications==1,"water shortage never repeats notification");
  Check(failed.Buffs.RefillNotifications==0,"failed water consumption never notifies");
  System.Console.WriteLine("PASS "+count+" runtime behavior checks (actual source with game API fixtures).");
 }
}
'@
Add-Type -TypeDefinition ($source+"`n"+$fixture)
[ShowerTests]::Run()
Add-Type -Path "$root/0_TFP_Harmony/Mono.Cecil.dll"
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path (Split-Path $root) '7DaysToDie_Data/Managed/Assembly-CSharp.dll'))
$events=($assembly.MainModule.Types|Where-Object Name -eq 'MinEventTypes').Fields.Name
[xml]$xml=Get-Content "$root/ZZZ-PZAEC_PortableShower/Config/buffs.xml" -Raw
foreach($effect in $xml.SelectNodes('//triggered_effect')){if($effect.trigger -notin $events){throw "Unknown native event: $($effect.trigger)"}}
$parser=($assembly.MainModule.Types|Where-Object Name -eq 'MinEventActionBase').Methods|Where-Object Name -eq 'ParseAction'
if(!($parser.Body.Instructions|Where-Object {($_.Operand -as [string]) -eq 'MinEventAction'})){throw 'Native custom action parser changed'}
$assembly.Dispose()
Write-Output 'PASS native event names and action factory prefix.'
