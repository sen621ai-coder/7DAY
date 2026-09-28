#Requires -Version 7.0
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$source=Get-Content -Raw (Join-Path $root '97-AutomationWorkshop/Source/CargoDroneNavigationScheduler.cs')
$stub=@"
namespace UnityEngine { public static class Time { public static int frameCount; } }
namespace YFAutomation.CargoDrones {
public static class NavigationSchedulerTest {
 public static string Run() {
  CargoNavigationScheduler.Clear();var jobs=new object[]{new object(),new object(),new object(),new object()};var counts=new int[4];
  for(int frame=1;frame<=100;frame++) { UnityEngine.Time.frameCount=frame;int advanced=0;
   for(int i=0;i<4;i++)if(CargoNavigationScheduler.Begin(jobs[i])){counts[i]++;advanced++;CargoNavigationScheduler.End(.8,4);}
   if(advanced>2)throw new System.Exception("More than two concurrent slices");
  }
  for(int i=0;i<4;i++)if(counts[i]<48)throw new System.Exception("Starvation: "+counts[i]);
  foreach(var j in jobs)CargoNavigationScheduler.Remove(j);
  UnityEngine.Time.frameCount++;var a=new object();if(!CargoNavigationScheduler.Begin(a))throw new System.Exception("Stale search retains slot");CargoNavigationScheduler.End(2.1,1);
  if(CargoNavigationScheduler.Begin(new object()))throw new System.Exception("Global CPU limit ignored");
  CargoNavigationScheduler.Clear();UnityEngine.Time.frameCount++;
  if(!CargoNavigationScheduler.Begin(a))throw new System.Exception("New frame admission failed");CargoNavigationScheduler.End(.1,8);
  if(CargoNavigationScheduler.Begin(new object()))throw new System.Exception("Global probe limit ignored");
  CargoNavigationScheduler.Clear();return "PASS scheduler: four-flight fairness, cancellation, two-search/eight-probe/2ms global limits";
 }
}}
"@
Add-Type -TypeDefinition ($source+"`n"+$stub)
[YFAutomation.CargoDrones.NavigationSchedulerTest]::Run()
