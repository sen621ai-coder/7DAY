#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Test-LegendaryAdventure.ps1')
Add-Type -ReferencedAssemblies ($references + $frameworkReferences) -TypeDefinition @'
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using AECT16RuntimeFix;
using GameEvent.SequenceActions;

public static class DefenseRegression
{
    static void Check(bool ok,string why) { if(!ok) throw new Exception(why); }
    public static string State()
    {
        foreach(int t in new[]{16,17,18,19}) {
            Check(LegendaryDefense.Tier("PZAECDefenseT"+t)==t,"Tier mapping");
            foreach(string kind in new[]{"bulwark","storm"})
                Check(LegendaryAdventure.EncounterKind("PZAECDefense_"+kind+"_T"+t+"_W3")==kind,"Defense boss weakness missing");
        }
        foreach(string s in new[]{null,"","PZAECDefenseT15","PZAECDefenseT20","PZAECDefenseT19\n","PZAECDefenseT19W1","aec_quest_T19_A1_clear"})
            Check(LegendaryDefense.Tier(s)==0,"Out-of-scope quest");
        Check(LegendaryDefense.BossKind("PZAECDefense_storm_T19_W2")=="","Other wave given weakness");
        Check(LegendaryDefense.EnemyTier("PZAECDefense_storm_T19_W2")==0,"Invalid defense role/wave recognized");
        Check(LegendaryDefense.EnemyTier("PZAECDefense_wolf_T20_W2")==0,"Future-tier navigation assumed");
        Check(LegendaryDefense.HasActive(new[]{"unrelated","PZAECDefenseT17"}),"Cross-tier active defense missed");
        Check(!LegendaryDefense.HasActive(new[]{"PZAECChallengeT19"}),"Independent trial blocked");
        Check(LegendaryDefense.Within(45,60,0,0,0,0),"Exact area boundary");
        Check(!LegendaryDefense.Within(45.01f,0,0,0,0,0),"Radius exceeded");
        Check(!LegendaryDefense.Within(0,60.01f,0,0,0,0),"Height exceeded");
        Check(!LegendaryDefense.Within(32,0,32,0,0,0),"Square accepted instead of circle");
        Check(LegendaryDefense.Within(-100,90,200,-101,100,199),"Nonzero anchor");
        foreach(float f in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity,float.MaxValue})
            Check(!LegendaryDefense.Within(f,0,0,0,0,0),"Invalid coordinates");
        var clock=new LegendaryDefense.WaveClock(1,100);
        Check(clock.ReadyAt==120 && clock.Deadline==1020,"Preparation or wave deadline");
        Check(clock.Check(110,true,true,false,false)==null,"No retreat grace");
        Check(clock.Check(124.9,true,true,false,false)==null,"Grace too short");
        Check(clock.Check(125,true,true,false,false)=="PZAECDefenseRetreated","Grace not enforced");
        Check(clock.Check(126,true,true,false,true)==null && clock.OutsideSince<0,"Return did not reset grace");
        Check(clock.Check(1019.9,true,true,false,true)==null,"Premature timeout");
        Check(clock.Check(1020,true,true,false,true)=="PZAECDefenseTimeout","Timeout not enforced");
        Check(clock.Check(130,false,true,false,true)=="PZAECDefenseDeath","Death ignored");
        Check(clock.Check(130,true,false,false,true)=="PZAECDefenseUnavailable","Spawning disabled ignored");
        Check(clock.Check(130,true,true,true,true)=="PZAECDefenseUnavailable","Trader protection ignored");
        clock.MoveTo(2,1019.9);
        Check(clock.ReadyAt==1034.9 && clock.Check(1020,true,true,false,true)==null,"Old deadline applied to completed wave");
        clock.MoveTo(3,1200);
        Check(clock.ReadyAt==1215,"Intermission incorrect");
        bool rejected=false; try{clock.MoveTo(1,1500);}catch(InvalidOperationException){rejected=true;}
        Check(rejected,"Wave replay allowed");
        var random=new Random(8124); int cases=0;
        foreach(int code in new[]{int.MinValue,-123456789,-1,1,65535,65536,123456789,int.MaxValue}.Concat(Enumerable.Range(0,500).Select(_=>random.Next())))
        for(int tier=16;tier<=19;tier++) for(int wave=1;wave<=3;wave++) {
            string ev=LegendaryDefense.EventId(tier,wave), tag=LegendaryDefense.Request(code,tier,wave);
            Check(LegendaryDefense.ScopeMatches(ev,tag,LegendaryDefense.High(code),LegendaryDefense.Low(code),wave),"Signed code lost through float scope");
            Check(!LegendaryDefense.ScopeMatches(ev,tag+"extra",LegendaryDefense.High(code),LegendaryDefense.Low(code),wave),"Foreign request accepted");
            Check(!LegendaryDefense.ScopeMatches(ev,tag,LegendaryDefense.High(code),LegendaryDefense.Low(code),0),"Revoked lease wave accepted");
            cases++;
        }
        Check(!LegendaryDefense.ScopeMatches("PZAECDefenseT19W3",LegendaryDefense.Request(0,19,3),0,0,3),"Empty scope accepted");
        Check(!LegendaryDefense.ScopeMatches("PZAECDefenseT19W3","bad",float.NaN,1,3),"NaN scope");
        var data=new Dictionary<string,string>(); string marker=LegendaryDefense.WaveMarker(1), eid=LegendaryDefense.EventId(19,1), req=LegendaryDefense.Request(-42,19,1);
        int calls=0;
        Check(LegendaryAdventure.DispatchOnce(data,marker,eid,e=>{calls++;return true;}),"First wave not queued");
        Check(!LegendaryAdventure.DispatchOnce(data,marker,eid,e=>{calls++;return true;}) && calls==1,"Wave duplicated");
        Check(!LegendaryDefense.ApplyReply(data,-42,19,1,eid,"another run",false),"Foreign denial altered state");
        Check(LegendaryDefense.ApplyReply(data,-42,19,1,eid,req,false) && !data.ContainsKey(marker),"Server-denied wave not retryable");
        Check(LegendaryAdventure.DispatchOnce(data,marker,eid,e=>true),"Denied retry failed");
        Check(LegendaryDefense.ApplyReply(data,-42,19,1,eid,req,true),"Approval not recorded");
        Check(!LegendaryDefense.ApplyReply(data,-42,19,1,eid,req,false) && data.ContainsKey(marker),"Late denial replays approved wave");
        Check(LegendaryAdventure.DispatchOnce(data,LegendaryDefense.WaveMarker(2),LegendaryDefense.EventId(19,2),e=>true),"Next wave blocked");
        Check(!LegendaryAdventure.DispatchOnce(new Dictionary<string,string>(data),marker,eid,e=>true),"Reload dedup lost");
        var action=(ActionSpawnEntity)RuntimeHelpers.GetUninitializedObject(typeof(ActionSpawnEntity));
        var seq=(GameEventActionSequence)RuntimeHelpers.GetUninitializedObject(typeof(GameEventActionSequence)); action.owner=seq;
        seq.Name="PZAECTrialT19"; var result=BaseAction.ActionCompleteStates.InComplete;
        Check(LegendaryDefense.BeforeNativeSpawn(action,ref result) && result==BaseAction.ActionCompleteStates.InComplete,"First-batch spawning changed");
        seq.Name="PZAECDefenseT19W1";
        Check(!LegendaryDefense.BeforeNativeSpawn(action,ref result) && result==BaseAction.ActionCompleteStates.Complete,"Missing defense owner still spawns");
        var item=(ItemActionQuest)RuntimeHelpers.GetUninitializedObject(typeof(ItemActionQuest)); item.QuestGiven="PZAECChallengeT19";
        bool used=true; Check(LegendaryDefense.BeforeUse(item,null,ref used) && used,"Unrelated quest item intercepted");
        return "PASS: three-wave timing, exact area/height, death/retreat/timeout, cross-tier concurrency, "+cases+" signed scope cases, denials/approvals and unrelated hook isolation.";
    }
    public static string Wire()
    {
        int count=0;
        foreach(int code in new[]{int.MinValue,-42,1,int.MaxValue}) for(int tier=16;tier<=19;tier++) for(int wave=1;wave<=3;wave++)
        {
            string tag=LegendaryDefense.Request(code,tier,wave);
            var original=new EntityCreationData {entityClass=123456789,id=51,spawnById=-1,spawnByName=tag};
            using(var stream=new MemoryStream()) {
                var writer=new PooledBinaryWriter(); writer.SetBaseStream(stream);
                original.write(writer,true); writer.Flush(); stream.Position=0;
                var reader=new PooledBinaryReader(); reader.SetBaseStream(stream);
                var received=new EntityCreationData(); received.read(reader,true);
                Check(received.spawnByName==tag && received.spawnById==-1 && received.id==51,"Spawn ownership lost in INITIAL packet");
                Check(stream.Position==stream.Length,"Entity packet size mismatch");
            }
            count++;
        }
        using(var module=Mono.Cecil.ModuleDefinition.ReadModule(typeof(Quest).Assembly.Location))
        using(var mod=Mono.Cecil.ModuleDefinition.ReadModule(typeof(LegendaryDefense).Assembly.Location))
        {
            var spawn=module.Types.Single(t=>t.FullName=="GameEvent.SequenceActions.ActionBaseSpawn").Methods.Single(m=>m.Name=="SpawnEntity");
            Check(spawn.Body.Instructions.Any(i=>i.Operand is Mono.Cecil.FieldReference f && f.Name=="ExtraData"),"Event extraData no longer reaches factory");
            Check(spawn.Body.Instructions.Any(i=>i.Operand is Mono.Cecil.MethodReference m && m.Name=="CreateEntity" && m.Parameters.Last().ParameterType.FullName=="System.String"),"Spawn attribution factory changed");
            var packet=module.Types.Single(t=>t.Name=="NetPackageGameEventRequest");
            foreach(string method in new[]{"read","write"}) foreach(string field in new[]{"tag","extraData"})
                Check(packet.Methods.Single(m=>m.Name==method).Body.Instructions.Any(i=>i.Operand is Mono.Cecil.FieldReference f && f.Name==field),"Game-event wire dropped "+field);
            var helper=mod.Types.Single(t=>t.FullName=="AECT16RuntimeFix.LegendaryDefense");
            var publish=helper.Methods.Single(m=>m.Name=="Publish");
            var ops=publish.Body.Instructions; int forced=0;
            for(int i=1;i<ops.Count;i++) if(ops[i].Operand is Mono.Cecil.MethodReference m && m.Name=="SetCustomVar") {
                Check(ops[i-1].OpCode.Code==Mono.Cecil.Cil.Code.Ldc_I4_1,"Owner CVars not force-sent by joining client"); forced++;
            }
            Check(forced==6,"Missing scope coordinates/identity/wave fields");
            Check(helper.Methods.Single(m=>m.Name=="BeforeKill").Body.Instructions.Any(i=>i.Operand is Mono.Cecil.FieldReference f && f.Name=="spawnByName"),"Kill credit not encounter-specific");
            var killOps=helper.Methods.Single(m=>m.Name=="BeforeKill").Body.Instructions.ToList();
            int phaseGuard=killOps.FindIndex(i=>i.Operand is Mono.Cecil.MethodReference m && m.Name=="get_CurrentPhase");
            int deadlineCheck=killOps.FindIndex(i=>i.Operand is Mono.Cecil.MethodReference m && m.Name=="Check");
            Check(phaseGuard>=0 && phaseGuard<deadlineCheck,"Next-wave kill callback can fail against previous-wave deadline");
            var gate=helper.Methods.Single(m=>m.Name=="BeforeNativeSpawn");
            foreach(string field in new[]{"Tag","Requester","TargetPosition","position"})
                Check(gate.Body.Instructions.Any(i=>i.Operand is Mono.Cecil.FieldReference f && f.Name==field),"Missing server scope/anchor guard "+field);
            Check(helper.Methods.Single(m=>m.Name=="BeforeAccept").Body.Instructions.Any(i=>i.Operand is Mono.Cecil.MethodReference m && m.Name=="CanStart"),"No confirmation-time revalidation");
            Check(helper.Methods.Single(m=>m.Name=="BeforeClose").Body.Instructions.Any(i=>i.Operand is Mono.Cecil.MethodReference m && m.Name=="Revoke"),"Completion/abandon does not revoke");
            var journal=module.Types.Single(t=>t.Name=="QuestJournal");
            Check(journal.Methods.Single(m=>m.Name=="RemoveQuest").Body.Instructions.Any(i=>i.Operand is Mono.Cecil.MethodReference m && m.Name=="CloseQuest"),"Native abandon hook changed");
            var accept=module.Types.Single(t=>t.Name=="XUiC_QuestOfferWindow").Methods.Single(m=>m.Name=="btnAccept_OnPress");
            Check(accept.Body.Instructions.Any(i=>i.Operand is Mono.Cecil.MethodReference m && m.Name=="AddQuest"),"Native accept handler changed");
        }
        return "PASS: "+count+" real EntityCreationData network round-trips preserve encounter identity; native event metadata, forced client CVars, fixed origin, kill identity and confirmation/cancellation hooks verified.";
    }
}
'@
[DefenseRegression]::State()
[DefenseRegression]::Wire()

