using System;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
namespace YFAutomation
{
    // A shared native output buffer is routed by item, never by slot position.
    // Native sorting and manual inventory rearrangement cannot change an item's outlet.
    public static class ThreeWaySorter
    {
        sealed class Cursors { public readonly int[] Input=new int[3],Output=new int[3]; }
        static readonly ConditionalWeakTable<TileEntityComposite,Cursors> cursors=new ConditionalWeakTable<TileEntityComposite,Cursors>();
        static Cursors Cursor(TileEntityComposite t)=>cursors.GetValue(t,_=>new Cursors());
        public static bool Is(TileEntityComposite t)=>t?.block.GetBlockName()=="yfAutoRouter";
        public static int Route(ItemValue item,MachineSettings settings)
        {
            string name=item?.ItemClass?.GetItemName();
            return !string.IsNullOrEmpty(settings.Product)&&name==settings.Product?0:
                !string.IsNullOrEmpty(settings.Product2)&&name==settings.Product2?1:2;
        }
        static Vector3i At(TileEntityComposite t,Vector3 direction)=>Logistics.Add(t.ToWorldPos(),ConveyorPath.Offset(GameManager.Instance.World.GetBlock(t.ToWorldPos()),direction));
        static int Outlet(TileEntityComposite t,Vector3i to)=>to==At(t,Vector3.left)?0:to==At(t,Vector3.forward)?1:2;
        public static int OutputStart(TileEntityComposite t,Vector3i to)=>Is(t)?Cursor(t).Output[Outlet(t,to)]:0;
        public static void OutputMoved(TileEntityComposite t,Vector3i to,int slot){if(Is(t))Cursor(t).Output[Outlet(t,to)]=(slot+1)%MachineInventory.TotalSlots;}
        public static string Step(TileEntityComposite t)
        {
            if(!Is(t)||!MachineInventory.UsesInternal(t))return "需要三路分拣箱内置库存";
            if(Logistics.Busy(t))return "库存正在使用";
            var config=MachineConfiguration.Get(t);
            if(config.Paused)return "已暂停";
            if(!Logistics.Powered(GameManager.Instance.World,t.ToWorldPos()))return "缺电";
            var store=t.GetFeature<TEFeatureStorage>();var input=ProductionInventory.Clone(store.items);var output=ProductionInventory.Clone(store.items);
            var cursor=Cursor(t);int total=0,active=1+(config.Product!=""?1:0)+(config.Product2!=""?1:0);
            int limit=MachineInventory.InputSlots/active;
            for(int route=0;route<3;route++){
                int lane=route;
                int occupied=output.Skip(MachineInventory.InputSlots).Count(s=>!s.IsEmpty()&&Route(s.itemValue,config)==lane);
                // Reserve cache capacity for the other lanes; native sorting can still move slots.
                total+=ConveyorTransfer.Move(input,output,
                    i=>!MachineInventory.IsInput(i)||Logistics.Locked(store,i)||Route(input[i].itemValue,config)!=lane,
                    i=>!MachineInventory.IsOutput(i)||Logistics.Locked(store,i)||output[i].IsEmpty()&&occupied>=limit,
                    16,int.MaxValue,v=>v.ItemClass.Stacknumber.Value,startIndex:cursor.Input[lane],movedFrom:i=>cursor.Input[lane]=(i+1)%MachineInventory.InputSlots);
            }
            if(total==0)return "等待物料／对应通道空位";
            if(Logistics.Busy(t)||GameManager.Instance.World.GetTileEntity(t.ToWorldPos())!=t)return "库存正在使用";
            for(int i=0;i<store.items.Length;i++)store.items[i]=MachineInventory.IsInput(i)?input[i]:output[i];
            t.SetChunkModified();t.SetModified();return "三路并行输送："+total;
        }
        public static bool CanInput(TileEntityComposite t,Vector3i from)=>!Is(t)||from==At(t,Vector3.back);
        public static bool CanOutput(TileEntityComposite t,Vector3i to,ItemStack item)
        {
            if(!Is(t))return true;
            if(item==null||item.IsEmpty())return false;
            var settings=MachineConfiguration.Get(t);
            if(settings.Paused||!Logistics.Powered(GameManager.Instance.World,t.ToWorldPos()))return false;
            int route=Route(item.itemValue,settings);
            return to==At(t,route==0?Vector3.left:route==1?Vector3.forward:Vector3.right);
        }
    }
}
