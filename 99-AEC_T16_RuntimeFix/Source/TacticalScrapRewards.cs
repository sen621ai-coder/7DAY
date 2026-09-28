using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;

namespace AECT16RuntimeFix
{
    public static class TacticalScrapRewards
    {
        private static readonly Regex armor = new Regex(@"^armorPZAEC(?:Harrier|Storm|Tremor|Warden)(?:Helmet|Outfit|Gloves|Boots)T(16|17|18|19)$", RegexOptions.CultureInvariant);
        private static readonly Regex weapon = new Regex(@"^(?:gunPZAEC(?:EmberPistol|HorizonNeedle|StormReservoir|BastionShotgun|EchoRepeater|CounterSiege)|meleePZAECFaultlineHammer)T(16|17|18|19)$", RegexOptions.CultureInvariant);
        private static readonly System.Random random = new System.Random();

        public static bool Classify(string name, out string component, out int amount)
        {
            component = null;
            amount = 0;
            if (string.IsNullOrEmpty(name)) return false;
            var match = armor.Match(name);
            amount = 1;
            if (!match.Success) { match = weapon.Match(name); amount = 2; }
            if (!match.Success) { amount = 0; return false; }
            component = "PZAECBuildPartsR" + (int.Parse(match.Groups[1].Value) - 14);
            return true;
        }

        // Recipe.ingredients, including the source item and consumed count, are
        // serialized by the native queue. Roll only when one queue unit commits.
        // No preview, enqueue, cancellation or recipe-generation patch is installed.
        public static int Roll(Recipe recipe, Func<bool> success, out string component)
        {
            component = null;
            if (recipe == null || !recipe.IsScrap || recipe.ingredients == null || recipe.ingredients.Count != 1) return 0;
            var source = recipe.ingredients[0];
            int amount;
            if (source == null || source.count <= 0 || source.itemValue == null ||
                source.itemValue.ItemClass == null ||
                !Classify(source.itemValue.ItemClass.GetItemName(), out component, out amount)) return 0;
            int total = 0;
            for (int i = 0; i < source.count; i++) if (success()) total += amount;
            return total;
        }

        private static bool CoinFlip() { lock (random) return random.Next(2) == 0; }
        private static ItemStack Reward(Recipe recipe)
        {
            string component;
            int count = Roll(recipe, CoinFlip, out component);
            if (count == 0) return null;
            var value = ItemClass.GetItem(component);
            if (value == null || value.IsEmpty()) throw new InvalidOperationException("Missing tactical component: " + component);
            return new ItemStack(value, count);
        }

        public static void CompleteUI(XUiC_RecipeStack stack)
        {
            try
            {
                var reward = Reward(stack.GetRecipe());
                if (reward == null) return;
                var xui = stack.xui;
                var grid = stack.WindowGroup.Controller.GetChildByType<XUiC_WorkstationOutputGrid>();
                if (grid != null)
                {
                    var slots = grid.GetSlots();
                    if (ItemStack.AddToItemStackArray(slots, reward, -1) != -1)
                    {
                        grid.SetSlots(slots);
                        grid.UpdateData(slots);
                        grid.IsDirty = true;
                        return;
                    }
                }
                // Preserve the native scrap result even when the bonus needs an
                // additional slot. Drop only the remainder after any partial add.
                xui.PlayerInventory.AddItem(reward, true);
                if (reward.count > 0) xui.PlayerInventory.DropItem(reward);
            }
            catch (Exception ex) { T16RuntimeFixMod.SafeLog("[AEC-Scrap] Completion bonus failed: " + ex); }
        }

        public static void CompleteQueued(TileEntityWorkstation station, RecipeQueueItem queue)
        {
            try
            {
                var reward = Reward(queue.Recipe);
                if (reward == null) return;
                if (ItemStack.AddToItemStackArray(station.Output, reward, -1) == -1)
                    GameManager.Instance.ItemDropServer(reward, station.ToWorldCenterPos() + Vector3.up,
                        Vector3.zero, queue.StartingEntityId, 300f, false);
                station.SetModified();
            }
            catch (Exception ex) { T16RuntimeFixMod.SafeLog("[AEC-Scrap] Workstation bonus failed: " + ex); }
        }

        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(XUiC_RecipeStack), "Update"),
                transpiler: new HarmonyMethod(typeof(TacticalScrapRewards), nameof(UITranspiler)));
            harmony.Patch(AccessTools.Method(typeof(TileEntityWorkstation), "HandleRecipeQueue"),
                transpiler: new HarmonyMethod(typeof(TacticalScrapRewards), nameof(QueueTranspiler)));
            T16RuntimeFixMod.SafeLog("[AEC-Scrap] T16-T19 completed scraps: 50% armor +1 / weapon +2 same-rank tactical components.");
        }

        public static IEnumerable<CodeInstruction> UITranspiler(IEnumerable<CodeInstruction> instructions) { return Inject(instructions, false); }
        public static IEnumerable<CodeInstruction> QueueTranspiler(IEnumerable<CodeInstruction> instructions) { return Inject(instructions, true); }
        private static IEnumerable<CodeInstruction> Inject(IEnumerable<CodeInstruction> instructions, bool queued)
        {
            var codes = instructions.ToList();
            var counter = AccessTools.Field(queued ? typeof(RecipeQueueItem) : typeof(XUiC_RecipeStack), queued ? "Multiplier" : "recipeCount");
            int patched = 0;
            for (int i = 0; i < codes.Count; i++)
            {
                yield return codes[i];
                if (codes[i].opcode != OpCodes.Stfld || !Equals(codes[i].operand, counter)) continue;
                int sub = i - (queued ? 2 : 1);
                if (sub < 2 || codes[sub].opcode != OpCodes.Sub || codes[sub - 1].opcode != OpCodes.Ldc_I4_1 ||
                    codes[sub - 2].opcode != OpCodes.Ldfld || !Equals(codes[sub - 2].operand, counter) ||
                    (queued && codes[i - 1].opcode != OpCodes.Conv_I2))
                    throw new InvalidOperationException("Native scrap completion counter changed");
                // The original output has succeeded and the queue counter has
                // already decremented; full-output retries never reach this point.
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                if (queued) yield return new CodeInstruction(OpCodes.Ldloc_0);
                yield return CodeInstruction.Call(typeof(TacticalScrapRewards), queued ? nameof(CompleteQueued) : nameof(CompleteUI));
                patched++;
            }
            if (patched != (queued ? 1 : 4)) throw new InvalidOperationException("Native scrap completion paths changed: " + patched);
        }
    }
}
