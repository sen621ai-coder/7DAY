#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GameRoot,
    [string[]]$TypeName = @('TileEntityCollector','ChunkCustomData','ChunkManager/ChunkObserver','RegionFileManager'),
    [string]$MethodPattern = '.',
    [switch]$IncludeIL,
    [string]$Output
)
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path $PSScriptRoot
$managed = Join-Path $GameRoot '7DaysToDie_Data/Managed'
Add-Type -Path (Join-Path $modRoot '0_TFP_Harmony/Mono.Cecil.dll')
$resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory($managed)
$reader = [Mono.Cecil.ReaderParameters]::new()
$reader.AssemblyResolver = $resolver
$module = [Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $managed 'Assembly-CSharp.dll'), $reader)
function Get-NativeTypes($types) {
    foreach ($type in $types) { $type; if ($type.HasNestedTypes) { Get-NativeTypes $type.NestedTypes } }
}
try {
    $allTypes = @(Get-NativeTypes $module.Types)
    $report = @(
        'Assembly: ' + $module.Assembly.FullName
        'MVID: ' + $module.Mvid
        'SHA256: ' + (Get-FileHash (Join-Path $managed 'Assembly-CSharp.dll') -Algorithm SHA256).Hash
        foreach ($name in $TypeName) {
            $found = @($allTypes | Where-Object { $_.Name -eq $name -or $_.FullName -eq $name })
            if (-not $found.Count) { 'NOT FOUND: ' + $name; continue }
            foreach ($type in $found) {
                'TYPE ' + $type.FullName + ' : ' + $type.BaseType
                foreach ($field in $type.Fields) { 'FIELD ' + $field.Attributes + ' ' + $field }
                foreach ($method in $type.Methods | Where-Object Name -match $MethodPattern) {
                    'METHOD ' + $method.Attributes + ' ' + $method
                    if ($IncludeIL -and $method.HasBody) {
                        foreach ($instruction in $method.Body.Instructions) { '  ' + $instruction }
                    }
                }
            }
        }
    )
    if ($Output) {
        $fullOutput = [IO.Path]::GetFullPath($Output)
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fullOutput)) | Out-Null
        [IO.File]::WriteAllLines($fullOutput, [string[]]$report, [Text.UTF8Encoding]::new($false))
        Write-Output "API report: $fullOutput"
    } else { $report }
} finally { $module.Dispose(); $resolver.Dispose() }
