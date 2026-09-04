using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace AECT16RuntimeFix
{
    // Scope is carried by the bag's own serialized entity/loot-list identity,
    // never by the looting player's GS or by a global change to shared tables.
    public static class HighTierMobLoot
    {
        [ThreadStatic] private static int activeTier;
        private static readonly Regex mobBagPattern = new Regex(
            @"^PZAECMobT(16|17|18|19)_zPack(Reg|Strong|Boss|Lab|Nurse|Soldier|Thug|Utility|Plague)$",
            RegexOptions.CultureInvariant);
        private static readonly Regex bossLootPattern = new Regex(
            @"^(?:PZAECBossLootBundle|AEC.*(?:BossLoot|Loot)|DoomlordBossLoot|RunningKamikazeBossLoot)T(16|17|18|19)$",
            RegexOptions.CultureInvariant);

        public static void Install(Harmony harmony)
        {
            try
            {
                var spawn = AccessTools.Method(typeof(LootContainer), "Spawn");
                var item = AccessTools.Method(typeof(LootContainer), "SpawnItem");
                if (spawn == null || item == null) throw new MissingMethodException("LootContainer spawn methods");
                harmony.Patch(item, transpiler: new HarmonyMethod(typeof(HighTierMobLoot), nameof(ItemFactoryTranspiler)));
                harmony.Patch(spawn,
                    prefix: new HarmonyMethod(typeof(HighTierMobLoot), nameof(SpawnPrefix)),
                    finalizer: new HarmonyMethod(typeof(HighTierMobLoot), nameof(SpawnFinalizer)));
                harmony.Patch(AccessTools.Method(typeof(LootContainer), "GetRewardItem"),
                    prefix: new HarmonyMethod(typeof(HighTierMobLoot), nameof(RewardPrefix)),
                    finalizer: new HarmonyMethod(typeof(HighTierMobLoot), nameof(SpawnFinalizer)));
                T16RuntimeFixMod.SafeLog("[AEC-Mob-Loot] T16-T19 mob and boss bags: ordinary quality 5-6; advanced quality 2/3/4/5; tiered supplies enabled.");
            }
            catch (Exception ex)
            {
                T16RuntimeFixMod.SafeLog("[AEC-Mob-Loot] Installation failed: " + ex.GetBaseException().Message);
            }
        }

        public static int TierForContainer(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            var match = mobBagPattern.Match(name);
            if (!match.Success) match = bossLootPattern.Match(name);
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }

        public static int EnterScope(string name)
        {
            int previous = activeTier;
            // A nested unrelated spawn must not inherit a legendary bag scope.
            activeTier = TierForContainer(name);
            return previous;
        }

        public static void ExitScope(int previous) { activeTier = previous; }
        public static void SpawnPrefix(LootContainer __instance, out int __state)
        {
            __state = EnterScope(__instance == null ? null : __instance.Name);
        }
        public static void SpawnFinalizer(int __state) { ExitScope(__state); }
        public static void RewardPrefix(out int __state) { __state = EnterScope(null); }

        public static void QualityBounds(int tier, bool hasQuality, bool advanced, ref int min, ref int max)
        {
            if (tier < 16 || tier > 19 || !hasQuality) return;
            min = advanced ? tier - 14 : 5;
            max = advanced ? tier - 14 : 6;
        }

        public static ItemValue CreateScopedItem(int type, int minQuality, int maxQuality,
            bool createDefaultMods, string[] modsToInstall, float modChance)
        {
            var item = ItemClass.list != null && type >= 0 && type < ItemClass.list.Length
                ? ItemClass.list[type] : null;
            bool advanced = item != null && item.Properties != null &&
                item.Properties.Values.TryGetValue("PZAECAdvancedLoot", out string flag) &&
                string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase);
            QualityBounds(activeTier, item != null && item.HasQuality, advanced, ref minQuality, ref maxQuality);
            // Construct with the correct quality BEFORE native slot allocation,
            // default mods, GS stats and durability initialization take place.
            return new ItemValue(type, minQuality, maxQuality, createDefaultMods, modsToInstall, modChance);
        }

        public static IEnumerable<CodeInstruction> ItemFactoryTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var signature = new[] { typeof(int), typeof(int), typeof(int), typeof(bool), typeof(string[]), typeof(float) };
            var ctor = AccessTools.Constructor(typeof(ItemValue), signature);
            var factory = AccessTools.Method(typeof(HighTierMobLoot), nameof(CreateScopedItem), signature);
            if (ctor == null || factory == null) throw new MissingMethodException("ItemValue quality constructor");
            int matches = 0;
            foreach (var instruction in code)
                if (instruction.opcode == OpCodes.Newobj && Equals(instruction.operand, ctor)) matches++;
            if (matches != 2) throw new InvalidOperationException("Unsupported SpawnItem IL: expected two quality constructors, found " + matches);
            foreach (var instruction in code)
            {
                if (instruction.opcode != OpCodes.Newobj || !Equals(instruction.operand, ctor)) continue;
                instruction.opcode = OpCodes.Call;
                instruction.operand = factory;
            }
            return code;
        }
    }
}
