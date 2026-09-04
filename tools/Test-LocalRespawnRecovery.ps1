$ErrorActionPreference = 'Stop'

$modRoot = Split-Path -Parent $PSScriptRoot
$references = @(
    (Join-Path $modRoot '00-TFP_Harmony/Mono.Cecil.dll'),
    (Join-Path $modRoot '../7DaysToDie_Data/Managed/Assembly-CSharp.dll')
)
foreach ($reference in $references) {
    if (-not (Test-Path -LiteralPath $reference)) {
        throw "Missing test dependency: $reference"
    }
    Add-Type -Path $reference
}

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$runtimePath = Join-Path $modRoot '99-AEC_T16_RuntimeFix/AEC.T16.RuntimeFix.dll'
$gamePath = Join-Path $modRoot '../7DaysToDie_Data/Managed/Assembly-CSharp.dll'
Assert-True (Test-Path -LiteralPath $runtimePath) 'Runtime DLL has not been built.'

$runtime = [Mono.Cecil.ModuleDefinition]::ReadModule($runtimePath)
$game = [Mono.Cecil.ModuleDefinition]::ReadModule($gamePath)
try {
    $fix = $runtime.Types | Where-Object FullName -eq 'AECT16RuntimeFix.LocalRespawnRecovery'
    Assert-True ($null -ne $fix) 'LocalRespawnRecovery is missing.'

    $install = $fix.Methods | Where-Object Name -eq 'Install'
    $installStrings = @($install.Body.Instructions | Where-Object { $_.OpCode.Code -eq 'Ldstr' } |
        ForEach-Object { [string]$_.Operand })
    foreach ($target in @('OnEntityDeath', 'Update', 'Respawn')) {
        Assert-True ($installStrings -contains $target) "Missing Harmony target: $target"
    }

    $update = $fix.Methods | Where-Object Name -eq 'AfterLocalUpdate'
    $calls = @($update.Body.Instructions | ForEach-Object { $_.Operand } |
        Where-Object { $_ -is [Mono.Cecil.MethodReference] })
    foreach ($call in @('IsDead', 'IsOpenInUI', 'Respawn')) {
        Assert-True ($calls.Name -contains $call) "Recovery guard/call is missing: $call"
    }
    Assert-True (@($update.Body.Instructions | Where-Object {
        $_.OpCode.Code -eq 'Ldc_R4' -and [single]$_.Operand -eq 12
    }).Count -gt 0) 'Recovery grace period changed unexpectedly.'

    $player = $game.Types | Where-Object Name -eq 'EntityPlayerLocal'
    $native = $player.Methods | Where-Object Name -eq 'OnDeathUpdate'
    $nativeCalls = @($native.Body.Instructions | ForEach-Object { $_.Operand } |
        Where-Object { $_ -is [Mono.Cecil.MethodReference] })
    Assert-True ($nativeCalls.Name -contains 'get_Spawned') 'Native Spawned gate changed; reassess recovery.'
    Assert-True ($nativeCalls.Name -contains 'Respawn') 'Native death transition changed; reassess recovery.'

    Write-Host 'Local respawn recovery checks passed.'
}
finally {
    $runtime.Dispose()
    $game.Dispose()
}
