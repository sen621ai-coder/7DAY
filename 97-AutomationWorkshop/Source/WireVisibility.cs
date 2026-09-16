using HarmonyLib;

namespace YFAutomation
{
    public static class WireVisibility
    {
        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(FastWireNode), nameof(FastWireNode.SetVisible)),
                prefix: new HarmonyMethod(typeof(WireVisibility), nameof(KeepVisible)));
        }

        // Keep the native mesh visible when the wire tool is put away. Pool return
        // disables the GameObject directly, so removed/unloaded wires stay removed.
        // Pulse, electrical state, connection limits and saved topology stay native.
        public static void KeepVisible(ref bool _visible)
        {
            _visible = true;
        }
    }
}
