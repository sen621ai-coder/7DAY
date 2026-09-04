using System;
using HarmonyLib;
using UnityEngine;

namespace AECT16RuntimeFix
{
    /// <summary>
    /// Recovers the local player when death overlaps a teleport/respawn state.
    /// Vanilla EntityPlayerLocal.OnDeathUpdate only starts the death respawn
    /// while Spawned is true. A death during an unfinished teleport can leave
    /// Spawned false forever, with the death window open and no spawn choices.
    /// </summary>
    public static class LocalRespawnRecovery
    {
        private const float RecoveryDelaySeconds = 12f;

        private static int trackedEntityId = -1;
        private static float deathObservedAt;
        private static bool deathRespawnStarted;
        private static bool recoveryAttempted;

        public static void Install(Harmony harmony)
        {
            try
            {
                var death = AccessTools.Method(typeof(EntityPlayerLocal), nameof(EntityPlayerLocal.OnEntityDeath));
                var update = AccessTools.Method(typeof(EntityPlayerLocal), nameof(EntityPlayerLocal.Update));
                var respawn = AccessTools.Method(typeof(EntityPlayerLocal), nameof(EntityPlayerLocal.Respawn),
                    new[] { typeof(RespawnType) });
                if (death == null || update == null || respawn == null)
                    throw new MissingMethodException("EntityPlayerLocal death/respawn methods");

                harmony.Patch(death,
                    postfix: new HarmonyMethod(typeof(LocalRespawnRecovery), nameof(AfterLocalDeath)));
                harmony.Patch(update,
                    postfix: new HarmonyMethod(typeof(LocalRespawnRecovery), nameof(AfterLocalUpdate)));
                harmony.Patch(respawn,
                    prefix: new HarmonyMethod(typeof(LocalRespawnRecovery), nameof(BeforeLocalRespawn)));
                T16RuntimeFixMod.SafeLog("[AEC-Respawn-Fix] Local death/teleport recovery active.");
            }
            catch (Exception ex)
            {
                T16RuntimeFixMod.SafeLog("[AEC-Respawn-Fix] Patch installation failed: " +
                    ex.GetBaseException().Message);
            }
        }

        public static void AfterLocalDeath(EntityPlayerLocal __instance)
        {
            try
            {
                BeginTracking(__instance);
                T16RuntimeFixMod.SafeLog("[AEC-Respawn-Fix] Local death observed: entity=" +
                    (__instance == null ? -1 : __instance.entityId) +
                    " spawned=" + (__instance != null && __instance.Spawned) +
                    " deathTicks=" + (__instance == null ? -1 : __instance.GetDeathTime()) + ".");
            }
            catch (Exception ex)
            {
                Warn("Death diagnostic failed", ex);
            }
        }

        public static void BeforeLocalRespawn(EntityPlayerLocal __instance, RespawnType _respawnReason)
        {
            if (__instance != null && _respawnReason == RespawnType.Died &&
                trackedEntityId == __instance.entityId)
            {
                deathRespawnStarted = true;
            }
        }

        public static void AfterLocalUpdate(EntityPlayerLocal __instance)
        {
            try
            {
                if (__instance == null || !__instance.IsDead())
                {
                    Reset();
                    return;
                }

                if (trackedEntityId != __instance.entityId)
                    BeginTracking(__instance);

                LocalPlayerUI ui = __instance.PlayerUI;
                if ((ui != null && XUiC_SpawnSelectionWindow.IsOpenInUI(ui)) ||
                    deathRespawnStarted || recoveryAttempted)
                    return;

                if (Time.realtimeSinceStartup - deathObservedAt < RecoveryDelaySeconds)
                    return;

                recoveryAttempted = true;
                T16RuntimeFixMod.SafeLog("[AEC-Respawn-Fix] Recovering stalled local death: entity=" +
                    __instance.entityId + " spawned=" + __instance.Spawned +
                    " deathTicks=" + __instance.GetDeathTime() +
                    " stayTicks=" + __instance.GetTimeStayAfterDeath() + ".");

                // Use the native transition so bedroll/backpack/random choices,
                // inventory rules and server notification remain unchanged.
                __instance.Respawn(RespawnType.Died);
            }
            catch (Exception ex)
            {
                recoveryAttempted = true;
                Warn("Recovery failed", ex);
            }
        }

        private static void BeginTracking(EntityPlayerLocal player)
        {
            trackedEntityId = player == null ? -1 : player.entityId;
            deathObservedAt = Time.realtimeSinceStartup;
            deathRespawnStarted = false;
            recoveryAttempted = false;
        }

        private static void Reset()
        {
            trackedEntityId = -1;
            deathObservedAt = 0f;
            deathRespawnStarted = false;
            recoveryAttempted = false;
        }

        private static void Warn(string context, Exception ex)
        {
            T16RuntimeFixMod.SafeLog("[AEC-Respawn-Fix] " + context + ": " +
                ex.GetBaseException().Message);
        }
    }
}
