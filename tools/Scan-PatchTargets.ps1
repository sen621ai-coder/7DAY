#Requires -Version 7.0
# Resolve every AccessTools.Method/Field(typeof(X),"Name") / HarmonyRef target in
# mod sources against the installed game assemblies; report unresolvable ones.
param([string]$SourceDirs)
$root = Split-Path $PSScriptRoot
Add-Type -Path (Join-Path $root '0_TFP_Harmony/Mono.Cecil.dll') -ErrorAction SilentlyContinue
$managed = Join-Path (Split-Path $root) '7DaysToDie_Data/Managed'
$cache = @{}
function Get-Type($name) {
    if ($cache.ContainsKey($name)) { return $cache[$name] }
    $found = $null
    foreach ($asmFile in @('Assembly-CSharp.dll')) {
        $asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $managed $asmFile))
        $t = $asm.MainModule.GetType($name)
        if (!$t) {
            $stack = [System.Collections.Generic.Stack[Mono.Cecil.TypeDefinition]]::new($asm.MainModule.Types)
            while ($stack.Count -and !$t) { $x = $stack.Pop(); if ($x.Name -eq $name -or $x.FullName -eq $name) { $t = $x }; foreach ($n in $x.NestedTypes) { $stack.Push($n) } }
        }
        if ($t) { $found = $t; break }
    }
    $cache[$name] = $found
    return $found
}
$report = @()
foreach ($dir in ($SourceDirs -split ',')) {
    foreach ($file in Get-ChildItem $dir -Filter '*.cs' -Recurse) {
        $text = Get-Content $file.FullName -Raw
        $matches_ = [regex]::Matches($text, 'AccessTools\.(Method|Field|Property|Constructor)\(\s*typeof\(([^)]+)\)\s*,\s*(?:new\[\]\s*)?"([^"]+)"')
        foreach ($m in $matches_) {
            $kind, $typeName, $member = $m.Groups[1].Value, $m.Groups[2].Value.Trim(), $m.Groups[3].Value
            $t = Get-Type $typeName
            if (!$t) { $report += [pscustomobject]@{File=$file.FullName.Substring($root.Length+1); Kind=$kind; Type=$typeName; Member=$member; Problem='type missing'}; continue }
            if ($kind -eq 'Method') { if (!($t.Methods | Where-Object { $_.Name -eq $member })) { $report += [pscustomobject]@{File=$file.FullName.Substring($root.Length+1); Kind=$kind; Type=$typeName; Member=$member; Problem='method missing'} } }
            elseif ($kind -eq 'Field') { if (!($t.Fields | Where-Object { $_.Name -eq $member })) { $report += [pscustomobject]@{File=$file.FullName.Substring($root.Length+1); Kind=$kind; Type=$typeName; Member=$member; Problem='field missing'} } }
            elseif ($kind -eq 'Property') { if (!($t.Properties | Where-Object { $_.Name -eq $member })) { $report += [pscustomobject]@{File=$file.FullName.Substring($root.Length+1); Kind=$kind; Type=$typeName; Member=$member; Problem='property missing'} } }
        }
    }
}
$report | Format-Table -AutoSize | Out-String -Width 240
