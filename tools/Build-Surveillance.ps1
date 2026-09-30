#Requires -Version 7.0
param([string]$OutputPath)
$ErrorActionPreference='Stop'
# Roslyn in-place build for ZZZ-PZAEC_Surveillance (same pattern as tools/Mecha/Build.ps1):
# output targets the installed game's Mono runtime, never CoreCLR.
$root=Split-Path $PSScriptRoot
$managed=Join-Path (Split-Path $root) '7DaysToDie_Data/Managed'
$compilerRefs=@('Microsoft.CodeAnalysis.dll','Microsoft.CodeAnalysis.CSharp.dll')|ForEach-Object {Join-Path $PSHOME $_}
$frameworkRefs=Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll'|ForEach-Object FullName
Add-Type -CompilerOptions '/nowarn:1701' -ReferencedAssemblies ($compilerRefs+$frameworkRefs) -TypeDefinition @'
using System;using System.IO;using System.Linq;using Microsoft.CodeAnalysis;using Microsoft.CodeAnalysis.CSharp;
public static class SurveillanceCompiler {
  public static void Build(string[] sources,string[] references,string output){
    var c=CSharpCompilation.Create("PZAEC.Surveillance",sources.Select(p=>CSharpSyntaxTree.ParseText(File.ReadAllText(p),path:p)),references.Select(p=>MetadataReference.CreateFromFile(p)),new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithOptimizationLevel(OptimizationLevel.Release).WithDeterministic(true));
    using(var s=new MemoryStream()){var result=c.Emit(s);foreach(var d in result.Diagnostics.Where(d=>d.Severity>=DiagnosticSeverity.Warning))Console.WriteLine(d);if(!result.Success)throw new Exception("Surveillance compile failed; installed DLL not changed");var temporary=output+".building";File.WriteAllBytes(temporary,s.ToArray());if(File.Exists(output))File.Replace(temporary,output,null);else File.Move(temporary,output);}
  }
}
'@
$sources=Get-ChildItem (Join-Path $root 'ZZZ-PZAEC_Surveillance/Source') -Filter '*.cs'|Sort-Object Name|ForEach-Object FullName
$refs=@(Get-ChildItem $managed -Filter '*.dll'|ForEach-Object FullName)+(Join-Path $root '0_TFP_Harmony/0Harmony.dll')
if(!$OutputPath){$OutputPath=Join-Path $root 'ZZZ-PZAEC_Surveillance/PZAEC.Surveillance.dll'}
[SurveillanceCompiler]::Build($sources,$refs,$OutputPath)
Write-Output 'Surveillance compiled against installed V3.2 Mono/Unity references.'
