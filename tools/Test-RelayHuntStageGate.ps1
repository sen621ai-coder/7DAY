#Requires -Version 7.0
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Test-ModelTintFix.ps1')
$checks=0
foreach($tier in 16..19){
    $minimum=@{16=180000;17=280000;18=380000;19=480000}[$tier]
    foreach($family in @('Dumdum','Executioner','Mechanician')){
        $name="itemPZAECRelayHunt${family}T$tier"
        if([AECT16RuntimeFix.RelayHuntStageGate]::RequiredStage($name) -ne $minimum){throw "Wrong threshold: $name"}
        foreach($gs in @(-1,0,($minimum-1),$minimum,($minimum+1),1000000)){
            if([AECT16RuntimeFix.RelayHuntStageGate]::Allowed($name,$gs) -ne ($gs -ge $minimum)){throw "Boundary failure: $name/$gs"}
            $checks++
        }
    }
}
foreach($name in @('','itemPZAECRelayHuntDumdumT15','itemPZAECRelayHuntDumdumT20','itemPZAECRelayHuntDumdumT19_extra','itemPZAECRelayHuntOtherT19','gunPZAECStormReservoirT19','itemPZAECBossLootBundleT19')){
    if(-not [AECT16RuntimeFix.RelayHuntStageGate]::Allowed($name,0)){throw "Unrelated item blocked: $name"}
}
# Verify exact native entrypoints and named Harmony arguments against this game.
foreach($pair in @(@([ItemActionEntryPurchase],'RefreshEnabled'),@([ItemActionEntryPurchase],'OnActivated'),@([BaseItemActionEntry],'OnDisabledActivate'),@([ItemActionQuest],'ExecuteAction'),@([ItemActionQuest],'ExecuteInstantAction'))){
    if(-not [HarmonyLib.AccessTools]::Method($pair[0],$pair[1])){throw "Missing native method $($pair[1])"}
}
$held=[HarmonyLib.AccessTools]::Method([ItemActionQuest],'ExecuteAction')
if(($held.GetParameters().Name -join ',') -ne '_actionData,_bReleased'){throw 'Held-item signature changed'}
$instant=[HarmonyLib.AccessTools]::Method([ItemActionQuest],'ExecuteInstantAction')
if(($instant.GetParameters().Name -join ',') -ne 'ent,stack,isHeldItem,stackController'){throw 'Instant-use signature changed'}
$result=$true
if(-not [AECT16RuntimeFix.RelayHuntStageGate]::InstantPrefix($null,$null,[ref]$result) -or -not $result){throw 'Unrelated instant action intercepted'}
if(-not [AECT16RuntimeFix.RelayHuntStageGate]::HeldPrefix($null,$false)){throw 'Unrelated held action intercepted'}
$label=Import-Csv (Join-Path $modRoot '99-AEC_T16_RuntimeFix/Config/Localization.csv') | Where-Object Key -eq 'aecRelayHuntStageDenied'
if($label.schinese -notmatch '\{0\}.*\{1\}'){throw 'Missing current/required Chinese tooltip'}
"PASS: 12 contracts, $checks stage boundary checks; unrelated items excluded; native purchase/held/instant signatures valid. Live multiplayer UI not exercised."
