using HarmonyLib;
using UnityEngine.Scripting;
namespace YFAutomation
{
    public static class MachineInventoryUI
    {
        public const string Group="yfMachineInventory";
        public static void Install(Harmony h)
        {
            h.Patch(AccessTools.PropertySetter(typeof(XUiView),"IsVisible"),prefix:new HarmonyMethod(typeof(MachineInventoryUI),nameof(HideInspectHelp)));
            h.Patch(AccessTools.Method(typeof(TEFeatureStorage),"ShowUI"),prefix:new HarmonyMethod(typeof(MachineInventoryUI),nameof(Show)));
            h.Patch(AccessTools.Method(typeof(TEFeatureStorage),"OnDestroy"),prefix:new HarmonyMethod(typeof(MachineInventoryUI),nameof(Close)));
            h.Patch(AccessTools.Method(typeof(TileEntityComposite),"OnUnload"),prefix:new HarmonyMethod(typeof(MachineInventoryUI),nameof(Unload)));
        }
        // The backpack shares this empty mouse-control tutorial with every screen.
        // Suppress only the tutorial while a machine inventory is open.
        public static void HideInspectHelp(XUiView __instance,ref bool __0)
        {
            if(__0 && __instance.Controller is XUiC_EmptyInfoWindow &&
                __instance.Controller.xui?.playerUI?.windowManager?.IsWindowOpen(Group)==true)
                __0=false;
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
            var help=ui.xui.FindWindowGroupByName("backpack")?.GetChildById("emptyInfoPanel");
            if(help!=null)help.ViewComponent.IsVisible=false;
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
