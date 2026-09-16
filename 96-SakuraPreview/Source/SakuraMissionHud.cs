using System;
using System.Globalization;
using UnityEngine;

namespace SakuraPreview
{
    public sealed class NetPackageSakuraMission : NetPackage
    {
        int npc,tier,waves,enemies,health,coins,xp,receipt;
        byte phase;
        float x,z,missing;
        bool paused,claim,guard;
        string failure,missionId;
        public NetPackageSakuraMission Setup(SakuraMissionState state,EntitySakura actor,string memberKey)
        {
            guard=state.Guard;npc=state.NpcId;tier=state.Tier;waves=state.Waves;enemies=state.Enemies.Count;
            health=actor==null?0:actor.Health;coins=state.Coins;xp=state.XP;phase=(byte)state.Phase;
            x=state.TargetX;z=state.TargetZ;missing=(float)state.MissingSeconds;paused=state.Paused;
            claim=actor!=null && state.CanClaim(memberKey);failure=state.Phase==EscortPhase.Completed && actor==null?"领奖人物已退场":state.Failure;missionId=state.Id;
            receipt=state.Members.Find(m=>m.Key==memberKey)?.Receipt??0;
            if(state.Phase==EscortPhase.Completed && actor!=null){x=actor.position.x;z=actor.position.z;}
            return this;
        }
        public override void read(PooledBinaryReader r)
        {npc=r.ReadInt32();tier=r.ReadInt32();waves=r.ReadInt32();enemies=r.ReadInt32();health=r.ReadInt32();coins=r.ReadInt32();xp=r.ReadInt32();phase=r.ReadByte();x=r.ReadSingle();z=r.ReadSingle();missing=r.ReadSingle();paused=r.ReadBoolean();claim=r.ReadBoolean();failure=r.ReadString();guard=r.ReadBoolean();missionId=r.ReadString();receipt=r.ReadInt32();}
        public override void write(PooledBinaryWriter w)
        {base.write(w);w.Write(npc);w.Write(tier);w.Write(waves);w.Write(enemies);w.Write(health);w.Write(coins);w.Write(xp);w.Write(phase);w.Write(x);w.Write(z);w.Write(missing);w.Write(paused);w.Write(claim);w.Write(failure??"");w.Write(guard);w.Write(missionId??"");w.Write(receipt);}
        public override int GetLength()=>64+System.Text.Encoding.UTF8.GetByteCount(failure??"")+System.Text.Encoding.UTF8.GetByteCount(missionId??"");
        public override void ProcessPackage(World world,GameManager manager){if(world!=null&&world.IsRemote())DeliverLocal();}
        public void DeliverLocal()
        {
            var player=GameManager.Instance?.World?.GetPrimaryPlayer();
            if(player?.QuestJournal==null || string.IsNullOrEmpty(missionId) || tier<16 || tier>19)return;
            try
            {
                var journal=player.QuestJournal;
                var quest=journal.quests.Find(q=>q.DataVariables.ContainsKey("sakuraMissionId") && q.DataVariables["sakuraMissionId"]==missionId);
                bool created=quest==null;
                if(created)
                {
                    quest=QuestClass.CreateQuest((guard?"mintGuardT":"sakuraEscortT")+tier);
                    if(quest==null)throw new InvalidOperationException("Native Sakura quest class missing");
                    quest.DataVariables["sakuraMissionId"]=missionId;
                    quest.QuestGiverID=npc;
                }
                quest.DataVariables["sakuraSearching"]=phase==(byte)EscortPhase.Searching?"1":"0";
                string status=phase==(byte)EscortPhase.Searching?(guard?"前往标记地点寻找 Mint，与她对话开始守护":"前往标记地点寻找小樱，与她对话开始救援"):phase==(byte)EscortPhase.Completed?(receipt==2?"奖励已发放":receipt==1?"领奖待核对，请联系服主":claim?"已完成，请与任务人物交谈领奖":!string.IsNullOrEmpty(failure)?failure:"已完成，参与时间不足，无法领奖"):
                    phase==(byte)EscortPhase.Failed?"失败："+failure:phase==(byte)EscortPhase.Ambush?(waves==tier-14?"最终波：击败首领及护卫":"清除伏击敌人"):paused?"原地等待":guard?"保护 Mint，等待下一波":"护送小樱到目标商店外围";
                quest.DataVariables["sakuraStatus"]=status+(phase!=(byte)EscortPhase.Failed && missing>0?" · 请返回人物附近（"+Mathf.Max(0,Mathf.CeilToInt(60-missing))+" 秒）":"");
                quest.DataVariables["sakuraHealth"]=health.ToString();quest.DataVariables["sakuraWaves"]=waves+"/"+(tier-14);quest.DataVariables["sakuraEnemies"]=enemies.ToString();
                quest.DataVariables["sakuraDetails"]="生命 "+health+" · 波次 "+waves+"/"+(tier-14)+" · 敌人 "+enemies+(phase!=(byte)EscortPhase.Failed && missing>0?" · 返回倒计时 "+Mathf.Max(0,Mathf.CeilToInt(60-missing))+" 秒":"");
                quest.DataVariables["sakuraX"]=x.ToString(CultureInfo.InvariantCulture);
                quest.DataVariables["sakuraZ"]=z.ToString(CultureInfo.InvariantCulture);
                if(created){journal.AddQuest(quest,true);journal.TrackedQuest=quest;Log.Out("[Sakura] Native quest added: "+missionId);}
                foreach(var objective in quest.Objectives)if(objective is ObjectiveSakuraMission native)native.Refresh();
                SakuraDialog.SetMissionFailure(npc,phase==(byte)EscortPhase.Failed?failure:null);
                bool terminal=quest.CurrentState==Quest.QuestState.Completed||quest.CurrentState==Quest.QuestState.Failed;
                if(phase==(byte)EscortPhase.Failed)
                {
                    if(!terminal)quest.CloseQuest(Quest.QuestState.Failed,null);
                    foreach(var objective in quest.Objectives)
                    {
                        objective.ObjectiveState=BaseObjective.ObjectiveStates.Failed;
                        objective.RemoveNavObject();
                    }
                    if(journal.TrackedQuest==quest)journal.TrackedQuest=null;
                    quest.Tracked=false;
                    if(!quest.DataVariables.ContainsKey("sakuraFailureNotified"))
                    {
                        GameManager.ShowTooltip(player,(guard?"Mint 守护":"小樱护送")+" T"+tier+" 失败："+failure+"。本次无奖励，角色已离开，请寻找新的角色。",false,false,8f);
                        SakuraDialog.Receive(npc,38);
                        quest.DataVariables["sakuraFailureNotified"]="1";
                    }
                }
                else if(!terminal && phase==(byte)EscortPhase.Completed && (receipt==2 || (!claim && receipt==0)))quest.CloseQuest(Quest.QuestState.Completed,null);
            }
            catch(Exception ex){Log.Error("[Sakura] Native quest sync failed: "+ex.Message);}
        }
    }
}

