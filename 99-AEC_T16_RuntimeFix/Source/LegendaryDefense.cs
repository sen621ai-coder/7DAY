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
        public const int CoreSearchRadius = 10, CoreSearchHeight = 6, FacilityRadius = 24, ScanRadius = 20, ScanHeight = 10;
        public const double Preparation = 20, Intermission = 15, RetreatGrace = 15, WaveLimit = 900;
        public const string Lease = "buffPZAECDefenseLease";
        public const string Scope = "PZAECDefense";
        public const string CoreBlock = "PZAECStrongholdCore";
        public const string PowerBlock = "PZAECStrongholdPower";
        public const string SupplyBlock = "PZAECStrongholdSupply";
        private static readonly Regex QuestPattern = new Regex(@"\APZAECDefenseT(16|17|18|19)\z", RegexOptions.CultureInvariant);
        private static readonly Regex EventPattern = new Regex(@"\APZAECDefenseT(16|17|18|19)W([1-3])\z", RegexOptions.CultureInvariant);
        private static readonly Regex BossPattern = new Regex(@"\APZAECDefense_(bulwark|storm)_T(16|17|18|19)_W3\z", RegexOptions.CultureInvariant);
        private static readonly Regex EnemyPattern = new Regex(@"\APZAECDefense_(runner|spider|cop|biker|demo|spitter|wolf|wight|bulwark|storm)_T(16|17|18|19)_W([1-3])\z", RegexOptions.CultureInvariant);
        private static readonly Dictionary<Quest, Session> Sessions = new Dictionary<Quest, Session>();

        internal sealed class StructuralSample
        {
            public int Type;
            public int HitPoints;
        }

        public sealed class FortificationReport
        {
            public Vector3i Core;
            public Vector3i Power;
            public Vector3i Supply;
            public int StructuralBlocks;
            public int Systems;
            public long InitialHitPoints;
            public int Score;
            public int Grade;
            internal readonly Dictionary<Vector3i, StructuralSample> Samples = new Dictionary<Vector3i, StructuralSample>();
        }

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
            public FortificationReport Fortification;
            public WaveClock Clock;
            public double NextAttempt, NextLease;
            public bool WarnedOutside, PowerAlive = true, SupplyAlive = true;
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
        private static int Bounded(int value, int maximum) { return value < 0 ? 0 : value > maximum ? maximum : value; }
        public static int ConstructionScore(long hitPoints, int structuralBlocks, int systems)
        {
            long safeHitPoints = Math.Max(0L, hitPoints);
            long hp = Math.Min(60L, safeHitPoints / 25000L);
            return (int)hp + Bounded(structuralBlocks / 10, 20) + Bounded(systems * 2, 20);
        }
        public static int ConstructionGrade(int score) { return score >= 65 ? 3 : score >= 30 ? 2 : 1; }
        public static int IntegrityGrade(long initialHitPoints, long remainingHitPoints)
        {
            if (initialHitPoints <= 0) return 1;
            double ratio = Math.Max(0d, Math.Min(1d, (double)remainingHitPoints / initialHitPoints));
            return ratio >= .9d ? 3 : ratio >= .7d ? 2 : 1;
        }
        public static int RewardRank(int constructionGrade, long initialHitPoints, long remainingHitPoints, bool powerAlive, bool supplyAlive)
        {
            int facilities = powerAlive && supplyAlive ? 3 : powerAlive || supplyAlive ? 2 : 1;
            return Math.Max(1, Math.Min(Math.Min(Bounded(constructionGrade, 3), IntegrityGrade(initialHitPoints, remainingHitPoints)), facilities));
        }
        public static string BonusEventId(int tier, int rank)
        {
            return tier >= 16 && tier <= 19 && rank >= 1 && rank <= 3 ? "PZAECStrongholdBonusT" + tier + "R" + rank : null;
        }
        private static string NameAt(World world, Vector3i position)
        {
            if (world == null) return "";
            BlockValue value = world.GetBlock(position);
            return value.isair || value.Block == null ? "" : value.Block.blockName ?? "";
        }
        private static bool IsAt(World world, Vector3i position, string name)
        {
            return string.Equals(NameAt(world, position), name, StringComparison.OrdinalIgnoreCase);
        }
        private static bool FindBlock(World world, Vector3i center, string name, int radius, int height, out Vector3i found)
        {
            found = Vector3i.zero;
            int best = int.MaxValue;
            for (int y = center.y - height; y <= center.y + height; y++)
                for (int z = center.z - radius; z <= center.z + radius; z++)
                    for (int x = center.x - radius; x <= center.x + radius; x++)
                    {
                        int dx = x - center.x, dz = z - center.z;
                        int distance = dx * dx + dz * dz + (y - center.y) * (y - center.y);
                        if (dx * dx + dz * dz > radius * radius || distance >= best) continue;
                        var position = new Vector3i(x, y, z);
                        if (!IsAt(world, position, name)) continue;
                        found = position;
                        best = distance;
                    }
            return best != int.MaxValue;
        }
        private static bool IsSystem(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            return n.Contains("trap") || n.Contains("turret") || n.Contains("electricfence") ||
                n.Contains("tripwire") || n.Contains("motion") || n.Contains("generator") ||
                n.Contains("batterybank") || n.Contains("solarbank") || n.Contains("speaker") ||
                n.Contains("relay") || n == PowerBlock.ToLowerInvariant();
        }
        private static bool Inspect(World world, Vector3 playerPosition, out FortificationReport report, out string reason)
        {
            report = null;
            reason = null;
            if (world == null) { reason = "PZAECDefenseUnavailable"; return false; }
            var playerBlock = new Vector3i(playerPosition);
            if (!FindBlock(world, playerBlock, CoreBlock, CoreSearchRadius, CoreSearchHeight, out var core))
            { reason = "PZAECStrongholdNeedCore"; return false; }
            if (!FindBlock(world, core, PowerBlock, FacilityRadius, ScanHeight, out var power))
            { reason = "PZAECStrongholdNeedPower"; return false; }
            if (!FindBlock(world, core, SupplyBlock, FacilityRadius, ScanHeight, out var supply))
            { reason = "PZAECStrongholdNeedSupply"; return false; }
            report = new FortificationReport { Core = core, Power = power, Supply = supply };
            for (int y = core.y - ScanHeight; y <= core.y + ScanHeight; y++)
                for (int z = core.z - ScanRadius; z <= core.z + ScanRadius; z++)
                    for (int x = core.x - ScanRadius; x <= core.x + ScanRadius; x++)
                    {
                        int dx = x - core.x, dz = z - core.z;
                        if (dx * dx + dz * dz > ScanRadius * ScanRadius) continue;
                        var position = new Vector3i(x, y, z);
                        BlockValue value = world.GetBlock(position);
                        Block block = value.Block;
                        if (value.isair || value.isTerrain || value.isWater || value.ischild || block == null) continue;
                        string name = block.blockName ?? "";
                        if (IsSystem(name)) report.Systems++;
                        if (block.BlocksMovement == 0 || block.MaxDamage < 500) continue;
                        int remaining = Math.Max(0, block.MaxDamage - value.damage);
                        if (remaining == 0) continue;
                        report.StructuralBlocks++;
                        report.InitialHitPoints += remaining;
                        report.Samples[position] = new StructuralSample { Type = value.type, HitPoints = remaining };
                    }
            report.Score = ConstructionScore(report.InitialHitPoints, report.StructuralBlocks, report.Systems);
            report.Grade = ConstructionGrade(report.Score);
            return true;
        }
        private static long RemainingHitPoints(World world, FortificationReport report)
        {
            long total = 0;
            if (world == null || report == null) return 0;
            foreach (var pair in report.Samples)
            {
                BlockValue value = world.GetBlock(pair.Key);
                Block block = value.Block;
                if (value.isair || block == null || value.type != pair.Value.Type) continue;
                total += Math.Min(pair.Value.HitPoints, Math.Max(0, block.MaxDamage - value.damage));
            }
            return total;
        }
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
            if (!Inspect(player.world, player.position, out _, out string reason)) return reason;
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
                if (!Inspect(player.world, player.position, out var report, out string reason))
                {
                    Tell(player, reason);
                    GameManager.Instance.StartCoroutine(FailLoaded(__instance, player));
                    return;
                }
                var anchor = new Vector3(report.Core.x + .5f, report.Core.y + .5f, report.Core.z + .5f);
                var session = new Session { Quest = __instance, Player = player, Anchor = anchor,
                    Fortification = report, Clock = new WaveClock(1, Time.time) };
                __instance.SetPositionData(Quest.PositionDataTypes.Location, session.Anchor);
                __instance.DataVariables[Scope + "Started_v1"] = "1";
                __instance.DataVariables[Scope + "Score_v1"] = report.Score.ToString(CultureInfo.InvariantCulture);
                Sessions.Add(__instance, session);
                Publish(session);
                Tell(player, "PZAECDefensePreparation");
                TellFormatted(player, "PZAECStrongholdRegistered", report.Score,
                    Localization.Get("PZAECStrongholdGrade" + report.Grade), report.StructuralBlocks, report.Systems);
                Log("Registered score=" + report.Score + " grade=" + report.Grade + " blocks=" +
                    report.StructuralBlocks + " systems=" + report.Systems + " hp=" + report.InitialHitPoints +
                    " core=" + report.Core + " code=" + __instance.QuestCode);
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
            if (!IsAt(player.world, s.Fortification.Core, CoreBlock)) return "PZAECStrongholdCoreLost";
            bool power = IsAt(player.world, s.Fortification.Power, PowerBlock);
            bool supply = IsAt(player.world, s.Fortification.Supply, SupplyBlock);
            if (power != s.PowerAlive) { s.PowerAlive = power; Tell(player, power ? "PZAECStrongholdPowerRestored" : "PZAECStrongholdPowerLost"); }
            if (supply != s.SupplyAlive) { s.SupplyAlive = supply; Tell(player, supply ? "PZAECStrongholdSupplyRestored" : "PZAECStrongholdSupplyLost"); }
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
                else
                {
                    long remaining = RemainingHitPoints(s.Player.world, s.Fortification);
                    bool power = IsAt(s.Player.world, s.Fortification.Power, PowerBlock);
                    bool supply = IsAt(s.Player.world, s.Fortification.Supply, SupplyBlock);
                    int rank = RewardRank(s.Fortification.Grade, s.Fortification.InitialHitPoints, remaining, power, supply);
                    string eventId = BonusEventId(Tier(__instance.ID), rank);
                    bool awarded = LegendaryAdventure.DispatchOnce(__instance.DataVariables, Scope + "Bonus_v1", eventId, id =>
                        GameEventManager.Current != null && GameEventManager.Current.HandleAction(id, s.Player, s.Player, false,
                            Scope + ":" + __instance.QuestCode + ":" + rank, Scope + ":" + __instance.QuestCode + ":" + rank,
                            false, false, "", null));
                    TellFormatted(s.Player, "PZAECStrongholdComplete", rank,
                        s.Fortification.InitialHitPoints <= 0 ? 0 : (int)Math.Round(100d * remaining / s.Fortification.InitialHitPoints),
                        power ? 1 : 0, supply ? 1 : 0);
                    Log("Completed rank=" + rank + " integrity=" + remaining + "/" + s.Fortification.InitialHitPoints +
                        " power=" + power + " supply=" + supply + " bonusQueued=" + awarded + " code=" + __instance.QuestCode);
                }
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
        private static void TellFormatted(EntityPlayerLocal player, string key, params object[] values)
        {
            GameManager.ShowTooltip(player, string.Format(CultureInfo.InvariantCulture, Localization.Get(key), values));
        }
        private static void Log(string message) { T16RuntimeFixMod.SafeLog("[AEC-Defense] " + message); }
    }
}
