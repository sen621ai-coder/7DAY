using System;
using System.Collections.Generic;

namespace AECT16RuntimeFix
{
    // Pilot model only. No installation hook, inventory entry, spawn or reward
    // side effect until a real route has passed the in-game survey.
    public static class EscortPrototype
    {
        public const double MoveRadius = 30, CreditRadius = 200, AbandonSeconds = 60;
        public enum Phase { Preparing, Moving, Ambush, Succeeded, Failed }
        public sealed class Member
        {
            public double ActiveSeconds;
        }
        public sealed class Run
        {
            public Phase State { get; private set; } = Phase.Preparing;
            public string Failure { get; private set; }
            public int Checkpoint { get; private set; }
            public double Health { get; private set; } = 100;
            public double Elapsed { get; private set; }
            public double Distance { get; private set; }
            public int Repairs { get; private set; }
            public readonly Dictionary<int, Member> Members = new Dictionary<int, Member>();
            private readonly HashSet<int> remaining = new HashSet<int>();
            private readonly HashSet<int> allSpawns = new HashSet<int>();
            private double outsideSeconds, lastRepair = -100;
            private readonly double length;
            public int RemainingEnemies { get { return remaining.Count; } }
            public bool Terminal { get { return State == Phase.Succeeded || State == Phase.Failed; } }
            public Run(double routeLength)
            {
                if (double.IsNaN(routeLength) || double.IsInfinity(routeLength) || routeLength < 300 || routeLength > 400)
                    throw new ArgumentOutOfRangeException(nameof(routeLength));
                length = routeLength;
            }
            public bool Join(int player, bool sameParty)
            {
                if (State != Phase.Preparing || player < 0 || !sameParty || Members.ContainsKey(player)) return false;
                Members.Add(player, new Member()); return true;
            }
            public bool Start()
            {
                if (State != Phase.Preparing || Members.Count == 0) return false;
                State = Phase.Moving; return true;
            }
            // The runtime adapter must supply server-observed living players in
            // the original party and distances from the transport, NOT killer.
            public void Tick(double seconds, IDictionary<int, double> eligibleDistances)
            {
                if (Terminal || State == Phase.Preparing) return;
                if (seconds <= 0 || seconds > 1 || double.IsNaN(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
                Elapsed += seconds;
                bool near = false, present = false;
                foreach (var entry in Members)
                {
                    double distance;
                    if (eligibleDistances == null || !eligibleDistances.TryGetValue(entry.Key, out distance) ||
                        double.IsNaN(distance) || distance < 0 || distance > CreditRadius) continue;
                    present = true;
                    entry.Value.ActiveSeconds += seconds;
                    if (distance <= MoveRadius) near = true;
                }
                outsideSeconds = present ? 0 : outsideSeconds + seconds;
                if (outsideSeconds >= AbandonSeconds) { Fail("abandoned"); return; }
                if (Elapsed >= 900) { Fail("timeout"); return; }
                if (State == Phase.Moving && near)
                {
                    // 1m/s pilot pace; never pass the next uncleared checkpoint.
                    Distance = Math.Min(length * (Checkpoint + 1) / 3, Distance + seconds);
                }
            }
            public bool AtCheckpoint { get { return State == Phase.Moving && Distance >= length * (Checkpoint + 1) / 3; } }
            // Call only AFTER all intended enemies have spawned successfully.
            // Partial-spawn failures must fail/clean up, not shrink the wave.
            public bool StartAmbush(int[] entityIds)
            {
                if (!AtCheckpoint || entityIds == null || entityIds.Length == 0) return false;
                var ids = new HashSet<int>(entityIds);
                if (ids.Count != entityIds.Length) return false;
                foreach (int id in ids) if (id == 0 || allSpawns.Contains(id)) return false;
                foreach (int id in ids) { allSpawns.Add(id); remaining.Add(id); }
                State = Phase.Ambush; return true;
            }
            public bool Killed(int entityId, bool serverConfirmedDeath)
            {
                if (State != Phase.Ambush || !serverConfirmedDeath || !remaining.Remove(entityId)) return false;
                if (remaining.Count == 0)
                {
                    Checkpoint++;
                    State = Checkpoint == 3 ? Phase.Succeeded : Phase.Moving;
                }
                return true;
            }
            public void MissingEnemy(int entityId)
            {
                if (State == Phase.Ambush && remaining.Contains(entityId)) Fail("enemy_missing");
            }
            public void Damage(double amount, bool playerCaused)
            {
                if (Terminal || State == Phase.Preparing || playerCaused || amount <= 0 || double.IsNaN(amount) || double.IsInfinity(amount)) return;
                Health = Math.Max(0, Health - amount);
                if (Health <= 0) Fail("transport_destroyed");
            }
            // Runtime adapter must atomically consume one repair kit first.
            // Pure model is not an inventory authorization boundary.
            public bool CanRepair(bool kitAvailable)
            { return !Terminal && State != Phase.Preparing && kitAvailable && Health < 100 && Repairs < 3 && Elapsed - lastRepair >= 30; }
            public bool Repair(bool kitConsumed)
            {
                if (!CanRepair(kitConsumed)) return false;
                Health = Math.Min(100, Health + 20); Repairs++; lastRepair = Elapsed; return true;
            }
            public bool RewardEligible(int player, bool alive, bool sameParty, double distance)
            {
                Member member;
                return State == Phase.Succeeded && alive && sameParty && distance >= 0 && distance <= CreditRadius &&
                    Members.TryGetValue(player, out member) && Elapsed > 0 && member.ActiveSeconds / Elapsed >= .6;
            }
            public void Fail(string reason)
            { if (!Terminal) { State = Phase.Failed; Failure = reason; } }
        }
    }
}
