#Requires -Version 7.0
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$source=Get-Content "$root/99-AEC_T16_RuntimeFix/Source/LootBagWorldLifetime.cs" -Raw
$stubs=@'
namespace HarmonyLib {
 public class Harmony { public void Patch(object method, HarmonyMethod prefix) {} }
 public class HarmonyMethod { public HarmonyMethod(System.Type type,string name) {} }
 public static class AccessTools { public static object Method(System.Type type,string name)=>null; }
}
public class World { public ulong worldTime; public bool Remote; public bool IsRemote()=>Remote; }
public class EntityLootContainer { public World world; public ulong WorldTimeBorn; public void OnUpdateEntity() {} }
namespace AECT16RuntimeFix { public static class T16RuntimeFixMod { public static void SafeLog(string text) {} } }
public static class LootLifetimeRegression {
 static void Check(bool value,string message) { if(!value)throw new System.Exception(message); }
 public static void Run() {
  Check(!AECT16RuntimeFix.LootBagWorldLifetime.Expired(7000,30999),"Not before 24 hours");
  Check(AECT16RuntimeFix.LootBagWorldLifetime.Expired(7000,31000),"Exact 24 hour boundary");
  Check(AECT16RuntimeFix.LootBagWorldLifetime.Expired(7000,90000),"Unloaded overdue bag expires on return");
  Check(!AECT16RuntimeFix.LootBagWorldLifetime.Expired(7000,6000),"Clock rewind cannot underflow");
  Check(AECT16RuntimeFix.LootBagWorldLifetime.Expired(0,24000),"World start is a valid birth time");
  Check(!AECT16RuntimeFix.LootBagWorldLifetime.Expired(ulong.MaxValue-100,ulong.MaxValue),"No deadline overflow");
  var e=new EntityLootContainer{WorldTimeBorn=7000,world=new World{worldTime=31000}};
  int age=200000,limit=1;
  AECT16RuntimeFix.LootBagWorldLifetime.BeforeUpdate(e,ref age,ref limit);
  Check(age>=limit-1,"Server triggers native expiration");
  e.world.Remote=true;
  AECT16RuntimeFix.LootBagWorldLifetime.BeforeUpdate(e,ref age,ref limit);
  Check(age<limit-1,"Client never initiates timed removal");
  e.world.Remote=false;e.world.worldTime=30999;
  AECT16RuntimeFix.LootBagWorldLifetime.BeforeUpdate(e,ref age,ref limit);
  Check(age<limit-1 && e.WorldTimeBorn==7000,"Loaded ticks cannot shorten or reset lifetime");
 }
}
'@
Add-Type -TypeDefinition ($source+[Environment]::NewLine+$stubs)
[LootLifetimeRegression]::Run()
Add-Type -Path "$root/0_TFP_Harmony/Mono.Cecil.dll"
$game=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path (Split-Path $root) '7DaysToDie_Data/Managed/Assembly-CSharp.dll'))
try {
 $entity=$game.MainModule.Types|Where-Object Name -eq Entity
 foreach($method in 'Awake','Read','Write') {
  $il=($entity.Methods|Where-Object Name -eq $method).Body.Instructions
  if(-not ($il.Operand -match 'Entity::WorldTimeBorn')){throw "Native birth time missing in $method"}
 }
 $loot=$game.MainModule.Types|Where-Object Name -eq EntityLootContainer
 foreach($field in 'deathUpdateTime','timeStayAfterDeath') {
  if(-not ($loot.Fields|Where-Object {$_.Name -eq $field -and $_.FieldType.FullName -eq 'System.Int32'})){throw "Native field changed: $field"}
 }
 $update=($loot.Methods|Where-Object Name -eq OnUpdateEntity).Body.Instructions
 foreach($expected in 'LockManager::IsLockedServer','Bag::IsEmpty','EntityLootContainer::removeBackpack') {
  if(-not ($update.Operand -match [regex]::Escape($expected))){throw "Native loot behavior changed: $expected"}
 }
 $backpack=$game.MainModule.Types|Where-Object Name -eq EntityBackpack
 if($backpack.BaseType.Name -eq 'EntityLootContainer'){throw 'Player backpacks would be affected'}
} finally {$game.Dispose()}
Write-Output 'PASS: 9 lifetime cases; native birth-time persistence, timer fields, loot locks, empty removal and backpack exclusion.'
