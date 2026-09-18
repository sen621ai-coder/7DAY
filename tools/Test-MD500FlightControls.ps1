$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$source = Join-Path $root '99-AEC_T16_RuntimeFix/Source'
$fixture = @'
namespace UnityEngine {
 public enum ForceMode { Acceleration }
 public struct Vector3 {
  public float x,y,z; public Vector3(float a,float b,float c){x=a;y=b;z=c;}
  public static Vector3 up=>new Vector3(0,1,0); public static Vector3 forward=>new Vector3(0,0,1);
  public float magnitude=>(float)Math.Sqrt(x*x+y*y+z*z);
  public Vector3 normalized=>magnitude>0?this*(1/magnitude):new Vector3();
  public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
  public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
  public static Vector3 operator *(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);
  public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
  public static Vector3 Cross(Vector3 a,Vector3 b)=>new Vector3(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
  public static Vector3 ProjectOnPlane(Vector3 a,Vector3 n)=>a-n*Dot(a,n);
  public static Vector3 ClampMagnitude(Vector3 a,float m)=>a.magnitude>m?a.normalized*m:a;
 }
 public struct Quaternion { public static Vector3 operator *(Quaternion q,Vector3 v)=>v; }
 public class Rigidbody {
  public Quaternion rotation; public Vector3 velocity,angularVelocity,force,torque; public bool isKinematic;
  public void AddForce(Vector3 f,ForceMode m){force=force+f;}
  public void AddTorque(Vector3 t,ForceMode m){torque=torque+t;}
 }
 public static class Mathf { public static float Clamp01(float v)=>Math.Max(0,Math.Min(1,v)); }
}
namespace HarmonyLib {
 public class HarmonyMethod {public HarmonyMethod(Type t,string n){} }
 public class Harmony {
  public bool Fail;
  public void Patch(object original,HarmonyMethod prefix=null,HarmonyMethod transpiler=null){if(Fail)throw new Exception("fixture failure");}
 }
 public static class AccessTools {
  public static System.Reflection.FieldInfo Field(Type t,string n)=>t.GetField(n);
  public static System.Reflection.MethodInfo PropertyGetter(Type t,string n)=>t.GetProperty(n).GetMethod;
  public static System.Reflection.MethodInfo Method(Type t,string n,Type[] p=null)=>p==null?t.GetMethod(n):t.GetMethod(n,p);
 }
 public class CodeInstruction {
  public OpCode opcode; public object operand;
  public CodeInstruction(OpCode o,object p=null){opcode=o;operand=p;}
  public bool LoadsField(System.Reflection.FieldInfo f)=>opcode==OpCodes.Ldfld&&object.Equals(f,operand);
  public bool Calls(System.Reflection.MethodInfo m)=>(opcode==OpCodes.Call||opcode==OpCodes.Callvirt)&&object.Equals(m,operand);
 }
}
public static class Log { public static void Out(string s){} public static void Error(string s){} }
public class MovementInput {public bool jump,down;public float moveForward,moveStrafe;}
public class Vehicle {
 public string Name="vehicleMD500"; public EntityVehicle entity; public bool CurrentIsAccel,IsTurbo;
 public float VelocityMaxForward=25,VelocityMaxBackward=10,VelocityMaxTurboForward=29,VelocityMaxTurboBackward=10;
 public float EffectVelocityMaxPer=1,EffectMotorTorquePer=1,Fuel=400; public int Health=5000;
 public string GetName()=>Name == null ? null : Name.ToLowerInvariant();public int GetHealth()=>Health;public float GetFuelLevel()=>Fuel;public void UpdateSimulation(){}
}
public class VPEngine {public Vehicle vehicle;public void Update(float dt){} }
public class EntityVehicle {
 public static float VehicleFuelUsageModifier=1;
 public bool hasDriver=true,IsEngineRunning=true,isEntityRemote,RBActive=true;
 public Vehicle vehicle;public MovementInput movementInput=new MovementInput();
 public UnityEngine.Rigidbody vehicleRB=new UnityEngine.Rigidbody();
 public UnityEngine.Vector3 position=new UnityEngine.Vector3(0,100,0);
 public float timeInWater;public int Wheels=0;
 public class Motor {public float rpm=8,rpmMax=8;}
 public Motor[] motors=new[]{new Motor()};
 public int GetWheelsOnGround()=>Wheels;public void FixedUpdateForces(){}public void PhysicsFixedUpdate(){}
 public EntityVehicle(){vehicle=new Vehicle();vehicle.entity=this;}
}
public static class MD500Tests {
 static int checks;
 static void Check(bool b,string why){checks++;if(!b)throw new Exception(why);}
 static void Near(float a,float b,float e,string why){Check(Math.Abs(a-b)<e,why+" got="+a+" expected="+b);}
 static AECT16RuntimeFix.MD500FlightMath.Output Law(float f=0,float t=0,bool up=false,bool down=false,float vf=0,float vr=0,float vy=0,float yaw=0,float alt=100,float power=1,float maxF=25){
  return AECT16RuntimeFix.MD500FlightMath.Calculate(f,t,up,down,vf,vr,vy,yaw,alt,power,maxF,10,1);
 }
 static void Simulate(float dt,bool up,bool down,float initial,float expected){
  float v=initial,y=100;
  for(int i=0;i<(int)(12/dt);i++){var o=Law(up:up,down:down,vy:v,alt:y);v+=(o.Up-9.81f)*dt;y+=v*dt;}
  Near(v,expected,.02f,"vertical target dt="+dt);
 }
 public static void Run(){
  var h=Law();Near(h.Up,9.81f,.0001f,"hover gravity compensation");Near(h.Forward,0,.0001f,"no unintended forward thrust");
  Check(Law(up:true).Up>9.81f,"Space rises");Check(Law(down:true).Up<9.81f,"C descends");
  Near(Law(up:true,down:true).Up,9.81f,.0001f,"opposing inputs cancel");
  Near(Law(f:1).Up,9.81f,.0001f,"W does not change lift");Check(Law(f:1).Forward>0,"W forward");
  Check(Law(f:-1,vf:20).Forward<0,"S brakes");Check(Law(f:-1,vf:0).Forward<0,"S reverses");
  Check(Law(vf:20).Forward<0,"release brakes forward drift");Check(Law(vr:3).Right<0,"lateral drift damped");
  Check(Law(t:1).Yaw>0&&Law(t:-1).Yaw<0,"A/D yaw");Check(Law(yaw:.3f).Yaw<0,"yaw release damping");
  Near(Law(power:0).Up,0,.0001f,"no unpowered levitation");
  Check(Law(up:true,alt:280).Up<=9.81f,"ceiling climb stopped");Check(Law(up:true,alt:285).Up<9.81f,"above ceiling descends");
  foreach(float dt in new[]{.01f,.02f,.033333f}){Simulate(dt,true,false,0,4);Simulate(dt,false,true,0,-3);Simulate(dt,false,false,-5,0);}
  Near(AECT16RuntimeFix.MD500FlightMath.FuelSpeed(0,25),2.5f,.0001f,"hover fuel floor");
  Near(AECT16RuntimeFix.MD500FlightMath.FuelSpeed(20,25),20,.0001f,"cruise fuel preserved");
  var harmony=new HarmonyLib.Harmony();AECT16RuntimeFix.MD500FlightControls.Install(harmony);
  foreach(string name in new[]{"vehicleMD500","vehiclemd500","VEHICLEMD500","vehicleApacheHelicopter","vehicleapachehelicopter","VEHICLEAPACHEHELICOPTER"}){
   var actual=new EntityVehicle();actual.vehicle.Name=name;actual.movementInput.jump=true;
   Check(!AECT16RuntimeFix.MD500FlightControls.BeforeForces(actual),"normalized vehicle suppresses original pitch: "+name);
   Check(actual.vehicleRB.force.y>9.81f,"normalized vehicle Space supplies upward force: "+name);
   Near(actual.vehicleRB.torque.x,0,.0001f,"Space does not pitch level helicopter: "+name);
   Check(AECT16RuntimeFix.ApacheWeapons.IsApache(actual)==name.ToLowerInvariant().Contains("apache"),"Apache weapon recognition: "+name);
  }
  Check(!AECT16RuntimeFix.ApacheWeapons.IsApache(null),"null is not Apache");
  var e=new EntityVehicle();Check(!AECT16RuntimeFix.MD500FlightControls.BeforeForces(e),"native MD forces replaced");
  Near(e.vehicleRB.force.y,9.81f,.0001f,"runtime hover");
  foreach(string reason in new[]{"remote","no driver","no fuel","broken","engine off","water","sleep","kinematic","no input","no rotor"}){
   e=new EntityVehicle();switch(reason){case "remote":e.isEntityRemote=true;break;case "no driver":e.hasDriver=false;break;case "no fuel":e.vehicle.Fuel=0;break;case "broken":e.vehicle.Health=0;break;case "engine off":e.IsEngineRunning=false;break;case "water":e.timeInWater=1;break;case "sleep":e.RBActive=false;break;case "kinematic":e.vehicleRB.isKinematic=true;break;case "no input":e.movementInput=null;break;case "no rotor":e.motors=null;break;}
   Check(!AECT16RuntimeFix.MD500FlightControls.BeforeForces(e),reason+" disables native lift too");Near(e.vehicleRB.force.magnitude,0,.0001f,reason+" no custom force");
  }
  foreach(string name in new[]{"vehicleGyrocopter","AECArmoredGyroVehicle","vehicleUH60","vehicleMD500Other","vehicleApacheHelicopterOther"}){
   e=new EntityVehicle();e.vehicle.Name=name;Check(AECT16RuntimeFix.MD500FlightControls.BeforeForces(e),"unrelated vehicle unchanged "+name);
   Check(!AECT16RuntimeFix.ApacheWeapons.IsApache(e),"unrelated vehicle has no Apache weapons "+name);
   Check(AECT16RuntimeFix.MD500FlightControls.GroundAction(true,e),"unrelated Space/C unchanged");
   Near(AECT16RuntimeFix.MD500FlightControls.EffectiveFuelSpeed(0,new VPEngine{vehicle=e.vehicle}),0,.0001f,"unrelated fuel unchanged");
  }
  e=new EntityVehicle();Check(!AECT16RuntimeFix.MD500FlightControls.GroundAction(true,e),"MD ground jump/brake suppressed");
  e.movementInput.jump=true;AECT16RuntimeFix.MD500FlightControls.BeforeForces(e);Check(e.movementInput.jump,"shared input never mutated");
  AECT16RuntimeFix.MD500FlightControls.BeforeEngineSimulation(e.vehicle);Check(e.vehicle.CurrentIsAccel,"hover sound flight RPM");
  e=new EntityVehicle();e.Wheels=3;AECT16RuntimeFix.MD500FlightControls.BeforeEngineSimulation(e.vehicle);Check(!e.vehicle.CurrentIsAccel,"landed idle retained");
  e=new EntityVehicle();e.isEntityRemote=true;AECT16RuntimeFix.MD500FlightControls.BeforeEngineSimulation(e.vehicle);Check(!e.vehicle.CurrentIsAccel,"remote sync flag not overwritten");
  e=new EntityVehicle();e.vehicle.Name="vehicleApacheHelicopter";
  Check(AECT16RuntimeFix.MD500FlightControls.Applies(e.vehicle),"Apache shares controller");
  Check(!AECT16RuntimeFix.MD500FlightControls.BeforeForces(e),"Apache replaces native lift");
  Near(e.vehicleRB.force.y,9.81f,.0001f,"Apache hover gravity");
  Check(!AECT16RuntimeFix.MD500FlightControls.GroundAction(true,e),"Apache ground brake suppressed");
  Near(AECT16RuntimeFix.MD500FlightControls.EffectiveFuelSpeed(0,new VPEngine{vehicle=e.vehicle}),2.5f,.0001f,"Apache hover consumes fuel");
  e.movementInput.jump=true;e.movementInput.moveForward=1;e.vehicleRB.force=new UnityEngine.Vector3();
  AECT16RuntimeFix.MD500FlightControls.BeforeForces(e);
  Check(e.vehicleRB.force.y>9.81f&&e.vehicleRB.force.z>0,"Apache simultaneous rise and forward");
  Check(e.movementInput.jump,"Apache shared input retained");
  foreach(string reason in new[]{"remote","no fuel","no driver","engine off","broken","water"}){
   e=new EntityVehicle();e.vehicle.Name="vehicleApacheHelicopter";
   switch(reason){case "remote":e.isEntityRemote=true;break;case "no fuel":e.vehicle.Fuel=0;break;case "no driver":e.hasDriver=false;break;case "engine off":e.IsEngineRunning=false;break;case "broken":e.vehicle.Health=0;break;case "water":e.timeInWater=1;break;}
   Check(!AECT16RuntimeFix.MD500FlightControls.BeforeForces(e),"Apache native forces blocked: "+reason);
   Near(e.vehicleRB.force.magnitude,0,.0001f,"Apache no active force: "+reason);
  }
  var inputCode=new List<HarmonyLib.CodeInstruction>();for(int i=0;i<3;i++)inputCode.Add(new HarmonyLib.CodeInstruction(OpCodes.Ldfld,typeof(MovementInput).GetField(i==0?"jump":"down")));
  int n=0;foreach(var op in AECT16RuntimeFix.MD500FlightControls.GroundActionsTranspiler(inputCode))n++;Check(n==9,"three ground loads wrapped");
  bool rejected=false;try{AECT16RuntimeFix.MD500FlightControls.GroundActionsTranspiler(new List<HarmonyLib.CodeInstruction>());}catch(InvalidOperationException){rejected=true;}Check(rejected,"unsupported ground IL rejected");
  var fuelCode=new[]{new HarmonyLib.CodeInstruction(OpCodes.Call,typeof(UnityEngine.Vector3).GetProperty("magnitude").GetMethod)};
  n=0;foreach(var op in AECT16RuntimeFix.MD500FlightControls.FuelTranspiler(fuelCode))n++;Check(n==3,"fuel magnitude wrapped once");
  AECT16RuntimeFix.MD500FlightControls.Install(new HarmonyLib.Harmony{Fail=true});
  e=new EntityVehicle();Check(AECT16RuntimeFix.MD500FlightControls.BeforeForces(e),"failed install preserves original controls");
  Check(AECT16RuntimeFix.MD500FlightControls.GroundAction(true,e),"failed install ground unchanged");
  e.vehicle.Name="vehicleApacheHelicopter";Check(AECT16RuntimeFix.MD500FlightControls.BeforeForces(e),"failed install Apache original controls retained");
  Console.WriteLine("PASS: "+checks+" MD-500 flight-control assertions (math simulation and runtime fixtures; not a Unity playtest).");
 }
}
'@
# Compile the actual adapter and control law, not a rewritten implementation.
$runtime = Get-Content (Join-Path $source 'MD500FlightControls.cs') -Raw
$math = Get-Content (Join-Path $source 'MD500FlightMath.cs') -Raw
# Separate source units permit their normal using directives.
$weapons = Get-Content (Join-Path $source 'ApacheWeapons.cs') -Raw
$identity = [regex]::Match($weapons, '(?s)public static bool IsApache\(EntityVehicle v\).*?(?=public static void Install)').Value
if (!$identity) { throw 'Cannot extract actual Apache vehicle identity method' }
Add-Type -TypeDefinition ($runtime + "`n" + ($math -replace '^using System;','') + "`nnamespace AECT16RuntimeFix { public static class ApacheWeapons { $identity } }`n" + $fixture)
[MD500Tests]::Run()

# Check the installed game's IL, independent of the fixture.
Add-Type -Path (Join-Path $root '0_TFP_Harmony/Mono.Cecil.dll')
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path (Split-Path $root) '7DaysToDie_Data/Managed/Assembly-CSharp.dll'))
try {
    $vehicleType = $game.MainModule.Types | Where-Object Name -eq 'Vehicle'
    $constructor = $vehicleType.Methods | Where-Object { $_.Name -eq '.ctor' -and $_.Parameters.Count -eq 2 }
    if (!($constructor.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString() -eq 'System.String System.String::ToLower()' })) { throw 'Recheck native Vehicle name normalization and fixture' }
    $entity = $game.MainModule.Types | Where-Object Name -eq 'EntityVehicle'
    $physics = $entity.Methods | Where-Object Name -eq 'PhysicsFixedUpdate'
    $loads = @($physics.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldfld' -and $_.Operand.DeclaringType.FullName -eq 'MovementInput' -and $_.Operand.Name -in @('jump','down') })
    if ($loads.Count -ne 3) { throw "Unsupported native ground-input sites: $($loads.Count)" }
    $engine = ($game.MainModule.Types | Where-Object Name -eq 'VPEngine').Methods | Where-Object Name -eq 'Update'
    $magnitudes = @($engine.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString() -eq 'System.Single UnityEngine.Vector3::get_magnitude()' })
    if ($magnitudes.Count -ne 1) { throw 'Unsupported engine fuel IL' }
    $ops = $physics.Body.Instructions
    $simulation = $ops | Where-Object { $_.Operand -and $_.Operand.ToString() -eq 'System.Void Vehicle::UpdateSimulation()' }
    $sends = @($ops | Where-Object { $_.Operand -and $_.Operand.ToString() -eq 'System.Void EntityVehicle::SendSyncData(System.UInt16)' })
    if (!$simulation -or !$sends.Count -or $simulation.Offset -ge $sends[0].Offset) { throw 'Engine state would be updated after sync' }
    Write-Output 'PASS: V3.2 native IL has three ground-input loads, one engine speed/fuel site, and simulation precedes network sends.'
} finally { $game.Dispose() }
