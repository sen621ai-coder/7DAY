using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PZAEC.SpawnSafety
{
    // Only independent, void-returning AEC spawns can be replayed without
    // inventing a success result for a wave, follower, boss, or quest owner.
    public static class Deferred
    {
        sealed class Entry
        {public string Source;public int ClassId;public Vector3 Center;public bool Surface;public float Due;public int Attempts;}
        sealed class Site
        {public int ClassId;public Vector3 Position;public float At;}
        public const int Capacity=32,MaxAttempts=3;
        const int SiteCapacity=64,KnownSiteAttempts=4;
        const float KnownSiteRange=96f,KnownSiteAge=600f;
        static readonly List<Entry> Pending=new List<Entry>();
        static readonly List<Site> Known=new List<Site>();
        static World lastWorld;
        static MethodInfo spawn;
        static FieldInfo suppressBonus;
        [ThreadStatic] static bool replaying;

        static void EnsureWorld(World world)
        {
            if(ReferenceEquals(lastWorld,world))return;
            Pending.Clear();Known.Clear();lastWorld=world;
        }

        public static bool Eligible(string source)
        {
            return source=="skill:TryApplySpawnRateBonus"||source=="skill:TrySpawnReplacementOnKill"||
                source=="skill:SpawnHeatmapEscortZombies";
        }
        public static void Configure(Type aec)
        {
            spawn=AccessTools.Method(aec,"TrySpawnEntityByClassIdAtPosition");
            suppressBonus=AccessTools.Field(aec,"_suppressSpawnRateBonus");
            if(spawn==null||suppressBonus==null||suppressBonus.FieldType!=typeof(bool))
                throw new MissingMemberException("AEC deferred direct-spawn boundary");
        }
        // Called only after AEC reports a successful world registration.
        public static void Remember(int classId,Vector3 position)
        {
            var world=GameManager.Instance==null?null:GameManager.Instance.World;
            if(world==null||world.IsRemote()||classId<=0||
                !SiteRules.Finite(position.x)||!SiteRules.Finite(position.y)||!SiteRules.Finite(position.z)||
                !world.IsChunkAreaLoaded(position))return;
            EnsureWorld(world);
            for(int i=Known.Count-1;i>=0;i--)
            {
                var p=Known[i].Position;
                if((p-position).sqrMagnitude<4f)Known.RemoveAt(i);
            }
            if(Known.Count>=SiteCapacity)Known.RemoveAt(0);
            Known.Add(new Site{ClassId=classId,Position=position,At=Time.realtimeSinceStartup});
        }
        public static bool Enqueue(string source,int classId,Vector3 center,bool surface)
        {
            if(replaying||!Eligible(source)||spawn==null||classId<=0||
                !SiteRules.Finite(center.x)||!SiteRules.Finite(center.y)||!SiteRules.Finite(center.z))return false;
            var world=GameManager.Instance==null?null:GameManager.Instance.World;
            if(world==null||world.IsRemote())return false;
            EnsureWorld(world);
            if(Pending.Count>=Capacity)
            {Diagnostics.Deferred(source,classId,center,"queue-full",0);return false;}
            Pending.Add(new Entry{Source=source,ClassId=classId,Center=center,Surface=surface,Due=Time.realtimeSinceStartup+6});
            Diagnostics.Deferred(source,classId,center,"queued",0);
            return true;
        }
        public static void Update(ref ModEvents.SGameUpdateData data)
        {
            var world=GameManager.Instance==null?null:GameManager.Instance.World;
            EnsureWorld(world);
            if(world==null||world.IsRemote()||spawn==null||Pending.Count==0)return;
            float now=Time.realtimeSinceStartup;int budget=2;
            for(int i=Pending.Count-1;i>=0&&budget>0;i--)
            {
                var entry=Pending[i];if(now<entry.Due)continue;
                budget--;Pending.RemoveAt(i);
                entry.Attempts++;
                if(!world.IsChunkAreaLoaded(entry.Center))
                {
                    if(entry.Attempts<MaxAttempts)
                    {
                        entry.Due=now+(entry.Attempts==1?12:18);
                        Pending.Add(entry);
                        Diagnostics.Deferred(entry.Source,entry.ClassId,entry.Center,"unloaded-pending",entry.Attempts);
                    }
                    else if(!TryKnownSite(entry,world))
                        Diagnostics.Deferred(entry.Source,entry.ClassId,entry.Center,"unloaded-exhausted",entry.Attempts);
                    continue;
                }
                bool success=TrySpawn(entry,entry.Center,false);
                if(success)Diagnostics.Deferred(entry.Source,entry.ClassId,entry.Center,"recovered",entry.Attempts);
                else if(entry.Attempts<MaxAttempts)
                {
                    entry.Due=now+(entry.Attempts==1?12:18);
                    Pending.Add(entry);
                    Diagnostics.Deferred(entry.Source,entry.ClassId,entry.Center,"retry-pending",entry.Attempts);
                }
                else if(!TryKnownSite(entry,world))
                    Diagnostics.Deferred(entry.Source,entry.ClassId,entry.Center,"exhausted",entry.Attempts);
            }
        }
        static bool TrySpawn(Entry entry,Vector3 position,bool knownSite)
        {
            bool success=false,oldSuppress=false,changedSuppress=false;
            var previous=Safety.Current;
            try
            {
                replaying=true;
                if(entry.Source!="skill:SpawnHeatmapEscortZombies")
                {
                    oldSuppress=(bool)suppressBonus.GetValue(null);
                    suppressBonus.SetValue(null,true);changedSuppress=true;
                }
                Safety.Current=new Request{Source=entry.Source+(knownSite?":known-site":":deferred"),Direct=true,Surface=entry.Surface,
                    Center=entry.Center,Remaining=1,Allowed=knownSite?(Func<Vector3,bool>)Hooks.AllowedCachedPoint:Hooks.OutsideTraderForRetry,
                    Fallback=knownSite?null:Recovery.Create(entry.Center,8,96)};
                object[] args={entry.ClassId,position,""};
                success=(bool)spawn.Invoke(null,args);
            }
            catch(Exception ex)
            {Log.Error("[SpawnSafety] deferred retry error: "+entry.Source+" class="+entry.ClassId+" knownSite="+knownSite+" "+ex);}
            finally
            {
                Safety.Current=previous;
                try
                {
                    if(changedSuppress)suppressBonus.SetValue(null,oldSuppress);
                }
                finally{replaying=false;}
            }
            return success;
        }
        static bool TryKnownSite(Entry entry,World world)
        {
            if(!entry.Surface||Known.Count==0)
            {Diagnostics.Deferred(entry.Source,entry.ClassId,entry.Center,"known-site-unavailable",entry.Attempts);return false;}
            var snapshot=Known.ToArray();float now=Time.realtimeSinceStartup;int attempts=0;
            for(int matching=0;matching<2&&attempts<KnownSiteAttempts;matching++)
            for(int i=snapshot.Length-1;i>=0;i--)
            {
                var site=snapshot[i];if((site.ClassId==entry.ClassId)!=(matching==0))continue;
                var point=site.Position;float dx=point.x-entry.Center.x,dz=point.z-entry.Center.z;
                if(now-site.At<0||now-site.At>KnownSiteAge||dx*dx+dz*dz>KnownSiteRange*KnownSiteRange||
                    !world.IsChunkAreaLoaded(point)||!Hooks.AllowedCachedPoint(point))continue;
                var players=new List<EntityPlayer>();world.GetPlayersAround(point,12f,players);
                if(players.Count>0)continue;
                var nearby=world.GetEntitiesInBounds(typeof(EntityAlive),
                    new Bounds(point+new Vector3(0,1.1f,0),new Vector3(1.5f,2.2f,1.5f)),new List<Entity>());
                if(nearby!=null&&nearby.Count>0)continue;
                attempts++;
                if(TrySpawn(entry,point,true))
                {Diagnostics.Deferred(entry.Source,entry.ClassId,point,"known-site-recovered",entry.Attempts);return true;}
                if(attempts>=KnownSiteAttempts)break;
            }
            Diagnostics.Deferred(entry.Source,entry.ClassId,entry.Center,
                attempts==0?"known-site-unavailable":"known-site-rejected",entry.Attempts);
            return false;
        }
    }
}
