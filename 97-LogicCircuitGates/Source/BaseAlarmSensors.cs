using System;
using System.Collections.Generic;
using UnityEngine;

namespace LogicCircuitGates
{
    public enum AlarmKind { None, Perimeter, Breach, Siege, BloodMoon }

    // Uses only world-space positions. A shared spatial index keeps a bank of
    // detectors from scanning the full entity list separately for every device.
    public sealed class AlarmThreatIndex
    {
        private const int CellSize = 32;
        private struct Threat { public Vector3 Position; public bool Siege; }
        private readonly Dictionary<long, List<Threat>> cells = new Dictionary<long, List<Threat>>();
        private readonly Stack<List<Threat>> pool = new Stack<List<Threat>>();
        private static int Cell(float position) { return (int)Math.Floor(position / CellSize); }
        private static long Key(int x, int z) { return ((long)x << 32) | (uint)z; }

        public void Clear()
        {
            foreach (var list in cells.Values) { list.Clear(); pool.Push(list); }
            cells.Clear();
        }

        public void Add(Vector3 position, bool siege)
        {
            long key = Key(Cell(position.x), Cell(position.z));
            if (!cells.TryGetValue(key, out var list))
            {
                list = pool.Count == 0 ? new List<Threat>() : pool.Pop();
                cells.Add(key, list);
            }
            list.Add(new Threat { Position = position, Siege = siege });
        }

        public bool Detect(Vector3 center, float range, bool siegeOnly)
        {
            if (range <= 0) return false;
            float squared = range * range;
            for (int x = Cell(center.x - range); x <= Cell(center.x + range); x++)
            for (int z = Cell(center.z - range); z <= Cell(center.z + range); z++)
            {
                if (!cells.TryGetValue(Key(x, z), out var list)) continue;
                foreach (var threat in list)
                {
                    if (siegeOnly && !threat.Siege) continue;
                    float dx = threat.Position.x - center.x;
                    float dy = threat.Position.y - center.y;
                    float dz = threat.Position.z - center.z;
                    if (dx * dx + dy * dy + dz * dz <= squared) return true;
                }
            }
            return false;
        }
    }

    public sealed class AlarmLatch
    {
        public const float ReleaseDelay = 3f;
        private float releaseAt = float.NegativeInfinity;
        public bool Update(bool powered, bool detected, bool hold, float now)
        {
            if (!powered) { releaseAt = float.NegativeInfinity; return false; }
            if (!hold) { releaseAt = float.NegativeInfinity; return detected; }
            if (detected) releaseAt = now + ReleaseDelay;
            return now < releaseAt;
        }
    }

    public static class BaseAlarmSensors
    {
        public const float ScanInterval = .5f;
        private static readonly AlarmThreatIndex Threats = new AlarmThreatIndex();
        private static readonly Dictionary<PowerTrigger, AlarmLatch> States = new Dictionary<PowerTrigger, AlarmLatch>();
        private static readonly List<PowerTrigger> Sensors = new List<PowerTrigger>();
        private static readonly HashSet<PowerTrigger> Present = new HashSet<PowerTrigger>();
        private static readonly List<PowerTrigger> Removed = new List<PowerTrigger>();
        private static float nextScan;

        public static AlarmKind Kind(string name)
        {
            switch (name)
            {
                case "logicAlarmPerimeter": return AlarmKind.Perimeter;
                case "logicAlarmBreach": return AlarmKind.Breach;
                case "logicAlarmSiege": return AlarmKind.Siege;
                case "logicAlarmBloodMoon": return AlarmKind.BloodMoon;
                default: return AlarmKind.None;
            }
        }

        public static float Range(AlarmKind kind)
        {
            switch (kind)
            {
                case AlarmKind.Perimeter: return 32;
                case AlarmKind.Breach: return 12;
                case AlarmKind.Siege: return 64;
                default: return 0;
            }
        }

        public static bool IsSiege(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length < 3) return false;
            string tier = name.Substring(name.Length - 2);
            if (tier != "16" && tier != "17" && tier != "18" && tier != "19") return false;
            string stem = name.Substring(0, name.Length - 2);
            return stem == "PZAECSiege_heavy_T" || stem == "PZAECSiege_acid_T" ||
                stem == "PZAECSiegeDisruptorT" || stem == "PZAECSiegeSpotterT" || stem == "PZAECSiegeLinesmanT";
        }

        public static bool Eligible(bool alive, bool enemy, bool sleeper, bool player)
        {
            return alive && enemy && !sleeper && !player;
        }

