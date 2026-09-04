#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot
function Assert-Balance([bool]$ok,[string]$message) { if (-not $ok) { throw $message } }
function Apply-Config([xml]$target,[xml]$patch,[switch]$Strict) {
    foreach ($op in $patch.DocumentElement.ChildNodes) {
        if ($op.NodeType -ne 'Element') { continue }
        $targets = @($target.SelectNodes($op.xpath))
        if ($Strict -and $op.LocalName -ne 'append' -and $targets.Count -ne 1) { throw "Balance selector matched $($targets.Count): $($op.xpath)" }
        foreach ($node in $targets) {
            switch ($op.LocalName) {
                'set' { $node.InnerText = $op.InnerText }
                'setattribute' { $node.SetAttribute($op.GetAttribute('name'),$op.InnerText) }
                'remove' { [void]$node.ParentNode.RemoveChild($node) }
                'append' { foreach ($child in $op.ChildNodes) { if ($child.NodeType -eq 'Element') { [void]$node.AppendChild($target.ImportNode($child,$true)) } } }
                'insertBefore' { foreach ($child in $op.ChildNodes) { if ($child.NodeType -eq 'Element') { [void]$node.ParentNode.InsertBefore($target.ImportNode($child,$true),$node) } } }
                'insertAfter' { $anchor=$node; foreach ($child in $op.ChildNodes) { if ($child.NodeType -eq 'Element') { $anchor=$node.ParentNode.InsertAfter($target.ImportNode($child,$true),$anchor) } } }
                default { throw "Unsupported config operation: $($op.LocalName)" }
            }
        }
    }
}
function Get-InheritedProperty([hashtable]$map,[string]$id,[string]$property) {
    $seen=@{}
    while ($map.ContainsKey($id) -and -not $seen.ContainsKey($id)) {
        $seen[$id]=$true; $node=$map[$id]; $props=@($node.SelectNodes("property[@name='$property']"))
        if ($props.Count) { return [string]$props[-1].value }
        $id=[string]$node.extends
    }
    return ''
}
Assert-Balance ((Get-Content (Join-Path $modRoot '99-AEC_T16_RuntimeFix/ModInfo.xml') -Raw) -match '<Version value="1\.15\.2"') 'Runtime version was not bumped'
Assert-Balance (Test-Path (Join-Path $modRoot '99-AEC_T16_RuntimeFix/ENDGAME_REWARD_BALANCE.md')) 'Missing reward guide'

# Apply the real configuration operations in load order, including this patch.
[xml]$loot = Get-Content (Join-Path $modRoot '../Data/Config/loot.xml') -Raw
Apply-Config $loot ([xml](Get-Content (Join-Path $modRoot '98-AECxProjectZ_Tweaks/Config/loot.xml') -Raw))
$knownGroups=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($group in $loot.SelectNodes('/lootcontainers/lootgroup[@name]')){[void]$knownGroups.Add([string]$group.name)}
# References may target groups contributed by any earlier mod, not only the
# vanilla file and Tweaks document used by the focused merge assertions below.
foreach($directory in Get-ChildItem $modRoot -Directory | Sort-Object Name){
    if($directory.Name -ge '99-AEC_T16_RuntimeFix'){continue}
    $path=Join-Path $directory.FullName 'Config/loot.xml'
    if(-not (Test-Path $path)){continue}
    [xml]$earlier=Get-Content $path -Raw
    foreach($group in $earlier.SelectNodes('//lootgroup[@name]')){[void]$knownGroups.Add([string]$group.name)}
}
[xml]$runtimeLootPatch=Get-Content (Join-Path $modRoot '99-AEC_T16_RuntimeFix/Config/loot.xml') -Raw
# The game validates group references after each XML operation. A final merged
# tree can therefore look valid even though an early set/append already failed.
foreach($op in $runtimeLootPatch.DocumentElement.ChildNodes){
    if($op.NodeType -ne 'Element'){continue}
    if($op.LocalName -eq 'append' -and $op.xpath -eq '/lootcontainers'){
        foreach($child in $op.ChildNodes){
            if($child.NodeType -ne 'Element'){continue}
            foreach($item in $child.SelectNodes('.//item[@group]')){
                Assert-Balance ($knownGroups.Contains([string]$item.group)) "Forward lootgroup reference before definition: $($item.group) in $($child.name)"
            }
            if($child.LocalName -eq 'lootgroup'){[void]$knownGroups.Add([string]$child.name)}
        }
    }
    if($op.LocalName -eq 'set' -and $op.xpath.EndsWith('/@group')){
        Assert-Balance ($knownGroups.Contains($op.InnerText.Trim())) "Set references lootgroup before definition: $($op.InnerText.Trim())"
    }
}
Apply-Config $loot $runtimeLootPatch -Strict
[xml]$quests = Get-Content (Join-Path $modRoot '../Data/Config/quests.xml') -Raw
Apply-Config $quests ([xml](Get-Content (Join-Path $modRoot '04-AEC-ENDGAME_OVERHAUL/Config/quests.xml') -Raw))
Apply-Config $quests ([xml](Get-Content (Join-Path $modRoot '98-AECxProjectZ_Tweaks/Config/quests.xml') -Raw))
Apply-Config $quests ([xml](Get-Content (Join-Path $modRoot '99-AEC_T16_RuntimeFix/Config/quests.xml') -Raw))
[xml]$entities = Get-Content (Join-Path $modRoot '../Data/Config/entityclasses.xml') -Raw
foreach ($directory in Get-ChildItem $modRoot -Directory | Sort-Object Name) {
    $path=Join-Path $directory.FullName 'Config/entityclasses.xml'
    if (Test-Path $path) { Apply-Config $entities ([xml](Get-Content $path -Raw)) }
}
$entityMap=@{}; foreach($node in $entities.SelectNodes('/entity_classes/entity_class[@name]')){$entityMap[$node.name]=$node}

