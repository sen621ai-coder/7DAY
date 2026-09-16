using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace AECT16RuntimeFix
{
    // Sort view rows together with their native stock indices. Never sort the
    // shared TraderData inventory: purchases and multiplayer use those indices.
    public static class RelayCatalogDisplay
    {
        public static bool IsRelay(int id) { return id >= 91 && id <= 96; }
        public static bool IsContract(string name)
        {
            return name != null && (RelayHuntStageGate.RequiredStage(name) > 0 ||
                (name.StartsWith("itemAEC", StringComparison.Ordinal) && name.Contains("Contract")));
        }
        public static int Tier(string name)
        {
            if (!IsContract(name)) return int.MaxValue;
            var match = Regex.Match(name, @"_?T(\d+)$");
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }
        public static int Kind(string name)
        {
            if (!IsContract(name)) return 9;
            if (name.Contains("100waves")) return 2;
            if (name.Contains("AllInOne")) return 3;
            if (name.Contains("Minion") || name.Contains("Horde")) return 1;
            if (name.Contains("Hunt") || Regex.IsMatch(name, @"^itemAECDumdumContract(_T\d+)?$")) return 0;
            return 4;
        }
        public static int[] Order(string[] names)
        {
            return Enumerable.Range(0, names.Length).OrderBy(i => Tier(names[i]))
                .ThenBy(i => Kind(names[i])).ThenBy(i => names[i], StringComparer.Ordinal)
                .ThenBy(i => i).ToArray();
        }
        public static void Reorder<T>(List<T> rows, List<int> indices, string[] names)
        {
            if (rows == null || indices == null || names == null || rows.Count != indices.Count || rows.Count != names.Length) return;
            int[] order = Order(names);
            var oldRows = rows.ToArray(); var oldIndices = indices.ToArray();
            for (int i = 0; i < order.Length; i++) { rows[i] = oldRows[order[i]]; indices[i] = oldIndices[order[i]]; }
        }
        public static string CleanName(string name)
        {
            string text = Regex.Replace(name ?? "", @"\[(?:[0-9A-Fa-f]{6,8}|-)\]", "");
            text = Regex.Replace(text, @"^\s*(?:\[AEC\]|AEC\s*\|)\s*", "");
            // Only the trailing tier-star marker is redundant; keep challenge
            // difficulty markers inside the original name intact.
            return Regex.Replace(text, @"\s*\[\d+★\]\s*$", "").Trim();
        }
        public static void AfterFilter(XUiC_TraderWindow __instance,
            List<ItemStack> ___currentInventory, List<int> ___currentIndexList, bool ___isSecretStash)
        {
            var trader = __instance.xui?.Trader?.TraderData;
            if (trader == null || !IsRelay(trader.TraderID) || ___isSecretStash || ___currentInventory == null) return;
            Reorder(___currentInventory, ___currentIndexList,
                ___currentInventory.Select(s => s?.itemValue?.ItemClass?.GetItemName() ?? "").ToArray());
        }
        public static void ItemNamePostfix(XUiC_TraderItemEntry __instance, string bindingName, ref string value)
        {
            if (bindingName != "itemname") return;
            var trader = __instance.xui?.Trader?.TraderData;
            if (trader == null || !IsRelay(trader.TraderID)) return;
            string name = __instance.Item?.itemValue?.ItemClass?.GetItemName();
            if (!IsContract(name)) return;
            int tier = Tier(name);
            string heading = tier >= 16 ? "T" + tier : (tier / 3) + "★";
            string kind = Localization.Get("aecRelayCatalogKind" + Kind(name));
            value = "[66BBFF][" + heading + " · " + kind + "][-] " + CleanName(value);
        }
        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(XUiC_TraderWindow), "FilterByName"),
                postfix: new HarmonyMethod(typeof(RelayCatalogDisplay), nameof(AfterFilter)));
            harmony.Patch(AccessTools.Method(typeof(XUiC_TraderItemEntry), "GetBindingValueInternal"),
                postfix: new HarmonyMethod(typeof(RelayCatalogDisplay), nameof(ItemNamePostfix)));
        }
    }
}
