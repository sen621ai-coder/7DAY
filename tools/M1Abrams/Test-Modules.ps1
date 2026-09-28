$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
$paths=@("$PSScriptRoot/ModuleFixtures.cs","$root/ZZ-PZAEC_M1Abrams/Source/M1Rules.cs","$root/ZZ-PZAEC_M1Abrams/Source/M1ModuleRules.cs","$root/ZZ-PZAEC_M1Abrams/Source/M1Modules.cs")
Add-Type -Path $paths
[PZAEC.M1.ModuleTests]::Run()
