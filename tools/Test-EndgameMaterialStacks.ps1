#Requires -Version 7.0
[CmdletBinding()]
param([switch]$NativeRoundTrip)
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path $PSScriptRoot -Parent
$config = Join-Path $modRoot '99-AEC_T16_RuntimeFix/Config'
[xml]$items = Get-Content (Join-Path $config 'items.xml') -Raw
[xml]$blocks = Get-Content (Join-Path $config 'blocks.xml') -Raw
[xml]$recipes = Get-Content (Join-Path $config 'recipes.xml') -Raw
$materials = @('resourcePZAECWeaponChassis', 'resourcePZAECArmorChassis', 'resourcePZAECDeviceChassis', 'resourcePZAECDefenseChassis')
$ammo = @()
$consumables = @('itemPZAECQuickArmorGel', 'itemPZAECResonanceInjector', 'itemPZAECDecoyBeacon')
$questItems = @()
foreach ($tier in 16..19) {
    $materials += 'PZAECBuildPartsR' + ($tier - 14)
    foreach ($kind in @('MutantHeart', 'SiegeCapacitor', 'CoreFragment', 'CapacitorFragment', 'RepairCharge', 'DecoyCharge')) {
        $materials += "resourcePZAEC${kind}T$tier"
    }
    foreach ($kind in @('ArmoryBlueprintCrate', 'ComponentChoiceCrate', 'DefenseBlueprintCrate')) {
        $materials += "itemPZAEC${kind}T$tier"
    }
    foreach ($kind in @('SkyguardInterceptor', 'CounterPulse')) { $ammo += "ammoPZAEC${kind}T$tier" }
    $consumables += "thrownPZAECCounterJammerT$tier", "itemPZAECFieldRepairKitT$tier"
    $questItems += "PZAECChallengeVoucherT$tier", "PZAECDefenseBeaconT$tier"
}
$targets = $materials + $ammo + $consumables + $questItems
if ($materials.Count -ne 44 -or $ammo.Count -ne 8 -or $consumables.Count -ne 11 -or $questItems.Count -ne 8 -or ($targets | Select-Object -Unique).Count -ne 71) {
    throw 'Stack target coverage changed'
}
foreach ($id in $targets) {
    $nodes = $items.SelectNodes("//item[@name='$id']/property[@name='Stacknumber']")
    if ($nodes.Count -ne 1 -or $nodes[0].value -ne '50000') { throw "Expected 50000 stack: $id" }
}
foreach ($id in $materials) {
    # Includes reserved support materials and blueprints whose higher-tier
    # recipes were replaced by equipment fusion; do not require an active use.
    if ($items.SelectSingleNode("//item[@name='$id']/property[@name='Extends']").value -ne 'resourceLegendaryParts') { throw "Material base changed: $id" }
}
foreach ($id in @('PZAECStrongholdCore', 'PZAECStrongholdPower', 'PZAECStrongholdSupply')) {
    if ($blocks.SelectSingleNode("//block[@name='$id']/property[@name='Stacknumber']").value -ne '1') { throw "Single-piece building changed: $id" }
}
$forge = $blocks.SelectSingleNode("//block[@name='PZAECResonanceForge']")
if ($forge.SelectSingleNode("property[@name='Extends']").value -ne 'workbench' -or $forge.SelectSingleNode("property[@name='Stacknumber']")) {
    throw 'Resonance forge no longer inherits the single-piece native workbench'
}
$anchor = $items.SelectSingleNode("//item[@name='itemPZAECEvacAnchor']")
if ($anchor.SelectSingleNode("property[@name='Stacknumber']").value -ne '1' -or $anchor.SelectSingleNode("property[@class='Action0']/property[@name='Consume']").value -ne 'false') {
    throw 'Reusable evacuation anchor changed'
}
foreach ($id in @('itemPZAECQuickArmorGel','itemPZAECResonanceInjector','itemPZAECDecoyBeacon') + @(16..19 | ForEach-Object { "itemPZAECFieldRepairKitT$_" })) {
    $item = $items.SelectSingleNode("//item[@name='$id']")
    if ($item.SelectSingleNode("property[@class='Action0']/property[@name='Consume']").value -ne 'true') { throw "Consumable no longer consumes: $id" }
}
foreach ($tier in 16..19) {
    foreach ($kind in @('Challenge','Defense')) {
        $id = if ($kind -eq 'Challenge') { "PZAECChallengeVoucherT$tier" } else { "PZAECDefenseBeaconT$tier" }
        $action = $items.SelectSingleNode("//item[@name='$id']/property[@class='Action0']")
        if ($action.SelectSingleNode("property[@name='Class']").value -ne 'Quest' -or $action.SelectSingleNode("property[@name='QuestGiven']").value -ne "PZAEC${kind}T$tier") { throw "Quest item action changed: $id" }
    }
}
'PASS: 71 targeted stacks at 50000 (44 materials / 8 ammo / 11 consumables / 8 quest items); four buildings and evacuation anchor remain at 1; consumption flags and quest targets preserved.'
if ($NativeRoundTrip) {
    $managed = Join-Path (Split-Path $modRoot -Parent) '7DaysToDie_Data/Managed'
    Get-ChildItem -LiteralPath $managed -Filter '*.dll' | ForEach-Object {
        try { [void][Reflection.Assembly]::LoadFrom($_.FullName) } catch { }
    }
    foreach ($count in @(1,49999,50000)) {
        $stream = [IO.MemoryStream]::new()
        try {
            # Exercise native count serialization with an empty ItemValue;
            # this is not an actual inventory/consumption gameplay test.
            $stack = [ItemStack]::new([ItemValue]::None, $count)
            $writer = [IO.BinaryWriter]::new($stream)
            $stack.Write($writer)
            $writer.Flush()
            $stream.Position = 0
            $restored = [ItemStack]::new([ItemValue]::None, 0)
            $null = $restored.Read([IO.BinaryReader]::new($stream))
            if ($restored.count -ne $count) { throw "Native stack count truncated: $count" }
        } finally { $stream.Dispose() }
    }
    'PASS: native ItemStack count write/read preserves 1, 49999 and 50000 (empty-item fixture; not live inventory use).'
}
