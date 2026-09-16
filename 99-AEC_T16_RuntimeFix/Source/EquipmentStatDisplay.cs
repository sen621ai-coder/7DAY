using System;
using System.Globalization;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace AECT16RuntimeFix
{
    public static class EquipmentStatDisplay
    {
        private static readonly Regex DurabilityWeapons = new Regex(
            @"^(gunPZAEC(EmberPistol|HorizonNeedle|StormReservoir|BastionShotgun|EchoRepeater|CounterSiege)|meleePZAECFaultlineHammer)T(16|17|18|19)$");
        public static bool ShowsWeaponDurability(string name)
        {
            return name != null && DurabilityWeapons.IsMatch(name);
        }
        public static string FormatDurability(int maximum, float used)
        {
            maximum = Math.Max(0, maximum);
            double remaining = Math.Max(0, Math.Min(maximum, maximum - (double)used));
            return string.Format(CultureInfo.InvariantCulture, "{0:0.#} / {1} ({2:0.#}%)",
                remaining, maximum, maximum > 0 ? remaining * 100 / maximum : 0);
        }
        public static bool DurabilityTextPrefix(ItemStack itemStack, DisplayInfoEntry infoEntry, ref string __result)
        {
            var value = itemStack == null ? null : itemStack.itemValue;
            if (infoEntry == null || infoEntry.CustomName != "aecBase_DegradationMax" ||
                value == null || value.IsEmpty() || value.ItemClass == null ||
                !ShowsWeaponDurability(value.ItemClass.GetItemName())) return true;
            // Use the same maximum as PercentUsesLeft, including installed mods,
            // fusion and native permanent-degradation metadata. Never change wear.
            __result = FormatDurability(value.MaxUseTimes, value.UseTimes);
            return false;
        }
        public static string ModDirectory { get; private set; }
        public static void Install(Harmony harmony, string modDirectory)
        {
            ModDirectory = modDirectory;
            harmony.Patch(AccessTools.Method(typeof(XUiM_ItemStack), "GetCustomValue"),
                prefix: new HarmonyMethod(typeof(EquipmentStatDisplay), nameof(Prefix)));
            harmony.Patch(AccessTools.Method(typeof(XUiM_ItemStack), "GetStatItemValueTextWithModInfo"),
                prefix: new HarmonyMethod(typeof(EquipmentStatDisplay), nameof(DurabilityTextPrefix)));
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
