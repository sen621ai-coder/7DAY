using System;
using System.Collections.Generic;
using System.Linq;

namespace SakuraEscort
{
    // Engine-independent authoritative rules. Inputs MUST be collected on the
    // server, never copied from an untrusted client message.
    public enum Phase { Waiting, Following, Paused, Completed, Failed }
    public enum Receipt { None, Pending, Granted }
    public sealed class Member
    {
        public string Id;
        public double SecondsNear;
        public Receipt Reward;
    }
    public sealed class TraderSite
    {
        public string Id;
        public double X, Z;
        public bool Enabled;
    }
    public sealed class EscortSession
    {
        public int Schema = 1;
        public string Id = Guid.NewGuid().ToString("N");
        public int Tier;
        public Phase State;
        // Stable platform/player IDs, not runtime entity IDs, survive restarts.
        public string Leader;
        public string Trader;
        public double TargetX, TargetZ, StartingDistance, BestDistance;
        public double MissingLeaderSeconds;
        public int WavesStarted;
        public bool WaveActive;
        public List<Member> Members = new List<Member>();

        public static int WaveCount(int tier)
        {
            if (tier < 16 || tier > 19) throw new ArgumentOutOfRangeException("tier");
            return tier - 14;
        }
        public static double Distance(double x, double z, double x2, double z2)
        { return Math.Sqrt((x-x2)*(x-x2)+(z-z2)*(z-z2)); }
        static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

        // Server passes approach distance after checking line of sight, alive
        // state, authenticated identity, and entitlement to this encounter.
        public bool Accept(string player, double playerDistance, double npcX, double npcZ,
            IEnumerable<TraderSite> sites, IEnumerable<string> nearbyParty)
        {
            WaveCount(Tier);
            if (State != Phase.Waiting || string.IsNullOrEmpty(player) ||
                !Finite(playerDistance) || playerDistance < 0 || playerDistance > 4 ||
                !Finite(npcX) || !Finite(npcZ)) return false;
            var target = sites.Where(t => t != null && t.Enabled && !string.IsNullOrEmpty(t.Id) &&
                Finite(t.X) && Finite(t.Z)).OrderBy(t => Distance(npcX,npcZ,t.X,t.Z)).ThenBy(t=>t.Id).FirstOrDefault();
            if (target == null) return false;
            double distance = Distance(npcX,npcZ,target.X,target.Z);
            // Spawn selection must keep encounters away from trader entrances.
            if (distance < 150) return false;
            Leader=player; Trader=target.Id; TargetX=target.X; TargetZ=target.Z;
            StartingDistance=BestDistance=distance;
            Members=(nearbyParty ?? new string[0]).Concat(new[]{player})
                .Where(p=>!string.IsNullOrEmpty(p)).Distinct().Select(p=>new Member { Id=p }).ToList();
            State=Phase.Following;
            return true;
        }
        public bool SetPaused(string requester, bool pause, double distance)
        {
            if (requester != Leader || !Finite(distance) || distance < 0 || distance > 4 ||
                (State != Phase.Following && State != Phase.Paused)) return false;
            State=pause ? Phase.Paused : Phase.Following;
            return true;
        }
        public bool TakeOver(string requester, double distance, bool oldLeaderAvailable)
        {
            if (oldLeaderAvailable || !Finite(distance) || distance < 0 || distance > 4 ||
                !Members.Any(m=>m.Id==requester) ||
                (State!=Phase.Following && State!=Phase.Paused)) return false;
            Leader=requester; MissingLeaderSeconds=0; State=Phase.Following; return true;
        }
        // Call once per bounded server tick. The adapter discovers nearby live
        // members itself and verifies they still belong to the accepted party.
        public void Tick(double dt, bool npcAlive, bool leaderAvailable, IEnumerable<string> nearbyMembers,
            double npcX, double npcZ, bool withinDestinationEntrance, bool enemiesNearby)
        {
            if (State!=Phase.Following && State!=Phase.Paused) return;
            if (!Finite(dt) || dt<=0 || dt>5) throw new ArgumentOutOfRangeException("dt");
            if (!npcAlive) { State=Phase.Failed; return; }
            if (!Finite(npcX) || !Finite(npcZ)) return;
            var nearby=new HashSet<string>(nearbyMembers ?? new string[0]);
            foreach(var m in Members) if(nearby.Contains(m.Id)) m.SecondsNear+=dt;
            MissingLeaderSeconds=leaderAvailable ? 0 : MissingLeaderSeconds+dt;
            if (!leaderAvailable) { State=Phase.Paused; return; }
            if (State!=Phase.Following) return;
            BestDistance=Math.Min(BestDistance,Distance(npcX,npcZ,TargetX,TargetZ));
            if (withinDestinationEntrance && !enemiesNearby && !WaveActive && WavesStarted==WaveCount(Tier))
                State=Phase.Completed;
        }
        public bool ShouldStartWave()
        {
            if(State!=Phase.Following || WaveActive || WavesStarted>=WaveCount(Tier) || StartingDistance<=0) return false;
            double progress=1-BestDistance/StartingDistance;
            return progress >= (WavesStarted+1.0)/(WaveCount(Tier)+1.0);
        }
        // Commit only after spawn succeeds; the adapter persists the session and
        // spawned entity identifiers together. Spawn failure must not consume a wave.
        public bool MarkWaveStarted()
        { if(!ShouldStartWave())return false; WavesStarted++;WaveActive=true;return true; }
        public void MarkWaveCleared() { WaveActive=false; }
        public bool BeginReward(string player)
        {
            var member=Members.FirstOrDefault(m=>m.Id==player);
            if(State!=Phase.Completed || member==null || member.SecondsNear<60 || member.Reward!=Receipt.None) return false;
            member.Reward=Receipt.Pending; return true;
        }
        public bool ConfirmReward(string player)
        {
            var member=Members.FirstOrDefault(m=>m.Id==player);
            if(member==null || member.Reward!=Receipt.Pending)return false;
            member.Reward=Receipt.Granted;return true;
        }
        // Pending is intentionally not automatically retried after a crash:
        // game inventory changes and this journal are not one atomic transaction.
        // The future adapter must reconcile a durable game-side receipt first.
    }
}
