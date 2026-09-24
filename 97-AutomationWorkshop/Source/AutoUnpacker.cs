using System;
using System.Collections.Generic;
using System.Linq;
namespace YFAutomation
{
    public static class AutoUnpacker
    {
        public static bool Is(TileEntityComposite t)=>t?.block.GetBlockName()=="yfAutoUnpacker";
        // Use the resolved action, including XML inheritance and installed mod patches.
        // Random rewards and reusable/conditional actions are not deterministic unpacking.
        public static List<ItemStack> Products(ItemValue value)
        {
            if(value?.ItemClass==null||value.HasQuality||value.HasMods()||value.Meta!=0||value.UseTimes!=0||value.MaxUseTimes>0)return null;
            var actions=value.ItemClass.Actions?.OfType<ItemActionOpenBundle>().ToArray();
            if(actions==null||actions.Length!=1)return null;
            var action=actions[0];
            if(action.GetType()!=typeof(ItemActionOpenBundle)||!action.Consume||action.ConditionBlockTypes!=null||
                action.RandomItem!=null||action.CreateItem==null||action.CreateItem.Length==0||action.CreateItemCount==null)return null;
            var products=new List<ItemStack>();
            for(int i=0;i<action.CreateItem.Length;i++){
                var item=ItemClass.GetItem(action.CreateItem[i]);int count;
                string number=i<action.CreateItemCount.Length?action.CreateItemCount[i]:"1";
                if(item.type==0||item.ItemClass==null||item.HasQuality||!int.TryParse(number,out count)||count<=0||item.ItemClass.Stacknumber.Value<=0)return null;
                products.Add(new ItemStack(item,count));
            }
            return products;
        }
        public static string Step(TileEntityComposite machine)
        {
            if(!Is(machine)||!MachineInventory.UsesInternal(machine))return "拆包机需要内置库存";
            if(Logistics.Busy(machine))return "库存正在使用，拆包暂停";
            if(MachineConfiguration.Paused(machine))return "已暂停";
            if(!Logistics.Powered(GameManager.Instance.World,machine.ToWorldPos()))return "缺电：4格内需通电供电口";
            var store=machine.GetFeature<TEFeatureStorage>();bool blocked=false,unsupported=false;
            for(int i=0;i<MachineInventory.InputSlots;i++){
                var input=store.items[i];if(input==null||input.IsEmpty()||Logistics.Locked(store,i))continue;
                var products=Products(input.itemValue);if(products==null){unsupported=true;continue;}
                var next=ProductionInventory.Clone(store.items);
                if(products.Any(p=>!ProductionInventory.Produce(next,p,j=>!MachineInventory.IsOutput(j)||Logistics.Locked(store,j),v=>v.ItemClass.Stacknumber.Value,MachineInventory.InputSlots))){blocked=true;continue;}
                next[i].count--;if(next[i].count==0)next[i]=ItemStack.Empty;
                // The caller holds the native chunk transaction lock. Publish all outputs and
                // the consumed package together, never drop overflow into the world.
                if(Logistics.Busy(machine)||GameManager.Instance.World.GetTileEntity(machine.ToWorldPos())!=machine)return "库存正在使用，拆包暂停";
                Array.Copy(next,store.items,next.Length);machine.SetChunkModified();machine.SetModified();
                return "已拆包："+Localization.Get(input.itemValue.ItemClass.GetItemName());
            }
            return blocked?"成品区放不下整包，保留原包等待":unsupported?"等待可拆包物品；不支持的物品保持原样":"等待打包物品";
        }
    }
}
