#Requires -Version 7.0
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
$scratch=Join-Path $root ('.local-tests/Surveillance/marker-state-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $scratch | Out-Null
Add-Type -Path @((Join-Path $root 'ZZZ-PZAEC_Surveillance/Source/SurveillanceState.cs'),(Join-Path $root 'ZZZ-PZAEC_Surveillance/Source/SurveillanceProtocol.cs'),(Join-Path $PSScriptRoot 'MarkerStateHarness.cs'))
[MarkerStateHarness]::Run((Join-Path $scratch 'state.xml'))
