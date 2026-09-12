#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Test-LegendaryAdventure.ps1')
Add-Type -ReferencedAssemblies ($references + $frameworkReferences) -TypeDefinition @'
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using AECT16RuntimeFix;

public static class TrialSharingRegression
{
    static void Check(bool ok, string text) { if (!ok) throw new Exception(text); }
    static T Raw<T>() { return (T)RuntimeHelpers.GetUninitializedObject(typeof(T)); }
    public sealed class OfflineLog : UnityEngine.ILogHandler
    {
        public void LogException(Exception exception, UnityEngine.Object context) { }
        public void LogFormat(UnityEngine.LogType type, UnityEngine.Object context, string format, params object[] args) { }
    }
    public sealed class ObjectiveProbe : ObjectiveEntityKill
    {
        public int Refreshes;
        public override void SetupDisplay() { }
        public override void Refresh() { Refreshes++; ObjectiveState = ObjectiveStates.Complete; }
    }
    public static string Credits()
    {
        UnityEngine.Debug.unityLogger.logHandler=new OfflineLog();
        var player = Raw<EntityPlayerLocal>(); player.entityId = 43;
        player.QuestJournal = Raw<QuestJournal>(); player.QuestJournal.quests = new List<Quest>();
        var q = Raw<Quest>(); q.ID = "PZAECChallengeT19"; q.QuestCode = -42; q.SharedOwnerID = 42;
        AccessTools.Field(typeof(Quest),"_currentState").SetValue(q,Quest.QuestState.InProgress); q.CurrentPhase = 1;
        q.DataVariables = new Dictionary<string,string>();
        q.Requirements = new List<Quests.Requirements.BaseRequirement>();
        q.Objectives = new List<BaseObjective>();
        player.QuestJournal.quests.Add(q);
        for(int kind=0;kind<3;kind++) {
            var objective=Raw<ObjectiveProbe>(); objective.ID=LegendaryTrialSharing.TrialClass(19,kind);
            objective.OwnerQuest=q; q.Objectives.Add(objective);
        }
        Check(LegendaryTrialSharing.Eligible(q,43,-42,19,0),"Shared invitation rejected");
        LegendaryTrialSharing.ApplyCredit(player,-43,19,0,900);
        LegendaryTrialSharing.ApplyCredit(player,-42,18,0,900);
        LegendaryTrialSharing.ApplyCredit(player,-42,19,3,900);
        Check(q.Objectives.All(o=>o.CurrentValue==0),"Foreign/malformed receipt counted");
        AccessTools.Field(typeof(Quest),"_currentState").SetValue(q,Quest.QuestState.Failed);
        LegendaryTrialSharing.ApplyCredit(player,-42,19,0,900);
        AccessTools.Field(typeof(Quest),"_currentState").SetValue(q,Quest.QuestState.InProgress); q.CurrentPhase=2;
        LegendaryTrialSharing.ApplyCredit(player,-42,19,0,900);
        Check(q.Objectives.All(o=>o.CurrentValue==0),"Inactive/wrong-phase receipt counted");
        q.CurrentPhase=1;
        // No World or victim entity exists in this test. Exercise the real
        // receiver; only the Unity-dependent display/Refresh endpoint is a stub.
        for(int kind=0;kind<3;kind++) {
            LegendaryTrialSharing.ApplyCredit(player,-42,19,kind,900+kind);
            LegendaryTrialSharing.ApplyCredit(player,-42,19,kind,900+kind);
        }
        Check(q.Objectives.All(o=>o.CurrentValue==1 && ((ObjectiveProbe)o).Refreshes==1),"Duplicate receipt or target selection error");
        q.SharedOwnerID=-1;
        Check(!LegendaryTrialSharing.Eligible(q,43,-42,19,0),"Unreserved owner trial accepted");
        q.DataVariables[LegendaryAdventure.SpawnMarker]="PZAECTrialT19";
        Check(LegendaryTrialSharing.Eligible(q,43,-42,19,0),"Owner trial rejected");
        player.QuestJournal.quests.Clear();
        LegendaryTrialSharing.ApplyCredit(player,-42,19,0,900);
        Check(player.QuestJournal.quests.Count==0,"Receipt created an unaccepted quest");
        return "PASS: real corpse-independent receiver; shared/owner guards; exact encounter/tier/target; inactive/phase/absent quest rejection; duplicate receipts capped at one.";
    }

