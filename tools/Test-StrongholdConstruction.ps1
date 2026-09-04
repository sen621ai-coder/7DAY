#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot
$managed = Join-Path (Split-Path -Parent $modRoot) '7DaysToDie_Data/Managed'
Get-ChildItem -LiteralPath $managed -Filter '*.dll' | ForEach-Object {
    try { [void][Reflection.Assembly]::LoadFrom($_.FullName) } catch { }
}
$references = @(
    (Join-Path $modRoot '00-TFP_Harmony/Mono.Cecil.dll'),
    (Join-Path $modRoot '99-AEC_T16_RuntimeFix/AEC.T16.RuntimeFix.dll'),
    (Join-Path $managed 'Assembly-CSharp.dll'),
    (Join-Path $managed 'UnityEngine.CoreModule.dll')
)
foreach ($path in $references) { [void][Reflection.Assembly]::LoadFrom($path) }
$frameworkReferences = @(Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName)

Add-Type -ReferencedAssemblies ($references + $frameworkReferences) -TypeDefinition @'
using System;
using System.Linq;
using AECT16RuntimeFix;

public static class StrongholdRegression
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    public static string Run()
    {
        Check(LegendaryDefense.ConstructionScore(0, 0, 0) == 0, "Empty score");
        Check(LegendaryDefense.ConstructionScore(750000, 0, 0) == 30, "HP score");
        Check(LegendaryDefense.ConstructionScore(1500000, 50, 0) == 65, "Fortress threshold");
        Check(LegendaryDefense.ConstructionScore(long.MaxValue, 10000, 10000) == 100, "Score caps");
        Check(LegendaryDefense.ConstructionGrade(29) == 1 && LegendaryDefense.ConstructionGrade(30) == 2 &&
            LegendaryDefense.ConstructionGrade(64) == 2 && LegendaryDefense.ConstructionGrade(65) == 3, "Grade boundaries");
        Check(LegendaryDefense.IntegrityGrade(100, 90) == 3 && LegendaryDefense.IntegrityGrade(100, 89) == 2 &&
            LegendaryDefense.IntegrityGrade(100, 70) == 2 && LegendaryDefense.IntegrityGrade(100, 69) == 1, "Integrity boundaries");
        Check(LegendaryDefense.RewardRank(3, 100, 95, true, true) == 3, "Rank 3");
        Check(LegendaryDefense.RewardRank(2, 100, 95, true, true) == 2, "Construction must cap reward");
        Check(LegendaryDefense.RewardRank(3, 100, 75, true, true) == 2, "Damage must cap reward");
        Check(LegendaryDefense.RewardRank(3, 100, 95, true, false) == 2, "One facility must cap reward");
        Check(LegendaryDefense.RewardRank(3, 100, 95, false, false) == 1, "No facilities must cap reward");
        for (int tier = 16; tier <= 19; tier++) for (int rank = 1; rank <= 3; rank++)
            Check(LegendaryDefense.BonusEventId(tier, rank) == "PZAECStrongholdBonusT" + tier + "R" + rank, "Bonus event mapping");
        Check(LegendaryDefense.BonusEventId(15, 1) == null && LegendaryDefense.BonusEventId(19, 4) == null, "Invalid bonus accepted");
        using (var module = Mono.Cecil.ModuleDefinition.ReadModule(typeof(LegendaryDefense).Assembly.Location))
        {
            var type = module.Types.Single(t => t.FullName == "AECT16RuntimeFix.LegendaryDefense");
            var inspect = type.Methods.Single(m => m.Name == "Inspect");
            Check(inspect.Body.Instructions.Count(i => i.Operand is Mono.Cecil.MethodReference r && r.Name == "GetBlock") > 0,
                "Construction scan no longer reads world blocks");
            var close = type.Methods.Single(m => m.Name == "BeforeClose");
            Check(close.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference r && r.Name == "RewardRank"),
                "Completion no longer calculates stronghold rank");
            Check(close.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference r && r.Name == "DispatchOnce"),
                "Conditional bonus lost duplicate guard");
        }
        return "PASS: score/integrity/facility rank boundaries and compiled world-scan/conditional-bonus wiring.";
    }
}
'@
[StrongholdRegression]::Run()

