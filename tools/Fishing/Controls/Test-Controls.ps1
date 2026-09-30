$ErrorActionPreference = 'Stop'
dotnet run --project (Join-Path $PSScriptRoot 'Controls.Tests.csproj') --configuration Release
if ($LASTEXITCODE -ne 0) { throw "Fishing controls tests failed ($LASTEXITCODE)." }
