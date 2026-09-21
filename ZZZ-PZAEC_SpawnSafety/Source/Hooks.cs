using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace PZAEC.SpawnSafety
{
    public static class Hooks
    {
        public sealed class ScopeState {public Request Previous;}
        public sealed class DirectState {public Request Previous,Active;}
        [ThreadStatic] static Request activeDirect;
        public static MethodInfo FollowerPosition,NearTrader,BloodPosition,EventPosition;
        static World World {get{return GameManager.Instance==null?null:GameManager.Instance.World;}}
        static readonly System.Random Random=new System.Random();
        static Vector3 Offset(Vector3 p,float range)
        {p.x+=(float)(Random.NextDouble()*2-1)*range;p.z+=(float)(Random.NextDouble()*2-1)*range;return p;}
        static Vector3? Ring(Vector3 p,float min,float max,bool terrain)
        {
            double angle=Random.NextDouble()*Math.PI*2,distance=min+Random.NextDouble()*(max-min);
            p.x+=(float)(Math.Cos(angle)*distance);p.z+=(float)(Math.Sin(angle)*distance);
            var world=World;if(world==null||!world.IsChunkAreaLoaded(p))return null;
            p.y=terrain?world.GetTerrainHeight(Mathf.FloorToInt(p.x),Mathf.FloorToInt(p.z))+1:p.y+.1f;return p;
        }
        static bool OutsideTrader(Vector3 p){return !(bool)NearTrader.Invoke(null,new object[]{p,80f});}
        public static Exception Restore(Exception __exception,ScopeState __state)
        {if(__state!=null)Safety.Current=__state.Previous;return __exception;}
        public static void FollowerPrefix(Vector3 __1,int __2,out ScopeState __state)
        {
            __state=new ScopeState{Previous=Safety.Current};var center=__1;int index=__2,attempt=0;
            Safety.Current=new Request{Source="AEC-follower",Direct=true,Surface=Safety.IsSurface(World,center),Center=center,
                Next=()=> (Vector3)FollowerPosition.Invoke(null,new object[]{center,index,(attempt++)%4}),Allowed=OutsideTrader,Fallback=Recovery.Create(center)};
        }
        public static bool DirectPrefix(Vector3 __1,ref string __2,ref bool __result,out DirectState __state)
        {
            __state=new DirectState{Previous=Safety.Current,Active=activeDirect};
            // An event/blood-moon registration can synchronously trigger an unrelated
            // AEC spawn. Its selector and attempt budget must not leak into that spawn.
            if(Safety.Current!=null&&(!Safety.Current.Direct||ReferenceEquals(Safety.Current,activeDirect)))Safety.Current=null;
            if(Safety.Current!=null&&Safety.Current.PerEntity)
            {
                var template=Safety.Current;
                Safety.Current=new Request{Source=template.Source,Direct=true,Surface=template.Surface,Center=template.Center,
                    Next=template.Next,Allowed=template.Allowed,Remaining=template.Next==null?1:12,Fallback=Recovery.Create(__1)};
            }
            if(Safety.Current==null)
            {
                Safety.Current=new Request{Source="AEC-direct",Direct=true,Surface=true,Center=__1,Remaining=1,Allowed=OutsideTrader,Fallback=Recovery.Create(__1)};
            }
            activeDirect=Safety.Current;
            if(!Safety.Current.Bypass && Safety.Current.Remaining<=0){__2="spawn-safety-budget-exhausted";__result=false;return false;}
            return true;
        }
        public static Exception RestoreDirect(Exception __exception,DirectState __state)
        {
            if(__state!=null){Safety.Current=__state.Previous;activeDirect=__state.Active;}
            return __exception;
        }
        public static void FollowerPostfix(bool __result,ref Vector3 __3,ScopeState __state)
        {if(__state!=null&&__result&&Safety.Current!=null&&Safety.Current.LastAccepted.HasValue)__3=Safety.Current.LastAccepted.Value;}
        public static bool AcceptDirect(object value,ref Vector3 position,ref string description)
        {
            var entity=value as Entity;
            if(entity==null)return false;
            if(!Safety.Accept(entity,Safety.Current)){description="spawn-safety-no-valid-site";return false;}
            position=entity.position;return true;
        }
        public static void BloodPrefix(object __instance,World __0,Vector3 __2,Vector3 __3,out ScopeState __state)
        {
            __state=new ScopeState{Previous=Safety.Current};var world=__0;var center=__2;var radius=__3;var instance=__instance;
            // CalcSpawnPos already applies native distance/protection/water policy.
            // CanMobsSpawnAtPos would re-floor its adjusted Y and forces water checks on.
            Safety.Current=new Request{Source="blood-moon",Surface=true,Center=center,RecoverSelector=true,
                Next=()=>{object[] args={world,center,radius,Vector3.zero};return (bool)BloodPosition.Invoke(instance,args)?(Vector3?)args[3]:null;},
                Fallback=Recovery.Create(center,Math.Max(30,(int)radius.magnitude-10),Math.Max(64,(int)radius.magnitude+32),30,false)};
        }
        public static void BloodPositionPostfix(ref Vector3 __3,ref bool __result)
        {if(!__result&&Safety.Current!=null&&Safety.Current.RecoverSelector&&Safety.Current.Source=="blood-moon")__result=Recovery.BeforeFactory(Safety.Current,ref __3);}
        public static void EventPositionPostfix(ref Vector3 __0,ref bool __result)
        {
            if(!__result&&Safety.Current!=null&&Safety.Current.RecoverSelector&&Safety.Current.Source!=null&&Safety.Current.Source.StartsWith("event:",StringComparison.Ordinal))
                __result=Recovery.BeforeFactory(Safety.Current,ref __0);
        }
        public static void EventPrefix(GameEvent.SequenceActions.ActionBaseSpawn __instance,int __0,Vector3 __2,float __3,float __4,bool __5,float __6,bool ___airSpawn,float ___raycastOffset,out ScopeState __state)
        {
            __state=new ScopeState{Previous=Safety.Current};
            var name=__instance.Owner==null?"":__instance.Owner.Name;
            // A vanilla sleeper/POI event is deliberately outside this module's scope.
            EntityClass definition;
            bool groundEnemy=EntityClass.list.TryGetValue(__0,out definition)&&definition.bIsEnemyEntity&&
                (definition.classname==null||!typeof(EntityFlying).IsAssignableFrom(definition.classname));
            if(___airSpawn||!groundEnemy||string.IsNullOrEmpty(name)||!(name.StartsWith("PZAEC",StringComparison.Ordinal)||name.StartsWith("eventPZAEC",StringComparison.Ordinal)))
            {Safety.Current=new Request{Bypass=true};return;}
            var center=__2;var min=__3;var max=__4;var safe=__5;var offset=__6;var ray=___raycastOffset;
            Safety.Current=new Request{Source="event:"+name,Surface=Safety.IsSurface(World,center),Center=center,Min=min,Max=max,RecoverSelector=true,
                Next=()=>{object[] args={Vector3.zero,center,min,max,safe,offset,false,ray};return (bool)EventPosition.Invoke(null,args)?(Vector3?)((Vector3)args[0]+Vector3.up*.5f):null;},
                Fallback=Recovery.Create(center,Math.Max(8,(int)min),Math.Max((int)min+1,Math.Min(96,Math.Max(64,(int)max+16))),12,!safe,!safe)};
        }
        public static void MegaPrefix(World __0,Vector3 __1,out ScopeState __state)
        {
            __state=new ScopeState{Previous=Safety.Current};var world=__0;var center=__1;
            Safety.Current=new Request{Source="skill:MegaHorde",Surface=true,Center=center,
                Next=()=>{var p=Offset(center,12);if(!world.IsChunkAreaLoaded(p))return null;p.y=world.GetHeightAt(p.x,p.z)+2;return (Vector3?)p;}};
        }
        public static Entity FilterMega(Entity entity)
        {
            // The native loop has a null branch before its successful-spawn counter.
            if(Safety.Current!=null)Safety.Current.Remaining=12;
            if(Safety.Current!=null&&entity!=null)Safety.Current.Fallback=Recovery.Create(entity.position);
            return Safety.Accept(entity,Safety.Current)?entity:null;
        }
        public static void SourcePrefix(object[] __args,MethodBase __originalMethod,out ScopeState __state)
        {
            __state=new ScopeState{Previous=Safety.Current};
            var anchor=__args.OfType<EntityAlive>().FirstOrDefault();
            // A captured kill/spawn position takes precedence over a moved entity.
            Vector3? center=__args.OfType<Vector3>().Select(p=>(Vector3?)p).FirstOrDefault();
            if(!center.HasValue&&anchor!=null)center=anchor.position;
            // Preserve the first choice and cooldown semantics; DirectPrefix supplies
            // a fresh bounded recovery search for each actual spawn in this batch.
            var request=new Request{Source="skill:"+__originalMethod.Name,Direct=true,Surface=!center.HasValue||Safety.IsSurface(World,center.Value),PerEntity=true,Allowed=OutsideTrader};
            if(center.HasValue)
            {
                var p=center.Value;request.Center=p;
                // These ranges and height rules match the installed AEC selector IL.
                if(__originalMethod.Name=="TrySpawnReplacementOnKill")
                {request.Surface=true;request.Next=()=>Ring(p,8,24,true);}
                else if(__originalMethod.Name=="SpawnHeatmapEscortZombies"||__originalMethod.DeclaringType.Name=="GhostMinionOnKillRuntime")
                {request.Surface=true;request.Next=()=>Ring(p,5,13,true);}
                else if(__originalMethod.Name=="TryApplySpawnRateBonus")request.Next=()=>Ring(p,1.5f,3.5f,false);
            }
            Safety.Current=request;
        }
        public static void ConsolePrefix(out ScopeState __state)
        {__state=new ScopeState{Previous=Safety.Current};Safety.Current=new Request{Direct=true,Bypass=true};}
        public static bool AcceptCurrent(Entity entity){return Safety.Accept(entity,Safety.Current);}

        public static IEnumerable<CodeInstruction> DirectTranspiler(IEnumerable<CodeInstruction> instructions,ILGenerator generator)
        {
            var codes=instructions.ToList();int index=-1;
            for(int i=0;i+1<codes.Count;i++)
                if(codes[i].opcode==OpCodes.Ldloc_0&&codes[i+1].Calls(AccessTools.Method(typeof(object),"GetType")))
                {if(index>=0)throw new InvalidOperationException("Ambiguous AEC creation boundary");index=i;}
            if(index<0)throw new InvalidOperationException("AEC creation boundary not found");
            var next=generator.DefineLabel();
            var first=new CodeInstruction(OpCodes.Ldloc_3);first.labels.AddRange(codes[index].labels);codes[index].labels.Clear();
            codes[index].labels.Add(next);
            codes.InsertRange(index,new[]{first,new CodeInstruction(OpCodes.Ldarga_S,(byte)1),new CodeInstruction(OpCodes.Ldarg_2),
                new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(Hooks),nameof(AcceptDirect))),new CodeInstruction(OpCodes.Brtrue,next),
                new CodeInstruction(OpCodes.Ldc_I4_0),new CodeInstruction(OpCodes.Ret)});
            return codes;
        }
        public static IEnumerable<CodeInstruction> FactoryTranspiler(IEnumerable<CodeInstruction> instructions,ILGenerator generator,MethodBase __originalMethod)
        {
            var codes=instructions.ToList();int count=0;
            for(int i=codes.Count-1;i>=0;i--)
            {
                var call=codes[i].operand as MethodInfo;
                if(call==null||call.DeclaringType!=typeof(EntityFactory)||call.Name!="CreateEntity")continue;
                count++;
                if(__originalMethod.Name=="SpawnVanillaPack")
                {codes.Insert(i+1,new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(Hooks),nameof(FilterMega))));continue;}
                var next=generator.DefineLabel();codes[i+1].labels.Add(next);
                bool returnsBool=((MethodInfo)__originalMethod).ReturnType==typeof(bool);
                codes.InsertRange(i+1,new[]{new CodeInstruction(OpCodes.Dup),new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(Hooks),nameof(AcceptCurrent))),
                    new CodeInstruction(OpCodes.Brtrue,next),new CodeInstruction(OpCodes.Pop),new CodeInstruction(returnsBool?OpCodes.Ldc_I4_0:OpCodes.Ldnull),new CodeInstruction(OpCodes.Ret)});
            }
            if(count!=1)throw new InvalidOperationException("Expected one creation boundary: "+__originalMethod);
            return codes;
        }
    }
    public sealed class ModApi:IModApi
    {
        static HarmonyMethod Method(string name){return new HarmonyMethod(typeof(Hooks),name);}
        static void Patch(Harmony h,MethodInfo target,string prefix,string transpiler=null,string finalizer=nameof(Hooks.Restore))
        {
            if(target==null)throw new MissingMethodException(prefix);
            h.Patch(target,prefix:Method(prefix),transpiler:transpiler==null?null:Method(transpiler),finalizer:Method(finalizer));
            Log.Out("[SpawnSafety] hook ready: "+target.DeclaringType.FullName+"."+target.Name);
        }
        public void InitMod(Mod mod)
        {
            var h=new Harmony("pzaec.spawn.safety");
            try
            {
                var aec=AccessTools.TypeByName("AeclipseCustomZombieSpawner.SpawnDebugPatcher");
                Hooks.FollowerPosition=AccessTools.Method(aec,"GetFollowerSpawnPositionNearLeader");
                Hooks.NearTrader=AccessTools.Method(aec,"IsNearTrader");
                if(Hooks.FollowerPosition==null||Hooks.NearTrader==null)throw new MissingMethodException("AEC selector");
                Patch(h,AccessTools.Method(aec,"TrySpawnFollowerNearLeader"),nameof(Hooks.FollowerPrefix));
                h.Patch(AccessTools.Method(aec,"TrySpawnFollowerNearLeader"),postfix:Method(nameof(Hooks.FollowerPostfix)));
                Patch(h,AccessTools.Method(aec,"TrySpawnEntityByClassIdAtPosition"),nameof(Hooks.DirectPrefix),nameof(Hooks.DirectTranspiler),nameof(Hooks.RestoreDirect));
                var blood=typeof(AIDirectorBloodMoonParty);
                Hooks.BloodPosition=AccessTools.Method(blood,"CalcSpawnPos");
                if(Hooks.BloodPosition==null)throw new MissingMethodException("CalcSpawnPos");
                h.Patch(Hooks.BloodPosition,postfix:Method(nameof(Hooks.BloodPositionPostfix)));
                Patch(h,AccessTools.Method(blood,"SpawnZombie"),nameof(Hooks.BloodPrefix),nameof(Hooks.FactoryTranspiler));
                var events=typeof(GameEvent.SequenceActions.ActionBaseSpawn);
                Hooks.EventPosition=AccessTools.Method(events,"FindValidPosition",new[]{typeof(Vector3).MakeByRefType(),typeof(Vector3),typeof(float),typeof(float),typeof(bool),typeof(float),typeof(bool),typeof(float)});
                if(Hooks.EventPosition==null)throw new MissingMethodException("FindValidPosition");
                h.Patch(Hooks.EventPosition,postfix:Method(nameof(Hooks.EventPositionPostfix)));
                Patch(h,AccessTools.Method(events,"SpawnEntity"),nameof(Hooks.EventPrefix),nameof(Hooks.FactoryTranspiler));
                Patch(h,AccessTools.Method(AccessTools.TypeByName("AeclipseCustomZombieAI01.MegaHordeRuntime"),"SpawnVanillaPack"),nameof(Hooks.MegaPrefix),nameof(Hooks.FactoryTranspiler));
                foreach(var name in new[]{"TrySpawnEventBoss","TrySpawnEventBossForHeatmap","SpawnHeatmapEscortZombies","TrySpawnReplacementOnKill","TryApplySpawnRateBonus"})
                    Patch(h,AccessTools.Method(aec,name),nameof(Hooks.SourcePrefix));
                foreach(var typeName in new[]{"AeclipseCustomZombieAI05.BossTierDropRuntime","AeclipseCustomZombieAI06.GhostMinionOnKillRuntime","AeclipseCustomZombieNemesis.NemesisManager"})
                {
                    var type=AccessTools.TypeByName(typeName);if(type==null)continue;
                    foreach(var method in AccessTools.GetDeclaredMethods(type).Where(m=>new[]{"OnGameUpdate","OnZombieKilled","TrySpawnWarAlly","TrySpawnNemesis","TryKinVengeanceSpawn","TrySpawnFallbackSameTier"}.Contains(m.Name)))
                        Patch(h,method,nameof(Hooks.SourcePrefix));
                }
                var console=AccessTools.TypeByName("AeclipseCustomZombieSpawner.ConsoleCmdAec");
                var cmd=AccessTools.Method(console,"CmdSpawn");if(cmd!=null)Patch(h,cmd,nameof(Hooks.ConsolePrefix));
                ModEvents.GameUpdate.RegisterHandler(Diagnostics.Update);
                Log.Out("[SpawnSafety] 1.1.0 loaded: bounded perimeter recovery for followers, blood moon and custom events/skills.");
            }
            catch(Exception ex)
            {h.UnpatchSelf();Log.Error("[SpawnSafety] NOT ACTIVE: hooks incompatible; rolled back all safety hooks. "+ex);}
        }
    }
}

