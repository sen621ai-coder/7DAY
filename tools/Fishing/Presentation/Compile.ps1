#Requires -Version 7.0
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path (Split-Path $PSScriptRoot))
$managed=Join-Path (Split-Path $root) '7DaysToDie_Data/Managed'
$output=Join-Path $PSScriptRoot 'artifacts/compile/PZAEC.Fishing.Presentation.Check.dll'
New-Item -ItemType Directory -Force -Path (Split-Path $output) | Out-Null
$refs=Get-ChildItem $managed -Filter '*.dll' | ForEach-Object FullName
$sources=@(Get-ChildItem "$root/ZZZ-PZAEC_Fishing/Source/Contracts" -Filter '*.cs')+@(Get-ChildItem "$root/ZZZ-PZAEC_Fishing/Source/Presentation" -Filter '*.cs')
Add-Type -CompilerOptions '/nowarn:1701' -ReferencedAssemblies (@("$PSHOME/Microsoft.CodeAnalysis.dll","$PSHOME/Microsoft.CodeAnalysis.CSharp.dll")+@(Get-ChildItem "$PSHOME/ref" -Filter '*.dll' | ForEach-Object FullName)) -TypeDefinition @'
using System;using System.IO;using System.Linq;using Microsoft.CodeAnalysis;using Microsoft.CodeAnalysis.CSharp;
public static class FishingPresentationCompiler {
 public static void Build(string[] sources,string[] refs,string output){
 var trees=sources.Select(p=>CSharpSyntaxTree.ParseText(File.ReadAllText(p),new CSharpParseOptions(LanguageVersion.Latest),p));
 var compilation=CSharpCompilation.Create("PZAEC.Fishing.Presentation.Check",trees,refs.Select(p=>MetadataReference.CreateFromFile(p)),new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
 var result=compilation.Emit(output);foreach(var d in result.Diagnostics.Where(d=>d.Severity>=DiagnosticSeverity.Warning))Console.WriteLine(d);
 if(!result.Success)throw new Exception("Presentation compile failed");Console.WriteLine("PASS: presentation + actual v1 contracts compiled against installed game assemblies.");
 }
}
'@
[FishingPresentationCompiler]::Build($sources.FullName,$refs,$output)
