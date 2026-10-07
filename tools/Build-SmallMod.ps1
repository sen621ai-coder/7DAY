#Requires -Version 7.0
param([string]$ModDir,[string]$AssemblyName,[string]$Output)
$ErrorActionPreference = 'Stop'
# Offline SDK-less Roslyn build for small C# mods against the installed game.
$modRoot = Split-Path -Parent $PSScriptRoot
$gameRoot = Split-Path -Parent $modRoot
$managed = Join-Path $gameRoot '7DaysToDie_Data/Managed'
if(-not(Test-Path -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll'))){throw 'Installed game assemblies not found.'}
if(-not $ModDir -or -not $AssemblyName){throw 'Usage: Build-SmallMod.ps1 -ModDir <folder> -AssemblyName <dll name>'}
$compilerRefs = @('Microsoft.CodeAnalysis.dll','Microsoft.CodeAnalysis.CSharp.dll') | ForEach-Object { Join-Path $PSHOME $_ }
$frameworkRefs = Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName
Add-Type -CompilerOptions '/nowarn:1701' -ReferencedAssemblies ($compilerRefs + $frameworkRefs) -TypeDefinition @'
using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
public static class SmallModCompiler
{
    public static void Build(string[] sources, string[] references, string output, string assemblyName)
    {
        var trees = sources.Select(p => CSharpSyntaxTree.ParseText(File.ReadAllText(p), path: p));
        var refs = references.Select(p => MetadataReference.CreateFromFile(p));
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithOptimizationLevel(OptimizationLevel.Release).WithDeterministic(true);
        var compilation = CSharpCompilation.Create(assemblyName, trees, refs, options);
        using (var stream = new MemoryStream()) {
            var result = compilation.Emit(stream);
            foreach (var diagnostic in result.Diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning)) Console.WriteLine(diagnostic.ToString());
            if (!result.Success) throw new Exception("Runtime compilation failed; installed DLL was not changed.");
            File.WriteAllBytes(output, stream.ToArray());
        }
    }
}
'@
$sources = Get-ChildItem (Join-Path $modRoot $ModDir | Join-Path -ChildPath 'Source') -Filter '*.cs' -Recurse | Sort-Object FullName | ForEach-Object FullName
$gameRefs = @(Get-ChildItem $managed -Filter '*.dll' | ForEach-Object FullName) + (Join-Path $modRoot '0_TFP_Harmony/0Harmony.dll')
if(-not $Output){$Output=Join-Path (Join-Path $modRoot $ModDir) $AssemblyName}
[SmallModCompiler]::Build($sources, $gameRefs, $Output, [IO.Path]::GetFileNameWithoutExtension($AssemblyName))
Write-Output "Runtime build succeeded: $Output"
