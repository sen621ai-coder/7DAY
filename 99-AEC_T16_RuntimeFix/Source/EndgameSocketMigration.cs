using System;
using System.IO;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace AECT16RuntimeFix
{
    internal static class EndgameSocketMigration
    {
        private static readonly Regex Supported = new Regex(
            @"^(armorPZAEC(Harrier|Storm|Tremor|Warden)(Helmet|Outfit|Gloves|Boots)|gunPZAEC(EmberPistol|HorizonNeedle|StormReservoir|BastionShotgun|EchoRepeater|CounterSiege)|meleePZAECFaultlineHammer)T1[6-9]$",
            RegexOptions.CultureInvariant);

        public static void Install(Harmony harmony)
        {
            var method = AccessTools.Method(typeof(ItemValue), "Read", new[] { typeof(BinaryReader) });
            if (method == null) throw new MissingMethodException("ItemValue.Read(BinaryReader)");
            harmony.Patch(method, postfix: new HarmonyMethod(typeof(EndgameSocketMigration), nameof(AfterRead)));
        }

        // Upgrade only the 92 known equipment IDs after their complete native
        // save/network payload is consumed. Preserve contents, quality and wear.
        internal static void AfterRead(ItemValue __instance)
        {
            var item = __instance.ItemClass;
            if (item == null || !Supported.IsMatch(item.GetItemName())) return;
            var old = __instance.Modifications;
            if (old != null && old.Length >= 6) return;
            var expanded = new ItemValue[6];
            int count = old == null ? 0 : old.Length;
            if (count > 0) Array.Copy(old, expanded, count);
            for (int i = count; i < expanded.Length; i++) expanded[i] = new ItemValue();
            __instance.Modifications = expanded;
        }
    }
}
