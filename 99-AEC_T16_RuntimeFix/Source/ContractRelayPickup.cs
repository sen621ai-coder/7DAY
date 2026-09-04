using System;
using System.Collections.Generic;
using HarmonyLib;

namespace AECT16RuntimeFix
{
    /// <summary>
    /// Restores Block's standard pickup command for the contract relays.
    /// BlockGameEvent replaces the base command list with activate/trigger,
    /// so PickupTarget alone can never expose the take interaction.
    /// </summary>
    public static class ContractRelayPickup
    {
        private static readonly HashSet<string> RelayNames = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            "aecQuestRelay",
            "aecQuestRelay1star",
            "aecQuestRelay2star",
            "aecQuestRelay3star",
            "aecQuestRelay4star",
            "aecQuestRelay5star"
        };

        public static void Install(Harmony harmony)
        {
            try
            {
                var commands = AccessTools.Method(typeof(BlockGameEvent),
                    nameof(BlockGameEvent.GetBlockActivationCommands));
                var activate = AccessTools.Method(typeof(BlockGameEvent),
                    nameof(BlockGameEvent.OnBlockActivated), new[]
                    {
                        typeof(string), typeof(WorldBase), typeof(Vector3i),
                        typeof(BlockValue), typeof(EntityPlayerLocal)
                    });
                if (commands == null || activate == null)
                    throw new MissingMethodException("BlockGameEvent pickup methods");

                harmony.Patch(commands,
                    postfix: new HarmonyMethod(typeof(ContractRelayPickup), nameof(AfterGetCommands)));
                harmony.Patch(activate,
                    prefix: new HarmonyMethod(typeof(ContractRelayPickup), nameof(BeforeActivation)));
                T16RuntimeFixMod.SafeLog("[AEC-Relay-Fix] Contract-relay pickup active.");
            }
            catch (Exception ex)
            {
                T16RuntimeFixMod.SafeLog("[AEC-Relay-Fix] Pickup patch installation failed: " +
                    ex.GetBaseException().Message);
            }
        }

        public static void AfterGetCommands(BlockGameEvent __instance,
            ref BlockActivationCommand[] __result)
        {
            if (!IsRelay(__instance) || !__instance.CanPickup) return;

            var existing = __result ?? BlockActivationCommand.Empty;
            for (int i = 0; i < existing.Length; i++)
            {
                if (string.Equals(existing[i].text, "take", StringComparison.OrdinalIgnoreCase))
                    return;
            }

            var baseCommands = ((Block)__instance).cmds;
            if (baseCommands == null || baseCommands.Length == 0) return;

            var combined = new BlockActivationCommand[existing.Length + 1];
            Array.Copy(existing, combined, existing.Length);
            var take = baseCommands[0];
            take.enabled = true;
            combined[existing.Length] = take;
            __result = combined;
        }

        public static bool BeforeActivation(BlockGameEvent __instance, string _commandName,
            WorldBase _world, Vector3i _blockPos, BlockValue _blockValue,
            EntityPlayerLocal _player, ref bool __result)
        {
            if (!IsRelay(__instance) ||
                !string.Equals(_commandName, "take", StringComparison.OrdinalIgnoreCase))
                return true;

            // This is Block's native pickup path. It checks land-claim access,
            // requires repair first, checks inventory space and performs the
            // authoritative server pickup using each tier's PickupTarget.
            ((Block)__instance).OnBlockActivated(_world, _blockPos, _blockValue, _player);
            __result = true;
            return false;
        }

        public static bool IsRelay(Block block)
        {
            return block != null && RelayNames.Contains(block.blockName ?? string.Empty);
        }
    }
}
