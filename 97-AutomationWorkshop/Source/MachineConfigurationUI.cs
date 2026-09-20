using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Scripting;

namespace YFAutomation
{
    public static class MachineConfigurationUI
    {
        public const string Group="yfMachineConfiguration", Command="yfConfigureMachine";
        public static Vector3i Pending;
        public static void Install(Harmony h)
        {
            MachineInventoryUI.Install(h);
            h.Patch(AccessTools.Method(typeof(BlockCompositeTileEntity),"GetBlockActivationCommands"),postfix:new HarmonyMethod(typeof(MachineConfigurationUI),nameof(Commands)));
            h.Patch(AccessTools.Method(typeof(BlockCompositeTileEntity),"HasBlockActivationCommands"),postfix:new HarmonyMethod(typeof(MachineConfigurationUI),nameof(HasCommands)));
            h.Patch(AccessTools.Method(typeof(BlockCompositeTileEntity),"OnBlockActivated",new[]{typeof(string),typeof(WorldBase),typeof(Vector3i),typeof(BlockValue),typeof(EntityPlayerLocal)}),prefix:new HarmonyMethod(typeof(MachineConfigurationUI),nameof(Activate)));
            h.Patch(AccessTools.Method(typeof(TileEntityComposite),"OnRemove"),prefix:new HarmonyMethod(typeof(MachineConfiguration),nameof(MachineConfiguration.Removed)));
            h.Patch(AccessTools.Method(typeof(TileEntityComposite),"OnUnload"),prefix:new HarmonyMethod(typeof(MachineConfiguration),nameof(MachineConfiguration.Unloaded)));
            h.Patch(AccessTools.Method(typeof(BlockCompositeTileEntity),"OnBlockAdded"),prefix:new HarmonyMethod(typeof(MachineConfiguration),nameof(MachineConfiguration.Placed)));
        }
        public static void HasCommands(BlockValue _blockValue,ref bool __result)
        {if(MachineConfiguration.Supported(_blockValue.Block.GetBlockName()))__result=true;}
        public static void Commands(BlockValue _blockValue,ref BlockActivationCommand[] __result)
        {
            if(!MachineConfiguration.Supported(_blockValue.Block.GetBlockName()))return;
            __result=new[]{new BlockActivationCommand(Command,"ui_game_symbol_workbench",true)}.Concat(__result??BlockActivationCommand.Empty).ToArray();
        }
        public static bool Activate(string _commandName,Vector3i _blockPos,BlockValue _blockValue,EntityPlayerLocal _player,ref bool __result)
        {
            if(_commandName!=Command||!MachineConfiguration.Supported(_blockValue.Block.GetBlockName()))return true;
            var tile=GameManager.Instance.World.GetTileEntity(_blockPos) as TileEntityComposite;
            if(MachineInventory.Has(tile))
            {__result=tile.GetFeature<TEFeatureStorage>().OnBlockActivated("Search".AsSpan(),GameManager.Instance.World,_blockPos,_blockValue,_player);return false;}
            Pending=_blockPos;_player.PlayerUI.windowManager.Open(Group,true);__result=true;return false;
        }
    }
    // Bound every client-controlled string before allocating or processing it.
    public static class ConfigurationWire
    {
        public static void Text(PooledBinaryWriter w,string value,int max=160)
        {var bytes=Encoding.UTF8.GetBytes(value??"");if(bytes.Length>max)throw new ArgumentException("Configuration text too long");w.Write((ushort)bytes.Length);w.Write(bytes);}
        public static string Text(PooledBinaryReader r,int max=160)
        {int size=r.ReadUInt16();if(size>max)throw new System.IO.InvalidDataException("Configuration text too long");var b=r.ReadBytes(size);if(b.Length!=size)throw new System.IO.EndOfStreamException();return Encoding.UTF8.GetString(b);}
        public static void Settings(PooledBinaryWriter w,MachineSettings s)
        {w.Write(s.Paused);w.Write(s.Revision);Text(w,s.Source);Text(w,s.Target);Text(w,s.Product);Text(w,s.StorageMode);}
        public static MachineSettings Settings(PooledBinaryReader r)=>new MachineSettings{Paused=r.ReadBoolean(),Revision=r.ReadInt32(),Source=Text(r),Target=Text(r),Product=Text(r),StorageMode=Text(r)};
    }
    public sealed class NetPackageYFAutomationConfigRequest : NetPackage
    {
        static World requestWorld;
        static readonly Dictionary<int,float> nextRequest=new Dictionary<int,float>();
        public Vector3i At;public int Request;public bool Save;public string Token="";public MachineSettings Value=new MachineSettings();
        public override NetPackageDirection PackageDirection=>NetPackageDirection.ToServer;
        public override int GetLength()=>34+Encoding.UTF8.GetByteCount(Token+Value.Source+Value.Target+Value.Product+Value.StorageMode);
        public override void write(PooledBinaryWriter w){base.write(w);w.Write(At.x);w.Write(At.y);w.Write(At.z);w.Write(Request);w.Write(Save);ConfigurationWire.Text(w,Token,64);ConfigurationWire.Settings(w,Value);}
        public override void read(PooledBinaryReader r){At=new Vector3i(r.ReadInt32(),r.ReadInt32(),r.ReadInt32());Request=r.ReadInt32();Save=r.ReadBoolean();Token=ConfigurationWire.Text(r,64);Value=ConfigurationWire.Settings(r);}
        public override void ProcessPackage(World world,GameManager callbacks)
        {if(world==null||!(ConnectionManager.Instance?.IsServer??false)||Sender==null||!Sender.loginDone||!Sender.bAttachedToEntity)return;Handle(world,Sender.entityId);}
        public void Handle(World world,int actor)
        {
            if(world==null||world.IsRemote()||!(ConnectionManager.Instance?.IsServer??false))return;
            if(requestWorld!=world){requestWorld=world;nextRequest.Clear();}
            var player=world.GetEntity(actor) as EntityPlayer;var t=world.GetTileEntity(At) as TileEntityComposite;
            var reply=NetPackageManager.GetPackage<NetPackageYFAutomationConfigReply>();reply.At=At;reply.Request=Request;
            // NetPackageManager pools instances; a denied request must not reuse a previous snapshot.
            reply.Allowed=false;reply.Kind="";reply.Token="";reply.Value=new MachineSettings();
            float next;
            if(nextRequest.TryGetValue(actor,out next)&&Time.realtimeSinceStartup<next)reply.Message="操作过快，请稍后刷新";
            else if(!MachineConfiguration.CanAccess(world,t,player))reply.Message="无配置权限或距离过远";
            else
            {
                reply.Message=Save?MachineConfiguration.Apply(world,t,player,Value,Token):"配置已读取";
                reply.Value=MachineConfiguration.Get(t).Clone();reply.Kind=t.block.GetBlockName();reply.Token=MachineConfiguration.Token(t);
                reply.Allowed=true;
            }
            nextRequest[actor]=Time.realtimeSinceStartup+.25f;
            if(player is EntityPlayerLocal local)reply.Deliver(local);else ConnectionManager.Instance.SendPackage(reply,false,actor);
        }
    }
    public sealed class NetPackageYFAutomationConfigReply : NetPackage
    {
        public Vector3i At;public int Request;public bool Allowed;public string Message="",Kind="",Token="";public MachineSettings Value=new MachineSettings();
        public override NetPackageDirection PackageDirection=>NetPackageDirection.ToClient;
        public override int GetLength()=>38+Encoding.UTF8.GetByteCount(Message+Kind+Token+Value.Source+Value.Target+Value.Product+Value.StorageMode);
        public override void write(PooledBinaryWriter w){base.write(w);w.Write(At.x);w.Write(At.y);w.Write(At.z);w.Write(Request);w.Write(Allowed);ConfigurationWire.Text(w,Message,512);ConfigurationWire.Text(w,Kind);ConfigurationWire.Text(w,Token,64);ConfigurationWire.Settings(w,Value);}
        public override void read(PooledBinaryReader r){At=new Vector3i(r.ReadInt32(),r.ReadInt32(),r.ReadInt32());Request=r.ReadInt32();Allowed=r.ReadBoolean();Message=ConfigurationWire.Text(r,512);Kind=ConfigurationWire.Text(r);Token=ConfigurationWire.Text(r,64);Value=ConfigurationWire.Settings(r);}
        public override void ProcessPackage(World world,GameManager callbacks)
        {if(world==null||ConnectionManager.Instance==null||ConnectionManager.Instance.IsServer)return;var p=world.GetPrimaryPlayer();if(p!=null)Deliver(p);}
        public void Deliver(EntityPlayerLocal player)=>XUiC_YFAutomationConfiguration.Active?.Receive(this);
    }

