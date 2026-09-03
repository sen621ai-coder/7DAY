$ErrorActionPreference = 'Stop'
# Offline regression checks. Never invoke Unity world/spawn methods here.
$modRoot = Split-Path -Parent $PSScriptRoot
$managed = Join-Path (Split-Path -Parent $modRoot) '7DaysToDie_Data/Managed'
Get-ChildItem -LiteralPath $managed -Filter '*.dll' | ForEach-Object {
    try { [void][Reflection.Assembly]::LoadFrom($_.FullName) } catch { }
}
$references = @(
    (Join-Path $modRoot '00-TFP_Harmony/0Harmony.dll'),
    (Join-Path $modRoot '00-TFP_Harmony/Mono.Cecil.dll'),
    (Join-Path $modRoot '99-AEC_T16_RuntimeFix/AEC.T16.RuntimeFix.dll'),
    (Join-Path $managed 'Assembly-CSharp.dll'),
    (Join-Path $managed 'UnityEngine.CoreModule.dll')
)
foreach ($path in $references) { [void][Reflection.Assembly]::LoadFrom($path) }
$frameworkReferences = @(Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName)
Add-Type -ReferencedAssemblies ($references + $frameworkReferences) -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Linq;
using AECT16RuntimeFix;

public static class QuestBossRegression
{
    static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
    public static string Run()
    {
        for (int tier = 17; tier <= 19; tier++)
            for (int area = 1; area <= 5; area++)
            {
                string id = "aec_quest_T" + tier + "_A" + area + "_clear";
                Check(QuestBossSpawn.TierForQuestId(id) == tier, "Wrong tier: " + id);
                Check(QuestBossSpawn.ShouldStart(id,2,3,true,-1,42,false), "Missing owner trigger");
                Check(QuestBossSpawn.ShouldStart(id,2,3,true,42,42,false), "Explicit owner rejected");
                Check(!QuestBossSpawn.ShouldStart(id,2,3,true,42,43,false), "Shared copy triggered");
                Check(!QuestBossSpawn.ShouldStart(id,1,2,true,-1,42,false), "Spawned on accepting quest");
                Check(!QuestBossSpawn.ShouldStart(id,3,4,true,-1,42,false), "Spawned at trader phase");
                Check(!QuestBossSpawn.ShouldStart(id,3,3,true,-1,42,false), "Reload triggered");
                Check(!QuestBossSpawn.ShouldStart(id,2,3,false,-1,42,false), "Inactive quest triggered");
                Check(!QuestBossSpawn.ShouldStart(id,2,3,true,-1,42,true), "Persisted marker ignored");
            }
        foreach (string id in new[]{null,"","aec_quest_T16_A1_clear","aec_quest_T20_A1_clear",
            "aec_quest_T17_A0_clear","aec_quest_T17_A6_clear","aec_quest_T17_A1_clear_extra",
            "aec_quest_T17_A1_clear\n","tier6_clear","aec_quest_T18_A1_fetch"})
            Check(QuestBossSpawn.TierForQuestId(id) == 0, "Out of scope: " + id);

        var data = new Dictionary<string,string>();
        int calls = 0;
        Check(QuestBossSpawn.DispatchOnce(data,18, e => { calls++; return e == "PZAECQuestBossT18"; }), "Dispatch failed");
        Check(!QuestBossSpawn.DispatchOnce(data,18,e => { calls++; return true; }), "Duplicate accepted");
        Check(calls == 1, "Dispatcher called twice");
        var restored = new Dictionary<string,string>(data);
        Check(!QuestBossSpawn.DispatchOnce(restored,18,e => true), "Reloaded marker ignored");
        Check(QuestBossSpawn.DispatchOnce(new Dictionary<string,string>(),18,e => true), "New independent quest blocked");

        data.Clear();
        Check(!QuestBossSpawn.DispatchOnce(data,17,e => false) && data.Count == 0, "Rejected event consumed allowance");
        try { QuestBossSpawn.DispatchOnce(data,17,e => { throw new InvalidOperationException(); }); }
        catch (InvalidOperationException) { }
        Check(data.Count == 0, "Exception consumed allowance");
        Check(QuestBossSpawn.DispatchOnce(data,17,e => {
            Check(!QuestBossSpawn.DispatchOnce(data,17,inner => true), "Reentrant duplicate"); return true;
        }), "Retry after failure did not work");
        Check(!QuestBossSpawn.DispatchOnce(new Dictionary<string,string>(),16,e => true), "Lower tier accepted");

        using (var module = Mono.Cecil.ModuleDefinition.ReadModule(typeof(Quest).Assembly.Location))
        {
            var quest = module.Types.Single(t => t.Name == "Quest");
            foreach (string method in new[]{"Write","Read"})
                Check(quest.Methods.Where(m => m.Name == method).Any(m => m.Body.Instructions.Any(i =>
                    i.Operand != null && i.Operand.ToString().Contains("Quest::DataVariables"))), "Marker not saved by Quest."+method);
            Check(quest.Methods.Any(m => m.Name == "AdvancePhase" && m.Parameters.Count == 0), "Hook target changed");
            var native = module.Types.Single(t => t.FullName == "GameEvent.SequenceActions.ActionBaseSpawn");
            var init = native.Methods.Single(m => m.Name == ".cctor");
            Check(init.Body.Instructions.Any(i => (i.Operand as string) == "ignore_multiplier"), "Wrong native multiplier property");
            Check(init.Body.Instructions.Any(i => (i.Operand as string) == "party_addition"), "Wrong native party property");
        }
        return "PASS: 15 quest mappings, phase/owner/party guards, reload and reentrancy dedup, failed dispatch retries, native serialization/API checks.";
    }
}
'@
[QuestBossRegression]::Run()

