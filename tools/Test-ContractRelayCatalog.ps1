#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
function Check($ok, $message) { if (-not $ok) { throw $message } }
function Property($node, $name) { $p=$node.SelectSingleNode("property[@name='$name']"); if ($p) { return $p.value }; return '' }
[xml]$original=Get-Content "$root/03-AEC-AIO_BOSS_EXTREME_EDITION/Config/traders.xml" -Raw
[xml]$catalog=Get-Content "$root/99-AEC_T16_RuntimeFix/Config/traders.xml" -Raw
[xml]$merged='<traders />'
foreach ($op in $original.DocumentElement.ChildNodes) {
    if ($op.NodeType -eq 'Element' -and $op.Name -eq 'append' -and $op.xpath -eq '/traders') {
        foreach ($n in $op.ChildNodes) { if ($n.NodeType -eq 'Element') { [void]$merged.DocumentElement.AppendChild($merged.ImportNode($n,$true)) } }
    }
}
foreach ($op in $catalog.configs.append) {
    $targets=@($merged.SelectNodes($op.xpath)); Check ($targets.Count -eq 1) "Unmatched trader selector: $($op.xpath)"
    foreach ($n in $op.ChildNodes) { if ($n.NodeType -eq 'Element') { [void]$targets[0].AppendChild($merged.ImportNode($n,$true)) } }
}
for ($star=1; $star -le 5; $star++) {
    $id=91+$star
    $stock=$merged.SelectSingleNode("/traders/trader_info[@id='$id']/trader_items")
    Check ($stock.count -eq 'all') "Trader $id does not use independent stock entries"
    $groups=@($stock.item | Where-Object group)
    Check ($groups.Count -eq $star) "Unexpected tier group count at $star stars"
    for ($tier=1; $tier -le $star; $tier++) {
        $entry=@($groups | Where-Object group -eq "aecContractsPool${tier}star")
        Check ($entry.Count -eq 1 -and $entry[0].count -eq '100,100') "Missing independent tier $tier at $star stars"
    }
    $before=$original.SelectSingleNode("//trader_info[@id='$id']")
    $after=$merged.SelectSingleNode("//trader_info[@id='$id']")
    Check ($before.override_buy_markup -eq $after.override_buy_markup -and $before.reset_interval -eq $after.reset_interval) 'Prices or refresh changed'
    Check (@($stock.item | Where-Object name -like 'itemPZAECRelayHunt*').Count -eq $(if ($star -eq 5) {12} else {0})) 'High-tier contract leaked to lower relay'
}
[xml]$items=Get-Content "$root/99-AEC_T16_RuntimeFix/Config/items.xml" -Raw
[xml]$quests=Get-Content "$root/99-AEC_T16_RuntimeFix/Config/quests.xml" -Raw
[xml]$events=Get-Content "$root/99-AEC_T16_RuntimeFix/Config/gameevents.xml" -Raw
[xml]$bosses=Get-Content "$root/98-AECxProjectZ_Tweaks/Config/entityclasses.xml" -Raw
$localization=Import-Csv "$root/99-AEC_T16_RuntimeFix/Config/Localization.csv"
$allQuests=@($quests.SelectNodes("//quest[starts-with(@id,'PZAECRelayHunt')]"))
Check ($allQuests.Count -eq 12) 'Expected 12 hunts'
foreach ($q in $allQuests) {
    $id=$q.id; $tier=[int]$id.Substring($id.Length-2)
    Check ($tier -ge 16 -and $tier -le 19) "Wrong tier: $id"
    $item=$items.SelectSingleNode("//item[@name='item$id']")
    Check ($null -ne $item -and $item.SelectSingleNode("property[@class='Action0']/property[@name='QuestGiven']").value -eq $id) "Missing quest item: $id"
    Check ((Property $q 'shareable') -eq 'false' -and (Property $q 'login_rally_reset') -eq 'false') "Unsafe replay/sharing: $id"
    Check ((Property $q 'add_to_tier_complete') -eq 'false') 'Hunt changes trader progression'
    Check (@($q.reward).Count -eq 1 -and $q.reward.type -eq 'Exp') 'Hunt grants extra loot or vouchers'
    $kill=$q.SelectSingleNode("objective[@type='EntityKill']")
    Check ($kill.value -eq '1' -and $kill.phase -eq '4' -and $kill.id.EndsWith("T$tier")) "Wrong kill target: $id"
    Check ($null -ne $bosses.SelectSingleNode("//entity_class[@name='$($kill.id)']")) "Unknown boss: $($kill.id)"
    $action=$q.SelectSingleNode("action[@phase='4']")
    $event=$events.SelectSingleNode("//action_sequence[@name='$($action.id)']")
    $spawn=$event.SelectSingleNode("action[@class='SpawnEntity']")
    Check ((Property $spawn 'entity_names') -eq $kill.id) 'Spawn and kill target mismatch'
    Check ((Property $spawn 'spawn_count') -eq '1' -and (Property $spawn 'party_addition') -eq '0' -and (Property $spawn 'ignore_multiplier') -eq 'true') 'Unexpected boss multiplication'
    Check ((Property ($q.SelectSingleNode("objective[@type='Time']")) 'time') -eq '15') 'Missing setup delay'
    foreach ($key in @("item$id", "${id}Name", "${id}Desc")) {
        $rows=@($localization | Where-Object Key -eq $key)
        Check ($rows.Count -eq 1 -and $rows[0].english -and $rows[0].schinese) "Missing/duplicate localization $key"
    }
}
Write-Output 'PASS: downward tier catalogs; 12 five-star-only T16-T19 hunts; exact spawn/kill references; owner-only quests; no vouchers/progression rewards; localization; unchanged price and refresh settings.'
