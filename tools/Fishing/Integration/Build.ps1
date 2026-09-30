#Requires -Version 7.0
[CmdletBinding()]
param([string]$GameRoot,[string]$OutputDirectory,[string[]]$ExtraSource=@(),[string]$AssemblyName='PZAEC.Fishing',[switch]$Executable,[switch]$CoreOnly,[switch]$ProbeOnly)
$ErrorActionPreference='Stop'
$modsRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if(!$GameRoot){$GameRoot=Split-Path $modsRoot}
if(!$OutputDirectory){$OutputDirectory=Join-Path $PSScriptRoot 'artifacts/build'}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
$liveMod=[IO.Path]::GetFullPath((Join-Path $modsRoot 'ZZZ-PZAEC_Fishing'))+[IO.Path]::DirectorySeparatorChar
if(($OutputDirectory+[IO.Path]::DirectorySeparatorChar).StartsWith($liveMod,[StringComparison]::OrdinalIgnoreCase)){throw 'M0 builds must remain staged outside the live mod directory.'}
$managed=Join-Path $GameRoot '7DaysToDie_Data/Managed'
$compilerRefs=@('Microsoft.CodeAnalysis.dll','Microsoft.CodeAnalysis.CSharp.dll')|ForEach-Object {Join-Path $PSHOME $_}
$frameworkRefs=Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll'|ForEach-Object FullName
if(-not('FishingStageCompiler' -as [type])){
Add-Type -CompilerOptions '/nowarn:1701' -ReferencedAssemblies ($compilerRefs+$frameworkRefs) -TypeDefinition @'
using System;using System.IO;using System.Linq;using Microsoft.CodeAnalysis;using Microsoft.CodeAnalysis.CSharp;
public static class FishingStageCompiler {
 public static void Build(string[] sources,string[] references,string output,string name,bool exe) {
  var trees=sources.Select(p=>CSharpSyntaxTree.ParseText(File.ReadAllText(p),new CSharpParseOptions(LanguageVersion.Latest),p));
  var options=new CSharpCompilationOptions(exe?OutputKind.ConsoleApplication:OutputKind.DynamicallyLinkedLibrary).WithOptimizationLevel(OptimizationLevel.Release).WithDeterministic(true);
  var result=CSharpCompilation.Create(name,trees,references.Distinct(StringComparer.OrdinalIgnoreCase).Select(p=>MetadataReference.CreateFromFile(p)),options);
  using(var memory=new MemoryStream()){var emitted=result.Emit(memory);foreach(var d in emitted.Diagnostics.Where(d=>d.Severity>=DiagnosticSeverity.Warning))Console.WriteLine(d);if(!emitted.Success)throw new Exception("Fishing staged build failed");File.WriteAllBytes(output,memory.ToArray());}
 }
}
'@
}
$sourceRoot=Join-Path $modsRoot 'ZZZ-PZAEC_Fishing/Source'
$sources=@(Get-ChildItem $sourceRoot -Recurse -Filter '*.cs' | Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' } | Sort-Object FullName | ForEach-Object FullName)
if($CoreOnly){$sources=@($sources|Where-Object {$_ -match '[\\/]Contracts[\\/]' -or [IO.Path]::GetFileName($_) -in @('SessionDriver.cs','MovementBridge.cs')})}
if($ProbeOnly){$sources=@($sources|Where-Object {
    ($_ -match '[\\/](Contracts|Runtime|Content|Presentation)[\\/]') -and
    [IO.Path]::GetFileName($_) -notin @('ModApi.cs','LocalFishingRuntime.cs')
})}
foreach($extra in $ExtraSource){$sources+=[IO.Path]::GetFullPath($extra)}
$refs=@(Get-ChildItem $managed -Filter '*.dll'|ForEach-Object FullName)+(Join-Path $modsRoot '0_TFP_Harmony/0Harmony.dll')
New-Item -ItemType Directory -Force $OutputDirectory|Out-Null
$extension=if($Executable){'.exe'}else{'.dll'}
$output=Join-Path $OutputDirectory ($AssemblyName+$extension)
[FishingStageCompiler]::Build($sources,$refs,$output,$AssemblyName,[bool]$Executable)
[pscustomobject]@{Output=$output;SourceCount=$sources.Count;SHA256=(Get-FileHash $output).Hash;CoreOnly=[bool]$CoreOnly}|ConvertTo-Json
