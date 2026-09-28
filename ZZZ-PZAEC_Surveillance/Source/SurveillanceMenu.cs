using System;
using System.Linq;
using UnityEngine;

namespace PZAEC.Surveillance
{
    public sealed class SurveillanceMenu : MonoBehaviour
    {
        sealed class SettingsWindow : GUIWindow
        {public SettingsWindow():base("pzaecSurveillanceSettings") {}}
        static SurveillanceMenu instance;
        Vector3i screen;
        EntityPlayerLocal player;
        int channel;
        Vector2 scroll;
        string message="选择摄像头后即可无线绑定，不需要视频线。";
        CursorLockMode previousLock;
        bool previousVisible;
        PlayerMoveController controls;
        bool previousInput;
        float openedAt;
        GUIWindow modal;
        GUIWindowManager windows;
        public static void Open(Vector3i position,EntityPlayerLocal owner)
        {
            if(GameManager.Instance==null||owner==null)return;
            if(instance==null)instance=GameManager.Instance.gameObject.AddComponent<SurveillanceMenu>();
            if(instance.player!=null)instance.CloseInstance();
            instance.screen=position;instance.player=owner;var state=SurveillanceClient.At(position);instance.channel=state==null?0:Mathf.Clamp(state.Selected,0,3);
            instance.previousLock=Cursor.lockState;instance.previousVisible=Cursor.visible;instance.enabled=true;instance.message="选择摄像头后即可无线绑定，不需要视频线。";
            instance.openedAt=Time.realtimeSinceStartup;
            var ui=LocalPlayerUI.GetUIForPlayer(owner);
            instance.windows=ui.windowManager;
            instance.modal=new SettingsWindow{playerUI=ui,alwaysUsesMouseCursor=true,isInputActive=true};
            instance.modal.OnWindowClose=instance.CloseInstance;
            instance.windows.Open(instance.modal,true);
            instance.controls=owner.MoveController;
            if(instance.controls!=null){instance.previousInput=instance.controls.bAllowPlayerInput;instance.controls.AllowPlayerInput(false);owner.ClearMovementInputs();}
            Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
        }
        public static void Close(){if(instance!=null)instance.CloseInstance();}
        public static void Result(Vector3i position,bool accepted,string text)
        {
            if(instance==null||!instance.enabled||instance.screen!=position)return;
            instance.message=(accepted?"✓ ":"⚠ ")+(text??"");
        }
        void CloseInstance()
        {
            if(player==null&&modal==null&&controls==null)return;
            if(controls!=null)controls.AllowPlayerInput(previousInput);controls=null;
            Cursor.lockState=previousLock;Cursor.visible=previousVisible;enabled=false;player=null;
            var closing=modal;modal=null;
            if(closing!=null&&windows!=null)windows.Close(closing,false);
            windows=null;
        }
        void Update()
        {
            if(!enabled)return;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            if(Input.GetKeyDown(KeyCode.Escape)||player==null||(player.position-new Vector3(screen.x+.5f,screen.y+1.5f,screen.z+.5f)).sqrMagnitude>100||(Time.realtimeSinceStartup-openedAt>5&&SurveillanceClient.At(screen)==null))CloseInstance();
        }
        void Send(Guid camera,bool modeOnly=false,bool cycle=false)
        {
            var world=GameManager.Instance?.World;if(world==null)return;
            var state=SurveillanceClient.At(screen);long revision=state==null?0:state.Revision;
            if(world.IsRemote())ConnectionManager.Instance.SendToServer(NetPackageManager.GetPackage<NetPackagePZSurveillanceConfigure>().Setup(screen,channel,camera,modeOnly,cycle,revision));
            else
            {
                string result;if(modeOnly)SurveillanceState.SetMode(screen,channel,cycle,revision,player,out result);else SurveillanceState.Configure(screen,channel,camera,revision,player,out result);message=result;
            }
            if(world.IsRemote())message="设置已发送，等待服务器确认…";
        }
        void SetMarkers(bool enabled)
        {
            var current=GameManager.Instance?.World;var device=SurveillanceClient.At(screen);
            if(current==null||device==null)return;
            if(current.IsRemote())
            {ConnectionManager.Instance.SendToServer(NetPackageManager.GetPackage<NetPackagePZSurveillanceMarkers>().Setup(device,enabled));message="设置已发送，等待服务器确认…";}
            else SurveillanceState.SetTargetMarkers(screen,device.Id,enabled,device.Revision,player,out message);
        }
        void OnGUI()
        {
            if(!enabled)return;
            var matrix=GUI.matrix;var color=GUI.color;int depth=GUI.depth;
            try{GUI.matrix=Matrix4x4.identity;GUI.color=Color.white;GUI.depth=-1000;DrawMenu();}
            finally{GUI.matrix=matrix;GUI.color=color;GUI.depth=depth;}
        }
        void OnDestroy(){CloseInstance();}
        void DrawMenu()
        {
            float width=Mathf.Min(760,Screen.width-40),height=Mathf.Min(620,Screen.height-40);
            var rect=new Rect((Screen.width-width)/2,(Screen.height-height)/2,width,height);GUI.Box(rect,"");
            GUILayout.BeginArea(new Rect(rect.x+20,rect.y+16,rect.width-40,rect.height-32));
            GUILayout.BeginHorizontal();GUILayout.Label("4×3基地监控屏 — 无线频道设置",GUILayout.Height(28));if(GUILayout.Button("关闭",GUILayout.Width(80)))CloseInstance();GUILayout.EndHorizontal();
            GUILayout.Space(8);GUILayout.BeginHorizontal();GUILayout.Label("选择频道：",GUILayout.Width(90));
            for(int i=0;i<4;i++){bool selected=i==channel;if(GUILayout.Button((selected?"● ":"")+"频道 "+(i+1),GUILayout.Width(115))){channel=i;Send(Guid.Empty,true,false);}}
            GUILayout.EndHorizontal();
            var state=SurveillanceClient.At(screen);Guid current=state==null?Guid.Empty:state.Channels[channel];var bound=SurveillanceClient.ById(current);
            GUILayout.Label("当前绑定："+(bound==null?(current==Guid.Empty?"未绑定":"设备已失效"):bound.Label));
            GUILayout.BeginHorizontal();if(GUILayout.Button("清除此频道",GUILayout.Width(140)))Send(Guid.Empty);
            bool cycling=state!=null&&state.Cycle;if(GUILayout.Button(cycling?"关闭5秒轮巡":"开启5秒轮巡",GUILayout.Width(160)))Send(Guid.Empty,true,!cycling);
            GUILayout.Label(message);GUILayout.EndHorizontal();GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if(GUILayout.Button(state!=null&&state.TargetMarkers?"目标红框：开启":"目标红框：关闭",GUILayout.Width(180)))SetMarkers(state==null||!state.TargetMarkers);
            GUILayout.Label("仅标记镜头可见的丧尸和动物；此设置属于当前大屏");GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            SurveillanceRenderService.AutomaticDegrade=GUILayout.Toggle(SurveillanceRenderService.AutomaticDegrade,"本机自动保底（试验）",GUILayout.Width(205));
            SurveillanceRenderService.Diagnostics=GUILayout.Toggle(SurveillanceRenderService.Diagnostics,"记录性能",GUILayout.Width(100));
            if(GUILayout.Button("导出记录",GUILayout.Width(90))){SurveillanceRenderService.Export();message=string.IsNullOrEmpty(SurveillanceRenderService.LastExport)?"暂无记录":"记录已导出，路径见游戏日志";}
            GUILayout.EndHorizontal();GUILayout.Label(SurveillanceRenderService.Summary);
            GUILayout.Label("128格内摄像头（墙壁和楼板不会削弱信号）");
            scroll=GUILayout.BeginScrollView(scroll,"box");
            var cameras=SurveillanceClient.Devices.Where(d=>d.Kind==SurveillanceDeviceKind.Camera)
                .Select(d=>new{Device=d,Distance=Vector3.Distance(new Vector3(screen.x,screen.y,screen.z),new Vector3(d.Position.x,d.Position.y,d.Position.z))})
                .Where(x=>x.Distance<=SurveillanceState.WirelessRange).OrderBy(x=>x.Distance).Take(32).ToArray();
            if(cameras.Length==0)GUILayout.Label("没有发现已登记的基地监控摄像头。请确认摄像头已放置，并等待最多2秒刷新。");
            foreach(var entry in cameras)
            {
                GUILayout.BeginHorizontal("box");string status=!entry.Device.Loaded?"未加载":entry.Device.Powered?"有电":"无电";
                GUILayout.Label(entry.Device.Label,GUILayout.Width(300));GUILayout.Label(Mathf.RoundToInt(entry.Distance)+"格",GUILayout.Width(70));GUILayout.Label(status,GUILayout.Width(80));
                if(GUILayout.Button(current==entry.Device.Id?"已绑定":"绑定到频道 "+(channel+1),GUILayout.Width(150)))Send(entry.Device.Id);GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();GUILayout.Label("摄像头和大屏各自接电即可；改接其他电源或短时断电不会丢失绑定。",GUILayout.Height(24));GUILayout.EndArea();
        }
    }
}
