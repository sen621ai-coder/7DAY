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
public class ItemStackGrid {
 public ItemStack[] slots; public PackedBoolArray SlotLocks; public Vector2i ContainerSize;
 public int Length=>slots.Length;
 public ItemStack this[int i] {get=>slots[i];set=>slots[i]=value;}
 public System.Collections.Generic.List<ItemStack> Resize(Vector2i s){int n=s.x*s.y;var r=ItemStack.CreateArray(n);Array.Copy(slots,r,Math.Min(slots.Length,n));slots=r;ContainerSize=s;return new System.Collections.Generic.List<ItemStack>();}
 public void SetSlotLocks(PackedBoolArray l){SlotLocks=l;}
}
public class TEFeatureStorage {
 public string lootListName;
 public ItemStackGrid grid=new ItemStackGrid{ContainerSize=new Vector2i(15,10),slots=new ItemStack[0]};
 public ItemStackGrid ItemGrid=>grid;
}
public static class StorageTests {
 static void Check(bool b){if(!b)throw new Exception("Migration regression");}
 public static void Run(){
  foreach(var name in new[]{"PZAECSteelCrateStorage150","PZAECSteelWallCabinetStorage150"}) {
   var s=new TEFeatureStorage{lootListName=name};
   s.grid.slots=ItemStack.CreateArray(150);s.grid.SlotLocks=new PackedBoolArray(150);
   var before=s.grid.slots;
   for(int i=0;i<150;i++){before[i].count=i+1;s.grid.SlotLocks[i]=i%3==0;}
   AECT16RuntimeFix.CompactSteelStorage.AfterRead(s);
   Check(s.grid.ContainerSize.x==12&&s.grid.ContainerSize.y==13&&s.grid.slots.Length==156&&s.grid.SlotLocks.Length==156);
   for(int i=0;i<150;i++)Check(Object.ReferenceEquals(before[i],s.grid.slots[i])&&s.grid.slots[i].count==i+1&&s.grid.SlotLocks[i]==(i%3==0));
   for(int i=150;i<156;i++)Check(s.grid.slots[i].count==0&&!s.grid.SlotLocks[i]);
   var expanded=s.grid.slots; var locks=s.grid.SlotLocks;
   s.grid.slots[155].count=999;
   AECT16RuntimeFix.CompactSteelStorage.AfterRead(s);
   Check(Object.ReferenceEquals(expanded,s.grid.slots)&&Object.ReferenceEquals(locks,s.grid.SlotLocks)&&s.grid.slots[155].count==999);
  }
  var unrelated=new TEFeatureStorage{lootListName="other"};unrelated.grid.slots=ItemStack.CreateArray(150);
  AECT16RuntimeFix.CompactSteelStorage.AfterRead(unrelated);Check(unrelated.grid.slots.Length==150&&unrelated.grid.ContainerSize.x==15);
  var larger=new TEFeatureStorage{lootListName="PZAECSteelCrateStorage150"};larger.grid.slots=ItemStack.CreateArray(200);
  AECT16RuntimeFix.CompactSteelStorage.AfterRead(larger);Check(larger.grid.slots.Length==200&&larger.grid.ContainerSize.x==15);
 }
}
'@
$source=Get-Content (Join-Path $root '99-AEC_T16_RuntimeFix/Source/CompactSteelStorage.cs') -Raw
Add-Type -TypeDefinition ($source + $fixture)
[StorageTests]::Run()
[xml]$loot=Get-Content (Join-Path $root 'ZZ-PZAEC_StorageExpansion/Config/loot.xml') -Raw
foreach($c in $loot.SelectNodes('//lootcontainer')) { if($c.size -ne '12,13'){throw 'Incorrect dimensions'} }
Write-Output 'PASS: both full old containers retain all 150 stack references and locks; six empty slots; repeat migration preserves new slots; unrelated/larger inventories unchanged; XML 12x13.'
