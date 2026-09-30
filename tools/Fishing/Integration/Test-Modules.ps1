#Requires -Version 7.0
$ErrorActionPreference='Stop'
$modPath=(& (Join-Path $PSScriptRoot '../Resolve-ModPath.ps1'))
dotnet run --project (Join-Path $PSScriptRoot 'Modules.Tests.csproj') --configuration Release -- $modPath
if($LASTEXITCODE -ne 0){throw 'Cross-module fishing tests failed'}
