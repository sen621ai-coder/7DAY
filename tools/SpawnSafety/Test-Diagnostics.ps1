$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
$source=Get-Content "$root/ZZZ-PZAEC_SpawnSafety/Source/Diagnostics.cs" -Raw
$fixture=@'
namespace UnityEngine {
 public struct Vector3 {public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}public override string ToString()=>"("+x+","+y+","+z+")";}
 public static class Time {public static float realtimeSinceStartup;}
 public static class Mathf {public static int FloorToInt(float x)=>(int)System.Math.Floor(x);}
}
public class World {public bool IsChunkAreaLoaded(UnityEngine.Vector3 p)=>true;public float GetTerrainHeight(int x,int z)=>30;}
public class Entity {public int entityClass=1,entityId=1;public World world;public UnityEngine.Vector3 position;public bool IsDead()=>false;}
public class GameManager {public static GameManager Instance=new GameManager();public World World=new World();}
public static class ModEvents {public struct SGameUpdateData {}}
public static class Log {public static readonly System.Collections.Generic.List<string> Lines=new System.Collections.Generic.List<string>();public static void Out(string line){Lines.Add(line);}}
public static class SpawnDiagnosticsTests {
 static int checks;static void Check(bool b,string message){checks++;if(!b)throw new System.Exception(message);}
 public static string Run(){
  var p=new UnityEngine.Vector3(10,31,20);
  PZAEC.SpawnSafety.Diagnostics.SelectorFailed("event:test",p,16,"perimeter-exhausted");
  PZAEC.SpawnSafety.Diagnostics.SelectorFailed("event:test",p,16,"perimeter-exhausted");
  PZAEC.SpawnSafety.Diagnostics.SelectorFailed("event:test",p,16,"perimeter-exhausted");
  Check(Log.Lines.Count==2&&Log.Lines[1].Contains("count=2"),"first two failures logged");
  var update=new ModEvents.SGameUpdateData();UnityEngine.Time.realtimeSinceStartup=61;
  PZAEC.SpawnSafety.Diagnostics.Update(ref update);
  Check(Log.Lines.Count==3&&Log.Lines[2].Contains("count=3"),"pending total flushed without another spawn");
  PZAEC.SpawnSafety.Diagnostics.ClaimExcluded(p,50);
  Check(Log.Lines.Count==4&&Log.Lines[3].Contains("radius=50m"),"claim reason and configured radius logged");
  GameManager.Instance.World=new World();
  PZAEC.SpawnSafety.Diagnostics.SelectorFailed("event:test",p,16,"perimeter-exhausted");
  Check(Log.Lines.Count==5&&Log.Lines[4].Contains("count=1"),"world change resets counters");
  GameManager.Instance=null;
  PZAEC.SpawnSafety.Diagnostics.DirectFailed("AEC-direct",7,p,"trader-80m","");
  Check(Log.Lines.Count==5,"diagnostics safe before game manager exists");
  return "PASS: "+checks+" diagnostic logging checks.";
 }
}
'@
Add-Type -TypeDefinition ($source+$fixture)
[SpawnDiagnosticsTests]::Run()
