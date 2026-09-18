$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$s=Get-Content "$root/99-AEC_T16_RuntimeFix/Source/ApacheWeapons.cs" -Raw
$start=$s.IndexOf('        private static void FireHeld(')
$end=$s.IndexOf('        private static int Ammo(',$start)
$method=$s.Substring($start,$end-$start)
$stub=@'
using System;
namespace AECT16RuntimeFix {
public static class FireFixture {
 public class Occupant {public int entityId=7;}
 public class Vehicle {public Occupant Person=new Occupant();public Occupant GetAttached(int seat)=>Person;}
 public class State {
  public Vehicle Vehicle=new Vehicle();public ApacheWeaponRules.Gate Gate=new ApacheWeaponRules.Gate();
  public ApacheWeaponRules.TriggerLease[] Triggers={new ApacheWeaponRules.TriggerLease(),new ApacheWeaponRules.TriggerLease()};
  public bool HasSight=true,Ready=true;public byte AimReason;public int SalvoRemaining,SalvoActor,Ammo=100,Shots;public float NextSalvo;
 }
 static bool ReadyOperator(State s,int actor,int seat)=>s.Ready&&s.Vehicle.Person.entityId==actor;
 static bool Consume(State s,string name){if(s.Ammo==0)return false;s.Ammo--;return true;}
 static void Launch(State s,int actor){s.Shots++;}
 static void ShootCannon(State s,int actor){s.Shots++;}
'@
$tests=@'
 static int checks;static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
 public static void Run(){
  var s=new State();s.Triggers[1].Accept(7,1,true,0);
  for(int i=0;i<=40;i++){
   if(i==11||i==23||i==35)s.Triggers[1].Accept(7,i,true,i/100f);
   FireHeld(s,1,i/100f);
  }
  Check(s.Shots==3&&s.Ammo==97,"server cadence independent of jittered heartbeat arrivals, one debit per shot");
  s.Triggers[1].Accept(7,41,false,.41f);FireHeld(s,1,.6f);Check(s.Shots==3,"release stops server firing");
  s.Triggers[1].Accept(7,42,true,.7f);FireHeld(s,1,1.21f);Check(s.Shots==3,"timeout cancels pending held fire");
  s=new State();s.Triggers[1].Accept(7,1,true,0);s.AimReason=2;FireHeld(s,1,0);Check(s.Ammo==100&&s.Gate.Heat==0,"muzzle obstruction cannot spend ammo or heat");
  s.AimReason=1;FireHeld(s,1,.1f);Check(s.Ammo==100,"out of arc cannot fire");
  s.AimReason=0;s.Ammo=0;FireHeld(s,1,.2f);Check(s.Gate.Heat==0&&s.Gate.NextCannon==0,"empty ammo does not commit shot");
  s=new State();s.Triggers[1].Accept(7,1,true,0);s.Ready=false;FireHeld(s,1,0);s.Ready=true;FireHeld(s,1,.1f);Check(s.Shots==0,"storage/death interruption cancels lease until fresh intent");
  s=new State();s.Triggers[1].Accept(7,1,true,0);s.Vehicle.Person.entityId=8;FireHeld(s,1,.1f);Check(s.Shots==0,"new seat occupant never inherits trigger");
  s=new State();s.Triggers[0].Accept(7,1,true,0);FireHeld(s,0,0);FireHeld(s,0,.1f);Check(s.Shots==1&&s.Ammo==99&&s.SalvoRemaining==2,"pilot starts one salvo only");
  s.Triggers[0].Accept(7,2,false,.11f);FireHeld(s,0,.12f);Check(s.SalvoRemaining==0,"pilot release cancels unlaunched salvo rounds");
  Console.WriteLine("PASS: "+checks+" actual FireHeld server cadence, interruption, ammo and heat checks with mocked vehicle/damage.");
 }
}}
'@
$rules=Get-Content "$root/99-AEC_T16_RuntimeFix/Source/ApacheWeaponRules.cs" -Raw
Add-Type -TypeDefinition ($stub+$method+$tests+$rules.Replace('using System;',''))
[AECT16RuntimeFix.FireFixture]::Run()
