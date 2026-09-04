$ErrorActionPreference = 'Stop'
# Offline regression: inspect the real game IL and exercise managed selection.
# Native Harmony installation, Unity entity creation and a live wave need an in-game test.
$modRoot = Split-Path -Parent $PSScriptRoot
$managed = Join-Path (Split-Path -Parent $modRoot) '7DaysToDie_Data/Managed'
Get-ChildItem -LiteralPath $managed -Filter '*.dll' | ForEach-Object {
    try { [void][Reflection.Assembly]::LoadFrom($_.FullName) } catch { }
}
$references = @(
    (Join-Path $modRoot '00-TFP_Harmony/0Harmony.dll'),
    (Join-Path $modRoot '00-TFP_Harmony/Mono.Cecil.dll'),
    (Join-Path $modRoot '04-AEC-ENDGAME_OVERHAUL/AeclipseCustomZombieSpawner.dll'),
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
using System.Runtime.Serialization;
using HarmonyLib;
using UnityEngine;
using AECT16RuntimeFix;
using AeclipseCustomZombieSpawner;

public static class BloodMoonRegression
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static List<CodeInstruction> ReadIL(MethodInfo target, ILGenerator il)
    {
        var locals = target.GetMethodBody().LocalVariables.Select(v => il.DeclareLocal(v.LocalType, v.IsPinned)).ToArray();
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)).ToDictionary(o => o.Value);
        using (var module = Mono.Cecil.ModuleDefinition.ReadModule(target.Module.FullyQualifiedName))
        {
            var definition = (Mono.Cecil.MethodDefinition)module.LookupToken(target.MetadataToken);
            Check(!definition.Body.HasExceptionHandlers, "SpawnZombie gained exception regions; update test reader");
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

    public static EntityPlayer SeenTarget;
    public static int Last;
    public static int StubSelect(string group, EntityClass.EntityTierTypes maxTier, ref int last,
        bool enemy, bool animal, GameRandom random, EntityPlayer target)
    {
        Check(group == "test-group" && enemy && !animal, "Original arguments corrupted");
        SeenTarget = target;
        last = -123456;
        return last;
    }

    public static string TestIL()
    {
        var target = AccessTools.Method(typeof(AIDirectorBloodMoonParty), "SpawnZombie",
            new[] { typeof(World), typeof(EntityPlayer), typeof(Vector3), typeof(Vector3) });
        var selector = AccessTools.Method(typeof(EntityGroups), "GetRandomEntityFromGroupMaxTier");
        var wrapper = AccessTools.Method(typeof(BloodMoonSpawnFix), "SelectForTarget");
        Check(target != null && selector != null, "Current game targets missing");
        var original = ReadIL(target, new DynamicMethod("ReadBloodMoon", typeof(void), Type.EmptyTypes).GetILGenerator());
        int site = original.FindIndex(c => Equals(c.operand, selector));
        Check(site >= 0, "New selector is not used by current BloodMoonParty");
        var savedOps = original.Select(c => c.opcode).ToArray();
        var savedOperands = original.Select(c => c.operand).ToArray();
        int labels = original.Sum(c => c.labels.Count);
        var patched = BloodMoonSpawnFix.Transpiler(original).ToList();
        Check(patched.Count == original.Count + 1, "Unexpected instruction count");
        Check(patched[site].opcode == OpCodes.Ldarg_2 && Equals(patched[site + 1].operand, wrapper), "Target argument not passed");
        Check(patched.Sum(c => c.labels.Count) == labels, "Branch labels lost");
        for (int i = 0; i < savedOps.Length; i++)
        {
            if (i == site) continue;
            int j = i > site ? i + 1 : i;
            Check(patched[j].opcode == savedOps[i] && Equals(patched[j].operand, savedOperands[i]), "Unrelated spawn logic changed");
        }
        bool missing = false, ambiguous = false;
        try { BloodMoonSpawnFix.Transpiler(new[] { new CodeInstruction(OpCodes.Ret) }).ToList(); }
        catch (InvalidOperationException) { missing = true; }
        try { BloodMoonSpawnFix.Transpiler(new[] { new CodeInstruction(OpCodes.Call, selector), new CodeInstruction(OpCodes.Call, selector) }).ToList(); }
        catch (InvalidOperationException) { ambiguous = true; }
        Check(missing && ambiguous, "Unsupported IL was not rejected");

        // Execute the patched call stack with a stub selector. No Unity methods run.
        var dm = new DynamicMethod("BloodMoonCallStack", typeof(int), new[] {
            typeof(object), typeof(World), typeof(EntityPlayer), typeof(Vector3), typeof(Vector3) });
        var il = dm.GetILGenerator();
        var call = BloodMoonSpawnFix.Transpiler(new[] {
            new CodeInstruction(OpCodes.Call, selector)
        }).ToList();
        il.Emit(OpCodes.Ldstr, "test-group");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldsflda, typeof(BloodMoonRegression).GetField("Last"));
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldnull);
        foreach (var instruction in call)
        {
            if (instruction.opcode == OpCodes.Ldarg_2) il.Emit(OpCodes.Ldarg_2);
            else il.Emit(OpCodes.Call, typeof(BloodMoonRegression).GetMethod("StubSelect"));
        }
        il.Emit(OpCodes.Ret);
        var player = (EntityPlayer)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(EntityPlayer));
        int result = (int)dm.Invoke(null, new object[] { null, null, player, default(Vector3), default(Vector3) });
        Check(ReferenceEquals(SeenTarget, player) && result == -123456 && Last == -123456,
            "Target identity or signed/by-ref class ID was lost");
        return "PASS: real BloodMoonParty IL matched; target argument, signed IDs, labels, native wave/vehicle logic and failure guards verified.";
    }

    public static string TestRules(string root)
    {
        Func<string,string> path = p => System.IO.Path.Combine(root, p);
        var rules = SpawnRules.Load(path("04-AEC-ENDGAME_OVERHAUL/AeclipseCustomZombieSpawnRules.json"));
        foreach (string p in new[] { "03-AEC-AIO_BOSS_EXTREME_EDITION", "98-AECxProjectZ_Tweaks", "99-AEC_T16_RuntimeFix" })
            rules.MergeFrom(SpawnRules.Load(path(p + "/AeclipseCustomZombieSpawnRules.json")));
        var controller = new ServerSpawnController(rules);
        var random = new System.Random(190090);
        int checks = 0;
        foreach (int gs in new[] { 30000, 30001, 179999, 180000, 190240, 239999, 240000, 269999, 270000, 299999, 300000, 999999, 1500000 })
        {
            int expected = gs >= 300000 ? 19 : gs >= 270000 ? 18 : gs >= 240000 ? 17 : gs >= 180000 ? 16 : 15;
            Check(BloodMoonSpawnFix.TierForGameStage(gs) == expected, "Wrong GS tier");
            if (expected < 16) continue;
            Check(BloodMoonSpawnFix.FallbackGroupForGameStage(gs) == "PZAECBloodMoonT" + expected + "Fallback", "Wrong fallback");
            double chance = 0;
            T16RuntimeFixMod.BloodMoonBossChancePostfix(gs, ref chance);
            Check(chance == 18d, "Boss chance changed");
            foreach (string biome in new[] { "forest", "burnt_forest", "desert", "snow", "wasteland" })
            foreach (bool boss in new[] { false, true })
            for (int i = 0; i < 100; i++)
            {
                string name;
                int capped = Math.Min(gs, 999999);
                Check(controller.TrySelectEntityClassForExplicitTier(capped, expected, biome, true, random, boss, boss, false, out name), "Empty tier/biome pool");
                var tier = rules.Tiers.First(t => capped >= t.MinGameStage && capped <= t.MaxGameStage);
                Check(tier.Entries.Any(e => e.EntityClass == name && e.Tier == expected && e.IsBoss == boss), "Downgraded tier or wrong boss branch");
                checks++;
            }
        }
        Check(BloodMoonSpawnFix.FallbackGroupForGameStage(599) == "PZAECBloodMoonGS000400", "Low-GS fallback changed");
        Check(BloodMoonSpawnFix.FallbackGroupForGameStage(30000) == "PZAECBloodMoonGS030000", "T15 fallback changed");
        Check(BloodMoonSpawnFix.FallbackGroupForGameStage(179999) == "PZAECBloodMoonGS030000", "Extended T15 fallback changed");
        return "PASS: " + checks + " real-controller selections across all biomes; exact T16-T19, normal/boss branches, GS boundaries and overflow cap.";
    }
}
'@
[BloodMoonRegression]::TestIL()
[BloodMoonRegression]::TestRules($modRoot)

