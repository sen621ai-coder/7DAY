#Requires -Version 7.0
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
$managed=Join-Path (Split-Path $root) '7DaysToDie_Data/Managed'
$compilerRefs=@('Microsoft.CodeAnalysis.dll','Microsoft.CodeAnalysis.CSharp.dll')|ForEach-Object {Join-Path $PSHOME $_}
$frameworkRefs=Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll'|ForEach-Object FullName
Add-Type -CompilerOptions '/nowarn:1701' -ReferencedAssemblies ($compilerRefs+$frameworkRefs) -TypeDefinition @'
using System;using System.IO;using System.Linq;using Microsoft.CodeAnalysis;using Microsoft.CodeAnalysis.CSharp;
public static class SurveillanceQACompiler {
 public static void Build(string source,string[] refs,string output){
  var c=CSharpCompilation.Create("PZAEC.Surveillance.VisualQA",new[]{CSharpSyntaxTree.ParseText(File.ReadAllText(source))},refs.Select(p=>MetadataReference.CreateFromFile(p)),new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
  using(var stream=new MemoryStream()){var result=c.Emit(stream);if(!result.Success)throw new Exception(string.Join("\n",result.Diagnostics));File.WriteAllBytes(output,stream.ToArray());}
 }
}
'@
New-Item -ItemType Directory -Force $OutputDirectory|Out-Null
$refs=@(Get-ChildItem $managed -Filter '*.dll'|ForEach-Object FullName)+(Join-Path $root '0_TFP_Harmony/0Harmony.dll')+(Join-Path $root 'ZZZ-PZAEC_Surveillance/PZAEC.Surveillance.dll')
[SurveillanceQACompiler]::Build((Join-Path $PSScriptRoot 'VisualGameQA.cs'),$refs,(Join-Path $OutputDirectory 'PZAEC.Surveillance.VisualQA.dll'))
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'ModInfo.xml'),'<xml><Name value="Surveillance_VisualQA"/><DisplayName value="Surveillance Visual QA"/><Description value="Isolated rendering regression"/><Author value="PZAEC"/><Version value="1.0.0"/></xml>')
