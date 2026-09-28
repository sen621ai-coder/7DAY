$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
Add-Type -Path @("$PSScriptRoot/CrewVisibilityFixtures.cs","$root/ZZ-PZAEC_M1Abrams/Source/M1CrewVisibility.cs")
[PZAEC.M1.CrewVisibilityTests]::Run()
