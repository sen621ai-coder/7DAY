#Requires -Version 7.0
param([string]$Output,[string]$RuntimeDll)
$ErrorActionPreference = 'Stop'
# Offline SDK-less fallback. PowerShell's Roslyn is only the compiler host;
# the output references the installed game's Mono assemblies, NOT CoreCLR.
$modRoot = Split-Path -Parent $PSScriptRoot
if(-not $RuntimeDll){$RuntimeDll=Join-Path $modRoot '96-SakuraPreview/Sakura.Preview.dll'}
$managed = Join-Path (Split-Path -Parent $modRoot) '7DaysToDie_Data/Managed'
$compilerRefs = @('Microsoft.CodeAnalysis.dll','Microsoft.CodeAnalysis.CSharp.dll') | ForEach-Object { Join-Path $PSHOME $_ }
$frameworkRefs = Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName
Add-Type -CompilerOptions '/nowarn:1701' -ReferencedAssemblies ($compilerRefs + $frameworkRefs) -TypeDefinition @'
using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
public static class SakuraNativeSmokeCompiler
{
    public static void Build(string[] sources, string[] references, string output)
    {
        var trees = sources.Select(p => CSharpSyntaxTree.ParseText(File.ReadAllText(p), path: p));
        var refs = references.Select(p => MetadataReference.CreateFromFile(p));
        var options = new CSharpCompilationOptions(OutputKind.ConsoleApplication)
            .WithOptimizationLevel(OptimizationLevel.Release).WithDeterministic(true);
        var compilation = CSharpCompilation.Create("NativeConfigSmoke", trees, refs, options);
        using (var stream = new MemoryStream()) {
            var result = compilation.Emit(stream);
            foreach (var diagnostic in result.Diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning)) Console.WriteLine(diagnostic.ToString());
            if (!result.Success) throw new Exception("Runtime compilation failed; installed DLL was not changed.");
            File.WriteAllBytes(output, stream.ToArray());
        }
    }
}
'@
$sources = @((Join-Path $PSScriptRoot 'EscortNPC/NativeConfigSmoke.cs'))
$gameRefs = @(Get-ChildItem $managed -Filter '*.dll' | ForEach-Object FullName) + (Join-Path $modRoot '0_TFP_Harmony/0Harmony.dll')
if(-not $Output){$Output='E:/soft/7DTD-Modding/SakuraNative-Staging/NativeConfigSmoke.exe'}
$gameRefs += $RuntimeDll
[SakuraNativeSmokeCompiler]::Build($sources, $gameRefs, $Output)
Write-Output 'Runtime build succeeded (offline Roslyn; game/Mono references).'