[xml]$events = Get-Content -LiteralPath (Join-Path $modRoot '99-AEC_T16_RuntimeFix/Config/gameevents.xml') -Raw
[xml]$entities = Get-Content -LiteralPath (Join-Path $modRoot '98-AECxProjectZ_Tweaks/Config/entityclasses.xml') -Raw
[xml]$quests = Get-Content -LiteralPath (Join-Path $modRoot '98-AECxProjectZ_Tweaks/Config/quests.xml') -Raw
$sequences = $events.SelectNodes('/configs/append/action_sequence[starts-with(@name,"PZAECQuestBossT")]')
if ($sequences.Count -ne 3) { throw 'Expected three tier-specific events' }
foreach ($tier in 17..19) {
    $sequence = $events.SelectSingleNode("//action_sequence[@name='PZAECQuestBossT$tier']")
    if (-not $sequence -or $sequence.action.Count -gt 1) { throw "Invalid T$tier sequence" }
    $action = $sequence.SelectSingleNode("action[@class='SpawnEntity']")
    $props = @{}
    foreach ($prop in $action.property) { $props[$prop.GetAttribute('name')] = $prop.GetAttribute('value') }
    foreach ($entry in @{'spawn_count'='2'; 'party_addition'='0'; 'ignore_multiplier'='true'; 'single_choice'='true'; 'safe_spawn'='true'; 'spawn_type'='WanderingHorde'}.GetEnumerator()) {
        if ($props[$entry.Key] -ne $entry.Value) { throw "T$tier incorrect $($entry.Key)" }
    }
    if ($props.ContainsKey('target_group')) { throw 'Party-wide target would multiply bosses' }
    $names = $props['entity_names'].Split(',')
    if ($names.Count -ne 21 -or @($names | Sort-Object -Unique).Count -ne 21) { throw "Wrong T$tier boss pool size" }
    foreach ($name in $names) {
        if (-not $name.EndsWith("T$tier") -or -not $entities.SelectSingleNode("//entity_class[@name='$name']")) { throw "Invalid boss: $name" }
    }
    foreach ($area in 1..5) {
        if (-not $quests.SelectSingleNode("//quest[@id='aec_quest_T${tier}_A${area}_clear']")) { throw 'Missing quest definition' }
    }
}
'PASS: three two-boss native events, 63 exact-tier boss references, all 15 existing quest definitions.'
