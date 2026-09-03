using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;

namespace AECT16RuntimeFix
{
    // Quest IDs, entity IDs and native serialized buffs carry the encounter
    // identity across peers. No custom packets or client-generated trader POIs.
    public static class LegendaryAdventure
    {
        public const string SpawnMarker = "PZAECAdventureSpawn_v1";
        public const string ClearMarker = "PZAECCleared_v1";
        public const string VoucherMarker = "PZAECVoucher_v1";
        public const string OverheatBuff = "buffPZAECOverheat";
        private static readonly string[] Affixes = { "hunter", "bulwark", "storm" };
        private static readonly Regex Contract = new Regex(
            @"\A(aec_quest_T(16|17|18|19)_A[1-5]_clear)(?:_(hunter|bulwark|storm))?\z",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Challenge = new Regex(@"\APZAECChallengeT(16|17|18|19)\z",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Encounter = new Regex(@"\APZAEC(Affix|Trial)_(hunter|bulwark|storm)_T(16|17|18|19)\z",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly HashSet<Tuple<Quest, string>> Pending = new HashSet<Tuple<Quest, string>>();

        public static void Install(Harmony harmony)
        {
            try
            {
                harmony.Patch(AccessTools.Method(typeof(Quest), nameof(Quest.AdvancePhase), Type.EmptyTypes),
                    prefix: new HarmonyMethod(typeof(LegendaryAdventure), nameof(BeforeAdvance)),
                    postfix: new HarmonyMethod(typeof(LegendaryAdventure), nameof(AfterAdvance)));
                harmony.Patch(AccessTools.Method(typeof(Quest), nameof(Quest.StartQuest), new[] { typeof(bool), typeof(bool) }),
                    postfix: new HarmonyMethod(typeof(LegendaryAdventure), nameof(AfterStart)));
                harmony.Patch(AccessTools.Method(typeof(ItemActionQuest), nameof(ItemActionQuest.ExecuteInstantAction)),
                    prefix: new HarmonyMethod(typeof(LegendaryAdventure), nameof(BeforeUseVoucher)));
                harmony.Patch(AccessTools.Method(typeof(EntityAlive), nameof(EntityAlive.damageEntityLocal),
                    new[] { typeof(DamageSource), typeof(int), typeof(bool), typeof(float) }),
                    prefix: new HarmonyMethod(typeof(LegendaryAdventure), nameof(BeforeDamage)));
                T16RuntimeFixMod.SafeLog("[AEC-Adventure] Affix contracts, optional trials and encounter-only weaknesses installed.");
            }
            catch (Exception ex) { Warn("Installation failed", ex); }
        }

        public static int ContractTier(string id) { var m = Contract.Match(id ?? ""); return m.Success ? int.Parse(m.Groups[2].Value) : 0; }
        public static int ChallengeTier(string id) { var m = Challenge.Match(id ?? ""); return m.Success ? int.Parse(m.Groups[1].Value) : 0; }
        public static string Affix(string id) { var m = Contract.Match(id ?? ""); return m.Success ? m.Groups[3].Value.ToLowerInvariant() : ""; }
        public static string EncounterKind(string id) { var m = Encounter.Match(id ?? ""); return m.Success ? m.Groups[2].Value.ToLowerInvariant() : LegendaryDefense.BossKind(id); }

        public static bool MatchesPage(string id, string wanted)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(wanted)) return false;
            if (string.Equals(id, wanted, StringComparison.OrdinalIgnoreCase)) return true;
            var m = Contract.Match(id);
            return m.Success && string.Equals(m.Groups[1].Value, wanted, StringComparison.OrdinalIgnoreCase);
        }

        // First three successful positions retain plain quests. The remaining
        // three rotate all three affixes, randomized once per server-made page.
        public static string OfferId(string id, int slot, int roll)
        {
            if (ContractTier(id) == 0 || Affix(id) != "" || slot < 3 || slot > 5) return id;
            return id + "_" + Affixes[((roll % 3 + 3) % 3 + slot - 3) % 3];
        }

        public static bool IsOwner(bool active, int sharedOwner, int playerId)
        {
            return active && playerId >= 0 && (sharedOwner < 0 || sharedOwner == playerId);
        }

        public static bool IsClearTransition(string id, int previous, int current)
        {
            return ContractTier(id) != 0 && previous == 3 && current == 4;
        }

        public static bool DispatchOnce(IDictionary<string, string> data, string marker, string eventId, Func<string, bool> dispatch)
        {
            if (data == null || string.IsNullOrEmpty(marker) || string.IsNullOrEmpty(eventId) || data.ContainsKey(marker)) return false;
            data[marker] = eventId;
            try { if (dispatch(eventId)) return true; }
            catch { data.Remove(marker); throw; }
            data.Remove(marker);
            return false;
        }

        public static void BeforeAdvance(Quest __instance, out byte __state) { __state = __instance.CurrentPhase; }
        public static void AfterAdvance(Quest __instance, byte __state)
        {
            try
            {
                if (!Owned(__instance, out var player)) return;
                if (IsClearTransition(__instance.ID, __state, __instance.CurrentPhase))
                    __instance.DataVariables[ClearMarker] = "1";
                Resume(__instance, player);
            }
            catch (Exception ex) { Warn("Quest phase hook failed", ex); }
        }

        public static void AfterStart(Quest __instance)
        {
            try { if (Owned(__instance, out var player)) Resume(__instance, player); }
            catch (Exception ex) { Warn("Quest start hook failed", ex); }
        }

        private static bool Owned(Quest quest, out EntityPlayerLocal player)
        {
            player = quest?.OwnerJournal?.OwnerPlayer;
            return player != null && quest.DataVariables != null && IsOwner(quest.Active, quest.SharedOwnerID, player.entityId);
        }

        private static void Resume(Quest quest, EntityPlayerLocal player)
        {
            int tier = ContractTier(quest.ID);
            string affix = Affix(quest.ID);
            if (tier != 0 && quest.CurrentPhase == 3 && affix != "")
                Queue(quest, player, SpawnMarker, "PZAECAffix_" + affix + "_T" + tier, 3, true);
            if (tier != 0 && quest.CurrentPhase == 4 && quest.DataVariables.ContainsKey(ClearMarker))
                Queue(quest, player, VoucherMarker, "PZAECGiveVoucherT" + tier, 4, false);
            tier = ChallengeTier(quest.ID);
            if (tier != 0 && quest.CurrentPhase == 1)
                Queue(quest, player, SpawnMarker, "PZAECTrialT" + tier, 1, true);
        }

        private static void Queue(Quest quest, EntityPlayerLocal player, string marker, string eventId, int phase, bool combat)
        {
            var key = Tuple.Create(quest, marker);
            if (quest.DataVariables.ContainsKey(marker) || !Pending.Add(key)) return;
            try { GameManager.Instance.StartCoroutine(DispatchWhenReady(key, player, eventId, phase, combat)); }
            catch { Pending.Remove(key); throw; }
        }

        private static IEnumerator DispatchWhenReady(Tuple<Quest, string> key, EntityPlayerLocal player, string eventId, int phase, bool combat)
        {
            try
            {
                yield return new WaitForSeconds(combat ? 3f : 0.1f);
                for (int attempt = 0; attempt < 24; attempt++)
                {
                    var quest = key.Item1;
                    if (player == null || !quest.Active || quest.CurrentPhase != phase || quest.DataVariables.ContainsKey(key.Item2)) yield break;
                    bool ready = !player.IsDead() && (!combat || CanFight(player));
                    if (ready && TryDispatch(quest, player, key.Item2, eventId)) yield break;
                    yield return new WaitForSeconds(5f);
                }
                GameManager.ShowTooltip(player, Localization.Get("PZAECAdventureUnavailable"));
                T16RuntimeFixMod.SafeLog("[AEC-Adventure] Event unavailable; unreserved for reload retry: " + eventId);
            }
            finally { Pending.Remove(key); }
        }

        private static bool TryDispatch(Quest quest, EntityPlayerLocal player, string marker, string eventId)
        {
            try
            {
                if (GameEventManager.Current == null) return false;
                bool accepted = DispatchOnce(quest.DataVariables, marker, eventId, name =>
                    GameEventManager.Current.HandleAction(name, player, player, false, "", "PZAECAdventure:" + quest.QuestCode + ":" + marker,
                        false, false, "", null));
                if (accepted) T16RuntimeFixMod.SafeLog("[AEC-Adventure] Queued " + eventId + " quest=" + quest.QuestCode + " player=" + player.entityId);
                return accepted;
            }
            catch (Exception ex) { Warn("Event request failed", ex); return false; }
        }

        private static bool CanFight(EntityPlayerLocal player)
        {
            return GameStats.GetBool(EnumGameStats.EnemySpawnMode) && player.world != null &&
                !player.world.IsWithinTraderArea(new Vector3i(player.position));
        }

        public static bool BeforeUseVoucher(ItemActionQuest __instance, EntityAlive ent, ref bool __result)
        {
            if (ChallengeTier(__instance.QuestGiven) == 0) return true;
            var player = ent as EntityPlayerLocal;
            if (player != null && !player.IsDead() && CanFight(player)) return true;
            if (player != null) GameManager.ShowTooltip(player, Localization.Get("PZAECAdventureUseOutside"));
            __result = false;
            return false; // Leave native stack and confirmation untouched on failure.
        }

        public static int WeaknessDamage(string kind, int strength, bool playerAttack, bool head, bool melee, bool overheated)
        {
            if (!playerAttack || strength <= 0) return strength;
            int multiplier = kind == "hunter" && head ? 2 :
                (kind == "bulwark" && overheated || kind == "storm" && melee ? 3 : 1);
            return (int)Math.Min(int.MaxValue, (long)strength * multiplier);
        }

        // Runs on the peer calculating the hit, NOT server-only: the native
        // DamageEntity packet carries an already calculated DamageResponse and
        // calls ProcessDamageResponse, not damageEntityLocal, on its recipient.
        public static void BeforeDamage(EntityAlive __instance, DamageSource _damageSource, ref int _strength)
        {
            if (__instance == null || _damageSource == null || _strength <= 0 ||
                _damageSource.damageSource != EnumDamageSource.External || _damageSource.BuffClass != null ||
                _damageSource.bTrapKillXP) return;
            var entityClass = EntityClass.list[__instance.entityClass];
            string kind = EncounterKind(entityClass?.entityClassName);
            if (kind == "" || __instance.world == null || !(__instance.world.GetEntity(_damageSource.getEntityId()) is EntityPlayer)) return;
            bool head = kind == "hunter" && (_damageSource.GetEntityDamageBodyPart(__instance) & EnumBodyPartHit.Head) != 0;
            bool melee = kind == "storm" && _damageSource.ItemClass != null &&
                _damageSource.ItemClass.HasAnyTags(FastTags<TagGroup.Global>.Parse("melee"));
            bool hot = kind == "bulwark" && __instance.Buffs != null && __instance.Buffs.HasBuff(OverheatBuff);
            _strength = WeaknessDamage(kind, _strength, true, head, melee, hot);
        }

        private static void Warn(string context, Exception ex)
        {
            T16RuntimeFixMod.SafeLog("[AEC-Adventure] " + context + ": " + ex.GetBaseException().Message);
        }
    }
}
