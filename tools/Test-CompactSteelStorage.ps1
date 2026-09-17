$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$fixture=@'
namespace HarmonyLib {
 public class HarmonyMethod { public HarmonyMethod(Type t,string n){} }
 public class Harmony { public void Patch(object m,HarmonyMethod postfix=null){} }
 public static class AccessTools { public static object Method(Type t,string n)=>null; }
}
public struct Vector2i { public int x,y; public Vector2i(int a,int b){x=a;y=b;} }
public class ItemStack {
 public int count; public object Metadata=new object();
 public static ItemStack[] CreateArray(int n){var a=new ItemStack[n];for(int i=0;i<n;i++)a[i]=new ItemStack();return a;}
}
public class PackedBoolArray {
 bool[] bits; public PackedBoolArray(int n){bits=new bool[n];} public int Length=>bits.Length;
 public bool this[int i] {get=>bits[i];set=>bits[i]=value;}
}
public class TEFeatureStorage {
 public string lootListName; public ItemStack[] items; public PackedBoolArray SlotLocks;
 public Vector2i size=new Vector2i(15,10); public Vector2i GetContainerSize()=>size;
 public void SetContainerSize(Vector2i s,bool clear){if(clear)throw new Exception("Destructive resize");size=s;}
}
public static class StorageTests {
 static void Check(bool b){if(!b)throw new Exception("Migration regression");}
 public static void Run(){
  foreach(var name in new[]{"PZAECSteelCrateStorage150","PZAECSteelWallCabinetStorage150"}) {
   var s=new TEFeatureStorage{lootListName=name,items=ItemStack.CreateArray(150),SlotLocks=new PackedBoolArray(150)};
   var before=s.items;
   for(int i=0;i<150;i++){before[i].count=i+1;s.SlotLocks[i]=i%3==0;}
   AECT16RuntimeFix.CompactSteelStorage.AfterRead(s);
   Check(s.size.x==12&&s.size.y==13&&s.items.Length==156&&s.SlotLocks.Length==156);
   for(int i=0;i<150;i++)Check(Object.ReferenceEquals(before[i],s.items[i])&&s.items[i].count==i+1&&s.SlotLocks[i]==(i%3==0));
   for(int i=150;i<156;i++)Check(s.items[i].count==0&&!s.SlotLocks[i]);
   var expanded=s.items; var locks=s.SlotLocks;
   s.items[155].count=999;
   AECT16RuntimeFix.CompactSteelStorage.AfterRead(s);
   Check(Object.ReferenceEquals(expanded,s.items)&&Object.ReferenceEquals(locks,s.SlotLocks)&&s.items[155].count==999);
  }
  var unrelated=new TEFeatureStorage{lootListName="other",items=ItemStack.CreateArray(150)};
  AECT16RuntimeFix.CompactSteelStorage.AfterRead(unrelated);Check(unrelated.items.Length==150&&unrelated.size.x==15);
  var larger=new TEFeatureStorage{lootListName="PZAECSteelCrateStorage150",items=ItemStack.CreateArray(200)};
  AECT16RuntimeFix.CompactSteelStorage.AfterRead(larger);Check(larger.items.Length==200&&larger.size.x==15);
 }
}
'@
$source=Get-Content (Join-Path $root '99-AEC_T16_RuntimeFix/Source/CompactSteelStorage.cs') -Raw
Add-Type -TypeDefinition ($source + $fixture)
[StorageTests]::Run()
[xml]$loot=Get-Content (Join-Path $root 'ZZ-PZAEC_StorageExpansion/Config/loot.xml') -Raw
foreach($c in $loot.SelectNodes('//lootcontainer')) { if($c.size -ne '12,13'){throw 'Incorrect dimensions'} }
Write-Output 'PASS: both full old containers retain all 150 stack references and locks; six empty slots; repeat migration preserves new slots; unrelated/larger inventories unchanged; XML 12x13.'
