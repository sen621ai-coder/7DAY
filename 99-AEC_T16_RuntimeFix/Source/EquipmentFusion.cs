using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AECT16RuntimeFix
{
    public static class EquipmentFusion
    {
        public const string RankKey = "AECFusionRank";
        public const int MaxRank = 1000; // Beyond feasible 2^rank material counts; bounds corrupt metadata.
        private static readonly Regex Supported = new Regex(
            @"^(armorPZAEC(Harrier|Storm|Tremor|Warden)(Helmet|Outfit|Gloves|Boots)|gunPZAEC(EmberPistol|HorizonNeedle|StormReservoir|BastionShotgun|EchoRepeater|CounterSiege)|meleePZAECFaultlineHammer)T1[6-9]$",
            RegexOptions.CultureInvariant);

        public static bool IsFusionItem(ItemValue item)
        {
            return item != null && !item.IsEmpty() && IsFusionClass(item.ItemClass);
        }

        public static bool IsFusionClass(ItemClass item)
        { return item != null && Supported.IsMatch(item.GetItemName()); }

        public static double Rank(ItemValue item)
        {
            if (!IsFusionItem(item)) return 0;
            int legacy;
            if (item.TryGetMetadata(RankKey, out legacy)) return legacy > 0 && legacy <= MaxRank ? legacy : 0;
            string text; double rank;
            return item.TryGetMetadata(RankKey, out text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out rank)
                && rank > 0 && rank <= MaxRank ? rank : 0;
        }

        // Equivalent legacy rank keeps old saves and their exact stat curves.
        // Store fractional progress as round-trip text supported by native metadata.
        public static void SetRank(ItemValue item, double rank)
        { item.SetMetadata(RankKey, Math.Min(MaxRank, Math.Max(0, rank)).ToString("R", CultureInfo.InvariantCulture)); }

        public static double CombinedRank(double a, double b)
        {
            double high = Math.Max(a, b), low = Math.Min(a, b);
            if (high == low) return Math.Min(MaxRank, high + 1);
            return Math.Min(MaxRank, high + Math.Log(1 + .05 * Math.Pow(1.05, low - high)) / Math.Log(1.05));
        }

        public static ItemStack Keeper(ItemStack a, ItemStack b)
        { return Rank(b.itemValue) > Rank(a.itemValue) ? b : a; }

        public static string FormatRank(double rank)
        { return rank.ToString("0.###", CultureInfo.InvariantCulture); }

        public static bool HasAttachments(ItemValue item)
        {
            return HasItems(item.Modifications) || HasItems(item.CosmeticMods);
        }

        private static bool HasItems(ItemValue[] items)
        {
            if (items == null) return false;
            foreach (var item in items) if (item != null && !item.IsEmpty()) return true;
            return false;
        }

        public static string Validate(ItemStack primary, ItemStack donor)
        {
            if (primary == null || donor == null || primary.IsEmpty() || donor.IsEmpty()) return "放入两件同名同阶装备";
            if (!IsFusionItem(primary.itemValue) || !IsFusionItem(donor.itemValue)) return "仅限T16–T19新武器和护甲";
            if (primary.count != 1 || donor.count != 1) return "每个槽位放入一件装备";
            if (ReferenceEquals(primary, donor) || ReferenceEquals(primary.itemValue, donor.itemValue)) return "需要两件独立装备";
            if (primary.itemValue.type != donor.itemValue.type) return "装备名称和T阶必须相同";
            var keeper = Keeper(primary, donor);
            var consumed = ReferenceEquals(keeper, primary) ? donor : primary;
            if (Rank(keeper.itemValue) >= MaxRank) return "已达到数值安全上限";
            if (CombinedRank(Rank(primary.itemValue), Rank(donor.itemValue)) <= Rank(keeper.itemValue)) return "材料强化过低，已低于数值精度";
            if (HasAttachments(consumed.itemValue)) return "请先拆下被消耗装备的模组和染色（低强化者；相同则第二件）";
            if (primary.itemValue.Meta != 0 || donor.itemValue.Meta != 0) return "请先卸下两件武器中的弹药";
            return null;
        }

        // Pure preview: never mutates either input or consumes inventory.
        public static bool TryCreate(ItemStack primary, ItemStack donor, out ItemStack output)
        {
            output = ItemStack.Empty.Clone();
            if (Validate(primary, donor) != null) return false;
            var keeper = Keeper(primary, donor);
            var value = keeper.itemValue.Clone();
            SetRank(value, CombinedRank(Rank(primary.itemValue), Rank(donor.itemValue)));
            // Preserve wear proportion as maximum durability increases; fusion
            // does not silently repair the primary item or discard its mods.
            float previousMax = keeper.itemValue.MaxUseTimes;
            value.UseTimes = previousMax > 0 ? keeper.itemValue.UseTimes * value.MaxUseTimes / previousMax : keeper.itemValue.UseTimes;
            output = new ItemStack(value, 1);
            return true;
        }

        public static string Label(ItemValue item)
        {
            double rank = Rank(item);
            return rank == 0 ? "" : " [融合+" + FormatRank(rank) + "]";
        }
    }
}
