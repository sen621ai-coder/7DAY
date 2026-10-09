using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace PZAEC.FlyingSword
{
    public static class SwordInventory
    {
        public static void Install(Harmony h)
        {
            foreach(var name in new[]{"HandleStackSwap","HandlePartialStackPickup","HandleDropOne","SwapItem","HandleMoveToPreferredLocation"})h.Patch(AccessTools.Method(typeof(XUiC_ItemStack),name),prefix:new HarmonyMethod(typeof(SwordInventory),nameof(Slot)));
            h.Patch(AccessTools.Method(typeof(XUiC_ItemStack),"CanSwap"),postfix:new HarmonyMethod(typeof(SwordInventory),nameof(Swap)));
            h.Patch(AccessTools.PropertyGetter(typeof(XUiC_ItemStack),"ItemNameText"),postfix:new HarmonyMethod(typeof(SwordInventory),nameof(Name)));
            h.Patch(AccessTools.Method(typeof(PlayerMoveController),"DropHeldItem"),prefix:new HarmonyMethod(typeof(SwordInventory),nameof(Drop)));
            h.Patch(AccessTools.Method(typeof(Inventory),"Execute"),prefix:new HarmonyMethod(typeof(SwordInventory),nameof(Execute)));
            h.Patch(AccessTools.Method(typeof(Inventory),"DecItem",new[]{typeof(ItemValue),typeof(int),typeof(bool),typeof(IList<ItemStack>)}),prefix:new HarmonyMethod(typeof(SwordInventory),nameof(Consume)));
            h.Patch(AccessTools.Method(typeof(Inventory),"GetItemCount",new[]{typeof(ItemValue),typeof(bool),typeof(int),typeof(int),typeof(bool)}),postfix:new HarmonyMethod(typeof(SwordInventory),nameof(Count)));
            h.Patch(AccessTools.Method(typeof(ItemStackGrid),"SetLocked"),prefix:new HarmonyMethod(typeof(SwordInventory),nameof(Unlock)));
            h.Patch(AccessTools.Method(typeof(ItemStackGrid),"IsLocked"),postfix:new HarmonyMethod(typeof(SwordInventory),nameof(Locked)));
            foreach(var name in new[]{"Sort","Compact","CompactSlots"})h.Patch(AccessTools.Method(typeof(ItemStackGrid),name),prefix:new HarmonyMethod(typeof(SwordInventory),nameof(Sort)));
            h.Patch(AccessTools.Method(typeof(NetPackagePlayerInventory),"ProcessPackage"),prefix:new HarmonyMethod(typeof(SwordInventory),nameof(BeforeSync)),postfix:new HarmonyMethod(typeof(SwordInventory),nameof(AfterSync)));
        }
        static bool Slot(XUiC_ItemStack __instance){return SwordRules.Deployed(__instance.ItemStack?.itemValue)==0;}
        static void Swap(XUiC_ItemStack __instance,ItemStack __0,ref bool __result){if(!Slot(__instance)||SwordRules.Deployed(__0?.itemValue)>0)__result=false;}
        static void Name(XUiC_ItemStack __instance,ref string __result){if(SwordRules.Deployed(__instance.ItemStack?.itemValue)>0)__result+=" · 已御出";}
        static bool Drop(EntityPlayerLocal ___entityPlayerLocal){return ___entityPlayerLocal==null||!SwordControls.UsingDeployKey(___entityPlayerLocal)&&SwordRules.Deployed(___entityPlayerLocal.inventory.holdingItemItemValue)==0;}
        static bool Execute(Inventory __instance,int __0,bool __1,EntityAlive ___entity)
        {
            var iv=__instance.holdingItemItemValue;if(SwordRules.Deployed(iv)>0)return false;
            var p=___entity as EntityPlayer;if(p!=null&&__0==0&&!__1){var s=SwordRuntime.State(p);if(SwordRules.IsSword(iv)&&s.ChargeAt>=0)return false;s.LastAttack=Time.time;}
            if(p!=null&&p.AttachedToEntity is EntityJuque v&&!__1){if(!__instance.IsHoldingGun()||!SwordRules.InArc(v.transform.forward,p.GetLookVector()))return false;}
            return true;
        }
        static bool Consume(Inventory __instance,ItemValue __0,ref int __result){if(__0==null)return true;for(int i=0;i<__instance.SlotCount;i++){var item=__instance.GetItem(i).itemValue;if(item.type==__0.type&&SwordRules.Deployed(item)>0){__result=0;return false;}}return true;}
        static void Count(Inventory __instance,ItemValue __0,ref int __result){if(__0==null)return;for(int i=0;i<__instance.SlotCount;i++){var item=__instance.GetItem(i);if(item.itemValue.type==__0.type&&SwordRules.Deployed(item.itemValue)>0)__result=Math.Max(0,__result-item.count);}}
        static bool Unlock(ItemStackGrid __instance,int __0,bool __1){return __1||__0<0||__0>=__instance.Length||SwordRules.Deployed(__instance.GetItem(__0).itemValue)==0;}
        static void Locked(ItemStackGrid __instance,int __0,ref bool __result){if(__0>=0&&__0<__instance.Length&&SwordRules.Deployed(__instance.GetItem(__0).itemValue)>0)__result=true;}
        static bool Sort(ItemStackGrid __instance){for(int i=0;i<__instance.Length;i++)if(SwordRules.Deployed(__instance.GetItem(i).itemValue)>0)return false;return true;}
        public static void Reserve(EntityPlayer p,int slot,ItemValue item){item.SetMetadata("PZAECJuqueWasLocked",p.inventory.ItemGrid.IsLocked(slot)?1:0);p.inventory.ItemGrid.SetLocked(slot,true);}
        public static void RestoreLock(EntityPlayer p,int slot,ItemValue item){int n;bool previous=item.TryGetMetadata("PZAECJuqueWasLocked",out n)&&n!=0;p.inventory.ItemGrid.SetLocked(slot,previous);item.RemoveMetaData("PZAECJuqueWasLocked");}
        public sealed class Saved {public EntityPlayer Player;public Dictionary<int,ItemValue> Items=new Dictionary<int,ItemValue>();}
        static void BeforeSync(NetPackagePlayerInventory __instance,World _world,out Saved __state)
        {__state=null;if(!SwordRuntime.Server||__instance.Sender==null)return;var p=_world.GetEntity(__instance.Sender.entityId) as EntityPlayer;if(p==null)return;__state=new Saved{Player=p};for(int i=0;i<p.inventory.SlotCount;i++){var iv=p.inventory.GetItem(i).itemValue;if(SwordRules.IsSword(iv))__state.Items[i]=iv.Clone();}for(int i=0;i<p.bag.ItemGrid.Length;i++){var iv=p.bag.ItemGrid.GetItem(i).itemValue;if(SwordRules.IsSword(iv))__state.Items[-i-1]=iv.Clone();}}
        static void AfterSync(Saved __state)
        {
            if(__state==null)return;var p=__state.Player;
            foreach(var pair in __state.Items){var old=pair.Value;var id=SwordRules.Id(old);if(string.IsNullOrEmpty(id))continue;bool reserved=SwordRules.Deployed(old)>0&&pair.Key>=0;
                for(int group=0;group<2;group++){var grid=group==0?p.inventory.ItemGrid:p.bag.ItemGrid;for(int i=0;i<grid.Length;i++){
                    var current=grid.GetItem(i).itemValue;if(SwordRules.Id(current)!=id)continue;
                    if(reserved&&(group!=0||i!=pair.Key)){grid.ChangeCount(i,-grid.GetItem(i).count);continue;}
                    if(SwordRules.IsSword(current)){float ratio=SwordRules.Capacity[SwordRules.Tier(current)]/SwordRules.Capacity[SwordRules.Tier(old)];current.SetMetadata(SwordRules.EnergyKey,SwordRules.Energy(old)*ratio);current.SetMetadata(SwordRules.DeployedKey,SwordRules.Deployed(old));}
                }}
                if(reserved)p.inventory.SetItem(pair.Key,new ItemStack(old,1));
            }
        }
    }
}
