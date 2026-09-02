$ErrorActionPreference = 'Stop'
# Also checks the prior console-error fix and supplies a read-only game IL reader.
. (Join-Path $PSScriptRoot 'Test-ModelTintFix.ps1')

function Assert-Headshot([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
$target = [HarmonyLib.AccessTools]::Method([ItemActionAttack], 'Hit')
$generator = [Reflection.Emit.DynamicMethod]::new('HeadshotRegression', [void], [Type[]]@()).GetILGenerator()
$original = [ModelTintRegression]::ReadGameIL($target, $generator)
$patched = @([AECT16RuntimeFix.PlayerHeadshotDamagePatch]::Transpiler($original))
Assert-Headshot ($patched.Count -eq $original.Count + 5) 'Unexpected number of inserted instructions'
$scaleSites = @(for ($i = 0; $i -lt $patched.Count; $i++) {
    if ($patched[$i].operand -is [Reflection.MethodInfo] -and $patched[$i].operand.Name -eq 'ScaleDamage') { $i }
})
Assert-Headshot ($scaleSites.Count -eq 1) 'Expected exactly one headshot multiplier'
$scale = $scaleSites[0]
Assert-Headshot ($patched[$scale - 3].opcode -eq [Reflection.Emit.OpCodes]::Isinst -and
    $patched[$scale - 3].operand -eq [EntityPlayer]) 'Non-player attacks are not excluded'
Assert-Headshot ($patched[$scale - 2].opcode -eq [Reflection.Emit.OpCodes]::Ldnull -and
    $patched[$scale - 1].opcode -eq [Reflection.Emit.OpCodes]::Cgt_Un) 'Player type test is incorrect'
Assert-Headshot ($patched[$scale - 5].opcode -eq [Reflection.Emit.OpCodes]::Conv_I4 -and
    $patched[$scale - 6].opcode -eq [Reflection.Emit.OpCodes]::Mul) 'Multiplier is not after original damage calculation'
Assert-Headshot ([HarmonyLib.CodeInstructionExtensions]::IsStloc($patched[$scale + 1])) 'Calculated damage is not stored'

# Every existing opcode, operand and label must remain untouched and in order.
$withoutPatch = @($patched[0..($scale - 5)]) + @($patched[($scale + 1)..($patched.Count - 1)])
Assert-Headshot ($withoutPatch.Count -eq $original.Count) 'Original control flow changed'
for ($i = 0; $i -lt $original.Count; $i++) {
    Assert-Headshot ([object]::ReferenceEquals($withoutPatch[$i], $original[$i])) "Original instruction changed at $i"
}
$query = -1
for ($i = 0; $i -lt $original.Count; $i++) {
    if ($original[$i].opcode -eq [Reflection.Emit.OpCodes]::Ldc_I4 -and
        $original[$i].operand -is [int] -and $original[$i].operand -eq [int][PassiveEffects]::HeadshotDamageModifier) {
        $query = $i
        break
    }
}
Assert-Headshot ($query -ge 1) 'Missing original headshot query'
Assert-Headshot ($patched[$scale - 4].opcode -eq $original[$query + 4].opcode -and
    [object]::Equals($patched[$scale - 4].operand, $original[$query + 4].operand)) 'Wrong attacker local'
$skip = $original[$query - 1]
Assert-Headshot ($skip.opcode -eq [Reflection.Emit.OpCodes]::Brfalse_S -or
    $skip.opcode -eq [Reflection.Emit.OpCodes]::Brfalse) 'Missing non-headshot bypass'
$bypass = @(for ($i = 0; $i -lt $patched.Count; $i++) {
    if ($patched[$i].labels.Contains($skip.operand)) { $i }
})
Assert-Headshot ($bypass.Count -eq 1 -and $bypass[0] -gt $scale) 'Body shots do not bypass the multiplier'

$tests = 0
foreach ($damage in @(0, 1, 100, 2000, 8400, 100000000, 429496729, 429496730, [int]::MaxValue, -1)) {
    foreach ($isPlayer in @($true, $false)) {
        $expected = if ($isPlayer -and $damage -gt 0) { [int][Math]::Min([long]$damage * 5, [long][int]::MaxValue) } else { $damage }
        $result = [AECT16RuntimeFix.PlayerHeadshotDamagePatch]::ScaleDamage($damage, $isPlayer)
        Assert-Headshot ($result -eq $expected) "Damage mismatch: $damage, player=$isPlayer"
        $tests++
    }
}
$rejected = $false
try {
    [void][AECT16RuntimeFix.PlayerHeadshotDamagePatch]::Transpiler([HarmonyLib.CodeInstruction[]]@(
        [HarmonyLib.CodeInstruction]::new([Reflection.Emit.OpCodes]::Ret, $null)))
} catch { $rejected = $true }
Assert-Headshot $rejected 'Unrecognized game code was not rejected'
$rejected = $false
try {
    [void][AECT16RuntimeFix.PlayerHeadshotDamagePatch]::Transpiler([HarmonyLib.CodeInstruction[]]@(@($original) + @($original)))
} catch { $rejected = $true }
Assert-Headshot $rejected 'Ambiguous game code was not rejected'

[xml]$items = Get-Content -LiteralPath (Join-Path $modRoot '98-AECxProjectZ_Tweaks/Config/items.xml') -Raw
$bonus = @($items.configs.set | Where-Object { $_.xpath -like '*gunRifleT4SniperRifleImp*gunRifleT5SniperRifleGaus*' -and $_.xpath -like '*DamageModifier*' })
Assert-Headshot ($bonus.Count -eq 1 -and $bonus[0].InnerText -eq '2000') 'Existing sniper +2000 override changed'
Write-Output "PASS: current game headshot IL; body-shot bypass; player-only guard; $tests damage/overflow cases; unsupported patterns; sniper +2000 preserved."
