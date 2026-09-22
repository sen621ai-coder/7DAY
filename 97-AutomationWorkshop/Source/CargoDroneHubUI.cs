using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Scripting;

namespace YFAutomation.CargoDrones
{
    public enum CargoHubAction : byte{Read,TogglePause,Recall,AddSource,RemoveSource,SetTarget,ClearTarget}
    public static class CargoHubUI
    {
        public const string Group="yfCargoHub",Command="yfCargoConfigure";
        public static Vector3i Pending;
        static bool installed;
        public static void Install(Harmony h)
        {
            if(installed)return;
            h.Patch(AccessTools.Method(typeof(BlockCompositeTileEntity),"GetBlockActivationCommands"),postfix:new HarmonyMethod(typeof(CargoHubUI),nameof(Commands)));
            h.Patch(AccessTools.Method(typeof(BlockCompositeTileEntity),"HasBlockActivationCommands"),postfix:new HarmonyMethod(typeof(CargoHubUI),nameof(HasCommands)));
            h.Patch(AccessTools.Method(typeof(BlockCompositeTileEntity),"OnBlockActivated",new[]{typeof(string),typeof(WorldBase),typeof(Vector3i),typeof(BlockValue),typeof(EntityPlayerLocal)}),prefix:new HarmonyMethod(typeof(CargoHubUI),nameof(Activate)));
            installed=true;
        }
        static bool IsHub(Vector3i p){return GameManager.Instance?.World?.GetBlock(p).Block.GetBlockName()==CargoRuntime.HubBlock||(CargoNativeWorld.Current?.Service.Status().Any(s=>s.Configuration.Position.Equals(new CargoPosition(p.x,p.y,p.z)))??false);}
        public static void HasCommands(Vector3i _blockPos,ref bool __result){if(IsHub(_blockPos))__result=true;}
        public static void Commands(Vector3i _blockPos,ref BlockActivationCommand[] __result)
        {if(IsHub(_blockPos))__result=new[]{new BlockActivationCommand(Command,"ui_game_symbol_workbench",true)}.Concat(__result??BlockActivationCommand.Empty).ToArray();}
        public static bool Activate(string _commandName,Vector3i _blockPos,EntityPlayerLocal _player,ref bool __result)
        {if(_commandName!=Command)return true;Pending=_blockPos;_player.PlayerUI.windowManager.Open(Group,true);__result=true;return false;}
        internal static string Coordinates(CargoPosition p){return p.X+", "+p.Y+", "+p.Z;}
        internal static string Describe(CargoHubStatus s)
        {
            var c=s.Configuration;var text=new StringBuilder();
            text.AppendLine("状态："+s.Message).AppendLine("电量："+(s.Battery/6000.0).ToString("0.0")+"%    货物："+s.Packages+" / 6");
            text.AppendLine("采集半径：64 格    目标距离上限：1000 格");
            text.AppendLine("目标："+(c.Target==null?"未绑定":Coordinates(c.Target.Position)));
            text.AppendLine("采集设备："+c.Sources.Length+" / 8");
            foreach(var source in c.Sources)text.AppendLine(Coordinates(source.Position)+"  "+source.BlockName);
            if(s.Packages>0)text.AppendLine("现有货物继续送往装货时的目标。");
            return text.ToString();
        }
    }
    public sealed class NetPackageYFCargoHubRequest : NetPackage
    {
        static World requestWorld;
        static readonly Dictionary<int,float> next=new Dictionary<int,float>();
        public Vector3i At,Endpoint;
        public int Request;
        public Guid Hub;
        public long Revision;
        public CargoHubAction Action;
        public override NetPackageDirection PackageDirection=>NetPackageDirection.ToServer;
        public override int GetLength()=>55;
        public override void write(PooledBinaryWriter w){base.write(w);w.Write(At.x);w.Write(At.y);w.Write(At.z);w.Write(Request);w.Write((byte)Action);w.Write(Hub.ToByteArray());w.Write(Revision);w.Write(Endpoint.x);w.Write(Endpoint.y);w.Write(Endpoint.z);}
        public override void read(PooledBinaryReader r){At=new Vector3i(r.ReadInt32(),r.ReadInt32(),r.ReadInt32());Request=r.ReadInt32();Action=(CargoHubAction)r.ReadByte();Hub=new Guid(r.ReadBytes(16));Revision=r.ReadInt64();Endpoint=new Vector3i(r.ReadInt32(),r.ReadInt32(),r.ReadInt32());}
        public override void ProcessPackage(World world,GameManager callbacks)
        {if(Sender==null||!Sender.loginDone||!Sender.bAttachedToEntity)return;Handle(world,Sender.entityId);}
        public void Handle(World world,int actor)
        {
            var reply=Evaluate(world,actor);if(reply==null)return;
            var player=world.GetEntity(actor) as EntityPlayer;
            if(player is EntityPlayerLocal){reply.Deliver();NetPackageManager.FreePackage(reply);}
            else if(player!=null)ConnectionManager.Instance.SendPackage(reply,false,actor);
            else NetPackageManager.FreePackage(reply);
        }
        // Same authenticated command path for local/remote delivery and native QA.
        public NetPackageYFCargoHubReply Evaluate(World world,int actor)
        {
            if(world==null||world.IsRemote()||!(ConnectionManager.Instance?.IsServer??false))return null;
            if(requestWorld!=world){requestWorld=world;next.Clear();}
            var reply=NetPackageManager.GetPackage<NetPackageYFCargoHubReply>();reply.At=At;reply.Request=Request;reply.Allowed=false;reply.Hub=Guid.Empty;reply.Revision=0;reply.Paused=false;reply.Details="";reply.Message="停机坪不可用";
            var player=world.GetEntity(actor) as EntityPlayer;var runtime=CargoNativeWorld.Current;CargoHubStatus authorized=null;
            try
            {
                if(runtime==null&&CargoRuntime.Failure!=null)throw new InvalidOperationException("货运记录需要恢复，请查看服务器日志");
                var state=runtime?.Service.Status().SingleOrDefault(s=>s.Configuration.Position.Equals(new CargoPosition(At.x,At.y,At.z)));
                if(state==null)throw new InvalidOperationException("停机坪尚未就绪；每人最多 4 座，全服最多 16 座，请稍后重试");
                if(player==null||player.IsDead()||(player.position-new Vector3(At.x+.5f,At.y+.5f,At.z+.5f)).sqrMagnitude>64||player.PersistentPlayerData?.PrimaryId?.CombinedString!=state.Configuration.Owner||!runtime.HubExists(state.Configuration))throw new InvalidOperationException("需要停机坪所有者在 8 格内操作");
                authorized=state;
                float until;if(next.TryGetValue(actor,out until)&&Time.realtimeSinceStartup<until)throw new InvalidOperationException("操作过快，请稍后重试");next[actor]=Time.realtimeSinceStartup+.2f;
                if(!Enum.IsDefined(typeof(CargoHubAction),Action))throw new InvalidOperationException("未知操作");
                var c=state.Configuration;var changed=c;var rules=new CargoRules();string owner=c.Owner;
                if(Action!=CargoHubAction.Read&&(Hub!=c.HubId||Revision!=c.Revision))throw new InvalidOperationException("配置或停机坪已改变，请刷新");
                var at=new CargoPosition(Endpoint.x,Endpoint.y,Endpoint.z);
                switch(Action)
                {
                    case CargoHubAction.TogglePause:changed=c.SetPaused(owner,c.Revision,!c.Paused);break;
                    case CargoHubAction.Recall:runtime.Service.Recall(c.HubId,owner);break;
                    case CargoHubAction.AddSource:
                        var source=runtime.ResolveBinding(at,owner,true);if(source==null)throw new InvalidOperationException("未找到支持的采矿机或林场");
                        changed=c.AddSource(owner,c.Revision,source,rules);break;
                    case CargoHubAction.RemoveSource:
                        var old=c.Sources.SingleOrDefault(s=>s.Position.Equals(at));if(old==null)throw new InvalidOperationException("此坐标未绑定采集设备");changed=c.RemoveSource(owner,c.Revision,old.EndpointId);break;
                    case CargoHubAction.SetTarget:
                        var target=runtime.ResolveBinding(at,owner,false);if(target==null)throw new InvalidOperationException("未找到支持的玩家储物箱");changed=c.SetTarget(owner,c.Revision,target,rules);break;
                    case CargoHubAction.ClearTarget:changed=c.SetTarget(owner,c.Revision,null,rules);break;
                }
                if(changed!=c)runtime.Service.Configure(c.HubId,owner,c.Revision,changed);
                state=runtime.Service.Status().Single(s=>s.Configuration.HubId==c.HubId);
                reply.Allowed=true;reply.Hub=c.HubId;reply.Revision=state.Configuration.Revision;reply.Paused=state.Configuration.Paused;reply.Details=CargoHubUI.Describe(state);reply.Message=Action==CargoHubAction.Read?"配置已读取":"操作已保存";
            }
            catch(Exception ex)
            {
                reply.Message=ex.Message.Length>150?"操作失败，请查看日志并重试":ex.Message;
                // A rejected command does not revoke permission to view the hub.
                // Return fresh state so capacity/version/throttle errors can be retried.
                if(authorized!=null)
                {
                    var state=runtime.Service.Status().Single(s=>s.Configuration.HubId==authorized.Configuration.HubId);
                    reply.Allowed=true;reply.Hub=state.Configuration.HubId;reply.Revision=state.Configuration.Revision;reply.Paused=state.Configuration.Paused;reply.Details=CargoHubUI.Describe(state);
                }
            }
            return reply;
        }
    }
    public sealed class NetPackageYFCargoHubReply : NetPackage
    {
        public Vector3i At;public int Request;public Guid Hub;public long Revision;public bool Allowed,Paused;public string Message="",Details="";
        public override NetPackageDirection PackageDirection=>NetPackageDirection.ToClient;
        public override int GetLength()=>48+Encoding.UTF8.GetByteCount(Message+Details);
        public override void write(PooledBinaryWriter w){base.write(w);w.Write(At.x);w.Write(At.y);w.Write(At.z);w.Write(Request);w.Write(Allowed);w.Write(Hub.ToByteArray());w.Write(Revision);w.Write(Paused);ConfigurationWire.Text(w,Message,512);ConfigurationWire.Text(w,Details,4096);}
        public override void read(PooledBinaryReader r){At=new Vector3i(r.ReadInt32(),r.ReadInt32(),r.ReadInt32());Request=r.ReadInt32();Allowed=r.ReadBoolean();Hub=new Guid(r.ReadBytes(16));Revision=r.ReadInt64();Paused=r.ReadBoolean();Message=ConfigurationWire.Text(r,512);Details=ConfigurationWire.Text(r,4096);}
        public override void ProcessPackage(World world,GameManager callbacks){if(world!=null&&!(ConnectionManager.Instance?.IsServer??true))Deliver();}
        public void Deliver(){XUiC_YFCargoHub.Active?.Receive(this);}
    }
    [Preserve]
    public sealed class XUiC_YFCargoHub : XUiController
    {
        public static XUiC_YFCargoHub Active;
        static int sequence;
        Vector3i at;Guid hub;long revision;int request;bool ready,open,awaiting;float sent;
        XUiC_TextInput warehouseQuery;
        CargoWarehouseKind warehouseKind;
        CargoSourceKind sourceKind;
        bool sources=true,boundOnly,refreshAfterAction;
        CargoWarehousePage warehouses=new CargoWarehousePage();
        int warehouseRequest,queuedPage=-1;bool searching;float warehouseSent;
        string lastWarehouseQuery="";
        void Label(string name,string text){((XUiV_Label)GetChildById(name).ViewComponent).Text=text;}
        public override void Init()
        {
            base.Init();warehouseQuery=GetChildById("warehouseQuery") as XUiC_TextInput;
            GetChildById("warehouseSearch").OnPress+=(s,b)=>SearchWarehouses(0);
            GetChildById("warehouseType").OnPress+=(s,b)=>
            {
                if(!ready||searching)return;
                if(sources)sourceKind=(CargoSourceKind)(((int)sourceKind+1)%9);else warehouseKind=(CargoWarehouseKind)(((int)warehouseKind+1)%6);
                PickerLabels();SearchWarehouses(0);
            };
            GetChildById("warehousePrev").OnPress+=(s,b)=>{if(warehouses.Page>0)SearchWarehouses(warehouses.Page-1);};
            GetChildById("warehouseNext").OnPress+=(s,b)=>{if((warehouses.Page+1)*8<warehouses.Total)SearchWarehouses(warehouses.Page+1);};
            for(int i=0;i<8;i++)
            {
                int index=i;GetChildById("warehouseRow"+i).OnPress+=(s,b)=>
                {
                    if(!ready||searching||queuedPage>=0||index>=warehouses.Rows.Length)return;
                    var row=warehouses.Rows[index];
                    if(sources&&!row.Available&&!row.Bound)return;
                    Label("selection",(sources?"采集设备：":"目标仓库：")+CargoWarehouseFilter.Clean(string.IsNullOrWhiteSpace(row.Sign)?row.Name:row.Sign,22));
                    refreshAfterAction=true;Send(sources?(row.Bound?CargoHubAction.RemoveSource:CargoHubAction.AddSource):CargoHubAction.SetTarget,row.Position);
                };
            }
            GetChildById("add").OnPress+=(s,b)=>Pick(true,false);
            GetChildById("remove").OnPress+=(s,b)=>Pick(true,true);
            GetChildById("target").OnPress+=(s,b)=>Pick(false,false);
            Bind("refresh",CargoHubAction.Read);Bind("pause",CargoHubAction.TogglePause);Bind("recall",CargoHubAction.Recall);Bind("clear",CargoHubAction.ClearTarget);
            GetChildById("close").OnPress+=(s,b)=>xui.playerUI.windowManager.Close(WindowGroup);
        }
        void Bind(string name,CargoHubAction action){GetChildById(name).OnPress+=(s,b)=>{if(ready||action==CargoHubAction.Read)Send(action);};}
        void PickerLabels()
        {
            Label("warehouseTitle",sources?(boundOnly?"管理已绑定设备":"选择矿机 / 林场"):"选择收货仓库");
            Label("warehouseScope",sources?(boundOnly?"包含暂不可用的绑定记录":"已加载区域 · 停机坪 64 格内"):"已加载区域 · 停机坪 1000 格内");
            Label("warehouseTypeText",sources?CargoSourceFilter.Label(sourceKind):CargoWarehouseFilter.Label(warehouseKind));
        }
        void Pick(bool sourceMode,bool onlyBound)
        {
            if(!ready)return;
            sources=sourceMode;boundOnly=onlyBound;sourceKind=CargoSourceKind.All;warehouseKind=CargoWarehouseKind.All;
            warehouseQuery.Text="";lastWarehouseQuery="";searching=false;warehouseRequest=++sequence;queuedPage=-1;
            warehouses=new CargoWarehousePage();PickerLabels();ShowWarehouses();SearchWarehouses(0);
        }
        public override void OnOpen()
        {
            base.OnOpen();Active=this;open=true;ready=false;awaiting=false;sent=-1;at=CargoHubUI.Pending;hub=Guid.Empty;revision=0;
            warehouseQuery.Text="";lastWarehouseQuery="";warehouseKind=CargoWarehouseKind.All;sourceKind=CargoSourceKind.All;sources=true;boundOnly=false;
            searching=false;queuedPage=-1;warehouses=new CargoWarehousePage();refreshAfterAction=true;
            PickerLabels();ShowWarehouses();Label("warehouseNotice","读取配置后自动列出矿机和林场");Label("selection","在右侧点击设备即可绑定，无需输入坐标");Label("details","");Send(CargoHubAction.Read);
        }
        public override void OnClose(){open=false;ready=false;awaiting=false;searching=false;queuedPage=-1;if(Active==this)Active=null;base.OnClose();}
        void SearchWarehouses(int page)
        {
            if(!ready||searching)return;
            if(Time.realtimeSinceStartup-warehouseSent<.55f){queuedPage=page;Label("warehouseNotice","正在刷新列表…");return;}
            queuedPage=-1;
            string query=CargoWarehouseFilter.Clean(warehouseQuery.Text,64);if(query!=lastWarehouseQuery)page=0;lastWarehouseQuery=query;
            var packet=NetPackageManager.GetPackage<NetPackageYFCargoWarehouseRequest>();packet.At=at;packet.Hub=hub;packet.Request=warehouseRequest=++sequence;packet.Page=page;packet.Kind=warehouseKind;packet.Query=query;packet.Sources=sources;packet.SourceKind=sourceKind;packet.BoundOnly=boundOnly;
            warehouses=new CargoWarehousePage();ShowWarehouses();searching=true;warehouseSent=Time.realtimeSinceStartup;Label("warehouseNotice","正在查找设备…");
            if(ConnectionManager.Instance.IsServer)packet.Handle(GameManager.Instance.World,xui.playerUI.entityPlayer.entityId);else ConnectionManager.Instance.SendToServer(packet);
        }
        void ShowWarehouses()
        {
            for(int i=0;i<8;i++)
            {
                if(i>=warehouses.Rows.Length){Label("warehouseName"+i,"");Label("warehouseInfo"+i,"");continue;}
                var row=warehouses.Rows[i];string type=sources?CargoSourceFilter.Label(CargoSourceFilter.Kind(row.Block)):CargoWarehouseFilter.Label(row.Kind);
                string name=string.IsNullOrWhiteSpace(row.Sign)?row.Name:row.Sign;if(string.IsNullOrWhiteSpace(name))name=type;
                Label("warehouseName"+i,(sources&&row.Bound?"[已绑定] ":"")+CargoWarehouseFilter.Clean(name,sources&&row.Bound?16:22));
                Label("warehouseInfo"+i,CargoHubUI.Coordinates(row.Position)+" · "+Math.Round(row.Distance)+" 格 · "+(sources&&row.Bound?(row.Available?"点击移除":"不可用，点击移除"):type));
            }
            Label("warehousePage",warehouses.Total==0?"暂无结果":(warehouses.Page+1)+" / "+((warehouses.Total+7)/8)+" 页 · "+warehouses.Total+" 项");
        }
        public void ReceiveWarehouses(NetPackageYFCargoWarehouseReply reply)
        {
            if(!open||!searching||reply.At!=at||reply.Hub!=hub||reply.Request!=warehouseRequest||reply.Sources!=sources)return;
            searching=false;warehouses=reply.Success?reply.Result:new CargoWarehousePage();ShowWarehouses();Label("warehouseNotice",reply.Message);
        }
        void Send(CargoHubAction action,CargoPosition? selected=null)
        {
            if(awaiting)return;
            if(Time.realtimeSinceStartup-sent<.25f){Label("notice","操作过快，请稍后重试");return;}
            if((action==CargoHubAction.AddSource||action==CargoHubAction.RemoveSource||action==CargoHubAction.SetTarget)&&!selected.HasValue)return;
            var endpoint=selected.HasValue?new Vector3i(selected.Value.X,selected.Value.Y,selected.Value.Z):Vector3i.zero;
            var p=NetPackageManager.GetPackage<NetPackageYFCargoHubRequest>();p.At=at;p.Endpoint=endpoint;p.Action=action;p.Request=request=++sequence;p.Hub=hub;p.Revision=revision;
            ready=false;awaiting=true;sent=Time.realtimeSinceStartup;Label("notice","正在处理…");
            if(ConnectionManager.Instance.IsServer)p.Handle(GameManager.Instance.World,xui.playerUI.entityPlayer.entityId);else ConnectionManager.Instance.SendToServer(p);
        }
        public void Receive(NetPackageYFCargoHubReply reply)
        {
            if(!open||reply.At!=at||reply.Request!=request)return;
            awaiting=false;ready=reply.Allowed;Label("notice",reply.Message);if(!ready)return;
            hub=reply.Hub;revision=reply.Revision;Label("details",reply.Details);Label("pauseText",reply.Paused?"恢复调度":"暂停调度");
            if(refreshAfterAction){refreshAfterAction=false;SearchWarehouses(warehouses.Page);}
        }
        public override void Update(float dt)
        {
            base.Update(dt);
            if(open&&awaiting&&Time.realtimeSinceStartup-sent>8){awaiting=false;Label("notice","请求超时，请刷新");}
            if(open&&searching&&Time.realtimeSinceStartup-warehouseSent>8){searching=false;Label("warehouseNotice","搜索超时，请重新查找");}
            if(open&&ready&&!searching&&queuedPage>=0&&Time.realtimeSinceStartup-warehouseSent>=.55f)SearchWarehouses(queuedPage);
        }
    }
}
