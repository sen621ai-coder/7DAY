using System;
using System.Collections.Generic;
using HarmonyLib;

namespace AECT16RuntimeFix
{
    // Keep the native timer, fuel accounting, serialization and networking.
    public static class CollectorBatchStorage
    {
        public static bool Applies(string name)
        {
            return name == "AutoMinerIron" || name == "AutoMinerLead" || name == "AutoMinerCoal"
                || name == "AutoMinerNitrate" || name == "AutoMinerClay" || name == "AutoMinerShale"
                || name == "AutoMinerBrass" || name == "yfAutoForestry" || name == "cntChickenCoop";
        }
        static bool Applies(TileEntityCollector te) => te != null && Applies(te.blockValue.Block.GetBlockName());
        public static int Capacity(bool packer) => packer ? 6 : 3;
        public static int Remaining(int stored, bool packer) => Math.Max(0, Capacity(packer) - stored);
        static bool IsCoop(TileEntityCollector te) => te.blockValue.Block.GetBlockName() == "cntChickenCoop";
        static int SlotCapacity(TileEntityCollector te, BlockCollector.OutputType type)
        {
            // Coop yield is now doubled by BatchCount; keep the previous slot capacity.
            return IsCoop(te) ? te.getCurrentConvertCount(type) : Capacity(te.HasModCount);
        }
        public static void Install(Harmony h)
        {
            Patch(h, "getCurrentConvertCount", nameof(BatchCount), false);
            Patch(h, "getFirstFreeIndex", nameof(FreeSlot), true);
            Patch(h, "handleUpdateForOutputType", nameof(Prepare), true);
            Patch(h, "getMaxProductionCount", nameof(LimitBatch), false);
            Patch(h, "newItem", nameof(MergeBatch), false);
            Patch(h, "get_containerSize", nameof(Size), false);
            Patch(h, "get_OutputWindowHeight", nameof(Height), false);
            h.Patch(AccessTools.Method(typeof(XUiC_DewCollectorContainer), "SetSlots"),
                postfix: new HarmonyMethod(typeof(CollectorBatchStorage), nameof(SlotLimits)));
        }
        static void Patch(Harmony h, string target, string method, bool prefix)
        {
            var patch = new HarmonyMethod(typeof(CollectorBatchStorage), method);
            h.Patch(AccessTools.Method(typeof(TileEntityCollector), target), prefix: prefix ? patch : null, postfix: prefix ? null : patch);
        }
        static void BatchCount(TileEntityCollector __instance, ref int __result)
        {
            // Exactly one full slot per batch for miners/forestry, including clay.
            // Native fuel and timer logic remains unchanged. Zero chickens still yields zero.
            if (!Applies(__instance)) return;
            __result = IsCoop(__instance) ? __result * 2 : Capacity(__instance.HasModCount);
        }
        static bool CanFill(ItemStack stack, BlockCollector.OutputType type, int capacity)
        {
            if (stack == null || stack.IsEmpty()) return true;
            string name = stack.itemValue.ItemClass.GetItemName();
            return (name == type.OutputItem || name == type.OutputItemModded) && stack.count < capacity;
        }
        static bool FreeSlot(TileEntityCollector __instance, List<int> __0, ref int __result)
        {
            if (!Applies(__instance)) return true;
            var items = __instance.Items;
            __result = -1;
            foreach (int i in __0)
            {
                if (i < 0 || i >= items.Length) continue;
                var type = __instance.GetSlotOutputType(i);
                if (CanFill(items[i], type, SlotCapacity(__instance, type))) { __result = i; break; }
            }
            return false;
        }
        static void Prepare(TileEntityCollector __instance, BlockCollector.OutputType __1)
        {
            if (!Applies(__instance)) return;
            TileEntityCollector.FillData fill;
            if (__instance.fillDataLookup.TryGetValue(__1.Name, out fill))
            {
                var items = __instance.Items;
                if (fill.slot < 0 || fill.slot >= items.Length || !CanFill(items[fill.slot], __1, SlotCapacity(__instance, __1)))
                    __instance.fillDataLookup.Remove(__1.Name);
            }
        }
        static int ProductionSlot(TileEntityCollector te, BlockCollector.OutputType type)
        {
            TileEntityCollector.FillData fill;
            if (te.fillDataLookup.TryGetValue(type.Name, out fill)) return fill.slot;
            var items = te.Items;
            for (int i = 0; i < items.Length; i++)
                if (te.GetSlotOutputType(i).Name == type.Name && CanFill(items[i], type, SlotCapacity(te, type))) return i;
            return -1;
        }
        static void LimitBatch(TileEntityCollector __instance, BlockCollector.OutputType __0, ref int __result)
        {
            if (!Applies(__instance)) return;
            int slot = ProductionSlot(__instance, __0);
            if (slot < 0 || slot >= __instance.Items.Length) { __result = 0; return; }
            var stack = __instance.Items[slot];
            __result = Math.Min(__result, Math.Max(0, SlotCapacity(__instance, __0) - (stack == null || stack.IsEmpty() ? 0 : stack.count)));
        }
        static void MergeBatch(TileEntityCollector __instance, BlockCollector.OutputType __0, ref ItemStack __result)
        {
            if (!Applies(__instance) || __result == null || __result.IsEmpty()) return;
            int slot = ProductionSlot(__instance, __0);
            if (slot < 0 || slot >= __instance.Items.Length) return;
            var previous = __instance.Items[slot];
            if (previous != null && !previous.IsEmpty() && previous.itemValue.type == __result.itemValue.type)
                __result.count += previous.count;
        }
        static void Size(TileEntityCollector __instance, ref Vector2i __result)
        { if (Applies(__instance) && !IsCoop(__instance)) __result = new Vector2i(3, 2); }
        static void Height(TileEntityCollector __instance, ref int __result)
        { if (Applies(__instance) && !IsCoop(__instance)) __result = 2; }
        static void SlotLimits(XUiC_DewCollectorContainer __instance, TileEntityCollector __0)
        {
            if (!Applies(__0) && !ApiaryProduction.Applies(__0)) return;
            foreach (var slot in __instance.GetChildrenByType<XUiC_ItemStack>())
                if (slot.SlotNumber >= 0 && slot.SlotNumber < __0.Items.Length)
                    slot.OverrideStackCount = Math.Max(1, ApiaryProduction.Applies(__0)
                        ? __0.getCurrentConvertCount(__0.GetSlotOutputType(slot.SlotNumber))
                        : SlotCapacity(__0, __0.GetSlotOutputType(slot.SlotNumber)));
        }
    }
}
