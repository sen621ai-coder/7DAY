#Requires -Version 7.0
param([string]$OutputPath)
$ErrorActionPreference = 'Stop'

# SDK-less build for this game installation. PowerShell supplies the Roslyn
# compiler host while the compiled mod targets the game's own Mono assemblies.
$modRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$sourceRoot = Join-Path $modRoot '99-AEC_T16_RuntimeFix\Source'
$managed = Join-Path (Split-Path -Parent $modRoot) '7DaysToDie_Data\Managed'
$compilerRefs = @('Microsoft.CodeAnalysis.dll','Microsoft.CodeAnalysis.CSharp.dll') |
    ForEach-Object { Join-Path $PSHOME $_ }
$frameworkRefs = Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll' |
    ForEach-Object FullName

Add-Type -CompilerOptions '/nowarn:1701' -ReferencedAssemblies ($compilerRefs + $frameworkRefs) -TypeDefinition @'
using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
public static class AecRuntimeCompiler
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
            foreach (var diagnostic in result.Diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning))
                Console.WriteLine(diagnostic.ToString());
            if (!result.Success) throw new Exception("Runtime compilation failed; installed DLL was not changed.");
            File.WriteAllBytes(output, stream.ToArray());
        }
    }
}
'@

$sources = @(Get-ChildItem $sourceRoot -Recurse -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' } |
    ForEach-Object FullName)
$gameRefs = @(Get-ChildItem $managed -Filter '*.dll' | ForEach-Object FullName) +
    (Join-Path $modRoot '0_TFP_Harmony\0Harmony.dll')
if (!$OutputPath) { $OutputPath = Join-Path $modRoot '99-AEC_T16_RuntimeFix\AEC.T16.RuntimeFix.dll' }
$temporary = "$OutputPath.new"
[AecRuntimeCompiler]::Build($sources, $gameRefs, $temporary)
Move-Item -LiteralPath $temporary -Destination $OutputPath -Force
Write-Host "Built $OutputPath from $($sources.Count) source files."
