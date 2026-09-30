#Requires -Version 7.0
$ErrorActionPreference='Stop'
$modPath=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../ZZZ-PZAEC_Fishing'))
dotnet run --project (Join-Path $PSScriptRoot 'Modules.Tests.csproj') --configuration Release -- $modPath
if($LASTEXITCODE -ne 0){throw 'Cross-module fishing tests failed'}
