using System;
using System.Reflection;
using HarmonyLib;

namespace YFAutomation.CargoDrones
{
    // API guards for fenced endpoints. Raw-array writers must check the shared
    // busy state before debiting their source; network reads use session envelopes.
    public static class CargoNativeWriteGuards
    {
        sealed class Permit{public object Instance;public ItemStack[] Items;public PackedBoolArray Locks;}
        [ThreadStatic] static Permit permit;
        static TileEntity Tile(object instance)
        {var feature=instance as TEFeatureAbs;return feature==null?instance as TileEntity:feature.Parent;}
        static bool Blocked(object instance){return CargoNativeValidationEndpoint.IsFenced(Tile(instance));}
        internal static void Install(Harmony harmony)
        {
            foreach(var type in new[]{typeof(TileEntityCollector),typeof(TEFeatureStorage)})
            {
                foreach(string name in new[]{type==typeof(TileEntityCollector)?"set_Items":"set_items","UpdateSlot","RemoveItem"})
                    Patch(harmony,type,name,name.StartsWith("set_")?nameof(BeforeReplace):nameof(BeforeVoid));
                Patch(harmony,type,"AddItem",nameof(BeforeAdd));
                Patch(harmony,type,"TryStackItem",nameof(BeforeStack));
            }
            foreach(string name in new[]{"SetEmpty","SetContainerSize"})Patch(harmony,typeof(TEFeatureStorage),name,nameof(BeforeVoid));
            Patch(harmony,typeof(TEFeatureStorage),"set_SlotLocks",nameof(BeforeLocks));
            Patch(harmony,typeof(TEFeatureStorage),"RemoveItems",nameof(BeforeRemove));
            Patch(harmony,typeof(TEFeatureAbs),"CanLockOnServer",nameof(BeforeFeatureLock));
            // Damage may harvest/drop inventory before OnBlockRemoved runs.
            // Defer damage only for the short prepared inventory transaction.
            Patch(harmony,typeof(Block),"DamageBlock",nameof(BeforeDamage));
        }
        static void Patch(Harmony harmony,Type type,string name,string prefix)
        {
            var method=AccessTools.Method(type,name);if(method==null)throw new MissingMethodException(type.Name,name);
            harmony.Patch(method,prefix:new HarmonyMethod(typeof(CargoNativeWriteGuards),prefix));
        }
        internal static void Replace(TileEntity tile,ItemStack[] items)
        {
            if(permit!=null||!CargoNativeValidationEndpoint.IsFenced(tile))throw new InvalidOperationException("Invalid inventory replacement authorization");
            var collector=tile as TileEntityCollector;object instance=collector!=null?(object)collector:((TileEntityComposite)tile).GetFeature<TEFeatureStorage>();
            permit=new Permit{Instance=instance,Items=items};
            try{if(collector!=null)collector.Items=items;else ((TEFeatureStorage)instance).items=items;
                if(permit!=null)throw new InvalidOperationException("Native replacement guard was not invoked");}
            finally{permit=null;}
        }
        public static void BeforeReplace(object __instance,ItemStack[] __0)
        {
            if(permit!=null&&permit.Items!=null&&ReferenceEquals(permit.Instance,__instance)&&ReferenceEquals(permit.Items,__0))
            {permit=null;return;} // Consume before callbacks; no reentrant authorization.
            BeforeVoid(__instance);
        }
        internal static void NormalizeRecoveryLocks(TEFeatureStorage storage)
        {
            if(permit!=null||storage.SlotLocks!=null||!CargoNativeValidationEndpoint.IsFenced(storage.Parent))throw new InvalidOperationException("Invalid recovery lock normalization");
            var locks=new PackedBoolArray(storage.items.Length);permit=new Permit{Instance=storage,Locks=locks};
            try{storage.SlotLocks=locks;if(permit!=null)throw new InvalidOperationException("Native lock guard was not invoked");}finally{permit=null;}
        }
        public static void BeforeLocks(TEFeatureStorage __instance,PackedBoolArray __0)
        {
            if(permit!=null&&permit.Locks!=null&&ReferenceEquals(permit.Instance,__instance)&&ReferenceEquals(permit.Locks,__0)){permit=null;return;}
            BeforeVoid(__instance);
        }
        public static void BeforeVoid(object __instance)
        {if(Blocked(__instance))throw new InvalidOperationException("Cargo transaction holds native inventory; void writer must retry before consuming its source");}
        public static bool BeforeAdd(object __instance,ref bool __result)
        {if(!Blocked(__instance))return true;__result=false;return false;}
        public static bool BeforeStack(object __instance,ref ValueTuple<bool,bool> __result)
        {if(!Blocked(__instance))return true;__result=new ValueTuple<bool,bool>(false,false);return false;}
        public static bool BeforeRemove(object __instance,ref int __result)
        {if(!Blocked(__instance))return true;__result=0;return false;}
        public static bool BeforeFeatureLock(TEFeatureAbs __instance,ref bool __result)
        {if(!Blocked(__instance))return true;__result=false;return false;}
        public static bool BeforeDamage(WorldBase __0,BlockValueRef __1,ref int __result)
        {
            var world=__0 as World;if(world==null||world.IsRemote())return true;
            if(!CargoNativeValidationEndpoint.IsFenced(world.GetTileEntity(__1.BlockPosition)))return true;
            __result=0;return false;
        }
    }
}
