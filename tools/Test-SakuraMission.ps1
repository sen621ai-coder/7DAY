#Requires -Version 7.0
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$source=Get-Content (Join-Path $root '96-SakuraPreview/Source/SakuraMissionState.cs') -Raw
$test=@'
public static class SakuraMissionRegression
{
    static int checks;
    static void Check(bool value,string name){checks++;if(!value)throw new System.Exception(name);}
    static SakuraPreview.SakuraMissionState New(int tier)
    {return new SakuraPreview.SakuraMissionState{Tier=tier,StartDistance=600,Leader="A",Members=new System.Collections.Generic.List<SakuraPreview.EscortMember>{new SakuraPreview.EscortMember{Key="A"},new SakuraPreview.EscortMember{Key="B"}}};}
    static readonly System.Collections.Generic.HashSet<string> Near=new System.Collections.Generic.HashSet<string>{"A"};
    public static string Run()
    {
        foreach(int tier in new[]{16,17,18,19})
        {
            var m=New(tier);
            Check(m.RewardBox=="itemPZAECBossLootBundleT"+tier,"reward box matches mission tier");
            Check(m.WaveCount==tier-14 && m.WaveSize==6+(tier-16)*2,"difficulty configuration");
            for(int i=0;i<65;i++)m.Tick(1,true,true,0,Near);
            Check(m.Active,"arrival cannot bypass waves");
            for(int wave=0;wave<m.WaveCount;wave++)
            {
                for(int i=0;i<12;i++)m.Tick(1,true,true,0,Near);
                Check(m.WantWave(0,true),"wave ready on arrival");
                var roster=m.NextRoster();
                Check(roster.Count==m.WaveSize,"mixed wave respects entity cap");
                Check(roster.FindAll(n=>n.EndsWith("Boss")).Count==(wave==m.WaveCount-1?1:0),"exactly one boss only in final wave");
                Check(roster.Exists(n=>n.EndsWith("Runner"))&&roster.Exists(n=>n.EndsWith("Soldier"))&&roster.Exists(n=>n.EndsWith("Tank")),"mixed roles in every wave");
                Check(roster.FindAll(n=>n.EndsWith("Tank")).Count==wave+1,"heavier composition in later waves");
                var ids=new System.Collections.Generic.List<int>();for(int i=0;i<m.WaveSize;i++)ids.Add(100+wave*100+i);
                m.CommitWave(ids);
                Check(!m.WantWave(0,true),"no overlapping waves");
                m.EnemyObserved(99999,true,true);Check(m.Enemies.Count==m.WaveSize,"foreign kills ignored");
                foreach(var id in ids){m.EnemyObserved(id,true,true);m.EnemyObserved(id,true,true);}
                Check(m.Enemies.Count==0,"confirmed deaths deduplicated");
            }
            m.Tick(1,true,true,19,Near);Check(m.Active,"outside destination cannot complete");
            m.Tick(1,true,true,18,Near);Check(m.Phase==SakuraPreview.EscortPhase.Completed,"all waves and arrival complete");
            Check(!m.CanClaim("B")&&!m.CanClaim("stranger"),"absent and foreign members excluded");
            Check(m.BeginClaim("A"),"earned reward intent");Check(!m.BeginClaim("A"),"no duplicate claim");
            var serializer=new System.Xml.Serialization.XmlSerializer(typeof(SakuraPreview.SakuraMissionState));
            using(var sw=new System.IO.StringWriter())
            {
                serializer.Serialize(sw,m);using(var sr=new System.IO.StringReader(sw.ToString()))
                {var restored=(SakuraPreview.SakuraMissionState)serializer.Deserialize(sr);Check(!restored.BeginClaim("A"),"pending intent survives restart without replay");}
            }
        }
        foreach(bool guard in new[]{false,true})
        {
            var fast=New(16);fast.Guard=guard;fast.CommitWave(new System.Collections.Generic.List<int>{1,2,3,4,5,6});
            Check(!fast.EnemyDied(999),"unrelated death ignored");
            for(int id=1;id<=6;id++)
            {
                Check(fast.EnemyDied(id),"death event counted before corpse removal");
                Check(!fast.EnemyDied(id),"duplicate death event ignored");
                fast.EnemyObserved(id,false,false);
                Check(fast.Active,"removed confirmed corpse does not fail mission");
            }
            Check(fast.Enemies.Count==0&&fast.Phase==SakuraPreview.EscortPhase.Following,"final event closes exactly one wave");
            Check(fast.Waves==1&&!fast.WantWave(0,true),"death events preserve next-wave delay");
            var failed=New(16);failed.CommitWave(new System.Collections.Generic.List<int>{1,2,3,4,5,6});failed.Fail("cancelled");
            Check(!failed.EnemyDied(1)&&failed.Phase==SakuraPreview.EscortPhase.Failed,"late death cannot reopen failed mission");
        }
        var search=New(17);search.Phase=SakuraPreview.EscortPhase.Searching;
        for(int i=0;i<400;i++)search.Tick(5,false,false,9999,null);
        Check(search.Active&&search.Elapsed==0&&search.MissingSeconds==0,"search does not run escort timeout, unload failure or distance grace");
        search.WaveDelay=999;Check(!search.WantWave(0,true)&&!search.CanClaim("A"),"search cannot start waves or rewards");
        search.Fail("abandoned");Check(search.ShouldDespawnNpc(true,0),"search abandonment retires target");
        Check((int)SakuraPreview.EscortPhase.Failed==3&&(int)SakuraPreview.EscortPhase.Searching==4,"existing serialized phase numbers preserved");
        var saved=new SakuraPreview.SakuraMissionJournal();saved.RescueGrants.Add(new SakuraPreview.SakuraRescueGrant{Key="A",QuestId="aec_quest_T16_A1_clear",Code=42,TraderId=7,Tier=16,Spawned=true});
        var rescueSerializer=new System.Xml.Serialization.XmlSerializer(typeof(SakuraPreview.SakuraMissionJournal));
        using(var stream=new System.IO.MemoryStream()){rescueSerializer.Serialize(stream,saved);stream.Position=0;var loaded=(SakuraPreview.SakuraMissionJournal)rescueSerializer.Deserialize(stream);Check(loaded.RescueGrants.Count==1&&loaded.RescueGrants[0].Code==42&&loaded.RescueGrants[0].Spawned,"rescue deduplication receipt survives save reload");}
        var shared=New(16);shared.Phase=SakuraPreview.EscortPhase.Searching;
        Check(shared.TryJoin("C",true,false),"party member can join during search without distance restriction");
        Check(!shared.TryJoin("C",true,false)&&shared.Members.Count==3,"repeated sync does not duplicate membership");
        Check(!shared.TryJoin("D",false,false)&&!shared.TryJoin("D",true,true),"non-party and already-busy players excluded");
        shared.Phase=SakuraPreview.EscortPhase.Following;Check(shared.TryJoin("D",true,false),"late party member can join active escort");
        Check(shared.Members.Find(m=>m.Key=="D").NearSeconds==0,"late members do not inherit participation credit");
        shared.Phase=SakuraPreview.EscortPhase.Completed;Check(!shared.TryJoin("E",true,false),"completed mission cannot admit reward farmers");
        shared.Phase=SakuraPreview.EscortPhase.Failed;Check(!shared.TryJoin("E",true,false),"failed mission cannot be shared");
        var retirement=New(16);Check(!retirement.ShouldDespawnNpc(true,9999),"active NPC remains");
        retirement.Fail("failed");Check(retirement.ShouldDespawnNpc(false,0),"failed fixed NPC disappears immediately");Check(retirement.ShouldDespawnNpc(true,0),"failed random NPC disappears immediately");
        retirement.Guard=true;Check(retirement.ShouldDespawnNpc(false,0),"failed Mint also disappears");
        retirement.Phase=SakuraPreview.EscortPhase.Completed;retirement.TerminalUtc=100;
        Check(!retirement.ShouldDespawnNpc(true,1900),"success retains full reward claim window");
        Check(retirement.ShouldDespawnNpc(true,1901),"successful random NPC retires after claim window");
        Check(!retirement.ShouldDespawnNpc(false,1901),"successful fixed NPC retained");
        var lost=New(16);for(int i=0;i<59;i++)lost.Tick(1,true,false,600,Near);Check(lost.Active,"59 seconds grace");
        lost.Tick(1,true,false,600,Near);Check(lost.Phase==SakuraPreview.EscortPhase.Failed,"60 seconds abandonment");
        var dead=New(16);dead.Tick(1,false,true,0,Near);Check(dead.Phase==SakuraPreview.EscortPhase.Failed,"NPC death fails");
        var timeout=New(16);for(int i=0;i<360;i++)timeout.Tick(5,true,true,600,Near);Check(timeout.Phase==SakuraPreview.EscortPhase.Failed,"30 minute timeout");
        var missing=New(16);missing.WaveDelay=12;missing.CommitWave(new System.Collections.Generic.List<int>{1,2,3,4,5,6});missing.EnemyObserved(1,false,false);Check(missing.Phase==SakuraPreview.EscortPhase.Failed,"unloaded enemy never counts as dead");
        var paused=New(16);paused.Paused=true;paused.WaveDelay=12;Check(!paused.WantWave(0,true),"pause prevents next wave");
        var partial=New(16);bool threw=false;try{partial.CommitWave(new System.Collections.Generic.List<int>{1});}catch(System.InvalidOperationException){threw=true;}Check(threw&&partial.Waves==0,"partial spawns cannot commit");
        return "PASS: "+checks+" mission checks (all tiers, wave gates, failure, participants and durable reward deduplication).";
    }
}
'@
Add-Type -TypeDefinition ($source+[Environment]::NewLine+$test)
[SakuraMissionRegression]::Run()


