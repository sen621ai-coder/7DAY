using System;
using HarmonyLib;
using AECT16RuntimeFix;
namespace PZAEC.FlyingSword
{
    public static class SwordProgression
    {
        public static void Install(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(EquipmentFusion),"IsFusionClass"),postfix:new HarmonyMethod(typeof(SwordProgression),nameof(Fusion)));
            h.Patch(AccessTools.Method(typeof(FusionTierUpgrade),"Apply"),postfix:new HarmonyMethod(typeof(SwordProgression),nameof(Upgrade)));
        }
        static void Fusion(ItemClass item,ref bool __result){if(item!=null&&SwordRules.Tier(item.GetItemName())>=0)__result=true;}
        public static void Upgrade(ItemValue output,Recipe recipe,ref ItemValue __result)
        {
            if(!SwordRules.IsSword(output)||recipe==null||recipe.count!=1||recipe.ingredients==null)return;
            foreach(var ingredient in recipe.ingredients){var from=ingredient.itemValue;int a=SwordRules.Tier(from),b=SwordRules.Tier(output);if(a<0||b!=a+1||ingredient.count!=1)continue;
                var result=from.Clone();result.type=output.type;result.UseTimes=from.MaxUseTimes>0?from.UseTimes/from.MaxUseTimes*result.MaxUseTimes:0;
                result.SetMetadata(SwordRules.EnergyKey,SwordRules.Energy(from)/SwordRules.Capacity[a]*SwordRules.Capacity[b]);result.SetMetadata(SwordRules.DeployedKey,0);__result=result;return;
            }
        }
    }
}
