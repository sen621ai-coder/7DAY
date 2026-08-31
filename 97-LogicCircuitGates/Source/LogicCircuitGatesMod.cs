using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace LogicCircuitGates
{
    public sealed class LogicCircuitGatesMod : IModApi
    {
        private const string HarmonyId = "yf.logic.circuit.gates";
        private const int MaxSignalInputs = 4;
        private static readonly Dictionary<Vector3i, bool> LastOutputs = new Dictionary<Vector3i, bool>();
        private static readonly Dictionary<Vector3i, HashSet<Vector3i>> InputsByGate =
            new Dictionary<Vector3i, HashSet<Vector3i>>();
        private static readonly Dictionary<Vector3i, HashSet<Vector3i>> GatesBySource =
            new Dictionary<Vector3i, HashSet<Vector3i>>();
        private static readonly List<PowerItem> WorkItems = new List<PowerItem>();
        private static readonly HashSet<PowerItem> OffVisited = new HashSet<PowerItem>();
        private static float nextWireRebuildTime;
        private static bool registryChecked;

        public void InitMod(Mod modInstance)
        {
            try
            {
                var harmony = new Harmony(HarmonyId);
                Patch(harmony, typeof(PowerManager), nameof(PowerManager.Update),
                    nameof(PowerManagerUpdatePrefix), null);
                Patch(harmony, typeof(PowerItem), nameof(PowerItem.PowerChildren),
                    nameof(PowerChildrenPrefix), null);
                Patch(harmony, typeof(TileEntityPowered), nameof(TileEntityPowered.SetParentWithWireTool),
                    nameof(SetParentWithWireToolPrefix), null);
                Patch(harmony, typeof(TileEntityPowered), nameof(TileEntityPowered.CreateWireDataFromPowerItem),
                    null, nameof(CreateWireDataFromPowerItemPostfix));
                Patch(harmony, typeof(TileEntityPowered), nameof(TileEntityPowered.SetWireData),
                    null, nameof(SetWireDataPostfix));
                Patch(harmony, typeof(TileEntityPowered), nameof(TileEntityPowered.CheckForNewWires),
                    nameof(CheckForNewWiresPrefix), nameof(CheckForNewWiresPostfix));
                Patch(harmony, typeof(TileEntityPowered), nameof(TileEntityPowered.DrawWires),
                    null, nameof(DrawWiresPostfix));
                Debug.Log("[LogicGates] Zero-consumption pass-through AND/OR/NOT/XOR gates loaded.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[LogicGates] Initialization failed: " + ex.GetBaseException().Message);
            }
        }

        private static void Patch(Harmony harmony, Type type, string methodName, string prefix, string postfix)
        {
            var target = AccessTools.Method(type, methodName);
            if (target == null) throw new MissingMethodException(type.FullName, methodName);
            harmony.Patch(target,
                prefix: prefix == null ? null : new HarmonyMethod(typeof(LogicCircuitGatesMod), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(LogicCircuitGatesMod), postfix));
        }

        // The first incoming connection remains the real power parent. Once that
        // exists, later incoming connections are signal-only inputs.
        public static bool SetParentWithWireToolPrefix(TileEntityPowered __instance, IPowered newParentTE,
            int wiringEntityID)
        {
            try
            {
                GateType gateType;
                if (__instance == null ||
                    (!TryGetGateType(__instance.PowerItem, out gateType) && !IsGateAt(__instance.ToWorldPos())))
                    return true;
                var source = newParentTE as TileEntityPowered;
                if (source == null || source.PowerItem == null) return false;

                bool hasSupply = __instance.HasParent() ||
                    (__instance.PowerItem != null && __instance.PowerItem.Parent != null);
                if (!hasSupply)
                {
                    Debug.Log("[LogicGates] Main power input assigned to gate " + __instance.ToWorldPos() + ".");
                    return true;
                }

                Vector3i gatePosition = __instance.ToWorldPos();
                Vector3i sourcePosition = source.ToWorldPos();
                if (__instance.GetParent() == sourcePosition ||
                    (__instance.PowerItem != null && __instance.PowerItem.Parent != null &&
                     __instance.PowerItem.Parent.Position == sourcePosition))
                    return true;
                bool exists = source.wireDataList.Contains(gatePosition);
                if (!exists && GetInputCount(gatePosition) >= MaxSignalInputs)
                {
                    Debug.LogWarning("[LogicGates] Gate " + gatePosition + " already has four inputs.");
                    return false;
                }

                if (exists)
                {
                    source.wireDataList.Remove(gatePosition);
                    UnregisterSignalWire(sourcePosition, gatePosition);
                    Debug.Log("[LogicGates] Input removed: " + sourcePosition + " -> " + gatePosition + ".");
                }
                else
                {
                    source.wireDataList.Add(gatePosition);
                    RegisterSignalWire(sourcePosition, gatePosition);
                    Debug.Log("[LogicGates] Input added: " + sourcePosition + " -> " + gatePosition + ".");
                }

                source.SendWireData();
                source.RemoveWires();
                source.DrawWires();
                source.MarkChanged();
                __instance.MarkChanged();
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LogicGates] Input connection rejected safely: " +
                    ex.GetBaseException().Message);
                return false;
            }
        }

        public static bool PowerChildrenPrefix(PowerItem __instance, ref bool __result)
        {
            GateType gateType;
            if (!TryGetGateType(__instance, out gateType)) return true;
            __result = Evaluate(__instance, gateType);
            return false;
        }

        public static void PowerManagerUpdatePrefix(PowerManager __instance)
        {
            try
            {
                LogRegistryOnce();
                if (__instance == null || __instance.PowerItemDictionary == null) return;

                if (Time.time >= nextWireRebuildTime)
                {
                    nextWireRebuildTime = Time.time + 2f;
                    RebuildSignalWires(__instance);
                }

                WorkItems.Clear();
                WorkItems.AddRange(__instance.PowerItemDictionary.Values);
                for (int i = 0; i < WorkItems.Count; i++)
                {
                    PowerItem gateItem = WorkItems[i];
                    GateType gateType;
                    if (!TryGetGateType(gateItem, out gateType)) continue;

                    bool output = Evaluate(gateItem, gateType);
                    bool previous;
                    bool changed = !LastOutputs.TryGetValue(gateItem.Position, out previous) || previous != output;
                    LastOutputs[gateItem.Position] = output;
                    if (changed)
                    {
                        if (!output)
                        {
                            OffVisited.Clear();
                            for (int child = 0; child < gateItem.Children.Count; child++)
                                ForceOff(gateItem.Children[child]);
                            OffVisited.Clear();
                        }
                        gateItem.SendHasLocalChangesToRoot();
                        if (gateItem.TileEntity != null)
                        {
                            gateItem.TileEntity.MarkChanged();
                            ApplyWireColors(gateItem.TileEntity);
                        }
                    }
                }
                WorkItems.Clear();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LogicGates] Update skipped: " + ex.GetBaseException().Message);
            }
        }

        // Preserve signal-only wires whenever vanilla rebuilds a source's child list.
        public static void CreateWireDataFromPowerItemPostfix(TileEntityPowered __instance)
        {
            if (__instance == null) return;
            HashSet<Vector3i> gates;
            if (!GatesBySource.TryGetValue(__instance.ToWorldPos(), out gates)) return;
            foreach (Vector3i gate in gates)
            {
                if (!__instance.wireDataList.Contains(gate)) __instance.wireDataList.Add(gate);
            }
        }

        // Reuse the vanilla wire-list packet for client/server display synchronization.
        public static void SetWireDataPostfix(TileEntityPowered __instance)
        {
            try
            {
                RegisterFromWireData(__instance);
                if (SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
                {
                    __instance.MarkChanged();
                    __instance.SendWireData();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LogicGates] Wire sync skipped: " + ex.GetBaseException().Message);
            }
        }

        // Stop vanilla load recovery from turning a saved signal wire into the gate's
        // one real parent. Restore it as visual data immediately afterward.
        public static void CheckForNewWiresPrefix(TileEntityPowered __instance,
            out List<Vector3i> __state)
        {
            __state = new List<Vector3i>();
            if (__instance == null || __instance.wireDataList == null) return;
            Vector3i source = __instance.ToWorldPos();
            for (int i = __instance.wireDataList.Count - 1; i >= 0; i--)
            {
                Vector3i target = __instance.wireDataList[i];
                if (!IsGateAt(target)) continue;
                if (IsSupplyWire(source, target)) continue;
                __state.Add(target);
                __instance.wireDataList.RemoveAt(i);
                RegisterSignalWire(source, target);
            }
        }

        public static void CheckForNewWiresPostfix(TileEntityPowered __instance, List<Vector3i> __state)
        {
            if (__instance == null || __state == null) return;
            for (int i = 0; i < __state.Count; i++)
            {
                if (!__instance.wireDataList.Contains(__state[i])) __instance.wireDataList.Add(__state[i]);
            }
        }

        private static void RebuildSignalWires(PowerManager manager)
        {
            InputsByGate.Clear();
            GatesBySource.Clear();
            WorkItems.Clear();
            WorkItems.AddRange(manager.PowerItemDictionary.Values);
            for (int i = 0; i < WorkItems.Count; i++)
            {
                PowerItem source = WorkItems[i];
                if (source != null && source.TileEntity != null) RegisterFromWireData(source.TileEntity);
            }
            WorkItems.Clear();
        }

        private static void RegisterFromWireData(TileEntityPowered sourceTile)
        {
            if (sourceTile == null || sourceTile.wireDataList == null) return;
            Vector3i source = sourceTile.ToWorldPos();
            for (int i = 0; i < sourceTile.wireDataList.Count; i++)
            {
                Vector3i target = sourceTile.wireDataList[i];
                if (IsGateAt(target) && !IsSupplyWire(source, target)) RegisterSignalWire(source, target);
            }
        }

        private static bool IsSupplyWire(Vector3i source, Vector3i gatePosition)
        {
            PowerManager manager = PowerManager.Instance;
            PowerItem gate = manager == null ? null : manager.GetPowerItemByWorldPos(gatePosition);
            if (gate != null && gate.Parent != null && gate.Parent.Position == source) return true;
            if (GameManager.Instance == null || GameManager.Instance.World == null) return false;
            var tile = GameManager.Instance.World.GetTileEntity(gatePosition) as TileEntityPowered;
            return tile != null && tile.HasParent() && tile.GetParent() == source;
        }

        public static void DrawWiresPostfix(TileEntityPowered __instance)
        {
            ApplyWireColors(__instance);
        }

        private static void ApplyWireColors(TileEntityPowered sourceTile)
        {
            if (sourceTile == null || sourceTile.currentWireNodes == null || sourceTile.wireDataList == null)
                return;
            Vector3i source = sourceTile.ToWorldPos();
            GateType sourceGateType;
            bool sourceIsGate = TryGetGateType(sourceTile.PowerItem, out sourceGateType);
            bool outputOn;
            LastOutputs.TryGetValue(source, out outputOn);
            int count = Math.Min(sourceTile.currentWireNodes.Count, sourceTile.wireDataList.Count);
            for (int i = 0; i < count; i++)
            {
                Vector3i target = sourceTile.wireDataList[i];
                Color color;
                if (sourceIsGate)
                    color = outputOn ? new Color(0.2f, 1f, 0.25f) : new Color(1f, 0.15f, 0.12f);
                else if (IsGateAt(target))
                    color = IsSupplyWire(source, target) ? new Color(1f, 0.8f, 0.1f) : new Color(0.15f, 0.45f, 1f);
                else
                    continue;
                IWireNode node = sourceTile.currentWireNodes[i];
                node.SetPulseColor(color);
                var fast = node as FastWireNode;
                if (fast != null) fast.SetWireColor(color);
                var normal = node as WireNode;
                if (normal != null)
                {
                    normal.wireColor = color;
                    normal.BuildMesh();
                }
            }
        }

        private static bool IsGateAt(Vector3i position)
        {
            PowerManager manager = PowerManager.Instance;
            PowerItem item = manager == null ? null : manager.GetPowerItemByWorldPos(position);
            GateType gate;
            if (TryGetGateType(item, out gate)) return true;
            if (GameManager.Instance == null || GameManager.Instance.World == null) return false;
            BlockValue value = GameManager.Instance.World.GetBlock(position);
            string name = value.Block == null ? null : value.Block.GetBlockName();
            return IsGateName(name);
        }

        private static void RegisterSignalWire(Vector3i source, Vector3i gate)
        {
            HashSet<Vector3i> inputs;
            if (!InputsByGate.TryGetValue(gate, out inputs))
            {
                inputs = new HashSet<Vector3i>();
                InputsByGate[gate] = inputs;
            }
            inputs.Add(source);
            HashSet<Vector3i> gates;
            if (!GatesBySource.TryGetValue(source, out gates))
            {
                gates = new HashSet<Vector3i>();
                GatesBySource[source] = gates;
            }
            gates.Add(gate);
        }

        private static void UnregisterSignalWire(Vector3i source, Vector3i gate)
        {
            HashSet<Vector3i> inputs;
            if (InputsByGate.TryGetValue(gate, out inputs))
            {
                inputs.Remove(source);
                if (inputs.Count == 0) InputsByGate.Remove(gate);
            }
            HashSet<Vector3i> gates;
            if (GatesBySource.TryGetValue(source, out gates))
            {
                gates.Remove(gate);
                if (gates.Count == 0) GatesBySource.Remove(source);
            }
        }

        private static int GetInputCount(Vector3i gate)
        {
            HashSet<Vector3i> inputs;
            return InputsByGate.TryGetValue(gate, out inputs) ? inputs.Count : 0;
        }

        private static bool Evaluate(PowerItem gateItem, GateType gateType)
        {
            HashSet<Vector3i> inputs;
            int connected = InputsByGate.TryGetValue(gateItem.Position, out inputs) ? inputs.Count : 0;
            int active = 0;
            if (inputs != null)
            {
                foreach (Vector3i position in inputs)
                {
                    PowerItem input = PowerManager.Instance.GetPowerItemByWorldPos(position);
                    if (input != null && GetSignal(input)) active++;
                }
            }
            switch (gateType)
            {
                case GateType.And: return connected >= 2 && active == connected;
                case GateType.Or: return connected >= 1 && active >= 1;
                case GateType.Not: return active == 0;
                case GateType.Xor: return connected >= 1 && (active & 1) == 1;
                default: return false;
            }
        }

        private static bool GetSignal(PowerItem input)
        {
            GateType nested;
            bool cached;
            if (TryGetGateType(input, out nested) && LastOutputs.TryGetValue(input.Position, out cached))
                return cached;
            var trigger = input as PowerTrigger;
            if (trigger != null) return trigger.IsPowered && trigger.IsActive;
            var source = input as PowerSource;
            if (source != null) return source.IsOn;
            return input.IsPowered;
        }

        private static void ForceOff(PowerItem item)
        {
            if (item == null || !OffVisited.Add(item)) return;
            item.isPowered = false;
            item.IsPoweredChanged(false);
            item.HandlePowerUpdate(false);
            for (int i = 0; i < item.Children.Count; i++) ForceOff(item.Children[i]);
        }

        private static void LogRegistryOnce()
        {
            if (registryChecked || !Block.BlocksLoaded) return;
            registryChecked = true;
            string[] names = { "logicGateAND", "logicGateOR", "logicGateNOT", "logicGateXOR" };
            for (int i = 0; i < names.Length; i++)
            {
                Block block = Block.GetBlockByName(names[i], true);
                ItemValue item = ItemClass.GetItem(names[i], true);
                Debug.Log("[LogicGates] Registry " + names[i] + ": block=" +
                    (block == null ? "missing" : block.blockID.ToString()) + ", item=" +
                    (item.IsEmpty() ? "missing" : item.type.ToString()) + ".");
            }
        }

        private static bool TryGetGateType(PowerItem item, out GateType gate)
        {
            gate = GateType.None;
            if (item == null || Block.list == null || item.BlockID >= Block.list.Length) return false;
            Block block = Block.list[item.BlockID];
            string name = block == null ? null : block.GetBlockName();
            if (name == null) return false;
            if (name.Equals("logicGateAND", StringComparison.OrdinalIgnoreCase)) gate = GateType.And;
            else if (name.Equals("logicGateOR", StringComparison.OrdinalIgnoreCase)) gate = GateType.Or;
            else if (name.Equals("logicGateNOT", StringComparison.OrdinalIgnoreCase)) gate = GateType.Not;
            else if (name.Equals("logicGateXOR", StringComparison.OrdinalIgnoreCase)) gate = GateType.Xor;
            return gate != GateType.None;
        }

        private static bool IsGateName(string name)
        {
            return name != null && (name.Equals("logicGateAND", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("logicGateOR", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("logicGateNOT", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("logicGateXOR", StringComparison.OrdinalIgnoreCase));
        }

        private enum GateType { None, And, Or, Not, Xor }
    }
}
