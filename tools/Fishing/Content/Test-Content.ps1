#Requires -Version 7.0
$ErrorActionPreference='Stop'
$mod=(& (Join-Path $PSScriptRoot '../Resolve-ModPath.ps1'))
dotnet build (Join-Path $PSScriptRoot 'Content.Tests.csproj') -c Release --nologo
if($LASTEXITCODE){throw 'Content test compilation failed.'}
dotnet (Join-Path $PSScriptRoot 'artifacts/bin/net8.0/Content.Tests.dll') $mod
if($LASTEXITCODE){throw '.NET 8 content tests failed.'}
& (Join-Path $PSScriptRoot 'artifacts/bin/net48/Content.Tests.exe') $mod
if($LASTEXITCODE){throw '.NET Framework content tests failed.'}
& (Join-Path $PSScriptRoot 'Test-Config.ps1')
