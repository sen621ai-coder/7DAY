using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;

namespace AECT16RuntimeFix
{
    // Observational only: never cancel or rewrite block operations.
    public static class AutoMinerAudit
    {
        private sealed class Recent { public long Next; public int Suppressed; public long Damage; }
        private static readonly Dictionary<Vector3i,Recent> recent = new Dictionary<Vector3i,Recent>();
        private static readonly object gate = new object();
        public static bool Applies(string name)
        {
            return name=="AutoMinerIron"||name=="AutoMinerLead"||name=="AutoMinerCoal"||
                name=="AutoMinerNitrate"||name=="AutoMinerClay"||name=="AutoMinerShale"||name=="AutoMinerBrass";
        }
        public static void Install()
        {
            var h=new Harmony("pzaec.autominer.audit.v1");
            try
            {
                var seen=new HashSet<MethodBase>();
                foreach(string name in new[]{"DamageBlock","OnBlockLoaded","OnBlockUnloaded","OnBlockAdded","OnBlockRemoved","OnBlockDestroyedBy","OnBlockDestroyedByExplosion"})
                {
                    var method=AccessTools.Method(typeof(BlockCollector),name);
                    if(method==null)throw new MissingMethodException("Collector audit: "+name);
                    if(seen.Add(method))h.Patch(method,prefix:new HarmonyMethod(typeof(AutoMinerAudit),nameof(Before)));
                }
                h.Patch(AccessTools.Method(typeof(GameManager),"SaveAndCleanupWorld"),postfix:new HarmonyMethod(typeof(AutoMinerAudit),nameof(Clear)));
                Log.Out("[AutoMiner-Audit] Enabled: seven resource miners; damage aggregation 5s; lifecycle traces. Events describe callbacks, not a proven cause.");
            }
            catch(Exception ex){h.UnpatchSelf();Log.Warning("[AutoMiner-Audit] Disabled: "+ex);}
        }
        public static void Clear(){lock(gate)recent.Clear();}
        public static void Before(MethodBase __originalMethod,object[] __args)
        {
            try
            {
                WorldBase world=null;BlockValue value=default(BlockValue);Vector3i pos=default(Vector3i);
                foreach(object arg in __args)
                {
                    if(arg is WorldBase w)world=w;
                    else if(arg is BlockValue b)value=b;
                    else if(arg is Vector3i p)pos=p;
                    else if(arg is BlockValueRef r)pos=r.BlockPosition;
                }
                if(value.Block==null||!Applies(value.Block.GetBlockName()))return;
                string operation=__originalMethod.Name;
                string detail="";int actor=-1;int amount=0;
                var parameters=__originalMethod.GetParameters();
                for(int i=0;i<parameters.Length;i++)
                {
                    string n=parameters[i].Name;
                    if(n=="_damagePoints")amount=(int)__args[i];
                    if(n=="_entityIdThatDamaged"||n=="_entityId"||n=="_playerThatStartedExpl")actor=(int)__args[i];
                    if(__args[i] is ItemActionAttack.AttackHitInfo hit)detail+=" weaponTags="+hit.WeaponTypeTag;
                }
                lock(gate)
                {
                    if(operation=="DamageBlock")
                    {
                        if(!recent.TryGetValue(pos,out Recent state))
                        {
                            if(recent.Count>=1024)recent.Clear();
                            recent[pos]=state=new Recent();
                        }
                        long now=Stopwatch.GetTimestamp();
                        if(now<state.Next){state.Suppressed++;state.Damage+=amount;return;}
                        detail+=" priorSuppressedHits="+state.Suppressed+" priorRequestedDamage="+state.Damage;
                        state.Next=now+5*Stopwatch.Frequency;state.Suppressed=0;state.Damage=0;
                    }
                    else if(recent.TryGetValue(pos,out Recent state))
                    {
                        detail+=" pendingSuppressedHits="+state.Suppressed+" pendingRequestedDamage="+state.Damage;
                        if(operation=="OnBlockRemoved"||operation=="OnBlockUnloaded")recent.Remove(pos);
                    }
                }
                if(operation=="DamageBlock"||operation=="OnBlockRemoved"||operation.StartsWith("OnBlockDestroyed",StringComparison.Ordinal))
                {
                    var trace=new StackTrace(2,false);var frames=trace.GetFrames();
                    detail+=" callers=";
                    if(frames!=null)for(int i=0;i<Math.Min(10,frames.Length);i++)
                    {var m=frames[i].GetMethod();detail+=(i==0?"":" > ")+m?.DeclaringType?.Name+"."+m?.Name;}
                }
                Log.Out("[AutoMiner-Audit] event="+operation+" side="+(world==null?"unknown":world.IsRemote()?"client":"server")+
                    " block="+value.Block.GetBlockName()+" pos="+pos+" chunk="+(pos.x>>4)+","+(pos.z>>4)+
                    " child="+value.ischild+" rotation="+value.rotation+" damageBefore="+value.damage+
                    " requestedDamage="+amount+" actorId="+actor+detail);
            }
            catch { /* Diagnostics must never interrupt damage, saving or loading. */ }
        }
    }
}
