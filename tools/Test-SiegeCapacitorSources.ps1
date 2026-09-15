#Requires -Version 7.0
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Test-EndgameRewardBalance.ps1')
$root=Split-Path -Parent $PSScriptRoot
[xml]$base=Get-Content "$root/98-AECxProjectZ_Tweaks/Config/loot.xml" -Raw
[xml]$patch=Get-Content "$root/99-AEC_T16_RuntimeFix/Config/loot.xml" -Raw
[xml]$recipes=Get-Content "$root/99-AEC_T16_RuntimeFix/Config/recipes.xml" -Raw
[xml]$items=Get-Content "$root/99-AEC_T16_RuntimeFix/Config/items.xml" -Raw
function Assert-Cap($ok,$message) { if (!$ok) { throw $message } }
foreach($tier in 16..19) {
    $name="resourcePZAECSiegeCapacitorT$tier"
    $path="/lootcontainers/lootgroup[@name='PZAECBossLootBundleT${tier}_Content']"
    $group=$base.SelectSingleNode("//lootgroup[@name='PZAECBossLootBundleT${tier}_Content']")
    $override=$patch.SelectSingleNode("//lootgroup[@name='PZAECBossLootBundleT${tier}_Content']")
    if ($null -ne $override) { $group=$override }
    Assert-Cap ($group.count -eq 'all') 'Boss box no longer rolls independent rewards'
    $entries=@($patch.SelectNodes("//append[@xpath=`"$path`"]/*[@name='$name']"))
    Assert-Cap ($entries.Count -eq 1) "Duplicate/missing capacitor source T$tier"
    $expected=if($tier -lt 18){'1'}else{'1,2'}
    Assert-Cap ($entries[0].count -eq $expected -and $entries[0].prob -eq '1' -and $entries[0].force_prob -eq 'true') "Wrong guaranteed count T$tier"
    $recipe=@($recipes.SelectNodes("//recipe[@name='$name']"))
    Assert-Cap ($recipe.Count -eq 1) "Duplicate/missing capacitor recipe T$tier"
    Assert-Cap ($recipe[0].count -eq '1' -and $recipe[0].craft_area -eq 'workbench' -and $recipe[0].always_unlocked -eq 'true' -and $recipe[0].use_ingredient_modifier -eq 'false') 'Wrong recipe settings'
    $ingredient=@($recipe[0].ingredient)
    Assert-Cap ($ingredient.Count -eq 1 -and $ingredient[0].name -eq "resourcePZAECCapacitorFragmentT$tier" -and $ingredient[0].count -eq '10') 'Wrong tier/cost'
    Assert-Cap ($null -ne $items.SelectSingleNode("//item[@name='$($ingredient[0].name)']")) 'Missing ingredient definition'
    $siege=$patch.SelectSingleNode("//lootgroup[@name='PZAECSiegeMaterialsT$tier']/item[@name='$name']")
    Assert-Cap ($siege.count -eq $expected) 'Existing siege capacitor yield changed'
}
Write-Output 'PASS: four guaranteed same-tier boss-box rewards; four exact 10:1 workbench recipes; existing siege yields preserved; prior boss loot regression passed.'
