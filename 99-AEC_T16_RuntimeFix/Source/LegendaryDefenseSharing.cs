using System;
using System.Collections.Generic;
using System.Linq;
using GameEvent.SequenceActions;
using HarmonyLib;
using UnityEngine;

namespace AECT16RuntimeFix
{
    // One server ledger per encounter, one cumulative receipt per participant.
    // Client requests never contain kills, counts, success, or reward rank.
    public static class LegendaryDefenseSharing
    {
        public const byte Active = 0, Won = 1, Failed = 2, Missed = 3, Late = 4;
        public const byte Pulse = 0, Leave = 1;
        public static readonly string[] Roles = { "runner", "spider", "cop", "biker", "demo", "spitter", "wolf", "wight", "cop", "bulwark", "storm" };
        public static readonly byte[] Required = { 4, 2, 3, 4, 2, 4, 2, 5, 2, 1, 1 };
        private static readonly Dictionary<int, Battle> Battles = new Dictionary<int, Battle>();
        private static readonly Dictionary<Quest, LocalState> Locals = new Dictionary<Quest, LocalState>();
        private static readonly HashSet<Tuple<int, int>> SettledCodes = new HashSet<Tuple<int, int>>();
        private static World ServerWorld, ClientWorld;
        private static float NextTick;

        public sealed class Ledger
        {
            public readonly byte[] Counts = new byte[11];
            public readonly HashSet<int> Seen = new HashSet<int>();
            public bool Add(int entityId, int slot)
            {
                if (slot < 0 || slot >= Required.Length || !Seen.Add(entityId)) return false;
                if (Counts[slot] >= Required[slot]) return false;
                Counts[slot]++;
                return true;
            }
            public bool Complete(int wave)
            {
                if (wave < 1 || wave > 3) return false;
                for (int i = 0; i < Counts.Length; i++) if (WaveFor(i) <= wave && Counts[i] != Required[i]) return false;
                return true;
            }
        }
        private sealed class Member
        {
            public int Id;
            public float SeenAt;
            public byte Status;
            public bool Closed;
            public readonly Ledger Progress = new Ledger();
        }
        private sealed class Battle
        {
            public int Code, Owner, Tier, Revision;
            public Vector3 Anchor;
            public LegendaryDefense.FortificationReport Fort;
            public LegendaryDefense.WaveClock Clock;
            public byte Status, Rank;
            public readonly Ledger Total = new Ledger();
            public readonly Dictionary<int, int> Spawned = new Dictionary<int, int>();
            public readonly Dictionary<int, Member> Members = new Dictionary<int, Member>();
        }
        private sealed class LocalState
        {
            public EntityPlayerLocal Player;
            public int Owner, Revision = -1, Rank, Range = 200, Wave = 1, Boundary = -1;
            public float NextPulse, NextNotice, StartedAt, LastReceiptAt;
            public Vector3 Anchor;
            public byte Status;
            public bool Received, Settled;
        }

        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(Quest), nameof(Quest.AddSharedKill)),
                prefix: new HarmonyMethod(typeof(LegendaryDefenseSharing), nameof(BeforeSharedKill)));
            harmony.Patch(AccessTools.Method(typeof(ActionBaseSpawn), "SpawnEntity"),
                postfix: new HarmonyMethod(typeof(LegendaryDefenseSharing), nameof(AfterSpawn)));
            harmony.Patch(AccessTools.Method(typeof(GameManager), nameof(GameManager.AwardKill)),
                postfix: new HarmonyMethod(typeof(LegendaryDefenseSharing), nameof(AfterAwardKill)));
            harmony.Patch(AccessTools.Method(typeof(GameManager), "UpdateTick"),
                postfix: new HarmonyMethod(typeof(LegendaryDefenseSharing), nameof(ServerTick)));
            harmony.Patch(AccessTools.Method(typeof(EntityPlayerLocal), nameof(EntityPlayerLocal.OnUpdateLive)),
                postfix: new HarmonyMethod(typeof(LegendaryDefenseSharing), nameof(PlayerTick)));
        }
        public static int WaveFor(int slot) { return slot < 0 || slot >= 11 ? 0 : slot < 3 ? 1 : slot < 7 ? 2 : 3; }
        public static string Target(int tier, int slot)
        { return tier < 16 || tier > 19 || WaveFor(slot) == 0 ? null : "PZAECDefense_" + Roles[slot] + "_T" + tier + "_W" + WaveFor(slot); }
        public static int Slot(string name, int tier)
        {
            if (name == null || !name.StartsWith("PZAECDefense_", StringComparison.Ordinal)) return -1;
            for (int i = 0; i < 11; i++) if (name == Target(tier, i)) return i;
            return -1;
        }
        public static bool MayJoin(bool sameParty, int wave, int kills)
        { return sameParty && wave == 1 && kills == 0; }
        // Native shared quest packets identify only a class, not a victim or
        // encounter. Do not let that second path race cumulative receipts.
        public static bool BeforeSharedKill(Quest __instance)
        { return LegendaryDefense.Tier(__instance?.ID) == 0; }
        public static bool CreditRange(bool sameParty, bool alive, float distance, int range)
        { return sameParty && alive && LegendaryTrialSharing.InRange(distance, range); }
        private static bool Server { get { return ConnectionManager.Instance != null && ConnectionManager.Instance.IsServer; } }
        private static bool SameParty(EntityPlayer a, EntityPlayer b)
        { return a != null && b != null && (a.entityId == b.entityId || a.Party != null && a.Party.MemberList.Any(p => p != null && p.entityId == b.entityId)); }
        private static void EnsureServer(World world)
        {
            if (ReferenceEquals(ServerWorld, world)) return;
            ServerWorld = world; Battles.Clear(); NextTick = 0;
        }
        private static void EnsureClient(World world)
        {
            if (ReferenceEquals(ClientWorld, world)) return;
            ClientWorld = world; Locals.Clear(); SettledCodes.Clear();
        }

        public static bool BeginLocal(Quest quest, bool newQuest)
        {
            var player = quest?.OwnerJournal?.OwnerPlayer;
            if (player == null || !quest.Active) return false;
            EnsureClient(player.world);
            if (Locals.ContainsKey(quest)) return true;
            // All peers are single-session, including shared copies. Reload
            // cannot rejoin a previously consumed encounter or replay rewards.
            if (!newQuest || SettledCodes.Contains(Tuple.Create(player.entityId, quest.QuestCode))) return false;
            bool conflict = player.QuestJournal.quests.Any(q => q != quest && q.Active && LegendaryDefense.Tier(q.ID) != 0);
            Locals[quest] = new LocalState { Player = player, Owner = quest.SharedOwnerID < 0 ? player.entityId : quest.SharedOwnerID,
                StartedAt = Time.time, Status = conflict ? Failed : Active };
            return !conflict;
        }
        private static void SendRequest(Quest quest, LocalState local, byte op)
        {
            int tier = LegendaryDefense.Tier(quest.ID);
            if (Server) Request(local.Player.world, local.Player.entityId, op, local.Owner, quest.QuestCode, tier);
            else ConnectionManager.Instance.SendToServer(NetPackageManager.GetPackage<NetPackagePZAECDefenseRequest>()
                .Setup(op, local.Owner, quest.QuestCode, tier), false);
        }
        public static void Request(World world, int sender, byte op, int ownerId, int code, int tier)
        {
            if (!Server || world == null || sender < 0 || code == 0 || tier < 16 || tier > 19 || op > Leave) return;
            EnsureServer(world);
            var player = world.GetEntity(sender) as EntityPlayer;
            if (player == null) return;
            if (!Battles.TryGetValue(code, out var battle))
            {
                if (op != Pulse || sender != ownerId || player.IsDead()) return;
                // The original owner must already have published a live,
                // matching native spawn lease. Packet ordering may delay this;
                // the local heartbeat retries before the first 20s wave.
                var buffs = player.Buffs;
                if (buffs == null || !buffs.HasBuff(LegendaryDefense.Lease) ||
                    !LegendaryDefense.ScopeMatches(LegendaryDefense.EventId(tier, 1), LegendaryDefense.Request(code, tier, 1),
                        buffs.GetCustomVar(LegendaryDefense.Scope + "High"), buffs.GetCustomVar(LegendaryDefense.Scope + "Low"), buffs.GetCustomVar(LegendaryDefense.Scope + "Wave"))) return;
                var anchor = new Vector3(buffs.GetCustomVar(LegendaryDefense.Scope + "X"), buffs.GetCustomVar(LegendaryDefense.Scope + "Y"), buffs.GetCustomVar(LegendaryDefense.Scope + "Z"));
                if (!LegendaryDefense.Within(player.position.x, player.position.y, player.position.z, anchor.x, anchor.y, anchor.z) ||
                    Battles.Values.Any(b => b.Status == Active && b.Members.TryGetValue(sender, out var member) && member.Status == Active) ||
                    !LegendaryDefense.Inspect(world, anchor, out var fort, out _)) return;
                Vector3 expected = new Vector3(fort.Core.x + .5f, fort.Core.y + .5f, fort.Core.z + .5f);
                if (Vector3.Distance(anchor, expected) > .1f) return;
                battle = new Battle { Owner = sender, Code = code, Tier = tier, Anchor = anchor, Fort = fort,
                    Clock = new LegendaryDefense.WaveClock(1, Time.time) };
                battle.Members.Add(sender, new Member { Id = sender, SeenAt = Time.time });
                Battles.Add(code, battle);
                Log("Registered owner=" + sender + " code=" + code + " tier=" + tier);
            }
            if (battle.Tier != tier) return;
            if (op == Leave)
            {
                if (battle.Members.TryGetValue(sender, out var closing)) closing.Closed = true;
                if (sender == battle.Owner) { if (battle.Status == Active) End(battle, Failed); }
                else if (battle.Members.TryGetValue(sender, out var leaving) && leaving.Status == Active)
                { leaving.Status = Failed; Send(battle, leaving); }
                return;
            }
            if (!battle.Members.TryGetValue(sender, out var joined))
            {
                var owner = world.GetEntity(battle.Owner) as EntityPlayer;
                bool available = !Battles.Values.Any(b => b != battle && b.Status == Active && b.Members.TryGetValue(sender, out var member) && member.Status == Active);
                bool accepted = battle.Status == Active && !player.IsDead() && available &&
                    MayJoin(SameParty(owner, player), battle.Clock.Wave, battle.Total.Seen.Count);
                // Tombstone rejected/left members: no leave/rejoin reward reset.
                joined = new Member { Id = sender, SeenAt = Time.time, Status = accepted ? Active : Late };
                battle.Members.Add(sender, joined);
            }
            if (joined.Closed || battle.Status != Active && Time.time - joined.SeenAt >= 12)
            { joined.Closed = true; joined.Status = Late; }
            joined.SeenAt = Time.time;
            Send(battle, joined);
        }

        public static bool AllowSpawn(EntityPlayer player, string request, int wave)
        {
            if (!Server) return true; // Native spawning is server-authoritative.
            EnsureServer(player.world);
            return Battles.Values.Any(b => b.Owner == player.entityId && b.Status == Active && b.Clock.Wave == wave &&
                Time.time >= b.Clock.ReadyAt && request == LegendaryDefense.Request(b.Code, b.Tier, wave));
        }
        public static void AfterSpawn(ActionBaseSpawn __instance, Entity __result)
        {
            try
            {
                if (!Server || __result == null || __instance.Owner?.Requester == null) return;
                EnsureServer(__result.world);
                int owner = __instance.Owner.Requester.entityId;
                foreach (var battle in Battles.Values)
                {
                    if (battle.Status != Active || battle.Owner != owner || __result.spawnByName != LegendaryDefense.Request(battle.Code, battle.Tier, battle.Clock.Wave)) continue;
                    int slot = Slot(__result.EntityClass?.entityClassName, battle.Tier);
                    if (WaveFor(slot) == battle.Clock.Wave) battle.Spawned[__result.entityId] = slot;
                }
            }
            catch (Exception ex) { Log("Spawn registration failed: " + ex.GetBaseException().Message); }
        }
        private static bool CheckBattle(Battle battle, double now)
        {
            if (battle.Status != Active) return false;
            var owner = ServerWorld.GetEntity(battle.Owner) as EntityPlayer;
            bool exists = owner != null && now - battle.Members[battle.Owner].SeenAt < 12;
            bool inside = exists && LegendaryDefense.Within(owner.position.x, owner.position.y, owner.position.z, battle.Anchor.x, battle.Anchor.y, battle.Anchor.z);
            if (!exists || !LegendaryDefense.IsAt(ServerWorld, battle.Fort.Core, LegendaryDefense.CoreBlock) ||
                battle.Clock.Check(now, !owner.IsDead(), GameStats.GetBool(EnumGameStats.EnemySpawnMode),
                    ServerWorld.IsWithinTraderArea(new Vector3i(owner.position)), inside) != null)
            { End(battle, Failed); return false; }
            foreach (var member in battle.Members.Values.ToArray())
            {
                if (member.Id == battle.Owner || member.Status != Active) continue;
                var player = ServerWorld.GetEntity(member.Id) as EntityPlayer;
                if (player == null || player.IsDead() || now - member.SeenAt >= 12 || !SameParty(owner, player))
                { member.Status = Failed; Send(battle, member); }
            }
            return inside;
        }
        public static void AfterAwardKill(EntityAlive __0, EntityAlive __1)
        {
            try
            {
                if (!Server || !(__0 is EntityPlayer killer) || __1 == null || LegendaryDefense.EnemyTier(__1.EntityClass?.entityClassName) == 0) return;
                EnsureServer(__1.world);
                foreach (var battle in Battles.Values.ToArray())
                {
                    if (battle.Status != Active || !battle.Spawned.TryGetValue(__1.entityId, out int slot) ||
                        WaveFor(slot) != battle.Clock.Wave || __1.spawnByName != LegendaryDefense.Request(battle.Code, battle.Tier, battle.Clock.Wave)) continue;
                    bool inside = CheckBattle(battle, Time.time);
                    if (battle.Status != Active || !battle.Total.Add(__1.entityId, slot)) continue;
                    var owner = ServerWorld.GetEntity(battle.Owner) as EntityPlayer;
                    int range = LegendaryTrialSharing.ShareRange;
                    foreach (var member in battle.Members.Values.ToArray())
                    {
                        var player = ServerWorld.GetEntity(member.Id) as EntityPlayer;
                        if (member.Status != Active || player == null || !inside) continue;
                        if (CreditRange(SameParty(owner, killer) && SameParty(killer, player), !player.IsDead(), Vector3.Distance(killer.position, player.position), range))
                            member.Progress.Add(__1.entityId, slot);
                    }
                    int wave = battle.Clock.Wave;
                    // Every initial target is now dead. Anyone missing a target
                    // cannot recover this wave; never leave an impossible task.
                    if (battle.Total.Complete(wave))
                    {
                        if (!battle.Members[battle.Owner].Progress.Complete(wave)) { End(battle, Missed); continue; }
                        foreach (var member in battle.Members.Values)
                            if (member.Status == Active && !member.Progress.Complete(wave)) member.Status = Missed;
                        if (wave == 3) { End(battle, Won); continue; }
                        battle.Clock.MoveTo(wave + 1, Time.time);
                    }
                    Broadcast(battle);
                }
            }
            catch (Exception ex) { Log("Kill relay failed: " + ex.GetBaseException().Message); }
        }
        private static void End(Battle battle, byte status)
        {
            if (battle.Status != Active) return;
            if (status == Won)
            {
                battle.Rank = (byte)LegendaryDefense.RewardRank(battle.Fort.Grade, battle.Fort.InitialHitPoints,
                    LegendaryDefense.RemainingHitPoints(ServerWorld, battle.Fort),
                    LegendaryDefense.IsAt(ServerWorld, battle.Fort.Power, LegendaryDefense.PowerBlock),
                    LegendaryDefense.IsAt(ServerWorld, battle.Fort.Supply, LegendaryDefense.SupplyBlock));
            }
            battle.Status = status;
            foreach (var member in battle.Members.Values)
                if (member.Status == Active) member.Status = status == Won && member.Progress.Complete(3) ? Won : status == Won ? Missed : status;
            Log("Ended code=" + battle.Code + " status=" + status + " rank=" + battle.Rank);
            Broadcast(battle);
            battle.Fort = null; battle.Spawned.Clear(); // Keep only compact anti-replay tombstones for this world.
        }
        private static void Broadcast(Battle battle)
        { foreach (var member in battle.Members.Values.ToArray()) Send(battle, member); }
        private static void Send(Battle battle, Member member)
        {
            var player = ServerWorld?.GetEntity(member.Id) as EntityPlayer;
            if (player == null) return;
            var packet = (player is EntityPlayerLocal ? new NetPackagePZAECDefenseState() : NetPackageManager.GetPackage<NetPackagePZAECDefenseState>()).Setup(member.Id, battle.Owner, battle.Code,
                battle.Tier, battle.Clock.Wave, member.Status, battle.Rank, ++battle.Revision, battle.Anchor,
                LegendaryTrialSharing.ShareRange, member.Progress.Counts);
            if (player is EntityPlayerLocal local) Apply(local, packet);
            else ConnectionManager.Instance.SendPackage(packet, false, member.Id);
        }
        public static void ServerTick()
        {
            try
            {
                if (!Server || GameManager.Instance?.World == null) return;
                EnsureServer(GameManager.Instance.World);
                if (Time.time < NextTick) return;
                NextTick = Time.time + 1;
                foreach (var battle in Battles.Values.ToArray()) if (battle.Status == Active) CheckBattle(battle, Time.time);
            }
            catch (Exception ex) { Log("Server status failed: " + ex.GetBaseException().Message); }
        }

        public static bool ValidReceipt(int tier, int wave, byte status, int rank, byte[] counts)
        {
            if (tier < 16 || tier > 19 || wave < 1 || wave > 3 || status > Late || counts == null || counts.Length != 11) return false;
            for (int i = 0; i < 11; i++) if (counts[i] > Required[i] || WaveFor(i) > wave && counts[i] != 0) return false;
            return status != Won || wave == 3 && rank >= 1 && rank <= 3 && counts.SequenceEqual(Required);
        }
        public static void Apply(EntityPlayerLocal player, NetPackagePZAECDefenseState receipt)
        {
            if (player?.QuestJournal?.quests == null || receipt.Recipient != player.entityId || !ValidReceipt(receipt.Tier, receipt.Wave, receipt.Status, receipt.Rank, receipt.Counts)) return;
            foreach (var quest in player.QuestJournal.quests.ToArray())
            {
                if (!quest.Active || quest.QuestCode != receipt.Code || LegendaryDefense.Tier(quest.ID) != receipt.Tier ||
                    !Locals.TryGetValue(quest, out var local) || local.Settled || receipt.Revision <= local.Revision) continue;
                if (local.Owner == player.entityId && receipt.Owner != player.entityId) continue;
                local.Owner = receipt.Owner; local.Revision = receipt.Revision; local.Received = true; local.LastReceiptAt = Time.time;
                local.Wave = receipt.Wave; local.Status = receipt.Status; local.Rank = receipt.Rank;
                local.Anchor = receipt.Anchor; local.Range = receipt.Range;
                quest.SetPositionData(Quest.PositionDataTypes.Location, receipt.Anchor);
                if (receipt.Status >= Failed)
                {
                    Tell(player, receipt.Status == Late ? "PZAECDefenseShareLate" : receipt.Status == Missed ? "PZAECDefenseShareMissed" : "PZAECDefenseShareFailed");
                    quest.CloseQuest(Quest.QuestState.Failed);
                    continue;
                }
                if (!quest.CheckRequirements()) continue;
                ApplyCounts(quest, receipt.Tier, receipt.Wave, receipt.Status, receipt.Counts);
            }
        }
        public static void ApplyCounts(Quest quest, int tier, int wave, byte status, byte[] counts)
        {
            if (quest == null || !quest.Active || status > Won || LegendaryDefense.Tier(quest.ID) != tier || counts == null || counts.Length != 11) return;
            foreach (var objective in quest.Objectives.OfType<ObjectiveEntityKill>().OrderBy(o => o.Phase).ToArray())
            {
                int slot = Slot(objective.ID, tier);
                if (slot < 0 || WaveFor(slot) != objective.Phase || objective.Phase > wave || objective.Phase != quest.CurrentPhase ||
                    objective.Complete || counts[slot] < objective.CurrentValue || counts[slot] > Required[slot]) continue;
                objective.CurrentValue = counts[slot];
                // Wait for the authoritative terminal receipt before completing
                // ANY wave-three target that could trigger final auto-rewards.
                if (status == Won || objective.Phase < 3 || counts[slot] < Required[slot]) objective.Refresh();
            }
        }
        public static bool AuthorizeCompletion(Quest quest, out int rank)
        {
            rank = 0;
            if (quest == null || !quest.Active || !Locals.TryGetValue(quest, out var local) || local.Settled || !local.Received || local.Status != Won ||
                local.Rank < 1 || local.Rank > 3 || quest.Objectives.Any(o => !o.Complete)) return false;
            rank = local.Rank;
            return true;
        }
        public static bool IsSettled(Quest quest) { return Locals.TryGetValue(quest, out var local) && local.Settled; }
        public static void Closed(Quest quest)
        {
            if (!Locals.TryGetValue(quest, out var local) || local.Settled) return;
            local.Settled = true;
            SettledCodes.Add(Tuple.Create(local.Player.entityId, quest.QuestCode));
            SendRequest(quest, local, Leave);
        }
        public static void PlayerTick(EntityPlayerLocal __instance)
        {
            var player = __instance;
            try
            {
                if (player?.world == null) return;
                EnsureClient(player.world);
                foreach (var pair in Locals.ToArray())
                {
                    var quest = pair.Key; var local = pair.Value;
                    if (local.Player != player) continue;
                    if (!quest.Active || !player.QuestJournal.quests.Contains(quest))
                    { Closed(quest); Locals.Remove(quest); continue; }
                    if (local.Settled) continue;
                    if (local.Status >= Failed || player.IsDead())
                    { Tell(player, "PZAECDefenseShareFailed"); quest.CloseQuest(Quest.QuestState.Failed); continue; }
                    if (Time.time >= local.NextPulse)
                    {
                        local.NextPulse = Time.time + 3;
                        SendRequest(quest, local, Pulse);
                    }
                    if (Time.time - (local.Received ? local.LastReceiptAt : local.StartedAt) > 15)
                    { Tell(player, "PZAECDefenseShareUnavailable"); quest.CloseQuest(Quest.QuestState.Failed); continue; }
                    if (!local.Received) continue;
                    float farthest = 0;
                    if (player.Party != null)
                        foreach (var member in player.Party.MemberList)
                            if (member != null && member.entityId != player.entityId) farthest = Math.Max(farthest, Vector3.Distance(player.position, member.position));
                    int state = LegendaryTrialSharing.BoundaryState(farthest, local.Range);
                    if (state != local.Boundary || state > 0 && Time.time >= local.NextNotice)
                    {
                        string key = state == 2 ? "PZAECDefenseShareOutside" : state == 1 ? "PZAECDefenseShareNear" : local.Boundary < 0 ? "PZAECDefenseShareStart" : "PZAECDefenseShareSafe";
                        GameManager.ShowTooltip(player, string.Format(Localization.Get(key), local.Range, Math.Ceiling(farthest)));
                        local.Boundary = state; local.NextNotice = Time.time + 20;
                    }
                }
            }
            catch (Exception ex) { Log("Participant update failed: " + ex.GetBaseException().Message); }
        }
        private static void Tell(EntityPlayerLocal player, string key) { GameManager.ShowTooltip(player, Localization.Get(key)); }
        private static void Log(string text) { T16RuntimeFixMod.SafeLog("[AEC-DefenseShare] " + text); }
    }

    public sealed class NetPackagePZAECDefenseRequest : NetPackage
    {
        public byte Op, Tier;
        public int Owner, Code;
        public override NetPackageDirection PackageDirection { get { return NetPackageDirection.ToServer; } }
        public NetPackagePZAECDefenseRequest Setup(byte op, int owner, int code, int tier)
        { Op = op; Owner = owner; Code = code; Tier = (byte)tier; return this; }
        public override int GetLength() { return 10; }
        public override void read(PooledBinaryReader r) { Op = r.ReadByte(); Owner = r.ReadInt32(); Code = r.ReadInt32(); Tier = r.ReadByte(); }
        public override void write(PooledBinaryWriter w) { base.write(w); w.Write(Op); w.Write(Owner); w.Write(Code); w.Write(Tier); }
        public override void ProcessPackage(World world, GameManager callbacks)
        {
            if (world == null || !(ConnectionManager.Instance?.IsServer ?? false) || Sender == null || !Sender.loginDone || !Sender.bAttachedToEntity) return;
            LegendaryDefenseSharing.Request(world, Sender.entityId, Op, Owner, Code, Tier);
        }
    }
    public sealed class NetPackagePZAECDefenseState : NetPackage
    {
        public int Recipient, Owner, Code, Revision, Range;
        public byte Tier, Wave, Status, Rank;
        public Vector3 Anchor;
        public byte[] Counts = new byte[11];
        public override NetPackageDirection PackageDirection { get { return NetPackageDirection.ToClient; } }
        public NetPackagePZAECDefenseState Setup(int recipient, int owner, int code, int tier, int wave, byte status, byte rank,
            int revision, Vector3 anchor, int range, byte[] counts)
        { Recipient = recipient; Owner = owner; Code = code; Tier = (byte)tier; Wave = (byte)wave; Status = status; Rank = rank;
            Revision = revision; Anchor = anchor; Range = range; Counts = (byte[])counts.Clone(); return this; }
        public override int GetLength() { return 47; }
        public override void read(PooledBinaryReader r)
        {
            Recipient = r.ReadInt32(); Owner = r.ReadInt32(); Code = r.ReadInt32(); Tier = r.ReadByte(); Wave = r.ReadByte(); Status = r.ReadByte(); Rank = r.ReadByte();
            Revision = r.ReadInt32(); Anchor = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); Range = r.ReadInt32(); Counts = r.ReadBytes(11);
        }
        public override void write(PooledBinaryWriter w)
        {
            base.write(w); w.Write(Recipient); w.Write(Owner); w.Write(Code); w.Write(Tier); w.Write(Wave); w.Write(Status); w.Write(Rank);
            w.Write(Revision); w.Write(Anchor.x); w.Write(Anchor.y); w.Write(Anchor.z); w.Write(Range); w.Write(Counts);
        }
        public override void ProcessPackage(World world, GameManager callbacks)
        {
            if (world == null || ConnectionManager.Instance == null || ConnectionManager.Instance.IsServer) return;
            if (world.GetEntity(Recipient) is EntityPlayerLocal local) LegendaryDefenseSharing.Apply(local, this);
        }
    }
}