    public static string Persistence()
    {
        int count=0;
        const int classId=123456789;
        var cls=Raw<EntityClass>(); cls.entityClassName="PZAECTrial_hunter_T19";
        EntityClass.list[classId]=cls;
        foreach(int code in new[]{int.MinValue,-42,-1,0,1,65536,int.MaxValue}) foreach(bool network in new[]{false,true}) {
            var original=new EntityCreationData { id=51,entityClass=classId,spawnByName=LegendaryAdventure.Request(code,LegendaryAdventure.SpawnMarker) };
            // Opaque native payload prefix must remain byte-for-byte untouched.
            original.entityData.Write(new byte[]{10,20,30,40},0,4);
            long position=original.entityData.Position;
            LegendaryTrialSharing.AfterSnapshot(original);
            Check(original.entityData.Length==20 && original.entityData.Position==position,"Trailer corrupted native payload/position");
            using(var stream=new MemoryStream()) {
                var writer=new PooledBinaryWriter(); writer.SetBaseStream(stream); original.write(writer,network); writer.Flush(); stream.Position=0;
                var reader=new PooledBinaryReader(); reader.SetBaseStream(stream); var restored=new EntityCreationData(); restored.read(reader,network);
                if(!network) Check(string.IsNullOrEmpty(restored.spawnByName),"Native disk format now saves tag; re-evaluate extension");
                LegendaryTrialSharing.AfterRead(restored);
                Check(restored.spawnByName==original.spawnByName,"Disk/network identity lost");
                Check(restored.entityData.ToArray().Take(4).SequenceEqual(new byte[]{10,20,30,40}),"Native payload overwritten");
                Check(stream.Position==stream.Length,"Native reader left bytes outside length-delimited payload");
                Check(LegendaryTrialSharing.ReadIdentity(restored.entityData,out int received) && received==code,"32-bit identity changed");
            }
            count++;
        }
        var old=new EntityCreationData {entityClass=classId};
        LegendaryTrialSharing.AfterRead(old);
        Check(string.IsNullOrEmpty(old.spawnByName),"Old untagged entity falsely adopted");
        var bad=new MemoryStream(); LegendaryTrialSharing.AppendIdentity(bad,42); bad.Position=15; bad.WriteByte(0);
        Check(!LegendaryTrialSharing.ReadIdentity(bad,out _),"Corrupt identity accepted");
        cls.entityClassName="PZAECDefense_hunter_T19";
        var unrelated=new EntityCreationData {entityClass=classId,spawnByName=LegendaryAdventure.Request(42,LegendaryAdventure.SpawnMarker)};
        LegendaryTrialSharing.AfterSnapshot(unrelated);
        Check(unrelated.entityData.Length==0,"Non-trial save changed");
        foreach(string badTag in new[]{null,"","PZAECAdventure:01:"+LegendaryAdventure.SpawnMarker,"PZAECAdventure:42:other",LegendaryAdventure.Request(42,LegendaryAdventure.SpawnMarker)+"x"})
            Check(!LegendaryTrialSharing.ParseTag(badTag,out _),"Malformed tag accepted");
        EntityClass.list.Remove(classId);
        return "PASS: "+count+" real native disk/network identity round-trips; zero/negative/full-int codes; native payload preservation; old/corrupt/non-trial guards.";
    }

