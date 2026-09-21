$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$source = Join-Path $root '99-AEC_T16_RuntimeFix/Source'
$fixture = @'
namespace UnityEngine {
 public static class Time {public static float fixedTime;public static float fixedDeltaTime=.02f;}
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
 public struct Quaternion {
  public float Pitch,Roll,Yaw;
  public static Vector3 operator *(Quaternion q,Vector3 v){
   double x=q.Pitch*Math.PI/180,z=q.Roll*Math.PI/180;
   var p=new Vector3(v.x,(float)(v.y*Math.Cos(x)-v.z*Math.Sin(x)),(float)(v.y*Math.Sin(x)+v.z*Math.Cos(x)));
   p=new Vector3((float)(p.x*Math.Cos(z)-p.y*Math.Sin(z)),(float)(p.x*Math.Sin(z)+p.y*Math.Cos(z)),p.z);
   double y=q.Yaw*Math.PI/180;
   return new Vector3((float)(p.x*Math.Cos(y)+p.z*Math.Sin(y)),p.y,(float)(-p.x*Math.Sin(y)+p.z*Math.Cos(y)));
  }
 }
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
 public float TiltUpForce=>1f;
 public float VelocityMaxForward=25,VelocityMaxBackward=10,VelocityMaxTurboForward=29,VelocityMaxTurboBackward=10;
 public float EffectVelocityMaxPer=1,EffectMotorTorquePer=1,Fuel=400; public int Health=5000;
 public float AirDragVelScale=.997f,AirDragAngVelScale=.97f;
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
 static AECT16RuntimeFix.MD500FlightMath.Output Law(float f=0,float t=0,bool up=false,bool down=false,float vf=0,float vr=0,float vy=0,float yaw=0,float alt=100,float power=1,float maxF=25,bool hold=false,float holdAlt=100){
  return AECT16RuntimeFix.MD500FlightMath.Calculate(f,t,up,down,vf,vr,vy,yaw,alt,holdAlt,hold,power,maxF,10,1);
 }
 static void Simulate(float dt,bool up,bool down,float initial,float expected){
  float v=initial,y=100;
  for(int i=0;i<(int)(12/dt);i++){var o=Law(up:up,down:down,vy:v,alt:y);v+=(o.Up-9.81f)*dt;y+=v*dt;}
  Near(v,expected,.02f,"vertical target dt="+dt);
 }
 public static void Run(){
  foreach(string name in new[]{"vehicleMD500","vehicleApacheHelicopter"})
   foreach(float dt in new[]{.01f,.02f,.04f})foreach(int sign in new[]{-1,1})RuntimeTurnResponse(name,dt,sign);
  UnityEngine.Time.fixedDeltaTime=.02f;
  foreach(string name in new[]{"vehicleMD500","vehicleApacheHelicopter"})
   foreach(float power in new[]{.75f,.85f,1f})foreach(float dt in new[]{.01f,.02f,.04f})RuntimeLevelHeight(name,power,dt);
  UnityEngine.Time.fixedDeltaTime=.02f;
  foreach(float dt in new[]{.01f,.02f,.04f}){
   float velocity=0,thrust=0;
   for(int i=0;i<(int)(20/dt);i++){
    float next=AECT16RuntimeFix.MD500FlightMath.SmoothThrust(thrust,Law(f:1,vf:velocity).Forward,dt,1,1);
    if(Math.Abs(next-thrust)>4*dt+.00001f)throw new Exception("thrust slew exceeded");
    thrust=next;velocity+=thrust*dt;
   }
   Near(velocity,25,.08f,"smooth start reaches cruise dt="+dt);
   float coastV=20,brakeV=20,coastA=0,brakeA=0;
   for(int i=0;i<(int)(3/dt);i++){
    coastA=AECT16RuntimeFix.MD500FlightMath.SmoothThrust(coastA,Law(vf:coastV).Forward,dt,1,1);
    brakeA=AECT16RuntimeFix.MD500FlightMath.SmoothThrust(brakeA,Law(f:-1,vf:brakeV).Forward,dt,1,1);
    coastV+=coastA*dt;brakeV+=brakeA*dt;
   }
   Check(coastV>brakeV+4,"release coasts farther than active braking dt="+dt);
   for(int i=0;i<(int)(40/dt);i++){
    thrust=AECT16RuntimeFix.MD500FlightMath.SmoothThrust(thrust,Law(vf:velocity).Forward,dt,1,1);
    velocity+=thrust*dt;
   }
   Near(velocity,0,.02f,"release settles without persistent drift dt="+dt);
   for(int i=0;i<(int)(15/dt);i++){
    thrust=AECT16RuntimeFix.MD500FlightMath.SmoothThrust(thrust,Law(f:-1,vf:velocity).Forward,dt,1,1);
    velocity+=thrust*dt;
   }
   Near(velocity,-10,.08f,"reverse settles at speed cap dt="+dt);
  }
  Near(AECT16RuntimeFix.MD500FlightMath.SmoothThrust(4,-4,.02f,1,1),3.92f,.0001f,"direction reversal does not flip thrust instantly");
  Near(AECT16RuntimeFix.MD500FlightMath.SmoothThrust(4,0,.02f,0,1),0,.0001f,"rotor power loss cuts residual thrust");
  var h=Law();Near(h.Up,9.81f,.0001f,"hover gravity compensation");Near(h.Forward,0,.0001f,"no unintended forward thrust");
  Check(Law(up:true).Up>9.81f,"Space rises");Check(Law(down:true).Up<9.81f,"C descends");
  Near(Law(up:true,down:true).Up,9.81f,.0001f,"opposing inputs cancel");
  Near(Law(f:1).Up,9.81f,.0001f,"W does not change lift");Check(Law(f:1).Forward>0,"W forward");
  Check(Law(f:-1,vf:20).Forward<0,"S brakes");Check(Law(f:-1,vf:0).Forward<0,"S reverses");
  Check(Law(vf:20).Forward<0,"release brakes forward drift");Check(Law(vr:3).Right<0,"lateral drift damped");
  Check(Law(t:1).Yaw>0&&Law(t:-1).Yaw<0,"A/D yaw");Check(Law(yaw:.3f).Yaw<0,"yaw release damping");
  Near(Law(power:0).Up,0,.0001f,"no unpowered levitation");
  Near(Law(power:.75f).Up,9.81f,.0001f,"flight rotor RPM supplies full hover lift");
  Check(Law(power:.4f).Up<9.81f,"low startup rotor RPM cannot hover");
  Check(Law(alt:98,hold:true,holdAlt:100).Up>9.81f,"altitude hold recovers level-flight height loss");
  Check(Law(down:true,alt:98,hold:true,holdAlt:100).Up<9.81f,"descent input overrides altitude hold");
  Check(Law(up:true,alt:280).Up<=9.81f,"ceiling climb stopped");Check(Law(up:true,alt:285).Up<9.81f,"above ceiling descends");
  foreach(float dt in new[]{.01f,.02f,.033333f}){Simulate(dt,true,false,0,4);Simulate(dt,false,true,0,-3);Simulate(dt,false,false,-5,0);}
  Near(AECT16RuntimeFix.MD500FlightMath.FuelSpeed(0,25),2.5f,.0001f,"hover fuel floor");
  Near(AECT16RuntimeFix.MD500FlightMath.FuelSpeed(20,25),20,.0001f,"cruise fuel preserved");
  var harmony=new HarmonyLib.Harmony();AECT16RuntimeFix.MD500FlightControls.Install(harmony);
  var ramp=new EntityVehicle();ramp.movementInput.moveForward=1;
  for(int i=0;i<50;i++){UnityEngine.Time.fixedTime+=.02f;ramp.vehicleRB.force=new UnityEngine.Vector3();AECT16RuntimeFix.MD500FlightControls.BeforeForces(ramp);}
  Near(ramp.vehicleRB.force.z,4,.001f,"runtime builds thrust over one second");
  ramp.vehicle.Fuel=0;AECT16RuntimeFix.MD500FlightControls.BeforeForces(ramp);ramp.vehicle.Fuel=1;ramp.vehicleRB.force=new UnityEngine.Vector3();
  AECT16RuntimeFix.MD500FlightControls.BeforeForces(ramp);Near(ramp.vehicleRB.force.z,.08f,.001f,"restored power starts fresh");
  UnityEngine.Time.fixedTime+=1;ramp.vehicleRB.force=new UnityEngine.Vector3();AECT16RuntimeFix.MD500FlightControls.BeforeForces(ramp);
  Near(ramp.vehicleRB.force.z,.08f,.001f,"authority or simulation gap resets stored thrust");
  var cruising=AECT16RuntimeFix.MD500FlightMath.TargetAttitude(20,0,0,true,false);
  Check(cruising.Pitch>0,"cruise retains nose-down attitude");
  Check(AECT16RuntimeFix.MD500FlightMath.TargetAttitude(20,0,-4,true,false).Pitch<0,"braking raises nose");
  Check(AECT16RuntimeFix.MD500FlightMath.TargetAttitude(-8,0,-4,true,false).Pitch<0,"reverse raises nose");
  foreach(float speed in new[]{-25f,0f,25f}){
   var a=AECT16RuntimeFix.MD500FlightMath.TargetAttitude(speed,.65f,4,true,true);
   var b=AECT16RuntimeFix.MD500FlightMath.TargetAttitude(speed,-.65f,4,true,true);
   Near(a.Bank,-b.Bank,.0001f,"symmetric banking");
   Check(Math.Abs(a.Bank)<=AECT16RuntimeFix.MD500FlightMath.Handling(false).BankMax&&a.Pitch<=14&&a.Pitch>=-10,"attitude bounds");
   if(speed==0)Near(a.Bank,0,.0001f,"hover yaw stays level");
  }
  var grounded=AECT16RuntimeFix.MD500FlightMath.TargetAttitude(25,1,4,false,true);
  Near(grounded.Pitch,0,.0001f,"ground has no commanded pitch");Near(grounded.Bank,0,.0001f,"ground has no commanded bank");
  foreach(string name in new[]{"vehicleMD500","vehicleApacheHelicopter"}){
   foreach(float dt in new[]{.01f,.02f,.04f}){
    RuntimeClimbTurn(name,dt,1);RuntimeClimbTurn(name,dt,-1);
   }
   UnityEngine.Time.fixedDeltaTime=.02f;
   var flying=new EntityVehicle();flying.vehicle.Name=name;flying.movementInput.moveForward=1;
   AECT16RuntimeFix.MD500FlightControls.BeforeForces(flying);
   Check(flying.vehicleRB.torque.x>0,"forward command tilts real body "+name);
   Near(AECT16RuntimeFix.MD500FlightControls.NativeTiltForce(1,flying),0,.0001f,"native roll does not fight bank");
   flying=new EntityVehicle();flying.vehicle.Name=name;flying.vehicleRB.velocity=new UnityEngine.Vector3(0,0,20);flying.vehicleRB.angularVelocity=new UnityEngine.Vector3(0,.5f,0);
   AECT16RuntimeFix.MD500FlightControls.BeforeForces(flying);
   Check(flying.vehicleRB.torque.z<0,"right turn leans right");Check(flying.vehicleRB.force.z<0,"release at speed applies gradual braking");
   Near(flying.vehicleRB.force.y,9.81f,.0001f,"attitude preserves independent lift");
   flying=new EntityVehicle();flying.vehicle.Name=name;AECT16RuntimeFix.MD500FlightControls.BeforeForces(flying);
   UnityEngine.Time.fixedTime+=.02f;flying.position=new UnityEngine.Vector3(0,98,0);flying.vehicleRB.force=new UnityEngine.Vector3();
   AECT16RuntimeFix.MD500FlightControls.BeforeForces(flying);
   Check(flying.vehicleRB.force.y>9.81f,"level-flight altitude hold restores height "+name);
   UnityEngine.Time.fixedTime+=.02f;flying.movementInput.down=true;flying.vehicleRB.force=new UnityEngine.Vector3();
   AECT16RuntimeFix.MD500FlightControls.BeforeForces(flying);
   // Vertical correction now slews across zero instead of changing instantly.
   UnityEngine.Time.fixedTime+=.02f;flying.vehicleRB.force=new UnityEngine.Vector3();
   AECT16RuntimeFix.MD500FlightControls.BeforeForces(flying);
   Check(flying.vehicleRB.force.y<9.81f,"explicit descent overrides altitude hold "+name);
  }
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
   Near(AECT16RuntimeFix.MD500FlightControls.NativeTiltForce(1,e),1,.0001f,"unrelated native roll retained");
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
  for(int i=0;i<2;i++)inputCode.Add(new HarmonyLib.CodeInstruction(OpCodes.Callvirt,typeof(Vehicle).GetProperty("TiltUpForce").GetMethod));
  int n=0;foreach(var op in AECT16RuntimeFix.MD500FlightControls.GroundActionsTranspiler(inputCode))n++;Check(n==15,"three ground loads and two native tilt forces wrapped");
  bool rejected=false;try{AECT16RuntimeFix.MD500FlightControls.GroundActionsTranspiler(new List<HarmonyLib.CodeInstruction>());}catch(InvalidOperationException){rejected=true;}Check(rejected,"unsupported ground IL rejected");
  var fuelCode=new[]{new HarmonyLib.CodeInstruction(OpCodes.Call,typeof(UnityEngine.Vector3).GetProperty("magnitude").GetMethod)};
  n=0;foreach(var op in AECT16RuntimeFix.MD500FlightControls.FuelTranspiler(fuelCode))n++;Check(n==3,"fuel magnitude wrapped once");
  Extended();
  AECT16RuntimeFix.MD500FlightControls.Install(new HarmonyLib.Harmony{Fail=true});
  e=new EntityVehicle();Check(AECT16RuntimeFix.MD500FlightControls.BeforeForces(e),"failed install preserves original controls");
  Check(AECT16RuntimeFix.MD500FlightControls.GroundAction(true,e),"failed install ground unchanged");
  e.vehicle.Name="vehicleApacheHelicopter";Check(AECT16RuntimeFix.MD500FlightControls.BeforeForces(e),"failed install Apache original controls retained");
  Console.WriteLine("PASS: "+checks+" MD-500 flight-control assertions (math simulation and runtime fixtures; not a Unity playtest).");
 }

