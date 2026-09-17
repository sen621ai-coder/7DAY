using HarmonyLib;

namespace AECT16RuntimeFix
{
    public static class ApiaryProduction
    {
        public static bool Applies(TileEntityCollector te) => te != null && te.blockValue.Block.GetBlockName() == "cntApiary";
        public static void Install(Harmony h)
        {
            Patch(h, "getCurrentConvertCount", nameof(Count), false);
            Patch(h, "getCurrentConvertSpeed", nameof(Speed), false);
            Patch(h, "getMaxProductionCount", nameof(Available), true);
            Patch(h, "removeFuel", nameof(Consume), true);
            Patch(h, "handleUpdateForOutputType", nameof(MigrateTimer), true);
        }
        static void Patch(Harmony h, string target, string method, bool prefix)
        {
            var p = new HarmonyMethod(typeof(ApiaryProduction), method);
            h.Patch(AccessTools.Method(typeof(TileEntityCollector), target), prefix: prefix ? p : null, postfix: prefix ? null : p);
        }
        static bool Fed(TileEntityCollector te, BlockCollector.OutputType type, int count)
        {
            var fuel = te.collector.GetFuelType(type.Fuel);
            // Match native removeFuel, including sandbox modifiers.
            int cost = te.collector.GetSandboxModifiedFuelNeeded(te.fuelCost(type, count));
            return te.getFuelCount(fuel) >= cost;
        }
        static void Count(TileEntityCollector __instance, ref int __result)
        { if (Applies(__instance)) __result *= 5; }
        static void Speed(TileEntityCollector __instance, BlockCollector.OutputType __0, ref ulong __result)
        {
            // XML duration is 96000 ticks. Feeding doubles progress to retain the
            // original 48h cycle; without feed it is 96h. Extractor still doubles speed.
            if (Applies(__instance) && Fed(__instance, __0, __instance.getCurrentConvertCount(__0))) __result *= 2;
        }
        static bool Available(TileEntityCollector __instance, BlockCollector.OutputType __0, ref int __result)
        {
            if (!Applies(__instance)) return true;
            __result = __instance.getCurrentConvertCount(__0);
            return false;
        }
        static bool Consume(TileEntityCollector __instance, int __0, BlockCollector.OutputType __1)
        { return !Applies(__instance) || Fed(__instance, __1, __0); }
        static void MigrateTimer(TileEntityCollector __instance, BlockCollector.OutputType __1)
        {
            if (!Applies(__instance)) return;
            TileEntityCollector.FillData fill;
            if (__instance.fillDataLookup.TryGetValue(__1.Name, out fill) && fill.fillTime == 48000)
            {
                fill.fillTime *= 2;
                fill.fillTimeLeft *= 2;
                __instance.SetModified();
            }
        }
    }
}
