using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;
namespace PZAEC.M1
{
    public static class Service
    {
        public static void Install(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(Vehicle),"SetItemValue"),prefix:new HarmonyMethod(typeof(Service),nameof(Migrate)));
            h.Patch(AccessTools.Method(typeof(XUiM_Vehicle),"RepairVehicle"),prefix:new HarmonyMethod(typeof(Service),nameof(BlockNativeRepair)));
            h.Patch(AccessTools.Method(typeof(Vehicle),"RepairParts"),prefix:new HarmonyMethod(typeof(Service),nameof(BlockRepairParts)));
            h.Patch(AccessTools.Method(typeof(ItemActionEntryRepair),"OnActivated"),prefix:new HarmonyMethod(typeof(Service),nameof(BlockInventoryRepair)));
            h.Patch(AccessTools.Method(typeof(ItemActionEntryCraft),"OnActivated"),prefix:new HarmonyMethod(typeof(Service),nameof(CraftGuard)));
        }
        static void Migrate(Vehicle __instance,ItemValue __0)
        {
            if(__0==null||Rules.Index(__0.ItemClass?.GetItemName())<0)return;
            if(!__0.TryGetMetadata("m1BalanceVersion",out int version)||version<2){
                // Original item ID stays T16. Preserve its used fraction when moving 1.5m -> 1m.
                if(Rules.Index(__0.ItemClass.GetItemName())==0)__0.UseTimes=Mathf.Clamp(__0.UseTimes/1500000f,0,1)*Rules.Specs[0].Health;
                __0.SetMetadata("m1BalanceVersion",2);
            }
        }
        static bool BlockNativeRepair(XUi __0,Vehicle __1,ref bool __result)
        {
            var v=__1??__0?.Vehicle?.CurrentVehicle?.vehicle;if(v==null||Rules.Index(v.GetName())<0)return true;
            __result=false;var p=__0?.playerUI?.entityPlayer;if(p!=null)GameManager.ShowTooltip(p,"M1维修：维修包放货仓，停车后在车外对准坦克按住G 8秒。");return false;
        }
        static bool BlockRepairParts(Vehicle __instance)=>Rules.Index(__instance.GetName())<0;
        static bool BlockInventoryRepair(ItemActionEntryRepair __instance)
        {
            var slot=__instance.ItemController as XUiC_ItemStack;
            if(slot==null||Rules.Index(slot.ItemStack?.itemValue?.ItemClass?.GetItemName())<0)return true;
            GameManager.ShowTooltip(slot.xui.playerUI.entityPlayer,"请先放置M1，将装甲维修包放入货仓，在车外对准坦克按住G维修。");return false;
        }
        static bool Clean(ItemValue item)=>item.UseTimes<=.5f&&item.Meta==0&&
            (item.Modifications==null||item.Modifications.All(m=>m==null||m.type==0))&&
            (item.CosmeticMods==null||item.CosmeticMods.All(m=>m==null||m.type==0));
        static bool CraftGuard(ItemActionEntryCraft __instance)
        {
            var entry=__instance.ItemController as XUiC_RecipeEntry;var recipe=entry?.Recipe;if(recipe==null)return true;
            var result=ItemClass.list[recipe.itemValueType];if(result==null||Rules.Index(result.GetItemName())<0)return true;
            var xui=entry.xui;
            var candidates=xui.PlayerInventory.GetAllItemStacks();
            if(xui.CurrentWorkstationInputGrid!=null)candidates.AddRange(xui.CurrentWorkstationInputGrid.GetSlots());
            foreach(var ingredient in recipe.ingredients){
                var name=ingredient.itemValue.ItemClass?.GetItemName();if(Rules.Index(name)<0&&name!="vehicleTruck4x4Placeable")continue;
                foreach(var stack in candidates)if(stack!=null&&stack.count>0&&stack.itemValue.type==ingredient.itemValue.type&&!Clean(stack.itemValue)){
                    GameManager.ShowTooltip(xui.playerUI.entityPlayer,"前置车辆必须满修、清空燃油、卸下全部改装。请先放置车辆整理，再收起制作。");return false;
                }
            }return true;
        }
        static bool Eligible(Weapons.State s,EntityPlayer p)
        {
            var v=s.Vehicle;
            return p!=null&&!p.IsDead()&&p.AttachedToEntity==null&&!v.IsDead()&&v.vehicle.GetHealth()>0&&!v.hasDriver&&v.GetAttached(1)==null&&
                (p.position-v.position).sqrMagnitude<=64&&(v.vehicleRB==null||v.vehicleRB.velocity.sqrMagnitude<.04f)&&
                Time.time-s.LastDamage>=10&&Time.time-s.LastShot>=10&&v.vehicle.GetRepairAmountNeeded()>0&&
                (LockManager.Instance==null||!LockManager.Instance.IsLockedServer(v,0))&&
                (v.GetOwner()==null||(p.PersistentPlayerData!=null&&v.IsUserAllowed(p.PersistentPlayerData.PrimaryId)));
        }
        public static void Request(World w,int actor,Weapons.State s,byte op,int serial,Vector3 origin,Vector3 direction)
        {
            var p=w.GetEntity(actor) as EntityPlayer;
            if(s.RepairTrigger.Active(Time.time)&&s.RepairTrigger.Actor!=actor)return;
            if(op==Weapons.RepairStop){if(s.RepairTrigger.Actor==actor&&s.RepairTrigger.Accept(actor,serial,false,Time.time))s.RepairStarted=-1;return;}
            if(!Eligible(s,p)||s.Vehicle.bag==null||s.Vehicle.bag.GetItemCount(ItemClass.GetItem(Rules.RepairKit,false))<1||(origin-p.position).sqrMagnitude>16||
                !Voxel.Raycast(w,new Ray(origin,direction),8,-538750997,8,0)||ItemActionAttack.FindHitEntity(Voxel.voxelRayHitInfo)!=s.Vehicle)return;
            bool continued=s.RepairTrigger.Active(Time.time)&&s.RepairTrigger.Actor==actor;
            if(!s.RepairTrigger.Accept(actor,serial,true,Time.time))return;
            if(!continued||s.RepairStarted<0)s.RepairStarted=Time.time;
        }
        public static void Update(World w,Weapons.State s)
        {
            if(s.RepairStarted<0)return;
            if(!s.RepairTrigger.Active(Time.time)||!Eligible(s,w.GetEntity(s.RepairTrigger.Actor) as EntityPlayer)){s.RepairStarted=-1;s.RepairTrigger.Stop();return;}
            if(Time.time-s.RepairStarted<8)return;
            var kit=ItemClass.GetItem(Rules.RepairKit,false);
            if(kit!=null&&kit.type!=0&&s.Vehicle.bag!=null&&s.Vehicle.bag.DecItem(kit,1)==1){
                s.Vehicle.Health=s.Vehicle.vehicle.GetMaxHealth();
                s.Vehicle.SendSyncData(EntityVehicle.cSyncItem|EntityVehicle.cSyncStorage);
            }
            s.RepairStarted=-1;s.RepairTrigger.Stop();
        }
    }
}
