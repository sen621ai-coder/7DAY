using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;
namespace YFAutomation
{
    public static class RecipePreview
    {
        public static string Describe(World world,TileEntityComposite machine,MachineSettings draft,EntityPlayer owner,List<RecipeMaterial> materials=null)
        {
            string kind=machine.block.GetBlockName();
            if(kind!="yfAutoForge"&&kind!="yfAutoKitchen")return "";
            if(string.IsNullOrEmpty(draft.Product))return "选择左侧产品后，这里显示材料和工具。\n原料放上3行，下面3行留空收成品。";
            if(owner==null)return "等待设备所有者上线，才能计算实际配方和技能加成。";
            if(!MachineSettingsStorage.ValidStorageMode(draft.StorageMode))return "库存模式无效";
            bool internalMode=draft.StorageMode=="internal"||draft.StorageMode==""&&machine.GetFeature<TEFeatureMachineInventory>()?.Legacy==false;
            var sources=internalMode?new[]{machine}:MachineConfiguration.Boxes(world,machine,true).Where(t=>draft.Source==""||MachineConfiguration.Key(t.ToWorldPos())==draft.Source).ToArray();
            RecipePlan best=null;
            foreach(var source in sources)
            {
                var storage=source.GetFeature<TEFeatureStorage>();if(storage==null)continue;
                var plan=RecipePlan.Select(draft.Product,kind,storage.items,i=>Logistics.Locked(storage,i)||internalMode&&!MachineInventory.IsInput(i)||!internalMode&&i==0&&source.block.GetBlockName()=="yfAutoOutput",owner);
                if(plan==null)continue;
                if(best==null||plan.Ready)best=plan;if(plan.Ready)break;
            }
            if(best==null)best=RecipePlan.Select(draft.Product,kind,ItemStack.CreateArray(0),i=>false,owner);
            if(best==null)return "此设备没有适用配方。";
            materials?.AddRange(best.Materials.Take(128));
            return Localization.Get(draft.Product)+"\n"+(best.Ready?"材料与工具已齐，保存并关闭面板后加工。":"请按下面的缺少数量补充原料/工具。")+
                "\n"+string.Join("\n",best.Lines)+(internalMode?"\n内置模式：上3行原料，下3行自动收成品。":"\n外接箱模式：原料从输入箱读取，产物送至输出箱。")+
                (kind=="yfAutoForge"?"\n普通材料可直接用；旧冶炼料也能抵用。":"")+
                (!internalMode?"\n当前统计外接输入箱；多配方自动选可加工的一种。":"");
        }
    }
    public sealed class NetPackageYFAutomationRecipeRequest : NetPackage
    {
        static World current;static readonly Dictionary<int,float> next=new Dictionary<int,float>();
        public Vector3i At;public int Request;public MachineSettings Draft=new MachineSettings();
        public override NetPackageDirection PackageDirection=>NetPackageDirection.ToServer;
        public override int GetLength()=>31+Encoding.UTF8.GetByteCount(Draft.Source+Draft.Target+Draft.Product+Draft.StorageMode);
        public override void write(PooledBinaryWriter w){base.write(w);w.Write(At.x);w.Write(At.y);w.Write(At.z);w.Write(Request);ConfigurationWire.Settings(w,Draft);}
        public override void read(PooledBinaryReader r){At=new Vector3i(r.ReadInt32(),r.ReadInt32(),r.ReadInt32());Request=r.ReadInt32();Draft=ConfigurationWire.Settings(r);}
        public override void ProcessPackage(World world,GameManager callbacks)
        {if(world==null||!(ConnectionManager.Instance?.IsServer??false)||Sender==null||!Sender.loginDone||!Sender.bAttachedToEntity)return;Handle(world,Sender.entityId);}
        public void Handle(World world,int actor)
        {
            if(world==null||world.IsRemote()||!(ConnectionManager.Instance?.IsServer??false))return;
            if(current!=world){current=world;next.Clear();}
            float at;if(next.TryGetValue(actor,out at)&&Time.realtimeSinceStartup<at)return;next[actor]=Time.realtimeSinceStartup+.5f;
            var player=world.GetEntity(actor) as EntityPlayer;var machine=world.GetTileEntity(At) as TileEntityComposite;
            if(!MachineConfiguration.CanAccess(world,machine,player))return;
            var owner=GameManager.Instance.GetPersistentPlayerList()?.GetEntityPlayerFromUserId(machine.GetFeature<TEFeatureLockable>()?.GetOwner()??machine.Owner);
            var reply=NetPackageManager.GetPackage<NetPackageYFAutomationRecipeReply>();reply.At=At;reply.Request=Request;
            reply.Materials=new List<RecipeMaterial>();
            reply.Text=RecipePreview.Describe(world,machine,Draft,owner,reply.Materials);
            if(Encoding.UTF8.GetByteCount(reply.Text)>8192)reply.Text="配方材料信息过长，无法显示。";
            if(player is EntityPlayerLocal)reply.Deliver();else ConnectionManager.Instance.SendPackage(reply,false,actor);
        }
    }
    public sealed class NetPackageYFAutomationRecipeReply : NetPackage
    {
        public Vector3i At;public int Request;public string Text="";
        public List<RecipeMaterial> Materials=new List<RecipeMaterial>();
        public override NetPackageDirection PackageDirection=>NetPackageDirection.ToClient;
        public override int GetLength()=>22+Encoding.UTF8.GetByteCount(Text)+Materials.Sum(m=>11+Encoding.UTF8.GetByteCount(m.Name));
        public override void write(PooledBinaryWriter w){base.write(w);w.Write(At.x);w.Write(At.y);w.Write(At.z);w.Write(Request);ConfigurationWire.Text(w,Text,8192);if(Materials.Count>128)throw new System.IO.InvalidDataException();w.Write((ushort)Materials.Count);foreach(var m in Materials){ConfigurationWire.Text(w,m.Name);w.Write(m.Need);w.Write(m.Have);w.Write(m.Tool);}}
        public override void read(PooledBinaryReader r){At=new Vector3i(r.ReadInt32(),r.ReadInt32(),r.ReadInt32());Request=r.ReadInt32();Text=ConfigurationWire.Text(r,8192);int count=r.ReadUInt16();if(count>128)throw new System.IO.InvalidDataException();Materials=new List<RecipeMaterial>();for(int i=0;i<count;i++)Materials.Add(new RecipeMaterial{Name=ConfigurationWire.Text(r),Need=r.ReadInt32(),Have=r.ReadInt32(),Tool=r.ReadBoolean()});}
        public override void ProcessPackage(World world,GameManager callbacks){if(world!=null&&ConnectionManager.Instance!=null&&!ConnectionManager.Instance.IsServer)Deliver();}
        public void Deliver()=>XUiC_YFAutomationConfiguration.Active?.ReceiveRecipe(this);
    }
    [Preserve]
    public sealed class XUiC_YFAutomationRecipePanel : XUiC_InfoWindow
    {
        int page;string previous="";
        public override void Init()
        {
            base.Init();GetChildById("back").OnPress+=(s,b)=>{page=Math.Max(0,page-1);};
            GetChildById("forward").OnPress+=(s,b)=>{page++;};
        }
        public override void OnOpen(){base.OnOpen();page=0;previous="";}
        public override void Update(float dt)
        {
            base.Update(dt);string text=XUiC_YFAutomationConfiguration.Active?.PreviewText??"选择左侧产品查看配方。";
            if(previous!=text){previous=text;page=0;}
            var active=XUiC_YFAutomationConfiguration.Active;
            var materials=active?.PreviewMaterials??new List<RecipeMaterial>();
            int pages=Math.Max(1,(materials.Count+3)/4);page=Math.Min(page,pages-1);
            // Keep explanations separate from the native ingredient rows.
            var lines=text.Split('\n').Where(s=>!s.Contains("：需要 ")&&!s.StartsWith("工具：")&&s!=Localization.Get(active?.SelectedProduct??"")).ToArray();
            string summary=string.Join("\n",lines.Take(6));
            if(materials.Count>0)summary=(text.Contains("尚未解锁")?"配方尚未解锁":text.Contains("材料与工具已齐")?"材料与工具已齐":"缺少材料、工具或余料空格")+
                "\n工具保留，不会消耗。\n"+(active.IsInternalInventory?"右侧材料区放原料和工具。\n成品自动进入右下方。":"当前从外接输入箱取料。\n产物送至外接输出箱。")+
                "\n保存并关闭面板后加工。";
            ((XUiV_Label)GetChildById("body").ViewComponent).Text=summary;
            var item=ItemClass.GetItem(active?.SelectedProduct??"").ItemClass;
            ((XUiV_Label)GetChildById("productName").ViewComponent).Text=item==null?"选择左侧物品":Localization.Get(item.GetItemName());
            var icon=(XUiV_Sprite)GetChildById("productIcon").ViewComponent;icon.SpriteName=item?.GetIconName()??"";icon.IsVisible=item!=null;
            for(int i=0;i<4;i++){var row=GetChildById("material"+i) as XUiC_YFAutomationMaterialEntry;int index=page*4+i;row.Material=index<materials.Count?materials[index]:null;row.RefreshBindings();row.ViewComponent.IsVisible=row.Material!=null;}
            ((XUiV_Label)GetChildById("page").ViewComponent).Text=(page+1)+" / "+pages;
            GetChildById("back").ViewComponent.IsVisible=pages>1;GetChildById("forward").ViewComponent.IsVisible=pages>1;
        }
    }
}
