$ErrorActionPreference = 'Stop'
# Run with PowerShell 7. The game uses Unity/Mono, not this test host.
# Unity native functions are never called by these offline tests.
$modRoot = Split-Path -Parent $PSScriptRoot
$managed = Join-Path (Split-Path -Parent $modRoot) '7DaysToDie_Data/Managed'
Get-ChildItem -LiteralPath $managed -Filter '*.dll' | ForEach-Object {
    try { [void][Reflection.Assembly]::LoadFrom($_.FullName) } catch { }
}
$references = @(
    (Join-Path $modRoot '00-TFP_Harmony/0Harmony.dll'),
    (Join-Path $modRoot '00-TFP_Harmony/Mono.Cecil.dll'),
    (Join-Path $modRoot '99-AEC_T16_RuntimeFix/AEC.T16.RuntimeFix.dll'),
    (Join-Path $managed 'Assembly-CSharp.dll'),
    (Join-Path $managed 'UnityEngine.CoreModule.dll')
)
foreach ($path in $references) { [void][Reflection.Assembly]::LoadFrom($path) }

$frameworkReferences = @(Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName)
Add-Type -ReferencedAssemblies ($references + $frameworkReferences) -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using AECT16RuntimeFix;

public static class ModelTintRegression
{
    public static List<object> Renderers = new List<object>();
    public static int Clones, Tints, Assignments;
    public static bool CanTint(object material, object entity) { return material != null; }
    public static object Clone(object material)
    {
        if (material == null) throw new Exception("Null material reached clone!");
        Clones++;
        return material;
    }
    public static void Tint(object material) { Tints++; }
    public static void Assign(object material) { Assignments++; }
    static MethodInfo Stub(string name) { return typeof(ModelTintRegression).GetMethod(name); }
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static List<CodeInstruction> Patch(IEnumerable<CodeInstruction> code, ILGenerator il)
    {
        return T16RuntimeFixMod.ModelTintSafetyTranspiler(code, il).ToList();
    }

