# Run each C# fixture in a fresh host so their mocked game types stay isolated.
param([string]$Python='C:/Users/nxvan/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe')
$ErrorActionPreference='Stop'
& $Python "$PSScriptRoot/Test-Assets.py"
if($LASTEXITCODE -ne 0){throw 'M1 asset checks failed'}
foreach($test in @('Test-Rules.ps1','Test-Chassis.ps1','Test-Packets.ps1','Test-ServerFire.ps1','Test-Service.ps1','Test-NativeHooks.ps1')){
    & (Join-Path $PSHOME 'pwsh.exe') -NoProfile -File (Join-Path $PSScriptRoot $test)
    if($LASTEXITCODE -ne 0){throw "M1 fixture failed: $test"}
}
& (Join-Path $PSHOME 'pwsh.exe') -NoProfile -File "$PSScriptRoot/Build.ps1"
if($LASTEXITCODE -ne 0){throw 'M1 V3.2 build failed'}
Write-Output 'All offline M1 checks and native-reference compilation passed. In-game acceptance remains separate.'
