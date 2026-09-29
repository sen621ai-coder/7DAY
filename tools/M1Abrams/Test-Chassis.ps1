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
  Check(PZAEC.M1.ChassisRules.CanAssist(1,-1,0,0,0,true,false),"slow reverse assistance");
  Check(!PZAEC.M1.ChassisRules.CanAssist(1.2f,-1,0,0,0,true,false),"reverse speed cap");
  Check(!PZAEC.M1.ChassisRules.CanAssist(1,1,1,0,0,true,false),"turning into side of obstacle");
  Check(!PZAEC.M1.ChassisRules.CanAssist(1,1,0,28,0,true,false),"pitch boundary");
  Check(!PZAEC.M1.ChassisRules.CanAssist(1,1,0,0,-15,true,false),"side tilt boundary");
  Check(!PZAEC.M1.ChassisRules.CanAssist(1,1,0,0,0,false,false),"one side missing support");
  Check(!PZAEC.M1.ChassisRules.CanAssist(1,1,0,0,0,true,true),"braking cancels assist");
  Check(PZAEC.M1.ChassisRules.Landing(.6f,.6f,1),"maximum target accepted");
  Check(!PZAEC.M1.ChassisRules.Landing(1,1,1),"full block denied");
  Check(!PZAEC.M1.ChassisRules.Landing(.4f,.8f,1),"another riser behind step denied");
  Check(!PZAEC.M1.ChassisRules.Landing(.4f,-1,1),"missing far landing denied");
  Check(!PZAEC.M1.ChassisRules.Landing(.4f,.4f,.6f),"steep landing denied");
  Check(!PZAEC.M1.ChassisRules.Landing(float.NaN,.4f,1),"invalid sample denied");
  Check(!PZAEC.M1.ChassisRules.CanAssist(float.NaN,1,0,0,0,true,false),"invalid speed denied");
  Check(PZAEC.M1.ChassisRules.HeightLimit(true)==.3f,"reverse height is separate");
  Check(PZAEC.M1.ChassisRules.TractionScale(0,0,0,true)==.6f,"reverse force reduction");
  Check(PZAEC.M1.ChassisRules.CanContinue(2.3f,1,0,0,0,true,false),"small speed overshoot retains obstacle state");
  Check(PZAEC.M1.ChassisRules.TractionScale(2.3f,0,0,false)==0,"overshoot never extends powered speed range");
  float last=32;for(int k=0;k<=100;k++){
   float limit=PZAEC.M1.ChassisRules.SteeringLimit(k/3.6f);
   Check(limit<=last+.00001f&&limit>=14&&limit<=32,"bounded continuous high speed steering "+k);last=limit;
  }
  Check(PZAEC.M1.ChassisRules.SteeringLimit(0)==32&&PZAEC.M1.ChassisRules.SteeringLimit(15)==14,"low/high steering endpoints");
  for(int kmh=0;kmh<=80;kmh++){
   float speed=kmh/3.6f;
   float yaw=PZAEC.M1.ChassisRules.YawRate(speed,speed,1,1,20,1);
   Check(yaw>0&&yaw*speed<=4.0001f,"moving steering remains available and laterally bounded "+kmh);
   Check(System.Math.Abs(PZAEC.M1.ChassisRules.YawRate(speed,speed,1,-1,20,1)+yaw)<.0001f,"left right symmetry "+kmh);
   Check(PZAEC.M1.ChassisRules.YawRate(speed,speed,1,0,20,1)==0,"neutral input no forced turn "+kmh);
  }
  Check(PZAEC.M1.ChassisRules.YawRate(2,-2,-1,1,20,1)<0,"reverse follows native steering direction");
  Check(PZAEC.M1.ChassisRules.YawRate(0,0,-1,1,20,1)<0,"reverse start has no opposite steering kick");
  Check(PZAEC.M1.ChassisRules.YawAcceleration(.3f,.3f)==0,"yaw at target adds no angular acceleration");
  Check(PZAEC.M1.ChassisRules.YawAcceleration(2,0)==.8f&&PZAEC.M1.ChassisRules.YawAcceleration(-2,0)==-.8f,"bounded yaw acceleration");
  foreach(float dt in new[]{.01f,.02f,.04f}){
   var stuck=new PZAEC.M1.ChassisRules.Attempt();
   Check(stuck.Tick(true,dt,0),"start attempt");
   for(int i=0;i<200;i++)stuck.Tick(true,dt,0);
   Check(stuck.Blocked&&!stuck.Active,"stalled wall expires");
   Check(!stuck.Tick(false,dt,0)&&!stuck.Tick(true,dt,0),"release and repress cannot climb wall repeatedly");
   Check(!stuck.Tick(true,dt,-.49f),"insufficient retreat");
   Check(stuck.Tick(true,dt,-.51f),"retreat rearms attempt");
   var moving=new PZAEC.M1.ChassisRules.Attempt();float distance=0;
   for(int i=0;i<800;i++){distance+=dt;moving.Tick(true,dt,distance);}
   Check(moving.Blocked,"even progressing attempts time out");
   var lost=new PZAEC.M1.ChassisRules.Attempt();lost.Tick(true,dt,0);
   Check(!lost.Tick(false,dt,.1f)&&lost.Blocked,"lost support or fuel abort immediately");
   var complete=new PZAEC.M1.ChassisRules.Attempt();complete.Begin(6);
   for(int i=0;i<260;i++)complete.Advance(true,true,.02f,i*.03f,3);
   Check(!complete.Active&&!complete.Blocked&&complete.Phase==PZAEC.M1.ChassisRules.Stage.Complete,"rear clears obstacle and permits next attempt");
   complete.Begin(6);Check(complete.Advance(true,true,.02f,0,3),"next obstacle requires no retreat");
   Check(!complete.Advance(true,false,.02f,.1f,3)&&complete.Active,"probe loss stops force immediately but allows short recovery");
   Check(complete.Advance(true,true,.02f,.12f,3),"fresh geometry resumes attempt");
   Check(!complete.Advance(false,true,.02f,.14f,3)&&complete.Blocked,"safety exit has no grace period");
   var jitter=new PZAEC.M1.ChassisRules.Attempt();jitter.Begin(6);
   for(int i=0;i<50;i++)jitter.Advance(true,true,.02f,i%2==0?0:.02f,3);
   Check(jitter.Blocked,"rocking without net progress cannot renew attempt");
   var missed=new PZAEC.M1.ChassisRules.Attempt();missed.Begin(6);
   for(int i=0;i<10;i++)missed.Advance(true,false,.02f,i*.01f,3);
   Check(missed.Blocked,"prolonged missing landing aborts");
  }
  System.Console.WriteLine("PASS "+n+" chassis eligibility, landing, steering and finite-attempt checks");
 }
}
'@
Add-Type -TypeDefinition ($rules+$tests)
[M1ChassisTests]::Run()
