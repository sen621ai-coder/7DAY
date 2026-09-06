using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace AECT16RuntimeFix
{
    public sealed class ConsoleCmdSaveReadAudit : ConsoleCmdAbstract
    {
        int checks;
        readonly List<string> failures = new List<string>();
        public override string[] getCommands() { return new[] { "aecsavecheck" }; }
        public override string getDescription() { return "Isolated native save-read regression; reads local copied player files only."; }
        void Check(bool ok, string message) { checks++; if (!ok) failures.Add(message); }
        static ItemValue Item(string name) { return new ItemValue(ItemClass.GetItem(name, false).type, 6, 6, false, null, 1); }
        static byte[] Bytes(ItemValue value)
        {
            using (var stream = new MemoryStream())
            { var writer = new BinaryWriter(stream); value.Write(writer); writer.Flush(); return stream.ToArray(); }
        }
        void Read(ItemValue into, byte[] bytes)
        {
            using (var stream = new MemoryStream(bytes))
            { into.Read(new BinaryReader(stream)); Check(stream.Position == stream.Length, "item payload fully consumed"); }
        }
        public override void Execute(List<string> args, CommandSenderInfo sender)
        {
            if (!GamePrefs.GetString(EnumGamePrefs.GameName).StartsWith("AEC_Equipment_Verification", StringComparison.Ordinal))
            { SdtdConsole.Instance.Output("[AEC-Save-Audit] Refused outside verification save."); return; }
            checks = 0; failures.Clear();
            try
            {
                var broken = Item("gunPZAECHorizonNeedleT19");
                broken.SetMetadata("same", 3);
                bool reproduced = false;
                try { broken.SetMetadata("same", new TypedMetadataValue(null, (TypedMetadataValue.TypeTag)0)); }
                catch (NullReferenceException) { reproduced = true; }
                Check(reproduced, "original metadata setter reproduces logged null-reference failure");

                var original = Item("gunPZAECHorizonNeedleT19");
                original.SetMetadata("AECFusionRank", 10);
                original.SetMetadata("audit", "keep");
                original.UseTimes = 71;
                original.Modifications[0] = ItemClass.GetItem("modGunScopeSmall", false);
                byte[] saved = Bytes(original);
                var restored = new ItemValue();
                restored.SetMetadata("AECFusionRank", "wrong previous type");
                restored.SetMetadata("stale", 999);
                Read(restored, saved);
                Check(EquipmentFusion.Rank(restored) == 10, "fusion rank restored from stream");
                Check(!restored.Metadata.ContainsKey("stale"), "previous item metadata removed");
                Check(restored.Metadata["audit"].GetValue().Equals("keep"), "custom metadata retained");
                Check(restored.Quality == 6 && restored.UseTimes == 71, "quality and wear retained");
                Check(restored.Modifications[0].type == original.Modifications[0].type, "attachment retained");
                Read(restored, Bytes(new ItemValue()));
                Check(restored.Metadata == null && restored.IsEmpty(), "empty slot clears old fusion metadata");

                // Exact native v8 payload with repeated metadata key; the None
                // record is legal to write but the native setter crashes on it.
                using (var stream = new MemoryStream())
                {
                    var writer = new BinaryWriter(stream);
                    writer.Write((byte)8); writer.Write((byte)1);
                    writer.Write((ushort)(original.type - Block.ItemsStartHere));
                    writer.Write(0f); writer.Write((ushort)6); writer.Write((ushort)0);
                    writer.Write((byte)2); writer.Write("same"); writer.Write(2); writer.Write(3);
                    writer.Write("same"); writer.Write(0);
                    writer.Write((byte)0); writer.Write((byte)0);
                    writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)0);
                    Read(restored, stream.ToArray());
                    Check((int)restored.Metadata["same"].GetTypeTag() == 0, "duplicate None key read without NRE");
                }
                bool invalidRejected = false;
                try { ItemMetadataReadFix.RestoreMetadata(restored, "bad", new TypedMetadataValue(null, (TypedMetadataValue.TypeTag)99)); }
                catch (InvalidDataException) { invalidRejected = true; }
                Check(invalidRejected, "unknown type tag rejected instead of hiding stream damage");
                bool truncatedRejected = false;
                try { Read(restored, saved.Take(10).ToArray()); }
                catch (EndOfStreamException) { truncatedRejected = true; }
                Check(truncatedRejected, "truncated item still rejected");

                foreach (string family in new[] { "Harrier", "Storm", "Tremor", "Warden" })
                foreach (int tier in Enumerable.Range(16, 4))
                {
                    var old = Item("itemPZAEC" + family + "DeviceT" + tier);
                    Check(!old.IsEmpty() && old.ItemClass != null, "retired definition present");
                    old.Modifications = new ItemValue[6];
                    for (int i = 0; i < 6; i++) old.Modifications[i] = new ItemValue();
                    old.Modifications[0] = original.Modifications[0].Clone();
                    var result = new ItemValue(); Read(result, Bytes(old));
                    Check(result.type == old.type && result.Modifications[0].type == old.Modifications[0].type, "retired item and nested attachment retained");
                    Check(old.ItemClass.Actions[0] == null, "retired item cannot cast ability");
                }
                List<SignRenderer> signs = new List<SignRenderer> { null };
                var previous = signs;
                WorldLogRecovery.BeforeSignRendering(ref signs);
                Check(signs.Count == 0 && previous.Count == 1, "stale sign references filtered without changing owner list");
                CheckFallenLoot(original);
                ReadCopiedPlayers();
            }
            catch (Exception ex) { failures.Add(ex.ToString()); }
            foreach (var failure in failures) SdtdConsole.Instance.Output("[AEC-Save-Audit] ERROR " + failure);
            SdtdConsole.Instance.Output("[AEC-Save-Audit] " + (failures.Count == 0 ? "PASS" : "FAIL") + " checks=" + checks + "; failures=" + failures.Count + ".");
        }

        void CheckFallenLoot(ItemValue item)
        {
            EntityLootContainer loot = null;
            var originalOrigin = Origin.position;
            try
            {
                loot = EntityFactory.CreateEntity(EntityClass.FromString("EntityLootContainerStrong"), new Vector3(0, 100, 0)) as EntityLootContainer;
                Check(loot != null, "native loot entity construction");
                if (loot == null) return;
                loot.world = GameManager.Instance.World;
                loot.bag = new Bag(18);
                loot.bag.items[0] = new ItemStack(item.Clone(), 1);
                var bag = loot.bag;
                loot.belongsPlayerId = 123;
                var normal = loot.position;
                Check(WorldLogRecovery.RecoverAtHeight(loot, 60) && loot.position == normal, "normal loot position unchanged");
                loot.SetPosition(new Vector3(12.5f, -30000, 15.5f), true);
                Check(!WorldLogRecovery.RecoverAtHeight(loot, float.NaN) && loot.position.y == -30000, "no recovery on invalid terrain height");
                Check(!WorldLogRecovery.RecoverAtHeight(loot, -1), "no recovery below world floor");
                loot.motion = new Vector3(0, -200, 0);
                if (loot.itemRB != null && !loot.itemRB.isKinematic) loot.itemRB.velocity = loot.motion;
                Check(WorldLogRecovery.RecoverAtHeight(loot, 60), "fallen loot recovered");
                Check(loot.position == new Vector3(12.5f, 62, 15.5f), "recovery preserves horizontal position above terrain");
                Check(loot.motion == Vector3.zero && loot.physicsVel == Vector3.zero, "fall velocity reset");
                Check(loot.itemRB == null || loot.itemRB.isKinematic || loot.itemRB.velocity == Vector3.zero, "native rigidbody velocity reset");
                Check(ReferenceEquals(bag, loot.bag) && loot.belongsPlayerId == 123 && loot.bag.items[0].itemValue.type == item.type, "bag contents and ownership preserved");
                var recovered = loot.position;
                Check(WorldLogRecovery.RecoverAtHeight(loot, 80) && loot.position == recovered, "recovery not repeated for healthy loot");
                var updateTransform = AccessTools.Method(typeof(EntityItem), "updateTransform");
                foreach (var offset in new[] { Vector3.zero, new Vector3(-1616, 48, -1152), new Vector3(-1616, 48, -1168) })
                {
                    Origin.position = offset;
                    var underground = new Vector3(-1583.95f, -22730.63f, -1208.09f);
                    var ground = new Vector3(underground.x, 67, underground.z);
                    loot.isPhysicsMaster = true;
                    loot.SetPosition(underground, true);
                    loot.transform.position = underground - offset;
                    if (loot.itemRB != null) loot.itemRB.position = underground - offset;
                    // Reproduce the previous repair: position field changes,
                    // but the native physics-master update restores the root's
                    // old underground position on the very next frame.
                    loot.SetPosition(ground, true);
                    updateTransform.Invoke(loot, null);
                    Check(loot.position.y < -32, "old SetPosition-only repair reproduces snap-back");
                    Check(WorldLogRecovery.RecoverAtHeight(loot, 65), "repair after native snap-back");
                    Check((loot.transform.position + offset - ground).sqrMagnitude < .001f, "root transform moved in scene coordinates");
                    Check(loot.itemRB == null || (loot.itemRB.position + offset - ground).sqrMagnitude < .001f, "rigidbody moved in scene coordinates");
                    for (int frame = 0; frame < 20; frame++) updateTransform.Invoke(loot, null);
                    Check((loot.position - ground).sqrMagnitude < .001f, "20 physics-master updates cannot undo recovery");
                    loot.isPhysicsMaster = false;
                    for (int frame = 0; frame < 20; frame++) updateTransform.Invoke(loot, null);
                    Check((loot.transform.position + offset - ground).sqrMagnitude < .001f, "20 interpolation updates remain at recovered position");
                    Check(ReferenceEquals(bag, loot.bag) && loot.belongsPlayerId == 123, "repeated updates preserve bag and owner");
                }
            }
            finally { Origin.position = originalOrigin; if (loot != null) UnityEngine.Object.Destroy(loot.gameObject); }
        }

        void ReadCopiedPlayers()
        {
            string root = Path.GetFullPath(Path.Combine(EquipmentStatDisplay.ModDirectory, "..", ".local-tests", "save-review-20260906"));
            string directory = Path.Combine(root, "Player");
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("Copied player audit directory missing");
            var originalList = ItemClass.list;
            try
            {
                var mapped = (ItemClass[])originalList.Clone();
                int remapped = 0, missing = 0;
                using (var reader = new BinaryReader(File.OpenRead(Path.Combine(root, "itemmappings.nim"))))
                {
                    if (reader.ReadInt32() != 1) throw new InvalidDataException("Unknown item mapping version");
                    int count = reader.ReadInt32();
                    for (int i = 0; i < count; i++)
                    {
                        int id = reader.ReadInt32(); string name = reader.ReadString();
                        var definition = ItemClass.GetItemClass(name, false);
                        if (id < 0 || id >= mapped.Length) throw new InvalidDataException("Mapping ID out of bounds");
                        if (definition == null) missing++;
                        if (mapped[id] != definition) remapped++;
                        mapped[id] = definition;
                    }
                }
                ItemClass.list = mapped;
                SdtdConsole.Instance.Output("[AEC-Save-Audit] Copied save mapping overrides=" + remapped + "; missing definitions=" + missing);
                Check(missing == 0, "copied save item mapping definitions exist");
                int index = 0;
                foreach (string path in Directory.GetFiles(directory).Where(p => p.EndsWith(".ttp") || p.EndsWith(".ttp.bak")))
                {
                    index++;
                    using (var stream = File.OpenRead(path))
                    using (var reader = new PooledBinaryReader())
                    {
                        reader.SetBaseStream(stream);
                        if (reader.ReadChar() != 't' || reader.ReadChar() != 't' || reader.ReadChar() != 'p' || reader.ReadChar() != 0)
                            throw new InvalidDataException("Bad copied player header");
                        uint version = reader.ReadByte();
                        var data = new PlayerDataFile();
                        try
                        {
                            data.Read(reader, version);
                            Check(stream.Position == stream.Length, "copied player payload fully consumed " + index);
                            SdtdConsole.Instance.Output("[AEC-Save-Audit] Copied player " + index + " read " + stream.Position + "/" + stream.Length + " bytes; entity=" + data.ecd.id);
                        }
                        catch (Exception ex) { failures.Add("Copied player " + index + " offset " + stream.Position + ": " + ex); }
                    }
                }
                Check(index == 4, "four copied primary/backup player files examined");
            }
            finally { ItemClass.list = originalList; }
        }
    }
}
