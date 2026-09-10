#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot
$managed = Join-Path (Split-Path -Parent $modRoot) '7DaysToDie_Data/Managed'
Get-ChildItem $managed -Filter '*.dll' | ForEach-Object {
    try { [void][Reflection.Assembly]::LoadFrom($_.FullName) } catch {}
}
[void][Reflection.Assembly]::LoadFrom((Join-Path $modRoot '00-TFP_Harmony/0Harmony.dll'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $modRoot '99-AEC_T16_RuntimeFix/AEC.T16.RuntimeFix.dll'))

function Assert-Near([double]$actual, [double]$expected, [string]$message) {
    if ([double]::IsNaN($actual) -or [Math]::Abs($actual - $expected) -gt 0.00001) {
        throw "$message : expected $expected, got $actual"
    }
}

# The bundled Harmony installer targets Unity/Mono, not this CoreCLR version.
# Reuse the metadata reader and execute a DynamicMethod containing the real
# installed game's IL transformed by our actual transpiler.
. (Join-Path $PSScriptRoot 'Test-ModelTintFix.ps1')
Add-Type -ReferencedAssemblies ($references + $frameworkReferences) -TypeDefinition @'
using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using AECT16RuntimeFix;
public static class SpreadRegressionEmitter
{
    public static DynamicMethod Build(MethodInfo target, object rawReader)
    {
        var reader = (MethodInfo)rawReader;
        var signature = new[] { typeof(PassiveEffect) }.Concat(target.GetParameters().Select(p => p.ParameterType)).ToArray();
        var method = new DynamicMethod("PatchedSpread", typeof(void), signature, typeof(PassiveEffect), true);
        var il = method.GetILGenerator();
        var source = (System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction>)reader.Invoke(null, new object[] {target, il});
        foreach (var code in SpreadPercentStacking.Transpiler(source))
        {
            foreach (var label in code.labels) il.MarkLabel(label);
            var op = code.opcode;
            var arg = code.operand;
            if (arg == null) il.Emit(op);
            else if (arg is Label) il.Emit(op, (Label)arg);
            else if (arg is Label[]) il.Emit(op, (Label[])arg);
            else if (arg is LocalBuilder) il.Emit(op, (LocalBuilder)arg);
            else if (arg is FieldInfo) il.Emit(op, (FieldInfo)arg);
            else if (arg is MethodInfo) il.Emit(op, (MethodInfo)arg);
            else if (arg is ConstructorInfo) il.Emit(op, (ConstructorInfo)arg);
            else if (arg is Type) il.Emit(op, (Type)arg);
            else if (arg is float) il.Emit(op, (float)arg);
            else if (arg is string) il.Emit(op, (string)arg);
            else if (op.OperandType == OperandType.ShortInlineVar) il.Emit(op, Convert.ToByte(arg));
            else if (op.OperandType == OperandType.InlineVar) il.Emit(op, Convert.ToInt16(arg));
            else if (op.OperandType == OperandType.ShortInlineI) il.Emit(op, Convert.ToSByte(arg));
            else if (arg is int) il.Emit(op, (int)arg);
            else throw new Exception("Unsupported operand: " + arg);
        }
        return method;
    }
}
'@
$original = [PassiveEffect].GetMethod('ModifyValue')
$tags = [Activator]::CreateInstance($original.GetParameters()[4].ParameterType)
$native = [SpreadRegressionEmitter]::Build($original, [ModelTintRegression].GetMethod('ReadGameIL'))

function Invoke-Passive([string]$kind, [string]$operation, [single[]]$values,
    [single]$percent = 1, [single]$base = 1, [single[]]$levels = $null, [single]$rank = 1) {
    $effect = [PassiveEffect]::new()
    $effect.Type = [Enum]::Parse([PassiveEffects], $kind)
    $effect.Modifier = [Enum]::Parse([PassiveEffect+ValueModifierTypes], $operation)
    $effect.Values = $values
    $effect.Levels = $levels
    $argsForCall = [object[]]@($effect, $null, $rank, $base, $percent, $tags, [int]1)
    [void]$native.Invoke($null, $argsForCall)
    return @{ Base = [single]$argsForCall[3]; Percent = [single]$argsForCall[4] }
}