function Assert-Defense([bool]$condition,[string]$message) { if (-not $condition) { throw $message } }
[xml]$recipes=Get-Content (Join-Path $configRoot 'recipes.xml') -Raw
$defQuests=@($quests.SelectNodes('//quest[starts-with(@id,"PZAECDefense")]'))
$defEntities=@($entities.SelectNodes('//entity_class[starts-with(@name,"PZAECDefense")]'))
$defItems=@($items.SelectNodes('//item[starts-with(@name,"PZAECDefense")]'))
$defEvents=@($events.SelectNodes('//action_sequence[starts-with(@name,"PZAECDefense")]'))
Assert-Defense ($defQuests.Count -eq 4 -and $defEntities.Count -eq 44 -and $defItems.Count -eq 4 -and $defEvents.Count -eq 12) 'Wrong defense definition counts'
Assert-Defense ($recipes.SelectNodes('//recipe[starts-with(@name,"PZAECDefenseBeacon")]').Count -eq 4) 'Wrong beacon recipe count'
$expectedWaves=@(
    @{runner=4;spider=2;cop=3},
    @{biker=4;demo=2;spitter=4;wolf=2},
    @{wight=5;cop=2;bulwark=1;storm=1}
)
foreach($tier in 16..19) {
    $qid="PZAECDefenseT$tier"; $itemId="PZAECDefenseBeaconT$tier"
    $quest=$quests.SelectSingleNode("//quest[@id='$qid']")
    Assert-Defense ($quest.SelectSingleNode("property[@name='completiontype']").value -eq 'AutoComplete') 'Defense must reward only automatic completion'
    Assert-Defense ($quest.SelectSingleNode("property[@name='shareable']").value -eq 'false') 'Defense must not duplicate shared journals'
    Assert-Defense ($quest.SelectNodes('objective').Count -eq 11 -and $quest.SelectNodes('action').Count -eq 0) 'Unexpected objective count or unguarded spawn action'
    foreach($wave in 1..3) {
        $sequence=$events.SelectSingleNode("//action_sequence[@name='${qid}W$wave']")
        $expected=$expectedWaves[$wave-1]
        Assert-Defense ($sequence.SelectNodes('action').Count -eq $expected.Count) 'Wrong action count'
        foreach($entry in $expected.GetEnumerator()) {
            $name="PZAECDefense_$($entry.Key)_T${tier}_W$wave"
            $entity=$entities.SelectSingleNode("//entity_class[@name='$name']")
            Assert-Defense ([AECT16RuntimeFix.LegendaryDefense]::EnemyTier($name) -eq $tier) 'Defense enemy lost high-tier navigation'
            $parent=$baseEntities.SelectSingleNode("//entity_class[@name='$($entity.extends)']")
            if(-not $parent){$parent=$entities.SelectSingleNode("//entity_class[@name='$($entity.extends)']")}
            Assert-Defense ($null -ne $parent -and ($entity.extends -match "T$tier|Tier$tier")) "Missing exact-tier parent $name"
            Assert-Defense ($entity.SelectNodes('effect_group').Count -eq 0) 'Defense must inherit existing combat and loot stats'
            $objective=$quest.SelectSingleNode("objective[@id='$name']")
            Assert-Defense ([int]$objective.phase -eq $wave -and [int]$objective.value -eq $entry.Value) "Wrong kill target $name"
            $action=$sequence.SelectSingleNode("action[property[@name='entity_names' and @value='$name']]")
            $props=@{'spawn_count'=[string]$entry.Value;'spawn_type'='NearPosition';'party_addition'='0';'ignore_multiplier'='true';'safe_spawn'='false';'min_distance'='32';'max_distance'='48'}
            foreach($p in $props.GetEnumerator()) { Assert-Defense ($action.SelectSingleNode("property[@name='$($p.Key)']").value -eq $p.Value) "Wrong guarded spawn $name / $($p.Key)" }
            Assert-Defense ($action.SelectNodes("property[@name='target_group']").Count -eq 0) 'Party-scaled spawning enabled'
            Assert-Defense ($localization.ContainsKey($name) -and $localization[$name].schinese.Contains("第${wave}波")) 'Missing wave label'
        }
        $total=($expected.Values | Measure-Object -Sum).Sum
        $ranged=0; foreach($role in @('cop','spitter','storm')) {if($expected.ContainsKey($role)){$ranged+=$expected[$role]}}
        Assert-Defense ($total -eq @(9,12,9)[$wave-1] -and $ranged*3 -eq $total) 'Wave count/ranged share changed'
    }
    $beacon=$items.SelectSingleNode("//item[@name='$itemId']")
    Assert-Defense ($beacon.SelectSingleNode("property[@class='Action0']/property[@name='QuestGiven']").value -eq $qid) 'Wrong beacon quest'
    Assert-Defense ($beacon.SelectSingleNode("property[@class='Action0']/property[@name='UseAnimation']").value -eq 'false') 'Use guard bypassed'
    $recipe=$recipes.SelectSingleNode("//recipe[@name='$itemId']")
    Assert-Defense ($recipe.craft_area -eq 'workbench' -and $recipe.count -eq '1' -and $recipe.use_ingredient_modifier -eq 'false') 'Wrong crafting station/count or discount exploit'
    foreach($cost in @{("PZAECChallengeVoucherT$tier")=1;'resourceDurablAlloys'=50;'resourceElectricParts'=20}.GetEnumerator()) {
        Assert-Defense ([int]$recipe.SelectSingleNode("ingredient[@name='$($cost.Key)']").count -eq $cost.Value) 'Wrong beacon cost'
    }
    Assert-Defense ($recipe.SelectNodes('ingredient').Count -eq 3) 'Unexpected extra cost'
    Assert-Defense ($quest.SelectNodes('reward').Count -eq 8 -and $quest.SelectNodes('reward[@ischosen or @stage]').Count -eq 0) 'Rewards must all be completion-only guarantees'
    Assert-Defense ($quest.SelectSingleNode("reward[@type='Item' and @id='PZAECBuildPartsR$($tier-14)']").value -eq '5') 'Wrong tactical components'
    $rewardValues=@{Exp=@(900000,1800000,3600000,6000000);aecUniversalToken=@(250,400,600,900);resourceLegendaryParts=@(10,20,35,60);resourceForgedSteel=@(100,150,200,300)}
    foreach($entry in $rewardValues.GetEnumerator()) {
        $selector=if($entry.Key -eq 'Exp'){"reward[@type='Exp']"}else{"reward[@id='$($entry.Key)']"}
        Assert-Defense ([int]$quest.SelectSingleNode($selector).value -eq $entry.Value[$tier-16]) 'Wrong defense reward gradient'
    }
    $expectedLoot=@($(if($tier -eq 16){'PZAECQuestWeaponsT16'}else{'PZAECQuestLegendWeapons'}),"PZAECQuestModsT$tier","PZAECQuestMaterialsT$tier")
    foreach($reward in $quest.SelectNodes("reward[@type='LootItem']")) {
        Assert-Defense ($expectedLoot -contains $reward.id -and [int]$reward.value -eq $tier -and $null -ne $baseLoot.SelectSingleNode("//lootgroup[@name='$($reward.id)']")) 'Wrong advanced reward pool'
    }
}
$lease=$buffs.SelectSingleNode('//buff[@name="buffPZAECDefenseLease"]')
Assert-Defense ($lease.duration.value -eq '8' -and $lease.stack_type.value -eq 'replace' -and $lease.SelectNodes('effect_group').Count -eq 0) 'Lease must expire/refresh without altering combat'
foreach($key in @('PZAECDefenseDesc','PZAECDefenseBeaconDesc')) { Assert-Defense ($localization[$key].schinese -match '拆毁' -and $localization[$key].schinese -match '不退') 'Missing destructive-risk/refund warning before activation' }
'PASS: 4 craftable tiers; 12 fixed-origin waves and 44 exact-tier entities; 9/12/9 counts with one-third initial ranged; native rewards, costs, lease and risk warnings verified.'
