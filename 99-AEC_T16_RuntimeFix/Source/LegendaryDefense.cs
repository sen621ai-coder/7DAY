using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using GameEvent.SequenceActions;
using HarmonyLib;
using UnityEngine;

namespace AECT16RuntimeFix
{
    // A voluntary, single-session encounter. Quest objectives own progression
    // and rewards; server-side native spawn actions obey a revocable lease.
    public static class LegendaryDefense
    {
        public const float Radius = 45f, Height = 60f;
        public const double Preparation = 20, Intermission = 15, RetreatGrace = 15, WaveLimit = 900;
        public const string Lease = "buffPZAECDefenseLease";
        public const string Scope = "PZAECDefense";
        private static readonly Regex QuestPattern = new Regex(@"\APZAECDefenseT(16|17|18|19)\z", RegexOptions.CultureInvariant);
        private static readonly Regex EventPattern = new Regex(@"\APZAECDefenseT(16|17|18|19)W([1-3])\z", RegexOptions.CultureInvariant);
        private static readonly Regex BossPattern = new Regex(@"\APZAECDefense_(bulwark|storm)_T(16|17|18|19)_W3\z", RegexOptions.CultureInvariant);
        private static readonly Regex EnemyPattern = new Regex(@"\APZAECDefense_(runner|spider|cop|biker|demo|spitter|wolf|wight|bulwark|storm)_T(16|17|18|19)_W([1-3])\z", RegexOptions.CultureInvariant);
        private static readonly Dictionary<Quest, Session> Sessions = new Dictionary<Quest, Session>();

        public sealed class WaveClock
        {
            public int Wave { get; private set; }
            public double ReadyAt { get; private set; }
            public double Deadline { get; private set; }
            public double OutsideSince { get; private set; } = -1;
            public WaveClock(int wave, double now) { MoveTo(wave, now); }
            public void MoveTo(int wave, double now)
            {
                if (wave < 1 || wave > 3 || Wave != 0 && wave != Wave + 1)
                    throw new InvalidOperationException("Invalid defense wave transition");
                Wave = wave;
                ReadyAt = now + (wave == 1 ? Preparation : Intermission);
                Deadline = ReadyAt + WaveLimit;
            }
            public string Check(double now, bool alive, bool enabled, bool protectedArea, bool within)
            {
                if (!alive) return "PZAECDefenseDeath";
                if (!enabled || protectedArea) return "PZAECDefenseUnavailable";
                if (now >= Deadline) return "PZAECDefenseTimeout";
                if (within) OutsideSince = -1;
                else if (OutsideSince < 0) OutsideSince = now;
                return OutsideSince >= 0 && now - OutsideSince >= RetreatGrace ? "PZAECDefenseRetreated" : null;
            }
        }

        private sealed class Session
        {
            public Quest Quest;
            public EntityPlayerLocal Player;
            public Vector3 Anchor;
            public WaveClock Clock;
            public double NextAttempt, NextLease;
            public bool WarnedOutside;
        }

        public static void Install(Harmony harmony)
        {
            try
            {
                harmony.Patch(AccessTools.Method(typeof(Quest), nameof(Quest.StartQuest), new[] { typeof(bool), typeof(bool) }),
                    postfix: new HarmonyMethod(typeof(LegendaryDefense), nameof(AfterStart)));
                harmony.Patch(AccessTools.Method(typeof(Quest), nameof(Quest.CloseQuest)),
                    prefix: new HarmonyMethod(typeof(LegendaryDefense), nameof(BeforeClose)));
                harmony.Patch(AccessTools.Method(typeof(ItemActionQuest), nameof(ItemActionQuest.ExecuteInstantAction)),
                    prefix: new HarmonyMethod(typeof(LegendaryDefense), nameof(BeforeUse)));
                harmony.Patch(AccessTools.Method(typeof(XUiC_QuestOfferWindow), "btnAccept_OnPress"),
                    prefix: new HarmonyMethod(typeof(LegendaryDefense), nameof(BeforeAccept)));
                harmony.Patch(AccessTools.Method(typeof(ObjectiveEntityKill), "Current_EntityKill"),
                    prefix: new HarmonyMethod(typeof(LegendaryDefense), nameof(BeforeKill)));
                harmony.Patch(AccessTools.Method(typeof(ActionBaseSpawn), nameof(ActionBaseSpawn.OnPerformAction)),
                    prefix: new HarmonyMethod(typeof(LegendaryDefense), nameof(BeforeNativeSpawn)));
                harmony.Patch(AccessTools.Method(typeof(NetPackageGameEventResponse), nameof(NetPackageGameEventResponse.ProcessPackage)),
                    postfix: new HarmonyMethod(typeof(LegendaryDefense), nameof(AfterReply)));
                Log("Three-wave voluntary defense enabled; native spawn lease and fixed-origin gate installed.");
            }
            catch (Exception ex) { Log("Installation failed: " + ex.GetBaseException().Message); }
        }

