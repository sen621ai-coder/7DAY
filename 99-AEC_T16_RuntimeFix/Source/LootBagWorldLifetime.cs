using HarmonyLib;

namespace AECT16RuntimeFix
{
    // WorldTimeBorn is initialized by Entity.Awake and persisted by Entity.Read/Write.
    // Do not use the native update counter: it measures loaded real time and resets on reload.
    public static class LootBagWorldLifetime
    {
        public const ulong OneDay = 24000;

        public static bool Expired(ulong born, ulong now)
        {
            // A backwards settime must not underflow into an immediate expiration.
            return now >= born && now - born >= OneDay;
        }

        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(EntityContainerAbs), "OnUpdateEntity"),
                prefix: new HarmonyMethod(typeof(LootBagWorldLifetime), nameof(BeforeUpdate)));
            T16RuntimeFixMod.SafeLog("[AEC-LootBag-Lifetime] Loot bags expire after 24 world hours; saved birth time retained.");
        }

        public static void BeforeUpdate(EntityContainerAbs __instance, ref int ___deathUpdateTime,
            ref int ___timeStayAfterDeath)
        {
            // Keep native physics, empty-bag disposal, loot locks and network removal.
            // Only the server may decide time-based expiration.
            var loot = __instance as EntityLootContainer;
            if (loot == null)
            {
                return;
            }
            bool expired = loot.world != null && !loot.world.IsRemote() &&
                Expired(loot.WorldTimeBorn, loot.world.worldTime);
            ___deathUpdateTime = 0;
            ___timeStayAfterDeath = expired ? 1 : int.MaxValue;
        }
    }
}
