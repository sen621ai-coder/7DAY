using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace AECT16RuntimeFix
{
    public static class FusionTierUpgrade
    {
        public static bool IsHigherSameFamily(ItemValue source, ItemValue target)
        {
            if (!EquipmentFusion.IsFusionItem(source) || !EquipmentFusion.IsFusionItem(target)) return false;
            string a = source.ItemClass.GetItemName(), b = target.ItemClass.GetItemName();
            return a.Substring(0, a.Length - 2) == b.Substring(0, b.Length - 2) &&
                int.Parse(a.Substring(a.Length - 2)) < int.Parse(b.Substring(b.Length - 2));
        }

        // Native quality-bearing recipes retain the actual consumed ItemValues
        // in ingredients and serialize them with the queue. Do not inspect the
        // player's current bag or modify the shared crafting recipe definition.
        public static ItemValue Apply(ItemValue output, Recipe recipe)
        {
            if (recipe == null || recipe.IsScrap || recipe.count != 1 || output == null ||
                output.type != recipe.itemValueType || !EquipmentFusion.IsFusionItem(output) || recipe.ingredients == null) return output;
            ItemValue source = null;
            foreach (var ingredient in recipe.ingredients)
            {
                if (ingredient == null || !EquipmentFusion.IsFusionItem(ingredient.itemValue)) continue;
                if (source != null || ingredient.count != 1 || !IsHigherSameFamily(ingredient.itemValue, output)) return output;
                source = ingredient.itemValue;
            }
            if (source != null) output.SetMetadata(EquipmentFusion.RankKey, EquipmentFusion.Rank(source) / 5);
            return output;
        }

        public static ItemValue ApplyUI(ItemValue output, XUiC_RecipeStack stack)
        { return Apply(output, stack.GetRecipe()); }
        public static ItemValue ApplyQueued(ItemValue output, RecipeQueueItem queue)
        { return queue == null || queue.Multiplier != 1 ? output : Apply(output, queue.Recipe); }

        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(XUiC_RecipeStack), "outputStack"),
                transpiler: new HarmonyMethod(typeof(FusionTierUpgrade), nameof(UITranspiler)));
            harmony.Patch(AccessTools.Method(typeof(TileEntityWorkstation), "HandleRecipeQueue"),
                transpiler: new HarmonyMethod(typeof(FusionTierUpgrade), nameof(QueueTranspiler)));
        }

        public static IEnumerable<CodeInstruction> UITranspiler(IEnumerable<CodeInstruction> instructions)
        { return Inject(instructions, false); }
        public static IEnumerable<CodeInstruction> QueueTranspiler(IEnumerable<CodeInstruction> instructions)
        { return Inject(instructions, true); }
        private static IEnumerable<CodeInstruction> Inject(IEnumerable<CodeInstruction> instructions, bool queued)
        {
            int patched = 0;
            foreach (var code in instructions)
            {
                yield return code;
                var ctor = code.operand as ConstructorInfo;
                if (code.opcode != OpCodes.Newobj || ctor == null || ctor.DeclaringType != typeof(ItemValue)) continue;
                if (!queued && ctor.GetParameters().Length != 6) continue;
                yield return new CodeInstruction(queued ? OpCodes.Ldloc_0 : OpCodes.Ldarg_0);
                yield return CodeInstruction.Call(typeof(FusionTierUpgrade), queued ? nameof(ApplyQueued) : nameof(ApplyUI));
                patched++;
            }
            if (patched != (queued ? 2 : 1)) throw new InvalidOperationException("Native fusion upgrade output construction changed: " + patched);
        }
    }
}
