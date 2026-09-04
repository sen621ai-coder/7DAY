using System;
using System.Collections.Generic;
using HarmonyLib;

namespace AECT16RuntimeFix
{
    /// <summary>
    /// Keeps very large positive effect totals inside the range used by the
    /// game's many float-to-int conversions. Without saturation, conv.i4 turns
    /// an out-of-range positive float into Int32.MinValue.
    /// </summary>
    public static class PassiveEffectOverflowGuard
    {
        private const float DamageCeiling = 100000000f;
        private const float VitalCeiling = 1000000000f;
        private const float QuantityCeiling = 1000000f;
        private const float RewardCeiling = 100000000f;
        private const float MultiplierCeiling = 10000f;
        private const float RecoilDegreeCeiling = 360f;

        private static readonly HashSet<PassiveEffects> Reported = new HashSet<PassiveEffects>();

        public static void Install(Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(typeof(EffectManager), nameof(EffectManager.GetValue));
                if (target == null) throw new MissingMethodException("EffectManager", "GetValue");
                harmony.Patch(target,
                    postfix: new HarmonyMethod(typeof(PassiveEffectOverflowGuard), nameof(AfterGetValue)));
                T16RuntimeFixMod.SafeLog("[AEC-Overflow-Fix] Passive-effect saturation active.");
            }
            catch (Exception ex)
            {
                T16RuntimeFixMod.SafeLog("[AEC-Overflow-Fix] Patch installation failed: " +
                    ex.GetBaseException().Message);
            }
        }

        public static void AfterGetValue(PassiveEffects _passiveEffect, float _originalValue, ref float __result)
        {
            float corrected = ClampValue(_passiveEffect, _originalValue, __result);
            if (corrected == __result || (float.IsNaN(corrected) && float.IsNaN(__result))) return;
            Report(_passiveEffect, __result, corrected);
            __result = corrected;
        }

        public static float ClampValue(PassiveEffects effect, float value)
        {
            return ClampValue(effect, value, value);
        }

        public static float ClampValue(PassiveEffects effect, float originalValue, float value)
        {
            int rule = Rule(effect);
            if (rule == 0) return value;

            // Recoil minima can intentionally be negative to aim the kick to
            // the left or down. Keep that direction, but never let stacked
            // reductions cross zero and reverse it.
            if (rule == 7)
            {
                if (float.IsNaN(value)) return 0f;
                if (originalValue > 0f)
                {
                    if (value < 0f || float.IsNegativeInfinity(value)) return 0f;
                    if (value > RecoilDegreeCeiling || float.IsPositiveInfinity(value))
                        return RecoilDegreeCeiling;
                }
                else if (originalValue < 0f)
                {
                    if (value > 0f || float.IsPositiveInfinity(value)) return 0f;
                    if (value < -RecoilDegreeCeiling || float.IsNegativeInfinity(value))
                        return -RecoilDegreeCeiling;
                }
                else
                {
                    if (float.IsPositiveInfinity(value) || value > RecoilDegreeCeiling)
                        return RecoilDegreeCeiling;
                    if (float.IsNegativeInfinity(value) || value < -RecoilDegreeCeiling)
                        return -RecoilDegreeCeiling;
                }
                return value;
            }

            // Spread and spread multipliers describe magnitudes. If stacked
            // reductions cross zero, use the item's native value instead of
            // zero: a zero timing/magnitude can break weapon state machines.
            if (rule == 6)
            {
                if (float.IsNaN(value) || float.IsNegativeInfinity(value) || value < 0f)
                    return FallbackToOriginal(originalValue, 0f, MultiplierCeiling);
                if (float.IsPositiveInfinity(value) || value > MultiplierCeiling)
                    return MultiplierCeiling;
                return value;
            }

            // Multipliers may legitimately be negative debuffs. Limit only
            // their positive side so those mechanics keep their meaning.
            if (rule == 5)
            {
                if (float.IsPositiveInfinity(value) || value > MultiplierCeiling)
                    return MultiplierCeiling;
                return value;
            }

            float minimum = 0f;
            float maximum = rule == 1 ? DamageCeiling :
                rule == 2 ? VitalCeiling :
                rule == 3 ? QuantityCeiling : RewardCeiling;

            if (float.IsNaN(value) || float.IsNegativeInfinity(value) || value < minimum)
                return FallbackToOriginal(originalValue, minimum, maximum);
            if (float.IsPositiveInfinity(value) || value > maximum)
                return maximum;
            return value;
        }

