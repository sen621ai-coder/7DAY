#Requires -Version 7.0
$ErrorActionPreference='Stop'
if(Get-Process -Name '7DaysToDie','7DaysToDieServer' -ErrorAction SilentlyContinue){throw 'Close the game and isolated probe before installing the test DLL.'}
$modPath=(& (Join-Path $PSScriptRoot '../Resolve-ModPath.ps1'))
$source=Join-Path $PSScriptRoot 'artifacts/build/PZAEC.Fishing.dll'
$target=Join-Path $modPath 'PZAEC.Fishing.dll'
if(!(Test-Path -LiteralPath $source)){throw 'Build the production PZAEC.Fishing assembly first.'}
if(Test-Path -LiteralPath $target){
    $backup=Join-Path $PSScriptRoot ('artifacts/backups/'+(Get-Date -Format 'yyyyMMdd-HHmmssfff'))
    New-Item -ItemType Directory -Force $backup|Out-Null
    Copy-Item -LiteralPath $target -Destination $backup
}
Copy-Item -LiteralPath $source -Destination $target
$sourceHash=(Get-FileHash -LiteralPath $source).Hash
if((Get-FileHash -LiteralPath $target).Hash -ne $sourceHash){throw 'Installed DLL hash mismatch'}
[pscustomobject]@{Installed=$target;SHA256=$sourceHash;Profile='Local authoritative single-player test build'}|ConvertTo-Json
