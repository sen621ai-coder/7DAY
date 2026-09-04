#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
# Also exercises the real native offer packet with 20 clear, 20 infested,
# 20 fetch/fetch-clear and 60 affixed IDs.
. (Join-Path $PSScriptRoot 'Test-LegendaryNetworking.ps1')
Add-Type -ReferencedAssemblies ($references + $frameworkReferences) -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Linq;
using AECT16RuntimeFix;
using HarmonyLib;

public static class AdventureRegression
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    public static string Boundaries()
    {
        int replies=0, wire=0;
        foreach(int tier in new[]{16,17,18,19})
        foreach(string id in new[]{"PZAECChallengeT"+tier,"aec_quest_T"+tier+"_A1_clear_hunter","aec_quest_T"+tier+"_A1_clear"})
        foreach(string marker in new[]{LegendaryAdventure.SpawnMarker,LegendaryAdventure.VoucherMarker})
        {
            string eid=LegendaryAdventure.EventFor(id,marker);
            if(eid==null) continue;
            string tag=LegendaryAdventure.Request(-42,marker);
            var data=new Dictionary<string,string>();
            Check(LegendaryAdventure.DispatchOnce(data,marker,eid,e=>true),"Initial event not queued");
            Check(!LegendaryAdventure.ApplyReply(data,id,-43,eid,tag,false) && data.ContainsKey(marker),"Foreign quest denial changed state");
            Check(!LegendaryAdventure.ApplyReply(data,id,-42,eid+"bad",tag,false),"Foreign event denial changed state");
            Check(LegendaryAdventure.ApplyReply(data,id,-42,eid,tag,false) && !data.ContainsKey(marker),"Server denial permanently consumed dispatch");
            Check(LegendaryAdventure.DispatchOnce(data,marker,eid,e=>true),"Denied event not retryable");
            Check(LegendaryAdventure.ApplyReply(data,id,-42,eid,tag,true),"Approval not saved");
            Check(!LegendaryAdventure.ApplyReply(data,id,-42,eid,tag,false) && data.ContainsKey(marker),"Late denial duplicated approved encounter/reward");
            var loaded=new Dictionary<string,string>(data);
            Check(!LegendaryAdventure.DispatchOnce(loaded,marker,eid,e=>true),"Approved event duplicated after reload");
            replies++;
        }
        foreach(int code in new[]{int.MinValue,-42,1,int.MaxValue}) foreach(int tier in new[]{16,17,18,19})
        {
            string id="PZAECChallengeT"+tier, tag=LegendaryAdventure.Request(code,LegendaryAdventure.SpawnMarker);
            var original=new EntityCreationData {id=51,entityClass=123456789,spawnById=-1,spawnByName=tag};
            using(var stream=new System.IO.MemoryStream()) {
                var writer=new PooledBinaryWriter(); writer.SetBaseStream(stream); original.write(writer,true); writer.Flush(); stream.Position=0;
                var reader=new PooledBinaryReader(); reader.SetBaseStream(stream); var received=new EntityCreationData(); received.read(reader,true);
                Check(LegendaryAdventure.MatchesTrialKill(id,code,received.spawnByName),"Own trial kill lost in initial packet");
                Check(!LegendaryAdventure.MatchesTrialKill(id,unchecked(code+1),received.spawnByName),"Nearby/old trial borrowed kill");
                Check(!LegendaryAdventure.MatchesTrialKill(id,code,null) && !LegendaryAdventure.MatchesTrialKill(id,code,""),"Unattributed old enemy counted");
                Check(!LegendaryAdventure.MatchesTrialKill(id,code,LegendaryDefense.Request(code,tier,1)),"Defense enemy counted as trial");
                Check(received.spawnById==-1 && stream.Position==stream.Length,"Trial wire metadata changed Twitch ownership");
            }
            wire++;
        }
        // Exercise the actual prefix for missing-player and unrelated offers.
        var offer=(XUiC_QuestOfferWindow)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(XUiC_QuestOfferWindow));
        var quest=(Quest)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Quest));
        offer.Quest=quest; quest.ID="PZAECChallengeT19";
        Check(!LegendaryAdventure.BeforeAccept(offer),"Missing confirmation owner still consumes voucher");
        quest.ID="PZAECDefenseT19";
        Check(LegendaryAdventure.BeforeAccept(offer),"Defense confirmation intercepted by trial guard");
        // PowerShell's CoreCLR cannot install this Unity/Mono Harmony detour.
        // Verify target signatures and compiled wiring without claiming a live patch.
        Check(AccessTools.Method(typeof(ObjectiveEntityKill),"Current_EntityKill").GetParameters()
            .Any(p=>p.Name=="killedEntity" && p.ParameterType==typeof(EntityAlive)),"Trial kill hook signature changed");
        Check(AccessTools.Method(typeof(NetPackageGameEventResponse),"ProcessPackage").GetParameters()[0].ParameterType==typeof(World),"Reply hook first argument changed");
        using(var module=Mono.Cecil.ModuleDefinition.ReadModule(typeof(LegendaryAdventure).Assembly.Location)) {
            var helper=module.Types.Single(t=>t.FullName=="AECT16RuntimeFix.LegendaryAdventure");
            var install=helper.Methods.Single(m=>m.Name=="Install");
            foreach(string hook in new[]{"BeforeAccept","BeforeTrialKill","AfterReply"})
                Check(install.Body.Instructions.Any(i=>i.Operand is string s && s==hook),"Boundary hook not registered: "+hook);
            Check(helper.Methods.Single(m=>m.Name=="BeforeTrialKill").Body.Instructions.Any(i=>i.Operand is Mono.Cecil.FieldReference f && f.Name=="spawnByName"),"Trial attribution not checked in kill hook");
            var dispatch=helper.NestedTypes.SelectMany(t=>t.Methods).Single(m=>m.Name.Contains("b__") && m.Body.Instructions.Any(i=>i.Operand is Mono.Cecil.MethodReference r && r.Name=="HandleAction"));
            Check(dispatch.Body.Instructions.Count(i=>i.Operand is Mono.Cecil.MethodReference r && r.Name=="Request")==2,"Trial identity not sent in both extraData and tag");
        }
        return "PASS: "+replies+" scoped server approval/denial cases; "+wire+" native trial identity round-trips; confirmation guard and compiled hook signatures/wiring (no live detours).";
    }
    public static string Run()
    {
        var affixes = new[]{"hunter","bulwark","storm"};
        int offers = 0;
        for (int tier=16;tier<=19;tier++)
        for (int area=1;area<=5;area++)
        for (int roll=0;roll<3;roll++)
        {
            string id="aec_quest_T"+tier+"_A"+area+"_clear";
            var ids=Enumerable.Range(0,6).Select(slot=>LegendaryAdventure.OfferId(id,slot,roll)).ToList();
            Check(ids.Take(3).SequenceEqual(new[]{id,id+"_infested",id+"_fetch"}),"Mixed ordinary offer identities changed");
            Check(ids.Distinct(StringComparer.OrdinalIgnoreCase).Count()==6,"A page contains duplicate quest IDs");
            Check(ids.Skip(3).Select(LegendaryAdventure.Affix).OrderBy(s=>s).SequenceEqual(affixes.OrderBy(s=>s)),"Affix rotation wrong");
            ids.Insert(0,"other_difficulty"); ids.Insert(1,"special6");
            var tiers = new[]{2,6,6,6,6,6,6,6}; var ready = Enumerable.Repeat(true,8).ToArray();
            for (int slot=0;slot<6;slot++)
            {
                int removal;
                int actual=LegendaryQuestNetworking.ResolveIndex(ids,tiers,ready,id,slot,out removal);
                Check(actual==slot+2 && removal==slot+1,"Affix menu/removal index mismatch");
                Check(LegendaryAdventure.ContractTier(ids[actual])==tier,"Wrong tier");
                Check(LegendaryAdventure.IsClearTransition(ids[actual],3,4),"No clear voucher");
                Check(!LegendaryAdventure.IsClearTransition(ids[actual],2,3),"Voucher on rally");
                offers++;
            }
            ready[5]=false;
            Check(LegendaryQuestNetworking.ResolveIndex(ids,tiers,ready,id,3,out int sparse)==6 && sparse==5,"Unready variant consumed UI slot");
        }
        foreach(string id in new[]{null,"","aec_quest_T15_A1_clear","aec_quest_T20_A1_clear","aec_quest_T16_A0_clear",
            "aec_quest_T19_A6_clear","aec_quest_T17_A1_clear_extra","aec_quest_T19_A1_clear_hunter_extra","aec_quest_T19_A1_clear\n"})
            Check(LegendaryAdventure.ContractTier(id)==0,"Invalid contract accepted: "+id);
        Check(!LegendaryAdventure.MatchesPage("aec_quest_T19_A1_clear_hunter_extra","aec_quest_T19_A1_clear"),"Arbitrary suffix leaked");
        Check(!LegendaryAdventure.MatchesPage("aec_quest_T19_A1_clear_hunter","aec_quest_T18_A1_clear"),"Higher-tier leak");
        Check(LegendaryAdventure.OfferId("aec_quest_T15_A1_clear",4,1)=="aec_quest_T15_A1_clear","Lower offers changed");
        Check(LegendaryAdventure.IsOwner(true,-1,42) && LegendaryAdventure.IsOwner(true,42,42),"Owner rejected");
        Check(!LegendaryAdventure.IsOwner(true,42,43) && !LegendaryAdventure.IsOwner(false,-1,42),"Shared/inactive copy dispatches");
        Check(!LegendaryAdventure.IsOwner(true,-1,-1),"Missing player dispatches");
        for(int tier=16;tier<=19;tier++)
        {
            Check(LegendaryAdventure.ChallengeTier("PZAECChallengeT"+tier)==tier,"Challenge mapping");
            foreach(string a in affixes) foreach(string role in new[]{"Affix","Trial"})
                Check(LegendaryAdventure.EncounterKind("PZAEC"+role+"_"+a+"_T"+tier)==a,"Encounter mapping");
        }
        Check(LegendaryAdventure.ChallengeTier("PZAECChallengeT19_extra")==0,"Unknown challenge");
        Check(LegendaryAdventure.EncounterKind("AECTheExecutionerBossT19")=="","Ordinary boss got weakness");
        Check(LegendaryAdventure.EncounterKind("PZAECTrial_hunter_T20")=="","Future tier assumed");
        var data = new Dictionary<string,string>(); int calls=0;
        string mark=LegendaryAdventure.SpawnMarker;
        Check(LegendaryAdventure.DispatchOnce(data,mark,"event",e=>{calls++;return true;}),"Dispatch failed");
        Check(!LegendaryAdventure.DispatchOnce(data,mark,"event",e=>{calls++;return true;}) && calls==1,"Duplicate request");
        Check(!LegendaryAdventure.DispatchOnce(new Dictionary<string,string>(data),mark,"event",e=>true),"Reload duplicated request");
        Check(LegendaryAdventure.DispatchOnce(data,LegendaryAdventure.VoucherMarker,"voucher",e=>true),"Separate clear award blocked");
        data.Clear();
        Check(!LegendaryAdventure.DispatchOnce(data,mark,"event",e=>false) && data.Count==0,"Rejected event consumed allowance");
        try { LegendaryAdventure.DispatchOnce(data,mark,"event",e=>{throw new Exception();}); } catch(Exception) {}
        Check(data.Count==0,"Exception consumed allowance");
        Check(LegendaryAdventure.DispatchOnce(data,mark,"event",e=>{
            Check(!LegendaryAdventure.DispatchOnce(data,mark,e,x=>true),"Reentrant dispatch");return true;}),"Retry failed");
        int cases=0;
        foreach(string kind in new[]{"", "hunter","bulwark","storm"})
        foreach(int strength in new[]{-1,0,1,100,1000000000,int.MaxValue})
        foreach(bool player in new[]{false,true}) foreach(bool head in new[]{false,true})
        foreach(bool melee in new[]{false,true}) foreach(bool hot in new[]{false,true})
        {
            long multiplier=!player?1:kind=="hunter"&&head?2:(kind=="bulwark"&&hot||kind=="storm"&&melee)?3:1;
            int expected=strength<=0?strength:(int)Math.Min(int.MaxValue,strength*multiplier);
            Check(LegendaryAdventure.WeaknessDamage(kind,strength,player,head,melee,hot)==expected,"Weakness/overflow guard");cases++;
        }
        int damage=23; LegendaryAdventure.BeforeDamage(null,null,ref damage); Check(damage==23,"Null hit mutated");
        using(var game=Mono.Cecil.ModuleDefinition.ReadModule(typeof(Quest).Assembly.Location))
        using(var mod=Mono.Cecil.ModuleDefinition.ReadModule(typeof(LegendaryAdventure).Assembly.Location))
        {
            var quest=game.Types.Single(t=>t.Name=="Quest");
            foreach(string method in new[]{"Read","Write"})
                Check(quest.Methods.Where(m=>m.Name==method).Any(m=>m.Body.Instructions.Any(i=>i.Operand!=null && i.Operand.ToString().Contains("Quest::DataVariables"))),"Quest marker serialization changed");
            var packet=game.Types.Single(t=>t.Name=="NetPackageDamageEntity").Methods.Single(m=>m.Name=="ProcessPackage");
            var callsPacket=packet.Body.Instructions.Select(i=>i.Operand).OfType<Mono.Cecil.MethodReference>().ToList();
            Check(callsPacket.Any(m=>m.Name=="ProcessDamageResponse"),"Damage packet behavior changed");
            Check(!callsPacket.Any(m=>m.Name=="DamageEntity" || m.Name=="damageEntityLocal"),"Damage packet could double weakness multiplier");
            var alive=game.Types.Single(t=>t.Name=="EntityAlive");
            Check(alive.Methods.Single(m=>m.Name=="DamageEntity").Body.Instructions.Any(i=>i.Operand is Mono.Cecil.MethodReference m && m.Name=="damageEntityLocal"),"Hit no longer goes through hook");
            var hook=alive.Methods.Single(m=>m.Name=="damageEntityLocal");
            Check(hook.Parameters[0].Name=="_damageSource" && hook.Parameters[1].Name=="_strength","Harmony argument names changed");
            var helper=mod.Types.Single(t=>t.Name=="AECT16RuntimeFix.LegendaryAdventure" || t.FullName=="AECT16RuntimeFix.LegendaryAdventure");
            var damageHook=helper.Methods.Single(m=>m.Name=="BeforeDamage");
            Check(!damageHook.Body.Instructions.Any(i=>i.Operand is Mono.Cecil.MethodReference m && (m.Name=="IsRemote" || m.Name=="get_IsServer")),"Joining-client damage excluded");
            Check(damageHook.Body.Instructions.Any(i=>i.Operand is Mono.Cecil.FieldReference f && f.Name=="bTrapKillXP"),"Native trap hits not excluded");
            Check(damageHook.Body.Instructions.Any(i=>i.Operand is Mono.Cecil.FieldReference f && f.Name=="BuffClass"),"Buff/DOT hits not excluded");
            var buffs=game.Types.Single(t=>t.Name=="EntityBuffs");
            Check(buffs.Methods.Any(m=>m.Name=="AddBuffNetwork"),"Native buff network unavailable");
            var use=game.Types.Single(t=>t.Name=="ItemActionQuest").Methods.Single(m=>m.Name=="ExecuteInstantAction");
            Check(use.Body.Instructions.Any(i=>i.Operand is Mono.Cecil.MethodReference m && m.Name=="OpenQuestOfferWindow"),"Native confirmation unavailable");
            Check(use.Body.Instructions.Any(i=>i.Operand is Mono.Cecil.MethodReference m && m.Name=="FindQuest"),"Native duplicate-active guard missing");
        }
        return "PASS: "+offers+" offer mappings; owner/phase/reload/reentrancy guards; "+cases+" weakness cases; native quest save, confirmation and no-double-damage packet flow.";
    }
}
'@
[AdventureRegression]::Run()
[AdventureRegression]::Boundaries()

