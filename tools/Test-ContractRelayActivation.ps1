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
$runtimePath = Join-Path $modRoot '99-AEC_T16_RuntimeFix/AEC.T16.RuntimeFix.dll'

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

$pickup = $fixBlocks.SelectSingleNode('/configs/append[@xpath="/blocks/block[@name=''aecQuestRelay'']"]/property[@name="CanPickup"]')
Assert-Relay ($null -ne $pickup -and $pickup.value -eq 'true') 'Contract relay pickup was not enabled.'

Add-Type -Path (Join-Path $modRoot '00-TFP_Harmony/Mono.Cecil.dll')
$runtime = [Mono.Cecil.ModuleDefinition]::ReadModule($runtimePath)
try {
    $pickupFix = $runtime.Types | Where-Object FullName -eq 'AECT16RuntimeFix.ContractRelayPickup'
    Assert-Relay ($null -ne $pickupFix) 'Compiled contract-relay pickup patch is missing.'
    $install = $pickupFix.Methods | Where-Object Name -eq 'Install'
    $installStrings = @($install.Body.Instructions | Where-Object { $_.OpCode.Code -eq 'Ldstr' } | ForEach-Object { [string]$_.Operand })
    Assert-Relay ($installStrings -contains 'GetBlockActivationCommands') 'Pickup command-list hook is missing.'
    Assert-Relay ($installStrings -contains 'OnBlockActivated') 'Pickup activation hook is missing.'

    $activation = $pickupFix.Methods | Where-Object Name -eq 'BeforeActivation'
    $calls = @($activation.Body.Instructions | ForEach-Object { $_.Operand } | Where-Object { $_ -is [Mono.Cecil.MethodReference] })
    Assert-Relay (@($calls | Where-Object { $_.DeclaringType.Name -eq 'Block' -and $_.Name -eq 'OnBlockActivated' }).Count -eq 1) 'Pickup does not delegate to the native Block permission/inventory path.'
}
finally {
    $runtime.Dispose()
}

Write-Host 'PASS: all six contract relays summon from their block target and expose native guarded pickup.'