$managed=Join-Path (Split-Path -Parent $modRoot) '7DaysToDie_Data/Managed'
Get-ChildItem $managed -Filter '*.dll' | ForEach-Object { try {[void][Reflection.Assembly]::LoadFrom($_.FullName)} catch {} }
[void][Reflection.Assembly]::LoadFrom((Join-Path $modRoot '00-TFP_Harmony/0Harmony.dll'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $modRoot '99-AEC_T16_RuntimeFix/AEC.T16.RuntimeFix.dll'))

$families=@('Reg','Strong','Boss','Lab','Nurse','Soldier','Thug','Utility','Plague')
$bonusProb=@{16='.12';17='.16';18='.20';19='.25'}
foreach($tier in 16..19) {
    Assert-Balance ([AECT16RuntimeFix.HighTierMobLoot]::TierForContainer("PZAECBossLootBundleT$tier") -eq $tier) "Bundle scope T$tier"
    foreach($name in @("AECArcherBossLootT$tier","DoomlordBossLootT$tier","RunningKamikazeBossLootT$tier")) {
        Assert-Balance ([AECT16RuntimeFix.HighTierMobLoot]::TierForContainer($name) -eq $tier) "Family scope: $name"
    }
    foreach($family in $families) {
        $container=$loot.SelectSingleNode("/lootcontainers/lootcontainer[@name='PZAECMobT${tier}_zPack$family']")
        $wrapper=$container.item.group
        $group=$loot.SelectSingleNode("/lootcontainers/lootgroup[@name='$wrapper']")
        Assert-Balance ($group.count -eq 'all' -and $group.item.Count -eq 2) "Broken mob wrapper T$tier/$family"
        Assert-Balance ($group.item[0].group -eq "groupZpack$family") "Native loot family changed T$tier/$family"
        Assert-Balance ($group.item[1].group -eq "PZAECMobBonusT$tier" -and $group.item[1].prob -eq $bonusProb[$tier] -and $group.item[1].force_prob -eq 'true') "Wrong supply probability T$tier/$family"
    }
    $bonus=$loot.SelectSingleNode("/lootcontainers/lootgroup[@name='PZAECMobBonusT$tier']")
    Assert-Balance ($bonus.item.Count -eq 5 -and @($bonus.item|Where-Object{$_.quality}).Count -eq 0) "Mob bonus may flood equipment T$tier"
    foreach($id in @('ammo762mmBulletAP','resourceRepairKit','resourceForgedSteel','resourceAECMutationSampleT5','casinoCoin')) {
        Assert-Balance ($bonus.SelectNodes("item[@name='$id']").Count -eq 1) "Missing utility drop $id/T$tier"
    }
    $bundle=$loot.SelectSingleNode("/lootcontainers/lootgroup[@name='PZAECBossLootBundleT${tier}_Content']")
    Assert-Balance ($bundle.item.Count -eq 15) "Wrong boss bundle breadth T$tier"
    $box=$loot.SelectSingleNode("/lootcontainers/lootcontainer[@name='PZAECBossLootBundleT$tier']")
    Assert-Balance ($box.unmodified_lootstage -eq 'true' -and $box.ignore_loot_abundance -eq 'true') "Bundle affected by world loot multipliers T$tier"
}
foreach($groupName in @('groupUnique_Weapon','groupLegend_Weapon','groupLegend_MeleeWeapon','groupUniqModsAll','groupUniqueParts')) {
    $last=0.0
    foreach($tier in 16..19) {
        $p=[double]$loot.SelectSingleNode("/lootcontainers/lootgroup[@name='PZAECBossLootBundleT${tier}_Content']/item[@group='$groupName']").prob
        Assert-Balance ($p -gt $last) "Boss rarity chance not increasing: $groupName/T$tier"; $last=$p
    }
}

