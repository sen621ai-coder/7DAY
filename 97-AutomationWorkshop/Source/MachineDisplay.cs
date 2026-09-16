using UnityEngine;
namespace YFAutomation
{
    public static class MachineDisplay
    {
        public static void AttachInteraction(TileEntityComposite __instance,BlockEntityData __0)
        {
            if(__0?.transform==null)return;
            string name=__instance.block.GetBlockName();
            if(IsMachine(name)||ConveyorPath.IsBelt(name))BindInteraction(__0.transform);
        }
        public static void BindInteraction(Transform root)
        {
            // Voxel.Raycast recognizes T_Block and resolves its owning block through
            // RootTransformRefParent. A plain Unity collider is not an interactable block.
            foreach(var collider in root.GetComponentsInChildren<Collider>(true)){
                collider.gameObject.tag="T_Block";
                collider.gameObject.layer=16; // Matches the native steel crate: TerrainCollision (16).
                var reference=collider.GetComponent<RootTransformRefParent>()??collider.gameObject.AddComponent<RootTransformRefParent>();
                reference.RootTransform=root;
            }
        }
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
