using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using AECT16RuntimeFix;

// Execute the installed game's serializer and response-building IL off-line.
// Only the packet-ID header/Unity world are omitted; payload and reconstruction
// come from the actual game assembly, not a hand-written wire-format mock.
public static class HighDamageNativeTests
{
    static int checks, allocations, frees;
    static Dictionary<MethodInfo, MethodInfo> replacements = new Dictionary<MethodInfo, MethodInfo>();
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static MethodInfo Method(Type type, string name) { return AccessTools.Method(type, name); }
    public static void SkipHeader(NetPackage packet, BinaryWriter writer) { }
    public static NetPackagePZAECFullDamageV1 Allocate() { allocations++; return new NetPackagePZAECFullDamageV1(); }
    public static void Free(NetPackage packet) { frees++; }

    static List<CodeInstruction> Read(MethodInfo method, ILGenerator il)
    {
        var locals = method.GetMethodBody().LocalVariables.Select(v => il.DeclareLocal(v.LocalType, v.IsPinned)).ToArray();
        var ops = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)).ToDictionary(o => o.Value);
        using (var module = Mono.Cecil.ModuleDefinition.ReadModule(method.Module.FullyQualifiedName))
        {
            var definition = (Mono.Cecil.MethodDefinition)module.LookupToken(method.MetadataToken);
            Check(!definition.Body.HasExceptionHandlers, "Update test IL reader for exception regions");
            var labels = definition.Body.Instructions.ToDictionary(i => i, i => il.DefineLabel());
            var result = new List<CodeInstruction>();
            foreach (var i in definition.Body.Instructions)
            {
                object operand = i.Operand;
                if (operand is Mono.Cecil.Cil.Instruction) operand = labels[(Mono.Cecil.Cil.Instruction)operand];
                else if (operand is Mono.Cecil.Cil.Instruction[]) operand = ((Mono.Cecil.Cil.Instruction[])operand).Select(b => labels[b]).ToArray();
                else if (operand is Mono.Cecil.Cil.VariableDefinition) operand = locals[((Mono.Cecil.Cil.VariableDefinition)operand).Index];
                else if (operand is Mono.Cecil.ParameterDefinition) operand = ((Mono.Cecil.ParameterDefinition)operand).Index + (method.IsStatic ? 0 : 1);
                else if (operand is Mono.Cecil.MemberReference) operand = method.Module.ResolveMember(((Mono.Cecil.MemberReference)operand).MetadataToken.ToInt32());
                var instruction = new CodeInstruction(ops[i.OpCode.Value], operand);
                instruction.labels.Add(labels[i]); result.Add(instruction);
            }
            return result;
        }
    }

    static void Emit(ILGenerator il, IEnumerable<CodeInstruction> code)
    {
        foreach (var c in code)
        {
            foreach (var label in c.labels) il.MarkLabel(label);
            var op = c.opcode; object value = c.operand;
            if (op.OperandType == OperandType.ShortInlineBrTarget)
                op = (OpCode)typeof(OpCodes).GetFields().First(f => f.FieldType == typeof(OpCode) &&
                    ((OpCode)f.GetValue(null)).Name == op.Name.Substring(0, op.Name.Length - 2)).GetValue(null);
            if (value is MethodInfo)
            {
                var m = (MethodInfo)value;
                if (replacements.ContainsKey(m)) { m = replacements[m]; op = OpCodes.Call; }
                else if (m.DeclaringType == typeof(PooledBinaryWriter) || m.DeclaringType == typeof(PooledBinaryReader))
                {
                    var type = m.DeclaringType == typeof(PooledBinaryWriter) ? typeof(BinaryWriter) : typeof(BinaryReader);
                    m = type.GetMethod(m.Name, m.GetParameters().Select(p => p.ParameterType).ToArray());
                }
                il.Emit(op, m);
            }
            else if (value is ConstructorInfo) il.Emit(op, (ConstructorInfo)value);
            else if (value is FieldInfo) il.Emit(op, (FieldInfo)value);
            else if (value is Type) il.Emit(op, (Type)value);
            else if (value is LocalBuilder) il.Emit(op, (LocalBuilder)value);
            else if (value is Label) il.Emit(op, (Label)value);
            else if (value is Label[]) il.Emit(op, (Label[])value);
            else if (value is string) il.Emit(op, (string)value);
            else if (value is float) il.Emit(op, (float)value);
            else if (value is int) { if (op.OperandType == OperandType.ShortInlineVar) il.Emit(op, (byte)(int)value); else il.Emit(op, (int)value); }
            else if (value is sbyte) il.Emit(op, (sbyte)value);
            else if (value == null) il.Emit(op);
            else throw new Exception("Unsupported operand: " + value);
        }
    }

    static DynamicMethod Clone(MethodInfo original, Type result, params Type[] parameters)
    {
        var dm = new DynamicMethod("Test_" + original.Name, result, parameters, typeof(HighDamageNativeTests).Module, true);
        var il = dm.GetILGenerator(); Emit(il, Read(original, il)); return dm;
    }

    static DamageResponse Response(int strength)
    {
        var source = new DamageSourceEntity((EnumDamageSource)1, (EnumDamageTypes)1, 777,
            new Vector3(1,2,3), "Head", new Vector3(4,5,6), new Vector2(.2f,.8f));
        source.KillXPScale = 1.25f; source.DamageMultiplier = 1.5f;
        source.bIgnorePartyShare = true; source.bTrapKillXP = true;
        source.SetIgnoreConsecutiveDamages(true);
        return new DamageResponse { Strength=strength, Source=source, HitBodyPart=(EnumBodyPartHit)2,
            Critical=true, Fatal=false, PainHit=true, MovementState=3, Random=.25f,
            Stun=(EnumEntityStunType)1, StunDuration=2.5f, ArmorDamage=123,
            ArmorSlot=(EquipmentSlots)1, ArmorSlotGroup=(EquipmentSlotGroups)1 };
    }

    delegate bool Select(NetPackageDamageEntity input, int target, DamageResponse response, ref NetPackageDamageEntity output);

    public static string Run()
    {
        var baseType = typeof(NetPackageDamageEntity); var fullType = typeof(NetPackagePZAECFullDamageV1);
        replacements[Method(typeof(NetPackage), "write")] = Method(typeof(HighDamageNativeTests), "SkipHeader");
        var baseWrite = Clone(Method(baseType,"write"),typeof(void),baseType,typeof(BinaryWriter));
        var baseRead = Clone(Method(baseType,"read"),typeof(void),baseType,typeof(BinaryReader));
        replacements[Method(baseType,"write")] = baseWrite;
        replacements[Method(baseType,"read")] = baseRead;
        var fullWrite = Clone(Method(fullType,"write"),typeof(void),fullType,typeof(BinaryWriter));
        var fullRead = Clone(Method(fullType,"read"),typeof(void),fullType,typeof(BinaryReader));

        // Verify exactly one strength instruction changes, and every original
        // branch, attack-event call and damage-processing call stays in place.
        var dm = new DynamicMethod("DecodeResponse",typeof(DamageResponse),new[]{baseType},typeof(HighDamageNativeTests).Module,true);
        var il = dm.GetILGenerator(); var original = Read(Method(baseType,"ProcessPackage"),il);
        var saved = original.Select(c => new CodeInstruction(c)).ToList();
        var patched = HighDamageNetworking.RestoreStrength(original).ToList();
        Check(saved.Count == patched.Count, "Instruction count changed");
        Check(Enumerable.Range(0,saved.Count).Count(i => saved[i].opcode != patched[i].opcode || !Equals(saved[i].operand,patched[i].operand)) == 1,
            "Only the strength read may change");
        Check(saved.Zip(patched,(a,b)=>a.labels.SequenceEqual(b.labels)).All(x=>x), "Labels changed");
        foreach (string name in new[]{"FireAttackedEvents","ProcessDamageResponse"})
            Check(patched.Count(c => c.operand is MethodInfo && ((MethodInfo)c.operand).Name == name) == 1,"Duplicate/missing native " + name);
        // Source/response construction begins immediately after the target-null
        // branch, and ends immediately before the entity event calls.
        int ctor = patched.FindIndex(c => c.opcode == OpCodes.Newobj && ((ConstructorInfo)c.operand).DeclaringType == typeof(DamageSourceEntity));
        int start = ctor - 14;
        Check(patched[start].opcode == OpCodes.Ldarg_0, "Native source construction start changed");
        int eventCall = patched.FindIndex(c => c.operand is MethodInfo && ((MethodInfo)c.operand).Name == "FireAttackedEvents");
        int end = eventCall - 2;
        Emit(il,patched.Skip(start).Take(end-start));
        // The final branch targets the first omitted ldloc.1.
        foreach(var label in patched[end].labels) il.MarkLabel(label);
        il.Emit(OpCodes.Ldloc_3); il.Emit(OpCodes.Ret);
        var decode = (Func<NetPackageDamageEntity,DamageResponse>)dm.CreateDelegate(typeof(Func<NetPackageDamageEntity,DamageResponse>));

        var active = typeof(HighDamageNetworking).GetField("<Active>k__BackingField",BindingFlags.NonPublic|BindingFlags.Static);
        active.SetValue(null,true);
        var get = typeof(NetPackageManager).GetMethods().Single(m=>m.Name=="GetPackage").MakeGenericMethod(fullType);
        replacements[get]=Method(typeof(HighDamageNativeTests),"Allocate");
        replacements[Method(typeof(NetPackageManager),"FreePackage")]=Method(typeof(HighDamageNativeTests),"Free");
        var select = (Select)Clone(Method(typeof(HighDamageNetworking),"SelectPacket"),typeof(bool),baseType,typeof(int),typeof(DamageResponse),baseType.MakeByRefType()).CreateDelegate(typeof(Select));
        int[] values={0,1,65534,65535,65536,200000,1000000,int.MaxValue};
        long total=0;
        foreach(int value in values)
        {
            var response=Response(value); var originalPacket=new NetPackageDamageEntity(); NetPackageDamageEntity sent=null;
            bool native=select(originalPacket,123,response,ref sent);
            Check(native==(value<=65535),"Wrong routing at "+value);
            if(native) sent=originalPacket.Setup(123,response);
            Check(sent.entityId==123&&sent.attackerEntityId==777,"Setup lost identity");
            for(int hop=0;hop<2;hop++)
            {
                bool full=sent is NetPackagePZAECFullDamageV1;
                var stream=new MemoryStream(); var writer=new BinaryWriter(stream);
                (full?fullWrite:baseWrite).Invoke(null,new object[]{sent,writer});writer.Flush();
                if(full)
                {
                    var legacy=new MemoryStream();baseWrite.Invoke(null,new object[]{sent,new BinaryWriter(legacy)});
                    Check(stream.Length==legacy.Length+4,"Unexpected high-damage payload size");
                    Check(stream.ToArray().Take((int)legacy.Length).SequenceEqual(legacy.ToArray()),"Native payload changed");
                }
                stream.Position=0;
                NetPackageDamageEntity received=full?(NetPackageDamageEntity)new NetPackagePZAECFullDamageV1():new NetPackageDamageEntity();
                if(full) received.attackingItem=(ItemValue)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ItemValue));
                (full?fullRead:baseRead).Invoke(null,new object[]{received,new BinaryReader(stream)});
                Check(stream.Position==stream.Length,"Payload not fully consumed");
                Check(received.attackingItem==null,"Stale item survived packet reuse");
                var restored=decode(received);
                Check(restored.Strength==value,"Truncated strength at "+value+" hop "+hop);
                Check(restored.HitBodyPart==response.HitBodyPart && restored.Critical && restored.PainHit && !restored.Fatal,"Hit flags changed");
                Check(restored.Source.getEntityId()==777 && restored.Source.getHitTransformName()=="Head" && restored.Source.KillXPScale==1.25f,"Attribution changed");
                Check(restored.StunDuration==2.5f && restored.ArmorDamage==123 && restored.Random==.25f,"Auxiliary hit data changed");
                sent=full?(NetPackageDamageEntity)new NetPackagePZAECFullDamageV1().SetupFull(123,restored):new NetPackageDamageEntity().Setup(123,restored);
                if(hop==0) total+=restored.Strength;
            }
        }
        Check(total==values.Sum(v=>(long)v),"Cumulative damage mismatch");
        Check(allocations==4&&frees==4,"Replacement allocation/release mismatch");
        // Sustained fire and multiple shooters, repeatedly reusing the receiver.
        var reusable=new NetPackagePZAECFullDamageV1(); long burstTotal=0;
        for(int shot=0;shot<1000;shot++)
        {
            int damage=200000+shot;
            var burst=Response(damage);
            burst.Source=new DamageSourceEntity((EnumDamageSource)1,(EnumDamageTypes)1,777+shot%2);
            burst.Fatal=shot==999;
            var outgoing=new NetPackagePZAECFullDamageV1().SetupFull(123,burst);
            var stream=new MemoryStream();var writer=new BinaryWriter(stream);
            fullWrite.Invoke(null,new object[]{outgoing,writer});writer.Flush();stream.Position=0;
            fullRead.Invoke(null,new object[]{reusable,new BinaryReader(stream)});
            var received=decode(reusable);
            Check(received.Strength==damage && received.Source.getEntityId()==777+shot%2 && received.Fatal==(shot==999),
                "Repeated read retained stale damage/attacker/fatal state");
            burstTotal+=received.Strength;
        }
        Check(burstTotal==200499500L,"Sustained fire total mismatch");
        var high=new NetPackagePZAECFullDamageV1().SetupFull(123,Response(200000)); NetPackageDamageEntity ignored=null;
        Check(select(high,123,Response(200000),ref ignored),"Subclass Setup recursed");
        var fatal=Response(200000);fatal.Fatal=true;
        Check(decode(new NetPackagePZAECFullDamageV1().SetupFull(123,fatal)).Fatal,"Fatal flag lost");
        foreach(int invalid in new[]{int.MinValue,-1,0,65535})
        {
            bool rejected=false;try{new NetPackagePZAECFullDamageV1().SetupFull(123,Response(invalid));}catch(InvalidDataException){rejected=true;}
            Check(rejected,"Invalid setup accepted");
            var stream=new MemoryStream();var writer=new BinaryWriter(stream);
            baseWrite.Invoke(null,new object[]{high,writer});writer.Write(invalid);writer.Flush();stream.Position=0;
            rejected=false;try{fullRead.Invoke(null,new object[]{new NetPackagePZAECFullDamageV1(),new BinaryReader(stream)});}catch(TargetInvocationException e){rejected=e.InnerException is InvalidDataException;}
            Check(rejected,"Invalid wire strength accepted");
        }
        var truncated=new MemoryStream();baseWrite.Invoke(null,new object[]{high,new BinaryWriter(truncated)});truncated.Position=0;
        bool truncatedRejected=false;
        try{fullRead.Invoke(null,new object[]{high,new BinaryReader(truncated)});}catch(TargetInvocationException e){truncatedRejected=e.InnerException is EndOfStreamException;}
        Check(truncatedRejected && high.FullStrength==0,"Truncated packet reused stale full strength");
        bool badIL=false;try{HighDamageNetworking.RestoreStrength(new[]{new CodeInstruction(OpCodes.Ret)}).ToList();}catch(InvalidOperationException){badIL=true;}
        Check(badIL,"Unsupported game IL accepted");
        Check(HighDamageNetworking.HasProtocol(new[]{"NetPackageDamageEntity",fullType.Name}),"V1 mapping rejected");
        Check(!HighDamageNetworking.HasProtocol(new[]{"NetPackagePZAECFullDamageV2"})&&!HighDamageNetworking.HasProtocol(null),"Missing/different protocol accepted");
        string version="V2.3";HighDamageNetworking.LoginPrepared(ref version);HighDamageNetworking.LoginPrepared(ref version);
        Check(version=="V2.3"+HighDamageNetworking.LoginMarker,"Marker duplicated or version changed");
        Check(HighDamageNetworking.HasLoginMarker(version)&&!HighDamageNetworking.HasLoginMarker("V2.3")&&!HighDamageNetworking.HasLoginMarker("V2.3|PZAEC-Damage32:2"),"Login compatibility check failed");
        Check(HighDamageNetworking.CheckLogin(null,version),"Matching client rejected");
        Check(HighDamageNetworking.CheckMappings(new[]{fullType.Name}),"Matching server rejected");
        active.SetValue(null,false);version="V2.3";HighDamageNetworking.LoginPrepared(ref version);
        Check(version=="V2.3","Inactive patch advertised support");
        return "PASS: "+checks+" native IL, payload round-trip, 32-bit boundaries, rebroadcast, hit metadata, routing and compatibility checks. Offline; no live multiplayer/Unity world test.";
    }
}
