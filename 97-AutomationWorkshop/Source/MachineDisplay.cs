using UnityEngine;
namespace YFAutomation
{
    public static class MachineDisplay
    {
        public static bool IsMachine(string name)
        {
            switch(name){
                case "yfAutoSorter":case "yfAutoKitchen":case "yfAutoSmelter":
                case "yfAutoForge":case "yfAutoRecycler":case "yfAutoFarm":
                case "yfAutoMiner":case "yfAutoTransfer":case "yfAutoWaterPump":
                case "yfAutoWaterTank":case "yfAutoAmmoFeed":return true;
                default:return false;
            }
        }
        // Keep the existing native sign feature and its persisted/networked state.
        // Machine prefabs have no crate text mesh; show that same status on aim.
        public static string WithStatus(TileEntityComposite tile,string text)
        {
            if(tile==null||!IsMachine(tile.block.GetBlockName()))return text;
            string status=tile.GetFeature<TEFeatureSignable>()?.GetAuthoredText().Text;
            return string.IsNullOrWhiteSpace(status)?text:text+"\n"+status;
        }
        public static void ActivationText(Vector3i __1,BlockValue __2,ref string __result)
        {
            if(!IsMachine(__2.Block.GetBlockName()))return;
            __result=WithStatus(GameManager.Instance?.World?.GetTileEntity(__1) as TileEntityComposite,__result);
        }
    }
}
