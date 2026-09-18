$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
$rules=Get-Content "$root/ZZ-PZAEC_M1Abrams/Source/M1ChassisRules.cs" -Raw
$tests=@'
public static class M1ChassisTests {
 static int n;static void Check(bool ok,string label){n++;if(!ok)throw new System.Exception(label);}
 public static void Run(){
  Check(PZAEC.M1.ChassisRules.CanAssist(1,1,0,0,0,true,false),"slow straight grounded approach");
  Check(!PZAEC.M1.ChassisRules.CanAssist(3,1,0,0,0,true,false),"high speed cannot trigger assist");
  Check(!PZAEC.M1.ChassisRules.CanAssist(1,0,0,0,0,true,false),"throttle release");
  Check(!PZAEC.M1.ChassisRules.CanAssist(1,-1,0,0,0,true,false),"reverse remains native");
  Check(!PZAEC.M1.ChassisRules.CanAssist(1,1,1,0,0,true,false),"turning into side of obstacle");
  Check(!PZAEC.M1.ChassisRules.CanAssist(1,1,0,18,0,true,false),"pitch boundary");
  Check(!PZAEC.M1.ChassisRules.CanAssist(1,1,0,0,-12,true,false),"side tilt boundary");
  Check(!PZAEC.M1.ChassisRules.CanAssist(1,1,0,0,0,false,false),"one side missing support");
  Check(!PZAEC.M1.ChassisRules.CanAssist(1,1,0,0,0,true,true),"braking cancels assist");
  Check(PZAEC.M1.ChassisRules.Landing(.6f,.6f,1),"maximum target accepted");
  Check(!PZAEC.M1.ChassisRules.Landing(1,1,1),"full block denied");
  Check(!PZAEC.M1.ChassisRules.Landing(.4f,.8f,1),"another riser behind step denied");
  Check(!PZAEC.M1.ChassisRules.Landing(.4f,-1,1),"missing far landing denied");
  Check(!PZAEC.M1.ChassisRules.Landing(.4f,.4f,.6f),"steep landing denied");
  Check(!PZAEC.M1.ChassisRules.Landing(float.NaN,.4f,1),"invalid sample denied");
  float last=25;for(int k=0;k<=100;k++){
   float limit=PZAEC.M1.ChassisRules.SteeringLimit(k/3.6f);
   Check(limit<=last+.00001f&&limit>=8&&limit<=25,"bounded continuous high speed steering "+k);last=limit;
  }
  Check(PZAEC.M1.ChassisRules.SteeringLimit(0)==25&&PZAEC.M1.ChassisRules.SteeringLimit(10)==8,"low/high steering endpoints");
  foreach(float dt in new[]{.01f,.02f,.04f}){
   var stuck=new PZAEC.M1.ChassisRules.Attempt();
   Check(stuck.Tick(true,dt,0),"start attempt");
   for(int i=0;i<200;i++)stuck.Tick(true,dt,0);
   Check(stuck.Blocked&&!stuck.Active,"stalled wall expires");
   Check(!stuck.Tick(false,dt,0)&&!stuck.Tick(true,dt,0),"release and repress cannot climb wall repeatedly");
   Check(!stuck.Tick(true,dt,-.49f),"insufficient retreat");
   Check(stuck.Tick(true,dt,-.51f),"retreat rearms attempt");
   var moving=new PZAEC.M1.ChassisRules.Attempt();float distance=0;
   for(int i=0;i<300;i++){distance+=dt;moving.Tick(true,dt,distance);}
   Check(moving.Blocked,"even progressing attempts time out");
   var lost=new PZAEC.M1.ChassisRules.Attempt();lost.Tick(true,dt,0);
   Check(!lost.Tick(false,dt,.1f)&&lost.Blocked,"lost support or fuel abort immediately");
  }
  System.Console.WriteLine("PASS "+n+" chassis eligibility, landing, steering and finite-attempt checks");
 }
}
'@
Add-Type -TypeDefinition ($rules+$tests)
[M1ChassisTests]::Run()
