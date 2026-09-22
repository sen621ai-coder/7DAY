$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$harmony=Join-Path $root '0_TFP_Harmony/0Harmony.dll'
[void][Reflection.Assembly]::LoadFrom($harmony)
$fixture=@'
public enum EnumDamageTypes { Bashing,Heat,Falling,VehicleInside,Suicide,Starvation,Disease }
public enum EnumDamageSource { External,Internal }
public class DamageSource {public EnumDamageTypes damageType;public EnumDamageSource damageSource;}
public struct DamageResponse {public DamageSource Source;public int Strength,ModStrength;public bool Fatal;}
public class ItemValue {}
public static class Log {public static void Out(string s){System.Console.WriteLine(s);}public static void Error(string s){throw new System.Exception(s);}}
public class EntityAlive {
 public int Health=1000,Max=1000;public object AttachedToEntity;public DamageResponse Last;
 public bool IsDead()=>Health<=0;public int GetMaxHealth()=>Max;
 [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
 public virtual DamageResponse damageEntityLocal(DamageSource source,int strength,bool critical,float impulse){
  DamageResponse r=new DamageResponse{Source=source,Strength=strength,ModStrength=strength,Fatal=strength>=Health};
  FireAttackedEvents(r);ProcessDamageResponseLocal(r);return r;
 }
 public void FireAttackedEvents(DamageResponse r){}
 public virtual void ProcessDamageResponseLocal(DamageResponse r){Last=r;Health-=r.Strength;}
}
public class EntityPlayer:EntityAlive {}
public class Vehicle {public EntityVehicle Owner;public int GetHealth()=>Owner.Health;public int GetMaxHealth()=>Owner.Max;}
public class EntityVehicle:EntityAlive {
 public bool Apache=true,Exploded;public EntityPlayer Pilot,Gunner;public Vehicle vehicle;
 public EntityVehicle(){Health=Max=1000000;vehicle=new Vehicle{Owner=this};}
 public EntityAlive GetAttached(int seat)=>seat==0?Pilot:Gunner;
 [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
 public override DamageResponse damageEntityLocal(DamageSource source,int strength,bool critical,float impulse){
  DamageResponse r=new DamageResponse{Source=source,Strength=strength,ModStrength=strength};
  ProcessDamageResponseLocal(r);return r;
 }
 [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
 public override void ProcessDamageResponseLocal(DamageResponse r){
  Last=r;
  if(Pilot!=null)Pilot.damageEntityLocal(new DamageSource(),r.Strength,false,1);
  ApplyDamage(r.Strength);
 }
 [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
 public void ApplyDamage(int damage){if(damage>=99999){Exploded=true;Health=0;}else Health=System.Math.Max(1,Health-damage);}
}
public class Explosion {
 public static EntityAlive Target;public static int Damage;public static bool Throw;
 [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
 public void AttackEntites(int id,ItemValue item,EnumDamageTypes type){
  if(Throw)throw new System.InvalidOperationException("test explosion failure");
  Target.damageEntityLocal(new DamageSource{damageType=type},Damage,false,1);
 }
}
namespace AECT16RuntimeFix {public static class ApacheWeapons {public static bool IsApache(EntityVehicle v)=>v!=null&&v.Apache;}}
public static class ArmorTests {
 static int checks;static void Check(bool ok,string text){checks++;if(!ok)throw new System.Exception(text);}
 static DamageSource Source(EnumDamageTypes type=EnumDamageTypes.Bashing,EnumDamageSource origin=EnumDamageSource.External)=>new DamageSource{damageType=type,damageSource=origin};
 public static void Run(){
  AECT16RuntimeFix.ApacheArmor.Install();
  var v=new EntityVehicle();var r=v.damageEntityLocal(Source(),100000,false,1);
  Check(v.Health==975000&&!v.Exploded&&r.Strength==25000,"ordinary hull hit reduced once before application and serialization");
  var replica=new EntityVehicle();replica.ProcessDamageResponseLocal(r);
  Check(replica.Health==975000,"network replay does not reduce a second time");
  v=new EntityVehicle();r=v.damageEntityLocal(Source(),int.MaxValue,false,1);
  Check(v.Health==850000&&!v.Exploded&&r.Strength==150000,"15 percent cap survives native 99999 kill threshold");
  v=new EntityVehicle();v.ApplyDamage(200000);
  Check(v.Health==900000&&!v.Exploded,"direct accumulated collision damage halved without premature explosion");
  v=new EntityVehicle();Explosion.Target=v;Explosion.Damage=100000;new Explosion().AttackEntites(1,null,EnumDamageTypes.Heat);
  Check(v.Health==960000,"explosion hull reduction is 60 percent");
  v=new EntityVehicle();v.damageEntityLocal(Source(),1000,false,1);Check(v.Health==999750,"explosion context restored");
  Explosion.Throw=true;try{new Explosion().AttackEntites(1,null,EnumDamageTypes.Heat);}catch(System.InvalidOperationException){}Explosion.Throw=false;
  v=new EntityVehicle();v.damageEntityLocal(Source(),1000,false,1);Check(v.Health==999750,"exception cannot leak explosion classification");
  v=new EntityVehicle{Apache=false};v.damageEntityLocal(Source(),100000,false,1);Check(v.Exploded,"non Apache native behavior preserved");
  v=new EntityVehicle();v.damageEntityLocal(Source(EnumDamageTypes.Suicide),100000,false,1);Check(v.Exploded,"explicit suicide retains native destruction behavior");
  foreach(bool gunner in new[]{false,true}){
   v=new EntityVehicle();var p=new EntityPlayer{AttachedToEntity=v};if(gunner)v.Gunner=p;else v.Pilot=p;
   r=p.damageEntityLocal(Source(),1000,false,1);
   Check(p.Health==900&&!r.Fatal&&r.Strength==100,"both seats get 90 percent ordinary protection and corrected fatal flag");
   var other=new EntityPlayer();other.ProcessDamageResponseLocal(r);Check(other.Health==900,"player response packet replays without second mitigation");
   p.Health=1000;r=p.damageEntityLocal(Source(),int.MaxValue,false,1);Check(p.Health==800&&!r.Fatal,"huge player hit capped at 20 percent maximum health");
   p.Health=1000;Explosion.Target=p;Explosion.Damage=500;new Explosion().AttackEntites(1,null,EnumDamageTypes.Heat);Check(p.Health==900,"direct passenger explosion mitigation 80 percent");
   p.Health=1000;p.damageEntityLocal(Source(EnumDamageTypes.VehicleInside,EnumDamageSource.Internal),400,false,1);Check(p.Health==900,"native occupant collision reduced 75 percent");
   p.Health=1000;p.damageEntityLocal(Source(EnumDamageTypes.Starvation,EnumDamageSource.Internal),100,false,1);Check(p.Health==900,"internal survival damage not shielded");
   p.Health=1000;p.AttachedToEntity=null;p.damageEntityLocal(Source(),500,false,1);Check(p.Health==500,"dismount immediately ends protection");
   p.Health=1000;p.AttachedToEntity=v;v.Health=0;p.damageEntityLocal(Source(),500,false,1);Check(p.Health==500,"dead airframe cannot protect occupants");
  }
  v=new EntityVehicle();var occupant=new EntityPlayer{AttachedToEntity=v};v.Pilot=occupant;
  v.damageEntityLocal(Source(),1000000,false,1);Check(occupant.Health==800&&v.Health==850000,"native hull transfer cannot instantly kill full health occupant");
  v.Pilot=null;occupant.Health=1000;occupant.damageEntityLocal(Source(),500,false,1);Check(occupant.Health==500,"stale attachment without actual seat gives no protection");
  Check(AECT16RuntimeFix.ApacheArmor.Reduce(0,1000,.1,.2)==0&&AECT16RuntimeFix.ApacheArmor.Reduce(-100,1000,.1,.2)==-100,"zero and healing values preserved");
  System.Console.WriteLine("PASS: "+checks+" real Harmony patched fixture checks (not an in-game multiplayer test).");
 }
}
'@
$source=Get-Content "$root/99-AEC_T16_RuntimeFix/Source/ApacheArmor.cs" -Raw
$testDir=Join-Path $root '.local-tests/apache-research'
$candidate=Join-Path $testDir 'AEC.T16.RuntimeFix.apache-armor.dll'
. "$PSScriptRoot/Build-RuntimeFixRoslyn.ps1" -OutputPath $candidate
$fixturePath=Join-Path $testDir 'ApacheArmorFixture.cs'
$testDll=Join-Path $testDir 'ApacheArmorFixture.dll'
Set-Content -LiteralPath $fixturePath -Value ($source+"`n"+$fixture) -Encoding utf8
$managed=Join-Path (Split-Path $root) '7DaysToDie_Data/Managed'
$refs=@('mscorlib.dll','System.dll','System.Core.dll')|ForEach-Object {Join-Path $managed $_}
[RuntimeFixCompiler]::Build(@($fixturePath),($refs+@($harmony)),$testDll)
# Game Harmony does not support the compiler host's CoreCLR 10. Run the
# Mono/.NET Framework-targeted fixture under Windows PowerShell instead.
$runner=Join-Path $testDir 'Run-ApacheArmorFixture.ps1'
Set-Content -LiteralPath $runner -Encoding utf8 -Value @'
param($HarmonyPath,$FixturePath)
$ErrorActionPreference='Stop'
[void][Reflection.Assembly]::LoadFrom($HarmonyPath)
[void][Reflection.Assembly]::LoadFrom($FixturePath)
[ArmorTests]::Run()
'@
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $runner $harmony $testDll
if($LASTEXITCODE -ne 0){throw 'Apache armor patched fixture failed'}

Add-Type -Path "$root/0_TFP_Harmony/Mono.Cecil.dll"
$game=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path (Split-Path $root) '7DaysToDie_Data/Managed/Assembly-CSharp.dll'))
try {
 foreach($name in @('EntityAlive','EntityVehicle')){
  $type=$game.MainModule.Types|Where-Object Name -eq $name
  $method=$type.Methods|Where-Object Name -eq damageEntityLocal
  $target=if($name -eq 'EntityAlive'){'FireAttackedEvents'}else{'ProcessDamageResponseLocal'}
  $ops=@($method.Body.Instructions);$sites=0
  for($i=2;$i -lt $ops.Count;$i++){if($ops[$i].Operand -is [Mono.Cecil.MethodReference] -and $ops[$i].Operand.Name -eq $target){
   if($ops[$i-2].OpCode.Name -ne 'ldarg.0' -or $ops[$i-1].OpCode.Name -ne 'ldloc.0' -or $method.Body.Variables[0].VariableType.Name -ne 'DamageResponse'){throw 'Native response IL layout changed'}
   $sites++
  }}
  if($sites -ne 1){throw 'Native response site count changed'}
 }
 $vehicle=$game.MainModule.Types|Where-Object Name -eq EntityVehicle
 $apply=$vehicle.Methods|Where-Object Name -eq ApplyDamage
 if(@($apply.Body.Instructions|Where-Object {$_.OpCode.Name -eq 'ldc.i4' -and $_.Operand -eq 99999}).Count -ne 1){throw 'Native vehicle kill threshold changed'}
 Write-Output 'PASS: installed V3.2 native response application sites and instant-explosion threshold verified.'
} finally {$game.Dispose()}

