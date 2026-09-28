#Requires -Version 7.0
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
$source=Join-Path $root 'ZZZ-PZAEC_Surveillance/Source'
# Compile the production screen block and placement helper against small game API doubles.
# These tests cover routing/math; shader output and native UI still need a graphical client.
$api=Get-Content (Join-Path $source 'ModApi.cs') -Raw
$screen='using System;using UnityEngine;'+$api.Substring($api.IndexOf('public sealed class BlockPZAEC_SurveillanceScreen'))
$code=(Get-Content (Join-Path $PSScriptRoot 'ScreenRepairHarness.cs') -Raw)+"`n"+(Get-Content (Join-Path $source 'ScreenLayout.cs') -Raw)
$scratch=Join-Path $root ('.local-tests/Surveillance/screen-tests-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $scratch | Out-Null
$screenFile=Join-Path $scratch 'Screen.cs';$harnessFile=Join-Path $scratch 'Harness.cs'
[IO.File]::WriteAllText($screenFile,$screen)
[IO.File]::WriteAllText($harnessFile,$code)
Add-Type -Path @($screenFile,$harnessFile,(Join-Path $source 'ScreenPlacement.cs'))
[ScreenRepairHarness]::Run()
[xml]$xml=Get-Content (Join-Path $root 'ZZZ-PZAEC_Surveillance/Config/blocks.xml')
$block=$xml.configs.append.block|Where-Object name -eq 'PZAEC_SurveillanceScreen4x3'
if($block.property|Where-Object name -in @('Extends','TintColor','Light','RuntimeSwitch')){throw 'Screen inherited a lamp/tint behavior'}
if(($block.property|Where-Object name -eq 'ModelOffset').value -ne '0.5,0,0'){throw 'Native even-width pivot compensation missing'}
Write-Output 'PASS standalone screen XML and native even-width pivot compensation'
