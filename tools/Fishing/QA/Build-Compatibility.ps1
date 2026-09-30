#Requires -Version 7.0
$ErrorActionPreference='Stop'
$mods=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$managed=Join-Path (Split-Path $mods) '7DaysToDie_Data/Managed'
$sdkLine=@(dotnet --list-sdks)[-1]
if($sdkLine -notmatch '^([^ ]+) \[(.+)\]$'){throw 'Cannot locate SDK compiler'}
$compiler=Join-Path $Matches[2] ($Matches[1]+'/Roslyn/bincore/csc.dll')
$outDir=Join-Path $PSScriptRoot 'artifacts/compatibility'
New-Item -ItemType Directory -Force $outDir | Out-Null
$compileArgs=@('/nologo','/target:library','/langversion:7.3','/noconfig','/nostdlib+','/warnaserror+',('/out:'+(Join-Path $outDir 'PZAEC.Fishing.Networking.Check.dll')))
foreach($name in @('mscorlib.dll','System.dll','System.Core.dll','netstandard.dll','Assembly-CSharp.dll','Assembly-CSharp-firstpass.dll','UnityEngine.CoreModule.dll','LogLibrary.dll')) {
    $compileArgs+='/reference:'+(Join-Path $managed $name)
}
$compileArgs+=@(Get-ChildItem (Join-Path $mods 'ZZZ-PZAEC_Fishing/Source/Networking') -Filter '*.cs' | ForEach-Object FullName)
$compileArgs+=@(Get-ChildItem (Join-Path $mods 'ZZZ-PZAEC_Fishing/Source/Contracts') -Filter '*.cs' | ForEach-Object FullName)
Set-Content -LiteralPath (Join-Path $outDir 'build.txt') -Value '' -Encoding utf8
dotnet $compiler @compileArgs | Tee-Object -FilePath (Join-Path $outDir 'build.txt') -Append
if($LASTEXITCODE -ne 0){throw 'F networking compatibility compilation failed'}
'PASS F networking and native packages compiled against installed game assemblies; no gameplay DLL installed.' | Tee-Object -FilePath (Join-Path $outDir 'build.txt') -Append
