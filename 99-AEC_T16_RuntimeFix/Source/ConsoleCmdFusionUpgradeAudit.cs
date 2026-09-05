using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using HarmonyLib;

namespace AECT16RuntimeFix
{
    public class ConsoleCmdFusionUpgradeAudit : ConsoleCmdAbstract
    {
        public override string[] getCommands() { return new[] { "aecupgradecheck" }; }
        public override string getDescription() { return "Read-only fusion tier inheritance and native workstation audit."; }
        private int checks;
        private void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
        private static ItemValue Item(string name) { return new ItemValue(ItemClass.GetItem(name, false).type, 1, 1, false, null, 1f); }
        private static Recipe Copy(Recipe recipe)
        {
            using (var stream = new MemoryStream())
            { var writer = new BinaryWriter(stream); recipe.Write(writer); writer.Flush(); stream.Position = 0; return Recipe.Read(new BinaryReader(stream)); }
        }
        private static RecipeQueueItem Saved(RecipeQueueItem queue)
        {
            using (var stream = new MemoryStream())
            { var writer = new BinaryWriter(stream); queue.Write(writer); writer.Flush(); stream.Position = 0; var result = new RecipeQueueItem(); result.Read(new BinaryReader(stream)); if (stream.Position != stream.Length) throw new Exception("Queue save alignment"); return result; }
        }
        public override void Execute(List<string> parameters, CommandSenderInfo senderInfo)
        {
            checks = 0;
            try
            {
                int recipes = 0;
                var targets = new HashSet<int>();
                foreach (var definition in CraftingManager.GetAllRecipes())
                {
                    var target = new ItemValue(definition.itemValueType, false);
                    if (!EquipmentFusion.IsFusionItem(target)) continue;
                    int tier = int.Parse(target.ItemClass.GetItemName().Substring(target.ItemClass.GetItemName().Length - 2));
                    if (tier == 16) continue;
                    var original = definition.ingredients.FirstOrDefault(i => FusionTierUpgrade.IsHigherSameFamily(i.itemValue, target));
                    Check(targets.Add(target.type), "Duplicate high-tier recipe: " + target.ItemClass.GetItemName());
                    Check(original != null && original.itemValue.ItemClass.GetItemName() == target.ItemClass.GetItemName().Substring(0, target.ItemClass.GetItemName().Length - 2) + (tier-1), "Direct or skip-tier recipe remains");
                    recipes++;
                    foreach (int rank in new[] { 0, 4, 5, 10, 14, 25 })
                    {
                        var recipe = Copy(definition);
                        var ingredient = recipe.ingredients.First(i => FusionTierUpgrade.IsHigherSameFamily(i.itemValue, target));
                        ingredient.itemValue.SetMetadata(EquipmentFusion.RankKey, rank);
                        var queue = Saved(new RecipeQueueItem { Recipe = recipe, Multiplier = 1, Quality = 1, CraftingTimeLeft = -.01f, OneItemCraftTime = 100, StartingEntityId = -1 });
                        Check(EquipmentFusion.Rank(queue.Recipe.ingredients.First(i => FusionTierUpgrade.IsHigherSameFamily(i.itemValue, target)).itemValue) == rank, "Queue lost source rank");
                        // Exercise the same helper injected at the client output constructor.
                        var ui = (XUiC_RecipeStack)FormatterServices.GetUninitializedObject(typeof(XUiC_RecipeStack));
                        AccessTools.Field(typeof(XUiC_RecipeStack), "recipe").SetValue(ui, queue.Recipe);
                        var preview = FusionTierUpgrade.ApplyUI(Item(target.ItemClass.GetItemName()), ui);
                        Check(EquipmentFusion.Rank(preview) == rank / 5, "UI inheritance");
                        FusionTierUpgrade.ApplyUI(preview, ui);
                        Check(EquipmentFusion.Rank(preview) == rank / 5, "Repeated output retry compounded inheritance");
                        // Real patched native background crafting, temporary TE only.
                        var station = new TileEntityWorkstation(null);
                        // Detached test TE: suppress only world dirty/network
                        // notification, since no real chunk owns this instance.
                        AccessTools.Field(typeof(TileEntity), "bDisableModifiedCheck").SetValue(station, true);
                        var slots = new RecipeQueueItem[] { new RecipeQueueItem(), queue };
                        AccessTools.Field(typeof(TileEntityWorkstation), "queue").SetValue(station, slots);
                        AccessTools.Method(typeof(TileEntityWorkstation), "HandleRecipeQueue").Invoke(station, new object[] { .1f });
                        var output = (ItemStack[])AccessTools.Field(typeof(TileEntityWorkstation), "output").GetValue(station);
                        var result = output.Single(i => !i.IsEmpty());
                        Check(result.count == 1 && result.itemValue.type == target.type && EquipmentFusion.Rank(result.itemValue) == rank / 5, "Native workstation inheritance");
                        Check(EquipmentFusion.Rank(ingredient.itemValue) == rank && EquipmentFusion.Rank(original.itemValue) == 0, "Input/shared definition mutated");
                    }
                }
                Check(recipes == 69, "Expected exactly 69 adjacent recipes, got " + recipes);
                var source = Item("gunPZAECHorizonNeedleT18"); source.SetMetadata(EquipmentFusion.RankKey, 10);
                var direct = new Recipe { itemValueType = Item("gunPZAECHorizonNeedleT19").type, count = 1, ingredients = new List<ItemStack> { new ItemStack(source, 1) } };
                var inherited = FusionTierUpgrade.Apply(Item("gunPZAECHorizonNeedleT19"), direct);
                float damage = EffectManager.GetValue(PassiveEffects.EntityDamage, inherited, 0, null, null, FastTags<TagGroup.Global>.Parse("perkDeadEye"), false, false, false, false, false, 1, false, false);
                Check(Math.Abs(damage - 1800 * 1.1025f) < .01f, "Inherited rank did not affect target stats");
                Check(!FusionTierUpgrade.IsHigherSameFamily(source, Item("gunPZAECStormReservoirT19")), "Different family accepted");
                Check(!FusionTierUpgrade.IsHigherSameFamily(source, Item("gunPZAECHorizonNeedleT18")), "Same tier accepted");
                Check(!FusionTierUpgrade.IsHigherSameFamily(Item("gunPZAECHorizonNeedleT19"), source), "Downgrade accepted");
                var chain = Item("gunPZAECHorizonNeedleT16"); chain.SetMetadata(EquipmentFusion.RankKey, 10);
                foreach (int tier in new[] { 17, 18, 19 })
                {
                    var target = Item("gunPZAECHorizonNeedleT" + tier);
                    chain = FusionTierUpgrade.Apply(target, new Recipe { itemValueType = target.type, count = 1, ingredients = new List<ItemStack> { new ItemStack(chain, 1) } });
                    Check(EquipmentFusion.Rank(chain) == (tier == 17 ? 2 : 0), "Sequential 20% inheritance failed");
                }
                SdtdConsole.Instance.Output("[AEC-Upgrade-Audit] PASS checks=" + checks + "; failures=0. 69 adjacent-only recipes; T18+10 -> T19+2, damage=" + damage + "; sequential T16+10 -> T17+2 -> T18+0 -> T19+0");
            }
            catch (Exception ex) { SdtdConsole.Instance.Output("[AEC-Upgrade-Audit] FAIL checks=" + checks + "; " + ex.GetBaseException()); }
        }
    }
}