// Server-owned progress: this objective never awards items or independently completes a mission.
public sealed class ObjectiveSakuraMission : BaseObjective
{
    Vector3 marker;
    public override bool UpdateUI=>true;
    public override bool useUpdateLoop=>true;
    string Data(string key,string fallback="")=>OwnerQuest!=null && OwnerQuest.DataVariables.TryGetValue(key,out var value)?value:fallback;
    public override string StatusText {get=>ID=="health"?Data("sakuraHealth"):ID=="waves"?Data("sakuraWaves"):ID=="enemies"?Data("sakuraEnemies"):"";set{}}
    public override BaseObjective Clone(){var copy=new ObjectiveSakuraMission();CopyValues(copy);return copy;}
    public override void SetupObjective(){SetupDisplay();}
    public override void SetupDisplay(){Description=ID=="health"?"保护目标生命":ID=="waves"?"抵御伏击波次":ID=="enemies"?"本波剩余敌人":Data("sakuraStatus","等待服务器进度");}
    public override void AddHooks(){Refresh();}
    public override void RemoveHooks(){RemoveNavObject();}
    public override void Update(float dt){Refresh();}
    public override void Refresh()
    {
        SetupDisplay();
        if(ID!="goal" || OwnerQuest==null || OwnerQuest.CurrentState==Quest.QuestState.Completed || OwnerQuest.CurrentState==Quest.QuestState.Failed || !OwnerQuest.Tracked)
        {RemoveNavObject();return;}
        float x,z;
        if(!float.TryParse(Data("sakuraX"),NumberStyles.Float,CultureInfo.InvariantCulture,out x)||!float.TryParse(Data("sakuraZ"),NumberStyles.Float,CultureInfo.InvariantCulture,out z))return;
        var world=GameManager.Instance?.World;if(world==null)return;
        var position=new Vector3(x,world.GetHeightAt((int)x,(int)z)+1,z);
        if(NavObject==null || (marker-position).sqrMagnitude>1)
        {RemoveNavObject();NavObjectName="return_to_trader";AddNavObject(position);if(NavObject!=null){NavObject.IsActive=true;NavObject.IsTracked=true;NavObject.name=OwnerQuest.QuestClass.Name;}marker=position;}
    }
}




