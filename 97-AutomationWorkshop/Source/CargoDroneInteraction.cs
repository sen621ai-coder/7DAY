using System;
using UnityEngine;
using UnityEngine.Scripting;

namespace YFAutomation.CargoDrones
{
    [DefaultExecutionOrder(-1000)]
    public sealed class CargoDroneInteraction : MonoBehaviour
    {
        CargoDroneVisual visual;
        bool focused;
        void Update()
        {
            focused=false;
            if(GameManager.IsDedicatedServer||GameManager.Instance?.World==null)return;
            if(visual==null)visual=GetComponent<CargoDroneVisual>();
            if(visual==null||visual.Hub==Guid.Empty)return;
            var player=GameManager.Instance.World.GetPrimaryPlayer();
            if(player==null||player.IsDead()||!GameManager.Instance.GameIsFocused)return;
            var ui=player.PlayerUI;
            if(ui==null||LocalPlayerUI.AnyModalWindowOpen()||ui.windowManager.IsCursorWindowOpen()||ui.windowManager.IsInputActive())return;
            var ray=player.playerCamera!=null?player.playerCamera.ViewportPointToRay(new Vector3(.5f,.5f,0)):player.GetLookRay();
            float distance;
            if(!new Bounds(transform.position,new Vector3(1.6f,1.2f,1.6f)).IntersectRay(ray,out distance)||distance>4)return;
            RaycastHit hit;if(Physics.Raycast(ray,out hit,distance,~0,QueryTriggerInteraction.Ignore))return;
            focused=true;
            if(ui.playerInput.Activate.WasPressed)
            {XUiC_YFCargoDrone.Pending=visual.Hub;ui.windowManager.Open("yfCargoDrone",true);focused=false;}
        }
        void OnGUI()
        {
            if(!focused)return;
            var style=new GUIStyle(GUI.skin.box){fontSize=20,alignment=TextAnchor.MiddleCenter};
            GUI.Box(new Rect(Screen.width/2-190,Screen.height/2+50,380,36),"[E] 货运无人机 · 状态与命令",style);
        }
    }
    [Preserve]
    public sealed class XUiC_YFCargoDrone : XUiController
    {
        public static Guid Pending;
        public static XUiC_YFCargoDrone Active;
        static int sequence;
        Guid hub;long revision;int request;
        bool ready,awaiting;float sent,nextRead;
        void Label(string name,string value){((XUiV_Label)GetChildById(name).ViewComponent).Text=value;}
        public override void Init()
        {
            base.Init();
            GetChildById("refresh").OnPress+=(s,b)=>Send(CargoHubAction.Read);
            GetChildById("recall").OnPress+=(s,b)=>Send(CargoHubAction.Recall);
            GetChildById("pause").OnPress+=(s,b)=>Send(CargoHubAction.TogglePause);
            GetChildById("close").OnPress+=(s,b)=>xui.playerUI.windowManager.Close(WindowGroup);
        }
        public override void OnOpen()
        {base.OnOpen();Active=this;hub=Pending;revision=0;ready=awaiting=false;sent=-1;Label("details","");Label("selection","");Send(CargoHubAction.Read);}
        public override void OnClose(){if(Active==this)Active=null;ready=awaiting=false;base.OnClose();}
        void Send(CargoHubAction action)
        {
            if(awaiting||Time.realtimeSinceStartup-sent<.3f||action!=CargoHubAction.Read&&!ready)return;
            var p=NetPackageManager.GetPackage<NetPackageYFCargoHubRequest>();
            p.FromDrone=true;p.At=p.Endpoint=Vector3i.zero;p.Hub=hub;p.Revision=revision;p.Action=action;p.Request=request=--sequence;
            awaiting=true;sent=Time.realtimeSinceStartup;
            if(ConnectionManager.Instance.IsServer)p.Handle(GameManager.Instance.World,xui.playerUI.entityPlayer.entityId);else ConnectionManager.Instance.SendToServer(p);
        }
        public void Receive(NetPackageYFCargoHubReply reply)
        {
            if(Active!=this||reply.Request!=request)return;
            awaiting=false;ready=reply.Allowed;nextRead=Time.realtimeSinceStartup+2;
            Label("notice",reply.Message);
            if(!ready){Label("details","");Label("selection","");return;}
            revision=reply.Revision;Label("details",reply.Details);Label("selection",reply.Destinations);Label("pauseText",reply.Paused?"恢复调度":"暂停调度");
        }
        public override void Update(float dt)
        {
            base.Update(dt);if(Active!=this)return;
            if(awaiting&&Time.realtimeSinceStartup-sent>8){awaiting=false;nextRead=Time.realtimeSinceStartup+2;Label("notice","请求超时，请刷新");}
            if(!awaiting&&Time.realtimeSinceStartup>=nextRead)Send(CargoHubAction.Read);
        }
    }
}
