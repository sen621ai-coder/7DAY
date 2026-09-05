using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;

namespace AECT16RuntimeFix
{
    // Read-only integration check. Constructs temporary ItemValues; never gives
    // items, edits a player, or touches saved inventories.
    public class ConsoleCmdEndgameGearAudit : ConsoleCmdAbstract
    {
        private static readonly List<string> Failures = new List<string>();
        public override string[] getCommands() { return new[] { "aecgearcheck" }; }
        public override string getDescription() { return "Audit native T16-T19 equipment stats against Q6 legacy gear."; }
        private static ItemValue Item(string name, int quality)
        {
            var template = ItemClass.GetItem(name, false);
            if (template == null || template.IsEmpty()) throw new Exception("Missing item " + name);
            return new ItemValue(template.type, quality, quality, false, null, 1f);
        }
        private static float Value(ItemValue value, PassiveEffects effect, string tags, float start = 0f)
        {
            return EffectManager.GetValue(effect, value, start, null, null,
                FastTags<TagGroup.Global>.Parse(tags), false, false, false, false, false, 1, false, false);
        }
        private static void Check(bool success, string message)
        {
            if (!success) Failures.Add(message);
        }
        private static void CheckSavedSlots(ItemValue value, int oldSlots)
        {
            value.Modifications = Enumerable.Range(0, oldSlots).Select(_ => new ItemValue()).ToArray();
            value.Modifications[0] = Item("modPZAECPrecisionR2", 1);
            value.UseTimes = 123;
            using (var stream = new MemoryStream())
            {
                var writer = new BinaryWriter(stream);
                value.Write(writer); writer.Flush(); stream.Position = 0;
                var restored = new ItemValue();
                restored.Read(new BinaryReader(stream));
                Check(stream.Position == stream.Length, "Save payload misaligned");
                Check(restored.type == value.type && restored.Quality == value.Quality && restored.UseTimes == value.UseTimes, "Save identity/quality/wear changed");
                Check(restored.Modifications.Length == 6 && restored.Modifications[0].type == value.Modifications[0].type, "Saved equipment did not preserve mods and expand sockets");
            }
        }
        public override void Execute(List<string> parameters, CommandSenderInfo senderInfo)
        {
            try
            {
                Failures.Clear();
                string[][] families = {
                    new[] { "gunPZAECEmberPistol", "perkGunslinger", "gunHandgunT5Hellgun" },
                    new[] { "gunPZAECHorizonNeedle", "perkDeadEye", "GausLegArsonist", "GausLegTesla", "GausLegURANUS", "GausLegUnkillable", "gunRifleT5SniperRifleGaus" },
                    new[] { "gunPZAECStormReservoir", "perkMachineGunner", "BulldogLegArsonist", "BulldogLegURANUS", "BulldogLegBreeze", "BulldogLegUnkillable", "BulldogLegAvenger", "BulldogLegBerserk" },
                    new[] { "gunPZAECBastionShotgun", "perkBoomstick", "EraserLegArsonist", "EraserLegURANUS", "EraserLegUnkillable", "EraserLegAvenger", "EraserLegBerserk" },
                    new[] { "meleePZAECFaultlineHammer", "perkSkullCrusher,secondary", "DestructorLegCrisis", "DestructorLegCrusher", "DestructorLegMasterpiece", "DestructorLegGuardian" },
                    new[] { "gunPZAECEchoRepeater", "perkArchery", "gunBowT3CompoundCrossbow" },
                    new[] { "gunPZAECCounterSiege", "perkDemolitionsExpert", "gunExplosivesT3RocketLauncher" }
                };
                int checks = 0;
                foreach (var family in families)
                {
                    var old = family.Skip(2).SelectMany(n => Enumerable.Range(0, 32).Select(_ => Item(n, 6))).ToArray();
                    bool melee = family[0].StartsWith("melee", StringComparison.Ordinal);
                    var rate = melee ? PassiveEffects.AttacksPerMinute : PassiveEffects.RoundsPerMinute;
                    float oldDamage = old.Max(v => Value(v, PassiveEffects.EntityDamage, family[1]));
                    float oldRate = old.Max(v => Value(v, rate, family[1]));
                    float oldMag = melee ? 0f : old.Max(v => Value(v, PassiveEffects.MagazineSize, family[1]));
                    float oldDurability = old.Max(v => Value(v, PassiveEffects.DegradationMax, family[1]));
                    float previousDamage = 0f, previousDps = 0f;
                    for (int tier = 16; tier <= 19; tier++)
                    {
                        var current = Enumerable.Range(1, 6).SelectMany(q => Enumerable.Range(0, 8).Select(_ => Item(family[0] + "T" + tier, q))).ToArray();
                        float damage = current.Min(v => Value(v, PassiveEffects.EntityDamage, family[1]));
                        float rpm = current.Min(v => Value(v, rate, family[1]));
                        float mag = melee ? 0f : current.Min(v => Value(v, PassiveEffects.MagazineSize, family[1]));
                        float durability = current.Min(v => Value(v, PassiveEffects.DegradationMax, family[1]));
                        float slots = current.Min(v => Value(v, PassiveEffects.ModSlots, ""));
                        string label = family[0] + "T" + tier;
                        Check(damage > oldDamage * 1.1f, label + " damage below Q6: " + damage + "/" + oldDamage);
                        Check(rpm > oldRate, label + " rate below Q6: " + rpm + "/" + oldRate);
                        Check(melee || mag > oldMag, label + " magazine below Q6: " + mag + "/" + oldMag);
                        Check(durability > oldDurability && slots >= 6, label + " durability or slots regressed");
                        float dps = damage * rpm;
                        Check(damage > previousDamage && dps > previousDps, label + " progression reversed");
                        previousDamage = damage; previousDps = dps;
                        CheckSavedSlots(current[0], 4);
                        SdtdConsole.Instance.Output(string.Format(CultureInfo.InvariantCulture,
                            "[AEC-Gear-Audit] {0}: damage={1:0.##}, rate={2:0.##}, mag={3:0.##}, durability={4:0.##}, slots={5}; oldQ6 damage<={6:0.##}, rate<={7:0.##}, mag<={8:0.##}",
                            label, damage, rpm, mag, durability, slots, oldDamage, oldRate, oldMag));
                        checks++;
                    }
                }
                foreach (string slot in new[] { "Helmet", "Outfit", "Gloves", "Boots" })
                {
                    var old = new[] { "Predator", "Plunderer", "Sonny", "Maus", "Rescuer" }
                        .SelectMany(s => Enumerable.Range(0, 32).Select(_ => Item("armor" + s + slot, 6))).ToArray();
                    float oldArmor = old.Max(v => Value(v, PassiveEffects.PhysicalDamageResist, ""));
                    string[] metrics = slot == "Helmet" ? new[] { "PlayerExpGain", "LootStage", "FoodLossPerStaminaPointGained", "WaterLossPerStaminaPointGained" }
                        : slot == "Outfit" ? new[] { "HealthMax", "StaminaMax", "CarryCapacity" }
                        : slot == "Gloves" ? new[] { "EntityDamage", "RoundsPerMinute", "AttacksPerMinute", "ReloadSpeedMultiplier", "ScavengingTime" }
                        : new[] { "RunSpeed", "StaminaChangeOT", "StaminaMax", "StaminaLoss", "VehicleMotorTorquePer", "VehicleVelocityMaxPer" };
                    foreach (string set in new[] { "Harrier", "Storm", "Tremor", "Warden" })
                    {
                        float previous = oldArmor;
                        for (int tier = 16; tier <= 19; tier++)
                        {
                            string name = "armorPZAEC" + set + slot + "T" + tier;
                            var value = Item(name, 1);
                            float armor = Value(value, PassiveEffects.PhysicalDamageResist, "");
                            Check(armor > previous, name + " armor below prior tier/Q6: " + armor + "/" + previous);
                            Check(Value(value, PassiveEffects.ModSlots, "") >= 6, name + " lost sockets");
                            Check(Value(value, PassiveEffects.DegradationMax, "") > old.Max(v => Value(v, PassiveEffects.DegradationMax, "")), name + " durability below Q6");
                            foreach (string metric in metrics)
                            {
                                var effect = (PassiveEffects)Enum.Parse(typeof(PassiveEffects), metric);
                                bool lower = metric.EndsWith("Loss", StringComparison.Ordinal) || metric.Contains("LossPer") || metric == "ScavengingTime";
                                // Use a unit base: speed is an absolute value, and
                                // a fictitious base of 100 hits the overflow cap.
                                float baseline = lower ? old.Min(v => Value(v, effect, "", 1)) : old.Max(v => Value(v, effect, "", 1));
                                for (int q = 1; q <= 6; q++)
                                {
                                    var atQuality = Item(name, q);
                                    float actual = Value(atQuality, effect, "", 1);
                                    Check(lower ? actual < baseline : actual > baseline, name + " " + metric + " below Q6: " + actual + "/" + baseline);
                                    Check(Value(atQuality, PassiveEffects.PhysicalDamageResist, "") == armor, name + " armor changed with quality");
                                }
                            }
                            previous = armor;
                            CheckSavedSlots(value, 5);
                            checks++;
                        }
                    }
                }
                foreach (string failure in Failures) SdtdConsole.Instance.Output("[AEC-Gear-Audit] FAIL " + failure);
                SdtdConsole.Instance.Output("[AEC-Gear-Audit] " + (Failures.Count == 0 ? "PASS " : "FAIL ") + checks + " tier/family checks; failures=" + Failures.Count + ". Native item-only stats; no player perks, mods, ammo or conditional combat procs included.");
            }
            catch (Exception ex)
            {
                SdtdConsole.Instance.Output("[AEC-Gear-Audit] FAIL " + ex.GetBaseException().Message);
            }
        }
    }
}
