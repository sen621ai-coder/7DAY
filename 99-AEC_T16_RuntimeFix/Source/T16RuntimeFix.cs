using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AECT16RuntimeFix
{
    public sealed class T16RuntimeFixMod : IModApi
    {
        private const string HarmonyId = "pzaec.t16.runtime.fix";

        [ThreadStatic]
        private static bool ForceBloodMoonNightWeights;

        public void InitMod(Mod modInstance)
        {
            try
            {
                var harmony = new Harmony(HarmonyId);
                PatchHighTierNavigation(harmony);
                var spawnerType = AccessTools.TypeByName("AeclipseCustomZombieSpawner.SpawnDebugPatcher");
                if (spawnerType == null)
                {
                    SafeLog("[AEC-T16-Fix] Spawner type not found; high-tier navigation is active, spawner fixes were skipped.");
                    return;
                }

                PatchSignedEntityIds(harmony, spawnerType);
                PatchFollowerGroundPlacement(harmony, spawnerType);
                PatchBloodMoonBossChance(harmony, spawnerType);
                PatchBloodMoonNightWeights(harmony, spawnerType);
                PatchRewardReload(harmony);

                int aliases = InstallT16RewardAliases();
                SafeLog("[AEC-T16-Fix] Runtime fixes active. Reward aliases=" + aliases + ".");
            }
            catch (Exception ex)
            {
                SafeLog("[AEC-T16-Fix] Initialization failed: " + ex.GetBaseException().Message);
            }
        }

        private static void PatchHighTierNavigation(Harmony harmony)
        {
            var target = AccessTools.Method(typeof(EntityAlive), "updateTasks", Type.EmptyTypes);
            if (target == null)
            {
                throw new MissingMethodException(typeof(EntityAlive).FullName, "updateTasks");
            }

            harmony.Patch(
                target,
                postfix: new HarmonyMethod(typeof(T16RuntimeFixMod), nameof(HighTierNavigationPostfix)));
        }

        private sealed class NavigationState
        {
            public EntityAlive Entity;
            public Vector3 LastPosition;
            public float LastSampleTime;
            public float LastSeenTime;
            public float NextRecoveryTime;
            public bool JumpBoostApplied;
        }

        private static readonly Dictionary<int, NavigationState> NavigationStates =
            new Dictionary<int, NavigationState>();
        private static readonly List<int> ExpiredNavigationStateIds = new List<int>();
        private static float NextNavigationCleanupTime = 60f;

        // Low-frequency supervision around the existing AI. It never replaces the
        // AITask/UAITask lists, so custom boss skills remain intact.
        public static void HighTierNavigationPostfix(EntityAlive __instance)
        {
            try
            {
                if (__instance == null || __instance.isEntityRemote || __instance.IsDead() ||
                    __instance.IsMarkedForUnload())
                {
                    if (__instance != null)
                    {
                        NavigationStates.Remove(__instance.entityId);
                    }
                    return;
                }

                int tier = GetHighTier(__instance);
                if (tier == 0)
                {
                    return;
                }

                float now = Time.time;
                CleanupNavigationStates(now);
                NavigationState state;
                if (!NavigationStates.TryGetValue(__instance.entityId, out state) || state.Entity != __instance)
                {
                    state = new NavigationState
                    {
                        Entity = __instance,
                        LastPosition = __instance.position,
                        LastSampleTime = now,
                        LastSeenTime = now,
                        NextRecoveryTime = now + 1.5f
                    };
                    NavigationStates[__instance.entityId] = state;
                }
                state.LastSeenTime = now;

                if (!state.JumpBoostApplied)
                {
                    float multiplier = tier >= 19 ? 1.65f :
                        (tier == 18 ? 1.55f : (tier == 17 ? 1.45f :
                        (tier == 16 ? 1.35f : (tier == 15 ? 1.25f : 1.15f))));
                    if (__instance.jumpMaxDistance > 0f)
                    {
                        __instance.jumpMaxDistance = Mathf.Min(4.5f, __instance.jumpMaxDistance * multiplier);
                    }
                    state.JumpBoostApplied = true;
                }

                // Spread checks over entity IDs and sample only about once per second.
                if (now - state.LastSampleTime < 0.9f + (__instance.entityId & 7) * 0.035f)
                {
                    return;
                }

                Vector3 currentPosition = __instance.position;
                Vector3 delta = currentPosition - state.LastPosition;
                float movedSq = delta.x * delta.x + delta.z * delta.z;
                float elapsed = now - state.LastSampleTime;
                state.LastPosition = currentPosition;
                state.LastSampleTime = now;

                EntityAlive target = __instance.GetAttackTarget();
                if (target == null || target.IsDead() || target.IsMarkedForUnload())
                {
                    return;
                }

                Vector3 toTarget = target.position - currentPosition;
                float targetHorizontalSq = toTarget.x * toTarget.x + toTarget.z * toTarget.z;

                // Same-target refresh only changes the timer and emits no new packet.
                if (targetHorizontalSq < 10000f)
                {
                    __instance.SetAttackTarget(target, 600);
                }

                bool expectedToMove = targetHorizontalSq > 16f &&
                    (__instance.navigator == null || __instance.navigator.HasPath() ||
                     __instance.navigator.isPlanningPath());
                bool barelyMoved = movedSq < 0.09f * Mathf.Max(1f, elapsed);
                // A world-block flag usually means BreakBlock is intentionally working
                // on a wall. Preserve that route; entity-on-entity congestion is safe to
                // recover because BlockedEntity identifies the crowding case.
                bool routeIsStale = __instance.moveHelper == null ||
                    __instance.moveHelper.BlockedFlags == 0 || __instance.moveHelper.BlockedEntity != null;
                if (!expectedToMove || !barelyMoved || !routeIsStale || now < state.NextRecoveryTime)
                {
                    return;
                }

                // Discard only the stale route. The original AI chooses a fresh route
                // on its next update and retains its BreakBlock/skill behavior.
                if (__instance.navigator != null)
                {
                    __instance.navigator.clearPath();
                }
                if (__instance.moveHelper != null)
                {
                    __instance.moveHelper.ResetStuckCheck();
                }
                state.NextRecoveryTime = now + (tier >= 16 ? 2.5f : 3.25f);
            }
            catch
            {
                // AI supervision must never interrupt the vanilla update loop.
            }
        }

        private static void CleanupNavigationStates(float now)
        {
            if (now < NextNavigationCleanupTime)
            {
                return;
            }

            NextNavigationCleanupTime = now + 60f;
            ExpiredNavigationStateIds.Clear();
            foreach (var pair in NavigationStates)
            {
                NavigationState state = pair.Value;
                if (state == null || state.Entity == null || state.Entity.IsDead() ||
                    state.Entity.IsMarkedForUnload() || now - state.LastSeenTime > 120f)
                {
                    ExpiredNavigationStateIds.Add(pair.Key);
                }
            }
            foreach (int entityId in ExpiredNavigationStateIds)
            {
                NavigationStates.Remove(entityId);
            }
            ExpiredNavigationStateIds.Clear();
        }

        private static int GetHighTier(EntityAlive entity)
        {
            string name = EntityClass.GetEntityClassName(entity.entityClass);
            if (string.IsNullOrEmpty(name))
            {
                return 0;
            }

            if (name.IndexOf("Tier19", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.EndsWith("T19", StringComparison.OrdinalIgnoreCase))
            {
                return 19;
            }
            if (name.IndexOf("Tier18", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.EndsWith("T18", StringComparison.OrdinalIgnoreCase))
            {
                return 18;
            }
            if (name.IndexOf("Tier17", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.EndsWith("T17", StringComparison.OrdinalIgnoreCase))
            {
                return 17;
            }
            if (name.IndexOf("Tier16", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.EndsWith("T16", StringComparison.OrdinalIgnoreCase))
            {
                return 16;
            }
            if (name.IndexOf("Tier15", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.EndsWith("T15", StringComparison.OrdinalIgnoreCase))
            {
                return 15;
            }
            if (name.IndexOf("Tier14", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.EndsWith("T14", StringComparison.OrdinalIgnoreCase))
            {
                return 14;
            }
            return 0;
        }

        private static void PatchSignedEntityIds(Harmony harmony, Type spawnerType)
        {
            var target = AccessTools.Method(spawnerType, "ResolveEntityClassId", new[] { typeof(string) });
            if (target == null)
            {
                throw new MissingMethodException(spawnerType.FullName, "ResolveEntityClassId");
            }

            harmony.Patch(target, prefix: new HarmonyMethod(typeof(T16RuntimeFixMod), nameof(ResolveEntityClassIdPrefix)));
        }

        private static void PatchFollowerGroundPlacement(Harmony harmony, Type spawnerType)
        {
            var target = AccessTools.Method(
                spawnerType,
                "GetFollowerSpawnPositionNearLeader",
                new[] { typeof(Vector3), typeof(int), typeof(int) });
            if (target == null)
            {
                throw new MissingMethodException(spawnerType.FullName, "GetFollowerSpawnPositionNearLeader");
            }

            harmony.Patch(target, postfix: new HarmonyMethod(typeof(T16RuntimeFixMod), nameof(FollowerPositionPostfix)));
        }

        private static void PatchRewardReload(Harmony harmony)
        {
            var rewardPatchType = AccessTools.TypeByName("Exodusoul.ConfigurableKillRewards.RewardPatch");
            var setRewards = rewardPatchType == null ? null : AccessTools.Method(rewardPatchType, "SetRewards");
            if (setRewards != null)
            {
                harmony.Patch(setRewards, postfix: new HarmonyMethod(typeof(T16RuntimeFixMod), nameof(RewardReloadPostfix)));
            }
        }

        private static void PatchBloodMoonBossChance(Harmony harmony, Type spawnerType)
        {
            var runtimeType = AccessTools.TypeByName("AeclipseCustomZombieBloodmoon.BloodmoonRuntime");
            var publicCurve = runtimeType == null
                ? null
                : AccessTools.Method(runtimeType, "GetBloodMoonBossChancePercent", new[] { typeof(int) });
            if (publicCurve != null)
            {
                harmony.Patch(
                    publicCurve,
                    postfix: new HarmonyMethod(typeof(T16RuntimeFixMod), nameof(BloodMoonBossChancePostfix)));
            }

            // Keep the spawner's internal fallback on the same curve if the optional
            // Bloodmoon assembly is unavailable or its reflection call fails.
            var fallbackCurve = AccessTools.Method(
                spawnerType,
                "GetBloodMoonBossChancePercentFallback",
                new[] { typeof(int) });
            if (fallbackCurve != null)
            {
                harmony.Patch(
                    fallbackCurve,
                    postfix: new HarmonyMethod(typeof(T16RuntimeFixMod), nameof(BloodMoonBossChancePostfix)));
            }
        }

        private static void PatchBloodMoonNightWeights(Harmony harmony, Type spawnerType)
        {
            var groupHook = AccessTools.Method(spawnerType, "GetRandomFromGroupPostfix");
            var controllerType = AccessTools.TypeByName("AeclipseCustomZombieSpawner.ServerSpawnController");
            var effectiveWeight = controllerType == null
                ? null
                : AccessTools.Method(controllerType, "GetEffectiveWeight");
            if (groupHook == null || effectiveWeight == null)
            {
                throw new MissingMethodException("Blood Moon night-weight patch targets were not found.");
            }

            harmony.Patch(
                groupHook,
                prefix: new HarmonyMethod(typeof(T16RuntimeFixMod), nameof(BloodMoonGroupHookPrefix)),
                finalizer: new HarmonyMethod(typeof(T16RuntimeFixMod), nameof(BloodMoonGroupHookFinalizer)));
            harmony.Patch(
                effectiveWeight,
                prefix: new HarmonyMethod(typeof(T16RuntimeFixMod), nameof(EffectiveWeightPrefix)));
        }

        // 7DTD custom entity IDs are signed integers. The original spawner rejected every
        // negative ID even though -1 is the only "not found" sentinel.
        public static bool ResolveEntityClassIdPrefix(string className, ref int __result)
        {
            if (string.IsNullOrWhiteSpace(className))
            {
                return true;
            }

            int id = EntityClass.GetId(className);
            if (id != -1 && id != 0)
            {
                __result = id;
                return false;
            }

            return true;
        }

        // The original near-leader path copied leaderPos.y. During replacement hooks that
        // value can still be zero, which creates followers under the world. Wilderness
        // followers are placed on terrain instead.
        public static void FollowerPositionPostfix(ref Vector3 __result)
        {
            try
            {
                var gameManager = GameManager.Instance;
                var world = gameManager == null ? null : gameManager.World;
                if (world == null)
                {
                    return;
                }

                float terrainY = world.GetTerrainHeight(
                    Mathf.FloorToInt(__result.x),
                    Mathf.FloorToInt(__result.z));
                if (!float.IsNaN(terrainY) && !float.IsInfinity(terrainY))
                {
                    __result.y = terrainY + 1f;
                }
            }
            catch
            {
                // Leave the original position intact if terrain lookup is unavailable.
            }
        }

        public static void RewardReloadPostfix()
        {
            InstallT16RewardAliases();
        }

        // Overall Blood Moon boss pressure rises in clear 5,000-GS steps. T16 has
        // its own higher cap because its 21 bosses are now present in the active pool.
        public static void BloodMoonBossChancePostfix(int gameStage, ref double __result)
        {
            if (gameStage < 600)
            {
                __result = 0d;
                return;
            }

            if (gameStage >= 30001)
            {
                __result = 18d;
                return;
            }

            int step = Math.Max(0, (gameStage - 600) / 5000);
            __result = Math.Min(12d, 2d + step * 2d);
        }

        // The original spawner inferred night solely from the entity-group name. The
        // integrated groups are named PZAECBloodMoonGS..., so they incorrectly used
        // daytime biome weights even though Blood Moon spawning always happens at night.
        public static void BloodMoonGroupHookPrefix(string _sEntityGroupName, out bool __state)
        {
            __state = ForceBloodMoonNightWeights;
            if (!string.IsNullOrEmpty(_sEntityGroupName) &&
                _sEntityGroupName.IndexOf("BloodMoon", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                ForceBloodMoonNightWeights = true;
            }
        }

        public static Exception BloodMoonGroupHookFinalizer(bool __state, Exception __exception)
        {
            ForceBloodMoonNightWeights = __state;
            return __exception;
        }

        public static void EffectiveWeightPrefix(ref bool isNight)
        {
            if (ForceBloodMoonNightWeights)
            {
                isNight = true;
            }
        }

        private static int InstallT16RewardAliases()
        {
            try
            {
                var rewardPatchType = AccessTools.TypeByName("Exodusoul.ConfigurableKillRewards.RewardPatch");
                var rewardsField = rewardPatchType == null ? null : AccessTools.Field(rewardPatchType, "Rewards");
                var rewards = rewardsField == null ? null : rewardsField.GetValue(null) as IDictionary;
                if (rewards == null)
                {
                    return 0;
                }

                var aliases = BuildRewardAliases(rewards);
                int installed = 0;
                foreach (var pair in aliases)
                {
                    if (!rewards.Contains(pair.Key) && rewards.Contains(pair.Value))
                    {
                        rewards[pair.Key] = rewards[pair.Value];
                        installed++;
                    }
                }

                return installed;
            }
            catch (Exception ex)
            {
                SafeLog("[AEC-T16-Fix] Reward aliasing failed: " + ex.GetBaseException().Message);
                return 0;
            }
        }

        private static void SafeLog(string message)
        {
            Debug.Log(message);
        }

        private static Dictionary<string, string> BuildRewardAliases(IDictionary rewards)
        {
            var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in rewards)
            {
                var name = entry.Key as string;
                if (!string.IsNullOrEmpty(name) && name.EndsWith("Tier15Mythic", StringComparison.OrdinalIgnoreCase))
                {
                    string baseName = name.Substring(0, name.Length - "Tier15Mythic".Length);
                    aliases[baseName + "Tier16Transcendent"] = name;
                    aliases[baseName + "Tier17Ascendant"] = name;
                    aliases[baseName + "Tier18Eternal"] = name;
                    aliases[baseName + "Tier19Apocalyptic"] = name;
                }
            }

            foreach (var t16Boss in T16BossClasses)
            {
                string baseName = t16Boss.Substring(0, t16Boss.Length - 3);
                string source = baseName + "T15";
                aliases[t16Boss] = source;
                aliases[baseName + "T17"] = source;
                aliases[baseName + "T18"] = source;
                aliases[baseName + "T19"] = source;
            }

            return aliases;
        }

        // Explosive Eagle intentionally remains harvest-only, matching the existing T15 rules.
        private static readonly string[] T16BossClasses =
        {
            "bossAECDumdumT16",
            "RunningKamikazeT16",
            "AECDoomlordDemolishmanBossT16",
            "AECTheWitchAngelBossT16",
            "AECElectricDemonRunnerBossT16",
            "AECHammerGuardianT16",
            "AECTheMechanicianBossT16",
            "AECTheDruidBossT16",
            "AECTheExecutionerBossT16",
            "AECPartyBeachBossT16",
            "AECMykirBossT16",
            "AECHellskyliBossT16",
            "AECRockbreakerBossT16",
            "AECSingerieBossT16",
            "AECGhostBossT16",
            "AECSirenHeadBossT16",
            "AECMushroomBossT16",
            "AECTheArcherBossT16",
            "AECSheriffBossT16",
            "AECHeadlessBossT16"
        };
    }
}
