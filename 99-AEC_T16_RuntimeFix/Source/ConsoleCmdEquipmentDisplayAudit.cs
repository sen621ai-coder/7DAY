using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HarmonyLib;

namespace AECT16RuntimeFix
{
    public class ConsoleCmdEquipmentDisplayAudit : ConsoleCmdAbstract
    {
        public override string[] getCommands() { return new[] { "aecequipmentdisplaycheck" }; }
        public override string getDescription() { return "Read-only native equipment tooltip and fusion display audit."; }
        public override void Execute(List<string> parameters, CommandSenderInfo senderInfo)
        {
            int checks = 0;
            var failures = new List<string>();
            try
            {
                var getCustom = AccessTools.Method(typeof(XUiM_ItemStack), "GetCustomValue");
                var path = Path.Combine(EquipmentStatDisplay.ModDirectory, "Tools/equipment_display_expected.csv");
                var names = new HashSet<string>();
                foreach (string line in File.ReadAllLines(path))
                {
                    string[] cells = line.Split(',');
                    names.Add(cells[0]);
                    var definition = ItemClass.GetItem(cells[0], false).ItemClass;
                    if (definition.DisplayType != "AECDisplay_" + cells[0]) throw new Exception("Wrong display type: " + cells[0]);
                    var panel = UIDisplayInfoManager.instance.GetDisplayStatsForTag(definition.DisplayType);
                    var entry = panel.DisplayStats[int.Parse(cells[1])];
                    foreach (int quality in new[] { 1,6 })
                    for (int r = 0; r < 3; r++)
                    foreach (bool mods in new[] { false,true })
                    {
                        var item = new ItemValue(definition.Id, quality, quality, false, null, 1f);
                        item.SetMetadata(EquipmentFusion.RankKey, new[] { 0,1,10 }[r]);
                        float expected = float.Parse(cells[2+r], CultureInfo.InvariantCulture);
                        float actual = (float)getCustom.Invoke(null, new object[] { entry,item,mods });
                        checks++;
                        if (Math.Abs(actual-expected) > Math.Max(.002f, Math.Abs(expected)*.0002f))
                            failures.Add(cells[0] + " " + entry.CustomName + " Q" + quality + " rank=" + new[] { 0,1,10 }[r] + " mods=" + mods + " display=" + actual + " expected=" + expected);
                    }
                }
                if (names.Count != 148) throw new Exception("Expected 148 equipment panels, got " + names.Count);
                var armor = new ItemValue(ItemClass.GetItem("armorPZAECHarrierOutfitT19", false).type, 6,6,false,null,1f);
                var health = UIDisplayInfoManager.instance.GetDisplayStatsForTag(armor.ItemClass.DisplayType).DisplayStats.Single(e => e.CustomName=="aecBase_HealthMax");
                string text = XUiM_ItemStack.GetStatItemValueTextWithModInfo(new ItemStack(armor,1), null, health);
                checks++;
                if (!text.Contains("350")) throw new Exception("Formatted health text: " + text);
                SdtdConsole.Instance.Output("[AEC-Display-Audit] T19 Harrier outfit formatted health=" + text);
                var gun = new ItemValue(ItemClass.GetItem("gunPZAECEmberPistolT19", false).type, 6,6,false,null,1f);
                gun.Modifications[0] = ItemClass.GetItem("modPZAECClosedLoopFeedT19", false);
                gun.SetMetadata(EquipmentFusion.RankKey, 1);
                var magazine = UIDisplayInfoManager.instance.GetDisplayStatsForTag(gun.ItemClass.DisplayType).DisplayStats.Single(e => e.CustomName=="aecBase_MagazineSize");
                foreach (bool mods in new[] { false,true })
                {
                    float actual = (float)getCustom.Invoke(null, new object[] { magazine,gun,mods });
                    float expected = 64 * 1.05f * (mods ? 13 : 1);
                    checks++;
                    if (Math.Abs(actual-expected) > .02f) throw new Exception("Attached mod display scope: " + actual + " expected " + expected);
                }
                float ignored = 0;
                checks++;
                if (!EquipmentStatDisplay.Prefix(health, ItemClass.GetItem("armorRangerOutfit", false), true, ref ignored)) throw new Exception("Legacy armor intercepted");
            }
            catch (Exception ex) { failures.Add(ex.GetBaseException().ToString()); }
            foreach (string failure in failures.Take(30)) SdtdConsole.Instance.Output("[AEC-Display-Audit] FAIL " + failure);
            SdtdConsole.Instance.Output("[AEC-Display-Audit] " + (failures.Count==0 ? "PASS" : "FAIL") + " checks=" + checks + "; failures=" + failures.Count);
        }
    }
}
