$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$rules=Get-Content (Join-Path $root '99-AEC_T16_RuntimeFix/Source/ApacheWeaponRules.cs') -Raw
$tests=@'
public static class ApacheWeaponTests {
 static int checks;
 static void Check(bool b,string message){checks++;if(!b)throw new System.Exception(message);}
 public static void Run(){
  Check(AECT16RuntimeFix.ApacheWeaponRules.OperatorAllowed(0,10,10,false,false,false),"pilot occupant accepted");
  Check(AECT16RuntimeFix.ApacheWeaponRules.OperatorAllowed(1,11,11,false,false,false),"gunner occupant accepted");
  foreach(int s in new[]{-1,2,255})Check(!AECT16RuntimeFix.ApacheWeaponRules.OperatorAllowed(s,10,10,false,false,false),"invalid seat rejected");
  Check(!AECT16RuntimeFix.ApacheWeaponRules.OperatorAllowed(0,11,10,false,false,false),"gunner cannot operate pilot seat");
  Check(!AECT16RuntimeFix.ApacheWeaponRules.OperatorAllowed(1,10,11,false,false,false),"pilot cannot operate gunner seat");
  Check(!AECT16RuntimeFix.ApacheWeaponRules.OperatorAllowed(0,10,-1,false,false,false),"dismount cancels salvo");
  Check(!AECT16RuntimeFix.ApacheWeaponRules.OperatorAllowed(0,10,10,true,false,false),"dead operator rejected");
  Check(!AECT16RuntimeFix.ApacheWeaponRules.OperatorAllowed(0,10,10,false,true,false),"destroyed helicopter rejected");
  Check(!AECT16RuntimeFix.ApacheWeaponRules.OperatorAllowed(1,11,11,false,false,true),"storage editor pauses firing");
  Check(!AECT16RuntimeFix.ApacheWeaponRules.OperatorAllowed(0,-1,-1,false,false,false),"missing entity rejected");
  Check(AECT16RuntimeFix.ApacheWeaponRules.InArc(0,0,1),"forward aim accepted");
  Check(!AECT16RuntimeFix.ApacheWeaponRules.InArc(0,0,-1),"rear cockpit direction blocked");
  Check(!AECT16RuntimeFix.ApacheWeaponRules.InArc(0,1,0),"rotor direction blocked");
  Check(!AECT16RuntimeFix.ApacheWeaponRules.InArc(0,-1,0),"straight down outside mount limits");
  Check(!AECT16RuntimeFix.ApacheWeaponRules.ValidDirection(0,0,0),"zero aim rejected");
  foreach(float bad in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity,float.MaxValue}){
   Check(!AECT16RuntimeFix.ApacheWeaponRules.ValidDirection(bad,0,1),"nonfinite/oversized aim rejected");
   Check(!AECT16RuntimeFix.ApacheWeaponRules.InArc(0,bad,1),"nonfinite pitch rejected");
  }
  foreach(double yaw in new[]{-100.0,100.0}){
   double radians=yaw*System.Math.PI/180;
   Check(AECT16RuntimeFix.ApacheWeaponRules.InArc((float)System.Math.Sin(radians),0,(float)System.Math.Cos(radians)),"yaw limit accepted");
  }
  foreach(double pitch in new[]{-85.0,-80.0,-70.0,15.0}){
   double radians=pitch*System.Math.PI/180;
   Check(AECT16RuntimeFix.ApacheWeaponRules.InArc(0,(float)System.Math.Sin(radians),(float)System.Math.Cos(radians)),"pitch limit accepted");
  }
  foreach(double pitch in new[]{-86.0,16.0}){
   double radians=pitch*System.Math.PI/180;
   Check(!AECT16RuntimeFix.ApacheWeaponRules.InArc(0,(float)System.Math.Sin(radians),(float)System.Math.Cos(radians)),"pitch beyond limit rejected");
  }
  var gate=new AECT16RuntimeFix.ApacheWeaponRules.Gate();
  Check(gate.Ready(0,0)&&gate.Ready(1,0),"both weapons initially ready");
  gate.Commit(0,0);Check(!gate.Ready(0,1.49f),"rocket cooldown rejects replay");
  Check(gate.Ready(1,.1f),"pilot fire does not block gunner");
  Check(gate.Ready(0,1.5f),"rocket cooldown expires");
  gate=new AECT16RuntimeFix.ApacheWeaponRules.Gate();
  Check(!gate.Ready(1,float.NaN),"NaN clock rejected");
  gate.Commit(1,0);Check(!gate.Ready(1,.1f),"cannon rate enforced");
  Check(gate.Ready(0,.1f),"gunner fire does not block pilot");
  gate=new AECT16RuntimeFix.ApacheWeaponRules.Gate();float now=0;int shots=0;
  while(!gate.Overheated&&shots<100){Check(gate.Ready(1,now),"scheduled cannon shot ready");gate.Commit(1,now);shots++;now+=.17f;}
  Check(gate.Overheated&&shots>15&&shots<50,"sustained fire overheats");
  Check(!gate.Ready(1,now),"overheated weapon refuses fire");
  Check(gate.Ready(0,now),"overheated cannon does not disable rockets");
  Check(!gate.Ready(1,now+1),"hysteresis prevents immediate cooling exploit");
  Check(gate.Ready(1,now+5)&&!gate.Overheated,"cooldown restores cannon");
  gate.Cool(now+100);Check(gate.Heat==0,"heat never negative");
  gate=new AECT16RuntimeFix.ApacheWeaponRules.Gate();
  for(int i=0;i<10;i++)Check(gate.Ready(1,i),"dry-fire readiness query does not spend heat or cooldown");
  Check(gate.Heat==0,"failed ammo removal must not commit heat");
  var lease=new AECT16RuntimeFix.ApacheWeaponRules.TriggerLease();
  Check(lease.Accept(7,10,true,0)&&lease.Active(7,.49f),"held trigger lease accepted");
  Check(!lease.Active(7,.5f),"lost heartbeat stops at timeout");
  Check(!lease.Active(8,.1f),"seat change cannot inherit fire");
  Check(lease.Accept(7,11,false,.2f)&&!lease.Active(7,.21f),"release stops immediately");
  Check(!lease.Accept(7,10,true,.3f)&&!lease.Active(7,.31f),"late fire cannot undo release");
  Check(!lease.Accept(7,11,true,.3f),"duplicate sequence rejected");
  Check(lease.Accept(8,1,true,.3f)&&lease.Active(8,.4f),"new occupant has independent sequence");
  lease.Stop();Check(!lease.Active(8,.4f),"menu or death cancels held state");
  Check(!lease.Accept(8,2,true,float.NaN),"invalid lease time rejected");
  lease=new AECT16RuntimeFix.ApacheWeaponRules.TriggerLease();
  Check(lease.Accept(7,int.MaxValue,true,0)&&lease.Accept(7,int.MinValue,false,.1f),"sequence wrap retains ordering");
  System.Console.WriteLine("PASS: "+checks+" Apache seat, aim, cooldown, heat and operator-state assertions.");
 }
}
'@
Add-Type -TypeDefinition ($rules+"`n"+$tests)
[ApacheWeaponTests]::Run()