        public static int Tier(string id) { var m = QuestPattern.Match(id ?? ""); return m.Success ? int.Parse(m.Groups[1].Value) : 0; }
        public static string BossKind(string id) { var m = BossPattern.Match(id ?? ""); return m.Success ? m.Groups[1].Value : ""; }
        public static int EnemyTier(string id)
        {
            var m = EnemyPattern.Match(id ?? "");
            if (!m.Success) return 0;
            string role = m.Groups[1].Value, wave = m.Groups[3].Value;
            bool valid = wave == "1" ? role == "runner" || role == "spider" || role == "cop" :
                wave == "2" ? role == "biker" || role == "demo" || role == "spitter" || role == "wolf" :
                role == "wight" || role == "cop" || role == "bulwark" || role == "storm";
            return valid ? int.Parse(m.Groups[2].Value) : 0;
        }
        public static string WaveMarker(int wave) { return Scope + "Wave" + wave + "_v1"; }
        public static string EventId(int tier, int wave) { return tier >= 16 && tier <= 19 && wave >= 1 && wave <= 3 ? Scope + "T" + tier + "W" + wave : null; }
        public static bool Within(float x, float y, float z, float ax, float ay, float az)
        {
            if (!Finite(x) || !Finite(y) || !Finite(z) || !Finite(ax) || !Finite(ay) || !Finite(az)) return false;
            double dx = (double)x - ax, dz = (double)z - az;
            return dx * dx + dz * dz <= Radius * Radius && Math.Abs((double)y - ay) <= Height;
        }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static bool Inside(Vector3 position, Vector3 anchor) { return Within(position.x, position.y, position.z, anchor.x, anchor.y, anchor.z); }
        public static bool HasActive(IEnumerable<string> activeIds)
        {
            if (activeIds != null) foreach (string id in activeIds) if (Tier(id) != 0) return true;
            return false;
        }
        public static string Request(int questCode, int tier, int wave)
        {
            return EventId(tier, wave) == null ? null : Scope + ":" + questCode.ToString(CultureInfo.InvariantCulture) + ":" + tier + ":" + wave;
        }
        // Two 16-bit float CVars carry the full signed native quest ID losslessly.
        public static int High(int code) { return (int)((uint)code >> 16); }
        public static int Low(int code) { return code & 65535; }
        public static bool ScopeMatches(string eventName, string request, float high, float low, float wave)
        {
            var match = EventPattern.Match(eventName ?? "");
            if (!match.Success || !Finite(high) || !Finite(low) || high < 0 || high > 65535 || low < 0 || low > 65535 || high != (int)high || low != (int)low) return false;
            int tier = int.Parse(match.Groups[1].Value), eventWave = int.Parse(match.Groups[2].Value);
            int code = unchecked((int)(((uint)high << 16) | (uint)low));
            return code != 0 && wave == eventWave && string.Equals(request, Request(code, tier, eventWave), StringComparison.Ordinal);
        }
        public static bool ApplyReply(IDictionary<string, string> data, int code, int tier, int wave, string eventId, string tag, bool approved)
        {
            string marker = WaveMarker(wave);
            if (data == null || EventId(tier, wave) == null || eventId != EventId(tier, wave) || tag != Request(code, tier, wave) ||
                !data.TryGetValue(marker, out var queued) || queued != eventId) return false;
            if (approved) { data[marker + "Approved"] = "1"; return true; }
            if (data.ContainsKey(marker + "Approved")) return false;
            return data.Remove(marker); // The server explicitly rejected enqueueing; safe to retry, no new allowance.
        }
        public static void AfterReply(NetPackageGameEventResponse __instance)
        {
            if (__instance.responseType != NetPackageGameEventResponse.ResponseTypes.Denied &&
                __instance.responseType != NetPackageGameEventResponse.ResponseTypes.Approved) return;
            foreach (var s in Sessions.Values)
            {
                if (!s.Quest.Active || s.Player.entityId != __instance.targetEntityID) continue;
                ApplyReply(s.Quest.DataVariables, s.Quest.QuestCode, Tier(s.Quest.ID), s.Clock.Wave, __instance.eventName,
                    __instance.tag, __instance.responseType == NetPackageGameEventResponse.ResponseTypes.Approved);
            }
        }

