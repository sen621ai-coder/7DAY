using System;
using System.Collections.Generic;
using UnityEngine;

namespace PZAEC.SpawnSafety
{
    public static class Diagnostics
    {
        sealed class Watch
        {public World World;public Entity Entity;public string Source;public Vector3 Start;public float Due;public int Step;public bool Surface;}
        sealed class Counter{public int Total;public float Next;}
        static readonly List<Watch> Pending=new List<Watch>();
        static readonly Dictionary<string,Counter> Counts=new Dictionary<string,Counter>();
        static World lastWorld;
        static void Report(string key,string detail)
        {
            Counter c;if(!Counts.TryGetValue(key,out c))Counts[key]=c=new Counter();c.Total++;
            float now=Time.realtimeSinceStartup;
            if(c.Total<=2||now>=c.Next){Log.Out("[SpawnSafety] "+key+" count="+c.Total+" "+detail);c.Next=now+60;}
        }
        public static void Accepted(World world,Entity e,string source,Vector3 original,Vector3 chosen,int attempt,bool surface)
        {
            if(lastWorld!=world){Pending.Clear();Counts.Clear();lastWorld=world;}
            if(attempt>0)Report(source+" relocated","class="+e.entityClass+" from="+original+" to="+chosen+" attempt="+(attempt+1));
            else Report(source+" accepted","class="+e.entityClass+" at="+chosen);
            if(Pending.Count<128)Pending.Add(new Watch{World=world,Entity=e,Source=source,Start=chosen,Due=Time.realtimeSinceStartup+1,Surface=surface});
        }
        public static void Rejected(string source,int id,Vector3 original,string reason)
        {Report(source+" rejected:"+reason,"class="+id+" at="+original+" (before world registration)");}
        public static void Update(ref ModEvents.SGameUpdateData data)
        {
            var world=GameManager.Instance==null?null:GameManager.Instance.World;
            if(world==null||world!=lastWorld){Pending.Clear();Counts.Clear();lastWorld=world;return;}
            float now=Time.realtimeSinceStartup;int budget=8;
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
