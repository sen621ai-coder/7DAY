#Requires -Version 7.0
param([string]$MonoPath='D:/unity/2022.3.48f1c1/Editor/Data/MonoBleedingEdge/bin/mono.exe')
$ErrorActionPreference='Stop'
$output=Join-Path $PSScriptRoot 'artifacts/contracts'
& (Join-Path $PSScriptRoot 'Build.ps1') -CoreOnly -Executable -AssemblyName 'Fishing.ContractHarness' -OutputDirectory $output -ExtraSource (Join-Path $PSScriptRoot 'ContractHarness.cs')
if(!(Test-Path -LiteralPath $MonoPath)){throw 'A Mono runtime is required; supply -MonoPath.'}
& $MonoPath (Join-Path $output 'Fishing.ContractHarness.exe') | Tee-Object (Join-Path $output 'result.txt')
if($LASTEXITCODE){throw 'Contract harness failed'}