 static void Extended(){
  foreach(bool apache in new[]{false,true}){
   var p=AECT16RuntimeFix.MD500FlightMath.Handling(apache);
   foreach(float speed in new[]{10f,25f,32.4f})foreach(bool turbo in new[]{false,true}){
    var brake=AECT16RuntimeFix.MD500FlightMath.TargetAttitude(speed,-1,-p.Acceleration,-p.Acceleration,0,0,true,turbo,p);
    Check(brake.Pitch<0,"active braking raises nose at all tested speeds/modes");
    var coast=AECT16RuntimeFix.MD500FlightMath.TargetAttitude(speed,0,-p.Acceleration*.3f,-p.Acceleration*.3f,0,0,true,turbo,p);
    Check(coast.Pitch<0,"coast cruise bias no longer cancels braking");
    var reverse=AECT16RuntimeFix.MD500FlightMath.TargetAttitude(-speed,1,p.Acceleration,p.Acceleration,0,0,true,turbo,p);
    Check(reverse.Pitch>0,"braking reverse flight lowers nose");
   }
   var level=AECT16RuntimeFix.MD500FlightMath.TargetAttitude(20,1,3,0,0,0,true,false,p);
   var climb=AECT16RuntimeFix.MD500FlightMath.TargetAttitude(20,1,3,0,0,4,true,false,p);
   Check(climb.Pitch>0&&climb.Pitch<level.Pitch,"cruise climb softens nose-down lean without commanding nose-up");
   var hover=AECT16RuntimeFix.MD500FlightMath.TargetAttitude(0,0,0,0,0,4,true,false,p);
   Near(hover.Pitch,0,.0001f,"vertical-only climb stays level");
   foreach(float speed in new[]{-25f,-10f,0f,10f,25f,32.4f}){
    float r=AECT16RuntimeFix.MD500FlightMath.TurnRateLimit(Math.Abs(speed),1,1,p);
    Check(Math.Abs(speed*r)<=.85f*AECT16RuntimeFix.MD500FlightMath.LateralLimit(1,1,p)+.001f,"turn demand fits lateral authority");
    var force=AECT16RuntimeFix.MD500FlightMath.Calculate(1,1,false,false,speed,0,0,r,100,100,true,1,32.4f,17.28f,1,p);
    var a=AECT16RuntimeFix.MD500FlightMath.TargetAttitude(speed,1,0,0,force.Right,0,true,false,p);
    Check(speed==0?Math.Abs(a.Bank)<.001f:a.Bank*speed>0,"bank matches signed forward/reverse centripetal force");
    Check(Math.Abs(a.Bank)<=p.BankMax,"bank within profile limit");
   }
   Check(AECT16RuntimeFix.MD500FlightMath.TurnRateLimit(30,1,1,p)<AECT16RuntimeFix.MD500FlightMath.TurnRateLimit(3,1,1,p),"fast flight turns more gently");
   foreach(float dt in new[]{.01f,.02f,.04f}){
    float y=100,vy=4,correction=0,alt=100;bool holding=false;float maxY=100;float minAfterCapture=1000,captured=0;
    for(int i=0;i<(int)(12/dt);i++){
     if(!holding&&Math.Abs(vy)<.15f){holding=true;alt=y;captured=y;}
     var force=AECT16RuntimeFix.MD500FlightMath.Calculate(1,0,false,false,20,0,vy,0,y,alt,holding,1,25,10,1,p);
     float next=AECT16RuntimeFix.MD500FlightMath.SmoothAxis(correction,force.Up-9.81f,dt,6,p.VerticalJerk);
     Check(Math.Abs(next-correction)<=p.VerticalJerk*dt+.0001f,"vertical correction jerk bounded");
     correction=next;vy+=correction*dt;y+=vy*dt;maxY=Math.Max(maxY,y);
     if(holding)minAfterCapture=Math.Min(minAfterCapture,y);
    }
    Check(holding&&captured>100,"climb release captures stopping altitude");
    Check(maxY-captured<.25f&&captured-minAfterCapture<.25f,"height capture avoids large overshoot or return to release point");
    Near(vy,0,.05f,"height capture settles");
    SimulateTurn(p,dt,25);SimulateTurn(p,dt,-10);
    foreach(float power in new[]{.75f,1f})foreach(float torque in new[]{.5f,2f}){
     SimulateTurn(p,dt,25,power,torque);SimulateTurn(p,dt,-10,power,torque);
    }
    float angle=0,rate=0,peak=0;
    for(int i=0;i<(int)(12/dt);i++){
     rate*=.97f;rate+=(p.AttitudeGain*(float)Math.Sin(.2f-angle)-p.AttitudeDamping*rate)*dt;
     angle+=rate*dt;peak=Math.Max(peak,angle);
    }
    Near(angle,.2f,.005f,"damped attitude reaches target");
    Check(peak<.23f,"damped attitude avoids excessive overshoot");
   }
  }
  Check(AECT16RuntimeFix.MD500FlightMath.Handling(true).Jerk<AECT16RuntimeFix.MD500FlightMath.Handling(false).Jerk,"Apache builds thrust more gradually");
  foreach(string name in new[]{"vehicleMD500","vehicleApacheHelicopter"}){
   var tilted=new EntityVehicle();tilted.vehicle.Name=name;tilted.vehicleRB.rotation=new UnityEngine.Quaternion{Pitch=12,Roll=14};
   AECT16RuntimeFix.MD500FlightControls.BeforeForces(tilted);
   Near(tilted.vehicleRB.force.y,9.81f,.001f,"tilted aircraft maintains world-up lift");
   var parked=new EntityVehicle();parked.vehicle.Name=name;parked.Wheels=3;parked.vehicleRB.rotation=new UnityEngine.Quaternion{Pitch=12,Roll=8};
   AECT16RuntimeFix.MD500FlightControls.BeforeForces(parked);
   Near(parked.vehicleRB.torque.magnitude,0,.001f,"slope parking is not forced horizontal");
   parked.Wheels=0;
   for(int i=0;i<3;i++){UnityEngine.Time.fixedTime+=.02f;parked.vehicleRB.torque=new UnityEngine.Vector3();AECT16RuntimeFix.MD500FlightControls.BeforeForces(parked);}
   Near(parked.vehicleRB.torque.magnitude,0,.001f,"brief wheel contact loss does not enable flight leveling");
   for(int i=0;i<40;i++){UnityEngine.Time.fixedTime+=.02f;parked.vehicleRB.torque=new UnityEngine.Vector3();AECT16RuntimeFix.MD500FlightControls.BeforeForces(parked);}
   Check(parked.vehicleRB.torque.magnitude>0,"confirmed takeoff enables attitude control");
   parked.Wheels=3;parked.vehicleRB.angularVelocity=new UnityEngine.Vector3();parked.vehicleRB.torque=new UnityEngine.Vector3();
   AECT16RuntimeFix.MD500FlightControls.BeforeForces(parked);
   Near(parked.vehicleRB.torque.magnitude,0,.001f,"touchdown immediately stops pulling toward world-horizontal");
  }
 }

