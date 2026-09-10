using System;
using GameEvent.SequenceActions;
using HarmonyLib;

namespace AECT16RuntimeFix
{
    // Observe the fresh voucher -> spawn -> kill -> objective path without
    // relaxing encounter ownership or crediting unrelated bosses.
    public static class LegendaryTrialDiagnostics
    {
        public static bool IsTrialEntity(string id)
        {
            if (string.IsNullOrEmpty(id) || !id.StartsWith("PZAECTrial_", StringComparison.OrdinalIgnoreCase)) return false;
            foreach (int tier in new[] { 16, 17, 18, 19 })
                foreach (string kind in new[] { "hunter", "bulwark", "storm" })
                    if (string.Equals(id, "PZAECTrial_" + kind + "_T" + tier, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static void Install(Harmony harmony)
        {
            try
            {
                harmony.Patch(AccessTools.Method(typeof(ActionBaseSpawn), "SpawnEntity"),
                    postfix: new HarmonyMethod(typeof(LegendaryTrialDiagnostics), nameof(AfterSpawn)));
                harmony.Patch(AccessTools.Method(typeof(GameManager), nameof(GameManager.AwardKill)),
                    postfix: new HarmonyMethod(typeof(LegendaryTrialDiagnostics), nameof(AfterAwardKill)));
            }
            catch (Exception ex) { Error(ex); }
        }

        public static void AfterSpawn(ActionBaseSpawn __instance, Entity __result)
        {
            try
            {
                if (__result == null || !IsTrialEntity(__result.EntityClass?.entityClassName)) return;
                var owner = __instance.Owner;
                Log("spawn entity=" + __result.entityId + " class=" + __result.EntityClass.entityClassName +
                    " event=" + owner?.Name + " request=" + owner?.ExtraData + " tag=" + __result.spawnByName +
                    " requester=" + owner?.Requester?.entityId);
            }
            catch (Exception ex) { Error(ex); }
        }

        public static void AfterAwardKill(EntityAlive __0, EntityAlive __1)
        {
            try
            {
                if (__1 == null || !IsTrialEntity(__1.EntityClass?.entityClassName)) return;
                Log("award entity=" + __1.entityId + " class=" + __1.EntityClass.entityClassName +
                    " tag=" + __1.spawnByName + " killer=" + __0?.entityId + " remoteKiller=" + __0?.isEntityRemote);
            }
            catch (Exception ex) { Error(ex); }
        }

        public static void AfterObjectiveKill(ObjectiveEntityKill __instance, EntityAlive killedEntity)
        {
            try
            {
                var quest = __instance.OwnerQuest;
                if (LegendaryAdventure.ChallengeTier(quest?.ID) == 0 || killedEntity == null ||
                    !IsTrialEntity(killedEntity.EntityClass?.entityClassName) ||
                    !string.Equals(__instance.ID, killedEntity.EntityClass.entityClassName, StringComparison.OrdinalIgnoreCase)) return;
                var player = quest.OwnerJournal?.OwnerPlayer;
                string marker = null;
                quest.DataVariables?.TryGetValue(LegendaryAdventure.SpawnMarker, out marker);
                bool eligible = LegendaryAdventure.CanCountTrialKill(quest.ID, quest.QuestCode, quest.Active,
                    quest.CurrentPhase, quest.SharedOwnerID, player?.entityId ?? -1, quest.DataVariables, killedEntity.spawnByName);
                Log("objective entity=" + killedEntity.entityId + " quest=" + quest.ID + " code=" + quest.QuestCode +
                    " player=" + player?.entityId + " sharedOwner=" + quest.SharedOwnerID + " phase=" + quest.CurrentPhase +
                    " active=" + quest.Active + " tag=" + killedEntity.spawnByName + " marker=" + marker +
                    " eligibleNow=" + eligible + " count=" + __instance.CurrentValue + " complete=" + __instance.Complete);
            }
            catch (Exception ex) { Error(ex); }
        }

        private static void Log(string message) { T16RuntimeFixMod.SafeLog("[AEC-TrialTrace] " + message); }
        private static void Error(Exception ex) { Log("Diagnostic unavailable: " + ex.GetType().Name); }
    }
}
