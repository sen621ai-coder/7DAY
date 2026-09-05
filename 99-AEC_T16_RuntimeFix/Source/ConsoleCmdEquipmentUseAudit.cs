using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HarmonyLib;
using UnityEngine;

namespace AECT16RuntimeFix
{
    public sealed class ConsoleCmdEquipmentUseAudit : ConsoleCmdAbstract
    {
        public override string[] getCommands() { return new[] { "aecusecheck" }; }
        public override string getDescription() { return "Isolated verification-save equipment use regression (temporary entities only)."; }
        private int checks;
        private readonly List<string> failures = new List<string>();
        private void Check(bool ok, string label) { checks++; if (!ok) failures.Add(label); }
        private static ItemValue Item(string name) { return new ItemValue(ItemClass.GetItem(name, false).type, 6, 6, false, null, 1); }
        private static void Buff(EntityPlayer p, string name) { p.Buffs.AddBuff(name, -1, false, true, -1); }
        private static void FlushRemoved(EntityPlayer p)
        {
            // Native RemoveBuff (and removeBuff) only mark removal. The real
            // Tick drains those marks and fires the dependent removal events.
            for (int pass = 0; pass < 4 && p.Buffs.ActiveBuffs.Any(b => b.Remove); pass++) p.Buffs.Tick();
        }
        private bool Use(EntityPlayer p, ItemValue value)
        {
            var stack = new ItemStack(value, 2);
            p.MinEventContext.ItemValue = value;
            var action = value.ItemClass.Actions[0];
            Check(action is ItemActionPZAECUse, value.ItemClass.GetItemName() + " native action factory");
            bool result = action.ExecuteInstantAction(p, stack, false, null);
            FlushRemoved(p);
            Check(stack.count == ((((ItemActionEat)action).Consume && result) ? 1 : 2), value.ItemClass.GetItemName() + " consumed exactly once");
            return result;
        }
        private static void AutoUpdate(EntityPlayer player)
        {
            ((System.Collections.IDictionary)AccessTools.Field(typeof(EndgameExpansionRuntime), "NextPlayerHeatUpdate").GetValue(null)).Clear();
            EndgameExpansionRuntime.PlayerUpdatePostfix(player);
            FlushRemoved(player);
        }
        public override void Execute(List<string> parameters, CommandSenderInfo senderInfo)
        {
            if (!GamePrefs.GetString(EnumGamePrefs.GameName).StartsWith("AEC_Equipment_Verification", StringComparison.Ordinal))
            { SdtdConsole.Instance.Output("[AEC-Use-Audit] Refused outside isolated verification save."); return; }
            checks = 0; failures.Clear();
            EntityPlayer player = null;
            var temporaryEnemies = new List<EntityAlive>();
            try
            {
                var world = GameManager.Instance.World;
                string mod = EquipmentStatDisplay.ModDirectory;
                var catalog = new HashSet<string>();
                foreach (string file in new[] { "items.xml", "item_modifiers.xml", "blocks.xml" })
                {
                    string text = File.ReadAllText(Path.Combine(mod, "Config", file));
                    foreach (Match match in Regex.Matches(text, @"<!-- BEGIN GENERATED ENDGAME (?:ARSENAL|EXPANSION) -->(.*?)<!-- END GENERATED ENDGAME (?:ARSENAL|EXPANSION) -->", RegexOptions.Singleline))
                    foreach (var node in XElement.Parse("<root>" + match.Groups[1].Value + "</root>").Descendants())
                    {
                        if (node.Name != "item" && node.Name != "item_modifier" && node.Name != "block") continue;
                        string name = (string)node.Attribute("name");
                        if (name != null && !name.StartsWith("PZAECAblativeWallRuin")) catalog.Add(name);
                    }
                }
                Check(catalog.Count == 250, "catalog count " + catalog.Count);
                foreach (string name in catalog)
                {
                    var definition = ItemClass.GetItemClass(name, false);
                    Check(definition != null, "native definition missing " + name);
                    if (definition != null) Check(!Item(name).IsEmpty(), "native construction " + name);
                }
                player = EntityFactory.CreateEntity(EntityClass.FromString("playerMale"), new Vector3(0, 100, 0)) as EntityPlayer;
                if (player == null) throw new Exception("Temporary player factory failed");
                player.world = world;
                player.isEntityRemote = false;
                player.MinEventContext.Self = player;
                player.MinEventContext.IsLocal = true;
                Buff(player, "buffStatusCheck02");

                // Execute the real inventory/toolbelt completion path at full health.
                var usable = catalog.Where(n => n.EndsWith("DeviceT16") || n.EndsWith("DeviceT17") || n.EndsWith("DeviceT18") || n.EndsWith("DeviceT19") ||
                    n.StartsWith("itemPZAECFieldRepairKitT") || new[] { "itemPZAECQuickArmorGel", "itemPZAECResonanceInjector", "itemPZAECDecoyBeacon", "itemPZAECEvacAnchor" }.Contains(n)).ToArray();
                Check(usable.Length == 8, "8 field use types");
                foreach (string name in usable)
                {
                    var value = Item(name); var action = value.ItemClass.Actions[0];
                    Check(action is ItemActionPZAECUse && !action.UseAnimation, name + " instant action");
                    Check(value.ItemClass.Actions.Length < 2 || value.ItemClass.Actions[1] == null, name + " inherited heal-other action");
                    if (!name.Contains("DeviceT"))
                        Check(action.ExecutionRequirements == null || action.ExecutionRequirements.IsValid(player.MinEventContext), name + " full-health use blocked");
                }

                foreach (int tier in Enumerable.Range(16, 4))
                {
                    string grenadeName = "thrownPZAECCounterJammerT" + tier;
                    var grenade = Item(grenadeName).ItemClass;
                    Check(grenade is ItemClassTimeBomb && grenade.Actions[0] is ItemActionThrowAway && grenade.Actions[1] is ItemActionActivate, grenadeName + " native throw/prime");
                    var explosion = new ExplosionData(grenade.Properties, grenade.Effects);
                    Check(explosion.BlockDamage == 0 && explosion.EntityDamage == 0 && explosion.EntityRadius == 8 && explosion.BlastPower == 0, grenadeName + " safe 8m explosion");
                    var wall = Block.GetBlockValue("PZAECReactiveWallT" + tier, false);
                    var pos = new Vector3i(12345, 100, tier);
                    int damage = 1400;
                    EndgameExpansionRuntime.BlockRemovedPostfix(pos);
                    EndgameExpansionRuntime.DamageBlockPrefix(new BlockValueRef(pos), wall, null, ref damage);
                    Check(damage == new[] { 1292,1268,1244,1220 }[tier-16], "reactive T" + tier + " first-hit=" + damage);
                    damage = 1400;
                    EndgameExpansionRuntime.DamageBlockPrefix(new BlockValueRef(pos), wall, null, ref damage);
                    Check(damage == 1400, "reactive cooldown T" + tier);
                    EndgameExpansionRuntime.BlockRemovedPostfix(pos);
                    var armor = Item("armorPZAECWardenOutfitT" + tier);
                    armor.UseTimes = armor.MaxUseTimes * .9f;
                    player.equipment.SetSlotItem(1, armor, false);
                    float before = armor.UseTimes;
                    Check(Use(player, Item("itemPZAECFieldRepairKitT" + tier)), "repair kit use T" + tier);
                    var repaired = player.equipment.GetItems()[1];
                    float expected = Math.Max(0, before - armor.MaxUseTimes * new[] { .35f,.45f,.55f,.65f }[tier-16]);
                    Check(Math.Abs(repaired.UseTimes - expected) < .01f, "repair actual wear T" + tier + " actual=" + repaired.UseTimes + " expected=" + expected);
                    player.equipment.SetSlotItem(1, ItemValue.None, false);
                }
                foreach (string family in new[] { "Harrier", "Storm", "Tremor", "Warden" })
                foreach (int tier in Enumerable.Range(16, 4))
                {
                    string prefix = "buffPZAEC" + family + "T" + tier;
                    string charge = "$PZAEC" + family + "T" + tier + "Resonance";
                    var retired = ItemClass.GetItemClass("itemPZAEC" + family + "DeviceT" + tier, false);
                    Check(retired != null && retired.Actions[0] == null && retired.Properties.GetString("CreativeMode") == "None", "manual device hidden and disabled " + family + tier);
                    string[] slots = { "Helmet", "Outfit", "Gloves", "Boots" };
                    for (int slot = 0; slot < 3; slot++) player.equipment.SetSlotItem(slot, Item("armorPZAEC" + family + slots[slot] + "T" + tier), false);
                    if (tier == 19)
                    {
                        var helmet = player.equipment.GetItems()[0];
                        Check(helmet.Modifications.Length == 6, family + " helmet retains six sockets");
                        helmet.Modifications[0] = Item("modPZAEC" + family + "StableT19");
                        player.equipment.SetSlotItem(0, helmet, false);
                    }
                    player.FireEvent(MinEventTypes.onSelfBuffUpdate, true);
                    FlushRemoved(player);
                    Check(player.Buffs.HasBuff(prefix+"Set3"), family+tier+" three-piece charge enabled");
                    player.Buffs.SetCustomVar(charge, 75);
                    Check(Use(player, Item("itemPZAECResonanceInjector")), "injector use");
                    Check(player.Buffs.GetCustomVar(charge) == 100, family+tier+" injector charges once");
                    AutoUpdate(player);
                    Check(!player.Buffs.HasBuff(prefix+"Active") && player.Buffs.GetCustomVar(charge)==100, family+tier+" three pieces cannot activate");
                    player.equipment.SetSlotItem(3, Item("armorPZAEC"+family+"BootsT"+(tier==19?18:tier+1)), false);
                    AutoUpdate(player);
                    Check(!player.Buffs.HasBuff(prefix+"Active"), family+tier+" mixed tier cannot activate");
                    player.equipment.SetSlotItem(3, Item("armorPZAEC"+family+"BootsT"+tier), false);
                    player.FireEvent(MinEventTypes.onSelfBuffUpdate, true);
                    AutoUpdate(player);
                    Check(player.Buffs.HasBuff(prefix+"Active") && player.Buffs.HasBuff(prefix+"Cooldown"), family+tier+" fourth piece auto activates");
                    Check(player.Buffs.GetCustomVar(charge)==0 && !player.Buffs.HasBuff(prefix+"Ready"), family+tier+" automatically spends charge");
                    if (tier == 19) Check(player.Buffs.HasBuff("buffPZAEC"+family+"StableCalibrationT19"), family+" helmet stable calibration activates");
                    player.Buffs.SetCustomVar(charge, 100);
                    AutoUpdate(player);
                    Check(player.Buffs.GetCustomVar(charge)==100, family+tier+" active/cooldown prevents duplicate activation");
                    player.Buffs.RemoveBuff(prefix+"Active", -1, false); FlushRemoved(player);
                    AutoUpdate(player);
                    Check(!player.Buffs.HasBuff(prefix+"Active") && player.Buffs.GetCustomVar(charge)==100, family+tier+" full charge waits for cooldown");
                    if (tier == 19)
                    {
                        var helmet = player.equipment.GetItems()[0];
                        helmet.Modifications[0] = Item("modPZAEC"+family+"OverloadT19");
                        player.equipment.SetSlotItem(0, helmet, false);
                    }
                    player.Buffs.RemoveBuff(prefix+"Cooldown", -1, false); FlushRemoved(player);
                    AutoUpdate(player);
                    Check(player.Buffs.HasBuff(prefix+"Active") && player.Buffs.GetCustomVar(charge)==0, family+tier+" cooldown ending activates queued charge");
                    if (tier == 19) Check(player.Buffs.HasBuff("buffPZAEC"+family+"OverloadCalibrationT19"), family+" helmet overload calibration activates");
                    player.Buffs.SetCustomVar(charge, 100);
                    player.equipment.SetSlotItem(3, ItemValue.None, false);
                    AutoUpdate(player);
                    Check(!player.Buffs.HasBuff(prefix+"Active") && player.Buffs.GetCustomVar(charge)==100, family+tier+" remove fourth piece cancels active, retains charge");
                    Check(player.Buffs.HasBuff(prefix+"Cooldown"), family+tier+" unequip does not bypass cooldown");
                    if (tier==19) Check(!player.Buffs.HasBuff("buffPZAEC"+family+"StableCalibrationT19") && !player.Buffs.HasBuff("buffPZAEC"+family+"OverloadCalibrationT19"), family+" incomplete set removes calibration");
                    player.equipment.SetSlotItem(2, ItemValue.None, false); AutoUpdate(player);
                    Check(player.Buffs.GetCustomVar(charge)==0, family+tier+" below three pieces clears charge");
                    foreach (string suffix in new[] { "Set3", "Set4", "Active", "Cooldown", "Ready" }) player.Buffs.RemoveBuff(prefix+suffix, -1, false);
                    for (int slot=0; slot<4; slot++) player.equipment.SetSlotItem(slot, ItemValue.None, false);
                    player.FireEvent(MinEventTypes.onSelfBuffUpdate, true); FlushRemoved(player);
                    Check(!player.Buffs.HasBuff(prefix+"Set2"), family+tier+" set removed after unequip");
                }
                Check(!EndgameExpansionRuntime.IsJammerTarget(player), "jammer excludes players");
                // Exercise the actual explosion postfix at a point away from the
                // player. Temporary entities never enter the user's save.
                foreach (float distance in new[] { 8f, 8.1f })
                {
                    var enemy = EntityFactory.CreateEntity(EntityClass.FromString("zombieBoe"), new Vector3(30 + distance,100,0)) as EntityAlive;
                    if (enemy == null) throw new Exception("Temporary enemy factory failed");
                    enemy.world = world; enemy.isEntityRemote = false;
                    temporaryEnemies.Add(enemy); world.Entities.list.Add(enemy);
                }
                foreach (int tier in Enumerable.Range(16,4))
                {
                    EndgameExpansionRuntime.JammerExplosionPostfix(new Vector3(30,100,0), player.entityId, Item("thrownPZAECCounterJammerT" + tier));
                    Check(temporaryEnemies[0].Buffs.HasBuff("buffPZAECCounterJammerT" + tier), "jammer T" + tier + " affects enemy at 8m");
                    Check(!temporaryEnemies[1].Buffs.HasBuff("buffPZAECCounterJammerT" + tier), "jammer T" + tier + " excludes enemy beyond 8m");
                    Check(!player.Buffs.HasBuff("buffPZAECCounterJammerT" + tier), "jammer T" + tier + " does not debuff player");
                    string heat = "$PZAECStormHeatT" + tier;
                    player.inventory.SetItem(0, Item("gunPZAECStormReservoirT" + tier), 1, false);
                    player.inventory.SetHoldingItemIdxNoHolsterTime(0);
                    player.Buffs.SetCustomVar(heat, 100, false);
                    var throttle = (System.Collections.IDictionary)AccessTools.Field(typeof(EndgameExpansionRuntime), "NextPlayerHeatUpdate").GetValue(null);
                    throttle.Clear();
                    EndgameExpansionRuntime.PlayerUpdatePostfix(player);
                    Check(player.Buffs.GetCustomVar(heat) == 0 && player.Buffs.HasBuff("buffPZAECStormOverheatedT" + tier), "machine gun T" + tier + " local overheat");
                    player.Buffs.SetCustomVar(heat, 50, false); throttle.Clear();
                    EndgameExpansionRuntime.PlayerUpdatePostfix(player);
                    Check(Math.Abs(player.Buffs.GetCustomVar(heat) - (50 - new[] { 9f,10f,11f,12.5f }[tier-16] * .5f)) < .01f, "machine gun T" + tier + " local cooling");
                }
                var powered = AccessTools.Method(typeof(EndgameExpansionRuntime), "IsPowered");
                Check(!(bool)powered.Invoke(null, new object[] { world, new Vector3i(12345,100,12345) }), "missing powered entity cannot run a defense device");
                SdtdConsole.Instance.Output("[AEC-Use-Audit] Coverage: 250 constructed types; 8 item use paths; 16 automatic sets; 8 helmet calibrations; 4 repair kits; 4 native grenades; 4 reactive walls; fail-closed power.");
            }
            catch (Exception ex) { failures.Add(ex.ToString()); }
            finally
            {
                foreach (var enemy in temporaryEnemies) { enemy.world.Entities.list.Remove(enemy); UnityEngine.Object.Destroy(enemy.gameObject); }
                if (player != null) UnityEngine.Object.Destroy(player.gameObject);
            }
            foreach (string error in failures) SdtdConsole.Instance.Output("[AEC-Use-Audit] ERROR " + error);
            SdtdConsole.Instance.Output("[AEC-Use-Audit] " + (failures.Count == 0 ? "PASS" : "FAIL") + " checks=" + checks + "; failures=" + failures.Count + ".");
        }
    }
}
