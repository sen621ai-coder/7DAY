using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LogicCircuitGates
{
    // Detached native power objects only: never places blocks, spawns enemies,
    // rewires circuits, or changes the live world's time or Blood Moon state.
    public sealed class ConsoleCmdBaseAlarmAudit : ConsoleCmdAbstract
    {
        public override string[] getCommands() { return new[] { "logicAlarmCheck" }; }
        public override string getDescription() { return "Read-only base alarm configuration and detached native power-flow audit."; }
        private int checks;
        private void Check(bool ok, string text) { checks++; if (!ok) throw new Exception(text); }

        public override void Execute(List<string> parameters, CommandSenderInfo senderInfo)
        {
            checks = 0;
            try
            {
                string[] names = { "logicAlarmPerimeter", "logicAlarmBreach", "logicAlarmSiege", "logicAlarmBloodMoon" };
                int[] watts = { 5, 5, 10, 1 };
                for (int i = 0; i < names.Length; i++)
                {
                    var block = Block.GetBlockByName(names[i], true) as BlockLogicAlarmSensor;
                    Check(block != null, names[i] + " block class missing");
                    Check(block.RequiredPower == watts[i], names[i] + " power cost");
                    Check(!ItemClass.GetItem(names[i], true).IsEmpty(), names[i] + " item registration");
                    Check(Localization.Get(names[i]) != names[i], names[i] + " localization");

                    var sensor = new PowerTrigger { BlockID = (ushort)block.blockID,
                        RequiredPower = (ushort)watts[i], TriggerType = PowerTrigger.TriggerTypes.Switch };
                    var child = new PowerItem { RequiredPower = 5, Parent = sensor };
                    sensor.Children.Add(child);
                    ushort budget = 100;
                    sensor.HandlePowerReceived(ref budget);
                    Check(sensor.IsPowered && !child.IsPowered && budget == 100 - watts[i], names[i] + " standby allocation");
                    BaseAlarmSensors.ApplyOutput(sensor, true);
                    budget = 100;
                    sensor.HandlePowerReceived(ref budget);
                    Check(sensor.IsPowered && child.IsPowered && budget == 95 - watts[i], names[i] + " active allocation");
                    BaseAlarmSensors.ApplyOutput(sensor, false);
                    Check(!sensor.IsActive && !child.IsPowered, names[i] + " falling edge disconnect");
                    // Exact supply covers the detector, but cannot run its load.
                    BaseAlarmSensors.ApplyOutput(sensor, true);
                    budget = (ushort)watts[i];
                    sensor.HandlePowerReceived(ref budget);
                    Check(sensor.IsPowered && !child.IsPowered && budget == 0, names[i] + " no free output watts");
                    sensor.HandleDisconnect();
                    BaseAlarmSensors.ApplyOutput(sensor, false);
                    Check(!sensor.IsPowered && !sensor.IsTriggered && !child.IsPowered, names[i] + " power-loss reset");
                    budget = (ushort)(watts[i] - 1);
                    sensor.HandlePowerReceived(ref budget);
                    Check(!sensor.IsPowered && !child.IsPowered && budget == 0, names[i] + " insufficient input");

                    // Exercise the unchanged vanilla save format with an active
                    // switch, then recompute it against an empty detection set.
                    sensor.Children.Clear();
                    BaseAlarmSensors.ApplyOutput(sensor, true);
                    var restored = new PowerTrigger();
                    using (var stream = new MemoryStream())
                    {
                        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) sensor.write(writer);
                        stream.Position = 0;
                        using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
                            restored.read(reader, PowerManager.Instance.CurrentFileVersion);
                        Check(stream.Position == stream.Length, names[i] + " vanilla save record consumed");
                    }
                    Check(restored.IsTriggered && restored.TriggerType == PowerTrigger.TriggerTypes.Switch, names[i] + " vanilla save roundtrip");
                    BaseAlarmSensors.ApplyOutput(restored, new AlarmLatch().Update(true, false, true, 0));
                    Check(!restored.IsTriggered, names[i] + " clear stale saved alarm");
                }
                var index = new AlarmThreatIndex();
                foreach (var center in new[] { new Vector3(0, 0, 0), new Vector3(-1616, 48, -1152), new Vector3(-1616, 48, -1168) })
                foreach (float range in new[] { 12f, 32f, 64f })
                {
                    index.Clear(); index.Add(new Vector3(center.x + range, center.y, center.z), true);
                    Check(index.Detect(center, range, true), "world-space range boundary");
                    index.Clear(); index.Add(new Vector3(center.x, center.y + range + .1f, center.z), true);
                    Check(!index.Detect(center, range, false), "vertical range boundary");
                }
                SdtdConsole.Instance.Output("[LogicAlarm-Audit] PASS checks=" + checks + "; failures=0.");
            }
            catch (Exception ex)
            {
                SdtdConsole.Instance.Output("[LogicAlarm-Audit] FAIL checks=" + checks + "; " + ex.GetBaseException());
            }
        }
    }
}
