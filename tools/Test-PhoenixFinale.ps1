#Requires -Version 7.0
param([string]$RuntimeDll)
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot
if (-not $RuntimeDll) { $RuntimeDll = Join-Path $modRoot '95-PhoenixBoss/YF.Phoenix.dll' }
Get-ChildItem (Join-Path (Split-Path -Parent $modRoot) '7DaysToDie_Data/Managed') -Filter '*.dll' | ForEach-Object {
    try { [void][Reflection.Assembly]::LoadFrom($_.FullName) } catch { }
}
[void][Reflection.Assembly]::LoadFrom((Resolve-Path $RuntimeDll))
function Check([bool]$ok, [string]$message) { if (-not $ok) { throw $message }; "PASS: $message" }
# Reproduce the real save: night 161, prematurely persisted deadline 3863065.
$state = [YFPhoenix.EncounterState]::new()
$state.Night = 161
$state.Armed = $true
$state.Deadline = 3863065
$state.Participants.Add('regression-player')
Check ([YFPhoenix.PhoenixRules]::ResetActiveDeadline($state)) 'Active horde clears the old saved deadline'
Check ($state.Deadline -eq 0 -and $state.Armed -and $state.Participants.Count -eq 1 -and $state.LastConsumedNight -eq -1) 'Reset preserves eligibility and does not consume the night'
Check (-not [YFPhoenix.PhoenixRules]::ResetActiveDeadline($state)) 'Repeated horde ticks do not request redundant saves'
foreach ($clock in @(@(161,22),@(161,23),@(162,0),@(162,3))) {
    Check (-not [YFPhoenix.PhoenixRules]::FinaleReady(161,$clock[0],$clock[1],4)) "Temporary false flag at day $($clock[0]) hour $($clock[1]) cannot start the finale"
}
Check ([YFPhoenix.PhoenixRules]::FinaleReady(161,162,4,4)) 'Actual dawn opens the finale'
$state.Deadline = [ulong](161 * 24000 + 4000 + 1000)
Check ($state.Deadline -eq 3869000) 'Fresh deadline is day 162 at 05:00, not the old night deadline'
Check (-not [YFPhoenix.PhoenixRules]::FinaleReady(161,162,4,6)) 'Longer nights wait for configured dawn'
Check ([YFPhoenix.PhoenixRules]::FinaleReady(161,162,6,6)) 'Configured dawn opens the finale'
Check (-not [YFPhoenix.PhoenixRules]::FinaleReady(161,163,4,4)) 'Stale nights cannot open a finale'
Check ([YFPhoenix.PhoenixRules]::Tier(314905) -eq 17 -and [YFPhoenix.PhoenixRules]::Tier(333256) -eq 17) 'Both current players retain T17 eligibility'