        private static float FallbackToOriginal(float originalValue, float minimum, float maximum)
        {
            if (float.IsNaN(originalValue) || float.IsNegativeInfinity(originalValue) ||
                originalValue < minimum)
                return minimum;
            if (float.IsPositiveInfinity(originalValue) || originalValue > maximum)
                return maximum;
            return originalValue;
        }

        // Only final values which have no valid negative meaning are guarded.
        // Percent reductions and penalties remain untouched.
        private static int Rule(PassiveEffects effect)
        {
            switch (effect)
            {
                case PassiveEffects.EntityDamage:
                case PassiveEffects.EntityHeal:
                case PassiveEffects.BlockDamage:
                case PassiveEffects.BlockRepairAmount:
                case PassiveEffects.ExplosionBlockDamage:
                case PassiveEffects.ExplosionEntityDamage:
                case PassiveEffects.VehicleEntityDamage:
                case PassiveEffects.VehicleBlockDamage:
                case PassiveEffects.FallingBlockDamage:
                    return 1;

                case PassiveEffects.DegradationMax:
                case PassiveEffects.EconomicValue:
                case PassiveEffects.HealthMax:
                case PassiveEffects.StaminaMax:
                case PassiveEffects.FoodMax:
                case PassiveEffects.WaterMax:
                case PassiveEffects.HealthGain:
                case PassiveEffects.StaminaGain:
                case PassiveEffects.FoodGain:
                case PassiveEffects.WaterGain:
                case PassiveEffects.RepairAmount:
                case PassiveEffects.VehicleFuelMaxPer:
                case PassiveEffects.VehicleTankSize:
                case PassiveEffects.BatteryMaxLoadInVolts:
                    return 2;

                case PassiveEffects.MagazineSize:
                case PassiveEffects.BurstRoundCount:
                case PassiveEffects.RoundRayCount:
                case PassiveEffects.RoundsPerMinute:
                case PassiveEffects.AttacksPerMinute:
                case PassiveEffects.ModSlots:
                case PassiveEffects.VehicleCarryCapacity:
                case PassiveEffects.VehicleSeats:
                case PassiveEffects.CarryCapacity:
                case PassiveEffects.BagSize:
                case PassiveEffects.LootQuantity:
                case PassiveEffects.CraftingOutputCount:
                case PassiveEffects.ActiveCraftingSlots:
                case PassiveEffects.CraftingSlots:
                case PassiveEffects.HarvestCount:
                case PassiveEffects.EntityPenetrationCount:
                    return 3;

                case PassiveEffects.SkillExpGain:
                case PassiveEffects.PlayerExpGain:
                case PassiveEffects.ExperienceGain:
                case PassiveEffects.ModPowerBonus:
                case PassiveEffects.QuestBonusItemReward:
                case PassiveEffects.QuestRewardOptionCount:
                case PassiveEffects.QuestRewardChoiceCount:
                    return 4;

                case PassiveEffects.DamageModifier:
                case PassiveEffects.HeadshotDamageModifier:
                case PassiveEffects.InternalDamageModifier:
                case PassiveEffects.DamageBonus:
                case PassiveEffects.GrazeDamageMultiplier:
                case PassiveEffects.ExplosionIncomingDamage:
                    return 5;

                case PassiveEffects.IncrementalSpreadMultiplier:
                case PassiveEffects.SpreadMultiplierHip:
                case PassiveEffects.SpreadMultiplierAiming:
                case PassiveEffects.SpreadMultiplierRunning:
                case PassiveEffects.SpreadMultiplierWalking:
                case PassiveEffects.SpreadMultiplierCrouching:
                case PassiveEffects.SpreadMultiplierIdle:
                case PassiveEffects.SpreadDegreesVertical:
                case PassiveEffects.SpreadDegreesHorizontal:
                    return 6;

                case PassiveEffects.KickDegreesVerticalMin:
                case PassiveEffects.KickDegreesHorizontalMin:
                case PassiveEffects.KickDegreesVerticalMax:
                case PassiveEffects.KickDegreesHorizontalMax:
                    return 7;

                default:
                    return 0;
            }
        }

        private static void Report(PassiveEffects effect, float before, float after)
        {
            lock (Reported)
            {
                if (!Reported.Add(effect)) return;
            }
            T16RuntimeFixMod.SafeLog("[AEC-Overflow-Fix] Saturated " + effect +
                ": " + before + " -> " + after + ".");
        }
    }
}