    public static List<CodeInstruction> ReadGameIL(MethodInfo target, ILGenerator il)
    {
        // Read metadata directly: Harmony's native patch installer only supports
        // the game's Mono runtime, not every newer CoreCLR used by PowerShell.
        var locals = target.GetMethodBody().LocalVariables.Select(v => il.DeclareLocal(v.LocalType, v.IsPinned)).ToArray();
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)).ToDictionary(o => o.Value);
        using (var module = Mono.Cecil.ModuleDefinition.ReadModule(target.Module.FullyQualifiedName))
        {
            var definition = (Mono.Cecil.MethodDefinition)module.LookupToken(target.MetadataToken);
            Check(!definition.Body.HasExceptionHandlers, "Game model method gained exception regions; update test reader");
            var labels = definition.Body.Instructions.ToDictionary(i => i, i => il.DefineLabel());
            var result = new List<CodeInstruction>();
            foreach (var instruction in definition.Body.Instructions)
            {
                object operand = instruction.Operand;
                var branch = operand as Mono.Cecil.Cil.Instruction;
                var branches = operand as Mono.Cecil.Cil.Instruction[];
                var variable = operand as Mono.Cecil.Cil.VariableDefinition;
                var parameter = operand as Mono.Cecil.ParameterDefinition;
                var member = operand as Mono.Cecil.MemberReference;
                if (branch != null) operand = labels[branch];
                else if (branches != null) operand = branches.Select(b => labels[b]).ToArray();
                else if (variable != null) operand = locals[variable.Index];
                else if (parameter != null) operand = parameter.Index + (target.IsStatic ? 0 : 1);
                else if (member != null) operand = target.Module.ResolveMember(member.MetadataToken.ToInt32());
                var code = new CodeInstruction(opcodes[instruction.OpCode.Value], operand);
                code.labels.Add(labels[instruction]);
                result.Add(code);
            }
            return result;
        }
    }

    public static string Run()
    {
        var target = AccessTools.Method(typeof(EModelBase), "createModel", new[] { typeof(World), typeof(EntityClass) });
        var actualIL = new DynamicMethod("OriginalModelIL", typeof(void), Type.EmptyTypes).GetILGenerator();
        var original = ReadGameIL(target, actualIL);
        var clone = original.Select(c => c.operand as MethodInfo).First(m =>
            m != null && m.DeclaringType == typeof(UnityEngine.Object) && m.Name == "Instantiate" &&
            m.IsGenericMethod && m.GetGenericArguments()[0] == typeof(Material) && m.GetParameters().Length == 1);
        var tint = AccessTools.Method(typeof(Material), "SetColor", new[] { typeof(string), typeof(Color) });
        var rendererField = AccessTools.Field(typeof(EModelBase), "skinnedRendererList");
        var clear = rendererField.FieldType.GetMethod("Clear");
        int count = original.Count;
        int returns = original.Count(c => c.opcode == OpCodes.Ret);
        var patched = Patch(original, actualIL);
        Check(patched.Count == count + 4, "Unexpected original IL changes");
        Check(patched.Count(c => c.opcode == OpCodes.Ret) == returns, "Entity initialization was truncated");
        int guard = patched.FindIndex(c => c.operand is MethodInfo && ((MethodInfo)c.operand).Name == "CanApplyModelTint");
        Check(guard >= 2 && patched[guard - 1].opcode == OpCodes.Ldarg_2, "Missing guard or entity argument");
        Check(patched[guard + 1].opcode == OpCodes.Brfalse, "Missing null-material branch");
        var cleanupLabel = (Label)patched[guard + 1].operand;
        int cleanup = patched.FindIndex(c => c.labels.Contains(cleanupLabel));
        Check(cleanup > guard && Equals(patched[cleanup].operand, rendererField) &&
            Equals(patched[cleanup + 1].operand, clear), "Guard does not reach renderer cleanup");
        Check(patched[guard - 2].opcode == patched[guard + 2].opcode &&
            Equals(patched[guard - 2].operand, patched[guard + 2].operand) &&
            Equals(patched[guard + 3].operand, clone), "Valid-material clone path changed");

        // Execute the inserted control flow with managed stand-ins for Unity calls.
        // The real-game instruction matching above is tested separately.
        var method = new DynamicMethod("TintControlFlow", typeof(int),
            new[] { typeof(object), typeof(object), typeof(object), typeof(object) }, typeof(ModelTintRegression), true);
        var il = method.GetILGenerator();
        il.DeclareLocal(typeof(object));
        var entry = il.DefineLabel();
        var load = new CodeInstruction(OpCodes.Ldloc_0);
        load.labels.Add(entry);
        var sample = new List<CodeInstruction> {
            new CodeInstruction(OpCodes.Ldarg_3), new CodeInstruction(OpCodes.Stloc_0),
            new CodeInstruction(OpCodes.Br, entry), load,
            new CodeInstruction(OpCodes.Call, clone), new CodeInstruction(OpCodes.Stloc_0),
            new CodeInstruction(OpCodes.Ldloc_0), new CodeInstruction(OpCodes.Callvirt, tint),
            new CodeInstruction(OpCodes.Ldloc_0), new CodeInstruction(OpCodes.Call, Stub("Assign")),
            new CodeInstruction(OpCodes.Ldsfld, rendererField), new CodeInstruction(OpCodes.Callvirt, clear),
            new CodeInstruction(OpCodes.Ldc_I4_1), new CodeInstruction(OpCodes.Ret)
        };
        var body = Patch(sample, il);
        Check(body[3].labels.Contains(entry) && !load.labels.Contains(entry), "Entry label bypasses guard");
        foreach (var c in body)
        {
            foreach (var label in c.labels) il.MarkLabel(label);
            var m = c.operand as MethodInfo;
            if (m != null && m.Name == "CanApplyModelTint") il.Emit(OpCodes.Call, Stub("CanTint"));
            else if (Equals(c.operand, clone)) il.Emit(OpCodes.Call, Stub("Clone"));
            else if (Equals(c.operand, tint)) il.Emit(OpCodes.Call, Stub("Tint"));
            else if (Equals(c.operand, rendererField)) il.Emit(OpCodes.Ldsfld, typeof(ModelTintRegression).GetField("Renderers"));
            else if (Equals(c.operand, clear)) il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("Clear"));
            else if (c.operand == null) il.Emit(c.opcode);
            else if (c.operand is Label) il.Emit(c.opcode, (Label)c.operand);
            else if (m != null) il.Emit(c.opcode, m);
            else throw new Exception("Unsupported test instruction: " + c);
        }
        var run = (Func<object, object, object, object, int>)method.CreateDelegate(typeof(Func<object, object, object, object, int>));
        foreach (var material in new[] { null, new object() })
        {
            Clones = Tints = Assignments = 0;
            Renderers.Add(new object());
            Check(run(null, null, null, material) == 1, "Remaining initialization did not run");
            int expected = material == null ? 0 : 1;
            Check(Clones == expected && Tints == expected && Assignments == expected, "Incorrect tint path");
            Check(Renderers.Count == 0, "Renderer scratch list not cleared");
        }

        bool rejected = false;
        try { Patch(new[] { new CodeInstruction(OpCodes.Ret) }, il); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Unknown game IL was not rejected");
        rejected = false;
        try { Patch(new[] { new CodeInstruction(OpCodes.Call, clone), new CodeInstruction(OpCodes.Call, clone) }, il); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Ambiguous clone pattern was not rejected");
        return "PASS: current game IL matched; null/valid material paths, remaining initialization, cleanup, labels and unsupported patterns verified.";
    }
}
'@
[ModelTintRegression]::Run()

[xml]$entities = Get-Content -LiteralPath (Join-Path $modRoot '98-AECxProjectZ_Tweaks/Config/entityclasses.xml') -Raw
$demolition = $entities.SelectSingleNode("//entity_class[@name='AECZombieDemolitionTier16Transcendent']")
if ($null -eq $demolition) { throw 'T16 Demolition definition missing' }
if ($null -ne $demolition.SelectSingleNode("property[@name='MatColor']")) {
    throw 'T16 Demolition still forces generic material tint'
}
Write-Output 'PASS: entityclasses.xml parses; T16 Demolition retains native materials.'
