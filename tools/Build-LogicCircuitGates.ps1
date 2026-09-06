#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot
$managed = Join-Path (Split-Path -Parent $modRoot) '7DaysToDie_Data/Managed'
if (!(Test-Path (Join-Path $managed 'mscorlib.dll'))) { throw 'Installed game assemblies not found.' }
dotnet build (Join-Path $modRoot '97-LogicCircuitGates/Source/LogicCircuitGates.csproj') `
    -c Release "-p:FrameworkPathOverride=$managed" -p:AutomaticallyUseReferenceAssemblyPackages=false `
    -p:NuGetAudit=false "-p:BaseIntermediateOutputPath=$(Join-Path $modRoot '.local-tests/logic-build/')"
if ($LASTEXITCODE -ne 0) { throw "Logic circuit build failed: $LASTEXITCODE" }
