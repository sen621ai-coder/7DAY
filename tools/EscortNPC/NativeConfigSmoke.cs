using System;
using System.Linq;
using System.Xml.Linq;
public static class NativeConfigSmoke
{
 public static int Main(string[] args)
 {
  try {
   // Standalone harness has no Unity application path; supply an empty localization table.
   foreach(var field in new[]{"mDictionary","mDictionaryCaseInsensitive"})typeof(Localization).GetField(field,System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic).SetValue(null,new System.Collections.Generic.Dictionary<string,string[]>());
   foreach(int tier in new[]{16,17,18,19})foreach(int area in new[]{1,2,3,4,5})foreach(string suffix in new[]{"","_infested","_fetch","_hunter","_bulwark","_storm"})
    if(SakuraPreview.SakuraTraderRescue.Tier("aec_quest_T"+tier+"_A"+area+"_clear"+suffix)!=tier)throw new Exception("Trader rescue tier/variant mismatch");
   foreach(string id in new[]{"aec_quest_T15_A1_clear","aec_quest_T20_A1_clear","sakuraEscortT16","mintGuardT19","aec_quest_T16_A6_clear","aec_quest_T16_A1_clear_fake","aec_quest_T16_A1_clear\n"})
    if(SakuraPreview.SakuraTraderRescue.Tier(id)!=0)throw new Exception("Non-trader or invalid quest granted rescue");
   if(!SakuraPreview.SakuraTraderRescue.ShouldGrant("aec_quest_T16_A1_clear",1,7,-1,Quest.QuestState.ReadyForTurnIn,Quest.QuestState.Completed))throw new Exception("Turn-in should grant rescue");
   foreach(var before in new[]{Quest.QuestState.Completed,Quest.QuestState.Failed})
    if(SakuraPreview.SakuraTraderRescue.ShouldGrant("aec_quest_T16_A1_clear",1,7,-1,before,Quest.QuestState.Completed))throw new Exception("Repeated/failed close granted rescue");
   if(SakuraPreview.SakuraTraderRescue.ShouldGrant("aec_quest_T16_A1_clear",1,7,8,Quest.QuestState.ReadyForTurnIn,Quest.QuestState.Completed) ||
      SakuraPreview.SakuraTraderRescue.ShouldGrant("aec_quest_T16_A1_clear",1,7,-1,Quest.QuestState.InProgress,Quest.QuestState.Failed) ||
      SakuraPreview.SakuraTraderRescue.ShouldGrant("aec_quest_T16_A1_clear",-1,7,-1,Quest.QuestState.ReadyForTurnIn,Quest.QuestState.Completed))throw new Exception("Shared, failed or non-trader trigger");
   Console.WriteLine("PASS: 120 trader contracts, invalid IDs, turn-in transitions and shared-copy exclusion");
   foreach(int tier in new[]{16,17,18,19})foreach(string kind in new[]{"sakura","mint"})
    if(SakuraPreview.SakuraTraderRescue.DispatchTier(kind+"DispatchT"+tier)!=tier)throw new Exception("Blueprint tier mismatch");
   foreach(string id in new[]{"sakuraDispatch","mintDispatchT20","mintDispatchT16extra","aec_quest_T16_A1_clear"})if(SakuraPreview.SakuraTraderRescue.IsDispatch(id))throw new Exception("Invalid dispatch");
   foreach(int tier in new[]{16,17,18,19}){
    var expected=new[]{"PZAECChallengeVoucherT"+tier,"sakuraMissionBlueprintT"+tier,"mintMissionBlueprintT"+tier};
    for(int roll=0;roll<3;roll++)if(SakuraPreview.SakuraVoucherRewards.Select(tier,roll)!=expected[roll])throw new Exception("Three-way voucher choice mismatch");
   }
   Console.WriteLine("PASS: three-champions / escort / guard fixed-tier voucher selection");
   int dialogs=0,objectives=0,actions=0;
   foreach(var d in XDocument.Load(args[0]+"/dialogs.xml").Descendants("dialog")) {
    var parsed=DialogFromXml.ParseDialog(d);
    if(parsed==null)throw new Exception("Null dialog");
    var responseIds=new System.Collections.Generic.HashSet<string>(d.Elements("response").Select(r=>(string)r.Attribute("id")));
    var statementIds=new System.Collections.Generic.HashSet<string>(d.Elements("statement").Select(r=>(string)r.Attribute("id")));
    foreach(var entry in d.Descendants("response_entry"))if(!responseIds.Contains((string)entry.Attribute("id")))throw new Exception("Dangling response");
    foreach(var next in d.Descendants().Attributes("nextstatementid"))if(!statementIds.Contains(next.Value))throw new Exception("Dangling statement");
    foreach(var a in d.Descendants("action")){
      var action=DialogFromXml.ParseAction(a);
      if(!(action is DialogActionSakura)||!(action.Clone() is DialogActionSakura))throw new Exception("Action resolution/clone failed");actions++;
    }
    int gateCount=0;
    foreach(var node in d.Descendants("requirement")){
      var gate=DialogFromXml.ParseRequirement(node);
      if(!(gate is DialogRequirementSakuraTier)||gate.RequirementVisibilityType!=BaseDialogRequirement.RequirementVisibilityTypes.Hide||gate.Clone().ID!=gate.ID)throw new Exception("Tier gate resolution/visibility/clone failed");
      gateCount++;
    }
    if(gateCount!=5)throw new Exception("Expected four gated tiers and one gated locked notice");
    dialogs++;
   }
   foreach(var q in XDocument.Load(args[0]+"/quests.xml").Descendants("quest")){
    QuestsFromXml.ParseQuest(q);
    var qc=QuestClass.s_Quests[(string)q.Attribute("id")];
    foreach(var o in q.Elements("objective")){
      objectives++;
    }
    if(qc.Shareable || qc.AllowRemove || qc.AddsToTierComplete || qc.Rewards.Count!=0)throw new Exception("Native quest must not share, remove or award independently");
    if(qc.Objectives.Count!=4)throw new Exception("Expected four native tracker rows");
    foreach(var obj in qc.Objectives){var clone=obj.Clone();if(!(clone is ObjectiveSakuraMission)||clone.ID!=obj.ID)throw new Exception("Objective clone failed");}
   }
   var stages=new[]{-1,0,179999,180000,279999,280000,379999,380000,479999,480000,int.MaxValue};
   var expectedTiers=new[]{0,0,0,16,16,17,17,18,18,19,19};
   for(int i=0;i<stages.Length;i++){
     if(SakuraPreview.SakuraMissionTier.ForGameStage(stages[i])!=expectedTiers[i])throw new Exception("Tier boundary failed");
     int visible=0;
     for(int t=16;t<=19;t++)if(SakuraPreview.SakuraMissionTier.CanOffer(stages[i],t)){visible++;if(t!=expectedTiers[i])throw new Exception("Wrong tier exposed");}
     if(visible!=(expectedTiers[i]==0?0:1))throw new Exception("More than one difficulty exposed");
     if(SakuraPreview.SakuraMissionTier.CanOffer(stages[i],0)||SakuraPreview.SakuraMissionTier.CanOffer(stages[i],20))throw new Exception("Invalid tier accepted");
   }
   Console.WriteLine("PASS: tier thresholds, single-tier offers, locked notice and native requirement parsing");
   var friendly=typeof(SakuraPreview.SakuraFriendlyProtection).GetMethod("Friendly",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
   foreach(var type in new[]{typeof(EntityPlayer),typeof(SakuraPreview.EntitySakura),typeof(EntityTurret),typeof(EntityDrone),typeof(EntityZombie),typeof(EntityAnimalRabbit)}) {
     var entity=(Entity)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
     bool expected=type!=typeof(EntityZombie)&&type!=typeof(EntityAnimalRabbit);
     if((bool)friendly.Invoke(null,new object[]{entity})!=expected)throw new Exception("Friendly damage classification failed: "+type.Name);
     bool ignored=false;bool runOriginal=SakuraPreview.SakuraFriendlyProtection.IgnoreCompanion(entity,ref ignored);
     if(type==typeof(SakuraPreview.EntitySakura)?runOriginal||!ignored:!runOriginal)throw new Exception("Turret target filter changed an unrelated entity");
   }
   if(ReflectionHelpers.GetTypeWithPrefix("XUiC_","SakuraStatement, Sakura.Preview")!=typeof(XUiC_SakuraStatement))throw new Exception("Statement controller resolution failed");
   Console.WriteLine("PASS: friendly/enemy classification, turret target filter and native statement controller");
   Console.WriteLine("NATIVE CONFIG PASS: "+dialogs+" dialogs, "+actions+" actions, "+objectives+" objectives");return 0;
  }catch(Exception ex){Console.WriteLine(ex);return 1;}
 }
}






