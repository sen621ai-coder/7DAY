#Requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$mods = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$managed = Join-Path (Split-Path $mods) '7DaysToDie_Data/Managed'
$sdkLine = @(dotnet --list-sdks)[-1]
if ($sdkLine -notmatch '^([^ ]+) \[(.+)\]$') { throw 'Cannot locate installed SDK compiler.' }
$compiler = Join-Path $Matches[2] ($Matches[1] + '/Roslyn/bincore/csc.dll')
$outDir = Join-Path $PSScriptRoot 'artifacts/compatibility'
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$compileArgs = @('/nologo', '/target:library', '/langversion:7.3', '/noconfig', '/nostdlib+', '/warnaserror+', ('/out:' + (Join-Path $outDir 'PZAEC.Fishing.Simulation.Check.dll')))
foreach ($name in @('mscorlib.dll', 'System.dll', 'System.Core.dll')) {
    $compileArgs += '/reference:' + (Join-Path $managed $name)
}
foreach ($folder in @('Contracts','Simulation')) {
    $compileArgs += @(Get-ChildItem (Join-Path $mods ('ZZZ-PZAEC_Fishing/Source/' + $folder)) -Filter '*.cs' | ForEach-Object FullName)
}
dotnet $compiler @compileArgs
if ($LASTEXITCODE -ne 0) { throw "Compatibility build failed ($LASTEXITCODE)." }
$message = 'PASS: B simulation and v1 adapter compiled against installed game framework assemblies (C# 7.3, warnings as errors). No game DLL installed.'
$message | Set-Content (Join-Path $outDir 'build-results.txt')
Write-Output $message
