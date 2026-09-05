using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace AECT16RuntimeFix
{
    // Scale the fused item's own XML values before native interpolation and
    // stacking. Ammo, attached mods, player perks, buffs and other worn items
    // retain their own scopes. The same path handles detailed stat previews.
    public static class FusionStatScaling
    {
        [ThreadStatic] private static ItemValue currentItem;
        [ThreadStatic] private static int currentRank;
        [ThreadStatic] private static PassiveEffect currentEffect;
        private static readonly Dictionary<PassiveEffect, Dictionary<int,float[]>> Cache = new Dictionary<PassiveEffect, Dictionary<int,float[]>>();

        public static void Install(Harmony harmony)
        {
            foreach (string name in new[] { "ModifyValue", "GetModifiedValueData" })
            {
                PatchScope(harmony, typeof(ItemValue), name, nameof(ItemPrefix), nameof(ItemFinalizer));
                PatchScope(harmony, typeof(MinEffectController), name, nameof(ControllerPrefix), nameof(ControllerFinalizer));
                PatchScope(harmony, typeof(PassiveEffect), name, nameof(EffectPrefix), nameof(EffectFinalizer));
            }
            harmony.Patch(AccessTools.Method(typeof(PassiveEffect), "ModValue"),
                prefix: new HarmonyMethod(typeof(FusionStatScaling), nameof(ValuesPrefix)));
        }

        private static void PatchScope(Harmony harmony, Type type, string name, string prefix, string finalizer)
        {
            var method = AccessTools.Method(type, name);
            if (method == null) throw new MissingMethodException(type.FullName, name);
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(FusionStatScaling), prefix),
                finalizer: new HarmonyMethod(typeof(FusionStatScaling), finalizer));
        }

        public static void ItemPrefix(ItemValue __instance, out ItemValue __state)
        { __state = currentItem; currentItem = __instance; }
        public static void ItemFinalizer(ItemValue __state) { currentItem = __state; }
        public static void ControllerPrefix(MinEffectController __instance, out int __state)
        {
            __state = currentRank;
            currentRank = currentItem != null && currentItem.ItemClass != null && ReferenceEquals(currentItem.ItemClass.Effects, __instance)
                ? EquipmentFusion.Rank(currentItem) : 0;
        }
        public static void ControllerFinalizer(int __state) { currentRank = __state; }
        public static void EffectPrefix(PassiveEffect __instance, out PassiveEffect __state)
        { __state = currentEffect; currentEffect = __instance; }
        public static void EffectFinalizer(PassiveEffect __state) { currentEffect = __state; }

        public static bool IsStructural(PassiveEffects effect)
        {
            return effect == PassiveEffects.None || effect == PassiveEffects.ModSlots || effect == PassiveEffects.Tier ||
                effect == PassiveEffects.CraftingTier || effect == PassiveEffects.RecipeTagUnlocked || effect == PassiveEffects.EconomicValue;
        }

        public static bool LowerIsBetter(PassiveEffects effect)
        {
            string name = effect.ToString();
            return name.StartsWith("Spread", StringComparison.Ordinal) || name.StartsWith("KickDegrees", StringComparison.Ordinal) ||
                effect == PassiveEffects.IncrementalSpreadMultiplier || effect == PassiveEffects.StaminaLoss ||
                effect == PassiveEffects.DegradationPerUse || effect == PassiveEffects.NoiseMultiplier ||
                effect == PassiveEffects.ScavengingTime || effect == PassiveEffects.FoodLossPerStaminaPointGained ||
                effect == PassiveEffects.WaterLossPerStaminaPointGained;
        }

        public static float Scale(PassiveEffects effect, PassiveEffect.ValueModifierTypes operation, float value, int rank)
        {
            if (rank <= 0 || IsStructural(effect) || value == 0f) return value;
            bool lower = LowerIsBetter(effect);
            bool subtract = operation == PassiveEffect.ValueModifierTypes.base_subtract || operation == PassiveEffect.ValueModifierTypes.perc_subtract;
            bool set = operation == PassiveEffect.ValueModifierTypes.base_set || operation == PassiveEffect.ValueModifierTypes.perc_set;
            bool improveMagnitude = set ? !lower : ((value < 0f) ^ subtract ^ lower) == false;
            // A signed recoil endpoint is a magnitude/direction, not a penalty.
            if (set && lower) improveMagnitude = false;
            double multiplier = Math.Pow(improveMagnitude ? 1.05 : .95, Math.Min(rank, EquipmentFusion.MaxRank));
            return (float)Math.Max(-1e30, Math.Min(1e30, value * multiplier));
        }

        public static void ValuesPrefix(ref float[] _values)
        {
            var effect = currentEffect;
            if (currentRank <= 0 || effect == null || IsStructural(effect.Type) || _values == null ||
                !ReferenceEquals(_values, effect.Values)) return;
            // CVar-backed definitions refresh Values in-place for each entity.
            // Never cache those values across players or successive calls.
            if (effect.CVarValues != null)
            {
                var dynamicValues = new float[_values.Length];
                for (int i = 0; i < dynamicValues.Length; i++) dynamicValues[i] = Scale(effect.Type, effect.Modifier, _values[i], currentRank);
                _values = dynamicValues;
                return;
            }
            lock (Cache)
            {
                Dictionary<int,float[]> ranks;
                if (!Cache.TryGetValue(effect, out ranks)) Cache[effect] = ranks = new Dictionary<int,float[]>();
                float[] scaled;
                if (!ranks.TryGetValue(currentRank, out scaled))
                {
                    scaled = new float[_values.Length];
                    for (int i = 0; i < scaled.Length; i++) scaled[i] = Scale(effect.Type, effect.Modifier, _values[i], currentRank);
                    ranks[currentRank] = scaled;
                }
                _values = scaled;
            }
        }
    }
}
