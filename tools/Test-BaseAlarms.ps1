#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot
$managed = Join-Path (Split-Path -Parent $modRoot) '7DaysToDie_Data/Managed'
Get-ChildItem -LiteralPath $managed -Filter '*.dll' | ForEach-Object {
    try { [void][Reflection.Assembly]::LoadFrom($_.FullName) } catch { }
}
$references = @(
    (Join-Path $modRoot '97-LogicCircuitGates/LogicCircuitGates.dll'),
    (Join-Path $managed 'Assembly-CSharp.dll'),
    (Join-Path $managed 'UnityEngine.CoreModule.dll')
)
foreach ($path in $references) { [void][Reflection.Assembly]::LoadFrom($path) }
$framework = @(Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName)
Add-Type -ReferencedAssemblies ($references + $framework) -TypeDefinition @'
using System;
using System.Collections.Generic;
using LogicCircuitGates;
using UnityEngine;

public static class BaseAlarmRegression
{
    static int checks;
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    public static string Run()
    {
        var latch = new AlarmLatch();
        Check(!latch.Update(true, false, true, 0), "new detector must not alert");
        Check(latch.Update(true, true, true, 0), "first detection");
        Check(latch.Update(true, false, true, 2.99f), "release hold");
        Check(!latch.Update(true, false, true, 3), "release boundary");
        Check(latch.Update(true, true, true, 4), "retrigger");
        Check(!latch.Update(false, true, true, 4.1f), "power overrides threat");
        Check(!latch.Update(true, false, true, 4.2f), "power restore must not resurrect hold");
        Check(latch.Update(true, true, false, 5), "blood moon starts");
        Check(!latch.Update(true, false, false, 5.1f), "blood moon ends without hold");
        for (int flags = 0; flags < 16; flags++)
            Check(BaseAlarmSensors.Eligible((flags&1)!=0,(flags&2)!=0,(flags&4)!=0,(flags&8)!=0) == (flags==3),
                "dead / neutral / sleeping / player filtering " + flags);
        foreach (string stem in new[] { "PZAECSiege_heavy_T", "PZAECSiege_acid_T", "PZAECSiegeDisruptorT", "PZAECSiegeSpotterT", "PZAECSiegeLinesmanT" })
        for (int tier = 14; tier <= 21; tier++)
            Check(BaseAlarmSensors.IsSiege(stem + tier) == (tier >= 16 && tier <= 19), "siege mapping " + stem + tier);
        foreach (string name in new[] { null, "", "PZAECSiege", "PZAECSiegeBossT19", "AECZombieCopTier19", "PZAECSiege_heavy_T190", "PZAECSiege_heavy_T16Clone" })
            Check(!BaseAlarmSensors.IsSiege(name), "false siege match " + name);

        var index = new AlarmThreatIndex();
        var random = new System.Random(6306);
        var points = new List<Vector3>();
        var siege = new List<bool>();
        for (int i = 0; i < 500; i++)
        {
            var point = new Vector3(random.Next(-2500, 2500), random.Next(-70, 220), random.Next(-2500, 2500));
            bool isSiege = (i % 3 == 0);
            points.Add(point); siege.Add(isSiege); index.Add(point, isSiege);
        }
        // Compare the spatial index against a separate full-scan oracle. Include
        // negative coordinates and exact cell/range boundaries, not just origin.
        for (int i = 0; i < 2000; i++)
        {
            Vector3 target = points[i % points.Count];
            var center = new Vector3(target.x + random.Next(-65,65), target.y + random.Next(-65,65), target.z + random.Next(-65,65));
            float range = new[] {12f,32f,64f}[i%3]; bool siegeOnly = (i%2==0); bool expected = false;
            for (int p = 0; p < points.Count; p++)
            {
                if (siegeOnly && !siege[p]) continue;
                double dx=points[p].x-center.x, dy=points[p].y-center.y, dz=points[p].z-center.z;
                if (dx*dx+dy*dy+dz*dz <= range*range) { expected=true; break; }
            }
            Check(index.Detect(center, range, siegeOnly) == expected, "spatial query " + i);
        }
        foreach (float range in new[] {12f,32f,64f})
        foreach (var center in new[] { new Vector3(0,0,0), new Vector3(-1616,48,-1152), new Vector3(-1616,48,-1168) })
        {
            index.Clear(); index.Add(new Vector3(center.x+range,center.y,center.z),false);
            Check(index.Detect(center,range,false), "inclusive range");
            Check(!index.Detect(center,range,true), "ordinary enemy is not siege");
            index.Clear(); index.Add(new Vector3(center.x,center.y+range+.1f,center.z),true);
            Check(!index.Detect(center,range,false), "vertical exclusion");
            index.Clear(); Check(!index.Detect(center,range,false), "empty world after reset");
        }
        return "PASS: " + checks + " alarm policy / timing / spatial queries.";
    }
}
'@
[BaseAlarmRegression]::Run()

