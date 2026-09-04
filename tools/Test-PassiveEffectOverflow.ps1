#Requires -Version 7.0
$ErrorActionPreference = 'Stop'

$modRoot = Split-Path -Parent $PSScriptRoot
$managed = Join-Path (Split-Path -Parent $modRoot) '7DaysToDie_Data/Managed'
Get-ChildItem $managed -Filter '*.dll' | ForEach-Object {
    try { [void][Reflection.Assembly]::LoadFrom($_.FullName) } catch {}
}
[void][Reflection.Assembly]::LoadFrom((Join-Path $modRoot '00-TFP_Harmony/0Harmony.dll'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $modRoot '00-TFP_Harmony/Mono.Cecil.dll'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $modRoot '99-AEC_T16_RuntimeFix/AEC.T16.RuntimeFix.dll'))

function Assert-Overflow([bool]$ok, [string]$message) {
    if (-not $ok) { throw $message }
}

$guard = [AECT16RuntimeFix.PassiveEffectOverflowGuard]
Assert-Overflow ($guard::ClampValue([PassiveEffects]::EntityDamage, [single]::PositiveInfinity) -eq 100000000) 'Damage infinity was not saturated.'
Assert-Overflow ($guard::ClampValue([PassiveEffects]::EntityDamage, -1) -eq 0) 'Negative damage was not clamped.'
Assert-Overflow ($guard::ClampValue([PassiveEffects]::HealthMax, 2000000000) -eq 1000000000) 'Health maximum can still overflow.'
Assert-Overflow ($guard::ClampValue([PassiveEffects]::MagazineSize, -20) -eq 1) 'Magazine minimum is invalid.'
Assert-Overflow ($guard::ClampValue([PassiveEffects]::RoundsPerMinute, 2000000) -eq 1000000) 'Rate maximum can still overflow.'
Assert-Overflow ($guard::ClampValue([PassiveEffects]::PlayerExpGain, 200000000) -eq 100000000) 'XP maximum can still overflow.'
Assert-Overflow ($guard::ClampValue([PassiveEffects]::HealthMax, 54000000) -eq 54000000) 'Existing T19 health was changed.'
Assert-Overflow ($guard::ClampValue([PassiveEffects]::DamageModifier, -2.5) -eq -2.5) 'A valid negative percent modifier was changed.'
Assert-Overflow ($guard::ClampValue([PassiveEffects]::DamageModifier, [single]::PositiveInfinity) -eq 10000) 'Damage multiplier infinity was not saturated.'
Assert-Overflow ($guard::ClampValue([PassiveEffects]::HeadshotDamageModifier, 25000) -eq 10000) 'Headshot multiplier can still overflow.'
Assert-Overflow ([single]::IsNaN($guard::ClampValue([PassiveEffects]::PhysicalDamageResist, [single]::NaN))) 'Unmanaged resistance semantics were changed.'
Assert-Overflow ($guard::ClampValue([PassiveEffects]::WeaponHandling, -0.25) -eq 0) 'Weapon handling can still become negative.'
Assert-Overflow ($guard::ClampValue([PassiveEffects]::SpreadMultiplierAiming, -0.5) -eq 0) 'Aiming spread multiplier can still become negative.'
Assert-Overflow ($guard::ClampValue([PassiveEffects]::SpreadDegreesHorizontal, -2) -eq 0) 'Spread magnitude can still become negative.'
Assert-Overflow ($guard::ClampValue([PassiveEffects]::KickDegreesHorizontalMin, [single]-0.3, [single]-0.05) -eq [single]-0.05) 'Valid leftward recoil was changed.'
Assert-Overflow ($guard::ClampValue([PassiveEffects]::KickDegreesHorizontalMin, [single]-0.3, [single]0.2) -eq [single]0) 'Reduced leftward recoil crossed zero and reversed.'
Assert-Overflow ($guard::ClampValue([PassiveEffects]::KickDegreesVerticalMin, [single]0.5, [single]-0.1) -eq [single]0) 'Reduced positive recoil crossed zero and reversed.'
Assert-Overflow ($guard::ClampValue([PassiveEffects]::KickDegreesVerticalMin, [single]-0.6, [single]0.1) -eq [single]0) 'Valid negative vertical recoil crossed zero and reversed.'
Assert-Overflow ($guard::ClampValue([PassiveEffects]::KickDegreesHorizontalMax, [single]0.8, [single]0.1) -eq [single]0.1) 'Valid positive recoil was changed.'
Assert-Overflow ($guard::ClampValue([PassiveEffects]::KickDegreesHorizontalMax, [single]0.8, [single]::PositiveInfinity) -eq [single]360) 'Infinite recoil was not saturated.'

$module = [Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $modRoot '99-AEC_T16_RuntimeFix/AEC.T16.RuntimeFix.dll'))
try {
    $type = $module.Types | Where-Object FullName -eq 'AECT16RuntimeFix.PassiveEffectOverflowGuard'
    $install = $type.Methods | Where-Object Name -eq 'Install'
    $update = $type.Methods | Where-Object Name -eq 'AfterGetValue'
    Assert-Overflow (@($install.Body.Instructions | Where-Object { $_.Operand -eq 'GetValue' }).Count -gt 0) 'EffectManager.GetValue hook is missing.'
    Assert-Overflow (@($update.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'ClampValue'
    }).Count -eq 1) 'Saturation is not applied exactly once.'
}
finally {
    $module.Dispose()
}

Write-Host 'PASS: passive-effect saturation, recoil direction, spread bounds, maxima, minima and negative modifier exclusions verified.'
