using System;
using System.Collections.Generic;
using System.Linq;

namespace AECT16RuntimeFix
{
    public class ConsoleCmdWeaponAttachmentAudit : ConsoleCmdAbstract
    {
        public override string[] getCommands() { return new[] { "aecattachmentcheck" }; }
        public override string getDescription() { return "Read-only native reused weapon model attachment checks."; }
        public override void Execute(List<string> parameters, CommandSenderInfo senderInfo)
        {
            int checks = 0, weapons = 0;
            var failures = new List<string>();
            try
            {
                foreach (var weapon in ItemClass.list.Where(i => i != null && WeaponAttachmentCompatibility.Model(i.GetItemName()) != null))
                {
                    weapons++;
                    string name = weapon.GetItemName(), model = WeaponAttachmentCompatibility.Model(name);
                    var value = new ItemValue(weapon.Id, 1, 1, false, null, 1f);
                    foreach (var modifier in ItemClass.list.OfType<ItemClassModifier>())
                    {
                        DynamicProperties original;
                        if (!modifier.PropertyOverrides.TryGetValue(model, out original)) continue;
                        value.Modifications[0] = ItemClass.GetItem(modifier.GetItemName(), false);
                        foreach (var property in original.Values)
                        {
                            string expected = property.Value;
                            DynamicProperties dedicated;
                            string custom;
                            if (modifier.PropertyOverrides.TryGetValue(name, out dedicated) && dedicated.Values.TryGetValue(property.Key, out custom)) expected = custom;
                            checks++;
                            if (value.GetPropertyOverride(property.Key, "__missing__") != expected)
                                failures.Add(name + "/" + modifier.GetItemName() + "/" + property.Key);
                        }
                    }
                }
                if (weapons != 28 || checks < 100) failures.Add("Insufficient coverage: " + weapons + " weapons, " + checks + " properties");
                // Isolated modifier: specific > model > wildcard, property by
                // property. Never mutate a loaded modifier or player equipment.
                var isolated = new ItemClassModifier();
                isolated.PropertyOverrides = new Dictionary<string, DynamicProperties>();
                foreach (string key in new[] { "*", "gunHandgunT3DesertVulture", "gunPZAECEmberPistolT16" })
                    isolated.PropertyOverrides[key] = new DynamicProperties();
                isolated.PropertyOverrides["*"].Values["audit"] = "wildcard";
                isolated.PropertyOverrides["gunHandgunT3DesertVulture"].Values["audit"] = "model";
                string actual = null;
                checks++;
                if (!isolated.GetPropertyOverride("audit", "gunPZAECEmberPistolT16", ref actual) || actual != "model") failures.Add("Model before wildcard");
                isolated.PropertyOverrides["gunPZAECEmberPistolT16"].Values["audit"] = "dedicated";
                checks++;
                if (!isolated.GetPropertyOverride("audit", "gunPZAECEmberPistolT16", ref actual) || actual != "dedicated") failures.Add("Dedicated priority");
                checks++;
                if (!isolated.GetPropertyOverride("audit", "gunPZAECEmberPistolT15", ref actual) || actual != "wildcard") failures.Add("Unrelated scope changed");
            }
            catch (Exception ex) { failures.Add(ex.ToString()); }
            foreach (string failure in failures) SdtdConsole.Instance.Output("[AEC-Attachment-Audit] FAIL " + failure);
            SdtdConsole.Instance.Output("[AEC-Attachment-Audit] " + (failures.Count == 0 ? "PASS" : "FAIL") + " weapons=" + weapons + "; checks=" + checks + "; failures=" + failures.Count);
        }
    }
}
