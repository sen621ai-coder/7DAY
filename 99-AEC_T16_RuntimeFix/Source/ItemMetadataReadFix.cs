using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Emit;
using HarmonyLib;

namespace AECT16RuntimeFix
{
    // A serialized dictionary replaces the previous item's dictionary. The
    // native live-value setter instead merges it and rejects changed type tags;
    // its error formatter also dereferences null for a serialized None value.
    internal static class ItemMetadataReadFix
    {
        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(ItemValue), "Read", new[] { typeof(BinaryReader) }),
                prefix: new HarmonyMethod(typeof(ItemMetadataReadFix), nameof(BeforeRead)));
            harmony.Patch(AccessTools.Method(typeof(ItemValue), "ReadData"),
                transpiler: new HarmonyMethod(typeof(ItemMetadataReadFix), nameof(ReadMetadataTranspiler)));
            T16RuntimeFixMod.SafeLog("[AEC-Save-Fix] Item metadata replacement on read active.");
        }

        public static void BeforeRead(ItemValue __instance) { __instance.Metadata = null; }

        public static IEnumerable<CodeInstruction> ReadMetadataTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var setter = AccessTools.Method(typeof(ItemValue), "SetMetadata", new[] { typeof(string), typeof(TypedMetadataValue) });
            var replacement = AccessTools.Method(typeof(ItemMetadataReadFix), nameof(RestoreMetadata));
            int replaced = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(setter))
                { instruction.opcode = OpCodes.Call; instruction.operand = replacement; replaced++; }
                yield return instruction;
            }
            if (replaced != 1) throw new InvalidOperationException("ItemValue.ReadData metadata layout changed: " + replaced);
        }

        public static void RestoreMetadata(ItemValue item, string key, TypedMetadataValue value)
        {
            // None (0) is a native on-disk tag with no payload. Unknown tags
            // cannot be skipped safely: never hide a damaged/misaligned stream.
            int tag = value == null ? -1 : (int)value.GetTypeTag();
            if (key == null || tag < 0 || tag > 3)
                throw new InvalidDataException("Invalid item metadata tag " + tag + " on item type " + item.type);
            if (item.Metadata == null) item.Metadata = new Dictionary<string, TypedMetadataValue>();
            item.Metadata[key] = value;
        }
    }
}
