using System;
using System.Collections.Generic;

namespace SakuraPreview
{
    public enum EscortPhase { Following, Ambush, Completed, Failed, Searching }
    public sealed class EscortMember
    {
        public string Key;
        public double NearSeconds;
        // 0=unclaimed, 1=durable intent (manual reconciliation after crash), 2=issued.
        public int Receipt;
    }
    public sealed class SakuraMissionState
    {
        public string Id=Guid.NewGuid().ToString("N");
        public int NpcId,Tier;
        public string Leader,Failure="";
        public float TargetX,TargetZ,StartDistance;
        public EscortPhase Phase;
        public bool Paused;
        public bool Guard;
        public int Waves;
        public double Elapsed,MissingSeconds,WaveDelay;
        public double TerminalUtc;
        public List<int> Enemies=new List<int>();
        public List<EscortMember> Members=new List<EscortMember>();
        public int WaveCount => Tier-14;
        public int WaveSize => 6+2*(Tier-16);
        public int Coins => 4000*(Tier-15);
        public int XP => 10000+5000*(Tier-16);
        public string RewardBox => "itemPZAECBossLootBundleT"+Tier;
        public List<string> NextRoster()
        {
            if(Tier<16||Tier>19||Waves>=WaveCount)throw new InvalidOperationException("No next wave");
            var roster=new List<string>();
            // Replace light enemies with heavies as the escort advances; total stays capped.
            int tanks=1+Waves;
            for(int i=0;i<WaveSize;i++)
                roster.Add("sakuraAmbushT"+Tier+(i<tanks?"Tank":i%2==0?"Runner":"Soldier"));
            if(Waves==WaveCount-1)roster[roster.Count-1]="sakuraAmbushT"+Tier+"Boss";
            return roster;
        }
        public bool ShouldDespawnNpc(bool randomEncounter,double now)
        {
            return Phase==EscortPhase.Failed || Phase==EscortPhase.Completed && randomEncounter && TerminalUtc>0 && now-TerminalUtc>1800;
        }
        public bool Active => Phase==EscortPhase.Following || Phase==EscortPhase.Ambush || Phase==EscortPhase.Searching;
        public bool TryJoin(string key,bool sameParty,bool busy)
        {
            if(!Active || string.IsNullOrEmpty(key) || !sameParty || busy || Members.Exists(m=>m.Key==key))return false;
            Members.Add(new EscortMember{Key=key});return true;
        }
        public void Fail(string why){if(Active){Phase=EscortPhase.Failed;Failure=why;}}
        public void Tick(double seconds,bool npcAlive,bool leaderNear,float distance,ISet<string> eligibleNear)
        {
            if(!Active || Phase==EscortPhase.Searching)return;
            if(double.IsNaN(seconds)||double.IsInfinity(seconds)||seconds<=0||seconds>5)throw new ArgumentOutOfRangeException(nameof(seconds));
            Elapsed+=seconds;WaveDelay+=seconds;
            if(!npcAlive){Fail(Guard?"Mint 倒下了":"小樱倒下了");return;}
            if(Guard && (float.IsNaN(distance)||float.IsInfinity(distance)||distance>5)){Fail("守护目标离开据点");return;}
            if(Elapsed>=1800){Fail("超过 30 分钟");return;}
            MissingSeconds=leaderNear?0:MissingSeconds+seconds;
            if(MissingSeconds>=60){Fail("带领者离开或死亡超过 60 秒");return;}
            foreach(var member in Members)if(eligibleNear!=null && eligibleNear.Contains(member.Key))member.NearSeconds+=seconds;
            if(Phase==EscortPhase.Following && Waves==WaveCount && Enemies.Count==0 && leaderNear &&
               !Paused && distance>=0 && distance<=(Guard?5:18) && Elapsed>=60)Phase=EscortPhase.Completed;
        }
        public bool WantWave(float distance,bool leaderNear)
        {
            if(Phase!=EscortPhase.Following || Paused || !leaderNear || Waves>=WaveCount || WaveDelay<12 ||
               float.IsNaN(distance)||float.IsInfinity(distance)||distance<0)return false;
            // At a trader, the test encounter still requires every wave; never instant reward.
            return Guard || StartDistance<150 || distance<=StartDistance*(1f-(Waves+1f)/(WaveCount+1));
        }
        public void CommitWave(List<int> ids)
        {
            if(Phase!=EscortPhase.Following || Waves>=WaveCount || ids==null || ids.Count!=WaveSize ||
               new HashSet<int>(ids).Count!=ids.Count || ids.Exists(id=>id<=0))throw new InvalidOperationException("Incomplete enemy wave");
            Enemies=new List<int>(ids);Waves++;WaveDelay=0;Phase=EscortPhase.Ambush;
        }
        // Called by the authoritative death event, before corpse disposal can hide the entity.
        public bool EnemyDied(int id)
        {
            if(Phase!=EscortPhase.Ambush || !Enemies.Remove(id))return false;
            if(Enemies.Count==0){Phase=EscortPhase.Following;WaveDelay=0;}
            return true;
        }
        public void EnemyObserved(int id,bool exists,bool dead)
        {
            if(Phase!=EscortPhase.Ambush || !Enemies.Contains(id))return;
            if(!exists){Fail("伏击敌人卸载或丢失，未计作击杀");return;}
            if(dead)EnemyDied(id);
        }
        public bool CanClaim(string key)
        {
            var member=Members.Find(m=>m.Key==key);
            return Phase==EscortPhase.Completed && member!=null && member.Receipt==0 && member.NearSeconds>=Math.Max(10,Elapsed*.5);
        }
        public bool BeginClaim(string key)
        {if(!CanClaim(key))return false;Members.Find(m=>m.Key==key).Receipt=1;return true;}
    }
    public sealed class SakuraMissionJournal
    {
        public int Schema=1;
        public double NextEncounterUtc;
        public List<int> EncounterIds=new List<int>();
        public List<SakuraMissionState> Missions=new List<SakuraMissionState>();
        public List<SakuraRescueGrant> RescueGrants=new List<SakuraRescueGrant>();
    }
    public sealed class SakuraRescueGrant
    {
        public string Key,QuestId;
        public int Code,TraderId,Tier;
        public bool Spawned;
    }
}


