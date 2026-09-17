using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;

namespace SakuraPreview
{
    // Native quests and their completion run on the owning client. Only the
    // server allocates encounters; retries use the original native quest code.
    public static class SakuraTraderRescue
    {
        static float next;
        static World lastWorld;
        static readonly Dictionary<string,float> lastCheck=new Dictionary<string,float>();
        static readonly Dictionary<string,float> waitingSince=new Dictionary<string,float>();
        static readonly Dictionary<string,float> lastNotice=new Dictionary<string,float>();
        static readonly Dictionary<string,byte> lastStatus=new Dictionary<string,byte>();
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
        public static int DispatchTier(string id)=>SakuraDispatchPolicy.Tier(id);
        public static bool IsDispatch(string id)=>SakuraDispatchPolicy.IsDispatch(id);
        static string RequestKey(Quest q)=>q.QuestClass.ID.ToLowerInvariant()+":"+q.QuestCode;
        static void AfterStart(Quest __instance)
        {
            if(__instance?.QuestClass==null || !IsDispatch(__instance.QuestClass.ID) ||
               __instance.CurrentState==Quest.QuestState.Failed)return;
            if(!__instance.DataVariables.ContainsKey(SakuraDispatchPolicy.Pending))
                __instance.DataVariables[SakuraDispatchPolicy.Pending]="1";
            if(__instance.DataVariables[SakuraDispatchPolicy.Pending]!="2")
                __instance.DataVariables["sakuraStatus"]=SakuraDispatchPolicy.StatusText(SakuraDispatchPolicy.Rejected);
            next=0;
        }
        static void BeforeClose(Quest __instance,out Quest.QuestState __state){__state=__instance.CurrentState;}
        static void AfterClose(Quest __instance,Quest.QuestState __state)
        {
            var p=__instance.OwnerJournal?.OwnerPlayer;
            if(p==null || !ShouldGrant(__instance.QuestClass.ID,__instance.QuestGiverID,p.entityId,
               __instance.SharedOwnerID,__state,__instance.CurrentState))return;
            __instance.DataVariables[SakuraDispatchPolicy.Pending]="1";
            next=0;
        }
        public static void Acknowledge(string id,int code,byte status)
        {
            if(status>SakuraDispatchPolicy.NoSite)status=SakuraDispatchPolicy.Rejected;
            var p=GameManager.Instance?.World?.GetPrimaryPlayer();if(p?.QuestJournal==null)return;
            foreach(var q in p.QuestJournal.quests.ToArray())
            {
                if(q?.QuestClass==null || !string.Equals(q.QuestClass.ID,id,StringComparison.OrdinalIgnoreCase) ||
                   q.QuestCode!=code || !IsDispatch(id))continue;
                string key=RequestKey(q);float now=Time.realtimeSinceStartup;
                bool changed=!lastStatus.TryGetValue(key,out var previous) || previous!=status;
                bool reminder=!lastNotice.TryGetValue(key,out var notice) || now-notice>=60;
                lastStatus[key]=status;
                if(changed)Log.Out("[SakuraRescue] Server dispatch status: "+id+" code="+code+" state="+status);
                q.DataVariables["sakuraStatus"]=SakuraDispatchPolicy.StatusText(status);
                if(status!=SakuraDispatchPolicy.Rejected)
                {
                    q.DataVariables[SakuraDispatchPolicy.Pending]="2";
                    if(q.CurrentState!=Quest.QuestState.Completed && q.CurrentState!=Quest.QuestState.Failed)
                        q.CloseQuest(Quest.QuestState.Completed,null);
                    waitingSince.Remove(key);
                }
                else q.DataVariables[SakuraDispatchPolicy.Pending]="1";
                if(changed || reminder)
                {
                    GameManager.ShowTooltip(p,q.DataVariables["sakuraStatus"],false,false,8f);
                    lastNotice[key]=now;
                }
            }
        }
        static void Tick()
        {
            if(Time.realtimeSinceStartup<next)return;next=Time.realtimeSinceStartup+5;
            var world=GameManager.Instance?.World;
            if(world!=lastWorld)
            {lastWorld=world;lastCheck.Clear();waitingSince.Clear();lastNotice.Clear();lastStatus.Clear();}
            var p=world?.GetPrimaryPlayer();if(p?.QuestJournal==null)return;
            foreach(var q in p.QuestJournal.quests.ToArray())
            {
                if(q?.QuestClass==null || !IsDispatch(q.QuestClass.ID) || q.CurrentState==Quest.QuestState.Failed)continue;
                string key=RequestKey(q);float now=Time.realtimeSinceStartup;
                if(lastStatus.TryGetValue(key,out var finalStatus) && finalStatus==SakuraDispatchPolicy.Spawned)continue;
                if(!q.DataVariables.TryGetValue(SakuraDispatchPolicy.Pending,out var value) || value!="2")
                {
                    if(value!="1")
                    {
                        q.DataVariables[SakuraDispatchPolicy.Pending]="1";
                        q.DataVariables["sakuraStatus"]=SakuraDispatchPolicy.StatusText(SakuraDispatchPolicy.Rejected);
                        Log.Warning("[SakuraRescue] Recovering dispatch without server receipt: "+q.QuestClass.ID+" code="+q.QuestCode);
                    }
                    value="1";
                    if(!waitingSince.ContainsKey(key))waitingSince[key]=now;
                    if(now-waitingSince[key]>=15 && (!lastNotice.TryGetValue(key,out var notice) || now-notice>=60))
                    {
                        GameManager.ShowTooltip(p,SakuraDispatchPolicy.StatusText(SakuraDispatchPolicy.Rejected),false,false,8f);
                        lastNotice[key]=now;
                    }
                }
                bool firstCheck=!lastCheck.TryGetValue(key,out var checkedAt);
                float sinceCheck=firstCheck?float.MaxValue:now-checkedAt;
                if(!SakuraDispatchPolicy.ShouldRetry(q.QuestClass.ID,false,value,sinceCheck))continue;
                lastCheck[key]=now;
                if(firstCheck)Log.Out("[SakuraRescue] Verifying dispatch registration: "+q.QuestClass.ID+" code="+q.QuestCode+" receipt="+value);
                try
                {
                    if(ConnectionManager.Instance==null)continue;
                    if(ConnectionManager.Instance.IsServer)
                        Acknowledge(q.QuestClass.ID,q.QuestCode,SakuraMissionServer.GrantRescueStatus(p,q.QuestClass.ID,q.QuestCode,q.QuestGiverID));
                    else ConnectionManager.Instance.SendToServer(NetPackageManager.GetPackage<NetPackageSakuraRescueGrant>()
                        .Setup(q.QuestClass.ID,q.QuestCode,q.QuestGiverID,false,SakuraDispatchPolicy.Rejected));
                }
                catch(Exception ex){Log.Warning("[SakuraRescue] Dispatch request retry failed: "+ex.Message);}
            }
        }
    }
    public sealed class NetPackageSakuraRescueGrant : NetPackage
    {
        string quest;int code,trader;bool ack;byte status;
        public NetPackageSakuraRescueGrant Setup(string id,int value,int giver,bool reply,byte state)
        {quest=id;code=value;trader=giver;ack=reply;status=state;return this;}
        public override void read(PooledBinaryReader r)
        {quest=r.ReadString();code=r.ReadInt32();trader=r.ReadInt32();ack=r.ReadBoolean();status=r.ReadByte();}
        public override void write(PooledBinaryWriter w)
        {base.write(w);w.Write(quest??"");w.Write(code);w.Write(trader);w.Write(ack);w.Write(status);}
        public override int GetLength()=>17+System.Text.Encoding.UTF8.GetByteCount(quest??"");
        public override void ProcessPackage(World world,GameManager manager)
        {
            if(world==null)return;
            if(world.IsRemote()){if(ack)SakuraTraderRescue.Acknowledge(quest,code,status);return;}
            if(ack || Sender==null || !Sender.bAttachedToEntity)return;
            var player=world.GetEntity(Sender.entityId) as EntityPlayer;
            byte reply=SakuraMissionServer.GrantRescueStatus(player,quest,code,trader);
            Sender.SendPackage(NetPackageManager.GetPackage<NetPackageSakuraRescueGrant>().Setup(quest,code,trader,true,reply));
        }
    }
}
