using UnityEngine;

namespace SakuraPreview
{
    // Uses the game's dialogue window and input/focus handling. No IMGUI or cursor overrides.
    public sealed class SakuraDialog : MonoBehaviour
    {
        EntitySakura npc;
        EntityPlayerLocal player;
        readonly System.Collections.Generic.Dictionary<int,string> failures=new System.Collections.Generic.Dictionary<int,string>();
        int lastOfferTier=-1;
        int pending=-1;
        bool waiting;
        float deadline;
        public static void Open(EntitySakura target,EntityPlayerLocal user)
        {
            if(target==null || target.NPCInfo==null || !Dialog.DialogList.ContainsKey(target.NPCInfo.DialogID))
                throw new System.InvalidOperationException("Sakura native dialogue configuration is missing");
            var bridge=user.GetComponent<SakuraDialog>()??user.gameObject.AddComponent<SakuraDialog>();
            bridge.npc=target;bridge.player=user;bridge.pending=bridge.failures.ContainsKey(target.entityId)?38:-1;bridge.waiting=false;
            var ui=user.PlayerUI.xui;
            ui.Dialog.Respondent=target;ui.Dialog.ReturnStatement="";ui.Dialog.QuestTurnIn=null;
            XUiC_DialogWindowGroup.Open(ui,null);
            var group=ui.Dialog.DialogWindowGroup;
            if(group?.CurrentDialog!=null)
            {
                group.RefreshDialog();
                Log.Out("[Sakura] Native dialogue opened: "+target.NPCInfo.DialogID+" responses="+(group.CurrentDialog.CurrentStatement?.GetResponses().Count??0));
            }
        }
        public static void SetMissionFailure(int id,string reason)
        {
            var user=GameManager.Instance?.World?.GetPrimaryPlayer();if(user==null)return;
            var bridge=user.GetComponent<SakuraDialog>()??user.gameObject.AddComponent<SakuraDialog>();
            if(string.IsNullOrEmpty(reason))bridge.failures.Remove(id);else bridge.failures[id]=reason;
        }
        public static void Receive(int id,byte reply)
        {
            var player=GameManager.Instance?.World?.GetPrimaryPlayer();
            var bridge=player==null?null:player.GetComponent<SakuraDialog>();
            if(bridge!=null && bridge.npc!=null && bridge.npc.entityId==id)bridge.pending=reply;
        }
        public static void Send(EntityPlayerLocal user,EntitySakura target,byte choice)
        {
            var bridge=user.GetComponent<SakuraDialog>();
            if(bridge==null || bridge.npc!=target || bridge.waiting)return;
            bridge.waiting=true;bridge.deadline=Time.realtimeSinceStartup+8;
            if(ConnectionManager.Instance.IsServer)
            {
                byte answer=target.Handle(user,choice);bridge.pending=answer;
                if(answer<5)ConnectionManager.Instance.SendPackage(NetPackageManager.GetPackage<NetPackageSakuraReply>().Setup(target.entityId,answer,true),_entitiesInRangeOfEntity:target.entityId);
            }
            else ConnectionManager.Instance.SendToServer(NetPackageManager.GetPackage<NetPackageSakuraRequest>().Setup(target.entityId,choice));
        }
        void Update()
        {
            if(player==null)return;
            var model=player.PlayerUI.xui.Dialog;
            if(model.Respondent!=npc)return;
            if(npc==null || npc.IsDead() || player.IsDead() || (npc.position-player.position).sqrMagnitude>25)
            {player.PlayerUI.windowManager.Close("dialog");waiting=false;return;}
            if(waiting && pending<0 && Time.realtimeSinceStartup>deadline)pending=37;
            int availableTier=SakuraMissionTier.ForGameStage(player.gameStage);
            if(availableTier!=lastOfferTier && model.DialogWindowGroup?.CurrentDialog!=null)
            {lastOfferTier=availableTier;model.DialogWindowGroup.RefreshDialog();}
            if(pending<0)return;
            int reply=pending;pending=-1;waiting=false;
            var group=model.DialogWindowGroup;
            if(group?.CurrentDialog==null)return;
            // Defer until after SelectResponse assigns its next statement, including on the host.
            string failed;
            if((reply==20||reply==31||reply==35||reply==38) && failures.TryGetValue(npc.entityId,out failed))reply=38;
            var statement=group.CurrentDialog.GetStatement("reply"+reply)??group.CurrentDialog.GetStatement("reply30");
            if(reply==38 && failures.TryGetValue(npc.entityId,out failed))statement.Text="这次任务失败了："+failed+"。护送已结束，本次没有奖励。";
            group.CurrentDialog.CurrentStatement=statement;
            group.RefreshDialog();
        }
    }
}

public sealed class DialogActionSakura : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        var local=player as EntityPlayerLocal;
        var npc=local?.PlayerUI.xui.Dialog.Respondent as SakuraPreview.EntitySakura;
        byte action;
        if(local!=null && npc!=null && byte.TryParse(ID,out action))SakuraPreview.SakuraDialog.Send(local,npc,action);
    }
    public override BaseDialogAction Clone(){var copy=new DialogActionSakura();CopyValues(copy);return copy;}
}



