#Requires -Version 7.0
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
Add-Type -Path @((Join-Path $root 'ZZZ-PZAEC_Surveillance/Source/TargetMarkerRules.cs'),(Join-Path $PSScriptRoot 'MarkerTests.cs'))
[SurveillanceMarkerTests]::Run()