 static void RuntimeLevelHeight(string name,float power,float dt){
  UnityEngine.Time.fixedDeltaTime=dt;UnityEngine.Time.fixedTime+=1;
  var e=new EntityVehicle();e.vehicle.Name=name;e.motors[0].rpm=e.motors[0].rpmMax*power;
  var p=AECT16RuntimeFix.MD500FlightMath.Handling(name=="vehicleApacheHelicopter");
  double heading=0;float cleanError=0,dip=0;
  bool disturbed=false;
  for(int i=0;i<(int)(60/dt);i++){
   float time=i*dt;
   e.movementInput.moveForward=time<10?0:time<40?1:time<50?0:-1;
   e.movementInput.moveStrafe=time>=20&&time<40?1:0;
   e.vehicle.IsTurbo=time>=15&&time<35;
   // Exercise maximum commanded pitch/bank, including the new larger turn limits.
   e.vehicleRB.rotation=new UnityEngine.Quaternion{Yaw=(float)(heading*180/Math.PI),Pitch=time>=10?p.PitchMax:0,Roll=time>=20&&time<40?p.BankMax:0};
   if(time>=45&&!disturbed){e.vehicleRB.velocity.y=-1;disturbed=true;}
   e.vehicleRB.velocity=e.vehicleRB.velocity*e.vehicle.AirDragVelScale;
   e.vehicleRB.angularVelocity=e.vehicleRB.angularVelocity*e.vehicle.AirDragAngVelScale;
   e.vehicleRB.force=new UnityEngine.Vector3();e.vehicleRB.torque=new UnityEngine.Vector3();
   UnityEngine.Time.fixedTime+=dt;AECT16RuntimeFix.MD500FlightControls.BeforeForces(e);
   e.vehicleRB.velocity=e.vehicleRB.velocity+(e.vehicleRB.force-new UnityEngine.Vector3(0,9.81f,0))*dt;
   e.position=e.position+e.vehicleRB.velocity*dt;
   e.vehicleRB.angularVelocity=new UnityEngine.Vector3(0,e.vehicleRB.angularVelocity.y+e.vehicleRB.torque.y*dt,0);
   heading+=e.vehicleRB.angularVelocity.y*dt;
   if(!disturbed)cleanError=Math.Max(cleanError,Math.Abs(e.position.y-100));
   else dip=Math.Max(dip,100-e.position.y);
  }
  Check(cleanError<.001f,"hover/cruise/turbo/banked turn/braking do not lose height at flight RPM");
  Check(dip>0&&dip<.6f,"external downward velocity has a bounded temporary dip");
  Near(e.position.y,100,.01f,"altitude hold returns to original height after downward disturbance");
  Near(e.vehicleRB.velocity.y,0,.01f,"vertical motion settles after recovery");
  Console.WriteLine("Height "+name+" power="+power+" dt="+dt+" normalError="+cleanError.ToString("F5")+" disturbedDip="+dip.ToString("F3")+" final="+e.position.y.ToString("F4"));
 }

