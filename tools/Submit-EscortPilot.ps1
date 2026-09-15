#Requires -Version 7.0
# Run manually. No reset, clean, merge, rebase or force push.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$expectedHead = '3eedf4ad75b86cf35e32d6ea7460a896e723ada6'
$expectedRemote = 'https://github.com/sen621ai-coder/7DAY.git'
$paths = @(
    '.gitignore',
    '99-AEC_T16_RuntimeFix/AEC.T16.RuntimeFix.dll',
    '99-AEC_T16_RuntimeFix/ESCORT_PILOT.md',
    '99-AEC_T16_RuntimeFix/Source/ConsoleCmdEscortSurvey.cs',
    '99-AEC_T16_RuntimeFix/Source/EscortPrototype.cs',
    'tools/Test-EscortPrototype.ps1',
    'tools/Submit-EscortPilot.ps1'
)
function Git {
    & git @args
    if ($LASTEXITCODE -ne 0) { throw "git command failed (exit $LASTEXITCODE). No automatic cleanup or rollback was performed." }
}
Push-Location -LiteralPath $repo
try {
    if ((Git rev-parse --show-toplevel).Trim().Replace('\','/') -ne $repo.Replace('\','/')) { throw 'Wrong repository root.' }
    if ((Git branch --show-current).Trim() -ne 'main') { throw 'Not on main. Review manually.' }
    if ((Git rev-parse HEAD).Trim() -ne $expectedHead) { throw 'HEAD changed since this script was prepared. Review before committing; if already committed, push that commit separately.' }
    if ((Git remote get-url origin).Trim() -ne $expectedRemote -or (Git remote get-url --push origin).Trim() -ne $expectedRemote) { throw 'Unexpected origin URL.' }
    if (@(Git diff --cached --name-only).Count) { throw 'Staging area is not empty. Review staged files manually first.' }
    foreach ($state in @('MERGE_HEAD','CHERRY_PICK_HEAD','REVERT_HEAD','rebase-merge','rebase-apply')) {
        $statePath = (Git rev-parse --git-path $state).Trim()
        if (Test-Path -LiteralPath $statePath) { throw "Git operation in progress: $state" }
    }
    $dirty = @(Git diff --name-only) + @(Git ls-files --others --exclude-standard)
    $unexpected = @($dirty | Where-Object { $_ -notin $paths })
    if ($unexpected.Count) { throw "Unexpected local changes. Review first: $($unexpected -join ', ')" }
    foreach ($path in $paths) { if (!(Test-Path -LiteralPath $path)) { throw "Missing file: $path" } }
    if (@(Git ls-files -- '04-AEC-ENDGAME_OVERHAUL/AeclipseNemesisPool.json').Count) { throw 'Nemesis pool is tracked again; review before proceeding.' }
    Git check-ignore --quiet -- '04-AEC-ENDGAME_OVERHAUL/AeclipseNemesisPool.json'
    Git diff --check
    Git fetch origin
    Git merge-base --is-ancestor origin/main HEAD
    Write-Host 'Existing commits that this push will also upload:'
    Git log --oneline origin/main..HEAD
    Write-Host 'Files to include in the new commit:'
    $paths | ForEach-Object { Write-Host "  $_" }
    Write-Host 'Includes your .gitignore rule. The existing 3eedf4a also contains contract, party-credit and capacitor changes, plus removal of NemesisPool from version control.'
    Write-Host 'The escort pilot is unfinished: survey/model only, not a playable escort quest.'
    Write-Host 'Author: itgou1 <65017794+itgou1@users.noreply.github.com>; destination: origin/main'
    if ((Read-Host 'Type PUSH to run tests, commit and push') -cne 'PUSH') { Write-Host 'Cancelled; no staging, commit or push performed.'; return }
    foreach ($test in @('Test-EscortPrototype.ps1','Test-ContractRelayCatalog.ps1','Test-RelayHuntPartyCredit.ps1','Test-SiegeCapacitorSources.ps1','Test-LegendaryDefenseSharing.ps1')) {
        & pwsh -NoProfile -File (Join-Path $PSScriptRoot $test)
        if ($LASTEXITCODE -ne 0) { throw "Test failed: $test. Nothing staged by this script." }
    }
    # Recheck immediately before staging in case something changed during tests.
    if ((Git rev-parse HEAD).Trim() -ne $expectedHead -or @(Git diff --cached --name-only).Count) { throw 'HEAD or staging changed during tests.' }
    $unexpected = @((@(Git diff --name-only) + @(Git ls-files --others --exclude-standard)) | Where-Object { $_ -notin $paths })
    if ($unexpected.Count) { throw 'Unexpected changes appeared during tests. Review manually.' }
    Git add -- @paths
    Git diff --cached --check
    Git diff --cached --stat
    Git -c user.name=itgou1 -c user.email=65017794+itgou1@users.noreply.github.com commit --author='itgou1 <65017794+itgou1@users.noreply.github.com>' -m 'feat: add offline T16 escort pilot and route survey'
    # Normal push only. A concurrent remote update will be rejected safely.
    Git push origin HEAD:main
    Git status --short --branch
    Write-Host 'Committed and pushed successfully.'
} finally {
    Pop-Location
}
