using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace AECT16RuntimeFix
{
    // Only replaces the selector call inside the real Blood Moon spawn method.
    // No global claim-protection bypass and no shared/stale player context.
    public static class BloodMoonSpawnFix
    {
        private static MethodInfo getController;
        private static MethodInfo getBiome;
        private static MethodInfo selectTier;
        private static MethodInfo selectPool;
        private static readonly System.Random random = new System.Random();
        private static readonly object sync = new object();
        private static readonly Dictionary<string, int> spawnCounts = new Dictionary<string, int>();
        private static DateTime nextWarning = DateTime.MinValue;

        public static void Install(Harmony harmony)
        {
            try
            {
                var spawner = AccessTools.TypeByName("AeclipseCustomZombieSpawner.SpawnDebugPatcher");
                var controller = AccessTools.TypeByName("AeclipseCustomZombieSpawner.ServerSpawnController");
                if (spawner != null && controller != null)
                {
                    getController = AccessTools.Method(spawner, "TryGetController");
                    getBiome = AccessTools.Method(spawner, "GetBiomeNameAt", new[] { typeof(World), typeof(int), typeof(int) });
                    selectTier = AccessTools.Method(controller, "TrySelectEntityClassForExplicitTier");
                    selectPool = AccessTools.Method(controller, "TrySelectEntityClass");
                }
                var target = AccessTools.Method(typeof(AIDirectorBloodMoonParty), "SpawnZombie",
                    new[] { typeof(World), typeof(EntityPlayer), typeof(Vector3), typeof(Vector3) });
                if (target == null) throw new MissingMethodException("AIDirectorBloodMoonParty.SpawnZombie");
                harmony.Patch(target, transpiler: new HarmonyMethod(typeof(BloodMoonSpawnFix), nameof(Transpiler)));
                Debug.Log("[AEC-BloodMoon-Fix] Current BloodMoonParty selector connected; target-player GS, exact T16-T19 pools, 18% high-tier bosses.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[AEC-BloodMoon-Fix] Installation failed; XML stage fallback remains active: " + ex.GetBaseException().Message);
            }
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var original = AccessTools.Method(typeof(EntityGroups), "GetRandomEntityFromGroupMaxTier",
                new[] { typeof(string), typeof(EntityClass.EntityTierTypes), typeof(int).MakeByRefType(),
                    typeof(bool), typeof(bool), typeof(GameRandom) });
            var replacement = AccessTools.Method(typeof(BloodMoonSpawnFix), nameof(SelectForTarget));
            int index = -1;
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode == OpCodes.Call && Equals(code[i].operand, original))
                {
                    if (index >= 0) throw new InvalidOperationException("Ambiguous Blood Moon selector call sites.");
                    index = i;
                }
            }
            if (original == null || replacement == null || index < 0)
                throw new InvalidOperationException("Current Blood Moon max-tier selector call not found.");
            // SpawnZombie is an instance method: arg 1 = world, arg 2 = target.
            // Keep the six original selector arguments and push its actual target.
            var loadTarget = new CodeInstruction(OpCodes.Ldarg_2);
            loadTarget.labels.AddRange(code[index].labels);
            code[index].labels.Clear();
            code[index].operand = replacement;
            code.Insert(index, loadTarget);
            return code;
        }

        public static int TierForGameStage(int gs)
        {
            if (gs >= 300000) return 19;
            if (gs >= 270000) return 18;
            if (gs >= 240000) return 17;
            if (gs >= 180000) return 16;
            if (gs >= 25000) return 15;
            if (gs >= 22000) return 14;
            if (gs >= 18000) return 13;
            if (gs >= 14000) return 12;
            if (gs >= 10500) return 11;
            if (gs >= 7500) return 10;
            if (gs >= 5000) return 9;
            if (gs >= 3200) return 8;
            if (gs >= 2000) return 7;
            if (gs >= 1200) return 6;
            return gs >= 600 ? 5 : 0;
        }

        public static string FallbackGroupForGameStage(int gs)
        {
            int tier = TierForGameStage(gs);
            if (tier >= 16) return "PZAECBloodMoonT" + tier + "Fallback";
            int stage = 1;
            foreach (int candidate in new[] { 25, 50, 100, 200, 400, 600, 900, 1200, 1600,
                2000, 2600, 3200, 4100, 5000, 6200, 7500, 9000, 10500, 12250,
                14000, 16000, 18000, 20000, 22000, 23500, 25000, 27000, 28500, 30000 })
            {
                if (gs < candidate) break;
                stage = candidate;
            }
            return "PZAECBloodMoonGS" + stage.ToString("D6");
        }

        public static int SelectForTarget(string group, EntityClass.EntityTierTypes maxTier,
            ref int lastClassId, bool isEnemy, bool isAnimal, GameRandom gameRandom, EntityPlayer target)
        {
            int gs = -1;
            try
            {
                if (target != null)
                {
                    gs = target.gameStage; // Never PartyGameStage or party average.
                    int tier = TierForGameStage(gs);
                    string biome = "";
                    if (getBiome != null)
                        biome = getBiome.Invoke(null, new object[] { target.world,
                            Mathf.FloorToInt(target.position.x), Mathf.FloorToInt(target.position.z) }) as string ?? "";
                    if (tier > 0 && TrySelect(gs, tier, biome, maxTier, isEnemy, isAnimal, out int id, out bool boss))
                    {
                        int siegeTier = BloodMoonSiege.TierForGameStage(gs);
                        if (!boss && siegeTier > 0)
                            id = BloodMoonSiege.SelectReplacement(id, siegeTier, target, isEnemy, isAnimal, gameRandom);
                        lastClassId = id; // Custom IDs are signed; only -1 means missing.
                        LogSelection(target.entityId, gs, tier, biome, boss, id, "rules");
                        return id;
                    }
                    if (tier > 0) Warn("No eligible dynamic candidate at GS " + gs + "; using target-tier XML fallback.");
                }
            }
            catch (Exception ex)
            {
                Warn("Dynamic selection failed: " + ex.GetBaseException().Message);
            }
            // Correct tier even if the optional controller has not loaded yet.
            // Below T16 retain the existing XML mixes, selected by target GS.
            string fallback = gs >= 0 ? FallbackGroupForGameStage(gs) : group;
            if (!EntityGroups.list.ContainsKey(fallback))
            {
                Warn("Missing fallback group " + fallback + "; preserving original group " + group);
                fallback = group;
            }
            int result = EntityGroups.GetRandomEntityFromGroupMaxTier(fallback, maxTier,
                ref lastClassId, isEnemy, isAnimal, gameRandom);
            int fallbackTier = BloodMoonSiege.TierForGameStage(gs);
            if (fallbackTier > 0 && result != -1 && target != null)
            {
                result = BloodMoonSiege.SelectReplacement(result, fallbackTier, target, isEnemy, isAnimal, gameRandom);
                lastClassId = result;
            }
            if (fallbackTier > 0 && result != -1 && target != null)
                LogSelection(target.entityId, gs, TierForGameStage(gs), "all", false, result, "xml-fallback");
            return result;
        }

        private static bool TrySelect(int gs, int tier, string biome, EntityClass.EntityTierTypes maxTier,
            bool isEnemy, bool isAnimal, out int classId, out bool boss)
        {
            classId = -1;
            boss = false;
            if (getController == null || selectTier == null || selectPool == null) return false;
            var controllerArgs = new object[] { null };
            if (!(bool)getController.Invoke(null, controllerArgs) || controllerArgs[0] == null) return false;
            double chance = 0;
            T16RuntimeFixMod.BloodMoonBossChancePostfix(gs, ref chance);
            lock (sync)
            {
                boss = random.NextDouble() * 100d < chance;
                // Do not roll again after an invalid candidate: preserve the boss budget.
                for (int attempt = 0; attempt < 24; attempt++)
                {
                    int rulesGs = Math.Min(gs, 999999);
                    object[] args = tier >= 16
                        ? new object[] { rulesGs, tier, biome, true, random, boss, boss, false, null }
                        : new object[] { rulesGs, biome, true, random, boss, boss, null, false };
                    var method = tier >= 16 ? selectTier : selectPool;
                    if (!(bool)method.Invoke(controllerArgs[0], args)) return false;
                    string name = args[tier >= 16 ? 8 : 6] as string;
                    if (string.IsNullOrEmpty(name)) continue;
                    int id = EntityClass.GetId(name);
                    if (id == -1) continue;
                    var entity = EntityClass.GetEntityClass(id);
                    if (entity == null || (isEnemy && !entity.bIsEnemyEntity) || (isAnimal && !entity.bIsAnimalEntity)) continue;
                    // Respect the sandbox max-tier setting without silently selecting
                    // a lower PreviousTier entity under a high-tier AEC name.
                    if (entity.EntityTier > maxTier) continue;
                    classId = id;
                    return true;
                }
            }
            return false;
        }

        private static void LogSelection(int player, int gs, int tier, string biome, bool boss, int id, string mode)
        {
            lock (sync)
            {
                string key = player + ":" + tier + ":" + mode;
                spawnCounts.TryGetValue(key, out int count);
                spawnCounts[key] = ++count;
                if (count <= 3 || count % 100 == 0 || boss)
                    Debug.Log("[AEC-BloodMoon-Fix] target=" + player + " gs=" + gs + " tier=T" + tier +
                        " mode=" + mode + " boss=" + (mode == "rules" ? boss.ToString() : "unknown") +
                        " biome=" + biome + " class=" + EntityClass.GetEntityClassName(id) + " count=" + count);
            }
        }

        private static void Warn(string reason)
        {
            lock (sync)
            {
                if (DateTime.UtcNow < nextWarning) return;
                nextWarning = DateTime.UtcNow.AddSeconds(20);
                Debug.LogWarning("[AEC-BloodMoon-Fix] " + reason);
            }
        }
    }
}
