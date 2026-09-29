# Run each C# fixture in a fresh host so their mocked game types stay isolated.
param([string]$Python='C:/Users/nxvan/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe',[string]$OutputPath)
$ErrorActionPreference='Stop'
& $Python "$PSScriptRoot/Test-Assets.py"
if($LASTEXITCODE -ne 0){throw 'M1 asset checks failed'}
& $Python "$PSScriptRoot/Test-ModuleConfig.py"
if($LASTEXITCODE -ne 0){throw 'M1 module config checks failed'}
& $Python "$PSScriptRoot/Test-RecipeKits.py"
if($LASTEXITCODE -ne 0){throw 'M1 recipe visibility and material conservation checks failed'}
& $Python "$PSScriptRoot/Test-SecondaryAssets.py"
if($LASTEXITCODE -ne 0){throw 'M1 secondary asset/config checks failed'}
foreach($test in @('Test-Storage.ps1','Test-SaveSync.ps1','Test-Secondary.ps1','Test-Modules.ps1','Test-CrewVisibility.ps1','Test-Rules.ps1','Test-Chassis.ps1','Test-Packets.ps1','Test-ServerFire.ps1','Test-Service.ps1','Test-NativeHooks.ps1')){
    & (Join-Path $PSHOME 'pwsh.exe') -NoProfile -File (Join-Path $PSScriptRoot $test)
    if($LASTEXITCODE -ne 0){throw "M1 fixture failed: $test"}
}
$buildArgs=@('-NoProfile','-File',"$PSScriptRoot/Build.ps1")
if($OutputPath){$buildArgs+=@('-OutputPath',$OutputPath)}
& (Join-Path $PSHOME 'pwsh.exe') @buildArgs
if($LASTEXITCODE -ne 0){throw 'M1 V3.2 build failed'}
Write-Output 'All offline M1 checks and native-reference compilation passed. In-game acceptance remains separate.'
