using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Scripting;
namespace YFAutomation
{
    // These bindings are used only by copies of the native recipe/ingredient templates.
    [Preserve]
    public sealed class XUiC_YFAutomationProductEntry : XUiController
    {
        public string Product="";public bool IsChosen;
        public override bool GetBindingValueInternal(ref string value,string name)
        {
            var item=ItemClass.GetItem(Product).ItemClass;
            switch(name){case "recipename":value=(IsChosen?"✓ ":"")+Localization.Get(Product);return true;
                case "recipeicon":value=item?.GetIconName()??"";return true;
                case "recipeicontint":value=item==null?"255,255,255,255":Tint(item.GetIconTint());return true;
                case "hasingredientsstatecolor":value=IsChosen?"222,206,163,255":"255,255,255,255";return true;}
            return base.GetBindingValueInternal(ref value,name);
        }
        internal static string Tint(Color32 c)=>c.r+","+c.g+","+c.b+","+c.a;
    }
    [Preserve]
    public sealed class XUiC_YFAutomationMaterialEntry : XUiController
    {
        public RecipeMaterial Material;
        public override bool GetBindingValueInternal(ref string value,string name)
        {
            var item=Material==null?null:ItemClass.GetItem(Material.Name).ItemClass;
            switch(name){case "itemicon":value=item?.GetIconName()??"";return true;
                case "itemicontint":value=item==null?"255,255,255,255":XUiC_YFAutomationProductEntry.Tint(item.GetIconTint());return true;
                case "itemname":value=Material==null?"":Localization.Get(Material.Name)+(Material.Tool?"（工具）":"");return true;
                case "haveneedcount":value=Material==null?"":(Material.Have<Material.Need?"[FF7777]":"[FFFFFF]")+Material.Have+" / "+Material.Need+"[-]";return true;}
            return base.GetBindingValueInternal(ref value,name);
        }
    }
    [Preserve]
    public sealed class XUiC_YFAutomationSplitContainer : XUiC_LootContainer { }

    // Shares native loot slot synchronization and locking. Buttons receive masks,
    // never a separate inventory copy: partitions cannot exchange items during sort.
    [Preserve]
    public sealed class XUiC_YFAutomationStorageWindow : XUiC_LootWindow
    {
        public override void Init()
        {
            base.Init();
            for(int part=0;part<2;part++)
            {
                int start=part*MachineInventory.InputSlots;
                var bar=(XUiC_ContainerStandardControls)GetChildById("toolbar"+part);
                bar.GetLockedSlotsFromStorage=()=>Mask(te,start);
                bar.SetLockedSlotsToStorage=slots=>{if(te!=null){te.SlotLocks=slots;te.SetModified();}};
                bar.ApplyLockedSlotStates=slots=>ApplyLockedSlotStates(te?.SlotLocks);
                bar.UpdateLockedSlotStates=c=>UpdateLockedSlots(standardControls);
                bar.LockModeToggled=()=>UserLockMode=!UserLockMode;
                bar.SortPressed=mask=>{
                    if(te==null)return;
                    var sorted=StackSortUtil.CombineAndSortStacks(ProductionInventory.Clone(te.items),0,Mask(te,start));
                    for(int i=start;i<start+18;i++)te.UpdateSlot(i,sorted[i]);te.SetModified();
                };
                bar.MoveAllowed=(out XUiController parent,out XUiC_ItemStackGrid grid,out IInventory inventory)=>{
                    // No preference tracker shortcut, which would bypass the partition mask.
                    parent=bar;grid=lootContainer;inventory=xui.PlayerInventory;return te!=null&&!UserLockMode;
                };
                bar.MoveAllDone=(all,any)=>{};
                bar.GetChildById("deposit").OnPress+=(s,b)=>{
                    if(te==null||UserLockMode)return;
                    var bag=xui.GetChildByType<XUiC_Backpack>();
                    var backpack=xui.GetChildByType<XUiC_BackpackWindow>();
                    if(backpack?.UserLockMode==true)return;
                    backpack?.UpdateLockedSlots(backpack.standardControls);
                    if(bag!=null)XUiM_LootContainer.StashItems(bar,bag,new MachinePartitionInventory(te,start),0,xui.playerUI.entityPlayer.bag.LockedSlots,XUiM_LootContainer.EItemMoveKind.All,false);
                };
            }
        }
        public static PackedBoolArray Mask(ITileEntityLootable tile,int start)
        {
            var mask=new PackedBoolArray(MachineInventory.TotalSlots);
            for(int i=0;i<MachineInventory.TotalSlots;i++)mask[i]=i<start||i>=start+18||(tile?.SlotLocks!=null&&tile.SlotLocks[i]);
            return mask;
        }
    }
    public sealed class MachinePartitionInventory : IInventory
    {
        readonly ITileEntityLootable tile;readonly int start;
        public MachinePartitionInventory(ITileEntityLootable tile,int start){this.tile=tile;this.start=start;}
        bool Available(int i)=>tile.SlotLocks==null||!tile.SlotLocks[i];
        public bool HasItem(ItemValue value)=>tile.items.Skip(start).Take(18).Any(s=>!s.IsEmpty()&&s.itemValue.type==value.type);
        public bool AddItem(ItemStack stack)
        {
            if(!stack.CanMoveTo(XUiC_ItemStack.StackLocationTypes.LootContainer))return false;
            for(int i=start;i<start+18;i++)if(Available(i)&&tile.items[i].IsEmpty()){tile.UpdateSlot(i,stack.Clone());tile.SetModified();return true;}
            return false;
        }
        public (bool anyMoved,bool allMoved) TryStackItem(int index,ItemStack stack)
        {
            if(!stack.CanMoveTo(XUiC_ItemStack.StackLocationTypes.LootContainer))return(false,false);
            int before=stack.count;
            for(int i=start;i<start+18&&stack.count>0;i++)if(Available(i)&&tile.items[i].CanStackWith(stack,true))
            {
                var next=tile.items[i].Clone();int count=Math.Min(stack.count,next.itemValue.ItemClass.Stacknumber.Value-next.count);
                if(count<=0)continue;next.count+=count;stack.count-=count;tile.UpdateSlot(i,next);
            }
            if(before!=stack.count)tile.SetModified();return(before!=stack.count,stack.count==0);
        }
    }
}
