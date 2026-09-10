#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot
[xml]$merged = Get-Content -Raw (Join-Path $modRoot '../Data/Config/progression.xml')
[xml]$endgame = Get-Content -Raw (Join-Path $modRoot '04-AEC-ENDGAME_OVERHAUL/Config/progression.xml')
$mastery = $endgame.SelectSingleNode("//perk[@name='perkAecRecoilMastery']")
[void]$merged.progression.perks.AppendChild($merged.ImportNode($mastery, $true))
[xml]$patch = Get-Content -Raw (Join-Path $modRoot '99-AEC_T16_RuntimeFix/Config/progression.xml')
foreach ($op in $patch.configs.ChildNodes) {
    if ($op.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
    $targets = @($merged.SelectNodes($op.xpath))
    if ($targets.Count -ne 1) { throw "Expected exactly one target: $($op.xpath)" }
    if ($op.Name -eq 'set') { $targets[0].Value = $op.InnerText }
    elseif ($op.Name -eq 'append') {
        foreach ($child in $op.ChildNodes) {
            [void]$targets[0].AppendChild($merged.ImportNode($child, $true))
        }
    }
    else { throw "Unsupported patch operation: $($op.Name)" }
}

function Get-Reduction($perk, $effect, [int]$rank) {
    if ($rank -eq 0) { return 0.0 }
    $nodes = @($merged.SelectNodes("//perk[@name='$perk']/effect_group/passive_effect[@name='$effect']"))
    if ($nodes.Count -ne 1) { throw "Expected one $effect in $perk" }
    $levels = $nodes[0].level.Split(',') | ForEach-Object { [double]::Parse($_, [cultureinfo]::InvariantCulture) }
    $values = $nodes[0].value.Split(',') | ForEach-Object { [double]::Parse($_, [cultureinfo]::InvariantCulture) }
    return $values[0] + ($values[1] - $values[0]) * ($rank - $levels[0]) / ($levels[1] - $levels[0])
}

# Exercise every talent rank plus armor/attachment reductions, including totals
# beyond -100%. Signed endpoints saturate at zero as in the runtime guard.
[xml]$items = Get-Content -Raw (Join-Path $modRoot '../Data/Config/items.xml')
$cases = 0
foreach ($axis in @('Vertical', 'Horizontal')) {
    $minEffect = "KickDegrees${axis}Min"
    $maxEffect = "KickDegrees${axis}Max"
    $nativeMin = [double]::Parse($items.SelectSingleNode("//item[@name='gunMGT3M60']//passive_effect[@name='$minEffect']").value, [cultureinfo]::InvariantCulture)
    $nativeMax = [double]::Parse($items.SelectSingleNode("//item[@name='gunMGT3M60']//passive_effect[@name='$maxEffect']").value, [cultureinfo]::InvariantCulture)
    foreach ($gunRank in 0..5) {
        foreach ($masteryRank in 0..100) {
            $minReduction = (Get-Reduction 'perkMachineGunner' $minEffect $gunRank) + (Get-Reduction 'perkAecRecoilMastery' $minEffect $masteryRank)
            $maxReduction = (Get-Reduction 'perkMachineGunner' $maxEffect $gunRank) + (Get-Reduction 'perkAecRecoilMastery' $maxEffect $masteryRank)
            foreach ($extra in @(0, -0.25, -0.5, -0.75, -1, 0.1)) {
                $lo = $nativeMin * [Math]::Max(0, 1 + $minReduction + $extra)
                $hi = $nativeMax * [Math]::Max(0, 1 + $maxReduction + $extra)
                if ($lo + $hi -lt -0.000001) { throw "Recoil drifts down/left: $axis, $gunRank, $masteryRank, $extra -> [$lo, $hi]" }
                if ([Math]::Abs($minReduction - $maxReduction) -gt 0.000001) { throw 'Talents shift recoil direction.' }
                $cases++
            }
        }
    }
}
Write-Host "PASS: recoil patch targets and $cases M60 talent/attachment combinations; no downward or leftward mean drift."
