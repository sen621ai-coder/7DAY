using System;
using HarmonyLib;

namespace AECT16RuntimeFix
{
    // Keep original loot IDs stable for existing saves.
    public static class CompactSteelStorage
    {
        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(TEFeatureStorage), "Read"),
                postfix: new HarmonyMethod(typeof(CompactSteelStorage), nameof(AfterRead)));
        }

        public static void AfterRead(TEFeatureStorage __instance)
        {
            if (__instance.lootListName != "PZAECSteelCrateStorage150" &&
                __instance.lootListName != "PZAECSteelWallCabinetStorage150") return;
            var size = __instance.GetContainerSize();
            if (!(size.x == 15 && size.y == 10) && !(size.x == 12 && size.y == 13)) return;
            var old = __instance.items;
            // Do not truncate unknown or larger inventories.
            if (old == null || (old.Length != 150 && old.Length != 156)) return;
            if (old.Length == 150)
            {
                var expanded = ItemStack.CreateArray(156);
                Array.Copy(old, expanded, old.Length);
                __instance.items = expanded;
            }
            var locks = __instance.SlotLocks;
            if (locks == null || locks.Length < 156)
            {
                var expandedLocks = new PackedBoolArray(156);
                if (locks != null)
                    for (int i = 0; i < locks.Length; i++) expandedLocks[i] = locks[i];
                __instance.SlotLocks = expandedLocks;
            }
            // true would clear the inventory. Preserve all existing item references.
            __instance.SetContainerSize(new Vector2i(12, 13), false);
        }
    }
}
