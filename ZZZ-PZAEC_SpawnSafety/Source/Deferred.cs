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
        public const int Capacity=32,MaxAttempts=3;
        static readonly List<Entry> Pending=new List<Entry>();
        static World lastWorld;
        static MethodInfo spawn;
        static FieldInfo suppressBonus;
        [ThreadStatic] static bool replaying;

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
        public static bool Enqueue(string source,int classId,Vector3 center,bool surface)
        {
            if(replaying||!Eligible(source)||spawn==null||classId<=0||
                !SiteRules.Finite(center.x)||!SiteRules.Finite(center.y)||!SiteRules.Finite(center.z))return false;
            var world=GameManager.Instance==null?null:GameManager.Instance.World;
            if(world==null||world.IsRemote())return false;
            if(!ReferenceEquals(lastWorld,world)){Pending.Clear();lastWorld=world;}
            if(Pending.Count>=Capacity)
            {Diagnostics.Deferred(source,classId,center,"queue-full",0);return false;}
            Pending.Add(new Entry{Source=source,ClassId=classId,Center=center,Surface=surface,Due=Time.realtimeSinceStartup+6});
            Diagnostics.Deferred(source,classId,center,"queued",0);
            return true;
        }
        public static void Update(ref ModEvents.SGameUpdateData data)
        {
            var world=GameManager.Instance==null?null:GameManager.Instance.World;
            if(!ReferenceEquals(lastWorld,world)){Pending.Clear();lastWorld=world;}
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
                    else Diagnostics.Deferred(entry.Source,entry.ClassId,entry.Center,"unloaded-exhausted",entry.Attempts);
                    continue;
                }
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
                    Safety.Current=new Request{Source=entry.Source+":deferred",Direct=true,Surface=entry.Surface,
                        Center=entry.Center,Remaining=1,Allowed=Hooks.OutsideTraderForRetry,
                        Fallback=Recovery.Create(entry.Center,8,96)};
                    object[] args={entry.ClassId,entry.Center,""};
                    success=(bool)spawn.Invoke(null,args);
                }
                catch(Exception ex)
                {Log.Error("[SpawnSafety] deferred retry error: "+entry.Source+" class="+entry.ClassId+" "+ex);}
                finally
                {
                    Safety.Current=previous;
                    if(changedSuppress)suppressBonus.SetValue(null,oldSuppress);
                    replaying=false;
                }
                if(success)Diagnostics.Deferred(entry.Source,entry.ClassId,entry.Center,"recovered",entry.Attempts);
                else if(entry.Attempts<MaxAttempts)
                {
                    entry.Due=now+(entry.Attempts==1?12:18);
                    Pending.Add(entry);
                    Diagnostics.Deferred(entry.Source,entry.ClassId,entry.Center,"retry-pending",entry.Attempts);
                }
                else Diagnostics.Deferred(entry.Source,entry.ClassId,entry.Center,"exhausted",entry.Attempts);
            }
        }
    }
}
