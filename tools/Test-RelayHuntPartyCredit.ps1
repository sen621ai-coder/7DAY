#Requires -Version 7.0
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Test-ModelTintFix.ps1')
Add-Type -ReferencedAssemblies ($references + $frameworkReferences) -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Linq;
using HarmonyLib;
using AECT16RuntimeFix;
public static class RelayHuntCreditRegression {
    static void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
    static T Raw<T>() { return (T)RuntimeHelpers.GetUninitializedObject(typeof(T)); }
    public sealed class Probe : ObjectiveEntityKill {
        public int Calls;
        public override void SetupDisplay() { }
        public override void Refresh() { Calls++; ObjectiveState=ObjectiveStates.Complete; }
    }
    public static string Run() {
        int tested=0;
        foreach(int tier in new[]{16,17,18,19}) foreach(string family in new[]{"Dumdum","Executioner","Mechanician"}) {
            string id="PZAECRelayHunt"+family+"T"+tier;
            string target=RelayHuntPartyCredit.TargetForQuest(id);
            Check(target!=null,"Missing exact hunt mapping");
            foreach(bool active in new[]{false,true}) foreach(int phase in new[]{1,2,3,4,5}) foreach(int owner in new[]{-1,43,44}) foreach(bool complete in new[]{false,true}) {
                bool expected=active && phase==4 && owner!=44 && !complete;
                Check(RelayHuntPartyCredit.Eligible(id,target,active,phase,owner,43,complete)==expected,"Eligibility mismatch"); tested++;
            }
            Check(!RelayHuntPartyCredit.Eligible(id,target+"Wrong",true,4,-1,43,false),"Wrong tier/family accepted");
            var q=Raw<Quest>(); q.ID=id; q.SharedOwnerID=-1;
            AccessTools.Field(typeof(Quest),"_currentState").SetValue(q,Quest.QuestState.InProgress);
            q.CurrentPhase=4; q.Requirements=new List<Quests.Requirements.BaseRequirement>();
            var objective=Raw<Probe>(); objective.ID=target; objective.Phase=4; objective.OwnerQuest=q;
            q.Objectives=new List<BaseObjective>{objective};
            // No player, corpse, world or network entity exists: run the actual receiver.
            RelayHuntPartyCredit.Credit(q,43,target+"Wrong"); Check(objective.CurrentValue==0,"Wrong victim counted");
            RelayHuntPartyCredit.Credit(q,43,target);
            RelayHuntPartyCredit.Credit(q,43,target);
            Check(objective.CurrentValue==1 && objective.Calls==1,"Duplicate or absent credit");
        }
        foreach(string id in new[]{"PZAECChallengeT16","PZAECDefenseT16","PZAECRelayHuntDumdumT15","PZAECRelayHuntDumdumT20","PZAECRelayHuntDumdumT16_extra"})
            Check(RelayHuntPartyCredit.TargetForQuest(id)==null,"Unrelated quest accepted");
        var method=AccessTools.Method(typeof(GameManager),"SharedKillClient",new[]{typeof(int),typeof(int),typeof(EntityPlayerLocal),typeof(int),typeof(int)});
        Check(method!=null,"Native party kill signature changed");
        using(var module=Mono.Cecil.ModuleDefinition.ReadModule(method.Module.FullyQualifiedName)) {
            var native=(Mono.Cecil.MethodDefinition)module.LookupToken(method.MetadataToken);
            Check(native.Body.Instructions.Any(i=>i.Operand!=null && i.Operand.ToString().Contains("QuestEventManager::EntityKilled")),"Native kill flow changed");
            var packet=module.Types.Single(t=>t.Name=="NetPackageSharedPartyKill");
            Check(packet.Fields.Any(f=>f.Name=="entityTypeID"),"Native packet no longer carries victim class");
        }
        return "PASS: "+tested+" scope cases; 12 actual corpse-free objective credits; duplicate suppression; unrelated quest isolation; native party notification signature/payload (offline; UI refresh stubbed).";
    }
}
'@
[RelayHuntCreditRegression]::Run()
