using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;

namespace AECT16RuntimeFix
{
    // Runs on the quest owner's local journal. Native game events forward remote
    // clients' requests to the server; shared journal copies must never dispatch.
    public static class QuestBossSpawn
    {
        public const string Marker = "PZAECQuestBossDispatched_v1";
        private static readonly Regex QuestIdPattern = new Regex(
            @"\Aaec_quest_T(17|18|19)_A[1-5]_clear\z",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly HashSet<Quest> Pending = new HashSet<Quest>();

        public static void Install(Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(typeof(Quest), nameof(Quest.AdvancePhase), Type.EmptyTypes);
                if (target == null) throw new MissingMethodException("Quest.AdvancePhase");
                harmony.Patch(target,
                    prefix: new HarmonyMethod(typeof(QuestBossSpawn), nameof(BeforeAdvance)),
                    postfix: new HarmonyMethod(typeof(QuestBossSpawn), nameof(AfterAdvance)));
                Debug.Log("[AEC-QuestBoss] T17-T19 clear contracts: two exact-tier bosses on rally activation; owner-only, persistent deduplication.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[AEC-QuestBoss] Installation failed: " + ex.GetBaseException().Message);
            }
        }

        public static int TierForQuestId(string id)
        {
            var match = QuestIdPattern.Match(id ?? "");
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }

        public static bool ShouldStart(string id, int previousPhase, int currentPhase,
            bool active, int sharedOwnerId, int localPlayerId, bool alreadyDispatched)
        {
            return TierForQuestId(id) != 0 && previousPhase == 2 && currentPhase == 3 && active &&
                localPlayerId >= 0 && (sharedOwnerId < 0 || sharedOwnerId == localPlayerId) && !alreadyDispatched;
        }

        // Quest.Write/Read persist DataVariables. Reserve before invoking the
        // native dispatcher so a reentrant phase change cannot enqueue twice.
        // A rejected request does not consume the quest's boss allowance.
        public static bool DispatchOnce(IDictionary<string, string> data, int tier, Func<string, bool> dispatch)
        {
            if (data == null || tier < 17 || tier > 19 || data.ContainsKey(Marker)) return false;
            string eventName = "PZAECQuestBossT" + tier;
            data[Marker] = eventName;
            try
            {
                if (dispatch(eventName)) return true;
            }
            catch
            {
                data.Remove(Marker);
                throw;
            }
            data.Remove(Marker);
            return false;
        }

        public static void BeforeAdvance(Quest __instance, out byte __state)
        {
            __state = __instance.CurrentPhase;
        }

        public static void AfterAdvance(Quest __instance, byte __state)
        {
            try
            {
                var player = __instance.OwnerJournal == null ? null : __instance.OwnerJournal.OwnerPlayer;
                if (player == null || __instance.DataVariables == null) return;
                if (!ShouldStart(__instance.ID, __state, __instance.CurrentPhase, __instance.Active,
                    __instance.SharedOwnerID, player.entityId, __instance.DataVariables.ContainsKey(Marker))) return;
                if (!GameStats.GetBool(EnumGameStats.EnemySpawnMode) || !Pending.Add(__instance)) return;
                try
                {
                    GameManager.Instance.StartCoroutine(DispatchWhenReady(__instance, player));
                }
                catch
                {
                    Pending.Remove(__instance);
                    throw;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AEC-QuestBoss] Could not start boss event: " + ex.GetBaseException().Message);
            }
        }

        private static IEnumerator DispatchWhenReady(Quest quest, EntityPlayerLocal player)
        {
            try
            {
                // Let rally/POI-reset callbacks finish before requesting a spawn.
                yield return new WaitForSeconds(2f);
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    if (player == null || player.IsDead() || !quest.Active || quest.CurrentPhase != 3 ||
                        quest.DataVariables.ContainsKey(Marker) || !GameStats.GetBool(EnumGameStats.EnemySpawnMode))
                        yield break;
                    if (TryDispatch(quest, player)) yield break;
                    yield return new WaitForSeconds(5f);
                }
                Debug.LogWarning("[AEC-QuestBoss] Spawn event unavailable for " + quest.ID +
                    "; request not marked complete. Native spawn limits were not bypassed.");
            }
            finally
            {
                Pending.Remove(quest);
            }
        }

        private static bool TryDispatch(Quest quest, EntityPlayerLocal player)
        {
            try
            {
                var manager = GameEventManager.Current;
                if (manager == null) return false;
                bool accepted = DispatchOnce(quest.DataVariables, TierForQuestId(quest.ID), eventName =>
                    manager.HandleAction(eventName, player, player, false, "", "PZAECQuestBoss:" + quest.QuestCode,
                        false, false, "", null));
                if (accepted)
                    Debug.Log("[AEC-QuestBoss] Queued " + quest.DataVariables[Marker] + " for quest=" + quest.ID +
                        " code=" + quest.QuestCode + " player=" + player.entityId + " count=2.");
                return accepted;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AEC-QuestBoss] Native event request failed: " + ex.GetBaseException().Message);
                return false;
            }
        }
    }
}
