param([string]$Python='C:/Users/nxvan/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe')
$ErrorActionPreference='Stop'
& $Python "$PSScriptRoot/Test-Config.py"
if($LASTEXITCODE -ne 0){throw 'Sealed cabin merged configuration checks failed'}
& (Join-Path $PSHOME 'pwsh.exe') -NoProfile -File "$PSScriptRoot/Test-Runtime.ps1"
if($LASTEXITCODE -ne 0){throw 'Sealed cabin runtime/native checks failed'}
& (Join-Path $PSHOME 'pwsh.exe') -NoProfile -File "$PSScriptRoot/Build.ps1"
if($LASTEXITCODE -ne 0){throw 'Sealed cabin native-reference build failed'}
Write-Output 'All offline sealed cabin checks passed; DLL installed. In-game acceptance remains separate.'
