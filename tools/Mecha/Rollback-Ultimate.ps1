#Requires -Version 7.0
# Restore the complete pre-Ultimate mod; preserve the current version for recovery.
$ErrorActionPreference = 'Stop'
$mods = [IO.Path]::GetFullPath((Split-Path (Split-Path $PSScriptRoot)))
$live = Join-Path $mods 'ZZ-PZAEC_Mecha'
$backup = Join-Path $mods '.local-tests/Mecha-Nu-20261007-195614/baseline-0.20.3'
$archive = Join-Path $mods ('.local-tests/Mecha-ultimate-rollback-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
if (Get-Process 7DaysToDie,7DaysToDieServer -ErrorAction SilentlyContinue) { throw 'Close the game and QA server before rollback.' }
foreach ($path in @($live,$backup,$archive)) {
    $resolved = [IO.Path]::GetFullPath($path)
    if (!$resolved.StartsWith($mods + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Path escapes Mods: $resolved" }
}
if (!(Test-Path -LiteralPath (Join-Path $backup 'PZAEC.Mecha.dll'))) { throw 'Original backup is missing; nothing was changed.' }
if (Test-Path -LiteralPath $archive) { throw 'Archive already exists; retry later.' }
Move-Item -LiteralPath $live -Destination $archive
try { Copy-Item -LiteralPath $backup -Destination $live -Recurse }
catch { throw "Restore interrupted. Current version is preserved at $archive; original remains at $backup. $($_.Exception.Message)" }
Write-Output "Restored pre-Ultimate version. Preserved Ultimate version: $archive"
