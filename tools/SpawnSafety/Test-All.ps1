$ErrorActionPreference='Stop'
# Each fixture defines isolated game types. Do not load them together in one CLR.
foreach($name in @('Test-Rules.ps1','Test-Sites.ps1','Test-Diagnostics.ps1','Test-Deferred.ps1','Test-Native.ps1')) {
    & (Join-Path $PSHOME 'pwsh.exe') -NoProfile -File (Join-Path $PSScriptRoot $name)
    if($LASTEXITCODE -ne 0){throw "$name failed ($LASTEXITCODE)"}
}
