#Requires -Version 7.0
param([Parameter(Mandatory=$true)][string]$RuntimeDll)
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path (Split-Path $PSScriptRoot)
$managed = Join-Path (Split-Path $modRoot) '7DaysToDie_Data/Managed'
# Load native managed types into an isolated PowerShell process; no game world,
# Unity native calls, player data or save files are involved.
Get-ChildItem -LiteralPath $managed -Filter '*.dll' | ForEach-Object {
    try { [void][Reflection.Assembly]::LoadFrom($_.FullName) } catch { }
}
[void][Reflection.Assembly]::LoadFrom((Join-Path $modRoot '0_TFP_Harmony/0Harmony.dll'))
$runtime = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $RuntimeDll).Path)
$resolve = $runtime.GetType('YFPhoenix.PhoenixEntityLookup', $true).GetMethod('Resolve')
$script:checks = 0
function Check([bool]$Passed, [string]$Message) {
    if (-not $Passed) { throw $Message }
    $script:checks++
    Write-Output "PASS $Message"
}
function Resolve-Name([string]$Name) { $resolve.Invoke($null, @($Name)) }
function Expect-Rejection([string]$Name, [string]$Reason) {
    $failure = $null
    try { [void](Resolve-Name $Name) } catch { $failure = $_.Exception.GetBaseException() }
    Check ($null -ne $failure) "reject $Reason"
    Check ($failure -is [InvalidOperationException] -and $failure.Message.Contains($Name) -and
        $failure.Message.Contains('id=' + [EntityClass]::FromString($Name))) "diagnostic includes name and ID: $Reason"
}
$original = [EntityClass]::list
try {
    [EntityClass]::list = [Activator]::CreateInstance($original.GetType())
    $positive = $null; $negative = $null
    for ($i = 0; $i -lt 10000 -and ($null -eq $positive -or $null -eq $negative); $i++) {
        $name = 'yfPhoenixLookupRegression' + $i
        $id = [EntityClass]::FromString($name)
        if ($id -lt 0) { $negative = $name } else { $positive = $name }
    }
    Check ($null -ne $positive -and $null -ne $negative) 'native hash supplies both signed ID cases'
    $names = @($negative, $positive) + (16..19 | ForEach-Object { 'yfPhoenixBossT' + $_ })
    foreach ($name in $names) {
        $id = [EntityClass]::FromString($name)
        $definition = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EntityClass])
        $definition.entityClassName = $name
        # Call the CLR setter explicitly: PowerShell [] can reinterpret negative
        # indices on a non-IDictionary custom collection as offsets from Count.
        [EntityClass]::list.set_Item($id, $definition)
        Check ((Resolve-Name $name) -eq $id) "registered $name accepted (id=$id)"
    }
    Check ([EntityClass]::FromString($negative) -lt 0) 'negative-ID regression cannot silently test a positive ID'
    [EntityClass]::list = [Activator]::CreateInstance($original.GetType())
    Expect-Rejection $positive 'missing positive hash'
    Expect-Rejection $negative 'missing negative hash'
    $id = [EntityClass]::FromString('yfPhoenixBossT17')
    [EntityClass]::list.set_Item($id, $null)
    Expect-Rejection 'yfPhoenixBossT17' 'null registry entry'
    $wrong = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EntityClass])
    $wrong.entityClassName = 'differentRegisteredEntity'
    [EntityClass]::list.set_Item($id, $wrong)
    Expect-Rejection 'yfPhoenixBossT17' 'registry name mismatch'
    $wrong.entityClassName = 'YFPHOENIXBOSST17'
    Expect-Rejection 'yfPhoenixBossT17' 'case-mismatched registration'
} finally {
    [EntityClass]::list = $original
}
# Verify the compiled encounter calls the tested resolver before constructing or
# consuming the finale; a helper-only test would miss a disconnected fix.
Add-Type -Path (Join-Path $modRoot '0_TFP_Harmony/Mono.Cecil.dll')
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $RuntimeDll).Path)
try {
    $encounter = $assembly.MainModule.Types | Where-Object FullName -eq 'YFPhoenix.PhoenixEncounter'
    $update = $encounter.Methods | Where-Object Name -eq 'Update'
    $ops = @($update.Body.Instructions)
    $resolverCalls = @($ops | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.FullName -eq 'System.Int32 YFPhoenix.PhoenixEntityLookup::Resolve(System.String)' })
    Check ($resolverCalls.Count -eq 1) 'compiled finale uses the tested registry resolver exactly once'
    $create = $ops | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.FullName -eq 'EntityFactory' -and $_.Operand.Name -eq 'CreateEntity' } | Select-Object -First 1
    Check ($null -ne $create -and $resolverCalls[0].Offset -lt $create.Offset) 'registry validation precedes entity construction'
    $hashCalls = @($ops | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.FullName -eq 'System.Int32 EntityClass::FromString(System.String)' })
    Check ($hashCalls.Count -eq 0) 'encounter no longer performs the old hash/sign lookup'
    $consume = $ops | Where-Object { $_.Offset -gt $create.Offset -and $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'Consume' } | Select-Object -First 1
    $spawn = $ops | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'SpawnEntityInWorld' } | Select-Object -First 1
    Check ($null -ne $consume -and $null -ne $spawn -and $consume.Offset -lt $spawn.Offset) 'finale journal reservation still precedes spawn'
} finally { $assembly.Dispose() }
Write-Output "FINISHED checks=$script:checks failures=0 (offline managed registry and compiled IL; not in-game blood-moon QA)"