    public static void Header(NetPackage ignored, PooledBinaryWriter writer) { ((BinaryWriter)writer).Write((ushort)321); }
    public static string PacketAndBoundaries(Func<MethodInfo,ILGenerator,List<CodeInstruction>> readIL)
    {
        // Execute actual compiled packet write IL; replace only base.write's
        // runtime package-ID lookup with a fixed header (no live server host).
        var method=typeof(NetPackagePZAECTrialKill).GetMethod("write");
        var dm=new DynamicMethod("TrialReceiptWrite",typeof(void),new[]{typeof(NetPackagePZAECTrialKill),typeof(PooledBinaryWriter)});
        var il=dm.GetILGenerator();
        foreach(var instruction in readIL(method,il)) {
            if(instruction.operand is MethodInfo m) il.Emit(instruction.opcode,m.DeclaringType==typeof(NetPackage) && m.Name=="write" ? typeof(TrialSharingRegression).GetMethod("Header") : m);
            else if(instruction.operand is FieldInfo f) il.Emit(instruction.opcode,f);
            else if(instruction.operand==null) il.Emit(instruction.opcode);
            else throw new Exception("Packet IL changed: "+instruction);
        }
        var write=(Action<NetPackagePZAECTrialKill,PooledBinaryWriter>)dm.CreateDelegate(typeof(Action<NetPackagePZAECTrialKill,PooledBinaryWriter>));
        int count=0;
        foreach(int code in new[]{int.MinValue,-42,0,int.MaxValue}) for(int tier=16;tier<=19;tier++) for(int kind=0;kind<3;kind++) {
            var sent=new NetPackagePZAECTrialKill().Setup(43,code,tier,kind,900);
            using(var stream=new MemoryStream()) {
                var writer=new PooledBinaryWriter(); writer.SetBaseStream(stream); write(sent,writer); writer.Flush(); stream.Position=0;
                var reader=new PooledBinaryReader(); reader.SetBaseStream(stream); Check(reader.ReadUInt16()==321,"Packet header");
                var received=new NetPackagePZAECTrialKill(); received.read(reader);
                Check(received.Recipient==43 && received.Code==code && received.Tier==tier && received.Kind==kind && received.Victim==900,"Receipt payload changed");
                Check(stream.Position==stream.Length && stream.Length==sent.GetLength()+2,"Receipt length mismatch");
                Check(received.PackageDirection==NetPackageDirection.ToClient && received.ReliableDelivery,"Receipt security/delivery direction changed");
                Check(LegendaryTrialSharing.ParseClass(LegendaryTrialSharing.TrialClass(tier,kind),out int t,out int k) && t==tier && k==kind,"Class mapping");
            }
            count++;
        }
        Check(LegendaryTrialSharing.EffectiveRange(100)==200 && LegendaryTrialSharing.EffectiveRange(500)==500,"Range shrank native settings");
        Check(LegendaryTrialSharing.InRange(200,200) && !LegendaryTrialSharing.InRange(200.01f,200),"Inclusive boundary mismatch");
        Check(!LegendaryTrialSharing.InRange(float.NaN,200) && !LegendaryTrialSharing.InRange(-1,200),"Invalid distance accepted");
        Check(LegendaryTrialSharing.BoundaryState(179,200)==0 && LegendaryTrialSharing.BoundaryState(180,200)==1 && LegendaryTrialSharing.BoundaryState(201,200)==2,"Warning boundary mismatch");
        using(var mod=Mono.Cecil.ModuleDefinition.ReadModule(typeof(LegendaryTrialSharing).Assembly.Location))
        using(var game=Mono.Cecil.ModuleDefinition.ReadModule(typeof(Quest).Assembly.Location)) {
            Check(!mod.AssemblyReferences.Any(r=>r.Name=="System.Private.CoreLib" || r.Name=="Microsoft.CodeAnalysis"),"Build leaked compiler host references into game DLL");
            var helper=mod.Types.Single(t=>t.Name=="LegendaryTrialSharing");
            foreach(string hook in new[]{"AfterSnapshot","AfterRead","AfterAwardKill","AfterPlayerUpdate"})
                Check(helper.Methods.Single(m=>m.Name=="Install").Body.Instructions.Any(i=>i.Operand is string s && s==hook),"Hook not registered: "+hook);
            Check(helper.Methods.Single(m=>m.Name=="AfterAwardKill").Body.Instructions.Any(i=>i.Operand is Mono.Cecil.MethodReference r && r.Name=="get_IsServer"),"Death relay not server-gated");
            var packet=mod.Types.Single(t=>t.Name=="NetPackagePZAECTrialKill");
            Check(packet.Methods.Single(m=>m.Name=="ProcessPackage").Body.Instructions.Any(i=>i.Operand is Mono.Cecil.MethodReference r && r.Name=="get_IsServer"),"Receive path missing server rejection");
            var apply=game.Types.Single(t=>t.Name=="EntityCreationData").Methods.Single(m=>m.Name=="ApplyToEntity");
            Check(apply.Body.Instructions.Any(i=>i.Operand is Mono.Cecil.FieldReference f && f.DeclaringType.Name=="Entity" && f.Name=="spawnByName"),"Restored tag no longer copied to actual entity");
            var nativeRead=apply.Body.Instructions.Single(i=>i.Operand is Mono.Cecil.MethodReference r && r.DeclaringType.Name=="Entity" && r.Name=="Read");
            Check(!apply.Body.Instructions.SkipWhile(i=>i!=nativeRead).Any(i=>i.Operand is Mono.Cecil.MethodReference r && r.Name=="get_Length"),"Native entity reader gained a trailing-length assertion");
        }
        return "PASS: "+count+" actual receipt write/read payloads; reliable client-only direction; 200m minimum / larger server setting / inclusive edge / 90% warning boundaries (not a live handshake).";
    }
}
'@
[TrialSharingRegression]::Persistence()
[TrialSharingRegression]::Credits()
[TrialSharingRegression]::PacketAndBoundaries({ param($method,$il) [LegendaryNetworkingRegression]::ReadGameIL($method,$il) })

$localization = Import-Csv (Join-Path $modRoot '99-AEC_T16_RuntimeFix/Config/Localization.csv')
foreach ($suffix in @('Start','Near','Outside','Safe','Solo')) {
    $entry = @($localization | Where-Object Key -eq "PZAECTrialRange$suffix")
    if ($entry.Count -ne 1 -or !$entry[0].english -or !$entry[0].schinese -or !$entry[0].tchinese) { throw "Missing/duplicate range notice: $suffix" }
    foreach ($language in @('english','schinese','tchinese')) { $null = $entry[0].$language -f 200,201 }
}
'PASS: English and Chinese range messages present with valid format placeholders.'
