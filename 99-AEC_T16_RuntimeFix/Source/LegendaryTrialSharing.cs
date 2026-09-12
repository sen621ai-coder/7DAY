using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace AECT16RuntimeFix
{
    // Supplement native awards, not XP or loot. Only the authoritative death
    // path can send receipts. Recipients never need a client-side corpse entity.
    public static class LegendaryTrialSharing
    {
        public const int MinimumRange = 200;
        private const ulong IdentityMagic = 0x3152544345415A50UL; // PZAECTR1
        private static readonly string[] Kinds = { "hunter", "bulwark", "storm" };
        private static readonly ConditionalWeakTable<EntityAlive, object> Awarded = new ConditionalWeakTable<EntityAlive, object>();
        private static readonly ConditionalWeakTable<EntityPlayerLocal, RangeNotice> Notices = new ConditionalWeakTable<EntityPlayerLocal, RangeNotice>();

        private sealed class RangeNotice
        {
            public float NextCheck, NextWarning;
            public int State = -1;
            public Quest Quest;
        }

        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Constructor(typeof(EntityCreationData), new[] { typeof(Entity), typeof(bool) }),
                postfix: new HarmonyMethod(typeof(LegendaryTrialSharing), nameof(AfterSnapshot)));
            harmony.Patch(AccessTools.Method(typeof(EntityCreationData), nameof(EntityCreationData.read)),
                postfix: new HarmonyMethod(typeof(LegendaryTrialSharing), nameof(AfterRead)));
            harmony.Patch(AccessTools.Method(typeof(GameManager), nameof(GameManager.AwardKill), new[] { typeof(EntityAlive), typeof(EntityAlive) }),
                postfix: new HarmonyMethod(typeof(LegendaryTrialSharing), nameof(AfterAwardKill)));
            harmony.Patch(AccessTools.Method(typeof(EntityPlayerLocal), nameof(EntityPlayerLocal.OnUpdateLive)),
                postfix: new HarmonyMethod(typeof(LegendaryTrialSharing), nameof(AfterPlayerUpdate)));
        }

        public static int EffectiveRange(int nativeRange) { return Math.Max(MinimumRange, nativeRange); }
        public static int ShareRange { get { return EffectiveRange(GameStats.GetInt(EnumGameStats.PartySharedKillRange)); } }
        public static bool InRange(float distance, int range) { return distance >= 0 && distance <= range; }
        public static int BoundaryState(float distance, int range)
        {
            return !InRange(distance, range) ? 2 : distance >= range * 0.9f ? 1 : 0;
        }

        public static string TrialClass(int tier, int kind)
        {
            return tier >= 16 && tier <= 19 && kind >= 0 && kind < Kinds.Length ? "PZAECTrial_" + Kinds[kind] + "_T" + tier : null;
        }

        public static bool ParseClass(string name, out int tier, out int kind)
        {
            tier = kind = 0;
            if (name == null || !name.StartsWith("PZAECTrial_", StringComparison.OrdinalIgnoreCase)) return false;
            for (tier = 16; tier <= 19; tier++)
                for (kind = 0; kind < Kinds.Length; kind++)
                    if (string.Equals(name, TrialClass(tier, kind), StringComparison.OrdinalIgnoreCase)) return true;
            tier = kind = 0;
            return false;
        }

        public static bool ParseTag(string tag, out int code)
        {
            code = 0;
            const string prefix = "PZAECAdventure:";
            string suffix = ":" + LegendaryAdventure.SpawnMarker;
            if (tag == null || !tag.StartsWith(prefix, StringComparison.Ordinal) || !tag.EndsWith(suffix, StringComparison.Ordinal) ||
                tag.Length <= prefix.Length + suffix.Length) return false;
            return int.TryParse(tag.Substring(prefix.Length, tag.Length - prefix.Length - suffix.Length),
                NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out code) &&
                tag == LegendaryAdventure.Request(code, LegendaryAdventure.SpawnMarker);
        }

        // Mob CVars are NOT persisted by native EntityAlive.Write. Put a small
        // versioned trailer inside the existing length-delimited entityData;
        // native entity readers ignore the tail, so no global save/wire format
        // changes and no data are added to ordinary entities.
        public static void AppendIdentity(MemoryStream data, int code)
        {
            long position = data.Position;
            try
            {
                data.Position = data.Length;
                using (var writer = new BinaryWriter(data, System.Text.Encoding.UTF8, true))
                { writer.Write(IdentityMagic); writer.Write(code); writer.Write(~code); }
            }
            finally { data.Position = position; }
        }

        public static bool ReadIdentity(MemoryStream data, out int code)
        {
            code = 0;
            if (data == null || data.Length < 16) return false;
            long position = data.Position;
            try
            {
                data.Position = data.Length - 16;
                using (var reader = new BinaryReader(data, System.Text.Encoding.UTF8, true))
                {
                    if (reader.ReadUInt64() != IdentityMagic) return false;
                    int candidate = reader.ReadInt32();
                    if (reader.ReadInt32() != ~candidate) return false;
                    code = candidate;
                    return true;
                }
            }
            finally { data.Position = position; }
        }

        public static void AfterSnapshot(EntityCreationData __instance)
        {
            try
            {
                if (EntityClass.list.TryGetValue(__instance.entityClass, out var entityClass) &&
                    ParseClass(entityClass?.entityClassName, out _, out _) &&
                    ParseTag(__instance.spawnByName, out int code)) AppendIdentity(__instance.entityData, code);
            }
            catch (Exception ex) { Warn("Save identity", ex); }
        }

        public static void AfterRead(EntityCreationData __instance)
        {
            try
            {
                if (EntityClass.list.TryGetValue(__instance.entityClass, out var entityClass) &&
                    ParseClass(entityClass?.entityClassName, out _, out _) &&
                    string.IsNullOrEmpty(__instance.spawnByName) && ReadIdentity(__instance.entityData, out int code))
                    __instance.spawnByName = LegendaryAdventure.Request(code, LegendaryAdventure.SpawnMarker);
            }
            catch (Exception ex) { Warn("Load identity", ex); }
        }

        public static void AfterAwardKill(EntityAlive __0, EntityAlive __1)
        {
            try
            {
                if (!(ConnectionManager.Instance?.IsServer ?? false) || !(__0 is EntityPlayer killer) || __1 == null ||
                    !ParseClass(EntityClass.list[__1.entityClass]?.entityClassName, out int tier, out int kind)) return;
                if (!ParseTag(__1.spawnByName, out int code))
                {
                    T16RuntimeFixMod.SafeLog("[AEC-TrialShare] Rejected unattributed trial boss=" + __1.entityId + "; old untagged encounters must be restarted.");
                    return;
                }
                if (Awarded.TryGetValue(__1, out _)) return;
                Awarded.Add(__1, new object());
                // Snapshot membership and distances at the authoritative kill.
                var recipients = new List<EntityPlayer> { killer };
                if (killer.Party != null)
                    recipients.AddRange(killer.Party.MemberList.Where(p => p != null && p.entityId != killer.entityId));
                int range = ShareRange;
                foreach (var recipient in recipients)
                {
                    float distance = Vector3.Distance(killer.position, recipient.position);
                    if (!InRange(distance, range))
                    {
                        T16RuntimeFixMod.SafeLog("[AEC-TrialShare] Out of range recipient=" + recipient.entityId + " killer=" + killer.entityId + " distance=" + distance + " range=" + range);
                        continue;
                    }
                    try
                    {
                        if (recipient is EntityPlayerLocal local) ApplyCredit(local, code, tier, kind, __1.entityId);
                        else ConnectionManager.Instance.SendPackage(NetPackageManager.GetPackage<NetPackagePZAECTrialKill>()
                            .Setup(recipient.entityId, code, tier, kind, __1.entityId), false, recipient.entityId);
                    }
                    catch (Exception ex) { Warn("Receipt recipient=" + recipient.entityId, ex); }
                }
            }
            catch (Exception ex) { Warn("Death relay", ex); }
        }

        public static bool Eligible(Quest quest, int playerId, int code, int tier, int kind)
        {
            return quest != null && TrialClass(tier, kind) != null && quest.QuestCode == code &&
                LegendaryAdventure.ChallengeTier(quest.ID) == tier &&
                LegendaryAdventure.CanCountTrialKill(quest.ID, code, quest.Active, quest.CurrentPhase,
                    quest.SharedOwnerID, playerId, quest.DataVariables, LegendaryAdventure.Request(code, LegendaryAdventure.SpawnMarker));
        }

        public static void ApplyCredit(EntityPlayerLocal player, int code, int tier, int kind, int victimId)
        {
            if (player?.QuestJournal?.quests == null) return;
            string target = TrialClass(tier, kind);
            // Refresh may auto-complete/remove a quest, so snapshot both lists.
            foreach (var quest in player.QuestJournal.quests.ToArray())
            {
                if (!Eligible(quest, player.entityId, code, tier, kind) || !quest.CheckRequirements()) continue;
                foreach (var objective in quest.Objectives.OfType<ObjectiveEntityKill>().ToArray())
                {
                    if (objective.Complete || !string.Equals(objective.ID, target, StringComparison.OrdinalIgnoreCase)) continue;
                    // Each trial has three distinct one-kill targets. Native
                    // events and duplicate receipts cannot increment past one.
                    objective.CurrentValue = 1;
                    objective.Refresh();
                    T16RuntimeFixMod.SafeLog("[AEC-TrialShare] Credited quest=" + code + " recipient=" + player.entityId + " victim=" + victimId + " target=" + target);
                }
            }
        }

        public static void AfterPlayerUpdate(EntityPlayerLocal __instance)
        {
            try
            {
                var player = __instance;
                if (player?.QuestJournal?.quests == null) return;
                var notice = Notices.GetValue(player, p => new RangeNotice());
                float now = Time.time;
                if (now < notice.NextCheck) return;
                notice.NextCheck = now + 1f;
                var quest = player.QuestJournal.quests.FirstOrDefault(q => q != null && q.Active && q.CurrentPhase == 1 && LegendaryAdventure.ChallengeTier(q.ID) != 0);
                if (quest == null) { notice.Quest = null; notice.State = -1; return; }
                int range = ShareRange;
                float farthest = 0;
                int others = 0;
                if (player.Party != null)
                    foreach (var member in player.Party.MemberList)
                        if (member != null && member.entityId != player.entityId)
                        { others++; farthest = Math.Max(farthest, Vector3.Distance(player.position, member.position)); }
                int state = others == 0 ? 3 : BoundaryState(farthest, range);
                bool started = !ReferenceEquals(notice.Quest, quest);
                if (started || state != notice.State || ((state == 1 || state == 2) && now >= notice.NextWarning))
                {
                    string key = state == 3 ? "PZAECTrialRangeSolo" : state == 2 ? "PZAECTrialRangeOutside" :
                        state == 1 ? "PZAECTrialRangeNear" : started ? "PZAECTrialRangeStart" : "PZAECTrialRangeSafe";
                    GameManager.ShowTooltip(player, string.Format(Localization.Get(key), range, Math.Ceiling(farthest)));
                    notice.NextWarning = now + 20f;
                }
                notice.Quest = quest;
                notice.State = state;
            }
            catch (Exception ex) { Warn("Range notice", ex); }
        }

        private static void Warn(string context, Exception ex)
        { T16RuntimeFixMod.SafeLog("[AEC-TrialShare] " + context + ": " + ex.GetBaseException().Message); }
    }

    // Registered by the game's native NetPackage subclass discovery. Install
    // the same runtime DLL on the server/host AND every client before connecting.
    public sealed class NetPackagePZAECTrialKill : NetPackage
    {
        public int Recipient, Code, Victim;
        public byte Tier, Kind;
        public override NetPackageDirection PackageDirection { get { return NetPackageDirection.ToClient; } }
        public NetPackagePZAECTrialKill Setup(int recipient, int code, int tier, int kind, int victim)
        { Recipient = recipient; Code = code; Tier = (byte)tier; Kind = (byte)kind; Victim = victim; return this; }
        public override void read(PooledBinaryReader reader)
        { Recipient = reader.ReadInt32(); Code = reader.ReadInt32(); Tier = reader.ReadByte(); Kind = reader.ReadByte(); Victim = reader.ReadInt32(); }
        public override void write(PooledBinaryWriter writer)
        { base.write(writer); writer.Write(Recipient); writer.Write(Code); writer.Write(Tier); writer.Write(Kind); writer.Write(Victim); }
        public override int GetLength() { return 14; }
        public override void ProcessPackage(World world, GameManager callbacks)
        {
            if (world == null || ConnectionManager.Instance == null || ConnectionManager.Instance.IsServer) return;
            if (world.GetEntity(Recipient) is EntityPlayerLocal player)
                LegendaryTrialSharing.ApplyCredit(player, Code, Tier, Kind, Victim);
        }
    }
}
