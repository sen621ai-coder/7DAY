$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
$source=Get-Content "$root/ZZZ-PZAEC_SealedCabins/Source/CabinProtection.cs" -Raw
$fixture=@'
public class Entity {public Entity AttachedToEntity;}
public class EntityAlive:Entity {public bool Dead;public bool IsDead()=>Dead;}
public class EntityPlayer:EntityAlive {public bool Remote;}
public class EntityVehicle:EntityAlive {public Vehicle vehicle=new Vehicle();public bool Engine;public float Fuel;}
public class Vehicle {public string Name;public float Health=100;public string GetName()=>Name;public float GetHealth()=>Health;}
public class EntityBuffs {public EntityAlive parent;public enum BuffStatus {Added,FailedInvalidName,FailedImmune}}
public class MinEventParams {public EntityAlive Self;}
public class RequirementBase {public bool invert;public virtual bool IsValid(MinEventParams p)=>true;}
public struct Vector3i {}
public class Mod {}
public interface IModApi {void InitMod(Mod mod);}
public static class Log {public static void Out(string s){}}
namespace HarmonyLib {
 public static class AccessTools {public static System.Reflection.MethodInfo Method(System.Type t,string n,System.Type[] p)=>null;}
 public class HarmonyMethod {public HarmonyMethod(System.Type t,string n){}}
 public class Harmony {public Harmony(string name){} public void Patch(System.Reflection.MethodInfo m,HarmonyMethod prefix){}}
}
public static class CabinTests {
 static int count;static void Check(bool ok,string label){count++;if(!ok)throw new System.Exception(label);}
 public static void Run(){
  string[] names={"vehicleMD500","vehicleApacheHelicopter","vehicleM1Abrams","vehicleM1AbramsT17","vehicleM1AbramsT18","vehicleM1AbramsT19"};
  var requirement=new PZAEC.SealedCabins.CabinProtected();
  foreach(string name in names){
   var v=new EntityVehicle{vehicle=new Vehicle{Name=name},Engine=false,Fuel=0};
   for(int seat=0;seat<4;seat++){
    var player=new EntityPlayer{AttachedToEntity=v,Remote=seat>0};var p=new MinEventParams{Self=player};
    Check(PZAEC.SealedCabins.Protection.Inside(player),name+" passenger "+seat+" protected without fuel or engine");
    Check(requirement.IsValid(p),"XML live requirement");
    requirement.invert=true;Check(!requirement.IsValid(p),"negative requirement blocks exposure");requirement.invert=false;
    var buffs=new EntityBuffs{parent=player};
    foreach(string b in PZAEC.SealedCabins.Protection.ExposureBuffs){
     var result=EntityBuffs.BuffStatus.Added;
     Check(!PZAEC.SealedCabins.Protection.BeforeAddBuff(buffs,b,ref result)&&result==EntityBuffs.BuffStatus.FailedImmune,"native exposure admission blocked "+b);
    }
    foreach(string b in new[]{"buffInjuryBleeding","buffInfectionCatch","BossHitRad","buffstimIrradiatedShell","buffIrradiationIncreasing3","buffFrostbiteDesk3","buffThermoplegiaDesk3"}){
     var result=EntityBuffs.BuffStatus.Added;Check(PZAEC.SealedCabins.Protection.BeforeAddBuff(buffs,b,ref result),"existing illness/direct hit/recovery preserved "+b);
    }
    player.AttachedToEntity=null;Check(!requirement.IsValid(p),"exit immediately removes protection");
    var allowed=EntityBuffs.BuffStatus.Added;Check(PZAEC.SealedCabins.Protection.BeforeAddBuff(buffs,"buffElementCold",ref allowed),"exposure resumes on foot");
    player.AttachedToEntity=v;player.Dead=true;Check(!requirement.IsValid(p),"dead passenger");player.Dead=false;
    v.vehicle.Health=0;Check(!requirement.IsValid(p),"destroyed hull");v.vehicle.Health=100;
    v.Dead=true;Check(!requirement.IsValid(p),"dead vehicle flag");v.Dead=false;
    player.AttachedToEntity=new EntityVehicle{vehicle=new Vehicle{Name="vehicleTruck4x4"}};Check(!requirement.IsValid(p),"switch to unprotected vehicle");
   }
  }
  foreach(string name in new[]{"vehicleM1AbramsT20","vehicleMD500Placeable","vehicleApacheHelicopterPlaceable","vehicleBicycle",null})Check(!PZAEC.SealedCabins.Protection.VehicleName(name),"exact vehicle allowlist");
  Check(!PZAEC.SealedCabins.Protection.Inside(new EntityAlive{AttachedToEntity=new EntityVehicle{vehicle=new Vehicle{Name=names[0]}}}),"nonplayer excluded");
  Check(!requirement.IsValid(null),"missing context");
  System.Console.WriteLine("PASS "+count+" actual cabin predicate/admission tests with mocked game entities");
 }
}
'@
Add-Type -TypeDefinition ($source+$fixture)
[CabinTests]::Run()

# Verify the native parser supports an assembly-qualified custom requirement,
# and both public AddBuff entries pass through the exact patched overload.
Add-Type -Path "$root/0_TFP_Harmony/Mono.Cecil.dll"
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path (Split-Path $root) '7DaysToDie_Data/Managed/Assembly-CSharp.dll'))
$type=$a.MainModule.Types|Where-Object Name -eq EntityBuffs
$methods=@($type.Methods|Where-Object Name -eq AddBuff)
if($methods.Count -ne 2){throw 'Native AddBuff overload set changed'}
$core=@($methods|Where-Object {$_.Parameters.Count -eq 6 -and $_.Parameters[1].ParameterType.FullName -eq 'Vector3i'})
if($core.Count -ne 1){throw 'Core AddBuff overload changed'}
$wrapper=$methods|Where-Object {$_.Parameters.Count -eq 5}
if(!($wrapper.Body.Instructions|Where-Object {$_.Operand -as [string] -eq $core[0].FullName})){throw 'Simple AddBuff no longer routes to core'}
$parser=($a.MainModule.Types|Where-Object Name -eq RequirementBase).Methods|Where-Object Name -eq ParseRequirement
if(!($parser.Body.Instructions|Where-Object {($_.Operand -as [string]) -match 'System.Type::GetType\(System.String\)'})){throw 'Requirement type resolution changed'}
$a.Dispose()
Write-Output 'PASS native buff admission and assembly-qualified requirement parser checks'
