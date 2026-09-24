using UnityEngine;
namespace YFAutomation
{
    // A shared native output buffer is routed by item, never by slot position.
    // Native sorting and manual inventory rearrangement cannot change an item's outlet.
    public static class ThreeWaySorter
    {
        public static bool Is(TileEntityComposite t)=>t?.block.GetBlockName()=="yfAutoRouter";
        public static int Route(ItemValue item,MachineSettings settings)
        {
            string name=item?.ItemClass?.GetItemName();
            return !string.IsNullOrEmpty(settings.Product)&&name==settings.Product?0:
                !string.IsNullOrEmpty(settings.Product2)&&name==settings.Product2?1:2;
        }
        static Vector3i At(TileEntityComposite t,Vector3 direction)=>Logistics.Add(t.ToWorldPos(),ConveyorPath.Offset(GameManager.Instance.World.GetBlock(t.ToWorldPos()),direction));
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
