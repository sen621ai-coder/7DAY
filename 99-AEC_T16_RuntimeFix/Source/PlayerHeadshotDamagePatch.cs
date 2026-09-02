using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace AECT16RuntimeFix
{
    // Multiply the already-calculated headshot, not base weapon damage. This
    // preserves flat rifle bonuses, perks and the world's sandbox multiplier.
    public static class PlayerHeadshotDamagePatch
    {
        public static void Install(Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(typeof(ItemActionAttack), "Hit");
                if (target == null) throw new MissingMethodException("ItemActionAttack", "Hit");
                harmony.Patch(target, transpiler: new HarmonyMethod(typeof(PlayerHeadshotDamagePatch), nameof(Transpiler)));
                T16RuntimeFixMod.SafeLog("[AEC-Headshot] Player weapon headshot damage x5 active; existing bonuses preserved.");
            }
            catch (Exception ex)
            {
                T16RuntimeFixMod.SafeLog("[AEC-Headshot] Patch installation failed: " + ex.GetBaseException().Message);
            }
        }

        public static int ScaleDamage(int damage, bool playerAttack)
        {
            if (!playerAttack || damage <= 0) return damage;
            // Avoid wrapping extremely high endgame damage to a negative value.
            return damage > int.MaxValue / 5 ? int.MaxValue : damage * 5;
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            int resultIndex = -1;
            for (int i = 0; i + 5 < code.Count; i++)
            {
                var field = code[i].operand as FieldInfo;
                if (code[i].opcode != OpCodes.Ldsfld || field == null ||
                    field.DeclaringType != typeof(ItemActionAttack) || field.Name != "HeadshotMultiplier" ||
                    !code[i + 1].IsLdloc() || code[i + 2].opcode != OpCodes.Conv_R4 ||
                    code[i + 3].opcode != OpCodes.Mul || code[i + 4].opcode != OpCodes.Conv_I4 ||
                    !code[i + 5].IsStloc()) continue;
                if (resultIndex >= 0) throw new InvalidOperationException("Ambiguous headshot result site.");
                resultIndex = i + 4;
            }

            int headshotQuery = -1;
            for (int i = 0; i + 5 < resultIndex; i++)
            {
                if (code[i].opcode == OpCodes.Ldc_I4 && code[i].operand is int &&
                    (int)code[i].operand == (int)PassiveEffects.HeadshotDamageModifier &&
                    code[i + 1].IsLdarg() && code[i + 2].IsLdloc() &&
                    code[i + 3].opcode == OpCodes.Conv_R4 && code[i + 4].IsLdloc() &&
                    code[i + 5].opcode == OpCodes.Ldnull)
                {
                    if (headshotQuery >= 0) throw new InvalidOperationException("Ambiguous headshot attacker load.");
                    headshotQuery = i;
                }
            }

            int effectCall = -1;
            for (int i = headshotQuery + 1; headshotQuery >= 0 && i < resultIndex; i++)
            {
                var method = code[i].operand as MethodInfo;
                if (method != null && method.DeclaringType == typeof(EffectManager) && method.Name == "GetValue")
                    effectCall = i;
            }
            // The query must immediately feed the game's original multiplier.
            if (resultIndex < 0 || headshotQuery < 0 || effectCall != resultIndex - 7 ||
                code[effectCall + 1].opcode != OpCodes.Conv_I4 || !code[effectCall + 2].IsStloc())
                throw new InvalidOperationException("Unsupported headshot IL; no damage change applied.");

            var attacker = code[headshotQuery + 4];
            code.InsertRange(resultIndex + 1, new[]
            {
                new CodeInstruction(attacker.opcode, attacker.operand),
                new CodeInstruction(OpCodes.Isinst, typeof(EntityPlayer)),
                new CodeInstruction(OpCodes.Ldnull),
                new CodeInstruction(OpCodes.Cgt_Un),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(PlayerHeadshotDamagePatch), nameof(ScaleDamage)))
            });
            return code;
        }
    }
}