# Validate the authored ammo chain, native sound references and V3.2 damage/lock signatures.
$mod=Join-Path $root 'ZZ-PZAEC_ApacheFlight/Config'
[xml]$items=Get-Content "$mod/items.xml" -Raw
[xml]$recipes=Get-Content "$mod/recipes.xml" -Raw
[xml]$progression=Get-Content "$mod/progression.xml" -Raw
foreach($name in @('pzApacheRocket','pzApache30mm')){
 if($items.SelectNodes("//item[@name='$name']").Count-ne 1){throw "Ammo item missing/duplicated: $name"}
 if($recipes.SelectNodes("//recipe[@name='$name']").Count-ne 1){throw "Ammo recipe missing/duplicated: $name"}
 if($name -notin $progression.SelectSingleNode('//passive_effect').tags.Split(',')){throw "Ammo unlock missing: $name"}
}
[xml]$sounds=Get-Content "$mod/sounds.xml" -Raw
foreach($name in @('pzApacheRocketFire','pzApacheCannonFire')){if(!$sounds.SelectSingleNode("//SoundDataNode[@name='$name']")){throw "Sound missing: $name"}}
Add-Type -Path (Join-Path $root '0_TFP_Harmony/Mono.Cecil.dll')
$game=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path (Split-Path $root) '7DaysToDie_Data/Managed/Assembly-CSharp.dll'))
try {
 $gm=$game.MainModule.Types|Where-Object Name -eq GameManager
 $explosion=@($gm.Methods|Where-Object Name -eq ExplosionServer)
 if($explosion.Count-ne 1 -or $explosion[0].Parameters.Count-ne 8){throw 'Unexpected V3.2 explosion signature'}
 $lock=($game.MainModule.Types|Where-Object Name -eq LockManager).Methods|Where-Object Name -eq IsLockedServer
 if($lock.Parameters.Count-ne 2){throw 'Native storage lock signature changed'}
 $bag=($game.MainModule.Types|Where-Object Name -eq Bag).Methods|Where-Object Name -eq DecItem
 if($bag.ReturnType.FullName-ne 'System.Int32'){throw 'Native ammo-removal result changed'}
 $hit=($game.MainModule.Types|Where-Object Name -eq ItemActionAttack).Methods|Where-Object Name -eq Hit
 if($hit.Parameters.Count-ne 26){throw 'Native attack signature changed'}
 $sync=($game.MainModule.Types|Where-Object Name -eq EntityVehicle).Methods|Where-Object Name -eq SendSyncData
 if(!($sync.Body.Instructions|Where-Object {$_.Operand -and $_.Operand.ToString()-match 'ConnectionManager::SendPackage'})){throw 'Native vehicle storage broadcast missing'}
 Write-Output 'PASS: ammo/sound/unlock references and native V3.2 explosion, ray-hit, lock, cargo-debit and storage-sync interfaces.'
} finally {$game.Dispose()}
