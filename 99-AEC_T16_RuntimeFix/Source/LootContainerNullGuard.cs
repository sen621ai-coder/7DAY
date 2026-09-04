using System;
using System.Collections.Generic;
using HarmonyLib;

namespace AECT16RuntimeFix
{
    /// <summary>
    /// EntityLootContainer.Write and OnUpdateEntity both assume Entity.bag is
    /// initialized. Entity.InitializeBagFromLootList leaves it null when the
    /// effective LootList cannot be resolved or has an invalid size, which then
    /// turns one bad loot-bag definition into a repeating world/network NRE.
    /// </summary>
    public static class LootContainerNullGuard
    {
        private const int FallbackSlotCount = 18;
        private static readonly HashSet<string> ReportedBags =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static void Install(Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(typeof(Entity), "InitCommon");
                if (target == null)
                {
                    throw new MissingMethodException(typeof(Entity).FullName, "InitCommon");
                }

                harmony.Patch(target,
                    postfix: new HarmonyMethod(typeof(LootContainerNullGuard), nameof(InitCommonPostfix)));
                T16RuntimeFixMod.SafeLog("[AEC-LootBag-Fix] Null Bag recovery active.");
            }
            catch (Exception ex)
            {
                T16RuntimeFixMod.SafeLog(
                    "[AEC-LootBag-Fix] Installation failed: " + ex.GetBaseException().Message);
            }
        }

        public static void InitCommonPostfix(Entity __instance)
        {
            var lootEntity = __instance as EntityLootContainer;
            if (lootEntity == null || lootEntity.bag != null)
            {
                return;
            }

            string lootList = null;
            string entityClassName = null;
            int slotCount = FallbackSlotCount;
            bool usedFallbackSize = true;

            try
            {
                lootList = lootEntity.GetLootList();
                entityClassName = EntityClass.GetEntityClassName(lootEntity.entityClass);

                var container = string.IsNullOrEmpty(lootList)
                    ? null
                    : LootContainer.GetLootContainer(lootList, false);
                if (container != null)
                {
                    long requestedSlots = (long)container.size.x * container.size.y;
                    if (requestedSlots > 0 && requestedSlots <= ushort.MaxValue)
                    {
                        slotCount = (int)requestedSlots;
                        usedFallbackSize = false;
                    }
                }

                lootEntity.bag = new Bag(slotCount);

                string signature = (entityClassName ?? "<unknown>") + "|" + (lootList ?? "<empty>");
                lock (ReportedBags)
                {
                    if (ReportedBags.Add(signature))
                    {
                        T16RuntimeFixMod.SafeLog(
                            "[AEC-LootBag-Fix] Recovered uninitialized Bag: entityClass=" +
                            (entityClassName ?? "<unknown>") + ", LootList=" +
                            (lootList ?? "<empty>") + ", slots=" + slotCount +
                            (usedFallbackSize ? " (fallback; lootcontainer missing or invalid)" :
                                " (resolved from lootcontainer)") + ".");
                    }
                }
            }
            catch (Exception ex)
            {
                // Never let diagnostics recreate the original spawn failure.
                if (lootEntity.bag == null)
                {
                    lootEntity.bag = new Bag(FallbackSlotCount);
                }

                T16RuntimeFixMod.SafeLog(
                    "[AEC-LootBag-Fix] Recovery diagnostics failed for entityClass=" +
                    (entityClassName ?? "<unknown>") + ", LootList=" +
                    (lootList ?? "<empty>") + ": " + ex.GetBaseException().Message);
            }
        }
    }
}