 static void RuntimeTurnResponse(string name,float dt,int sign){
  AECT16RuntimeFix.MD500FlightControls.Install(new HarmonyLib.Harmony());
  UnityEngine.Time.fixedDeltaTime=dt;UnityEngine.Time.fixedTime+=1;
  var e=new EntityVehicle();e.vehicle.Name=name;e.vehicleRB.velocity=new UnityEngine.Vector3(0,0,25);
  e.movementInput.moveForward=1;
  var p=AECT16RuntimeFix.MD500FlightMath.Handling(name=="vehicleApacheHelicopter");
  double heading=0;float maxSlip=0,turnRate=0,releaseHeading=0;
  for(int i=0;i<(int)(16/dt);i++){
   float time=i*dt;e.movementInput.moveStrafe=time<12?sign:0;
   e.vehicleRB.rotation=new UnityEngine.Quaternion{Yaw=(float)(heading*180/Math.PI)};
   e.vehicleRB.velocity=e.vehicleRB.velocity*e.vehicle.AirDragVelScale;
   e.vehicleRB.angularVelocity=e.vehicleRB.angularVelocity*e.vehicle.AirDragAngVelScale;
   e.vehicleRB.force=new UnityEngine.Vector3();e.vehicleRB.torque=new UnityEngine.Vector3();
   UnityEngine.Time.fixedTime+=dt;AECT16RuntimeFix.MD500FlightControls.BeforeForces(e);
   Check(Math.Abs(e.vehicleRB.torque.y)<=2.0001f,"drag compensation respects yaw torque bound");
   e.vehicleRB.velocity=e.vehicleRB.velocity+(e.vehicleRB.force-new UnityEngine.Vector3(0,9.81f,0))*dt;
   e.vehicleRB.angularVelocity=new UnityEngine.Vector3(0,e.vehicleRB.angularVelocity.y+e.vehicleRB.torque.y*dt,0);
   heading+=e.vehicleRB.angularVelocity.y*dt;
   var right=e.vehicleRB.rotation*new UnityEngine.Vector3(1,0,0);
   maxSlip=Math.Max(maxSlip,Math.Abs(UnityEngine.Vector3.Dot(e.vehicleRB.velocity,right)));
   if(time<12){turnRate=e.vehicleRB.angularVelocity.y;releaseHeading=(float)heading;}
   if(time>=14)Check(Math.Abs(e.vehicleRB.angularVelocity.y)<.01f,"turn release settles within two seconds");
  }
  float oldLimit=(name=="vehicleApacheHelicopter"?4.48f:5.16f)*(float)Math.PI/180;
  Check(turnRate*sign>oldLimit*2,"runtime cruise turn exceeds twice former target despite native drag");
  Check(maxSlip<2.5f,"stronger runtime turn remains coordinated, slip="+maxSlip);
  Check(Math.Abs(heading-releaseHeading)*180/Math.PI<15,"release does not keep swinging through a large angle");
  Console.WriteLine("Turn "+name+" dt="+dt+" sign="+sign+" deg/s="+(turnRate*180/Math.PI).ToString("F2")+" maxSlip="+maxSlip.ToString("F2"));
 }