# Every original T16 family is preserved at T17-T19 with its own bag/table.
[xml]$tweaksEntities=Get-Content (Join-Path $modRoot '98-AECxProjectZ_Tweaks/Config/entityclasses.xml') -Raw
$bosses=@($tweaksEntities.SelectNodes("//entity_class[property[@name='LootDropEntityClass']]")|Where-Object{$_.name -match 'T16$' -and $_.SelectSingleNode("property[@name='LootDropEntityClass']").value -match 'LootBagT16$'})
Assert-Balance ($bosses.Count -eq 21) 'Expected 21 boss families'
foreach($source in $bosses) {
    foreach($tier in 16..19) {
        $boss=$source.name -replace 'T16$',"T$tier"
        $bag=Get-InheritedProperty $entityMap $boss 'LootDropEntityClass'
        $lootName=Get-InheritedProperty $entityMap $bag 'LootList'
        Assert-Balance ($bag.EndsWith("T$tier") -and $lootName.EndsWith("T$tier")) "Boss inherited lower-tier bag: $boss"
        $family=$loot.SelectSingleNode("/lootcontainers/lootcontainer[@name='$lootName']")
        Assert-Balance ($null -ne $family) "Missing family table: $lootName"
        $boxes=@($family.SelectNodes("item[@name='itemPZAECBossLootBundleT$tier']"))
        $expected=if($tier -eq 16){1}else{2}
        Assert-Balance ($boxes.Count -eq $expected -and $boxes[0].prob -eq '1') "Wrong guaranteed boss box: $boss"
        if($tier -gt 16){Assert-Balance ([double]$boxes[1].prob -eq @{17=.4;18=.6;19=.8}[$tier]) "Wrong bonus box chance: $boss"}
    }
}

$coinTable=@{18=@(36000,40000,44000,48000,52000);19=@(60000,66000,72000,78000,84000)}
foreach($tier in 18..19){for($area=1;$area -le 5;$area++){
    $id="aec_quest_T${tier}_A${area}_clear"; $coin=[int]$quests.SelectSingleNode("/quests/quest[@id='$id']/reward[@id='casinoCoin']").value
    Assert-Balance ($coin -eq $coinTable[$tier][$area-1]) "Wrong size coin reward: $id"
    foreach($kind in @('infested','fetch')){
        $v=[int]$quests.SelectSingleNode("/quests/quest[@id='${id}_$kind']/reward[@id='casinoCoin']").value
        Assert-Balance ($v -eq $coin) "Ordinary mission type got affix premium: ${id}_$kind"
    }
    foreach($affix in @('hunter','bulwark','storm')){
        $v=[int]$quests.SelectSingleNode("/quests/quest[@id='${id}_$affix']/reward[@id='casinoCoin']").value
        Assert-Balance ($v -eq [int]($coin*1.25)) "Affix premium changed: ${id}_$affix"
    }
}}
foreach($tier in 16..19){
    foreach($suffix in @('','_infested','_fetch','_hunter','_bulwark','_storm')){
        $reward=$quests.SelectSingleNode("/quests/quest[@id='aec_quest_T${tier}_A5_clear$suffix']/reward[starts-with(@id,'itemPZAECBossLootBundleT')]")
        Assert-Balance ($reward.id -eq "itemPZAECBossLootBundleT$tier" -and $reward.value -eq '1') "A5 gives wrong tier box T$tier$suffix"
    }
}
'PASS: 36 native mob families plus tiered 12/16/20/25% utility supplies; 84 exact-family boss tables; mixed quest-type coins, affix premiums and exact-tier A5 rewards.'
