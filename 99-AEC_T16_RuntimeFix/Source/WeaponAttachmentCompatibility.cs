using System;
using System.Collections.Generic;
using HarmonyLib;

namespace AECT16RuntimeFix
{
    // Native attachment overrides use exact item IDs, not Extends. Reused
    // weapon models need their original sound, scope and barrel transforms.
    public static class WeaponAttachmentCompatibility
    {
        private static readonly Dictionary<string, string> Models = new Dictionary<string, string>
        {
            { "gunPZAECEmberPistol", "gunHandgunT3DesertVulture" },
            { "gunPZAECHorizonNeedle", "gunRifleT3SniperRifle" },
            { "gunPZAECStormReservoir", "gunMGT3M60" },
            { "gunPZAECBastionShotgun", "gunShotgunT3AutoShotgun" },
            { "gunPZAECEchoRepeater", "gunBowT3CompoundCrossbow" },
            { "gunPZAECCounterSiege", "gunExplosivesT3RocketLauncher" },
            { "meleePZAECFaultlineHammer", "meleeWpnSledgeT3SteelSledgehammer" }
        };

        public static string Model(string name)
        {
            if (name == null || name.Length < 4) return null;
            string tier = name.Substring(name.Length - 3);
            if (tier != "T16" && tier != "T17" && tier != "T18" && tier != "T19") return null;
            string model;
            return Models.TryGetValue(name.Substring(0, name.Length - 3), out model) ? model : null;
        }

        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(ItemClassModifier), "GetPropertyOverride"),
                prefix: new HarmonyMethod(typeof(WeaponAttachmentCompatibility), nameof(Prefix)));
        }

        public static bool Prefix(ItemClassModifier __instance, string __0, string __1, ref string __2, ref bool __result)
        {
            string model = Model(__1);
            if (model == null) return true;
            DynamicProperties properties;
            // A dedicated override for this new weapon always wins. Otherwise
            // prefer its model-specific value to the native wildcard fallback.
            if (__instance.PropertyOverrides.TryGetValue(__1, out properties) && properties.Values.ContainsKey(__0)) return true;
            string value;
            if (!__instance.PropertyOverrides.TryGetValue(model, out properties) || !properties.Values.TryGetValue(__0, out value)) return true;
            __2 = value;
            __result = true;
            return false;
        }
    }
}
