using System;
using HarmonyLib;
using GameEvent.SequenceActions;
namespace SakuraPreview
{
    // Keep the original earned-voucher event and its per-player receipt path.
    public static class SakuraVoucherRewards
    {
        public static string Select(int tier,int roll)
        {
            if(tier<16||tier>19||roll<0||roll>2)throw new ArgumentOutOfRangeException();
            return (roll==0?"PZAECChallengeVoucherT":roll==1?"sakuraMissionBlueprintT":"mintMissionBlueprintT")+tier;
        }
        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(ActionAddItems),"OnClientPerform"),
                prefix:new HarmonyMethod(typeof(SakuraVoucherRewards),nameof(Before)),
                finalizer:new HarmonyMethod(typeof(SakuraVoucherRewards),nameof(Restore)));
        }
        static void Before(ActionAddItems __instance,out string[] __state)
        {
            __state=null;
            if(__instance.AddItems==null || __instance.AddItems.Length!=1)return;
            for(int tier=16;tier<=19;tier++)
                if(__instance.AddItems[0]=="PZAECChallengeVoucherT"+tier)
                {
                    __state=__instance.AddItems;
                    __instance.AddItems=new[]{Select(tier,UnityEngine.Random.Range(0,3))};return;
                }
        }
        static void Restore(ActionAddItems __instance,string[] __state)
        {if(__state!=null)__instance.AddItems=__state;}
    }
}
