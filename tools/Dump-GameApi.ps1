#Requires -Version 7.0
# Dump type/member signatures from game assemblies via Mono.Cecil (no load).
param(
    [Parameter(Mandatory=$true)][string]$TypeName,
    [string]$Assembly = 'Assembly-CSharp.dll',
    [switch]$Methods,
    [switch]$Fields,
    [switch]$Properties
)
$root = Split-Path $PSScriptRoot
$cecil = Join-Path $root '0_TFP_Harmony/Mono.Cecil.dll'
Add-Type -Path $cecil
$game = Split-Path $root
$path = Join-Path $game "7DaysToDie_Data/Managed/$Assembly"
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
$types = $asm.MainModule.Types
$flat = [System.Collections.Generic.List[Mono.Cecil.TypeDefinition]]::new()
$stack = [System.Collections.Generic.Stack[Mono.Cecil.TypeDefinition]]::new($types)
while ($stack.Count) { $t = $stack.Pop(); $flat.Add($t); foreach ($n in $t.NestedTypes) { $stack.Push($n) } }
$found = $flat | Where-Object { $_.FullName -eq $TypeName -or $_.Name -eq $TypeName }
if (!$found) { Write-Output "type not found: $TypeName"; exit 1 }
foreach ($t in $found) {
    Write-Output ("TYPE " + $t.FullName + " : " + $t.BaseType)
    if ($Methods -or (!$Fields -and !$Properties)) {
        foreach ($m in $t.Methods) {
            if ($m.IsConstructor) { continue }
            $pars = ($m.Parameters | ForEach-Object { $_.ParameterType.Name + ' ' + $_.Name }) -join ', '
            $vis = if ($m.IsPublic) { 'public' } elseif ($m.IsFamily) { 'protected' } else { 'private' }
            Write-Output ("  " + $vis + " " + $m.ReturnType.Name + " " + $m.Name + "(" + $pars + ")" + $(if ($m.IsVirtual) { ' virtual' } else { '' }))
        }
    }
    if ($Fields) { foreach ($f in $t.Fields) { Write-Output ("  field " + $f.FieldType.Name + " " + $f.Name) } }
    if ($Properties) { foreach ($p in $t.Properties) { Write-Output ("  prop " + $p.PropertyType.Name + " " + $p.Name) } }
}
