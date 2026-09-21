using System;
using System.Runtime.CompilerServices;
using GamePath;
using HarmonyLib;
using UnityEngine;

namespace PZAEC.WallStudBarrier
{
    public static class Recovery
    {
        private sealed class State
        {
            public Vector3 Position;
            public float Checked,StillSince,NextRetry,NoDestroyUntil;
            public int Attempts;
        }
        private static readonly ConditionalWeakTable<EntityAlive,State> States=new ConditionalWeakTable<EntityAlive,State>();
        public static bool CoolingDown(EntityAlive entity)
        {
            State state;
            return entity!=null && States.TryGetValue(entity,out state) && Time.time<state.NoDestroyUntil;
        }
        public static void DestroyPostfix(EntityAlive ___theEntity,ref bool __result)
        {if(__result && CoolingDown(___theEntity)) __result=false;}
        public static void UpdatePostfix(EntityAlive __instance)
        {
            var entity=__instance;
            if(!Barrier.Enemy(entity)||entity.world==null||entity.world.IsRemote()||entity.IsDead()||entity.navigator==null||entity.moveHelper==null)return;
            float now=Time.time;
            var state=States.GetValue(entity,e=>new State{Position=e.position,Checked=now,StillSince=now});
            if(now-state.Checked<1)return;
            state.Checked=now;
            if((entity.position-state.Position).sqrMagnitude>.16f)
            {
                state.Position=entity.position;state.StillSince=now;state.Attempts=0;return;
            }
            var target=entity.GetAttackTarget();
            if(target==null||target.IsDead()||(target.position-entity.position).sqrMagnitude<4)
            {state.StillSince=now;return;}
            if(now-state.StillSince<3||now<state.NextRetry||entity.navigator.isPlanningPath())return;
            // A general crowd/jam elsewhere must not trigger this recovery.
            int px=Mathf.FloorToInt(entity.position.x),py=Mathf.FloorToInt(entity.position.y),pz=Mathf.FloorToInt(entity.position.z);
            bool near=false;
            for(int x=px-1;x<=px+1&&!near;x++)for(int z=pz-1;z<=pz+1&&!near;z++)for(int y=py;y<=py+2;y++)
                if(Barrier.Stud(entity.world.GetBlock(x,y,z))){near=true;break;}
            if(!near)return;
            state.Attempts++;
            state.NextRetry=now+Math.Min(12,3*state.Attempts)+(entity.entityId&7)*.1f;
            state.NoDestroyUntil=now+2;
            entity.IsBreakingBlocks=false;entity.IsBreakingDoors=false;
            entity.moveHelper.IsDestroyArea=false;entity.moveHelper.ClearBlocked();
            entity.navigator.clearPath();
            var info=new PathInfoSingleTarget(entity,target.position,entity.moveHelper.CanBreakBlocks,entity.GetMoveSpeedAggro(),null);
            entity.navigator.GetPathTo(info);
        }
        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(EntityAlive),"OnUpdateLive"),postfix:new HarmonyMethod(typeof(Recovery),nameof(UpdatePostfix)));
            harmony.Patch(AccessTools.Method(typeof(EAIDestroyArea),"CanExecute"),postfix:new HarmonyMethod(typeof(Recovery),nameof(DestroyPostfix)));
            harmony.Patch(AccessTools.Method(typeof(EAIDestroyArea),"Continue"),postfix:new HarmonyMethod(typeof(Recovery),nameof(DestroyPostfix)));
        }
    }
}
