using System;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
namespace PZAEC.M1
{
    public static class Modules
    {
        const string Quarantine="m1ModulesQuarantined";
        sealed class Cache {public string Signature;public int Mask;}
        static readonly ConditionalWeakTable<Vehicle,Cache> cache=new ConditionalWeakTable<Vehicle,Cache>();
        [ThreadStatic] static int? syncActor;
        public static void Install(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(Vehicle),"CalcEffects"),prefix:new HarmonyMethod(typeof(Modules),nameof(BeforeEffects)),postfix:new HarmonyMethod(typeof(Modules),nameof(AfterEffects)),finalizer:new HarmonyMethod(typeof(Modules),nameof(RestoreEffects)));
            h.Patch(AccessTools.Method(typeof(Vehicle),"CalcMods"),prefix:new HarmonyMethod(typeof(Modules),nameof(BareMods)),finalizer:new HarmonyMethod(typeof(Modules),nameof(RestoreEffects)));
            h.Patch(AccessTools.Method(typeof(Vehicle),"SetItemValue"),prefix:new HarmonyMethod(typeof(Modules),nameof(ExpandVehicle)));
            h.Patch(AccessTools.Method(typeof(Vehicle),"SetItemValueMods"),prefix:new HarmonyMethod(typeof(Modules),nameof(LocalMods)));
            h.Patch(AccessTools.Method(typeof(ItemValue),"Read"),postfix:new HarmonyMethod(typeof(Modules),nameof(ReadItem)));
            h.Patch(AccessTools.Method(typeof(ItemValue),"CalcModSlotCount"),postfix:new HarmonyMethod(typeof(Modules),nameof(SlotCount)));
            h.Patch(AccessTools.Method(typeof(Vehicle),"LoadItems"),prefix:new HarmonyMethod(typeof(Modules),nameof(LoadItems)));
            h.Patch(AccessTools.Method(typeof(EntityVehicle),"ReadSyncData"),prefix:new HarmonyMethod(typeof(Modules),nameof(BeginSync)),finalizer:new HarmonyMethod(typeof(Modules),nameof(EndSync)));
            h.Patch(AccessTools.Method(typeof(XUiC_ItemCosmeticStack),"CanRemove"),postfix:new HarmonyMethod(typeof(Modules),nameof(CanRemove)));
            h.Patch(AccessTools.Method(typeof(XUiC_ItemCosmeticStack),"CanSwap"),prefix:new HarmonyMethod(typeof(Modules),nameof(CosmeticSwap)));
            h.Patch(AccessTools.Method(typeof(XUiC_ItemPartStack),"CanSwap"),prefix:new HarmonyMethod(typeof(Modules),nameof(CanSwap)));
            h.Patch(AccessTools.Method(typeof(XUiC_ItemPartStack),"CanRemove"),postfix:new HarmonyMethod(typeof(Modules),nameof(CanRemove)));
        }
        static string[] Names(ItemValue item)=>item?.Modifications==null?new string[0]:item.Modifications.Select(x=>x==null||x.type==0?null:x.ItemClass?.GetItemName()??"unknown").ToArray();
        static bool Cosmetic(ItemValue item)=>item?.CosmeticMods!=null&&item.CosmeticMods.Any(x=>x!=null&&x.type!=0);
        static string Signature(ItemValue item)=>string.Join("|",Names(item))+"/"+(Cosmetic(item)?"cosmetic":"")+"/"+(item!=null&&item.TryGetMetadata(Quarantine,out int q)?q:0);
        static int Mask(ItemValue item)
        {return item!=null&&!Cosmetic(item)&&(!item.TryGetMetadata(Quarantine,out int q)||q==0)&&ModuleRules.Validate(Rules.Index(item.ItemClass?.GetItemName()),Names(item),out int mask)?mask:0;}
        public static int Get(EntityVehicle entity)=>entity?.vehicle==null?0:Get(entity.vehicle);
        public static int Get(Vehicle vehicle)
        {if(Rules.Index(vehicle.GetName())<0)return 0;if(!cache.TryGetValue(vehicle,out var c)){c=new Cache{Signature=Signature(vehicle.itemValue),Mask=Mask(vehicle.itemValue)};cache.Add(vehicle,c);}return c.Mask;}
        public static bool Invalid(EntityVehicle entity)
        {var item=entity.vehicle.itemValue;return Cosmetic(item)||(item.TryGetMetadata(Quarantine,out int q)&&q!=0)||!ModuleRules.Validate(Weapons.Tier(entity),Names(item),out int mask);}
        public static float Reload(EntityVehicle entity)=>Weapons.Spec(entity).Reload*ModuleRules.Reload(Get(entity));
        static void Expand(ItemValue item)
        {
            int tier=Rules.Index(item?.ItemClass?.GetItemName());if(tier<0)return;
            int count=ModuleRules.Slots(tier);if(item.Modifications==null||item.Modifications.Length<count){var array=item.Modifications??new ItemValue[0];Array.Resize(ref array,count);for(int i=0;i<array.Length;i++)if(array[i]==null)array[i]=ItemValue.None;item.Modifications=array;}
        }
        static void LocalMods(Vehicle __instance,ItemValue __0)
        {
            if(Rules.Index(__instance.GetName())<0||__0==null)return;
            var p=GameManager.Instance?.World?.GetPrimaryPlayer();
            if(cache.TryGetValue(__instance,out var previous)&&previous.Signature!=Signature(__0)&&Parked(__instance.entity)&&Authorized(__instance.entity,p)&&!Cosmetic(__0)&&ModuleRules.Validate(Rules.Index(__0.ItemClass?.GetItemName()),Names(__0),out int mask))
                __instance.itemValue.SetMetadata(Quarantine,0);
        }
        static void ExpandVehicle(ItemValue __0)=>Expand(__0);
        static void ReadItem(ItemValue __instance)=>Expand(__instance);
        static void SlotCount(ItemValue __instance,ref int __result){int tier=Rules.Index(__instance.ItemClass?.GetItemName());if(tier>=0)__result=ModuleRules.Slots(tier);}
        // Native effect calculation still handles global settings and driver effects. Invalid legacy mods are retained but cannot supply effects.
        static void BareMods(Vehicle __instance,out ItemValue __state)
        {__state=null;if(Rules.Index(__instance.GetName())<0)return;__state=__instance.itemValue;var bare=__state.Clone();bare.Modifications=new ItemValue[0];bare.CosmeticMods=new ItemValue[0];__instance.itemValue=bare;}
        static void BeforeEffects(Vehicle __instance,out ItemValue __state)
        {
            __state=null;if(Rules.Index(__instance.GetName())<0)return;
            var item=__instance.itemValue;var signature=Signature(item);var c=cache.GetValue(__instance,_=>new Cache());
            if(c.Signature!=signature){c.Signature=signature;c.Mask=Mask(item);if(Weapons.IsTank(__instance.entity)){var s=Weapons.Register(__instance.entity);s.Trigger.Stop();s.NextFire=Mathf.Max(s.NextFire,Time.time+Reload(__instance.entity));}}
            __state=item;var bare=item.Clone();bare.Modifications=new ItemValue[0];bare.CosmeticMods=new ItemValue[0];__instance.itemValue=bare;
        }
        static void AfterEffects(Vehicle __instance,ItemValue __state)
        {
            if(__state==null)return;__instance.itemValue=__state;int mask=Get(__instance);
            __instance.EffectMotorTorquePer*=ModuleRules.Torque(mask);__instance.EffectVelocityMaxPer*=ModuleRules.Speed(mask);__instance.EffectFuelUsePer*=ModuleRules.Fuel(mask);
        }
        static Exception RestoreEffects(Vehicle __instance,ItemValue __state,Exception __exception){if(__state!=null)__instance.itemValue=__state;return __exception;}
        static bool Parked(EntityVehicle v)
        {var s=Weapons.Register(v);return !v.IsDead()&&v.vehicle.GetHealth()>0&&!v.hasDriver&&v.GetAttached(1)==null&&(v.vehicleRB==null||v.vehicleRB.velocity.sqrMagnitude<.04f)&&Time.time-s.LastShot>=10&&Time.time-s.LastWeaponActivity>=10&&Time.time-s.LastDamage>=10;}
        static bool Authorized(EntityVehicle v,EntityPlayer p)=>p!=null&&!p.IsDead()&&p.AttachedToEntity==null&&(p.position-v.position).sqrMagnitude<=64&&(v.GetOwner()==null||(p.PersistentPlayerData!=null&&v.IsUserAllowed(p.PersistentPlayerData.PrimaryId)));
        static void BeginSync(int __2,out int? __state){__state=syncActor;syncActor=__2;}
        static Exception EndSync(EntityVehicle __instance,ushort __1,int? __state,Exception __exception){syncActor=__state;if(__exception==null&&Weapons.Server&&Weapons.IsTank(__instance)&&(__1&EntityVehicle.cSyncItem)!=0)__instance.SendSyncData(EntityVehicle.cSyncItem);return __exception;}
        static void LoadItems(Vehicle __instance,ItemStack[] __0)
        {
            if(!Weapons.Server||!syncActor.HasValue||Rules.Index(__instance.GetName())<0||__0==null||__0.Length==0)return;
            var incoming=__0[0]?.itemValue;if(incoming==null)return;
            var old=__instance.itemValue;bool changed=!Names(old).SequenceEqual(Names(incoming))||Cosmetic(old)!=Cosmetic(incoming);
            if(!changed){incoming.SetMetadata(Quarantine,old!=null&&old.TryGetMetadata(Quarantine,out int q)?q:0);return;}
            var player=GameManager.Instance.World.GetEntity(syncActor.Value) as EntityPlayer;
            bool valid=string.Equals(incoming.ItemClass?.GetItemName(),__instance.GetName()+"Placeable",StringComparison.OrdinalIgnoreCase)&&Parked(__instance.entity)&&Authorized(__instance.entity,player)&&!Cosmetic(incoming)&&ModuleRules.Validate(Rules.Index(incoming.ItemClass?.GetItemName()),Names(incoming),out int mask);
            // A native inventory transaction may already have moved the item on the sender. Keep it removable rather than deleting/rolling it back and risking loss or duplication.
            incoming.SetMetadata(Quarantine,valid?0:1);
            if(!valid)Log.Warning("[M1] Module update quarantined; retained items, disabled all module effects. Park and remove/reinstall modules with permission.");
        }
        static EntityVehicle UIEntity(XUiC_BasePartStack slot)=>(slot.WindowGroup?.Controller as XUiC_VehicleWindowGroup)?.CurrentVehicleEntity;
        static bool UIAllowed(XUiC_BasePartStack slot)
        {var v=UIEntity(slot);return !Weapons.IsTank(v)||(Parked(v)&&Authorized(v,slot.xui.playerUI.entityPlayer));}
        static void Tip(XUiC_BasePartStack slot,string message)=>GameManager.ShowTooltip(slot.xui.playerUI.entityPlayer,message);
        static bool CosmeticSwap(XUiC_ItemCosmeticStack __instance,ItemStack __0,ref bool __result)
        {var item=__instance.xui.AssembleItem.CurrentItem?.itemValue;if(Rules.Index(item?.ItemClass?.GetItemName())<0&&ModuleRules.Bit(__0?.itemValue?.ItemClass?.GetItemName())==0)return true;__result=false;return false;}
        static bool CanSwap(XUiC_ItemPartStack __instance,ItemStack __0,ref bool __result)
        {
            var item=__instance.xui.AssembleItem.CurrentItem?.itemValue;int tier=Rules.Index(item?.ItemClass?.GetItemName());int bit=ModuleRules.Bit(__0?.itemValue?.ItemClass?.GetItemName());
            if(tier<0&&bit==0)return true;
            __result=false;if(tier<0){Tip(__instance,"M1专用模组只能安装到M1坦克。");return false;}
            if(!UIAllowed(__instance)){Tip(__instance,"M1改装：需有权限，停车离座且10秒内未开火或受伤。");return false;}
            var names=Names(item);int slot=__instance.SlotNumber;if(slot<0||slot>=ModuleRules.Slots(tier)||slot>=names.Length)return false;
            names[slot]=__0?.itemValue?.ItemClass?.GetItemName();
            if(bit==0||!ModuleRules.Validate(tier,names,out int mask)){Tip(__instance,"M1仅接受专用模组；同类不能重复，动力/巡航与火控/装填分别互斥。");return false;}
            // Native UI owns the actual item transfer, preserving its slot and inventory accounting.
            return true;
        }
        static void CanRemove(XUiC_BasePartStack __instance,ref bool __result)
        {if(__result&&!UIAllowed(__instance)){__result=false;Tip(__instance,"M1改装：需有权限，停车离座且10秒内未开火或受伤。");}
        }
    }
}
