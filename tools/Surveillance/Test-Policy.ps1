#Requires -Version 7.0
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
Add-Type -Path @((Join-Path $root 'ZZZ-PZAEC_Surveillance/Source/RenderPolicy.cs'),(Join-Path $PSScriptRoot 'PolicyTests.cs'))
[SurveillancePolicyTests]::Run()
