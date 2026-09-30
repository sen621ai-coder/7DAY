#Requires -Version 7.0
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path (Split-Path $PSScriptRoot))
$sources=@(Get-ChildItem "$root/ZZZ-PZAEC_Fishing/Source/Contracts" -Filter '*.cs')+@(Get-ChildItem "$root/ZZZ-PZAEC_Fishing/Source/Simulation" -Filter '*.cs')
Add-Type -Path ($sources.FullName+"$PSScriptRoot/GeometryAudit.cs")
$report=[FishingGeometryAudit]::Run()
New-Item -ItemType Directory -Force "$PSScriptRoot/artifacts" | Out-Null
$report | Set-Content "$PSScriptRoot/artifacts/geometry-audit.txt"
$report.Split("`n") | Select-Object -First 5