        private static string CanStart(EntityPlayerLocal player)
        {
            if (player == null || player.IsDead() || player.world == null || player.Buffs == null ||
                !GameStats.GetBool(EnumGameStats.EnemySpawnMode) || player.world.IsWithinTraderArea(new Vector3i(player.position)) ||
                !player.world.CanPlaceBlockAt(new Vector3i(player.position), null)) return "PZAECDefenseUseOutside";
            foreach (var quest in player.QuestJournal.quests)
                if (quest != null && quest.Active && Tier(quest.ID) != 0) return "PZAECDefenseAlreadyActive";
            return null;
        }
        public static bool BeforeUse(ItemActionQuest __instance, EntityAlive ent, ref bool __result)
        {
            if (Tier(__instance.QuestGiven) == 0) return true;
            var player = ent as EntityPlayerLocal;
            string reason = CanStart(player);
            if (reason == null) return true;
            if (player != null) Tell(player, reason);
            __result = false;
            return false;
        }
        public static bool BeforeAccept(XUiC_QuestOfferWindow __instance)
        {
            if (Tier(__instance.Quest?.ID) == 0) return true;
            var player = __instance.xui?.playerUI?.entityPlayer;
            string reason = CanStart(player);
            if (reason == null) return true;
            if (player != null) Tell(player, reason);
            return false; // Before native decrement: moved/died/another tier active while offer was open.
        }

