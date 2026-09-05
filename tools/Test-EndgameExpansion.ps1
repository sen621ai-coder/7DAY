#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
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
using System.Linq;
using System.Xml.Linq;
using Mono.Cecil;

public static class EndgameExpansionRegression
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }

    public static string Runtime(string path)
    {
        using (var module = ModuleDefinition.ReadModule(path))
        {
            var runtime = module.Types.Single(t => t.Name == "EndgameExpansionRuntime");
            foreach (string method in new[] { "Install", "ApplyArmorGel", "ApplyResonance", "ApplyDecoy",
                "ApplyJammer", "ApplyFieldRepair", "ApplyCalibration", "UseEvacAnchor", "DamageBlockPrefix", "DamageBlockPostfix",
                "ProjectileUpdatePrefix", "PlayerUpdatePostfix", "EnemyUpdatePostfix", "DamageEntityPostfix" })
                Check(runtime.Methods.Any(m => m.Name == method), "Missing runtime path " + method);

            string[] requiredCalls = { "SetBlockRPC", "SetCustomVarNetwork", "AddBuffNetwork", "Teleport",
                "SetInvestigatePosition", "Destroy", "SetSlotItem" };
            var calls = runtime.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions)
                .Select(i => i.Operand as MethodReference).Where(m => m != null).Select(m => m.Name).ToArray();
            foreach (string call in requiredCalls)
                Check(calls.Contains(call), "Runtime no longer calls " + call);

            var entry = module.Types.Single(t => t.Name == "T16RuntimeFixMod").Methods.Single(m => m.Name == "InitMod");
            Check(entry.Body.Instructions.Any(i => i.Operand is MethodReference m &&
                m.DeclaringType.Name == "EndgameExpansionRuntime" && m.Name == "Install"),
                "Expansion runtime is not installed by the mod entry point");
        }
        return "PASS: field items, reactive walls, interception, repair, diversion, armor break and heat runtime paths are installed.";
    }

    public static string Effects(string itemsXml, string modifiersXml)
    {
        string[] itemStems = { "EmberPistol", "HorizonNeedle", "StormReservoir", "FaultlineHammer", "BastionShotgun",
            "EchoRepeater", "CounterSiege", "SkyguardInterceptor", "CounterPulse", "QuickArmorGel",
            "ResonanceInjector", "DecoyBeacon", "EvacAnchor", "CounterJammer", "FieldRepairKit",
            "DefenseChassis", "CoreFragment", "CapacitorFragment", "ArmoryBlueprintCrate",
            "ComponentChoiceCrate", "RepairCharge", "DecoyCharge", "DefenseBlueprintCrate" };
        var items = XDocument.Parse(itemsXml).Descendants("item")
            .Where(n => itemStems.Any(s => (((string)n.Attribute("name")) ?? "").Contains(s))).ToArray();
        Check(items.Length == 77, "Expected 77 expansion items in native effect test");
        foreach (var node in items.Where(n => n.Elements("effect_group").Any()))
        {
            var effects = MinEffectController.ParseXml(node, null, MinEffectController.SourceParentType.ItemClass);
            Check(effects.EffectGroups.Sum(g => g.PassiveEffects.Count) == node.Descendants("passive_effect").Count(),
                "Item passive effect was ignored: " + (string)node.Attribute("name"));
        }

        string[] componentStems = { "ThreatLens", "CoolingSink", "KineticRecycler", "RepairServo",
            "PhaseRangefinder", "NearfieldReflex", "ClosedLoopFeed", "PulseCapacitor", "StanceBreaker",
            "GatekeeperHook", "EmergencyLiner", "InsulatedTreads" };
        var modifiers = XDocument.Parse(modifiersXml).Descendants("item_modifier").Where(n => {
            string name = (string)n.Attribute("name") ?? "";
            return componentStems.Any(name.Contains) || ((name.Contains("Stable") || name.Contains("Overload")) && name.EndsWith("T19"));
        }).ToArray();
        Check(modifiers.Length == 56, "Expected 56 expansion modifiers in native effect test");
        foreach (var node in modifiers)
        {
            var effects = MinEffectController.ParseXml(node, null, MinEffectController.SourceParentType.ItemModifierClass);
            Check(effects.EffectGroups.Sum(g => g.PassiveEffects.Count) == node.Descendants("passive_effect").Count(),
                "Modifier passive effect was ignored: " + (string)node.Attribute("name"));
        }
        return "PASS: all 77 items and 56 modifiers parse through the game's native effect controller.";
    }
}
'@

python (Join-Path $modRoot '99-AEC_T16_RuntimeFix/Tools/validate_endgame_expansion.py')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$generator = Join-Path $modRoot '99-AEC_T16_RuntimeFix/Tools/generate_endgame_expansion.py'
$tracked = @('items.xml','item_modifiers.xml','buffs.xml','blocks.xml','recipes.xml','loot.xml','entityclasses.xml','entitygroups.xml','Localization.csv') |
    ForEach-Object { Join-Path $modRoot "99-AEC_T16_RuntimeFix/Config/$_" }
$originalBytes = @{}
foreach ($path in $tracked) { $originalBytes[$path] = [IO.File]::ReadAllBytes($path) }
try {
    # Git may check out CRLF while Python emits LF. Compare text per file,
    # preserving all differences except line endings, and restore exact bytes.
    python $generator
    if ($LASTEXITCODE -ne 0) { throw "Expansion generator failed: $LASTEXITCODE" }
    foreach ($path in $tracked) {
        $before = [Text.Encoding]::UTF8.GetString($originalBytes[$path]).Replace("`r`n", "`n")
        $after = [IO.File]::ReadAllText($path).Replace("`r`n", "`n")
        if ($before -cne $after) { throw "Expansion generator changed content: $path" }
    }
}
finally {
    foreach ($path in $tracked) { [IO.File]::WriteAllBytes($path, $originalBytes[$path]) }
}

[EndgameExpansionRegression]::Runtime((Join-Path $modRoot '99-AEC_T16_RuntimeFix/AEC.T16.RuntimeFix.dll'))
[EndgameExpansionRegression]::Effects(
    (Get-Content (Join-Path $modRoot '99-AEC_T16_RuntimeFix/Config/items.xml') -Raw),
    (Get-Content (Join-Path $modRoot '99-AEC_T16_RuntimeFix/Config/item_modifiers.xml') -Raw))
'PASS: expansion generator is idempotent.'
