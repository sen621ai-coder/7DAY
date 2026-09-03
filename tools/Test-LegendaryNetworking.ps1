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
# Test the actual networking helper and the game packet wire format without Unity.
Add-Type -ReferencedAssemblies ($references + $frameworkReferences) -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using AECT16RuntimeFix;

public static class LegendaryNetworkingRegression
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
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
            Check(!definition.Body.HasExceptionHandlers, "Packet method gained exception regions; update test reader");
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


    public static string Mapping()
    {
        Check(LegendaryQuestNetworking.QuestIdForPage("pzaec_t18_huge") == "aec_quest_T18_A4_clear", "Page mapping");
        Check(LegendaryQuestNetworking.QuestIdForPage("pzaec_t15_huge") == null, "Lower menus touched");
        Check(LegendaryQuestNetworking.QuestIdForPage("pzaec_t19_massive_extra") == null, "Invalid page accepted");
        Check(LegendaryQuestNetworking.OfferTiers(15).SequenceEqual(new[]{15}), "Lower tier generator changed");
        Check(LegendaryQuestNetworking.OfferTiers(16).SequenceEqual(new[]{16}), "T16 offers");
        Check(LegendaryQuestNetworking.OfferTiers(18).SequenceEqual(new[]{16,17,18}), "Missing unlocked tiers");
        Check(LegendaryQuestNetworking.OfferTiers(19).Length * 30 == 120, "Offer count cap");

        var ids = new List<string> { "vanilla1", "special6", "aec_quest_T18_A4_clear", "aec_quest_T17_A4_clear",
            "aec_quest_t18_a4_clear", "aec_quest_T18_A4_clear", "vanilla3", "aec_quest_T18_A4_clear" };
        var tiers = new List<int> { 1,6,6,6,6,6,3,6 };
        var ready = new List<bool> { true,false,false,true,true,true,true,true };
        int removal;
        Check(LegendaryQuestNetworking.ResolveIndex(ids, tiers, ready, "aec_quest_T18_A4_clear", 0, out removal) == 4 && removal == 3,
            "Absolute/difficulty-relative index mismatch with special and unready entries");
        int chosen = LegendaryQuestNetworking.ResolveIndex(ids, tiers, ready, "AEC_QUEST_T18_A4_CLEAR", 1, out removal);
        Check(chosen == 5 && removal == 4, "Second offer slot mismatch");
        var nativeRemoval = Enumerable.Range(0, ids.Count).Where(i => tiers[i] == 6).ElementAt(removal);
        Check(nativeRemoval == chosen, "Server would remove another quest");
        Check(LegendaryQuestNetworking.ResolveIndex(ids, tiers, ready, "aec_quest_T19_A4_clear", 0, out removal) == -1, "Absent tier leaked another offer");
        Check(LegendaryQuestNetworking.ResolveIndex(ids, tiers, ready, "aec_quest_T18_A4_clear", 5, out removal) == -1, "Sparse page out of range");
        var other = new List<string>(ids); other[4] = "aec_quest_T19_A4_clear";
        Check(LegendaryQuestNetworking.ResolveIndex(other, tiers, ready, "aec_quest_T19_A4_clear", 0, out removal) == 4, "Peer-local mapping");
        Check(ids[4] != other[4], "Player lists shared/mutated");
        var many = Enumerable.Repeat("base6", 256).Concat(new[]{"wanted"}).ToList();
        Check(LegendaryQuestNetworking.ResolveIndex(many, Enumerable.Repeat(6,257).ToList(), Enumerable.Repeat(true,257).ToList(),
            "wanted", 0, out removal) == -1, "Byte removal overflow");

        string id = "keep", type = "keep";
        int index = 3, tier = 6, state;
        LegendaryQuestNetworking.ResponsePrefix("pzaec_t15_small", ref id, ref type, ref index, ref tier, out state);
        Check(id == "keep" && index == 3 && tier == 6 && state == -1, "Unrelated menu changed");
        tier = LegendaryQuestNetworking.MenuMarker; index = -1;
        LegendaryQuestNetworking.ResponsePrefix("pzaec_t19_small", ref id, ref type, ref index, ref tier, out state);
        Check(id == "" && type == "" && index == int.MaxValue && tier == -1 && state == -1, "Invalid slot can create local quest");

        var response = (DialogResponseQuest)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(DialogResponseQuest));
        var action = (DialogActionAddQuest)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(DialogActionAddQuest));
        response.Actions = new List<BaseDialogAction>{action}; response.IsValid = true; action.ListIndex = 100;
        LegendaryQuestNetworking.ResponsePostfix(response, 17);
        Check(action.ListIndex == 17, "Removal index not applied to real dialog action");
        Check(!LegendaryQuestNetworking.PacketPrefix(null, null), "Null world packet not rejected");
        return "PASS: per-player cached-list mapping, sparse pages, no local quest creation, difficulty-relative removal and byte-limit guards.";
    }

    public static string Wire()
    {
        var entries = new List<NetPackageNPCQuestList.QuestPacketEntry>();
        foreach (int tier in new[]{16,17,18,19})
        for (int area=1; area<=5; area++)
        for (int slot=0; slot<6; slot++)
            entries.Add(new NetPackageNPCQuestList.QuestPacketEntry {
                QuestID = LegendaryAdventure.OfferId("aec_quest_T"+tier+"_A"+area+"_clear", slot, area),
                QuestLocation = new Vector3(tier*100+slot,60,area*100),
                QuestSize = new Vector3(area*10,20,area*10),
                TraderPos = new Vector3(500,61,600), POIName = "server-poi-"+tier+"-"+area+"-"+slot });
        using (var stream = new MemoryStream())
        {
            var writer = new BinaryWriter(stream);
            foreach(var entry in entries) entry.write(writer);
            writer.Flush(); stream.Position=0;
            var reader = new BinaryReader(stream);
            foreach(var source in entries)
            {
                var received = new NetPackageNPCQuestList.QuestPacketEntry(); received.read(reader);
                Check(received.QuestID==source.QuestID && received.POIName==source.POIName &&
                    received.QuestLocation.x==source.QuestLocation.x && received.QuestLocation.y==60 &&
                    received.QuestLocation.z==source.QuestLocation.z && received.QuestSize.x==source.QuestSize.x &&
                    received.TraderPos.x==500, "Server POI data changed on wire");
            }
            Check(stream.Position==stream.Length, "Packet stream not fully consumed");
        }
        return "PASS: 120 legendary offers round-trip through the game's real QuestPacketEntry serialization with IDs/locations/sizes/trader positions preserved.";
    }

    static object RunExpression(List<CodeInstruction> expression, PrefabInstance value)
    {
        var dm = new DynamicMethod("PacketNullPOI", typeof(int), new[]{typeof(PrefabInstance)});
        var il = dm.GetILGenerator();
        foreach(var c in expression)
        {
            if (c.opcode.Name.StartsWith("ldloc")) il.Emit(OpCodes.Ldarg_0);
            else if (c.operand is FieldInfo field) il.Emit(c.opcode,field);
            else if (c.operand is ConstructorInfo ctor) il.Emit(c.opcode,ctor);
            else if (c.operand is float number) il.Emit(c.opcode,number);
            else il.Emit(c.opcode);
        }
        var empty = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Brfalse,empty);
        il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
        il.MarkLabel(empty); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
        return dm.Invoke(null,new object[]{value});
    }

    public static string PacketIL()
    {
        var target = AccessTools.Method(typeof(NetPackageQuestGotoPoint),"ProcessPackage");
        var original = ReadGameIL(target,new DynamicMethod("ReadPacket",typeof(void),Type.EmptyTypes).GetILGenerator());
        var ops = original.Select(c=>c.opcode).ToArray();
        var operands = original.Select(c=>c.operand).ToArray();
        var saved = original.Select(c=>new CodeInstruction(c)).ToList();
        int labels=original.Sum(c=>c.labels.Count);
        var patched = LegendaryQuestNetworking.PacketNullPoiTranspiler(original).ToList();
        var changed = Enumerable.Range(0,ops.Length).Where(i=>patched[i].opcode!=ops[i]).ToList();
        Check(changed.Count==24 && patched.Count==ops.Length && changed.Last()-changed.First()==23,"Unexpected packet patch region");
        Check(patched.Sum(c=>c.labels.Count)==labels,"Packet branch labels lost");
        for(int i=0;i<ops.Length;i++)
            if(!changed.Contains(i)) Check(patched[i].opcode==ops[i] && Equals(patched[i].operand,operands[i]),"Payload/retry/client logic changed");
        int start=changed.First();
        Check(patched[start+25].opcode==OpCodes.Brfalse || patched[start+25].opcode==OpCodes.Brfalse_S,"Native prefab null check lost");
        bool oldThrows=false;
        try { RunExpression(saved.Skip(start).Take(24).ToList(),null); }
        catch (TargetInvocationException ex) { oldThrows=ex.InnerException is NullReferenceException; }
        Check(oldThrows,"Original missing-POI bug was not reproduced");
        Check((int)RunExpression(patched.Skip(start).Take(24).ToList(),null)==0,"Patched null POI still throws");
        var valid=(PrefabInstance)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(PrefabInstance));
        Check((int)RunExpression(patched.Skip(start).Take(24).ToList(),valid)==1,"Valid POI path changed");
        bool rejected=false;
        try { LegendaryQuestNetworking.PacketNullPoiTranspiler(new[]{new CodeInstruction(OpCodes.Ret)}).ToList(); }
        catch(InvalidOperationException) { rejected=true; }
        Check(rejected,"Unsupported packet code not rejected");
        return "PASS: real packet IL matched; original null-POI exception reproduced, patched null/valid paths executed; native response/retry/client logic preserved.";
    }
}
'@
[LegendaryNetworkingRegression]::Mapping()
[LegendaryNetworkingRegression]::Wire()
[LegendaryNetworkingRegression]::PacketIL()
