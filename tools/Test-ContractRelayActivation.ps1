$ErrorActionPreference = 'Stop'

$modRoot = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $modRoot '03-AEC-AIO_BOSS_EXTREME_EDITION/Config'
$fixRoot = Join-Path $modRoot '99-AEC_T16_RuntimeFix/Config'

function Assert-Relay([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

[xml]$sourceBlocks = Get-Content -LiteralPath (Join-Path $sourceRoot 'blocks.xml') -Raw
[xml]$sourceEvents = Get-Content -LiteralPath (Join-Path $sourceRoot 'gameevents.xml') -Raw
[xml]$fixBlocks = Get-Content -LiteralPath (Join-Path $fixRoot 'blocks.xml') -Raw
[xml]$fixEvents = Get-Content -LiteralPath (Join-Path $fixRoot 'gameevents.xml') -Raw

$relayNames = @('aecQuestRelay','aecQuestRelay1star','aecQuestRelay2star','aecQuestRelay3star','aecQuestRelay4star','aecQuestRelay5star')
$spawnNames = @('eventAECQuestRelaySpawn','eventAECQuestRelaySpawn1star','eventAECQuestRelaySpawn2star','eventAECQuestRelaySpawn3star','eventAECQuestRelaySpawn4star','eventAECQuestRelaySpawn5star')

for ($i = 0; $i -lt $relayNames.Count; $i++) {
    $relay = $relayNames[$i]
    $spawn = $spawnNames[$i]
    $block = $sourceBlocks.SelectSingleNode("/config/append/block[@name='$relay']")
    $sequence = $sourceEvents.SelectSingleNode("/configs/append/action_sequence[@name='$spawn']")
    Assert-Relay ($null -ne $block) "Missing source relay: $relay"
    Assert-Relay ($null -ne $sequence) "Missing source relay spawn event: $spawn"
    Assert-Relay ($null -ne $sequence.SelectSingleNode("action[@class='SpawnEntity']")) "Relay event does not spawn its quest giver: $spawn"

    $blockSet = $fixBlocks.SelectSingleNode("/configs/set[@xpath=`"/blocks/block[@name='$relay']/property[@name='ActivateEvent']/@value`"]")
    $eventSet = $fixEvents.SelectSingleNode("/configs/set[@xpath=`"/gameevents/action_sequence[@name='$spawn']/property[@name='target_type']/@value`"]")
    Assert-Relay ($null -ne $blockSet -and $blockSet.InnerText -eq $spawn) "Relay still uses the position-losing CallGameEvent wrapper: $relay"
    Assert-Relay ($null -ne $eventSet -and $eventSet.InnerText -eq 'Block') "Relay spawn event does not accept a block target: $spawn"
}

Write-Host 'PASS: all six contract relays directly invoke block-targeted quest-giver spawn events.'
