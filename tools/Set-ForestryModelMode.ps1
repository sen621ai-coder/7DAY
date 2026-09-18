#Requires -Version 7.0
param([Parameter(Mandatory)][ValidateSet('Legacy','Compact','Sawmill')][string]$Mode)
$ErrorActionPreference='Stop'
if(Get-Process 7DaysToDie,7DaysToDieServer -ErrorAction SilentlyContinue){throw 'Close the game/server before changing forestry footprint.'}
$root=Split-Path $PSScriptRoot
$path=Join-Path $root '98-AECxProjectZ_Tweaks/Config/blocks.xml'
$fragment=Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot "Forestry/$Mode.xml")
$text=Get-Content -Raw -LiteralPath $path
$pattern='<block name="yfAutoForestry">[\s\S]*?</block>'
if([regex]::Matches($text,$pattern).Count -ne 1){throw 'Expected exactly one forestry block.'}
$new=[regex]::Replace($text,$pattern,[Text.RegularExpressions.MatchEvaluator]{param($m) $fragment.Trim()})
$null=[xml]$new
[IO.File]::WriteAllText($path,$new,[Text.UTF8Encoding]::new($false))
Write-Output "Forestry mode: $Mode. Other blocks and save files were not changed."