 static void RuntimeClimbTurn(string name,float dt,int verticalSign){
  UnityEngine.Time.fixedDeltaTime=dt;UnityEngine.Time.fixedTime+=1;
  var e=new EntityVehicle();e.vehicle.Name=name;e.vehicle.IsTurbo=true;e.movementInput.moveForward=1;
  double heading=0;float releaseY=0,settledY=0,lastY=0;
  for(int i=0;i<(int)(24/dt);i++){
   float t=i*dt;
   e.movementInput.moveStrafe=t<12?1:0;e.movementInput.jump=t<6&&verticalSign>0;e.movementInput.down=t<6&&verticalSign<0;
   if(i==(int)(6/dt))releaseY=e.position.y;
   // Reproduce native velocity damping, gravity and accumulated force integration.
   e.vehicleRB.velocity=e.vehicleRB.velocity*e.vehicle.AirDragVelScale;
   e.vehicleRB.angularVelocity=e.vehicleRB.angularVelocity*.97f;
   e.vehicleRB.rotation=new UnityEngine.Quaternion{Yaw=(float)(heading*180/Math.PI),Pitch=8,Roll=6};
   e.vehicleRB.force=new UnityEngine.Vector3();e.vehicleRB.torque=new UnityEngine.Vector3();
   UnityEngine.Time.fixedTime+=dt;AECT16RuntimeFix.MD500FlightControls.BeforeForces(e);
   e.vehicleRB.velocity=e.vehicleRB.velocity+(e.vehicleRB.force-new UnityEngine.Vector3(0,9.81f,0))*dt;
   e.position=e.position+e.vehicleRB.velocity*dt;
   e.vehicleRB.angularVelocity=new UnityEngine.Vector3(0,e.vehicleRB.angularVelocity.y+e.vehicleRB.torque.y*dt,0);
   heading+=e.vehicleRB.angularVelocity.y*dt;
   if(i==(int)(18/dt))settledY=e.position.y;
   lastY=e.position.y;
   Check(!float.IsNaN(lastY)&&Math.Abs(e.vehicleRB.velocity.y)<6,"combined runtime climb/turn remains finite and bounded");
  }
  Check((settledY-releaseY)*verticalSign>=0,"release captures altitude after vertical braking");
  Check(Math.Abs(settledY-releaseY)<5,"capture stopping distance remains moderate");
  Near(lastY,settledY,.12f,"simultaneous forward/turn/vertical release holds settled altitude");
  Near(e.vehicleRB.velocity.y,0,.05f,"runtime vertical capture settles with native drag");
  Near(e.vehicleRB.angularVelocity.y,0,.01f,"runtime turn release settles with native drag");
 }

