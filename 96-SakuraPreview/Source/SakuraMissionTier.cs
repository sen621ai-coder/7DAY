namespace SakuraPreview
{
    // Match the installed AEC high-tier trader / world progression thresholds.
    public static class SakuraMissionTier
    {
        public static int ForGameStage(int stage)
        {
            if(stage>=480000)return 19;
            if(stage>=380000)return 18;
            if(stage>=280000)return 17;
            return stage>=180000?16:0;
        }
        public static bool CanOffer(int stage,int tier)=>tier>=16&&tier<=19&&ForGameStage(stage)==tier;
    }
}
public sealed class DialogRequirementSakuraTier : BaseDialogRequirement
{
    public DialogRequirementSakuraTier(){RequirementVisibilityType=RequirementVisibilityTypes.Hide;}
    public override bool CheckRequirement(EntityPlayer player,EntityNPC respondent)
    {
        int tier;if(player==null || !(respondent is SakuraPreview.EntitySakura) || !int.TryParse(ID,out tier))return false;
        var quest=player.QuestJournal?.quests.Find(q=>q.QuestGiverID==respondent.entityId &&
            q.CurrentState!=Quest.QuestState.Completed && q.CurrentState!=Quest.QuestState.Failed &&
            q.DataVariables.TryGetValue("sakuraSearching",out var value) && value=="1");
        // QuestClass.NewClass normalizes IDs to lowercase, including existing saves.
        return tier==0?quest==null:quest!=null && string.Equals(quest.QuestClass.ID,
            (((SakuraPreview.EntitySakura)respondent).IsGuardian?"mintGuardT":"sakuraEscortT")+tier,
            System.StringComparison.OrdinalIgnoreCase);
    }
    public override BaseDialogRequirement Clone()=>new DialogRequirementSakuraTier{ID=ID,Value=Value,Tag=Tag,Owner=Owner,RequirementVisibilityType=RequirementVisibilityType};
}
public sealed class DialogRequirementSakuraClaim : BaseDialogRequirement
{
    public DialogRequirementSakuraClaim(){RequirementVisibilityType=RequirementVisibilityTypes.Hide;}
    public override bool CheckRequirement(EntityPlayer player,EntityNPC respondent)
    {
        var npc=respondent as SakuraPreview.EntitySakura;
        if(player?.QuestJournal==null || npc==null)return false;
        string prefix=npc.IsGuardian?"mintGuardT":"sakuraEscortT";
        return player.QuestJournal.quests.Exists(q=>q.QuestGiverID==npc.entityId &&
            q.CurrentState!=Quest.QuestState.Completed && q.CurrentState!=Quest.QuestState.Failed &&
            q.DataVariables.TryGetValue("sakuraClaimable",out var value) && value=="1" &&
            (string.Equals(q.QuestClass.ID,prefix+16,System.StringComparison.OrdinalIgnoreCase) ||
             string.Equals(q.QuestClass.ID,prefix+17,System.StringComparison.OrdinalIgnoreCase) ||
             string.Equals(q.QuestClass.ID,prefix+18,System.StringComparison.OrdinalIgnoreCase) ||
             string.Equals(q.QuestClass.ID,prefix+19,System.StringComparison.OrdinalIgnoreCase)));
    }
    public override BaseDialogRequirement Clone()=>new DialogRequirementSakuraClaim{ID=ID,Value=Value,Tag=Tag,Owner=Owner,RequirementVisibilityType=RequirementVisibilityType};
}
