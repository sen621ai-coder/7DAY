#Requires -Version 7.0
$ErrorActionPreference='Stop'
$artifacts=Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Force $artifacts | Out-Null
$modPath=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../ZZZ-PZAEC_Fishing'))
dotnet run --project (Join-Path $PSScriptRoot 'Network.Tests.csproj') --configuration Release -- $modPath | Tee-Object -FilePath (Join-Path $artifacts 'network-tests.txt')
if($LASTEXITCODE -ne 0){throw 'Fishing F network tests failed'}
