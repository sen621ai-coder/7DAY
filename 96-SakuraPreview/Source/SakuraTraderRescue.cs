using System;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;

namespace SakuraPreview
{
    // Native quests and their completion run on the owning client. Only the
    // server allocates encounters; retries use the original native quest code.
    public static class SakuraTraderRescue
    {
        const string Pending="sakuraRescuePending_v1";
        static float next;
        static readonly Regex Pattern=new Regex(@"\Aaec_quest_T(16|17|18|19)_A[1-5]_clear(?:_(?:infested|fetch|hunter|bulwark|storm))?\z",RegexOptions.CultureInvariant);
        public static int Tier(string id){var m=Pattern.Match(id??"");return m.Success?int.Parse(m.Groups[1].Value):0;}
        public static bool ShouldGrant(string id,int giver,int local,int shared,Quest.QuestState before,Quest.QuestState after)
        {
            return Tier(id)!=0 && giver>0 && local>=0 && (shared<0 || shared==local) &&
                before!=Quest.QuestState.Completed && before!=Quest.QuestState.Failed && after==Quest.QuestState.Completed;
        }
        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(Quest),nameof(Quest.StartQuest),new[]{typeof(bool),typeof(bool)}),
                postfix:new HarmonyMethod(typeof(SakuraTraderRescue),nameof(AfterStart)));
            harmony.Patch(AccessTools.Method(typeof(GameManager),"Update"),
                postfix:new HarmonyMethod(typeof(SakuraTraderRescue),nameof(Tick)));
        }
        public static int DispatchTier(string id)
        {var m=Regex.Match(id??"",@"\A(?:sakura|mint)DispatchT(16|17|18|19)\z");return m.Success?int.Parse(m.Groups[1].Value):0;}
        public static bool IsDispatch(string id)=>DispatchTier(id)!=0;
        static void AfterStart(Quest __instance)
        {
            if(!IsDispatch(__instance.QuestClass.ID) || __instance.OwnerJournal?.OwnerPlayer==null ||
               __instance.CurrentState==Quest.QuestState.Completed || __instance.CurrentState==Quest.QuestState.Failed)return;
            if(!__instance.DataVariables.ContainsKey(Pending))__instance.DataVariables[Pending]="1";
            __instance.DataVariables["sakuraStatus"]="正在请求服务器安排任务地点（已有任务时排队）";
            next=0;
        }
        static void BeforeClose(Quest __instance,out Quest.QuestState __state){__state=__instance.CurrentState;}
        static void AfterClose(Quest __instance,Quest.QuestState __state)
        {
            var p=__instance.OwnerJournal?.OwnerPlayer;
            if(p==null || !ShouldGrant(__instance.QuestClass.ID,__instance.QuestGiverID,p.entityId,
               __instance.SharedOwnerID,__state,__instance.CurrentState))return;
            __instance.DataVariables[Pending]="1";
            next=0;
        }
        public static void Acknowledge(string id,int code)
        {
            var p=GameManager.Instance?.World?.GetPrimaryPlayer();if(p?.QuestJournal==null)return;
            foreach(var q in p.QuestJournal.quests.ToArray())
                if(q.QuestClass.ID==id && q.QuestCode==code && q.DataVariables.TryGetValue(Pending,out var value) && value=="1")
                {q.DataVariables[Pending]="2";if(IsDispatch(id))q.CloseQuest(Quest.QuestState.Completed,null);GameManager.ShowTooltip(p,"任务图纸已受理，正在安排坐标；已有同伴任务时排队。请在开阔地稍候。",false,false,8f);}
        }
        static void Tick()
        {
            if(Time.realtimeSinceStartup<next)return;next=Time.realtimeSinceStartup+5;
            var p=GameManager.Instance?.World?.GetPrimaryPlayer();if(p?.QuestJournal==null)return;
            foreach(var q in p.QuestJournal.quests.ToArray())
            {
                if(!IsDispatch(q.QuestClass.ID) || q.CurrentState==Quest.QuestState.Failed || q.CurrentState==Quest.QuestState.Completed || !q.DataVariables.TryGetValue(Pending,out var value) || value!="1")continue;
                if(ConnectionManager.Instance.IsServer)
                {if(SakuraMissionServer.GrantRescue(p,q.QuestClass.ID,q.QuestCode,q.QuestGiverID))Acknowledge(q.QuestClass.ID,q.QuestCode);}
                else ConnectionManager.Instance.SendToServer(NetPackageManager.GetPackage<NetPackageSakuraRescueGrant>().Setup(q.QuestClass.ID,q.QuestCode,q.QuestGiverID,false));
            }
        }
    }
    public sealed class NetPackageSakuraRescueGrant : NetPackage
    {
        string quest;int code,trader;bool ack;
        public NetPackageSakuraRescueGrant Setup(string id,int value,int giver,bool reply){quest=id;code=value;trader=giver;ack=reply;return this;}
        public override void read(PooledBinaryReader r){quest=r.ReadString();code=r.ReadInt32();trader=r.ReadInt32();ack=r.ReadBoolean();}
        public override void write(PooledBinaryWriter w){base.write(w);w.Write(quest??"");w.Write(code);w.Write(trader);w.Write(ack);}
        public override int GetLength()=>16+System.Text.Encoding.UTF8.GetByteCount(quest??"");
        public override void ProcessPackage(World world,GameManager manager)
        {
            if(world==null)return;
            if(world.IsRemote()){if(ack)SakuraTraderRescue.Acknowledge(quest,code);return;}
            if(ack || Sender==null || !Sender.bAttachedToEntity)return;
            var player=world.GetEntity(Sender.entityId) as EntityPlayer;
            if(SakuraMissionServer.GrantRescue(player,quest,code,trader))
                Sender.SendPackage(NetPackageManager.GetPackage<NetPackageSakuraRescueGrant>().Setup(quest,code,trader,true));
        }
    }
}
