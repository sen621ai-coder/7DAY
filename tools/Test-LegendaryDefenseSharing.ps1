#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Test-LegendaryDefense.ps1')
Add-Type -ReferencedAssemblies ($references + $frameworkReferences) -TypeDefinition @'
using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using AECT16RuntimeFix;
using S = AECT16RuntimeFix.LegendaryDefenseSharing;

public static class DefenseSharingRegression
{
    static void Check(bool ok,string why) { if(!ok) throw new Exception(why); }
    static T Raw<T>() { return (T)RuntimeHelpers.GetUninitializedObject(typeof(T)); }
    public sealed class OfflineLog : UnityEngine.ILogHandler
    {
        public void LogException(Exception e,UnityEngine.Object context) { }
        public void LogFormat(UnityEngine.LogType t,UnityEngine.Object c,string f,params object[] a) { }
    }
    public sealed class ObjectiveProbe : ObjectiveEntityKill
    {
        public override void SetupDisplay() { }
        public override void Refresh()
        {
            if(CurrentValue < S.Required[S.Slot(ID,19)]) return;
            ObjectiveState=ObjectiveStates.Complete;
            if(OwnerQuest.Objectives.Where(o=>o.Phase==Phase).All(o=>o.Complete)) OwnerQuest.CurrentPhase++;
        }
    }
    public static string Ledgers()
    {
        var all=new S.Ledger(); var missed=new S.Ledger(); int id=100;
        Check(!all.Add(1,-1) && !all.Add(1,11),"Invalid slot accepted");
        for(int slot=0;slot<11;slot++) for(int n=0;n<S.Required[slot];n++) {
            id++;
            Check(all.Add(id,slot),"Distinct same-class victim not counted");
            Check(!all.Add(id,slot) && !all.Add(id,(slot+1)%11),"Victim counted twice");
            if(id!=101) missed.Add(id,slot);
        }
        Check(id==130 && all.Counts.SequenceEqual(S.Required),"Not exactly thirty unique kills");
        for(int wave=1;wave<=3;wave++) Check(all.Complete(wave) && !missed.Complete(wave),"Missed target silently completed");
        Check(!all.Add(999,0) && all.Counts[0]==4,"Target quota exceeded");
        Check(!all.Complete(0) && !all.Complete(4),"Invalid wave complete");
        Check(S.MayJoin(true,1,0) && !S.MayJoin(false,1,0) && !S.MayJoin(true,1,1) && !S.MayJoin(true,2,0),"Late/foreign join guard");
        Check(S.CreditRange(true,true,200,200) && !S.CreditRange(true,true,200.01f,200),"200m inclusive edge");
        Check(!S.CreditRange(false,true,0,200) && !S.CreditRange(true,false,0,200) && !S.CreditRange(true,true,float.NaN,200),"Invalid participant credit");
        Check(LegendaryTrialSharing.EffectiveRange(50)==200 && LegendaryTrialSharing.EffectiveRange(500)==500 && S.CreditRange(true,true,500,500),"Server larger range reduced");
        for(int t=16;t<=19;t++) for(int slot=0;slot<11;slot++) Check(S.Slot(S.Target(t,slot),t)==slot,"Tier/role/wave mapping");
        Check(S.Slot(S.Target(18,0),19)==-1 && S.Slot("zombieArlene",19)==-1,"Foreign class accepted");
        Check(S.ValidReceipt(19,3,S.Won,3,S.Required),"Valid winner rejected");
        Check(!S.ValidReceipt(19,2,S.Won,3,S.Required) && !S.ValidReceipt(19,3,S.Won,0,S.Required) && !S.ValidReceipt(19,3,S.Won,4,S.Required),"Premature or forged rank");
        Check(!S.ValidReceipt(19,3,S.Won,3,missed.Counts) && !S.ValidReceipt(15,3,S.Active,0,S.Required) && !S.ValidReceipt(19,3,5,0,S.Required),"Invalid receipt accepted");
        Check(!S.ValidReceipt(19,1,S.Active,0,S.Required) && !S.ValidReceipt(19,3,S.Active,0,new byte[10]),"Future-wave/truncated receipt accepted");
        return "PASS: 30 unique targets, duplicate/cross-slot dedup, missing target failure, exact class/wave/tier, admission, inclusive 200m and larger ranges, terminal receipt guards.";
    }
    public static string Receiver()
    {
        UnityEngine.Debug.unityLogger.logHandler=new OfflineLog();
        var q=Raw<Quest>(); q.ID="PZAECDefenseT19"; q.CurrentPhase=1;
        Check(!S.BeforeSharedKill(q),"Native name-only shared credit not blocked");
        var ordinary=Raw<Quest>(); ordinary.ID="PZAECChallengeT19";
        Check(S.BeforeSharedKill(ordinary),"Three-champion sharing changed");
        AccessTools.Field(typeof(Quest),"_currentState").SetValue(q,Quest.QuestState.InProgress);
        q.Objectives=new List<BaseObjective>();
        for(int slot=0;slot<11;slot++) {
            var o=Raw<ObjectiveProbe>(); o.ID=S.Target(19,slot); o.Phase=(byte)S.WaveFor(slot); o.OwnerQuest=q; q.Objectives.Add(o);
        }
        S.ApplyCounts(q,18,1,S.Active,S.Required);
        S.ApplyCounts(q,19,1,S.Failed,S.Required);
        Check(q.Objectives.All(o=>o.CurrentValue==0),"Foreign/failed receipt counted");
        // Actual receiver with only Unity display/Refresh replaced. No world or corpse exists.
        var counts=new byte[11]; counts[0]=1;
        S.ApplyCounts(q,19,1,S.Active,counts); S.ApplyCounts(q,19,1,S.Active,counts);
        Check(q.Objectives[0].CurrentValue==1,"Duplicate snapshot counted twice");
        S.ApplyCounts(q,19,1,S.Active,new byte[11]);
        Check(q.Objectives[0].CurrentValue==1,"Older counts rolled progress back");
        for(int slot=0;slot<3;slot++) counts[slot]=S.Required[slot];
        S.ApplyCounts(q,19,2,S.Active,counts);
        Check(q.CurrentPhase==2,"First wave did not advance");
        for(int slot=3;slot<7;slot++) counts[slot]=S.Required[slot];
        S.ApplyCounts(q,19,3,S.Active,counts);
        Check(q.CurrentPhase==3,"Second wave did not advance");
        S.ApplyCounts(q,19,3,S.Active,S.Required);
        Check(q.CurrentPhase==3 && q.Objectives.Where(o=>o.Phase==3).All(o=>!o.Complete),"Final auto-reward before server Won");
        S.ApplyCounts(q,19,3,S.Won,S.Required); S.ApplyCounts(q,19,3,S.Won,S.Required);
        Check(q.CurrentPhase==4 && q.Objectives.All(o=>o.Complete),"Winner snapshot did not complete exactly once");
        Check(!S.AuthorizeCompletion(q,out _),"No authoritative local state but completion authorized");
        var localType=typeof(S).GetNestedType("LocalState",BindingFlags.NonPublic);
        var state=Activator.CreateInstance(localType,true);
        var locals=(IDictionary)AccessTools.Field(typeof(S),"Locals").GetValue(null); locals.Add(q,state);
        localType.GetField("Received").SetValue(state,true); localType.GetField("Rank").SetValue(state,3);
        Check(!S.AuthorizeCompletion(q,out _),"Active state authorized");
        localType.GetField("Status").SetValue(state,S.Won);
        Check(S.AuthorizeCompletion(q,out int rank) && rank==3,"Server winner rank lost");
        localType.GetField("Settled").SetValue(state,true);
        Check(!S.AuthorizeCompletion(q,out _) && S.IsSettled(q),"Settled reward replay");
        localType.GetField("Settled").SetValue(state,false);
        q.Objectives[0].ObjectiveState=(BaseObjective.ObjectiveStates)0;
        Check(!S.AuthorizeCompletion(q,out _),"Incomplete objectives authorized");
        locals.Remove(q);
        return "PASS: actual corpse-independent cumulative receiver, duplicate/stale receipts, three wave transitions, server-only completion/rank, settled replay and missing objective guards (Unity endpoints stubbed).";
    }
    public static void Header(NetPackage p,PooledBinaryWriter writer) { ((BinaryWriter)writer).Write((ushort)321); }
    static Action<T,PooledBinaryWriter> Writer<T>(Func<MethodInfo,ILGenerator,List<CodeInstruction>> readIL)
    {
        var dm=new DynamicMethod("DefenseWrite",typeof(void),new[]{typeof(T),typeof(PooledBinaryWriter)}); var il=dm.GetILGenerator();
        foreach(var ins in readIL(typeof(T).GetMethod("write"),il)) {
            if(ins.operand is MethodInfo m) il.Emit(ins.opcode,m.DeclaringType==typeof(NetPackage) && m.Name=="write" ? typeof(DefenseSharingRegression).GetMethod("Header") : m);
            else if(ins.operand is FieldInfo f) il.Emit(ins.opcode,f);
            else if(ins.operand==null) il.Emit(ins.opcode);
            else throw new Exception("Unexpected packet IL: "+ins);
        }
        return (Action<T,PooledBinaryWriter>)dm.CreateDelegate(typeof(Action<T,PooledBinaryWriter>));
    }
    public static string Packets(Func<MethodInfo,ILGenerator,List<CodeInstruction>> readIL)
    {
        var requestWrite=Writer<NetPackagePZAECDefenseRequest>(readIL); var stateWrite=Writer<NetPackagePZAECDefenseState>(readIL); int n=0;
        foreach(int code in new[]{int.MinValue,-42,1,int.MaxValue}) for(int tier=16;tier<=19;tier++) {
            using(var stream=new MemoryStream()) {
                var w=new PooledBinaryWriter(); w.SetBaseStream(stream);
                var sent=new NetPackagePZAECDefenseRequest().Setup(S.Pulse,42,code,tier); requestWrite(sent,w); w.Flush(); stream.Position=0;
                var r=new PooledBinaryReader(); r.SetBaseStream(stream); Check(r.ReadUInt16()==321,"Request header");
                var got=new NetPackagePZAECDefenseRequest(); got.read(r);
                Check(got.Owner==42 && got.Code==code && got.Op==S.Pulse && got.Tier==tier,"Request payload");
                Check(stream.Length==sent.GetLength()+2 && stream.Position==stream.Length && got.ReliableDelivery && got.PackageDirection==NetPackageDirection.ToServer,"Request wire contract");
            }
            using(var stream=new MemoryStream()) {
                var counts=(byte[])S.Required.Clone();
                var sent=new NetPackagePZAECDefenseState().Setup(43,42,code,tier,3,S.Won,2,9,new UnityEngine.Vector3(1,2,3),500,counts);
                counts[0]=0; Check(sent.Counts[0]==4,"Async packet array alias");
                var w=new PooledBinaryWriter(); w.SetBaseStream(stream); stateWrite(sent,w); w.Flush(); stream.Position=0;
                var r=new PooledBinaryReader(); r.SetBaseStream(stream); Check(r.ReadUInt16()==321,"State header");
                var got=new NetPackagePZAECDefenseState(); got.read(r);
                Check(got.Recipient==43 && got.Owner==42 && got.Code==code && got.Tier==tier && got.Wave==3 && got.Status==S.Won && got.Rank==2 && got.Revision==9 && got.Range==500 && got.Anchor.y==2 && got.Counts.SequenceEqual(S.Required),"State payload");
                Check(stream.Length==sent.GetLength()+2 && stream.Position==stream.Length && got.ReliableDelivery && got.PackageDirection==NetPackageDirection.ToClient,"State wire contract");
            }
            n++;
        }
        // Catch Harmony parameter-binding errors before loading the mod in Unity.
        Check(typeof(S).GetMethod("PlayerTick").GetParameters().Single().Name=="__instance","Player tick not bound to instance");
        using(var mod=Mono.Cecil.ModuleDefinition.ReadModule(typeof(S).Assembly.Location)) {
            var helper=mod.Types.Single(t=>t.Name=="LegendaryDefenseSharing");
            foreach(string hook in new[]{"AfterSpawn","AfterAwardKill","ServerTick","PlayerTick","BeforeSharedKill"})
                Check(helper.Methods.Single(m=>m.Name=="Install").Body.Instructions.Any(i=>i.Operand is string s && s==hook),"Missing hook "+hook);
            var request=mod.Types.Single(t=>t.Name=="NetPackagePZAECDefenseRequest");
            foreach(string field in new[]{"loginDone","bAttachedToEntity","entityId"})
                Check(request.Methods.Single(m=>m.Name=="ProcessPackage").Body.Instructions.Any(i=>i.Operand is Mono.Cecil.FieldReference f && f.Name==field),"Unauthenticated request field "+field);
            Check(!request.Fields.Any(f=>new[]{"Counts","Rank","Status","Victim"}.Contains(f.Name)),"Client can submit kill/rank state");
            Check(helper.Methods.Single(m=>m.Name=="ApplyCounts").Body.Instructions.All(i=>!(i.Operand is Mono.Cecil.MethodReference m) || m.Name!="GetEntity"),"Receiver depends on victim entity");
            var defense=mod.Types.Single(t=>t.Name=="LegendaryDefense");
            foreach(string guard in new[]{"AuthorizeCompletion","IsSettled","Closed"})
                Check(defense.Methods.Single(m=>m.Name=="BeforeClose").Body.Instructions.Any(i=>i.Operand is Mono.Cecil.MethodReference m && m.Name==guard),"Missing completion guard "+guard);
            Check(helper.Methods.Single(m=>m.Name=="Closed").Body.Instructions.Any(i=>i.Operand is Mono.Cecil.FieldReference f && f.Name=="SettledCodes"),"No accepted-quest replay tombstone");
        }
        return "PASS: "+n+" actual paired request/state write-read cases; reliability/direction, snapshot copy, sender authentication, registered hooks and completion guards (not a live handshake).";
    }
}
'@
[DefenseSharingRegression]::Ledgers()
[DefenseSharingRegression]::Receiver()
[DefenseSharingRegression]::Packets({ param($method,$il) [LegendaryNetworkingRegression]::ReadGameIL($method,$il) })
$localization = Import-Csv (Join-Path $modRoot '99-AEC_T16_RuntimeFix/Config/Localization.csv')
foreach ($suffix in @('Late','Missed','Failed','Unavailable','Outside','Near','Start','Safe','Complete')) {
    $entry = @($localization | Where-Object Key -eq "PZAECDefenseShare$suffix")
    if ($entry.Count -ne 1) { throw "Missing/duplicate defense notice: $suffix" }
    foreach ($language in @('english','schinese','tchinese')) {
        if (!$entry[0].$language) { throw "Missing $language for $suffix" }
        $null = $entry[0].$language -f 200,201
    }
}
'PASS: all English/Chinese sharing, failure and reward notices have valid format placeholders.'
