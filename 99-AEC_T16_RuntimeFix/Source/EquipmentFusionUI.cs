using System;
using System.Collections.Generic;
using HarmonyLib;

namespace AECT16RuntimeFix
{
    public static class EquipmentFusionUI
    {
        private static readonly AccessTools.FieldRef<XUiC_CombineGrid,XUiC_RequiredItemStack> First = AccessTools.FieldRefAccess<XUiC_CombineGrid,XUiC_RequiredItemStack>("merge1");
        private static readonly AccessTools.FieldRef<XUiC_CombineGrid,XUiC_RequiredItemStack> Second = AccessTools.FieldRefAccess<XUiC_CombineGrid,XUiC_RequiredItemStack>("merge2");
        private static readonly AccessTools.FieldRef<XUiC_CombineGrid,ItemStack> Result = AccessTools.FieldRefAccess<XUiC_CombineGrid,ItemStack>("result");
        private static readonly AccessTools.FieldRef<XUiC_CombineGrid,float> Experience = AccessTools.FieldRefAccess<XUiC_CombineGrid,float>("experienceFromLastResult");
        private static readonly AccessTools.FieldRef<XUiC_ItemInfoWindow,ItemStack> InfoItem = AccessTools.FieldRefAccess<XUiC_ItemInfoWindow,ItemStack>("itemStack");
        private static readonly HashSet<XUiC_CombineGrid> Committing = new HashSet<XUiC_CombineGrid>();

        public static void Install(Harmony harmony)
        {
            // Fixed-stat gear has no tiered effect group, so vanilla mistakenly
            // treats it as qualityless and creates zero actual mod slots. Keep
            // numeric values fixed but grant these exact 92 types normal gear
            // identity, constructors, mod sockets and both station input paths.
            harmony.Patch(AccessTools.PropertyGetter(typeof(ItemClass), "HasQuality"),
                postfix: new HarmonyMethod(typeof(EquipmentFusionUI), nameof(QualityPostfix)));
            harmony.Patch(AccessTools.Method(typeof(XUiC_CombineGrid), "Merge_SlotChangedEvent"),
                prefix: new HarmonyMethod(typeof(EquipmentFusionUI), nameof(PreviewPrefix)));
            harmony.Patch(AccessTools.Method(typeof(XUiC_CombineGrid), "BtnCombine_OnPressed"),
                prefix: new HarmonyMethod(typeof(EquipmentFusionUI), nameof(CommitPrefix)),
                finalizer: new HarmonyMethod(typeof(EquipmentFusionUI), nameof(CommitFinalizer)));
            harmony.Patch(AccessTools.Method(typeof(XUiC_CombineGrid), "GetBindingValueInternal"),
                postfix: new HarmonyMethod(typeof(EquipmentFusionUI), nameof(GridBindingPostfix)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(XUiC_ItemStack), "ItemNameText"),
                postfix: new HarmonyMethod(typeof(EquipmentFusionUI), nameof(ItemNamePostfix)));
            harmony.Patch(AccessTools.Method(typeof(XUiC_ItemInfoWindow), "GetBindingValueInternal"),
                postfix: new HarmonyMethod(typeof(EquipmentFusionUI), nameof(InfoBindingPostfix)));
        }

        public static void QualityPostfix(ItemClass __instance, ref bool __result)
        { if (!__result && EquipmentFusion.IsFusionClass(__instance)) __result = true; }

        private static bool IsFusion(ItemStack stack) { return stack != null && EquipmentFusion.IsFusionItem(stack.itemValue); }
        private static bool Touched(XUiC_CombineGrid grid)
        { return IsFusion(First(grid)?.ItemStack) || IsFusion(Second(grid)?.ItemStack) || IsFusion(Result(grid)); }

        public static bool PreviewPrefix(XUiC_CombineGrid __instance)
        {
            if (!Touched(__instance)) return true;
            ItemStack output;
            EquipmentFusion.TryCreate(First(__instance)?.ItemStack, Second(__instance)?.ItemStack, out output);
            Experience(__instance) = 0;
            __instance.SetResult(output);
            return false;
        }

        public static bool CommitPrefix(XUiC_CombineGrid __instance, out bool __state)
        {
            __state = false;
            if (!Touched(__instance)) return true;
            if (Committing.Contains(__instance)) return false;
            ItemStack output;
            if (!EquipmentFusion.TryCreate(First(__instance)?.ItemStack, Second(__instance)?.ItemStack, out output))
            { __instance.SetResult(ItemStack.Empty.Clone()); return false; }
            // Refresh from the current inputs; never trust a stale preview.
            Experience(__instance) = 0;
            __instance.SetResult(output);
            Committing.Add(__instance);
            __state = true;
            // Native AddItem must succeed before it clears either input slot.
            return true;
        }

        public static void CommitFinalizer(XUiC_CombineGrid __instance, bool __state)
        { if (__state) Committing.Remove(__instance); }

        public static void GridBindingPostfix(XUiC_CombineGrid __instance, string bindingName, ref string value, ref bool __result)
        {
            if (bindingName == "aecfusionhint")
            {
                var a = First(__instance)?.ItemStack;
                var b = Second(__instance)?.ItemStack;
                value = Touched(__instance) ? EquipmentFusion.Validate(a,b) ??
                    "融合+" + (EquipmentFusion.Rank(a.itemValue)+1) + "：固有数值再提升5%；保留第一件模组与品质" :
                    "T16–T19同名同阶、同融合次数：两件合一，每次提升5%";
                __result = true;
            }
            else if (bindingName == "itemstackname1" || bindingName == "itemstackname2")
            {
                var stack = bindingName == "itemstackname1" ? First(__instance)?.ItemStack : Second(__instance)?.ItemStack;
                if (stack != null) value += EquipmentFusion.Label(stack.itemValue);
            }
        }

        public static void ItemNamePostfix(XUiC_ItemStack __instance, ref string __result)
        { if (__instance.ItemStack != null) __result += EquipmentFusion.Label(__instance.ItemStack.itemValue); }
        public static void InfoBindingPostfix(XUiC_ItemInfoWindow __instance, string bindingName, ref string value)
        { if (bindingName == "itemname" && InfoItem(__instance) != null) value += EquipmentFusion.Label(InfoItem(__instance).itemValue); }
    }
}
