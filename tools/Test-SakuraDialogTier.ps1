#Requires -Version 7.0
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$source=Get-Content (Join-Path $root '96-SakuraPreview/Source/SakuraMissionTier.cs') -Raw
# Compile the actual dialogue requirement against minimal game-state fixtures.
$fixtures=@'
public class EntityNPC { public int entityId; }
public class EntityPlayer { public QuestJournal QuestJournal = new QuestJournal(); }
public class QuestJournal { public System.Collections.Generic.List<Quest> quests = new System.Collections.Generic.List<Quest>(); }
public class QuestClass { public string ID; }
public class Quest
{
    public enum QuestState { InProgress, Completed, Failed }
    public int QuestGiverID;
    public QuestState CurrentState;
    public QuestClass QuestClass = new QuestClass();
    public System.Collections.Generic.Dictionary<string,string> DataVariables = new System.Collections.Generic.Dictionary<string,string>();
}
public abstract class BaseDialogRequirement
{
    public enum RequirementVisibilityTypes { Hide }
    public RequirementVisibilityTypes RequirementVisibilityType;
    public string ID, Value, Tag;
    public object Owner;
    public abstract bool CheckRequirement(EntityPlayer player, EntityNPC respondent);
    public abstract BaseDialogRequirement Clone();
}
namespace SakuraPreview { public class EntitySakura : EntityNPC { public bool IsGuardian; } }
public static class SakuraDialogTierRegression
{
    static int checks;
    static void Check(bool value,string name) { checks++; if(!value)throw new System.Exception(name); }
    public static string Run()
    {
        foreach(bool guard in new[]{true,false})
        foreach(int tier in new[]{16,17,18,19})
        {
            var npc=new SakuraPreview.EntitySakura{entityId=105235,IsGuardian=guard};
            var player=new EntityPlayer();
            var quest=new Quest{QuestGiverID=npc.entityId};
            quest.DataVariables["sakuraSearching"]="1";
            player.QuestJournal.quests.Add(quest);
            var gate=new DialogRequirementSakuraTier{ID=tier.ToString()};
            var locked=new DialogRequirementSakuraTier{ID="0"};
            var claim=new DialogRequirementSakuraClaim();
            string id=(guard?"mintGuardT":"sakuraEscortT")+tier;
            foreach(string variant in new[]{id.ToLowerInvariant(),id,id.ToUpperInvariant()})
            {
                quest.QuestClass.ID=variant;
                Check(gate.CheckRequirement(player,npc),"Searching mission must expose start: "+variant);
                Check(!locked.CheckRequirement(player,npc),"Existing mission must hide no-mission entry");
                for(int other=16;other<=19;other++)
                    Check(new DialogRequirementSakuraTier{ID=other.ToString()}.CheckRequirement(player,npc)==(other==tier),"Only matching tier is visible");
            }
            quest.QuestClass.ID=(guard?"sakuraescortt":"mintguardt")+tier;
            Check(!gate.CheckRequirement(player,npc),"Wrong mission type rejected");
            quest.QuestClass.ID=null;
            Check(!gate.CheckRequirement(player,npc),"Missing quest ID rejected");
            quest.QuestClass.ID=id.ToLowerInvariant();
            Check(!claim.CheckRequirement(player,npc),"Unsynchronized claim remains hidden");
            quest.DataVariables["sakuraClaimable"]="1";
            Check(claim.CheckRequirement(player,npc)&&claim.Clone().CheckRequirement(player,npc),"Server-authorized claim and clone are visible");
            quest.DataVariables["sakuraClaimable"]="0";
            Check(!claim.CheckRequirement(player,npc),"Issued or retired claim is hidden");
            quest.DataVariables["sakuraClaimable"]="1";
            quest.QuestGiverID++;
            Check(!gate.CheckRequirement(player,npc),"Other NPC cannot start this mission");
            Check(!claim.CheckRequirement(player,npc),"Other NPC cannot expose claim");
            quest.QuestGiverID=npc.entityId;
            foreach(var state in new[]{Quest.QuestState.Completed,Quest.QuestState.Failed})
            {
                quest.CurrentState=state;
                Check(!gate.CheckRequirement(player,npc),"Terminal mission cannot restart");
                Check(!claim.CheckRequirement(player,npc),"Terminal local quest cannot expose stale claim");
            }
            quest.CurrentState=Quest.QuestState.InProgress;
            quest.DataVariables["sakuraSearching"]="0";
            Check(!gate.CheckRequirement(player,npc),"Already-started mission cannot restart");
            quest.DataVariables.Clear();
            Check(!gate.CheckRequirement(player,npc),"Missing search state rejected");
            quest.DataVariables["sakuraSearching"]="1";
            Check(gate.Clone().CheckRequirement(player,npc),"Cloned requirement preserves matching");
            Check(!gate.CheckRequirement(null,npc),"Null player rejected");
            Check(!gate.CheckRequirement(player,null),"Null NPC rejected");
            Check(!gate.CheckRequirement(player,new EntityNPC{entityId=npc.entityId}),"Non-Sakura NPC rejected");
            Check(!new DialogRequirementSakuraTier{ID="invalid"}.CheckRequirement(player,npc),"Malformed tier rejected");
            player.QuestJournal.quests.Clear();
            Check(!gate.CheckRequirement(player,npc)&&locked.CheckRequirement(player,npc),"Missing mission shows only locked entry");
            player.QuestJournal=null;
            Check(!gate.CheckRequirement(player,npc),"Missing journal cannot start mission");
            Check(!claim.CheckRequirement(player,npc),"Missing journal cannot expose claim");
        }
        return "PASS: "+checks+" dialogue requirement checks (Mint/Sakura T16-T19, normalized/mixed-case IDs, NPC binding and lifecycle guards).";
    }
}
'@
Add-Type -TypeDefinition ($source+[Environment]::NewLine+$fixtures)
[SakuraDialogTierRegression]::Run()
[xml]$dialogs=Get-Content (Join-Path $root '96-SakuraPreview/Config/dialogs.xml') -Raw
foreach($id in @('sakuraNative','mintNative')){
    if($dialogs.SelectNodes("//dialog[@id='$id']/response[@id='claim']/requirement[@type='SakuraClaim, Sakura.Preview']").Count -ne 1){throw "$id claim requirement missing"}
}
'PASS: both native claim responses require server-synchronized eligibility.'
