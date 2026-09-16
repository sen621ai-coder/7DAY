using System;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace AECT16RuntimeFix
{
    public static class RelayHuntStageGate
    {
        private static readonly Regex Names = new Regex(@"^itemPZAECRelayHunt(Dumdum|Executioner|Mechanician)T(16|17|18|19)$");
        private sealed class Label { public string Original; }
        private static readonly ConditionalWeakTable<BaseItemActionEntry, Label> Labels = new ConditionalWeakTable<BaseItemActionEntry, Label>();

        public static int RequiredStage(string name)
        {
            var match = Names.Match(name ?? "");
            if (!match.Success) return 0;
            switch (match.Groups[2].Value)
            {
                case "16": return BloodMoonSiege.T16MinGameStage;
                case "17": return BloodMoonSiege.T17MinGameStage;
                case "18": return BloodMoonSiege.T18MinGameStage;
                default: return BloodMoonSiege.T19MinGameStage;
            }
        }
        public static bool Allowed(string name, int stage) { int need = RequiredStage(name); return need == 0 || stage >= need; }
        private static bool Blocked(ItemStack stack, EntityPlayer player, out int need)
        {
            need = RequiredStage(stack?.itemValue?.ItemClass?.GetItemName());
            return need > 0 && (player == null || player.gameStage < need);
        }
        private static void Explain(EntityPlayer player, int need)
        {
            var local = player as EntityPlayerLocal;
            if (local != null)
                GameManager.ShowTooltip(local, string.Format(Localization.Get("aecRelayHuntStageDenied"), player.gameStage, need));
        }
        public static void RestoreLabel(ItemActionEntryPurchase __instance)
        {
            if (Labels.TryGetValue(__instance, out Label old)) { __instance.ActionName = old.Original; Labels.Remove(__instance); }
        }
        public static void RefreshPurchase(ItemActionEntryPurchase __instance)
        {
            var entry = __instance.ItemController as XUiC_TraderItemEntry;
            var player = entry?.xui?.playerUI?.entityPlayer;
            if (!Blocked(entry?.Item, player, out int need)) return;
            Labels.GetValue(__instance, x => new Label { Original = x.ActionName });
            __instance.Enabled = false;
            __instance.ActionName = "GS " + (player == null ? "?" : player.gameStage.ToString()) + " / " + need;
        }
        public static bool PurchasePrefix(ItemActionEntryPurchase __instance)
        {
            // Rebind immediately before checking: the selected stock entry may
            // have changed since RefreshEnabled. No money or stock is mutated.
            AccessTools.Method(typeof(ItemActionEntryPurchase), "refreshBinding").Invoke(__instance, null);
            var entry = __instance.ItemController as XUiC_TraderItemEntry;
            var player = entry?.xui?.playerUI?.entityPlayer;
            if (!Blocked(entry?.Item, player, out int need)) return true;
            Explain(player, need);
            return false;
        }
        public static void DisabledPurchase(BaseItemActionEntry __instance)
        {
            if (__instance is ItemActionEntryPurchase purchase) PurchasePrefix(purchase);
        }
        public static bool InstantPrefix(EntityAlive ent, ItemStack stack, ref bool __result)
        {
            var player = ent as EntityPlayer;
            if (!Blocked(stack, player, out int need)) return true;
            Explain(player, need);
            __result = false;
            return false;
        }
        public static bool HeldPrefix(ItemActionData _actionData, bool _bReleased)
        {
            var data = _actionData?.invData;
            int need = RequiredStage(data?.item?.GetItemName());
            var player = data?.holdingEntity as EntityPlayer;
            if (need == 0 || (player != null && player.gameStage >= need)) return true;
            if (!_bReleased) Explain(player, need);
            return false;
        }
        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(ItemActionEntryPurchase), "RefreshEnabled"),
                prefix: new HarmonyMethod(typeof(RelayHuntStageGate), nameof(RestoreLabel)),
                postfix: new HarmonyMethod(typeof(RelayHuntStageGate), nameof(RefreshPurchase)));
            harmony.Patch(AccessTools.Method(typeof(ItemActionEntryPurchase), "OnActivated"),
                prefix: new HarmonyMethod(typeof(RelayHuntStageGate), nameof(PurchasePrefix)));
            harmony.Patch(AccessTools.Method(typeof(BaseItemActionEntry), "OnDisabledActivate"),
                postfix: new HarmonyMethod(typeof(RelayHuntStageGate), nameof(DisabledPurchase)));
            harmony.Patch(AccessTools.Method(typeof(ItemActionQuest), "ExecuteInstantAction"),
                prefix: new HarmonyMethod(typeof(RelayHuntStageGate), nameof(InstantPrefix)));
            harmony.Patch(AccessTools.Method(typeof(ItemActionQuest), "ExecuteAction"),
                prefix: new HarmonyMethod(typeof(RelayHuntStageGate), nameof(HeldPrefix)));
            T16RuntimeFixMod.SafeLog("[AEC-RelayHunt] Personal game-stage purchase/use gates active: 180000/280000/380000/480000.");
        }
    }
}
