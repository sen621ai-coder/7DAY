#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot
$managed = Join-Path (Split-Path -Parent $modRoot) '7DaysToDie_Data/Managed'
if (!(Test-Path (Join-Path $managed 'mscorlib.dll'))) { throw 'Installed game assemblies not found.' }
# Compile against the installed Unity runtime without requiring a global targeting pack.
dotnet build (Join-Path $modRoot '99-AEC_T16_RuntimeFix/Source/T16RuntimeFix.csproj') `
    -c Release "-p:FrameworkPathOverride=$managed" -p:AutomaticallyUseReferenceAssemblyPackages=false `
    -p:NuGetAudit=false "-p:RestorePackagesPath=$(Join-Path $modRoot '.local-tests/packages')" `
    "-p:BaseIntermediateOutputPath=$(Join-Path $modRoot '.local-tests/build/')"
if ($LASTEXITCODE -ne 0) { throw "Runtime build failed: $LASTEXITCODE" }
