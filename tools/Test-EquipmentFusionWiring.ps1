#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot
[void][Reflection.Assembly]::LoadFrom((Join-Path $modRoot '00-TFP_Harmony/Mono.Cecil.dll'))
$module = [Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path (Split-Path -Parent $modRoot) '7DaysToDie_Data/Managed/Assembly-CSharp.dll'))
try {
    $type = $module.Types | Where-Object Name -eq 'XUiC_CombineGrid'
    $method = $type.Methods | Where-Object Name -eq 'BtnCombine_OnPressed'
    $code = @($method.Body.Instructions)
    $add = @($code | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.FullName -eq 'System.Boolean XUiM_PlayerInventory::AddItem(ItemStack)' })
    if ($add.Count -ne 1) { throw 'Native result insertion contract changed.' }
    $branch = $add[0].Next
    if ($branch.OpCode.Code.ToString() -notmatch '^Brtrue') { throw 'Native inventory-full guard changed.' }
    $failurePath = @($code | Where-Object { $_.Offset -gt $branch.Offset -and $_.Offset -lt $branch.Operand.Offset })
    if ($failurePath[-1].OpCode.Code.ToString() -ne 'Ret') { throw 'Inventory-full path no longer returns before consumption.' }
    $clears = @($code | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.FullName -eq 'System.Void XUiC_ItemStack::set_ItemStack(ItemStack)' })
    if ($clears.Count -ne 2) { throw 'Native input consumption count changed.' }
    foreach ($clear in $clears) {
        if ($clear.Offset -le $branch.Operand.Offset -or $clear.Previous.Operand.FullName -ne 'ItemStack ItemStack::get_Empty()') { throw 'Inputs may be consumed before successful result insertion.' }
    }
    $inputs = @($clears | ForEach-Object { $_.Previous.Previous.Operand.Name })
    if (($inputs -join ',') -ne 'merge1,merge2') { throw 'Native input slots changed.' }
    $close = $type.Methods | Where-Object Name -eq 'OnClose'
    $returns = @($close.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'AddBackItem' })
    if ($returns.Count -ne 2) { throw 'Native close no longer returns both inputs.' }
    [xml]$windows = Get-Content -Raw -LiteralPath (Join-Path $modRoot '.local-tests/UserData/Saves/Navezgane/AEC_Equipment_Verification_20260905/ConfigsDump/XUi_InGame/windows.xml')
    $hint = $windows.SelectNodes("/windows/window[@name='windowCombine']/rect[@controller='CombineGrid']/label[@name='aecFusionHint']")
    if ($hint.Count -ne 1 -or $hint[0].text -ne '{aecfusionhint}') { throw 'Fusion UI patch missing or duplicated in actual merged windows.' }
    Write-Output 'PASS: native full-inventory return, exactly two post-insertion input clears, close returns both inputs, merged fusion hint.'
    Write-Output 'Scope: native IL and merged XML validation, not graphical client or multiplayer simulation.'
} finally { $module.Dispose() }