function Assert-Stronghold([bool]$ok, [string]$message) { if (-not $ok) { throw $message } }
$config = Join-Path $modRoot '99-AEC_T16_RuntimeFix/Config'
[xml]$blocks = Get-Content (Join-Path $config 'blocks.xml') -Raw
[xml]$recipes = Get-Content (Join-Path $config 'recipes.xml') -Raw
[xml]$events = Get-Content (Join-Path $config 'gameevents.xml') -Raw
[xml]$nativeBlocks = Get-Content (Join-Path $modRoot '../Data/Config/blocks.xml') -Raw
$localization = @{}
foreach ($row in Import-Csv (Join-Path $config 'Localization.csv')) {
    Assert-Stronghold (-not $localization.ContainsKey($row.Key)) "Duplicate localization $($row.Key)"
    $localization[$row.Key] = $row
}

$facilityNames = @('PZAECStrongholdCore','PZAECStrongholdPower','PZAECStrongholdSupply')
$facilityBlocks = @($blocks.SelectNodes('/configs/append/block'))
Assert-Stronghold ($facilityBlocks.Count -eq 3) 'Expected three stronghold facility blocks'
foreach ($name in $facilityNames) {
    $block = $blocks.SelectSingleNode("//block[@name='$name']")
    Assert-Stronghold ($null -ne $block -and [int]$block.SelectSingleNode("property[@name='MaxDamage']").value -ge 12000) "Weak/missing facility $name"
    Assert-Stronghold ($localization.ContainsKey($name) -and $localization.ContainsKey("${name}Desc")) "Missing facility localization $name"
    $recipe = $recipes.SelectSingleNode("//recipe[@name='$name']")
    Assert-Stronghold ($null -ne $recipe -and $recipe.craft_area -eq 'workbench' -and $recipe.always_unlocked -eq 'true') "Missing facility recipe $name"
    $parent = $block.SelectSingleNode("property[@name='Extends']").value
    Assert-Stronghold ($null -ne $nativeBlocks.SelectSingleNode("/blocks/block[@name='$parent']")) "Missing native facility parent $parent"
}

$bonusEvents = @($events.SelectNodes("//action_sequence[starts-with(@name,'PZAECStrongholdBonusT')]"))
Assert-Stronghold ($bonusEvents.Count -eq 12) 'Expected twelve tier/rank bonus events'
foreach ($tier in 16..19) {
    foreach ($rank in 1..3) {
        $event = $events.SelectSingleNode("//action_sequence[@name='PZAECStrongholdBonusT${tier}R${rank}']")
        $action = $event.SelectSingleNode("action[@class='AddItems']")
        $items = @($action.SelectSingleNode("property[@name='added_items']").value -split ',')
        $counts = @($action.SelectSingleNode("property[@name='added_item_counts']").value -split ',')
        Assert-Stronghold ($items.Count -eq $counts.Count -and $items.Count -ge 4) "Malformed bonus event T$tier R$rank"
        Assert-Stronghold (($items -contains "PZAECBuildPartsR$($tier-14)") -and (($items -match 'itemPZAECBossLootBundle').Count -eq ($rank -eq 3 ? 1 : 0))) "Wrong tier/rank bonus contents T$tier R$rank"
    }
}
foreach ($key in @('PZAECStrongholdNeedCore','PZAECStrongholdNeedPower','PZAECStrongholdNeedSupply','PZAECStrongholdRegistered',
    'PZAECStrongholdCoreLost','PZAECStrongholdPowerLost','PZAECStrongholdSupplyLost','PZAECStrongholdComplete')) {
    Assert-Stronghold ($localization.ContainsKey($key) -and -not [string]::IsNullOrWhiteSpace($localization[$key].schinese)) "Missing stronghold text $key"
}
Assert-Stronghold ((Get-Content (Join-Path $modRoot '99-AEC_T16_RuntimeFix/ModInfo.xml') -Raw) -match '<Version value="1\.16\.7"') 'Runtime version was not bumped'
'PASS: three craftable 12k+ HP facilities, native parents, Chinese text and 12 exact-tier construction bonus events.'
