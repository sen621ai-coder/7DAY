using System;
using System.Text;
using UnityEngine;

namespace PZAEC.Mecha
{
    // Debug dump: F9 while near a walker logs every render part's state,
    // transform, material and the rig anchors so visual regressions can be
    // diagnosed from the player log without a debugger.
    public static class MechaDebug
    {
        public static void Dump(World world, EntityVehicle vehicle)
        {
            if (vehicle == null) return;
            var sb = new StringBuilder();
            sb.AppendLine("[MechaDebug] vehicleId=" + vehicle.entityId + " pos=" + vehicle.position +
                " rot=" + vehicle.rotation + " health=" + vehicle.vehicle.GetHealth() + "/" + vehicle.vehicle.GetMaxHealth() +
                " fuel=" + vehicle.vehicle.GetFuelLevel() + " engineRunning=" + vehicle.IsEngineRunning +
                " hasDriver=" + vehicle.hasDriver + " wheelsOnGround=" + vehicle.GetWheelsOnGround());
            var rb = vehicle.vehicleRB;
            if (rb != null) sb.AppendLine("[MechaDebug] rb pos=" + rb.position + " rot=" + rb.rotation.eulerAngles +
                " vel=" + rb.velocity + " kinematic=" + rb.isKinematic + " mass=" + rb.mass);
            var rig = Model.GetRig(vehicle);
            if (rig != null)
            {
                sb.AppendLine("[MechaDebug] rig anchors:");
                DumpAnchor(sb, "Head", rig.Head);
                DumpAnchor(sb, "Torso", rig.Torso);
                DumpAnchor(sb, "HandL", rig.HandL);
                DumpAnchor(sb, "HandR", rig.HandR);
                DumpAnchor(sb, "HipL", rig.HipL);
                DumpAnchor(sb, "KneeL", rig.KneeL);
                DumpAnchor(sb, "Backpack", rig.Backpack);
            }
            var visual = Find(vehicle.PhysicsTransform != null ? vehicle.PhysicsTransform : vehicle.transform, "MechaVisual");
            if (visual == null) { sb.AppendLine("[MechaDebug] MechaVisual missing!"); }
            else
            {
                var renderers = visual.GetComponentsInChildren<Renderer>(true);
                sb.AppendLine("[MechaDebug] renderers=" + renderers.Length);
                int enabledCount = 0;
                foreach (var r in renderers)
                {
                    if (r.enabled) enabledCount++;
                    sb.AppendLine("  " + Path(r.transform) + " mat=" + (r.sharedMaterial != null ? r.sharedMaterial.name : "NULL") +
                        " enabled=" + r.enabled + " pos=" + r.transform.position + " scale=" + r.transform.lossyScale);
                }
                sb.AppendLine("[MechaDebug] enabled=" + enabledCount + "/" + renderers.Length);
            }
            Log.Out(sb.ToString());
        }

        static void DumpAnchor(StringBuilder sb, string name, Transform t)
        {
            sb.AppendLine("  " + name + (t == null ? " MISSING" : " pos=" + t.position + " rot=" + t.rotation.eulerAngles + " scale=" + t.lossyScale));
        }

        static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        static string Path(Transform t)
        {
            var path = t.name;
            while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }
    }
}
