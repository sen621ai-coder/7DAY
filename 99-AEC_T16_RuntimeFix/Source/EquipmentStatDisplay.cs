using System;
using HarmonyLib;

namespace AECT16RuntimeFix
{
    public static class EquipmentStatDisplay
    {
        public static string ModDirectory { get; private set; }
        public static void Install(Harmony harmony, string modDirectory)
        {
            ModDirectory = modDirectory;
            harmony.Patch(AccessTools.Method(typeof(XUiM_ItemStack), "GetCustomValue"),
                prefix: new HarmonyMethod(typeof(EquipmentStatDisplay), nameof(Prefix)));
        }
        public static bool Prefix(DisplayInfoEntry entry, ItemValue itemValue, bool useMods, ref float __result)
        {
            if (entry == null || itemValue == null || itemValue.IsEmpty() || itemValue.ItemClass == null ||
                !(itemValue.ItemClass.DisplayType ?? "").StartsWith("AECDisplay_", StringComparison.Ordinal)) return true;
            string key = entry.CustomName;
            bool percent = key != null && key.StartsWith("aecPercent_", StringComparison.Ordinal);
            if (!percent && (key == null || !key.StartsWith("aecBase_", StringComparison.Ordinal))) return true;
            PassiveEffects effect;
            if (!Enum.TryParse(key.Substring(percent ? 11 : 8), out effect)) return true;
            float basis = 0, multiplier = 1;
            // Native _originalItemValue is an exclusion sentinel, not the
            // current item. Passing this item there would skip all its effects.
            itemValue.ModifyValue(null, null, effect, ref basis, ref multiplier, entry.Tags, useMods, false);
            __result = percent ? multiplier - 1 : basis * multiplier;
            return false;
        }
    }
}