 // Integrate world-plane velocity and heading together. This catches turn/slip
 // problems that independent scalar forward/yaw tests cannot detect.
 static void SimulateTurn(AECT16RuntimeFix.MD500FlightMath.Profile p,float dt,float initial,float power=1,float torque=1){
  double heading=0;float vx=0,vz=initial,yaw=0,fx=0,fy=0,yawForce=0,maxSlip=0;
  for(int i=0;i<(int)(18/dt);i++){
   float s=(float)Math.Sin(heading),c=(float)Math.Cos(heading);
   float vf=vx*s+vz*c,vr=vx*c-vz*s;
   float turn=i*dt<12?1:0;
   var f=AECT16RuntimeFix.MD500FlightMath.Calculate(initial>0?1:-1,turn,false,false,vf,vr,0,yaw,100,100,true,power,25,10,torque,p);
   fx=AECT16RuntimeFix.MD500FlightMath.SmoothAxis(fx,f.Forward,dt,p.Acceleration*torque*power,p.Jerk*torque);
   fy=AECT16RuntimeFix.MD500FlightMath.SmoothAxis(fy,f.Right,dt,AECT16RuntimeFix.MD500FlightMath.LateralLimit(power,torque,p),p.LateralJerk*torque);
   yawForce=AECT16RuntimeFix.MD500FlightMath.SmoothAxis(yawForce,f.Yaw,dt,2*power,p.YawJerk);
   // Deliberately omit native drag: this is the more demanding turn case.
   vx+=(s*fx+c*fy)*dt;vz+=(c*fx-s*fy)*dt;yaw+=yawForce*dt;heading+=yaw*dt;
   maxSlip=Math.Max(maxSlip,Math.Abs(vr));
  }
  Check(maxSlip<2.5f,"coupled forward/reverse turn avoids excessive sideslip; got "+maxSlip);
  Near(yaw,0,.01f,"turn release settles without persistent yaw");
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
    $tiltSites = @($physics.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString() -eq 'System.Single Vehicle::get_TiltUpForce()' })
    if ($tiltSites.Count -ne 2) { throw 'Unsupported native tilt-force sites' }
    $loads = @($physics.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldfld' -and $_.Operand.DeclaringType.FullName -eq 'MovementInput' -and $_.Operand.Name -in @('jump','down') })
    if ($loads.Count -ne 3) { throw "Unsupported native ground-input sites: $($loads.Count)" }
    $engine = ($game.MainModule.Types | Where-Object Name -eq 'VPEngine').Methods | Where-Object Name -eq 'Update'
    $magnitudes = @($engine.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString() -eq 'System.Single UnityEngine.Vector3::get_magnitude()' })
    if ($magnitudes.Count -ne 1) { throw 'Unsupported engine fuel IL' }
    $ops = $physics.Body.Instructions
    $angularDrag = $ops | Where-Object { $_.Operand -and $_.Operand.ToString() -eq 'System.Single Vehicle::AirDragAngVelScale' }
    $forces = $ops | Where-Object { $_.Operand -and $_.Operand.ToString() -eq 'System.Void EntityVehicle::FixedUpdateForces()' }
    if (!$angularDrag -or !$forces -or $angularDrag.Offset -ge $forces.Offset) { throw 'Recheck native angular damping order before applying compensation' }
    $simulation = $ops | Where-Object { $_.Operand -and $_.Operand.ToString() -eq 'System.Void Vehicle::UpdateSimulation()' }
    $sends = @($ops | Where-Object { $_.Operand -and $_.Operand.ToString() -eq 'System.Void EntityVehicle::SendSyncData(System.UInt16)' })
    if (!$simulation -or !$sends.Count -or $simulation.Offset -ge $sends[0].Offset) { throw 'Engine state would be updated after sync' }
    Write-Output 'PASS: V3.2 native IL has three ground-input loads, one engine speed/fuel site, and simulation precedes network sends.'
} finally { $game.Dispose() }
