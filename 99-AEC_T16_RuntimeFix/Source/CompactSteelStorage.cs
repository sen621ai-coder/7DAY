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
            var grid = __instance.ItemGrid;
            if (grid == null) return;
            var size = grid.ContainerSize;
            if (!(size.x == 15 && size.y == 10) && !(size.x == 12 && size.y == 13)) return;
            // Do not truncate unknown or larger inventories.
            if (grid.Length != 150 && grid.Length != 156) return;
            var locks = grid.SlotLocks;
            PackedBoolArray expandedLocks = null;
            if (locks == null || locks.Length < 156)
            {
                expandedLocks = new PackedBoolArray(156);
                if (locks != null)
                    for (int i = 0; i < locks.Length; i++) expandedLocks[i] = locks[i];
            }
            // Preserve all existing item references while expanding the grid.
            if (grid.Length != 156 || size.x != 12 || size.y != 13) grid.Resize(new Vector2i(12, 13));
            if (expandedLocks != null) grid.SetSlotLocks(expandedLocks);
        }
    }
}