        public static void AfterStart(Quest __instance, bool newQuest)
        {
            if (Tier(__instance.ID) == 0 || !Owned(__instance, out var player)) return;
            try
            {
                if (!newQuest)
                {
                    Revoke(player, __instance.QuestCode);
                    GameManager.Instance.StartCoroutine(FailLoaded(__instance, player));
                    return;
                }
                if (Sessions.ContainsKey(__instance)) return;
                var session = new Session { Quest = __instance, Player = player, Anchor = player.position, Clock = new WaveClock(1, Time.time) };
                __instance.SetPositionData(Quest.PositionDataTypes.Location, session.Anchor);
                __instance.DataVariables[Scope + "Started_v1"] = "1";
                Sessions.Add(__instance, session);
                Publish(session);
                Tell(player, "PZAECDefensePreparation");
                GameManager.Instance.StartCoroutine(Run(session));
            }
            catch (Exception ex)
            {
                Revoke(player, __instance.QuestCode);
                Sessions.Remove(__instance);
                Log("Start failed: " + ex.GetBaseException().Message);
                GameManager.Instance.StartCoroutine(FailLoaded(__instance, player));
            }
        }
        private static bool Owned(Quest quest, out EntityPlayerLocal player)
        {
            player = quest?.OwnerJournal?.OwnerPlayer;
            return player != null && LegendaryAdventure.IsOwner(quest.Active, quest.SharedOwnerID, player.entityId);
        }
        private static IEnumerator FailLoaded(Quest quest, EntityPlayerLocal player)
        {
            yield return new WaitForSeconds(2);
            if (quest.Active) Fail(quest, player, "PZAECDefenseResumeFailed");
        }
        private static IEnumerator Run(Session s)
        {
            try
            {
                // StartQuest returns before QuestJournal.AddQuest appends the
                // quest. Wait once, then also honor force-removal/world unload.
                yield return new WaitForSeconds(1);
                while (Owned(s.Quest, out var player) && player.world == GameManager.Instance.World && player.QuestJournal.quests.Contains(s.Quest))
                {
                    try { if (!Tick(s)) yield break; }
                    catch (Exception ex)
                    {
                        Log("Defense stopped safely: " + ex.GetBaseException().Message);
                        Fail(s.Quest, player, "PZAECDefenseUnavailable");
                        yield break;
                    }
                    yield return new WaitForSeconds(1);
                }
            }
            finally
            {
                Revoke(s.Player, s.Quest.QuestCode);
                Sessions.Remove(s.Quest);
            }
        }
        private static bool Tick(Session s)
        {
            double now = Time.time;
            var q = s.Quest; var player = s.Player;
            // The final kill may have advanced the native phase just before the
            // previous deadline; do not fail the new wave against the old timer.
            if (q.CurrentPhase != s.Clock.Wave)
            {
                s.Clock.MoveTo(q.CurrentPhase, now);
                s.NextAttempt = 0;
                Publish(s);
                Tell(player, "PZAECDefenseIntermission");
            }
            string reason = Check(s, now);
            if (reason != null) { Fail(q, player, reason); return false; }
            bool inside = Inside(player.position, s.Anchor);
            if (!inside && !s.WarnedOutside) { Tell(player, "PZAECDefenseReturn"); s.WarnedOutside = true; }
            if (inside) s.WarnedOutside = false;
            if (now >= s.NextLease)
            {
                RenewLease(player);
                s.NextLease = now + 3;
            }
            int wave = s.Clock.Wave;
            if (!inside || now < s.Clock.ReadyAt || now < s.NextAttempt || q.DataVariables.ContainsKey(WaveMarker(wave))) return true;
            s.NextAttempt = now + 5;
            if (GameEventManager.Current == null) return true;
            bool accepted = LegendaryAdventure.DispatchOnce(q.DataVariables, WaveMarker(wave), EventId(Tier(q.ID), wave), id =>
                GameEventManager.Current.HandleAction(id, player, player, false, Request(q.QuestCode, Tier(q.ID), wave),
                    Request(q.QuestCode, Tier(q.ID), wave), false, false, "", null));
            if (accepted) { Tell(player, "PZAECDefenseWave" + wave); Log("Queued " + EventId(Tier(q.ID), wave) + " code=" + q.QuestCode); }
            return true;
        }
        private static string Check(Session s, double now)
        {
            var player = s.Player;
            if (player == null || player.world == null) return "PZAECDefenseUnavailable";
            return s.Clock.Check(now, !player.IsDead(), GameStats.GetBool(EnumGameStats.EnemySpawnMode),
                player.world.IsWithinTraderArea(new Vector3i(player.position)), Inside(player.position, s.Anchor));
        }
        private static void Publish(Session s)
        {
            var buffs = s.Player.Buffs;
            // Local players do NOT send ordinary CVars with just _netSync=true.
            // Force the native packet path so joining clients publish their scope.
            buffs.SetCustomVar(Scope + "High", High(s.Quest.QuestCode), _forceSendToClients: true);
            buffs.SetCustomVar(Scope + "Low", Low(s.Quest.QuestCode), _forceSendToClients: true);
            buffs.SetCustomVar(Scope + "X", s.Anchor.x, _forceSendToClients: true);
            buffs.SetCustomVar(Scope + "Y", s.Anchor.y, _forceSendToClients: true);
            buffs.SetCustomVar(Scope + "Z", s.Anchor.z, _forceSendToClients: true);
            buffs.SetCustomVar(Scope + "Wave", s.Clock.Wave, _forceSendToClients: true);
            RenewLease(s.Player);
            s.NextLease = Time.time + 3;
        }
        private static void RenewLease(EntityPlayerLocal player)
        {
            // This has no combat effects. Do not let combat BuffResistance reject
            // the bookkeeping lease; apply locally then use the native buff packet.
            player.Buffs.AddBuff(Lease, player.entityId, false);
            if (!player.Buffs.HasBuff(Lease)) throw new InvalidOperationException("Defense lease unavailable");
            player.Buffs.AddBuffNetwork(Lease, -1f, Vector3i.zero, player.entityId);
        }
        private static void Revoke(EntityPlayerLocal player, int code)
        {
            if (player == null || player.Buffs == null || player.world != GameManager.Instance?.World ||
                player.Buffs.GetCustomVar(Scope + "High") != High(code) || player.Buffs.GetCustomVar(Scope + "Low") != Low(code)) return;
            player.Buffs.RemoveBuff(Lease);
            player.Buffs.SetCustomVar(Scope + "Wave", 0, _forceSendToClients: true);
        }