& {
    $hip = [single]1
    foreach ($reduction in @(-.25, -.5, -.2)) {
        $hip = (Invoke-Passive 'SpreadMultiplierHip' 'perc_add' @($reduction) $hip).Percent
    }
    Assert-Near $hip .3 'M60 max talents/books'
    $laser = (Invoke-Passive 'SpreadMultiplierHip' 'perc_add' @(-.2) $hip).Percent
    Assert-Near $laser .24 'M60 laser must improve hip spread without resetting'
    $reverse = [single]1
    foreach ($reduction in @(-.2, -.2, -.5, -.25)) {
        $reverse = (Invoke-Passive 'SpreadMultiplierHip' 'perc_add' @($reduction) $reverse).Percent
    }
    Assert-Near $reverse $laser 'Effect order'
    Assert-Near (Invoke-Passive 'SpreadMultiplierHip' 'perc_add' @(.2) $laser).Percent .288 'Spread penalty'
    Assert-Near (Invoke-Passive 'SpreadMultiplierAiming' 'perc_add' @(-.88) .82).Percent .0984 'T19 rangefinder and 4x scope'

    $steady = (Invoke-Passive 'IncrementalSpreadMultiplier' 'perc_add' @(-.03,-.15) 1 1 @(1,5) 5).Percent
    $steady = (Invoke-Passive 'IncrementalSpreadMultiplier' 'perc_add' @(-.03,-.3) $steady 1 @(1,100) 100).Percent
    # Values supplied by the book CVar range from 0 to -.5; verify every count.
    $last = $steady
    foreach ($shots in 0..60) {
        $book = -[Math]::Min(.5, .015 * $shots)
        $actual = (Invoke-Passive 'IncrementalSpreadMultiplier' 'perc_add' @($book) $steady).Percent
        Assert-Near $actual (.85 * .7 * (1 + $book)) "Book shot count $shots"
        if ($actual -gt $last + .00001) { throw 'Continuous fire reduction rebounded.' }
        $last = $actual
    }
    Assert-Near (1.5 * $last) .44625 'M60 final incremental spread parameter'
    Assert-Near (Invoke-Passive 'SpreadMultiplierHip' 'perc_add' @(-.02,-.5) .8 1 @(1,100) 50.5).Percent .592 'Intermediate rank interpolation'
    Assert-Near (Invoke-Passive 'SpreadMultiplierHip' 'perc_add' @(-.2) .8 1 @(2) 1).Percent .8 'Inactive rank'

    foreach ($kind in @('SpreadMultiplierHip','SpreadMultiplierAiming','SpreadMultiplierRunning',
        'SpreadMultiplierWalking','SpreadMultiplierCrouching','SpreadMultiplierIdle',
        'SpreadDegreesVertical','SpreadDegreesHorizontal','IncrementalSpreadMultiplier')) {
        Assert-Near (Invoke-Passive $kind 'perc_add' @(-1) .4).Percent 0 "$kind -100%"
        Assert-Near (Invoke-Passive $kind 'perc_add' @(-1.2) .4).Percent 0 "$kind beyond -100%"
        Assert-Near (Invoke-Passive $kind 'perc_subtract' @(.25) .4).Percent .3 "$kind subtraction"
        Assert-Near (Invoke-Passive $kind 'perc_add' @(.2) 0).Percent 0 "$kind zero remains zero"
        Assert-Near (Invoke-Passive $kind 'base_set' @(2.8) .4).Base 2.8 "$kind base set"
        Assert-Near (Invoke-Passive $kind 'perc_set' @(.6) .4).Percent .6 "$kind explicit percentage set"
    }
    foreach ($kind in @('EntityDamage','RoundsPerMinute','WeaponHandling','KickDegreesVerticalMin','KickDegreesVerticalMax')) {
        Assert-Near (Invoke-Passive $kind 'perc_add' @(-.25) .4).Percent .15 "$kind must retain native additive behavior"
    }
    $rejected = $false
    try {
        [void][AECT16RuntimeFix.SpreadPercentStacking]::Transpiler([HarmonyLib.CodeInstruction[]]@(
            [HarmonyLib.CodeInstruction]::new([Reflection.Emit.OpCodes]::Ret, $null)))
    } catch { $rejected = $true }
    if (-not $rejected) { throw 'Unknown game IL was silently accepted.' }
    $module = [Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $modRoot '99-AEC_T16_RuntimeFix/AEC.T16.RuntimeFix.dll'))
    try {
        $init = ($module.Types | Where-Object Name -eq 'T16RuntimeFixMod').Methods | Where-Object Name -eq 'InitMod'
        $hooks = @($init.Body.Instructions | Where-Object {
            $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'SpreadPercentStacking' -and $_.Operand.Name -eq 'Install'
        })
        if ($hooks.Count -ne 1) { throw 'Spread patch must be installed exactly once at startup.' }
    } finally { $module.Dispose() }
    Write-Host 'PASS: actual patched game method; M60 hip/aim/burst stacking, ranks, order, penalties, zero bounds and unrelated stats.'
}
