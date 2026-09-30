#Requires -Version 7.0
$ErrorActionPreference='Stop'
$artifacts=Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Force $artifacts | Out-Null
$modPath=(& (Join-Path $PSScriptRoot '../Resolve-ModPath.ps1'))
dotnet run --project (Join-Path $PSScriptRoot 'Network.Tests.csproj') --configuration Release -- $modPath | Tee-Object -FilePath (Join-Path $artifacts 'network-tests.txt')
if($LASTEXITCODE -ne 0){throw 'Fishing F network tests failed'}