    [Preserve]
    public class XUiC_YFAutomationConfiguration : XUiController
    {
        public static XUiC_YFAutomationConfiguration Active;
        protected virtual bool InventoryScreen=>false;
        int page,previewRequest;float nextPreview;string previewKey="",serverPreview="";
        public string PreviewText=>RecipeMachines.IsMachine(kind)?(serverPreview==""?"正在读取配方材料…":serverPreview):Details();
        public string SelectedProduct=>draft.Product;
        public bool AutomaticRecycling=>kind=="yfAutoRecycler";
        public bool IsInternalInventory=>InternalMode();
        public List<RecipeMaterial> PreviewMaterials=new List<RecipeMaterial>();
        string PreviewKey()=>draft.Product+"|"+draft.StorageMode+"|"+draft.Source;
        string lastQuery="";
        List<string> matches=new List<string>();
        static int sequence;
        Vector3i at;int request;bool open,ready;float sent;
        string token="",kind="",notice="";MachineSettings draft=new MachineSettings();
        readonly List<string> sources=new List<string>(),targets=new List<string>(),products=new List<string>();
        XUiC_TextInput search;
        void Label(string id,string text){((XUiV_Label)GetChildById(id).ViewComponent).Text=text;}
        public override void Init()
        {
            base.Init();search=GetChildById("search") as XUiC_TextInput;
            GetChildById("toggle").OnPress+=(s,b)=>{if(ready){draft.Paused=!draft.Paused;Send(true);}};
            GetChildById("source").OnPress+=(s,b)=>{if(ready){draft.Source=Next(sources,draft.Source,b==1?-1:1);Render();}};
            GetChildById("target").OnPress+=(s,b)=>{if(ready){draft.Target=Next(targets,draft.Target,b==1?-1:1);Render();}};
            GetChildById("product").OnPress+=(s,b)=>{if(ready){var q=search.Text??"";var matches=products.Where(n=>n==""||n.IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0||Localization.Get(n).IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0).ToList();draft.Product=Next(matches,draft.Product,b==1?-1:1);Render();}};
            GetChildById("mode").OnPress+=(s,b)=>{if(ready){draft.StorageMode=InternalMode()?"external":"internal";draft.Source="";draft.Target="";Render();}};
            GetChildById("previous").OnPress+=(s,b)=>{page=Math.Max(0,page-1);RenderProducts();};
            GetChildById("next").OnPress+=(s,b)=>{page=Math.Min(Math.Max(0,(matches.Count-1)/8),page+1);RenderProducts();};
            for(int row=0;row<8;row++){int index=row;GetChildById("recipe"+row).OnPress+=(s,b)=>{int entry=page*8+index;if(ready&&entry<matches.Count){draft.Product=matches[entry];Render();}};}
            GetChildById("save").OnPress+=(s,b)=>{if(ready)Send(true);};
            GetChildById("refresh").OnPress+=(s,b)=>Send(false);
            GetChildById("close").OnPress+=(s,b)=>xui.playerUI.windowManager.Close(WindowGroup);
        }
        static string Next(List<string> list,string value,int direction)
        {if(list.Count==0)return "";int i=list.IndexOf(value);return list[(i+direction+list.Count)%list.Count];}
        public override void OnOpen()
        {base.OnOpen();open=true;Active=this;
            var native=xui.FindWindowGroupByName(MachineInventoryUI.Group) as XUiC_LootWindowGroup;
            at=InventoryScreen&&native?.te!=null?native.te.ToWorldPos():MachineConfigurationUI.Pending;
            kind="";draft=new MachineSettings();PreviewMaterials.Clear();search.Text="";page=0;lastQuery="";nextPreview=0;previewKey="";serverPreview="";Send(false);}
        public override void OnClose(){open=false;ready=false;if(Active==this)Active=null;base.OnClose();}
        void Send(bool save)
        {
            ready=false;request=++sequence;sent=Time.realtimeSinceStartup;notice=save?"正在保存…":"正在读取…";Render();
            var p=NetPackageManager.GetPackage<NetPackageYFAutomationConfigRequest>();p.At=at;p.Request=request;p.Save=save;p.Token=token;p.Value=draft.Clone();
            if(ConnectionManager.Instance.IsServer)p.Handle(GameManager.Instance.World,xui.playerUI.entityPlayer.entityId);
            else ConnectionManager.Instance.SendToServer(p);
        }
        public void Receive(NetPackageYFAutomationConfigReply reply)
        {
            if(!open||reply.Request!=request||reply.At!=at)return;
            ready=reply.Allowed;notice=reply.Message;
            if(ready)
            {
                draft=reply.Value.Clone();kind=reply.Kind;token=reply.Token;
                if(AutomaticRecycling)draft.Product="";
                sources.Clear();targets.Clear();products.Clear();sources.Add("");targets.Add("");products.Add("");
                var w=GameManager.Instance.World;var t=w.GetTileEntity(at) as TileEntityComposite;
                if(t!=null){sources.AddRange(MachineConfiguration.Boxes(w,t,true).Select(b=>MachineConfiguration.Key(b.ToWorldPos())));targets.AddRange(MachineConfiguration.Boxes(w,t,false).Select(b=>MachineConfiguration.Key(b.ToWorldPos())));}
                products.AddRange(MachineConfiguration.Products(kind));
            }
            Render();
        }
        public override void Update(float dt)
        {
            base.Update(dt);if(!open)return;
            if(lastQuery!=(search.Text??"")){lastQuery=search.Text??"";page=0;RenderProducts();}
            if(ready&&(RecipeMachines.IsMachine(kind))&&Time.realtimeSinceStartup>=nextPreview)
            {
                nextPreview=Time.realtimeSinceStartup+1;previewKey=PreviewKey();previewRequest=++sequence;
                var p=NetPackageManager.GetPackage<NetPackageYFAutomationRecipeRequest>();p.At=at;p.Request=previewRequest;p.Draft=draft.Clone();
                if(ConnectionManager.Instance.IsServer)p.Handle(GameManager.Instance.World,xui.playerUI.entityPlayer.entityId);else ConnectionManager.Instance.SendToServer(p);
            }
            if(!ready&&Time.realtimeSinceStartup-sent>8){notice="未收到可用配置；请刷新，或关闭后重试";sent=float.MaxValue;Render();}
            var t=GameManager.Instance?.World?.GetTileEntity(at) as TileEntityComposite;
            Label("status",t?.GetFeature<TEFeatureSignable>()?.GetAuthoredText().Text??"设备已卸载或移除");
        }
        public void ReceiveRecipe(NetPackageYFAutomationRecipeReply reply)
        {if(open&&reply.At==at&&reply.Request==previewRequest&&previewKey==PreviewKey()){serverPreview=reply.Text;PreviewMaterials=reply.Materials;}}
        bool InternalMode()
        {
            var t=GameManager.Instance?.World?.GetTileEntity(at) as TileEntityComposite;
            return draft.StorageMode=="internal"||(draft.StorageMode==""&&t?.GetFeature<TEFeatureMachineInventory>()?.Legacy==false);
        }
        void RenderProducts()
        {
            string q=search.Text??"";
            matches=products.Where(n=>n!=""&&(n.IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0||Localization.Get(n).IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0)).ToList();
            page=Math.Min(page,Math.Max(0,(matches.Count-1)/8));
            bool supported=MachineConfiguration.HasProduct(kind);
            for(int row=0;row<8;row++)
            {int index=page*8+row;GetChildById("recipe"+row).ViewComponent.IsVisible=supported&&index<matches.Count;
             var entry=GetChildById("recipe"+row) as XUiC_YFAutomationProductEntry;
             if(entry!=null){entry.Product=index<matches.Count?matches[index]:"";entry.IsChosen=entry.Product==draft.Product;entry.RefreshBindings();}}
            Label("pages",supported?(page+1)+" / "+Math.Max(1,(matches.Count+7)/8):"无需选择产品");
            GetChildById("previous").ViewComponent.IsVisible=supported;GetChildById("next").ViewComponent.IsVisible=supported;
        }
        string Details()
        {
            if(kind=="yfAutoWaterPump")return "紧贴水体，每5秒产出1份灌溉水。\n内置成品区上限200份；传送带可直接取水。";
            if(kind=="yfAutoAmmoFeed")return "原料区放匹配弹药，设备紧邻同主炮塔。\n只在炮塔实际射击时扣除1发。";
            if(kind=="yfAutoMiner")return "原料区：钻头耗材，每60秒消耗1份。\n下方须为自有领地真实矿点，每次产出20份。";
            if(kind=="yfAutoFarm")return "原料区放对应种子，可放灌溉水加速。\n收获自有领地成熟作物并补种。";
            if(kind=="yfAutoSmelter")return "原料区放同类可冶炼材料，按原生重量出料。\n成品区无需放样品。";
            if(kind=="yfAutoRecycler")return "自动识别装备的原生拆解产物。\n无需选材料，无需放样品。\n原料区放废装备，成品区收材料。\n品质6及以上、带模组、特殊数据\n及锁定格中的装备不会分解。\n所有者在线，启动后关闭面板。";
            if(kind=="yfAutoSorter"||kind=="yfAutoTransfer")return "每次最多16件，从原料区转入成品区。\n分拣机按所选物品过滤；未选则全部通过。";
            if(draft.Product=="")return "先从上方选择产品。";
            var player=xui.playerUI.entityPlayer;
            var recipes=CraftingManager.GetRecipes(draft.Product).Where(r=>RecipeMachines.Supports(kind,r)).ToList();
            var recipe=recipes.FirstOrDefault(r=>r.IsUnlocked(player))??recipes.FirstOrDefault();
            if(recipe==null)return "没有适用配方。";
            string ingredients=string.Join("、",recipe.GetIngredientsSummedUp().Select(v=>Localization.Get(v.itemValue.ItemClass.GetItemName().Replace("unit_","yfAutoIngot_"))+"×"+v.count));
            return (recipe.IsUnlocked(player)?"材料参考：":"未解锁：")+ingredients+"\n工具："+(recipe.craftingToolType>0?Localization.Get(ItemClass.GetForId(recipe.craftingToolType).GetItemName()):"无")+"（实际消耗按角色加成）";
        }
        void Render()
        {
            if(InventoryScreen&&open)MachineInventoryUI.ShowRecipe(xui);
            if(previewKey!=PreviewKey()){serverPreview="";PreviewMaterials.Clear();nextPreview=0;}
            Label("title","机器配置 · "+(kind==""?"读取中":Localization.Get(kind)));
            Label("toggleText",draft.Paused?"启动并保存":"暂停并保存");
            Label("sourceText","输入箱："+(draft.Source==""?"自动选择":draft.Source));
            Label("targetText","输出箱："+(draft.Target==""?"自动选择":draft.Target));
            Label("productText",(kind=="yfAutoSorter"?"过滤物品：":"目标产品：")+(draft.Product==""?(InternalMode()?"请选择":"沿用输出箱首格样品"):Localization.Get(draft.Product)));
            Label("modeText",InternalMode()?"库存：内置（点击改用外接箱）":"库存：外接箱（点击改用内置）");
            bool boxes=!InternalMode()&&MachineConfiguration.HasBoxes(kind),product=MachineConfiguration.HasProduct(kind);
            GetChildById("source").ViewComponent.IsVisible=boxes;GetChildById("target").ViewComponent.IsVisible=boxes;
            GetChildById("product").ViewComponent.IsVisible=product;search.ViewComponent.IsVisible=product;
            GetChildById("details").ViewComponent.IsVisible=!boxes;Label("details",RecipeMachines.IsMachine(kind)?"材料、工具和缺少数量见中间配方面板。\n上3行放原料，下3行留空收成品。":Details());
            GetChildById("details").ViewComponent.IsVisible=false;GetChildById("product").ViewComponent.IsVisible=false;
            Label("help",InternalMode()?"内置库存：上3行原料/工具，下3行成品。\n传送带指向机器送入原料，背向机器取走成品。\n打开库存期间暂停加工；关闭后自动继续。":
                "外接箱模式：同主人、同区块，输出首格保留。\n选择产品后保存。切换库存模式不搬动物品。");
            RenderProducts();
            Label("notice",notice);
        }
    }
}
