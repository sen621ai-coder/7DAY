#Requires -Version 7.0
param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$modRoot=Split-Path $PSScriptRoot
if(-not $OutputDirectory){$OutputDirectory=Join-Path $modRoot '.local-tests/CargoDrones/Core'}
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$sources=@(
    (Join-Path $modRoot '97-AutomationWorkshop/Source/CargoDroneDomain.cs'),
    (Join-Path $modRoot '97-AutomationWorkshop/Source/CargoDroneJournal.cs'),
    (Join-Path $modRoot '97-AutomationWorkshop/Source/CargoDroneLeases.cs'),
    (Join-Path $modRoot '97-AutomationWorkshop/Source/CargoDroneBindings.cs'),
    (Join-Path $modRoot '97-AutomationWorkshop/Source/CargoDroneWarehouse.cs'),
    (Join-Path $modRoot '97-AutomationWorkshop/Source/CargoDroneRecovery.cs'),
    (Join-Path $modRoot '97-AutomationWorkshop/Source/CargoDronePreparedRecovery.cs'),
    (Join-Path $modRoot '97-AutomationWorkshop/Source/CargoDroneMotion.cs'),
    (Join-Path $modRoot '97-AutomationWorkshop/Source/CargoDroneRouting.cs'),
    (Join-Path $modRoot '97-AutomationWorkshop/Source/CargoDroneReturnTrail.cs'),
    (Join-Path $modRoot '97-AutomationWorkshop/Source/CargoDroneMission.cs'),
    (Join-Path $modRoot '97-AutomationWorkshop/Source/CargoDroneCheckpoint.cs'),
    (Join-Path $modRoot '97-AutomationWorkshop/Source/CargoDroneWorld.cs'),
    (Join-Path $modRoot '97-AutomationWorkshop/Source/CargoDroneAccessSession.cs'),
    (Join-Path $modRoot '97-AutomationWorkshop/Source/CargoDroneRecoveryDelivery.cs'),
    (Join-Path $PSScriptRoot 'CargoDrones/CargoDroneCoreTests.cs')
    (Join-Path $PSScriptRoot 'CargoDrones/CargoDroneWorldTests.cs')
)
Add-Type -Path $sources
$report=[CargoDroneCoreTests]::Run([IO.Path]::GetFullPath($OutputDirectory))
$worldChecks=[CargoDroneWorldTests]::Run([IO.Path]::GetFullPath($OutputDirectory))
$report+="`nPASS world orchestration checks=$worldChecks; scope=admission, real core endpoint transfers, world checkpoint, lifecycle; native and multiplayer NOT tested by this suite."
$report | Set-Content -LiteralPath (Join-Path $OutputDirectory 'report.txt') -Encoding utf8
$report
