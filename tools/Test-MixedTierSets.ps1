$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$source=Get-Content "$root/99-AEC_T16_RuntimeFix/Source/ArmorFamilySets.cs" -Raw
$runtime=Get-Content "$root/99-AEC_T16_RuntimeFix/Source/EndgameExpansionRuntime.cs" -Raw
$automatic=[regex]::Match($runtime,'(?s)public static void UpdateAutomaticResonance\(EntityPlayer player\).*?(?=        private static void UseEvacAnchor)').Value
if(!$automatic){throw 'Automatic resonance method not found'}
$source="using System.Linq;`n"+$source+"`nnamespace AECT16RuntimeFix { public static class EndgameExpansionRuntime { "+$automatic+" private static void ApplyCalibration(EntityPlayer player,ItemValue helmet,string family){} } }"
$fixture=@'
public enum CVarOperation {set}
public class Equipment {
 public ItemValue[] GetItems()=>new ItemValue[0];
 public class ArmorGroupInfo {public int Count;}
 public System.Collections.Generic.Dictionary<string,ArmorGroupInfo> ArmorGroupEquipped=new System.Collections.Generic.Dictionary<string,ArmorGroupInfo>();
 public int GetArmorGroupCount(string name){int n=0;if(!AECT16RuntimeFix.ArmorFamilySets.CountPrefix(this,name,ref n))return n;return ArmorGroupEquipped.TryGetValue(name,out var i)?i.Count:0;}
}
public class EntityBuffs {
 public System.Collections.Generic.Dictionary<string,float> Vars=new System.Collections.Generic.Dictionary<string,float>();
 public System.Collections.Generic.HashSet<string> Buffs=new System.Collections.Generic.HashSet<string>();
 public float GetCustomVar(string name)=>Vars.TryGetValue(name,out var x)?x:0;
 public void SetCustomVar(string name,float x,bool sync,CVarOperation op){Vars[name]=x;}
 public bool HasBuff(string name)=>Buffs.Contains(name);
 public void AddBuff(string name,int id,bool sync){Buffs.Add(name);}
 public void RemoveBuff(string name,int id,bool sync){Buffs.Remove(name);}
}
public class World {public bool Remote;public bool IsRemote()=>Remote;}
public class ItemClass {public string GetItemName()=>"";}
public class ItemValue {public ItemClass ItemClass;}
public class EntityPlayer {public Equipment equipment=new Equipment();public EntityBuffs Buffs=new EntityBuffs();public World world=new World();public int entityId;public bool Dead;public bool IsDead()=>Dead;}
namespace HarmonyLib {
 public static class AccessTools {public static System.Reflection.MethodInfo Method(System.Type t,string name,System.Type[] args)=>null;}
 public class HarmonyMethod {public HarmonyMethod(System.Type t,string name){}}
 public class Harmony {public void Patch(System.Reflection.MethodInfo m,HarmonyMethod prefix){}}
}
public static class MixedSetTests {
 static int n;static void Check(bool ok,string message){n++;if(!ok)throw new System.Exception(message);}
 public static void Run(){
  foreach(string family in AECT16RuntimeFix.ArmorFamilySets.Families){
   for(int combo=0;combo<625;combo++){
    var e=new Equipment();int k=combo,total=0,lowest=20;
    for(int slot=0;slot<4;slot++){int t=k%5;k/=5;if(t==0)continue;int tier=t+15;total++;lowest=System.Math.Min(lowest,tier);
     string key="groupPZAEC"+family+"T"+tier;if(!e.ArmorGroupEquipped.ContainsKey(key))e.ArmorGroupEquipped[key]=new Equipment.ArmorGroupInfo();e.ArmorGroupEquipped[key].Count++;
    }
    Check(e.GetArmorGroupCount("groupPZAEC"+family)==total,"family count all combinations");int sum=0;
    for(int tier=16;tier<=19;tier++){int count=e.GetArmorGroupCount("groupPZAEC"+family+"T"+tier);sum+=count;Check(count==(tier==lowest?total:0),"only lowest tier receives family count");}
    Check(sum==total,"no duplicated multi-tier bonuses");
    Check(e.GetArmorGroupCount("groupPZAECUnknownT16")==0,"unrelated family excluded");
   }
   var p=new EntityPlayer();p.equipment.ArmorGroupEquipped["groupPZAEC"+family+"T16"]=new Equipment.ArmorGroupInfo{Count=2};p.equipment.ArmorGroupEquipped["groupPZAEC"+family+"T17"]=new Equipment.ArmorGroupInfo{Count=2};
   p.Buffs.Vars["$PZAEC"+family+"T16Resonance"]=65;p.Buffs.Vars["$PZAEC"+family+"T17Resonance"]=80;
   AECT16RuntimeFix.ArmorFamilySets.MigrateCharge(p,family);string shared=AECT16RuntimeFix.ArmorFamilySets.Charge(family);
   Check(p.Buffs.GetCustomVar(shared)==80,"legacy migration uses max, never adds charges");
   Check(p.Buffs.GetCustomVar("$PZAEC"+family+"T17Resonance")==0,"legacy counter retired");
   p.equipment.ArmorGroupEquipped.Remove("groupPZAEC"+family+"T16");p.equipment.ArmorGroupEquipped["groupPZAEC"+family+"T17"].Count=4;
   AECT16RuntimeFix.ArmorFamilySets.MigrateCharge(p,family);Check(p.Buffs.GetCustomVar(shared)==80,"upgrade preserves shared charge");
   p.Buffs.Buffs.Add("buffPZAEC"+family+"T16Cooldown");Check(AECT16RuntimeFix.ArmorFamilySets.Busy(p,family),"old tier cooldown blocks upgraded tier");
   p.Buffs.Buffs.Clear();Check(!AECT16RuntimeFix.ArmorFamilySets.Busy(p,family),"expired cooldown allows activation");
   p.equipment.ArmorGroupEquipped["groupPZAEC"+family+"T17"].Count=2;AECT16RuntimeFix.ArmorFamilySets.MigrateCharge(p,family);Check(p.Buffs.GetCustomVar(shared)==0,"below three pieces clears charge");
   p.equipment.ArmorGroupEquipped["groupPZAEC"+family+"T16"]=new Equipment.ArmorGroupInfo{Count=2};p.Buffs.Vars[shared]=100;
   AECT16RuntimeFix.EndgameExpansionRuntime.UpdateAutomaticResonance(p);
   Check(p.Buffs.HasBuff("buffPZAEC"+family+"T16Active")&&p.Buffs.HasBuff("buffPZAEC"+family+"T16Cooldown")&&p.Buffs.GetCustomVar(shared)==0,"actual auto method activates mixed four-piece at T16");
   p.equipment.ArmorGroupEquipped.Remove("groupPZAEC"+family+"T16");p.equipment.ArmorGroupEquipped["groupPZAEC"+family+"T17"].Count=4;p.Buffs.Vars[shared]=100;
   AECT16RuntimeFix.EndgameExpansionRuntime.UpdateAutomaticResonance(p);
   Check(!p.Buffs.HasBuff("buffPZAEC"+family+"T16Active")&&!p.Buffs.HasBuff("buffPZAEC"+family+"T17Active")&&p.Buffs.GetCustomVar(shared)==100,"actual upgrade cancels old active, preserves charge, respects old cooldown");
   p.Buffs.RemoveBuff("buffPZAEC"+family+"T16Cooldown",-1,true);AECT16RuntimeFix.EndgameExpansionRuntime.UpdateAutomaticResonance(p);
   Check(p.Buffs.HasBuff("buffPZAEC"+family+"T17Active")&&p.Buffs.GetCustomVar(shared)==0,"actual upgraded set fires after prior cooldown ends");
   p.equipment.ArmorGroupEquipped["groupPZAEC"+family+"T17"].Count=3;p.Buffs.Vars[shared]=80;AECT16RuntimeFix.EndgameExpansionRuntime.UpdateAutomaticResonance(p);
   Check(!p.Buffs.HasBuff("buffPZAEC"+family+"T17Active")&&p.Buffs.GetCustomVar(shared)==80,"actual three-piece retains charge without active");
   p.equipment.ArmorGroupEquipped["groupPZAEC"+family+"T17"].Count=2;AECT16RuntimeFix.EndgameExpansionRuntime.UpdateAutomaticResonance(p);Check(p.Buffs.GetCustomVar(shared)==0,"actual removal below three clears shared counter");
  }
  var native=new Equipment();native.ArmorGroupEquipped["groupRanger"]=new Equipment.ArmorGroupInfo{Count=3};Check(native.GetArmorGroupCount("groupRanger")==3,"native group unaffected");
  System.Console.WriteLine("PASS "+n+" mixed-tier counting, migration and cooldown checks");
 }
}
'@
Add-Type -TypeDefinition ($source+$fixture)
[MixedSetTests]::Run()