function Assert-Alarm([bool]$ok, [string]$message) { if (-not $ok) { throw $message } }
$config = Join-Path $modRoot '97-LogicCircuitGates/Config'
[xml]$blocks = Get-Content (Join-Path $config 'blocks.xml') -Raw
[xml]$recipes = Get-Content (Join-Path $config 'recipes.xml') -Raw
[xml]$nativeBlocks = Get-Content (Join-Path $modRoot '../Data/Config/blocks.xml') -Raw
[xml]$nativeItems = Get-Content (Join-Path $modRoot '../Data/Config/items.xml') -Raw
$rows = @(Import-Csv (Join-Path $config 'Localization.csv'))
$keys = @{}
foreach ($row in $rows) {
    Assert-Alarm (-not $keys.ContainsKey($row.Key)) "Duplicate localization $($row.Key)"
    Assert-Alarm (-not [string]::IsNullOrWhiteSpace($row.schinese)) "Missing Chinese $($row.Key)"
    $keys[$row.Key] = $true
}
$names = @('logicAlarmPerimeter','logicAlarmBreach','logicAlarmSiege','logicAlarmBloodMoon')
Assert-Alarm ($blocks.SelectNodes("//block[starts-with(@name,'logicAlarm')]").Count -eq 4) 'Expected four detectors'
foreach ($name in $names) {
    $block = $blocks.SelectSingleNode("//block[@name='$name']")
    Assert-Alarm ($block.SelectSingleNode("property[@name='Class']").value -eq 'LogicAlarmSensor,LogicCircuitGates') "Custom factory $name"
    Assert-Alarm ($block.SelectSingleNode("property[@name='Extends']").value -eq 'switch') "Native persistence $name"
    $icon = $block.SelectSingleNode("property[@name='CustomIcon']").value
    Assert-Alarm ($null -ne $nativeBlocks.SelectSingleNode("/blocks/block[@name='$icon']")) "Native icon $icon"
    Assert-Alarm ($keys.ContainsKey($name) -and $keys.ContainsKey("${name}Desc")) "Localized $name"
    $recipe = $recipes.SelectSingleNode("//recipe[@name='$name']")
    Assert-Alarm ($recipe.always_unlocked -eq 'true' -and $recipe.craft_area -eq 'workbench') "Recipe $name"
    foreach ($ingredient in $recipe.ingredient) {
        Assert-Alarm ($null -ne $nativeItems.SelectSingleNode("/items/item[@name='$($ingredient.name)']") -and [int]$ingredient.count -gt 0) "Ingredient $($ingredient.name)"
    }
}
[xml]$siege = Get-Content (Join-Path $modRoot '99-AEC_T16_RuntimeFix/Config/entityclasses.xml') -Raw
$count = 0
foreach ($entity in $siege.SelectNodes('//entity_class')) {
    if ([LogicCircuitGates.BaseAlarmSensors]::IsSiege($entity.name)) { $count++ }
}
Assert-Alarm ($count -eq 20) 'All 20 existing siege class IDs must match'
'PASS: block factories, localization, recipes, native assets and 20 existing siege classes.'
