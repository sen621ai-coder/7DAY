#Requires -Version 7.0
$ErrorActionPreference = 'Stop'

$modRoot = Split-Path -Parent $PSScriptRoot
function Assert-Movement([bool]$ok, [string]$message) {
    if (-not $ok) { throw $message }
}

$sourcePath = Join-Path $modRoot '04-AEC-ENDGAME_OVERHAUL/Config/progression.xml'
$patchPath = Join-Path $modRoot '98-AECxProjectZ_Tweaks/Config/progression.xml'
$modifierPath = Join-Path $modRoot '04-AEC-ENDGAME_OVERHAUL/Config/item_modifiers.xml'
$source = [xml](Get-Content -LiteralPath $sourcePath -Raw)
$patch = [xml](Get-Content -LiteralPath $patchPath -Raw)
$modifiers = [xml](Get-Content -LiteralPath $modifierPath -Raw)

$spring = $source.SelectSingleNode("//perk[@name='perkAecSpringHeel']/effect_group/passive_effect[@name='JumpStrength']")
Assert-Movement ($null -ne $spring) 'Spring Heel source effect is missing.'

$springPath = "/progression/perks/perk[@name='perkAecSpringHeel']/effect_group/passive_effect[@name='JumpStrength']"
$operation = $patch.SelectSingleNode("//set[@xpath=`"$springPath/@operation`"]")
$levels = $patch.SelectSingleNode("//set[@xpath=`"$springPath/@level`"]")
$values = $patch.SelectSingleNode("//set[@xpath=`"$springPath/@value`"]")
Assert-Movement ($operation.InnerText -eq 'perc_add') 'Spring Heel is not converted from unsafe base addition to percentage scaling.'
Assert-Movement ($levels.InnerText -eq '1,20,100') 'Spring Heel does not scale through all 100 ranks.'
Assert-Movement ($values.InnerText -eq '.005,.1,2') 'Spring Heel maximum is not the documented +200%.'
Assert-Movement ($null -eq $patch.SelectSingleNode("//remove[@xpath=`"/progression/perks/perk[@name='perkAecSpringHeel']/effect_group/effect_description`"]")) 'Obsolete Spring Heel removal still produces an XML warning.'

$families = @(
    @{ Prefix='modAECMutatorJumpBoost'; Effect='JumpStrength'; Tag='modAECJumpBoost' },
    @{ Prefix='modAECMutatorCrouchBoost'; Effect='CrouchSpeed'; Tag='modAECCrouchBoost' },
    @{ Prefix='modAECMutatorWalkBoost'; Effect='WalkSpeed'; Tag='modAECWalkBoost' },
    @{ Prefix='modAECMutatorRunBoost'; Effect='RunSpeed'; Tag='modAECRunBoost' }
)
foreach ($family in $families) {
    $nodes = @($modifiers.SelectNodes("//item_modifier[starts-with(@name,'$($family.Prefix)')]"))
    Assert-Movement ($nodes.Count -eq 100) "$($family.Prefix) does not contain exactly 100 ranks."
    foreach ($node in $nodes) {
        Assert-Movement ($node.modifier_tags -eq $family.Tag) "$($node.name) can stack with another rank from its own family."
        $effect = $node.SelectSingleNode("effect_group/passive_effect[@name='$($family.Effect)']")
        $value = [single]::Parse($effect.value, [Globalization.CultureInfo]::InvariantCulture)
        Assert-Movement ($value -gt 0 -and $value -le 2) "$($node.name) has an unsafe movement bonus $value."
    }
}

$tweaksInfo = Get-Content -LiteralPath (Join-Path $modRoot '98-AECxProjectZ_Tweaks/ModInfo.xml') -Raw
Assert-Movement ($tweaksInfo -match '<Version value="3\.9\.8"') 'Tweaks version was not bumped.'

Write-Host 'PASS: Spring Heel uses rank-100 percentage scaling, movement mutators cap at +200%, duplicate ranks are blocked, and the obsolete XML warning is removed.'
