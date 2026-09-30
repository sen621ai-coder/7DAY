#Requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'Simulation.Tests.csproj'
$artifacts = Join-Path $PSScriptRoot 'artifacts'
dotnet run --project $project --configuration Release -- $artifacts
if ($LASTEXITCODE -ne 0) { throw 'Fishing simulation tests failed.' }
