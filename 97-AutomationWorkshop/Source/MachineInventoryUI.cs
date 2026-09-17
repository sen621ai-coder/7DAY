using HarmonyLib;
using UnityEngine.Scripting;
namespace YFAutomation
{
    public static class MachineInventoryUI
    {
        public const string Group="yfMachineInventory";
        public static void Install(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(XUiV_Grid),"Update"),postfix:new HarmonyMethod(typeof(MachineInventoryUI),nameof(SplitGrid)));
            h.Patch(AccessTools.Method(typeof(XUiC_BackpackWindow),"TryGetMoveDestinationInventory"),postfix:new HarmonyMethod(typeof(MachineInventoryUI),nameof(BackpackDestination)));
            h.Patch(AccessTools.Method(typeof(XUiM_LootContainer),"AddItem"),prefix:new HarmonyMethod(typeof(MachineInventoryUI),nameof(QuickDeposit)));
            h.Patch(AccessTools.Method(typeof(XUiC_LootWindowGroup),"openContainer"),postfix:new HarmonyMethod(typeof(MachineInventoryUI),nameof(ContainerOpened)));
            h.Patch(AccessTools.Method(typeof(TEFeatureStorage),"ShowUI"),prefix:new HarmonyMethod(typeof(MachineInventoryUI),nameof(Show)));
            h.Patch(AccessTools.Method(typeof(TEFeatureStorage),"OnDestroy"),prefix:new HarmonyMethod(typeof(MachineInventoryUI),nameof(Close)));
            h.Patch(AccessTools.Method(typeof(TileEntityComposite),"OnUnload"),prefix:new HarmonyMethod(typeof(MachineInventoryUI),nameof(Unload)));
        }
        static bool IsMachineScreen(XUi xui)=>xui?.FindWindowGroupByName(Group)?.WindowGroup?.isShowing==true&&
            xui.LootContainer is TEFeatureStorage storage&&MachineInventory.Has(storage.Parent);
        public static void BackpackDestination(XUiC_BackpackWindow __instance,ref IInventory _dstInventory,bool __result)
        {
            if(__result&&IsMachineScreen(__instance.xui))_dstInventory=new MachinePartitionInventory(__instance.xui.LootContainer,0);
        }
        public static bool QuickDeposit(ItemStack _itemStack,XUi _xui,ref bool __result)
        {
            if(!IsMachineScreen(_xui))return true;
            var destination=new MachinePartitionInventory(_xui.LootContainer,0);destination.TryStackItem(0,_itemStack);
            __result=_itemStack.count==0||destination.AddItem(_itemStack);return false;
        }
        public static void SplitGrid(XUiV_Grid __instance)
        {
            if(!(__instance.Controller is XUiC_YFAutomationSplitContainer))return;
            __instance.grid.arrangement=UIGrid.Arrangement.Horizontal;
            __instance.grid.maxPerLine=1;__instance.grid.cellWidth=324;__instance.grid.cellHeight=382;
        }
        public static void ContainerOpened(XUiC_LootWindowGroup __instance)
        {
            if(__instance.WindowGroup.Id!=Group)return;
            var panel=__instance.GetChildByType<XUiC_YFAutomationRecipePanel>();
            if(panel!=null){panel.ViewComponent.IsVisible=true;__instance.xui.RecenterWindowGroup(__instance.WindowGroup,true);}
        }
        public static void ShowRecipe(XUi xui)
        {
            var group=xui.FindWindowGroupByName(Group);
            if(group?.WindowGroup?.isShowing!=true)return;
            var panel=group.GetChildByType<XUiC_YFAutomationRecipePanel>();
            if(panel!=null&&!panel.ViewComponent.IsVisible){panel.ViewComponent.IsVisible=true;xui.RecenterWindowGroup(group.WindowGroup,true);}
        }
        public static bool OwnsStorageLock(TileEntityComposite t,int actor)
        {
            var s=t.GetFeature<TEFeatureStorage>();var sign=t.GetFeature<TEFeatureSignable>();
            int holder;
            return s!=null&&(sign==null||!LockManager.Instance.IsLockedServer(sign,0))&&
                LockManager.Instance.singleLocks.TryGetByValue(new LockEntry(s,0),out holder)&&holder==actor;
        }
        public static bool Show(TEFeatureStorage __instance,bool _lockGranted)
        {
            if(!_lockGranted||!MachineInventory.Has(__instance.Parent))return true;
            var ui=LocalPlayerUI.GetUIForPrimaryPlayer();
            var group=ui?.xui.FindWindowGroupByName(Group) as XUiC_LootWindowGroup;
            if(group==null)return true;
            group.OpenLooting(__instance.Parent.block.GetLocalizedBlockName(),__instance);
            return false;
        }
        public static void Close(TEFeatureStorage __instance)
        {
            if(GameManager.IsDedicatedServer)return;
            var ui=LocalPlayerUI.GetUIForPrimaryPlayer();
            var group=ui?.xui.FindWindowGroupByName(Group) as XUiC_LootWindowGroup;
            if(group?.te==__instance&&ui.windowManager.IsWindowOpen(Group))ui.windowManager.Close(Group);
        }
        public static void Unload(TileEntityComposite __instance)
        {var s=__instance.GetFeature<TEFeatureStorage>();if(s!=null)Close(s);}
    }
    [Preserve]
    public sealed class XUiC_YFAutomationInventoryControls : XUiC_YFAutomationConfiguration
    {
        protected override bool InventoryScreen=>true;
    }
}