        public static bool BeforeNativeSpawn(ActionBaseSpawn __instance, ref BaseAction.ActionCompleteStates __result)
        {
            var owner = __instance.Owner;
            if (owner == null || !EventPattern.IsMatch(owner.Name ?? "")) return true;
            var player = owner.Target as EntityPlayer;
            __result = BaseAction.ActionCompleteStates.Complete;
            if (player == null || player.IsDead() || player.world == null || player.Buffs == null ||
                !GameStats.GetBool(EnumGameStats.EnemySpawnMode) || !player.Buffs.HasBuff(Lease) ||
                owner.Requester == null || owner.Requester.entityId != player.entityId ||
                !ScopeMatches(owner.Name, owner.Tag, player.Buffs.GetCustomVar(Scope + "High"), player.Buffs.GetCustomVar(Scope + "Low"), player.Buffs.GetCustomVar(Scope + "Wave")) ||
                player.world.IsWithinTraderArea(new Vector3i(player.position))) return false;
            var anchor = new Vector3(player.Buffs.GetCustomVar(Scope + "X"), player.Buffs.GetCustomVar(Scope + "Y"), player.Buffs.GetCustomVar(Scope + "Z"));
            if (!Inside(player.position, anchor))
            {
                __result = BaseAction.ActionCompleteStates.InComplete; // Pause during the retreat grace, never follow the owner out.
                return false;
            }
            // NearPosition uses this cached origin for every entry, not the last
            // entity's position (native WanderingHorde can drift after each spawn).
            owner.TargetPosition = anchor;
            __instance.position = anchor;
            return true;
        }
        public static bool BeforeKill(ObjectiveEntityKill __instance, EntityAlive killedEntity)
        {
            var q = __instance.OwnerQuest;
            if (Tier(q?.ID) == 0) return true;
            if (!Owned(q, out var player) || !Sessions.TryGetValue(q, out var s)) return false;
            // Native phase advancement can precede the next one-second Tick.
            // Ignore callbacks for that unscheduled wave before checking the
            // old deadline; otherwise an unrelated kill can fail a cleared wave.
            if (q.CurrentPhase != s.Clock.Wave || __instance.Phase != s.Clock.Wave) return false;
            string reason = Check(s, Time.time);
            if (reason != null) { Fail(q, player, reason); return false; }
            // Game-event ExtraData becomes native spawnByName before the first
            // EntityCreationData packet. spawnById stays -1 (not a Twitch spawn).
            // No dependency on later/delta-only entity-buff synchronization.
            return killedEntity != null && string.Equals(killedEntity.spawnByName,
                Request(q.QuestCode, Tier(q.ID), s.Clock.Wave), StringComparison.Ordinal) &&
                Inside(player.position, s.Anchor) && __instance.Phase == s.Clock.Wave && q.CurrentPhase == s.Clock.Wave &&
                q.DataVariables.ContainsKey(WaveMarker(s.Clock.Wave));
        }
        public static void BeforeClose(Quest __instance, ref Quest.QuestState finalState)
        {
            if (Tier(__instance.ID) == 0) return;
            if (finalState == Quest.QuestState.Completed)
            {
                bool valid = Sessions.TryGetValue(__instance, out var s) && s.Clock.Wave == 3 &&
                    Check(s, Time.time) == null && Inside(s.Player.position, s.Anchor);
                for (int wave = 1; wave <= 3 && valid; wave++) valid &= __instance.DataVariables.ContainsKey(WaveMarker(wave));
                foreach (var objective in __instance.Objectives) valid &= objective.Complete;
                if (!valid) finalState = Quest.QuestState.Failed;
            }
            Revoke(__instance.OwnerJournal?.OwnerPlayer, __instance.QuestCode);
        }
        private static void Fail(Quest quest, EntityPlayerLocal player, string reason)
        {
            if (!quest.Active) return;
            quest.CloseQuest(Quest.QuestState.Failed);
            if (player != null) Tell(player, reason);
        }
        private static void Tell(EntityPlayerLocal player, string key) { GameManager.ShowTooltip(player, Localization.Get(key)); }
        private static void Log(string message) { T16RuntimeFixMod.SafeLog("[AEC-Defense] " + message); }
    }
}