function Assert-Adventure([bool]$condition, [string]$message) { if (-not $condition) { throw $message } }
function Get-ObjectiveTemplate([xml]$document, $quest) {
    $seen=@{}
    while ($null -ne $quest -and $quest.SelectNodes('objective').Count -eq 0 -and $quest.template -and -not $seen.ContainsKey([string]$quest.id)) {
        $seen[[string]$quest.id]=$true
        $quest=$document.SelectSingleNode("/quests/quest[@id='$($quest.template)']")
    }
    return $quest
}
$configRoot = Join-Path $modRoot '99-AEC_T16_RuntimeFix/Config'
[xml]$quests = Get-Content (Join-Path $configRoot 'quests.xml') -Raw
[xml]$events = Get-Content (Join-Path $configRoot 'gameevents.xml') -Raw
[xml]$entities = Get-Content (Join-Path $configRoot 'entityclasses.xml') -Raw
[xml]$items = Get-Content (Join-Path $configRoot 'items.xml') -Raw
[xml]$buffs = Get-Content (Join-Path $configRoot 'buffs.xml') -Raw
[xml]$baseQuests = Get-Content (Join-Path $modRoot '98-AECxProjectZ_Tweaks/Config/quests.xml') -Raw
[xml]$nativeQuests = Get-Content (Join-Path $modRoot '../Data/Config/quests.xml') -Raw
[xml]$baseEntities = Get-Content (Join-Path $modRoot '98-AECxProjectZ_Tweaks/Config/entityclasses.xml') -Raw
[xml]$baseLoot = Get-Content (Join-Path $modRoot '98-AECxProjectZ_Tweaks/Config/loot.xml') -Raw
$localization = @{}
foreach ($row in Import-Csv (Join-Path $configRoot 'Localization.csv')) {
    Assert-Adventure (-not $localization.ContainsKey($row.Key)) "Duplicate localization $($row.Key)"
    $localization[$row.Key] = $row
}
Assert-Adventure ($quests.SelectNodes('//quest[not(starts-with(@id,"PZAECDefense"))]').Count -eq 104) 'Expected 40 mixed-type contracts, 60 affix contracts and four trials'
Assert-Adventure ($entities.SelectNodes('//entity_class[starts-with(@name,"PZAECAffix_") or starts-with(@name,"PZAECTrial_")]').Count -eq 24) 'Expected 24 isolated champions'
Assert-Adventure ($items.SelectNodes('//item[starts-with(@name,"PZAECChallengeVoucher")]').Count -eq 4) 'Expected four vouchers'
Assert-Adventure ($events.SelectNodes('//action_sequence[not(starts-with(@name,"PZAECDefense")) and not(starts-with(@name,"PZAECStronghold"))]').Count -eq 23) 'Original three + twenty adventure events required'
foreach ($tier in 16..19) {
    foreach ($area in 1..5) {
        $baseId = "aec_quest_T${tier}_A${area}_clear"
        $base = $baseQuests.SelectSingleNode("//quest[@id='$baseId']")
        foreach ($suffix in @('infested','fetch')) {
            $typed = $quests.SelectSingleNode("//quest[@id='${baseId}_$suffix']")
            $expectedTemplate = if ($suffix -eq 'infested') { "tier$($area+1)_clear_infested" } elseif ($area -eq 1) { 'tier1_fetch' } else { "tier${area}_fetch_clear" }
            Assert-Adventure ($null -ne $typed -and $typed.template -eq $expectedTemplate) "Broken mission template ${baseId}_$suffix"
            $nativeTemplate = $nativeQuests.SelectSingleNode("/quests/quest[@id='$expectedTemplate']")
            Assert-Adventure ($null -ne $nativeTemplate) "Missing native mission template $expectedTemplate"
            $objectiveTemplate = Get-ObjectiveTemplate $nativeQuests $nativeTemplate
            if ($suffix -eq 'infested') {
                Assert-Adventure ($objectiveTemplate.SelectNodes("objective[@type='ClearSleepers']").Count -eq 1) "Infestation lost clear objective $expectedTemplate"
            } else {
                Assert-Adventure ($objectiveTemplate.SelectNodes("objective[@type='FetchFromContainer']").Count -eq 1) "Fetch mission lost satchel objective $expectedTemplate"
                Assert-Adventure (($objectiveTemplate.SelectNodes("objective[@type='ClearSleepers']").Count -eq 1) -eq ($area -gt 1)) "Fetch/clear size mapping changed $expectedTemplate"
            }
            Assert-Adventure ($typed.SelectNodes('objective | action').Count -eq 0) "Typed child duplicated inherited objectives ${baseId}_$suffix"
            Assert-Adventure ($typed.SelectSingleNode("property[@name='reward_choices_count']").value -eq '3') "Wrong reward choice count ${baseId}_$suffix"
            Assert-Adventure ($typed.reward.Count -eq $base.reward.Count) "Reward count changed ${baseId}_$suffix"
            Assert-Adventure ($localization.ContainsKey($typed.SelectSingleNode("variable[@name='name']").value)) "Missing typed mission label ${baseId}_$suffix"
            Assert-Adventure ($localization.ContainsKey($typed.SelectSingleNode("variable[@name='offer']").value)) "Missing typed mission offer ${baseId}_$suffix"
        }
        foreach ($affix in @('hunter','bulwark','storm')) {
            $id = "${baseId}_$affix"
            $quest = $quests.SelectSingleNode("//quest[@id='$id']")
            Assert-Adventure ($null -ne $quest -and $quest.template -eq $base.template) "Missing/changed template $id"
            Assert-Adventure ($quest.SelectNodes('objective | action').Count -eq 0) 'Affix must retain native POI flow'
            Assert-Adventure ($quest.reward.Count -eq $base.reward.Count) "Reward count changed $id"
            for ($r=0; $r -lt $base.reward.Count; $r++) {
                $expected = $base.reward[$r].CloneNode($true)
                if ($expected.type -eq 'Exp' -or $expected.id -in @('casinoCoin','resourceAECMutationSampleT5')) {
                    $expected.SetAttribute('value', [string][int]([int]$expected.value * 1.25))
                }
                Assert-Adventure ($quest.reward[$r].OuterXml -eq $expected.OuterXml) "Unintended reward change $id / $r"
            }
            Assert-Adventure ($localization.ContainsKey("${id}_name") -and $localization["${id}_name"].schinese.Contains("T$tier")) "Missing affix label $id"
        }
    }
    $challenge = $quests.SelectSingleNode("//quest[@id='PZAECChallengeT$tier']")
    Assert-Adventure ($challenge.SelectSingleNode("property[@name='completiontype']").value -eq 'AutoComplete') 'Trial reward not automatic'
    Assert-Adventure ($challenge.SelectSingleNode("property[@name='shareable']").value -eq 'false') 'Instant trial must not duplicate shared owners'
    Assert-Adventure ($challenge.SelectNodes('objective').Count -eq 3) 'Require one of each champion'
    Assert-Adventure ($challenge.SelectNodes('reward[@ischosen]').Count -eq 0) 'Automatic rewards cannot need selection'
    Assert-Adventure ($challenge.SelectNodes('reward').Count -eq 6) 'Expected five original guarantees plus tactical components'
    Assert-Adventure ($challenge.SelectSingleNode("reward[@type='Item' and @id='PZAECBuildPartsR$($tier-14)']").value -eq '3') 'Wrong tactical components'
    foreach ($reward in $challenge.SelectNodes("reward[@type='LootItem']")) {
        Assert-Adventure ($null -ne $baseLoot.SelectSingleNode("//lootgroup[@name='$($reward.id)']") -and [int]$reward.value -eq $tier) 'Missing/wrong-tier trial reward pool'
    }
    $item = $items.SelectSingleNode("//item[@name='PZAECChallengeVoucherT$tier']")
    Assert-Adventure ($item.SelectSingleNode("property[@name='Extends']").value -eq 'questMaster') 'Voucher must use native quest-note flow'
    Assert-Adventure ($item.SelectSingleNode("property[@class='Action0']/property[@name='QuestGiven']").value -eq $challenge.id) 'Wrong voucher target'
    Assert-Adventure ($item.SelectSingleNode("property[@class='Action0']/property[@name='UseAnimation']").value -eq 'false') 'Voucher could bypass use guard'
    $voucherEvent = $events.SelectSingleNode("//action_sequence[@name='PZAECGiveVoucherT$tier']")
    Assert-Adventure ($voucherEvent.SelectSingleNode("action[@class='AddItems']/property[@name='added_items']").value -eq $item.name) 'Voucher reward target wrong'
    Assert-Adventure ($voucherEvent.SelectSingleNode("action[@class='AddItems']/property[@name='added_item_counts']").value -eq '1') 'One clear must give one voucher'
    $trial = $events.SelectSingleNode("//action_sequence[@name='PZAECTrialT$tier']")
    Assert-Adventure ($trial.SelectNodes("action[@class='SpawnEntity']").Count -eq 3) 'Trial needs three distinct spawn actions'
    foreach ($affix in @('hunter','bulwark','storm')) {
        foreach ($role in @('Affix','Trial')) {
            $name = "PZAEC${role}_${affix}_T$tier"
            $entity = $entities.SelectSingleNode("//entity_class[@name='$name']")
            Assert-Adventure ($null -ne $baseEntities.SelectSingleNode("//entity_class[@name='$($entity.extends)']") -and $entity.extends.EndsWith("T$tier")) "Missing/exact-tier parent $name"
            Assert-Adventure ($entity.SelectNodes("effect_group/passive_effect").Count -eq 0) "Unexpected permanent stat change $name"
            if ($affix -eq 'bulwark') {
                Assert-Adventure ($entity.SelectSingleNode("effect_group/triggered_effect[@trigger='onSelfFirstSpawn']").buff -eq 'buffPZAECCharge') 'Overheat cycle not started'
            }
            if ($role -eq 'Trial') {
                Assert-Adventure ($challenge.SelectSingleNode("objective[@id='$name']").value -eq '1') 'Trial kill target wrong'
                $action = $trial.SelectSingleNode("action[property[@name='entity_names' and @value='$name']]")
                $count = '1'
            } else {
                $action = $events.SelectSingleNode("//action_sequence[@name='PZAECAffix_${affix}_T$tier']/action[@class='SpawnEntity']")
                $count = '2'
            }
            foreach ($entry in @{'entity_names'=$name;'spawn_count'=$count;'party_addition'='0';'ignore_multiplier'='true';'safe_spawn'='false';'spawn_type'='WanderingHorde'}.GetEnumerator()) {
                Assert-Adventure ($action.SelectSingleNode("property[@name='$($entry.Key)']").value -eq $entry.Value) "Wrong spawn setting $name / $($entry.Key)"
            }
            Assert-Adventure ($action.SelectNodes("property[@name='target_group']").Count -eq 0) 'Party duplication risk'
            Assert-Adventure ($localization.ContainsKey($name) -and $localization[$name].schinese.Contains("T$tier")) "Missing champion label $name"
        }
    }
}
Assert-Adventure ($buffs.SelectSingleNode("//buff[@name='buffPZAECCharge']/duration").value -eq '18') 'Wrong charge duration'
Assert-Adventure ($buffs.SelectSingleNode("//buff[@name='buffPZAECOverheat']/duration").value -eq '6') 'Wrong overheat duration'
foreach ($buff in $buffs.SelectNodes('//buff[@name="buffPZAECCharge" or @name="buffPZAECOverheat"]')) {
    $next = $buff.SelectSingleNode("effect_group/triggered_effect[@trigger='onSelfBuffFinish' and @action='AddBuff']").buff
    Assert-Adventure ($null -ne $buffs.SelectSingleNode("//buff[@name='$next']") -and $next -ne $buff.name) 'Broken two-stage cycle'
}
foreach ($prop in $quests.SelectNodes('//property[contains(@name,"_key")]') + $items.SelectNodes('//property[@name="DescriptionKey"]') + $events.SelectNodes('//property[@name="text_key"]')) {
    Assert-Adventure ($localization.ContainsKey($prop.value) -and -not [string]::IsNullOrWhiteSpace($localization[$prop.value].schinese)) "Missing description $($prop.value)"
}
'PASS: 20 infestation and 20 fetch/fetch-clear contracts plus 60 affix contracts; six unique IDs per page, tier rewards, 4 optional trials, 24 champions and Chinese text verified.'
