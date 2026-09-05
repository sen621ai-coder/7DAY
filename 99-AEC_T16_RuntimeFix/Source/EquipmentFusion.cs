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

        public static int Rank(ItemValue item)
        {
            int rank;
            return IsFusionItem(item) && item.TryGetMetadata(RankKey, out rank) && rank > 0 && rank <= MaxRank ? rank : 0;
        }

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
            if (Rank(primary.itemValue) != Rank(donor.itemValue)) return "两件装备的融合次数必须相同";
            if (Rank(primary.itemValue) >= MaxRank) return "已达到数值安全上限";
            if (HasAttachments(donor.itemValue)) return "请先拆下第二件装备的模组和染色";
            if (primary.itemValue.Meta != 0 || donor.itemValue.Meta != 0) return "请先卸下两件武器中的弹药";
            return null;
        }

        // Pure preview: never mutates either input or consumes inventory.
        public static bool TryCreate(ItemStack primary, ItemStack donor, out ItemStack output)
        {
            output = ItemStack.Empty.Clone();
            if (Validate(primary, donor) != null) return false;
            var value = primary.itemValue.Clone();
            value.SetMetadata(RankKey, Rank(primary.itemValue) + 1);
            // Preserve wear proportion as maximum durability increases; fusion
            // does not silently repair the primary item or discard its mods.
            float previousMax = primary.itemValue.MaxUseTimes;
            value.UseTimes = previousMax > 0 ? primary.itemValue.UseTimes * value.MaxUseTimes / previousMax : primary.itemValue.UseTimes;
            output = new ItemStack(value, 1);
            return true;
        }

        public static string Label(ItemValue item)
        {
            int rank = Rank(item);
            return rank == 0 ? "" : " [融合+" + rank.ToString(CultureInfo.InvariantCulture) + "]";
        }
    }
}
