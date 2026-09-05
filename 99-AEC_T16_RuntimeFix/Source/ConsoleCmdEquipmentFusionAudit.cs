using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AECT16RuntimeFix
{
    // Temporary items only: safe to run without altering player inventories.
    public class ConsoleCmdEquipmentFusionAudit : ConsoleCmdAbstract
    {
        private readonly List<string> failures = new List<string>();
        private int checks;
        public override string[] getCommands() { return new[] { "aecfusioncheck" }; }
        public override string getDescription() { return "Read-only native T16-T19 fusion integration audit."; }
        private void Check(bool ok, string message) { checks++; if (!ok) failures.Add(message); }
        private void Near(float actual, float expected, string message)
        { Check(Math.Abs(actual - expected) <= Math.Max(.002f, Math.Abs(expected) * .0001f), message + ": " + actual + " expected " + expected); }
        private static ItemStack Item(string name, int quality = 1)
        { return new ItemStack(new ItemValue(ItemClass.GetItem(name, false).type, quality, quality, false, null, 1f), 1); }
        private static float Value(ItemValue item, PassiveEffects effect, string tags = "", float start = 0)
        { return EffectManager.GetValue(effect, item, start, null, null, FastTags<TagGroup.Global>.Parse(tags), false, false, false, false, false, 1, false, false); }
        private static ItemValue Saved(ItemValue item)
        {
            using (var stream = new MemoryStream())
            {
                var writer = new BinaryWriter(stream); item.Write(writer); writer.Flush(); stream.Position = 0;
                var restored = new ItemValue(); restored.Read(new BinaryReader(stream));
                if (stream.Position != stream.Length) throw new Exception("Save payload alignment");
                return restored;
            }
        }
        public override void Execute(List<string> parameters, CommandSenderInfo senderInfo)
        {
            failures.Clear(); checks = 0;
            try
            {
                var names = new List<string>();
                foreach (int tier in Enumerable.Range(16, 4))
                {
                    foreach (string weapon in new[] { "gunPZAECEmberPistol", "gunPZAECHorizonNeedle", "gunPZAECStormReservoir", "gunPZAECBastionShotgun", "gunPZAECEchoRepeater", "gunPZAECCounterSiege", "meleePZAECFaultlineHammer" }) names.Add(weapon + "T" + tier);
                    foreach (string set in new[] { "Harrier", "Storm", "Tremor", "Warden" })
                        foreach (string slot in new[] { "Helmet", "Outfit", "Gloves", "Boots" }) names.Add("armorPZAEC" + set + slot + "T" + tier);
                }
                foreach (string name in names)
                {
                    var first = Item(name); var second = Item(name, 6); ItemStack fused;
                    Check(EquipmentFusion.IsFusionItem(first.itemValue), name + " allowlist");
                    Check(first.itemValue.HasQuality && first.itemValue.ItemClass.HasQuality && !first.itemValue.ItemClass.HasSubItems, name + " native station acceptance");
                    Check(first.itemValue.Modifications.Length == 6, name + " fresh actual six sockets");
                    Check(EquipmentFusion.TryCreate(first, second, out fused), name + " first fusion: " + EquipmentFusion.Validate(first, second));
                    if (fused.IsEmpty()) continue;
                    Check(EquipmentFusion.Rank(fused.itemValue) == 1 && first.itemValue.Quality == fused.itemValue.Quality, name + " output rank/quality");
                    var effect = name.StartsWith("armor", StringComparison.Ordinal) ? PassiveEffects.PhysicalDamageResist : PassiveEffects.EntityDamage;
                    string tags = name.StartsWith("melee", StringComparison.Ordinal) ? "perkSkullCrusher,secondary"
                        : name.Contains("BastionShotgun") ? "perkBoomstick" : name.Contains("CounterSiege") ? "perkDemolitionsExpert"
                        : name.Contains("HorizonNeedle") ? "perkDeadEye" : name.Contains("StormReservoir") ? "perkMachineGunner"
                        : name.Contains("EchoRepeater") ? "perkArchery" : name.Contains("EmberPistol") ? "perkGunslinger" : "";
                    float original = Value(first.itemValue, effect, tags);
                    Near(Value(fused.itemValue, effect, tags), original * 1.05f, name + " actual primary stat");
                    Near(Value(fused.itemValue, PassiveEffects.DegradationMax), Value(first.itemValue, PassiveEffects.DegradationMax) * 1.05f, name + " durability");
                    Near(Value(fused.itemValue, PassiveEffects.ModSlots), 6, name + " fixed sockets");
                    Near(Value(first.itemValue, effect, tags), original, name + " original unchanged");
                    var sources = new List<EffectManager.ModifierValuesAndSources>(); float displayBase = 0, displayPercent = 1;
                    fused.itemValue.GetModifiedValueData(sources, default(EffectManager.ModifierValuesAndSources.ValueSourceType), null, null, effect,
                        ref displayBase, ref displayPercent, FastTags<TagGroup.Global>.Parse(tags));
                    Check(sources.Any(source => Math.Abs(source.Value - original * 1.05f) <= Math.Max(.002f, original * .0001f)), name + " detailed stat preview scaled");
                    Check(EquipmentFusion.Rank(first.itemValue) == 0 && EquipmentFusion.Rank(second.itemValue) == 0, name + " preview no input mutation");
                    ItemStack twice;
                    Check(EquipmentFusion.TryCreate(fused, fused.Clone(), out twice), name + " second fusion");
                    Near(Value(twice.itemValue, effect, tags), original * 1.1025f, name + " compound scaling");
                    var restored = Saved(twice.itemValue);
                    Check(EquipmentFusion.Rank(restored) == 2, name + " saved rank");
                    Near(Value(restored, effect, tags), original * 1.1025f, name + " saved actual stat");
                    Check(EquipmentFusion.Validate(first, fused) != null, name + " different rank rejected");
                }
                const string gun = "gunPZAECHorizonNeedleT16";
                var a = Item(gun); var b = Item(gun); ItemStack output;
                Check(!EquipmentFusion.TryCreate(a, a, out output), "same input object rejected");
                Check(!EquipmentFusion.TryCreate(a, new ItemStack(a.itemValue, 1), out output), "shared ItemValue rejected");
                Check(!EquipmentFusion.TryCreate(a, ItemStack.Empty, out output), "empty rejected");
                Check(!EquipmentFusion.TryCreate(a, Item("gunPZAECHorizonNeedleT17"), out output), "different tier rejected");
                Check(!EquipmentFusion.TryCreate(a, Item("gunPZAECStormReservoirT16"), out output), "different weapon rejected");
                var old = Item("GausLegArsonist", 6); old.itemValue.SetMetadata(EquipmentFusion.RankKey, 5);
                Check(!EquipmentFusion.IsFusionItem(old.itemValue) && EquipmentFusion.Rank(old.itemValue) == 0, "legacy excluded even with metadata");
                Check(!EquipmentFusion.TryCreate(old, old.Clone(), out output), "legacy fusion rejected");
                b.count = 2; Check(!EquipmentFusion.TryCreate(a, b, out output), "stack of two rejected"); b.count = 1;
                b.itemValue.Meta = 10; Check(!EquipmentFusion.TryCreate(a, b, out output), "loaded donor rejected"); b.itemValue.Meta = 0;
                a.itemValue.Meta = 10; Check(!EquipmentFusion.TryCreate(a, b, out output), "loaded primary rejected"); a.itemValue.Meta = 0;
                b.itemValue.Modifications[0] = Item("modPZAECPrecisionR2").itemValue;
                Check(!EquipmentFusion.TryCreate(a, b, out output), "donor mods rejected"); b = Item(gun);
                a.itemValue.Modifications[0] = Item("modPZAECPrecisionR2").itemValue;
                float primaryModdedDamage = Value(a.itemValue, PassiveEffects.EntityDamage, "perkDeadEye");
                float primaryModdedRate = Value(a.itemValue, PassiveEffects.RoundsPerMinute, "perkDeadEye");
                a.itemValue.SetMetadata("audit", "keep"); a.itemValue.UseTimes = 100;
                Check(EquipmentFusion.TryCreate(a, b, out output), "primary mods accepted");
                Check(output.itemValue.Modifications[0].type == a.itemValue.Modifications[0].type && !ReferenceEquals(output.itemValue.Modifications, a.itemValue.Modifications), "primary mods cloned");
                Check(output.itemValue.Seed == a.itemValue.Seed, "primary seed retained");
                Near(Value(output.itemValue, PassiveEffects.EntityDamage, "perkDeadEye"), primaryModdedDamage * 1.05f, "attached mod bonus not independently scaled");
                Near(Value(output.itemValue, PassiveEffects.RoundsPerMinute, "perkDeadEye"), primaryModdedRate * 1.05f, "active attached mod penalty retains own scope");
                Near(output.itemValue.UseTimes, 105, "wear proportion");
                string metadata; Check(output.itemValue.TryGetMetadata("audit", out metadata) && metadata == "keep", "primary metadata retained");
                var saved = Saved(output.itemValue);
                Check(saved.Modifications[0].type == a.itemValue.Modifications[0].type && EquipmentFusion.Rank(saved) == 1, "saved attachments and fusion");
                // Signed bonuses, costs and structural values exercise distinct rules.
                Near(FusionStatScaling.Scale(PassiveEffects.StaminaLoss, PassiveEffect.ValueModifierTypes.base_set, 20, 1), 19, "positive cost reduced");
                Near(FusionStatScaling.Scale(PassiveEffects.StaminaLoss, PassiveEffect.ValueModifierTypes.perc_add, -.2f, 1), -.21f, "cost reduction enhanced");
                Near(FusionStatScaling.Scale(PassiveEffects.RunSpeed, PassiveEffect.ValueModifierTypes.perc_add, -.2f, 1), -.19f, "movement penalty reduced");
                Near(FusionStatScaling.Scale(PassiveEffects.EntityDamage, PassiveEffect.ValueModifierTypes.base_subtract, 20, 1), 19, "damage penalty reduced");
                Near(FusionStatScaling.Scale(PassiveEffects.ModSlots, PassiveEffect.ValueModifierTypes.base_set, 6, 10), 6, "slot count structural");
                SdtdConsole.Instance.Output("[AEC-Fusion-Audit] SAMPLE damage " + Value(Item(gun).itemValue, PassiveEffects.EntityDamage, "perkDeadEye") + " -> " + Value(output.itemValue, PassiveEffects.EntityDamage, "perkDeadEye") + " (primary has precision mod)");
            }
            catch (Exception ex) { failures.Add(ex.ToString()); }
            foreach (string failure in failures) SdtdConsole.Instance.Output("[AEC-Fusion-Audit] FAIL " + failure);
            SdtdConsole.Instance.Output("[AEC-Fusion-Audit] " + (failures.Count == 0 ? "PASS" : "FAIL") + " checks=" + checks + "; failures=" + failures.Count + ".");
        }
    }
}