[xml]$groups = Get-Content -LiteralPath (Join-Path $modRoot '99-AEC_T16_RuntimeFix/Config/entitygroups.xml') -Raw
[xml]$entities = Get-Content -LiteralPath (Join-Path $modRoot '98-AECxProjectZ_Tweaks/Config/entityclasses.xml') -Raw
$rules = Get-Content -LiteralPath (Join-Path $modRoot '99-AEC_T16_RuntimeFix/AeclipseCustomZombieSpawnRules.json') -Raw | ConvertFrom-Json
foreach ($tier in 16..19) {
    $group = $groups.SelectSingleNode("//entitygroup[@name='PZAECBloodMoonT${tier}Fallback']")
    if (@($group.e).Count -ne 64) { throw "Expected 64 entries in T$tier fallback" }
    if (@($group.e | Group-Object n | Where-Object Count -gt 1).Count) { throw 'Duplicate fallback class' }
    $bossNames = @(($rules.tiers | Where-Object { $_.minGameStage -ge 180000 -and $_.entries[0].tier -eq $tier }).entries | Where-Object isBoss | ForEach-Object entityClass)
    $total = 0.0; $boss = 0.0
    foreach ($entry in $group.e) {
        $weight = [double]::Parse($entry.p, [Globalization.CultureInfo]::InvariantCulture)
        if ($weight -le 0) { throw 'Nonpositive fallback weight' }
        $total += $weight
        if ($bossNames -contains $entry.n) { $boss += $weight }
        if ($null -eq $entities.SelectSingleNode("//entity_class[@name='$($entry.n)']")) { throw "Undefined fallback class $($entry.n)" }
        if ($entry.n -notmatch "(Tier${tier}[A-Za-z]+|T${tier})$") { throw "Wrong class tier $($entry.n)" }
    }
    if ([math]::Abs($total - 1) -gt 0.00000001 -or [math]::Abs($boss - 0.18) -gt 0.00000001) { throw 'Incorrect boss or total weight' }
}
# Apply just the high-GS operations to the existing stages and check exact boundaries.
[xml]$baseStages = Get-Content -LiteralPath (Join-Path $modRoot '98-AECxProjectZ_Tweaks/Config/gamestages.xml') -Raw
[xml]$patchStages = Get-Content -LiteralPath (Join-Path $modRoot '99-AEC_T16_RuntimeFix/Config/gamestages.xml') -Raw
[xml]$effective = '<gamestages><spawner name="BloodMoonHorde"/></gamestages>'
$spawner = $effective.SelectSingleNode('/gamestages/spawner')
foreach ($node in $baseStages.SelectNodes('//gamestage')) { [void]$spawner.AppendChild($effective.ImportNode($node, $true)) }
foreach ($op in $patchStages.configs.ChildNodes) {
    if ($op.Name -eq 'remove') { foreach ($node in @($effective.SelectNodes($op.xpath))) { [void]$node.ParentNode.RemoveChild($node) } }
    if ($op.Name -eq 'append') { foreach ($node in $op.ChildNodes) { if ($node.Name -eq 'gamestage') { [void]$spawner.AppendChild($effective.ImportNode($node, $true)) } } }
}
foreach ($gs in 30000,30001,179999,180000,239999,240000,269999,270000,299999,300000,999999) {
    $stage = $spawner.gamestage | Where-Object { [int]$_.stage -le $gs } | Sort-Object { [int]$_.stage } | Select-Object -Last 1
    $expected = [AECT16RuntimeFix.BloodMoonSpawnFix]::FallbackGroupForGameStage($gs)
    if ($stage.spawn.group -ne $expected) { throw "Wrong effective GS $gs group: $($stage.spawn.group)" }
}
Write-Output 'PASS: all four XML pools have 64 valid exact-tier classes and 18% boss weight; effective high-GS stages and boundaries verified.'
