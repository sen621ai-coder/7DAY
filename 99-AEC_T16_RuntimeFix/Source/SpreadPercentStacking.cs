using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace AECT16RuntimeFix
{
    // Compose spread percentages per passive, after native requirements, tags
    // and CVar resolution. No shared accumulator or second effect query is used.
    public static class SpreadPercentStacking
    {
        public static void Install(Harmony harmony)
        {
            try
            {
                harmony.Patch(AccessTools.Method(typeof(PassiveEffect), nameof(PassiveEffect.ModifyValue)),
                    transpiler: new HarmonyMethod(typeof(SpreadPercentStacking), nameof(Transpiler)));
                T16RuntimeFixMod.SafeLog("[AEC-Spread-Fix] Spread percentages now stack multiplicatively.");
            }
            catch (Exception ex)
            {
                T16RuntimeFixMod.SafeLog("[AEC-Spread-Fix] Installation failed: " + ex.GetBaseException().Message);
            }
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var native = AccessTools.Method(typeof(PassiveEffect), nameof(PassiveEffect.ModValue));
            var replacement = AccessTools.Method(typeof(SpreadPercentStacking), nameof(Apply));
            var code = new List<CodeInstruction>();
            int replaced = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(native))
                {
                    var load = new CodeInstruction(OpCodes.Ldarg_0);
                    load.labels.AddRange(instruction.labels);
                    load.blocks.AddRange(instruction.blocks);
                    code.Add(load);
                    code.Add(new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(PassiveEffect), nameof(PassiveEffect.Type))));
                    code.Add(new CodeInstruction(OpCodes.Call, replacement));
                    replaced++;
                }
                else code.Add(instruction);
            }
            if (replaced != 1) throw new InvalidOperationException("Expected one PassiveEffect.ModValue call, found " + replaced);
            return code;
        }

        public static void Apply(PassiveEffect.ValueModifierTypes modifier, float level,
            ref float baseValue, ref float percentValue, float[] levels, float[] values,
            float multiplier, int seed, PassiveEffects effect)
        {
            if (!IsSpread(effect) || (modifier != PassiveEffect.ValueModifierTypes.perc_add &&
                modifier != PassiveEffect.ValueModifierTypes.perc_subtract))
            {
                PassiveEffect.ModValue(modifier, level, ref baseValue, ref percentValue,
                    levels, values, multiplier, seed);
                return;
            }

            // Let the native evaluator handle interpolation, quality ranges,
            // random rolls and inactive ranks exactly once, starting from 1.
            float factor = 1f;
            PassiveEffect.ModValue(modifier, level, ref baseValue, ref factor,
                levels, values, multiplier, seed);
            // A single reduction of 100% or more means zero spread, not reversal.
            // Ignore invalid NaN effects; existing final-value guards handle overflow.
            if (float.IsNaN(factor)) return;
            percentValue = factor <= 0f || percentValue == 0f ? 0f : percentValue * factor;
        }

        public static bool IsSpread(PassiveEffects effect)
        {
            switch (effect)
            {
                case PassiveEffects.SpreadMultiplierHip:
                case PassiveEffects.SpreadMultiplierAiming:
                case PassiveEffects.SpreadMultiplierRunning:
                case PassiveEffects.SpreadMultiplierWalking:
                case PassiveEffects.SpreadMultiplierCrouching:
                case PassiveEffects.SpreadMultiplierIdle:
                case PassiveEffects.SpreadDegreesVertical:
                case PassiveEffects.SpreadDegreesHorizontal:
                case PassiveEffects.IncrementalSpreadMultiplier:
                    return true;
                default:
                    return false;
            }
        }
    }
}