        public static void Reset()
        {
            Threats.Clear(); States.Clear(); Sensors.Clear(); Present.Clear(); Removed.Clear(); nextScan = 0;
        }

        public static void ApplyOutput(PowerTrigger item, bool output)
        {
            if (item.IsTriggered == output) return;
            item.IsTriggered = output;
            // The native setter shuts children down on falling edges. A rising
            // edge requests allocation from the root instead of creating watts.
            item.HandlePowerUpdate(item.IsPowered);
            if (item.TileEntity != null) item.TileEntity.MarkChanged();
        }

        // Called on the authority before logic gates read their input signals.
        // Native switch PowerTriggers own power consumption, children, save and
        // replication; no custom power-item type is written into the save file.
        public static void Update(PowerManager manager, World world, float now)
        {
            if (manager == null || world == null || world.IsRemote() || !Block.BlocksLoaded ||
                Block.list == null || manager.PowerTriggers == null) return;
            if (now < nextScan) return;
            nextScan = now + ScanInterval;
            Sensors.Clear(); Present.Clear();
            bool needsThreats = false;
            foreach (var item in manager.PowerTriggers)
            {
                if (item == null || item.BlockID >= Block.list.Length) continue;
                var block = Block.list[item.BlockID];
                AlarmKind kind = Kind(block == null ? null : block.GetBlockName());
                if (kind == AlarmKind.None) continue;
                if (item.TileEntity == null)
                {
                    ApplyOutput(item, false);
                    continue;
                }
                // Power nodes persist for unloaded chunks. Never scan for them,
                // or let stale nodes at replaced block positions emit signals.
                if (world.GetTileEntity(item.Position) != item.TileEntity ||
                    world.GetBlock(item.Position).type != item.BlockID) continue;
                Sensors.Add(item); Present.Add(item);
                if (item.IsPowered && item.Parent != null && kind != AlarmKind.BloodMoon) needsThreats = true;
            }
            Removed.Clear();
            foreach (var pair in States) if (!Present.Contains(pair.Key)) Removed.Add(pair.Key);
            foreach (var item in Removed) States.Remove(item);
            Threats.Clear();
            if (needsThreats && world.Entities != null && world.Entities.list != null)
            {
                foreach (var entity in world.Entities.list)
                {
                    var alive = entity as EntityAlive;
                    if (alive == null) continue;
                    var definition = EntityClass.GetEntityClass(alive.entityClass);
                    if (!Eligible(!alive.IsDead(), definition != null && definition.bIsEnemyEntity,
                        alive.IsSleeping, alive is EntityPlayer)) continue;
                    Threats.Add(alive.position, IsSiege(EntityClass.GetEntityClassName(alive.entityClass)));
                }
            }
            foreach (var item in Sensors)
            {
                var kind = Kind(Block.list[item.BlockID].GetBlockName());
                bool powered = item.IsPowered && item.Parent != null;
                bool detected = powered && (kind == AlarmKind.BloodMoon ? world.isEventBloodMoon :
                    Threats.Detect(item.Position.ToVector3() + new Vector3(.5f, .5f, .5f), Range(kind), kind == AlarmKind.Siege));
                if (!States.TryGetValue(item, out var latch)) States.Add(item, latch = new AlarmLatch());
                bool output = latch.Update(powered, detected, kind != AlarmKind.BloodMoon, now);
                ApplyOutput(item, output);
            }
        }
    }
}

// Native Switch supplies compatible wiring, persistence and client animation.
// Automatic sensors expose only the original protected pickup command.
public sealed class BlockLogicAlarmSensor : BlockSwitch
{
    public override string GetActivationText(WorldBase world, BlockValue value, Vector3i position, EntityAlive focusing)
    {
        string state = (value.meta & 1) == 0 ? "logicAlarmNoPower" :
            (value.meta & 2) != 0 ? "logicAlarmActive" : "logicAlarmReady";
        return Localization.Get(value.Block.GetBlockName()) + " — " + Localization.Get(state);
    }

    public override BlockActivationCommand[] GetBlockActivationCommands(WorldBase world, BlockValue value,
        Vector3i position, EntityAlive focusing)
    {
        var commands = base.GetBlockActivationCommands(world, value, position, focusing);
        for (int i = 0; i < commands.Length; i++)
            if (commands[i].text == "light") commands[i].enabled = false;
        return commands;
    }

    public override bool OnBlockActivated(string command, WorldBase world, Vector3i position,
        BlockValue value, EntityPlayerLocal player)
    {
        return command == "take" && base.OnBlockActivated(command, world, position, value, player);
    }
}
