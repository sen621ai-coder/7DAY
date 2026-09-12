#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
# Offline SDK-less fallback. PowerShell's Roslyn is only the compiler host;
# the output references the installed game's Mono assemblies, NOT CoreCLR.
$modRoot = Split-Path -Parent $PSScriptRoot
$managed = Join-Path (Split-Path -Parent $modRoot) '7DaysToDie_Data/Managed'
$compilerRefs = @('Microsoft.CodeAnalysis.dll','Microsoft.CodeAnalysis.CSharp.dll') | ForEach-Object { Join-Path $PSHOME $_ }
$frameworkRefs = Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName
Add-Type -CompilerOptions '/nowarn:1701' -ReferencedAssemblies ($compilerRefs + $frameworkRefs) -TypeDefinition @'
using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
public static class RuntimeFixCompiler
{
    public static void Build(string[] sources, string[] references, string output)
    {
        var trees = sources.Select(p => CSharpSyntaxTree.ParseText(File.ReadAllText(p), path: p));
        var refs = references.Select(p => MetadataReference.CreateFromFile(p));
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithOptimizationLevel(OptimizationLevel.Release).WithDeterministic(true);
        var compilation = CSharpCompilation.Create("AEC.T16.RuntimeFix", trees, refs, options);
        using (var stream = new MemoryStream()) {
            var result = compilation.Emit(stream);
            foreach (var diagnostic in result.Diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning)) Console.WriteLine(diagnostic.ToString());
            if (!result.Success) throw new Exception("Runtime compilation failed; installed DLL was not changed.");
            File.WriteAllBytes(output, stream.ToArray());
        }
    }
}
'@
$sources = Get-ChildItem (Join-Path $modRoot '99-AEC_T16_RuntimeFix/Source') -Filter '*.cs' | Sort-Object Name | ForEach-Object FullName
$gameRefs = @(Get-ChildItem $managed -Filter '*.dll' | ForEach-Object FullName) + (Join-Path $modRoot '0_TFP_Harmony/0Harmony.dll')
[RuntimeFixCompiler]::Build($sources, $gameRefs, (Join-Path $modRoot '99-AEC_T16_RuntimeFix/AEC.T16.RuntimeFix.dll'))
Write-Output 'Runtime build succeeded (offline Roslyn; game/Mono references).'
