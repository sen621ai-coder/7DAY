#Requires -Version 7.0
param([string]$OutputPath,[string]$SourceRoot,[string[]]$ExtraSources=@())
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$managed=Join-Path (Split-Path $root) '7DaysToDie_Data/Managed'
$compilerRefs=@('Microsoft.CodeAnalysis.dll','Microsoft.CodeAnalysis.CSharp.dll')|ForEach-Object {Join-Path $PSHOME $_}
$frameworkRefs=Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll'|ForEach-Object FullName
Add-Type -CompilerOptions '/nowarn:1701' -ReferencedAssemblies ($compilerRefs+$frameworkRefs) -TypeDefinition @'
using System;using System.IO;using System.Linq;using Microsoft.CodeAnalysis;using Microsoft.CodeAnalysis.CSharp;
public static class JuqueCompiler {
 public static void Build(string[] paths,string[] refs,string output){
 var r=refs.Select(x=>MetadataReference.CreateFromFile(x)).ToArray();
 var c=CSharpCompilation.Create("PZAEC.FlyingSword",paths.Select(p=>CSharpSyntaxTree.ParseText(File.ReadAllText(p),path:p)),r,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithOptimizationLevel(OptimizationLevel.Release).WithDeterministic(true));
 using(var s=new MemoryStream()){var result=c.Emit(s);foreach(var d in result.Diagnostics.Where(d=>d.Severity>=DiagnosticSeverity.Warning))Console.WriteLine(d);if(!result.Success)throw new Exception("Juque compilation failed; existing DLL preserved");File.WriteAllBytes(output+".building",s.ToArray());if(File.Exists(output))File.Replace(output+".building",output,null);else File.Move(output+".building",output);}
 }
}
'@
if(!$SourceRoot){$SourceRoot=Join-Path $root 'ZZ-PZAEC_FlyingSword/Source'}
if(!$OutputPath){$OutputPath=Join-Path $root 'ZZ-PZAEC_FlyingSword/PZAEC.FlyingSword.dll'}
$sources=@(Get-ChildItem $SourceRoot -Filter '*.cs'|Sort-Object Name|ForEach-Object FullName)+$ExtraSources
$refs=@(Get-ChildItem $managed -Filter '*.dll'|ForEach-Object FullName)+(Join-Path $root '0_TFP_Harmony/0Harmony.dll')+(Join-Path $root '99-AEC_T16_RuntimeFix/AEC.T16.RuntimeFix.dll')
[JuqueCompiler]::Build($sources,$refs,$OutputPath)
