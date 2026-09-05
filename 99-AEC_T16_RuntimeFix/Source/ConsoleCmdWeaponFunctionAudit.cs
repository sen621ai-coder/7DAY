using System;
using System.Collections.Generic;
using System.Linq;

namespace AECT16RuntimeFix
{
    public class ConsoleCmdWeaponFunctionAudit : ConsoleCmdAbstract
    {
        public override string[] getCommands() { return new[] { "aecweaponcheck" }; }
        public override string getDescription() { return "Read-only native weapon range, handling, aim and ammunition audit."; }
        private int checks;
        private void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
        private static float Value(ItemValue item, PassiveEffects effect, string tags)
        { return EffectManager.GetValue(effect, item, 0, null, null, FastTags<TagGroup.Global>.Parse(tags), false, false, false, false, false, 1, false, false); }
        public override void Execute(List<string> parameters, CommandSenderInfo senderInfo)
        {
            checks = 0;
            try
            {
                int weapons = 0;
                foreach (var weapon in ItemClass.list.Where(i => i != null && WeaponAttachmentCompatibility.Model(i.GetItemName()) != null))
                {
                    weapons++;
                    string name = weapon.GetItemName(), model = WeaponAttachmentCompatibility.Model(name);
                    var item = new ItemValue(weapon.Id, 6, 6, false, null, 1);
                    var original = new ItemValue(ItemClass.GetItem(model, false).type, 1, 1, false, null, 1);
                    string tags = name.Contains("EmberPistol") ? "perkGunslinger" : name.Contains("HorizonNeedle") ? "perkDeadEye"
                        : name.Contains("StormReservoir") ? "perkMachineGunner" : name.Contains("BastionShotgun") ? "perkBoomstick"
                        : name.Contains("EchoRepeater") ? "perkArchery" : name.Contains("CounterSiege") ? "perkDemolitionsExpert" : "perkSkullCrusher";
                    bool melee = name.StartsWith("melee", StringComparison.Ordinal);
                    Check(weapon.Actions[0] != null && weapon.Actions[0].GetType() == original.ItemClass.Actions[0].GetType(), name + " firing action changed");
                    foreach (var metric in new[] { PassiveEffects.MaxRange, PassiveEffects.WeaponHandling,
                        PassiveEffects.SpreadDegreesVertical, PassiveEffects.SpreadDegreesHorizontal,
                        PassiveEffects.IncrementalSpreadMultiplier, PassiveEffects.DegradationPerUse })
                    {
                        // The original weapon also rolls random percentage bonuses.
                        // We copy its fixed base, not that random roll.
                        var foundation = original.ItemClass.Effects.EffectGroups.SelectMany(group => group.PassiveEffects)
                            .FirstOrDefault(effect => effect.Type == metric && effect.Modifier == PassiveEffect.ValueModifierTypes.base_set);
                        float expected = foundation == null ? 0 : foundation.Values[0];
                        if (expected == 0) continue; // E.g. shotgun range/spread belongs to its ammunition.
                        float actual = Value(item, metric, tags);
                        Check(Math.Abs(actual - expected) < .001f, name + " missing native " + metric + ": " + actual + " / " + expected);
                    }
                    if (!melee)
                    {
                        Check(weapon.Actions[1] is ItemActionZoom, name + " missing zoom action");
                        string ammo;
                        Check(weapon.Actions[0].Properties.Values.TryGetValue("Magazine_items", out ammo) && !string.IsNullOrEmpty(ammo), name + " missing ammunition list");
                        foreach (string entry in ammo.Split(',')) Check(!ItemClass.GetItem(entry, false).IsEmpty(), name + " missing ammunition: " + entry);
                        Check(Value(item, PassiveEffects.ReloadSpeedMultiplier, tags) > 0, name + " invalid reload speed");
                        Check(item.Modifications != null && item.Modifications.All(m => m == null || m.IsEmpty()), name + " giveself false unexpectedly installed mods");
                        item.SetMetadata(EquipmentFusion.RankKey, 25);
                        Check(Value(item, PassiveEffects.BurstRoundCount, tags) == Value(original, PassiveEffects.BurstRoundCount, tags), name + " fusion changed fire mode");
                    }
                    if (name == "gunPZAECHorizonNeedleT19")
                    {
                        item.SetMetadata(EquipmentFusion.RankKey, 0);
                        Check(Value(item, PassiveEffects.MaxRange, tags) == 150, "T19 rifle maximum range");
                        Check(Value(item, PassiveEffects.DamageFalloffRange, tags) == 80, "T19 rifle effective range");
                        Check(Math.Abs(Value(item, PassiveEffects.SpreadMultiplierAiming, tags) - .05f) < .0001f, "T19 rifle aim spread");
                        Check(Value(item, PassiveEffects.EntityDamage, tags) == 1800, "T19 rifle damage changed");
                        string zoom;
                        Check(weapon.Actions[1].Properties.Values.TryGetValue("Zoom_max_in", out zoom) && zoom == "55", "Bare rifle zoom changed");
                        SdtdConsole.Instance.Output("[AEC-Weapon-Audit] T19 rifle: maxRange=150m, falloff=80m, handling=0.75, spread=6x6, aimingMultiplier=0.05, bareZoom=55, damage=1800");
                    }
                }
                Check(weapons == 28, "Expected 28 weapons");
                SdtdConsole.Instance.Output("[AEC-Weapon-Audit] PASS checks=" + checks + "; failures=0. Native parameters/actions only; graphical aiming and combat remain client checks.");
            }
            catch (Exception ex) { SdtdConsole.Instance.Output("[AEC-Weapon-Audit] FAIL checks=" + checks + "; " + ex.GetBaseException()); }
        }
    }
}
