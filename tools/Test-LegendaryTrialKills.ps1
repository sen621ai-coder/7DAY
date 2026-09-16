#Requires -Version 7.0
param([string]$RuntimeDll)
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot
$managed = Join-Path (Split-Path -Parent $modRoot) '7DaysToDie_Data/Managed'
if (!$RuntimeDll) { $RuntimeDll = Join-Path $modRoot '99-AEC_T16_RuntimeFix/AEC.T16.RuntimeFix.dll' }
Get-ChildItem -LiteralPath $managed -Filter '*.dll' | ForEach-Object {
    try { [void][Reflection.Assembly]::LoadFrom($_.FullName) } catch { }
}
$references = @($RuntimeDll, (Join-Path $modRoot '00-TFP_Harmony/0Harmony.dll'),
    (Join-Path $managed 'Assembly-CSharp.dll'), (Join-Path $managed 'UnityEngine.CoreModule.dll'))
foreach ($path in $references) { [void][Reflection.Assembly]::LoadFrom($path) }
$framework = @(Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName)
Add-Type -ReferencedAssemblies ($references + $framework) -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using AECT16RuntimeFix;

public class TrialKillObjectiveProbe : ObjectiveEntityKill
{
    public int RefreshCalls;
    // Only UI and quest completion need Unity; run the original kill callback
    // and original counter setter, and observe whether Refresh is requested.
    public override void SetupDisplay() { }
    public override void Refresh() { RefreshCalls++; }
}
public static class TrialKillRegression
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static T Bare<T>() { return (T)RuntimeHelpers.GetUninitializedObject(typeof(T)); }
    static TrialKillObjectiveProbe Objective(string questId, string entityId, string explicitTags = null)
    {
        var quest = Bare<Quest>(); quest.ID = questId;
        quest.Requirements = new List<Quests.Requirements.BaseRequirement>();
        var objective = new TrialKillObjectiveProbe { ID = entityId, Value = "1", OwnerQuest = quest,
            OwnerQuestClass = new QuestClass(questId) };
        var properties = new DynamicProperties();
        // Any property block enters native ParseProperties. Localization itself
        // requires Unity; the missing-target_tags branch below is unchanged.
        if (explicitTags != null) properties.Values[ObjectiveEntityKill.PropTargetTags] = explicitTags;
        objective.ParseProperties(properties);
        objective.SetupObjective();
        return objective;
    }
    static void Kill(ObjectiveEntityKill objective, EntityAlive entity)
    {
        AccessTools.Method(typeof(ObjectiveEntityKill), "Current_EntityKill").Invoke(objective, new object[] { null, entity });
    }
    public static string Run()
    {
        int checks = 0;
        foreach (int tier in new[] {16,17,18,19}) foreach (string kind in new[] {"hunter","bulwark","storm"})
        {
            string questId = "pzaecchallenget" + tier, id = "PZAECTrial_" + kind + "_T" + tier;
            int classId = 700000 + checks;
            var entityClass = Bare<EntityClass>(); entityClass.entityClassName = id;
            entityClass.Tags = FastTags<TagGroup.Global>.Parse("zombie,trialBoss");
            EntityClass.list[classId] = entityClass;
            var killed = Bare<EntityZombie>(); killed.entityClass = classId;
            var objective = Objective(questId, id);
            Check(!objective.targetTags.IsEmpty, "Installed game no longer reproduces empty-name target tag; reassess fix");
            Kill(objective, killed);
            Check(objective.CurrentValue == 0 && objective.RefreshCalls == 0, "Original callback did not reproduce zero kills");
            LegendaryAdventure.NormalizeTrialTargetTags(objective);
            Check(objective.targetTags.IsEmpty, "Target tags were not cleared");
            Kill(objective, killed);
            Check(objective.CurrentValue == 1 && objective.RefreshCalls == 1, "Fixed original callback did not count the boss");

            var wrong = Objective(questId, "PZAECTrial_" + (kind == "hunter" ? "storm" : "hunter") + "_T" + tier);
            LegendaryAdventure.NormalizeTrialTargetTags(wrong); Kill(wrong, killed);
            Check(wrong.CurrentValue == 0, "Wrong boss kind counted");
            var explicitTarget = Objective(questId, id, "otherEnemy");
            LegendaryAdventure.NormalizeTrialTargetTags(explicitTarget); Kill(explicitTarget, killed);
            Check(!explicitTarget.targetTags.IsEmpty && explicitTarget.CurrentValue == 0, "Explicit target tags bypassed");
            var unrelated = Objective("ordinaryQuest", id);
            LegendaryAdventure.NormalizeTrialTargetTags(unrelated);
            Check(!unrelated.targetTags.IsEmpty, "Unrelated quest changed");
            var clone = (ObjectiveEntityKill)Objective(questId, id).Clone(); clone.OwnerQuest = objective.OwnerQuest;
            LegendaryAdventure.NormalizeTrialTargetTags(clone);
            Check(clone.targetTags.IsEmpty, "Cloned/reloaded objective not repaired");
            string tag = LegendaryAdventure.Request(42, LegendaryAdventure.SpawnMarker);
            var data = new Dictionary<string,string> { [LegendaryAdventure.SpawnMarker] = "PZAECTrialT" + tier };
            Check(LegendaryAdventure.CanCountTrialKill(questId,42,true,1,-1,1,data,tag), "Owner's kill blocked");
            Check(LegendaryAdventure.CanCountTrialKill(questId,42,true,1,1,2,new Dictionary<string,string>(),tag), "Shared kill blocked");
            Check(!LegendaryAdventure.CanCountTrialKill(questId,43,true,1,-1,1,data,tag), "Foreign encounter counted");
            EntityClass.list.Remove(classId);
            checks++;
        }
        return "PASS: " + checks + " native boss-kill callbacks reproduce 0 before repair and 1 after repair; wrong targets, explicit tags, unrelated quests, cloned objectives and encounter ownership checked. UI/phase/rewards require in-game validation.";
    }
}
'@
[TrialKillRegression]::Run()
