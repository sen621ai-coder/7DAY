using System;
using System.Collections.Generic;
using UnityEngine;

namespace PZAEC.SpawnSafety
{
    public static class Diagnostics
    {
        sealed class Watch
        {public World World;public Entity Entity;public string Source;public Vector3 Start;public float Due;public int Step;public bool Surface;}
        sealed class Counter{public int Total,LastLogged;public float Next;public string Detail;}
        static readonly List<Watch> Pending=new List<Watch>();
        static readonly Dictionary<string,Counter> Counts=new Dictionary<string,Counter>();
        static World lastWorld;
        static float nextSummaryCheck;
        static void EnsureWorld(World world)
        {
            if(ReferenceEquals(lastWorld,world))return;
            Pending.Clear();Counts.Clear();lastWorld=world;nextSummaryCheck=0;
        }
        static void Report(string key,string detail)
        {
            var manager=GameManager.Instance;
            if(manager==null||manager.World==null)return;
            EnsureWorld(manager.World);
            Counter c;if(!Counts.TryGetValue(key,out c))Counts[key]=c=new Counter();c.Total++;
            c.Detail=detail;
            float now=Time.realtimeSinceStartup;
            if(c.Total<=2||now>=c.Next){Log.Out("[SpawnSafety] "+key+" count="+c.Total+" "+detail);c.LastLogged=c.Total;c.Next=now+60;}
        }
        public static void Accepted(World world,Entity e,string source,Vector3 original,Vector3 chosen,int attempt,bool surface)
        {
            EnsureWorld(world);
            if(attempt>0)Report(source+" relocated","class="+e.entityClass+" from="+original+" to="+chosen+" attempt="+(attempt+1));
            else Report(source+" accepted","class="+e.entityClass+" at="+chosen);
            if(Pending.Count<128)Pending.Add(new Watch{World=world,Entity=e,Source=source,Start=chosen,Due=Time.realtimeSinceStartup+1,Surface=surface});
        }
        public static void Rejected(string source,int id,Vector3 original,string reason)
        {Report(source+" rejected:"+reason,"class="+id+" at="+original+" (before world registration)");}
        public static void DirectFailed(string source,int id,Vector3 point,string reason,string detail)
        {Report(source+" failed:"+reason,"class="+id+" at="+point+" detail="+(string.IsNullOrEmpty(detail)?"none":detail)+" (before world registration)");}
        public static void FollowerFailed(string className,Vector3 center,string reason)
        {Report("AEC-follower failed","class="+className+" leader="+center+" reason="+(string.IsNullOrEmpty(reason)?"unknown":reason));}
        public static void SelectorFailed(string source,Vector3 center,int attempts,string reason)
        {Report(source+" selector-failed","center="+center+" perimeterAttempts="+attempts+" reason="+reason+" (no entity created)");}
        public static void SelectorRecovered(string source,Vector3 center,Vector3 chosen,int attempts)
        {Report(source+" selector-recovered","center="+center+" chosen="+chosen+" perimeterAttempts="+attempts);}
        public static void ClaimExcluded(Vector3 point,float radius)
        {Report("AEC claim-check hit","at="+point+" radius="+(radius>0?radius+"m":"unknown"));}
        public static void FactoryNull(string source)
        {Report(source+" factory-null","no entity created");}
        public static void Update(ref ModEvents.SGameUpdateData data)
        {
            var world=GameManager.Instance==null?null:GameManager.Instance.World;
            if(!ReferenceEquals(world,lastWorld)){EnsureWorld(world);return;}
            if(world==null)return;
            float now=Time.realtimeSinceStartup;int budget=8;
            if(now>=nextSummaryCheck)
            {
                nextSummaryCheck=now+1;
                foreach(var entry in Counts)
                {
                    var c=entry.Value;
                    if(c.Total>c.LastLogged&&now>=c.Next)
                    {Log.Out("[SpawnSafety] "+entry.Key+" count="+c.Total+" "+c.Detail);c.LastLogged=c.Total;c.Next=now+60;}
                }
            }
            for(int i=Pending.Count-1;i>=0&&budget>0;i--)
            {
                var w=Pending[i];if(now<w.Due)continue;budget--;
                var e=w.Entity;
                if(e==null||e.world!=world||e.IsDead()){Pending.RemoveAt(i);continue;}
                // Observe a bounded sample; never move, kill or remove a live spawned enemy.
                if(world.IsChunkAreaLoaded(e.position))
                {
                    bool below=w.Surface&&e.position.y+.5f<world.GetTerrainHeight(Mathf.FloorToInt(e.position.x),Mathf.FloorToInt(e.position.z));
                    if(below||e.position.y<w.Start.y-3.5f)
                        Report(w.Source+" post-spawn-fall","entity="+e.entityId+" start="+w.Start+" now="+e.position+" sample="+(w.Step==0?1:3)+"s");
                }
                if(w.Step++==0)w.Due=now+2;else Pending.RemoveAt(i);
            }
        }
    }
}
