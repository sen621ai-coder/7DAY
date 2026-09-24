$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
$source=Get-Content "$root/ZZZ-PZAEC_SpawnSafety/Source/Deferred.cs" -Raw
$fixture=@'
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
namespace UnityEngine {
 public struct Vector3 {public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}public float sqrMagnitude=>x*x+y*y+z*z;public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);}
 public struct Bounds {public Vector3 center,size;public Bounds(Vector3 c,Vector3 s){center=c;size=s;}}
 public static class Time {public static float realtimeSinceStartup;}
}
namespace HarmonyLib {
 public static class AccessTools {
  public static MethodInfo Method(Type t,string n)=>t.GetMethod(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
  public static FieldInfo Field(Type t,string n)=>t.GetField(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
 }
}
public class Entity {} public class EntityAlive:Entity {} public class EntityPlayer:EntityAlive {}
public class World {public bool Loaded=true,PlayerNear,EntityNear;public bool IsRemote()=>false;public bool IsChunkAreaLoaded(UnityEngine.Vector3 p)=>Loaded;
 public void GetPlayersAround(UnityEngine.Vector3 p,float r,List<EntityPlayer> players){if(PlayerNear)players.Add(new EntityPlayer());}
 public List<Entity> GetEntitiesInBounds(Type t,UnityEngine.Bounds b,List<Entity> entities){if(EntityNear)entities.Add(new EntityAlive());return entities;}}
public class GameManager {public static GameManager Instance=new GameManager();public World World=new World();}
public static class ModEvents {public struct SGameUpdateData {}}
public static class Log {public static void Error(string s){throw new Exception(s);}}
namespace PZAEC.SpawnSafety {
 public static class SiteRules {public static bool Finite(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x);}
 public class Request {public string Source;public bool Direct,Surface;public UnityEngine.Vector3 Center;public int Remaining;public Func<UnityEngine.Vector3,bool> Allowed;public Func<UnityEngine.Vector3?> Fallback;}
 public static class Safety {public static Request Current;}
 public static class Hooks {public static bool OutsideTraderForRetry(UnityEngine.Vector3 p)=>true;public static bool AllowedCachedPoint(UnityEngine.Vector3 p)=>true;}
 public static class Recovery {public static Func<UnityEngine.Vector3?> Create(UnityEngine.Vector3 p,int min,int max){if(min!=8||max!=96)throw new Exception("retry range");return ()=>p;}}
 public static class Diagnostics {public static readonly List<string> States=new List<string>();public static void Deferred(string source,int id,UnityEngine.Vector3 p,string state,int attempt){States.Add(source+":"+state+":"+attempt);}}
}
public static class FakeAec {
 public static bool _suppressSpawnRateBonus;
 public static bool NextResult,KnownSiteSucceeds;public static int Calls;public static bool SawSuppression;public static string SawSource;public static UnityEngine.Vector3 SawPoint;
 public static bool TrySpawnEntityByClassIdAtPosition(int id,UnityEngine.Vector3 p,ref string description){Calls++;SawSuppression=_suppressSpawnRateBonus;SawSource=PZAEC.SpawnSafety.Safety.Current.Source;SawPoint=p;return NextResult||(KnownSiteSucceeds&&SawSource.Contains(":known-site"));}
}
public static class DeferredTests {
 static int checks;static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
 static void Tick(float time){UnityEngine.Time.realtimeSinceStartup=time;var data=new ModEvents.SGameUpdateData();PZAEC.SpawnSafety.Deferred.Update(ref data);}
 public static string Run(){
  var p=new UnityEngine.Vector3(10,30,20);PZAEC.SpawnSafety.Deferred.Configure(typeof(FakeAec));
  Check(!PZAEC.SpawnSafety.Deferred.Enqueue("skill:TrySpawnEventBoss",7,p,true),"owner counted source excluded");
  Check(PZAEC.SpawnSafety.Deferred.Enqueue("skill:TryApplySpawnRateBonus",7,p,true),"bonus queued");
  Tick(5);Check(FakeAec.Calls==0,"delay observed");
  Tick(6);Check(FakeAec.Calls==1&&FakeAec.SawSuppression&&FakeAec.SawSource.Contains(":deferred"),"same entry replayed with bonus recursion guard");
  Check(!FakeAec._suppressSpawnRateBonus,"guard restored");
  FakeAec.NextResult=true;Tick(17);Check(FakeAec.Calls==1,"retry interval observed");
  Tick(18);Check(FakeAec.Calls==2&&PZAEC.SpawnSafety.Diagnostics.States.Exists(s=>s.Contains(":recovered:2")),"recovery reported after real success");
  FakeAec.NextResult=false;Check(PZAEC.SpawnSafety.Deferred.Enqueue("skill:SpawnHeatmapEscortZombies",8,p,true),"escort queued");
  Tick(24);Tick(36);Tick(54);Check(FakeAec.Calls==5&&PZAEC.SpawnSafety.Diagnostics.States.Exists(s=>s.Contains(":exhausted:3")),"retry budget exhausted");
  Check(!FakeAec.SawSuppression,"escort retains original bonus behavior");
  Check(PZAEC.SpawnSafety.Deferred.Enqueue("skill:TrySpawnReplacementOnKill",9,p,true),"replacement queued");
  GameManager.Instance.World=new World();Tick(100);Check(FakeAec.Calls==5,"world change discards old queue");
  Check(PZAEC.SpawnSafety.Deferred.Enqueue("skill:TrySpawnReplacementOnKill",9,p,true),"transient unload fixture queued");
  GameManager.Instance.World.Loaded=false;Tick(106);Check(FakeAec.Calls==5&&PZAEC.SpawnSafety.Diagnostics.States.Exists(s=>s.Contains(":unloaded-pending:1")),"unloaded center stays pending");
  GameManager.Instance.World.Loaded=true;FakeAec.NextResult=true;Tick(118);Check(FakeAec.Calls==6&&FakeAec.SawSuppression,"loaded center recovers with replacement suppression");
  var good=new UnityEngine.Vector3(35,31,25);PZAEC.SpawnSafety.Deferred.Remember(42,good);
  FakeAec.NextResult=false;FakeAec.KnownSiteSucceeds=true;
  Check(PZAEC.SpawnSafety.Deferred.Enqueue("skill:TrySpawnReplacementOnKill",42,p,true),"known site fallback queued");
  Tick(124);Tick(136);Tick(154);
  Check(FakeAec.Calls==10&&FakeAec.SawSource.Contains(":known-site")&&FakeAec.SawPoint.x==good.x,"exhaustion reuses prior successful point once");
  Check(PZAEC.SpawnSafety.Diagnostics.States.Exists(s=>s.Contains(":known-site-recovered:3")),"known site reported only after success");
  FakeAec.KnownSiteSucceeds=false;GameManager.Instance.World.PlayerNear=true;
  Check(PZAEC.SpawnSafety.Deferred.Enqueue("skill:TrySpawnReplacementOnKill",42,p,true),"occupied known site case queued");
  Tick(160);Tick(172);Tick(190);
  Check(FakeAec.Calls==13&&PZAEC.SpawnSafety.Diagnostics.States.Exists(s=>s.Contains(":known-site-unavailable:3")),"nearby player blocks cached site");
  GameManager.Instance.World.PlayerNear=false;
  GameManager.Instance.World.EntityNear=true;
  Check(PZAEC.SpawnSafety.Deferred.Enqueue("skill:TrySpawnReplacementOnKill",42,p,true),"occupied site case queued");
  Tick(196);Tick(208);Tick(226);
  Check(FakeAec.Calls==16&&!FakeAec.SawSource.Contains(":known-site"),"living entity blocks cached site");
  GameManager.Instance.World.EntityNear=false;
  GameManager.Instance.World=new World();Tick(230);
  Check(PZAEC.SpawnSafety.Deferred.Enqueue("skill:TrySpawnReplacementOnKill",42,p,true),"new world queue available");
  Tick(236);Tick(248);Tick(266);
  Check(FakeAec.Calls==19&&!FakeAec.SawSource.Contains(":known-site"),"previous world successful site is never reused");
  for(int i=0;i<PZAEC.SpawnSafety.Deferred.Capacity;i++)Check(PZAEC.SpawnSafety.Deferred.Enqueue("skill:TrySpawnReplacementOnKill",10+i,p,true),"within capacity");
  Check(!PZAEC.SpawnSafety.Deferred.Enqueue("skill:TrySpawnReplacementOnKill",99,p,true),"queue full refuses extra entry");
  return "PASS: "+checks+" deferred retry checks.";
 }
}
'@
Add-Type -TypeDefinition ($fixture+($source -replace '(?m)^using [^\r\n]+\r?\n',''))
[DeferredTests]::Run()
