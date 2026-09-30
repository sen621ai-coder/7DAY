using System;
using HarmonyLib;
using PZAEC.Fishing.Contracts;
using UnityEngine;

namespace PZAEC.Fishing.Runtime
{
    // One local owner. The runtime acquires a lease only for a validated equipped-rod session.
    // Native Update is never skipped; unrelated players and inventory actions are untouched.
    public sealed class NativeInputHooks : IDisposable
    {
        static NativeInputHooks owner;
        readonly Harmony harmony=new Harmony("pzaec.fishing.input.v1");
        readonly NativeControlLease lease;
        long sequence;
        public Action<RawInputFrame> Sampled;
        public Action<FailureReason> LostInput;
        public Action<PlayerMoveController> Preparing;
        public NativeInputHooks(NativeControlLease lease)
        {
            if(lease==null)throw new ArgumentNullException(nameof(lease));
            if(owner!=null)throw new InvalidOperationException("One local fishing input hook owner is allowed");
            this.lease=lease;owner=this;
            try
            {
                harmony.Patch(AccessTools.Method(typeof(PlayerMoveController),"Update"),prefix:new HarmonyMethod(typeof(NativeInputHooks),nameof(BeforeUpdate)),finalizer:new HarmonyMethod(typeof(NativeInputHooks),nameof(AfterUpdate)));
                harmony.Patch(AccessTools.Method(typeof(EntityPlayerLocal),"MoveByInput"),prefix:new HarmonyMethod(typeof(NativeInputHooks),nameof(BeforeMove)),finalizer:new HarmonyMethod(typeof(NativeInputHooks),nameof(AfterMove)));
                harmony.Patch(AccessTools.Method(typeof(Inventory),"Execute",new[]{typeof(int),typeof(bool),typeof(PlayerActionsLocal)}),prefix:new HarmonyMethod(typeof(NativeInputHooks),nameof(BeforeExecute)));
                harmony.Patch(AccessTools.Method(typeof(EntityPlayerLocal),"DamageEntity",new[]{typeof(DamageSource),typeof(int),typeof(bool),typeof(float)}),postfix:new HarmonyMethod(typeof(NativeInputHooks),nameof(AfterDamage)));
            }
            catch{Dispose();throw;}
        }
        static void BeforeUpdate(PlayerMoveController __instance)
        {
            var self=owner;if(self==null)return;
            try{self.Preparing?.Invoke(__instance);}catch(Exception error){self.Lose(FailureReason.ModuleError);Log.Error("[PZAEC.Fishing] "+error);return;}
            if(!self.lease.Active||__instance.entityPlayerLocal!=self.lease.Player)return;
            if(!self.lease.CanReadInput()){self.Lose(FailureReason.MenuOpened);return;}
            try
            {
                self.lease.BeginFrame();
                self.Sampled?.Invoke(self.lease.Sample(++self.sequence,Time.realtimeSinceStartup,Time.unscaledDeltaTime));
            }
            catch(Exception error){self.Lose(FailureReason.ModuleError);Log.Error("[PZAEC.Fishing] "+error);}
        }
        static void BeforeMove(EntityPlayerLocal __instance){owner?.lease.ApplyBeforeMove(__instance);}
        static Exception AfterMove(EntityPlayerLocal __instance,Exception __exception)
        {if(owner!=null&&owner.lease.Player==__instance)owner.lease.RestoreAfterMove();return __exception;}
        static Exception AfterUpdate(PlayerMoveController __instance,Exception __exception)
        {if(__exception!=null&&owner!=null&&owner.lease.Player==__instance.entityPlayerLocal)owner.Lose(FailureReason.ModuleError);return __exception;}
        static bool BeforeExecute(Inventory __instance,int __0)
            => owner==null||!owner.lease.SuppressInventoryAction(__instance,__0);
        static void AfterDamage(EntityPlayerLocal __instance,int __result)
        {if(__result>0&&owner!=null&&owner.lease.Player==__instance)owner.Lose(FailureReason.Damaged);}
        void Lose(FailureReason reason){lease.Release();LostInput?.Invoke(reason);}
        public void Dispose(){lease.Release();harmony.UnpatchSelf();if(owner==this)owner=null;}
    }
}
