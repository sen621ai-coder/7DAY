using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Emit;
using HarmonyLib;

namespace AECT16RuntimeFix
{
    // Protocol V1 is a distinct packet type. Never change the vanilla wire layout.
    // Both the submitting client and the server's normal rebroadcast use Setup.
    public static class HighDamageNetworking
    {
        public const string LoginMarker = "|PZAEC-Damage32:1";
        public const string UpdateMessage = "高额伤害联机补丁不兼容或未启用。请房主和所有玩家安装相同版本的 99-AEC_T16_RuntimeFix，然后重启游戏。 (PZAEC Damage32 V1)";
        public static bool Active { get; private set; }

        public static void Install()
        {
            var guard = new Harmony("pzaec.damage32.compatibility.v1");
            var core = new Harmony("pzaec.damage32.transport.v1");
            try
            {
                // Guards remain in place if a game update invalidates the damage hook.
                guard.Patch(AccessTools.Method(typeof(NetPackagePlayerLogin), "Setup"),
                    postfix: new HarmonyMethod(typeof(HighDamageNetworking), nameof(LoginPrepared)));
                guard.Patch(AccessTools.Method(typeof(NetPackagePlayerLogin), "ProcessPackage"),
                    prefix: new HarmonyMethod(typeof(HighDamageNetworking), nameof(CheckLogin)));
                guard.Patch(AccessTools.Method(typeof(NetPackageManager), "IdMappingsReceived"),
                    prefix: new HarmonyMethod(typeof(HighDamageNetworking), nameof(CheckMappings)));
                core.Patch(AccessTools.Method(typeof(NetPackageDamageEntity), "ProcessPackage"),
                    transpiler: new HarmonyMethod(typeof(HighDamageNetworking), nameof(RestoreStrength)));
                core.Patch(AccessTools.Method(typeof(NetPackageDamageEntity), "Setup"),
                    prefix: new HarmonyMethod(typeof(HighDamageNetworking), nameof(SelectPacket)));
                Active = true;
                T16RuntimeFixMod.SafeLog("[AEC-Damage32] V1 active: full Int32 damage, one native hit; matching clients/server required.");
            }
            catch (Exception ex)
            {
                Active = false;
                core.UnpatchSelf();
                T16RuntimeFixMod.SafeLog("[AEC-Damage32] FAILED; multiplayer compatibility checks will refuse this installation: " + ex);
            }
        }

        public static bool SelectPacket(NetPackageDamageEntity __instance, int __0,
            DamageResponse __1, ref NetPackageDamageEntity __result)
        {
            if (__1.Strength <= ushort.MaxValue || __instance.GetType() != typeof(NetPackageDamageEntity))
                return true;
            if (!Active) throw new InvalidOperationException("Damage32 transport is not active");
            __result = NetPackageManager.GetPackage<NetPackagePZAECFullDamageV1>().SetupFull(__0, __1);
            // The original packet was obtained by the caller but will never be sent.
            NetPackageManager.FreePackage(__instance);
            return false;
        }

        public static int GetStrength(NetPackageDamageEntity packet)
        {
            var full = packet as NetPackagePZAECFullDamageV1;
            return full == null ? packet.strength : full.FullStrength;
        }

        public static IEnumerable<CodeInstruction> RestoreStrength(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var field = AccessTools.Field(typeof(NetPackageDamageEntity), "strength");
            int matches = 0;
            foreach (var instruction in code)
            {
                if (instruction.opcode != OpCodes.Ldfld || !Equals(instruction.operand, field)) continue;
                // Same stack shape; retain labels/exception blocks and every native event.
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(HighDamageNetworking), nameof(GetStrength));
                matches++;
            }
            if (matches != 1) throw new InvalidOperationException("Damage32: expected exactly one native strength read, found " + matches);
            return code;
        }

        public static bool HasLoginMarker(string version)
        {
            return version != null && version.EndsWith(LoginMarker, StringComparison.Ordinal);
        }

        public static void LoginPrepared(ref string ___version)
        {
            if (Active && !HasLoginMarker(___version)) ___version += LoginMarker;
        }

        public static bool CheckLogin(NetPackagePlayerLogin __instance, string ___version)
        {
            if (Active && HasLoginMarker(___version)) return true;
            T16RuntimeFixMod.SafeLog("[AEC-Damage32] Login refused: " + UpdateMessage);
            if (__instance.Sender != null)
                GameUtils.KickPlayerForClientInfo(__instance.Sender,
                    new GameUtils.KickPlayerData(GameUtils.EKickReason.ModDecision, 0, default(DateTime), UpdateMessage));
            return false;
        }

        public static bool HasProtocol(string[] names)
        {
            // NetPackageManager exchanges Type.Name, including the protocol version suffix.
            return names != null && Array.IndexOf(names, typeof(NetPackagePZAECFullDamageV1).Name) >= 0;
        }

        public static bool CheckMappings(string[] __0)
        {
            if (Active && HasProtocol(__0)) return true;
            T16RuntimeFixMod.SafeLog("[AEC-Damage32] Connection refused: " + UpdateMessage);
            ConnectionManager.Instance.Disconnect();
            GameManager.Instance.ShowMessagePlayerDenied(
                new GameUtils.KickPlayerData(GameUtils.EKickReason.ModDecision, 0, default(DateTime), UpdateMessage));
            return false;
        }
    }

    public sealed class NetPackagePZAECFullDamageV1 : NetPackageDamageEntity
    {
        public int FullStrength { get; private set; }

        public NetPackagePZAECFullDamageV1 SetupFull(int target, DamageResponse response)
        {
            Validate(response.Strength);
            base.Setup(target, response);
            FullStrength = response.Strength;
            return this;
        }

        public override void write(PooledBinaryWriter writer)
        {
            Validate(FullStrength);
            base.write(writer);
            writer.Write(FullStrength);
        }

        public override void read(PooledBinaryReader reader)
        {
            FullStrength = 0;
            // Vanilla read leaves this field untouched when the wire says no item.
            attackingItem = null;
            base.read(reader);
            int value = reader.ReadInt32();
            Validate(value);
            FullStrength = value;
        }

        public override int GetLength() { return base.GetLength() + sizeof(int); }

        private static void Validate(int value)
        {
            if (value <= ushort.MaxValue) throw new InvalidDataException("Invalid Damage32 V1 strength: " + value);
        }
    }
}
