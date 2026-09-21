param([string]$AssemblyPath)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$managed=Join-Path (Split-Path $root) '7DaysToDie_Data/Managed'
if(!$AssemblyPath){$AssemblyPath=Join-Path $root '99-AEC_T16_RuntimeFix/AEC.T16.RuntimeFix.dll'}
$AssemblyPath=(Resolve-Path $AssemblyPath).Path
Get-ChildItem $managed -Filter '*.dll' | ForEach-Object {try{[void][Reflection.Assembly]::LoadFrom($_.FullName)}catch{}}
$refs=@((Join-Path $root '0_TFP_Harmony/0Harmony.dll'),(Join-Path $root '0_TFP_Harmony/Mono.Cecil.dll'),$AssemblyPath,
    (Join-Path $managed 'Assembly-CSharp.dll'),(Join-Path $managed 'UnityEngine.CoreModule.dll'))
foreach($p in $refs){[void][Reflection.Assembly]::LoadFrom($p)}
$framework=@(Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName)
Add-Type -ReferencedAssemblies ($refs+$framework) -Path (Join-Path $PSScriptRoot 'HighDamageNetworking/NativeTests.cs')
[HighDamageNativeTests]::Run()
