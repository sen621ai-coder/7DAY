#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot
function Assert-Reward([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
[xml]$quests = Get-Content -LiteralPath (Join-Path $modRoot '98-AECxProjectZ_Tweaks/Config/quests.xml') -Raw
[xml]$items = Get-Content -LiteralPath (Join-Path $modRoot '98-AECxProjectZ_Tweaks/Config/items.xml') -Raw
[xml]$baseItems = Get-Content -LiteralPath (Join-Path $modRoot '../Data/Config/items.xml') -Raw
foreach ($id in @('aecUniversalToken', 'resourceAECMutationSampleT5', 'itemPZAECBossLootBundleT16')) {
    Assert-Reward ($null -ne $items.SelectSingleNode("//item[@name='$id']")) "Missing reward item: $id"
}
Assert-Reward ($null -ne $baseItems.SelectSingleNode("/items/item[@name='casinoCoin']")) 'Missing casinoCoin'
$partMultipliers = @{ 16 = 2; 17 = 4; 18 = 6; 19 = 10 }
$values = @{}
foreach ($tier in 16..19) {
    foreach ($area in 1..5) {
        $id = "aec_quest_T${tier}_A${area}_clear"
        $nodes = @($quests.SelectNodes("//quest[@id='$id']"))
        Assert-Reward ($nodes.Count -eq 1) "Missing or duplicate quest: $id"
        $quest = $nodes[0]
        Assert-Reward ($quest.template -eq "aec_base_A$area") "Changed template: $id"
        Assert-Reward ($quest.SelectSingleNode("property[@name='difficulty_tier']").value -eq '6') "Changed native difficulty: $id"
        $row = @{}
        foreach ($key in @('Exp', 'casinoCoin', 'aecUniversalToken', 'resourceAECMutationSampleT5')) {
            $selector = if ($key -eq 'Exp') { "reward[@type='Exp']" } else { "reward[@type='Item' and @id='$key']" }
            $rewards = @($quest.SelectNodes($selector))
            Assert-Reward ($rewards.Count -eq 1) "Missing or duplicate $key reward: $id"
            $reward = $rewards[0]
            Assert-Reward (-not $reward.HasAttribute('ischosen')) "Guaranteed reward became optional: $id / $key"
            $row[$key] = [int]$reward.value
            Assert-Reward ($row[$key] -gt 0) "Invalid reward amount: $id / $key"
        }
        $expected = switch ($tier) {
            16 { @(300000 * $area; 15000 + 2000 * ($area - 1); 350 + 25 * ($area - 1); 1200 * $area) }
            17 { @(600000 * $area; 25000 + 2000 * ($area - 1); 450 + 40 * ($area - 1); 2400 * $area) }
            18 { @(800000 * $area; 35000; 500 + 40 * ($area - 1); 3000 * $area) }
            19 { @(1600000 * $area; 50000; 750 + 75 * ($area - 1); 6000 * $area) }
        }
        $keys = @('Exp', 'casinoCoin', 'aecUniversalToken', 'resourceAECMutationSampleT5')
        for ($i = 0; $i -lt $keys.Count; $i++) {
            Assert-Reward ($row[$keys[$i]] -eq $expected[$i]) "Wrong reward: $id / $($keys[$i])"
        }
        $choiceIds = @(
            $(if ($tier -eq 16) { 'PZAECQuestWeaponsT16' } else { 'PZAECQuestLegendWeapons' }),
            "PZAECQuestModsT$tier",
            "PZAECQuestMaterialsT$tier"
        )
        $choices = @($quest.SelectNodes("reward[@type='LootItem' and @ischosen='true']"))
        Assert-Reward ($choices.Count -eq 3) "Choice count changed: $id"
        for ($i = 0; $i -lt 3; $i++) {
            Assert-Reward ($choices[$i].id -eq $choiceIds[$i] -and [int]$choices[$i].value -eq $tier) "Wrong advanced choice loot: $id"
        }
        $parts = @($quest.SelectNodes("reward[@type='Item' and @id='resourceLegendaryParts']"))
        Assert-Reward ($parts.Count -eq 1 -and [int]$parts[0].value -eq $partMultipliers[$tier] * $area -and -not $parts[0].HasAttribute('ischosen')) "Wrong guaranteed legendary parts: $id"
        $guaranteedMods = @($quest.SelectNodes("reward[@type='LootItem' and not(@ischosen)]"))
        Assert-Reward ($guaranteedMods.Count -eq [int]($tier -eq 19)) "Wrong guaranteed unique mod count: $id"
        if ($tier -eq 19) {
            Assert-Reward ($guaranteedMods[0].id -eq 'PZAECQuestModsT17' -and $guaranteedMods[0].value -eq '19') "Wrong guaranteed unique mod pool: $id"
        }
        $bundles = @($quest.SelectNodes("reward[@id='itemPZAECBossLootBundleT16']"))
        Assert-Reward ($bundles.Count -eq [int]($area -eq 5)) "Boss bundle placement changed: $id"
        if ($area -eq 5) {
            Assert-Reward ($bundles[0].type -eq 'Item' -and $bundles[0].value -eq '1' -and -not $bundles[0].HasAttribute('ischosen')) "Boss bundle changed: $id"
        }
        Assert-Reward (@($quest.reward).Count -eq (8 + [int]($area -eq 5) + [int]($tier -eq 19))) "Unexpected additional reward: $id"
        $values["$tier/$area"] = $row
    }
}
foreach ($tier in 16..19) {
    foreach ($area in 1..5) {
        foreach ($key in $keys) {
            $amount = $values["$tier/$area"][$key]
            if ($tier -gt 16) {
                Assert-Reward ($amount -gt $values["$($tier - 1)/$area"][$key]) "Tier reward inversion: T$tier A$area / $key"
            }
            if ($tier -le 17 -and $area -gt 1) {
                Assert-Reward ($amount -gt $values["$tier/$($area - 1)"][$key]) "Size reward inversion: T$tier A$area / $key"
            }
        }
    }
}
foreach ($tier in 16..17) {
    foreach ($area in 1..5) {
        $oldXp = $(if ($tier -eq 16) { 200000 } else { 400000 }) * $area
        $oldSamples = $(if ($tier -eq 16) { 750 } else { 1500 }) * $area
        $oldCoins = if ($tier -eq 16) { 10000 } else { 20000 }
        $oldTokens = if ($tier -eq 16) { @(250, 260, 270, 280, 300)[$area - 1] } else { 350 + 25 * ($area - 1) }
        $row = $values["$tier/$area"]
        Assert-Reward ($row.Exp * 2 -eq $oldXp * 3) "XP uplift is not 50%: T$tier A$area"
        Assert-Reward ($row.resourceAECMutationSampleT5 * 5 -eq $oldSamples * 8) "Sample uplift is not 60%: T$tier A$area"
        Assert-Reward ($row.casinoCoin -gt $oldCoins -and $row.aecUniversalToken -gt $oldTokens) "Currency did not increase: T$tier A$area"
    }
}
[xml]$loot = Get-Content -LiteralPath (Join-Path $modRoot '98-AECxProjectZ_Tweaks/Config/loot.xml') -Raw
[xml]$projectLoot = Get-Content -LiteralPath (Join-Path $modRoot '01-ProjectZ/Config/loot.xml') -Raw
[xml]$mergedLoot = Get-Content -LiteralPath (Join-Path $modRoot '../Data/Config/loot.xml') -Raw
foreach ($operation in $loot.SelectNodes("/configs/append[.//*[@name='PZAECQuestLegendQuality'] or lootgroup[starts-with(@name,'PZAECQuest')]]")) {
    $targets = @($mergedLoot.SelectNodes($operation.xpath))
    Assert-Reward ($targets.Count -eq 1) 'Advanced reward patch matches zero/multiple targets'
    foreach ($child in $operation.ChildNodes) {
        if ($child.NodeType -eq [System.Xml.XmlNodeType]::Element) { [void]$targets[0].AppendChild($mergedLoot.ImportNode($child, $true)) }
    }
}
Assert-Reward (@($mergedLoot.SelectNodes("/lootcontainers/lootqualitytemplates/lootqualitytemplate[@name='PZAECQuestLegendQuality']")).Count -eq 1) 'Quality template duplicated or missing after patch application'
Assert-Reward (@($mergedLoot.SelectNodes("/lootcontainers/lootgroup[starts-with(@name,'PZAECQuest')]")).Count -eq 10) 'Pools duplicated or missing after patch application'
$definedItems = [System.Collections.Generic.HashSet[string]]::new()
foreach ($relative in @('../Data/Config/items.xml', '../Data/Config/item_modifiers.xml', '01-ProjectZ/Config/items.xml', '01-ProjectZ/Config/item_modifiers.xml', '98-AECxProjectZ_Tweaks/Config/items.xml')) {
    [xml]$definitions = Get-Content -LiteralPath (Join-Path $modRoot $relative) -Raw
    foreach ($node in $definitions.SelectNodes('//item[@name] | //item_modifier[@name]')) { [void]$definedItems.Add($node.name) }
}
Assert-Reward ($definedItems.Contains('resourceLegendaryParts')) 'Missing legendary parts definition'
$pools = @($loot.SelectNodes("//lootgroup[starts-with(@name,'PZAECQuest')]"))
Assert-Reward ($pools.Count -eq 10) 'Missing or duplicate advanced pools'
Assert-Reward (@($pools | Group-Object name | Where-Object Count -gt 1).Count -eq 0) 'Duplicate advanced pool IDs'
foreach ($pool in $pools) {
    Assert-Reward (@($pool.item).Count -gt 0) "Empty reward pool: $($pool.name)"
    foreach ($entry in $pool.item) {
        Assert-Reward ($definedItems.Contains($entry.name)) "Undefined item: $($entry.name)"
        Assert-Reward (-not $entry.HasAttribute('group') -and -not $entry.HasAttribute('loot_prob_template') -and -not $entry.HasAttribute('loot_stage_count_mod') -and -not $entry.HasAttribute('prob')) "Stage/probability-dependent reward entry: $($pool.name)"
        Assert-Reward ([int]$entry.count -gt 0) 'Invalid item quantity'
    }
}
$uniqueWeapons = @($projectLoot.SelectSingleNode("//lootgroup[@name='groupUnique_Weapon']").item.name)
$legendWeapons = @($projectLoot.SelectSingleNode("//lootgroup[@name='groupLegend_Weapon']").item.name) + @($projectLoot.SelectSingleNode("//lootgroup[@name='groupLegend_MeleeWeapon']").item.name)
$uniqueMods = @($projectLoot.SelectSingleNode("//lootgroup[@name='groupUniqModsRange']").item.name) + @($projectLoot.SelectSingleNode("//lootgroup[@name='groupUniqModsMelee']").item.name)
$rareMods = @($projectLoot.SelectSingleNode("//lootgroup[@name='groupRareMods']").item.name)
foreach ($entry in $loot.SelectSingleNode("//lootgroup[@name='PZAECQuestWeaponsT16']").item) {
    Assert-Reward ($uniqueWeapons -contains $entry.name -and $entry.quality -eq '5' -and $entry.count -eq '1') 'T16 weapon is not Q5 unique'
}
$legendPool = $loot.SelectSingleNode("//lootgroup[@name='PZAECQuestLegendWeapons']")
Assert-Reward ($legendPool.loot_quality_template -eq 'PZAECQuestLegendQuality') 'Missing fixed legendary quality template'
foreach ($entry in $legendPool.item) { Assert-Reward ($legendWeapons -contains $entry.name -and $entry.count -eq '1') 'Non-legendary item in legendary weapon pool' }
$qualityTemplate = $loot.SelectSingleNode("//lootqualitytemplate[@name='PZAECQuestLegendQuality']")
foreach ($tier in 17..19) {
    $bands = @($qualityTemplate.qualitytemplate | Where-Object { $bounds = $_.level.Split(','); [int]$bounds[0] -le $tier -and $tier -le [int]$bounds[1] })
    $quality = if ($tier -eq 17) { 5 } else { 6 }
    Assert-Reward ($bands.Count -eq 1 -and @($bands[0].loot).Count -eq 1 -and [int]$bands[0].loot.quality -eq $quality -and $bands[0].loot.prob -eq '1') "Wrong T$tier weapon quality"
}
$materialCounts = @{ 16 = @(20, 2, 5); 17 = @(40, 4, 10); 18 = @(60, 6, 15); 19 = @(100, 10, 25) }
foreach ($tier in 16..19) {
    $modPool = $loot.SelectSingleNode("//lootgroup[@name='PZAECQuestModsT$tier']")
    $allowed = if ($tier -eq 16) { $rareMods } else { $uniqueMods }
    $count = if ($tier -lt 18) { 1 } else { $tier - 16 }
    foreach ($entry in $modPool.item) { Assert-Reward ($allowed -contains $entry.name -and [int]$entry.count -eq $count) "Wrong T$tier mod quality/count" }
    $materialPool = $loot.SelectSingleNode("//lootgroup[@name='PZAECQuestMaterialsT$tier']")
    $names = @('resourceExperimentalAlloys', 'UniqueParts', 'resourceLegendaryParts')
    Assert-Reward (@($materialPool.item).Count -eq 3) 'Wrong material choice count'
    for ($i = 0; $i -lt 3; $i++) { Assert-Reward ($materialPool.item[$i].name -eq $names[$i] -and [int]$materialPool.item[$i].count -eq $materialCounts[$tier][$i]) "Wrong T$tier material reward" }
}
Write-Output 'PASS: 20 quests; advanced choices, fixed weapon qualities, 10 nonempty pools with valid item IDs, guaranteed parts/mods, currency gradients and existing A5 bundles verified.'
