using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace AECT16RuntimeFix
{
    // Installed on every peer: vehicle physics can move from driver to server at exit.
    public static class VehicleDismountSafety
    {
        public const float GraceSeconds = 4f, MaxGraceSeconds = 8f, DiagnosticSeconds = 10f;

        public static bool Supported(string name)
        {
            return string.Equals(name, "vehicleMD500", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "vehicleApacheHelicopter", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "vehicleM1Abrams", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "vehicleM1AbramsT17", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "vehicleM1AbramsT18", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "vehicleM1AbramsT19", StringComparison.OrdinalIgnoreCase);
        }

        public static bool Supported(EntityVehicle vehicle)
        { return vehicle != null && vehicle.vehicle != null && Supported(vehicle.vehicle.GetName()); }

        public static void Install()
        {
            var h = new Harmony("pzaec.vehicle.dismount.safety.v1");
            try
            {
                h.Patch(AccessTools.Method(typeof(EntityVehicle), "DetachEntity", new[] { typeof(Entity) }),
                    prefix: new HarmonyMethod(typeof(VehicleDismountSafety), nameof(BeforeDetach)),
                    postfix: new HarmonyMethod(typeof(VehicleDismountSafety), nameof(AfterDetach)));
                h.Patch(AccessTools.Method(typeof(EntityVehicle), "GetAttachedToInfo", new[] { typeof(int) }),
                    postfix: new HarmonyMethod(typeof(VehicleDismountSafety), nameof(ExitPositions)));
                h.Patch(AccessTools.Method(typeof(EntityVehicle), "OnCollisionForward",
                    new[] { typeof(Transform), typeof(Collision), typeof(bool) }),
                    prefix: new HarmonyMethod(typeof(VehicleDismountSafety), nameof(BeforeCollision)));
                h.Patch(AccessTools.Method(typeof(EntityAlive), "damageEntityLocal",
                    new[] { typeof(DamageSource), typeof(int), typeof(bool), typeof(float) }),
                    prefix: new HarmonyMethod(typeof(VehicleDismountSafety), nameof(ObserveDamage)));
                Log.Out("[Vehicle-Dismount] Safe side exits and own-vehicle collision grace enabled (4s, overlap extension up to 8s); all peers must update.");
            }
            catch (Exception ex)
            {
                h.UnpatchSelf();
                Log.Error("[Vehicle-Dismount] Installation failed; native behavior retained: " + ex);
            }
        }

        // At this call Entity.Detach has already cleared AttachedToEntity, but the
        // vehicle's seat still identifies the departing player. Never infer by proximity.
        public static void BeforeDetach(EntityVehicle __instance, Entity __0, out EntityPlayer __state)
        {
            __state = Supported(__instance) && __instance.FindAttachSlot(__0) >= 0
                && __0 is EntityPlayer player && !player.IsDead() ? player : null;
        }

        public static void AfterDetach(EntityVehicle __instance, EntityPlayer __state)
        {
            if (__state == null || __state.IsDead() || __state.AttachedToEntity != null ||
                __instance.FindAttachSlot(__state) >= 0) return;
            var guard = __state.GetComponent<VehicleDismountGuard>() ??
                __state.gameObject.AddComponent<VehicleDismountGuard>();
            guard.Begin(__state, __instance);
        }

        // Match vanilla's absolute world position convention (GetPosition includes
        // floating origin). Use yaw only so banking/rolling cannot put an exit overhead.
        // Native FindValidExitPosition still performs its world obstacle/capsule casts.
        public static void ExitPositions(EntityVehicle __instance, AttachedToEntitySlotInfo __result)
        {
            if (!Supported(__instance) || __result == null) return;
            string name = __instance.vehicle.GetName();
            float side = string.Equals(name, "vehicleMD500", StringComparison.OrdinalIgnoreCase) ? 3f :
                string.Equals(name, "vehicleApacheHelicopter", StringComparison.OrdinalIgnoreCase) ? 4f : 3.3f;
            var yaw = Quaternion.Euler(0, __instance.rotation.y, 0);
            __result.exits.Clear();
            foreach (float z in new[] { 0.9f, -1.5f, 3f, -3f })
                foreach (float x in new[] { -side, side })
                    __result.exits.Add(new AttachedToEntitySlotExit {
                        position = __instance.GetPosition() + yaw * new Vector3(x, .02f, z),
                        rotation = new Vector3(0, Mathf.Atan2(x, z) * Mathf.Rad2Deg + 180 + __instance.rotation.y, 0)
                    });
        }

        public static bool BeforeCollision(EntityVehicle __instance, Collision __1)
        {
            if (!Supported(__instance) || __1 == null) return true;
            var forward = __1.gameObject.GetComponent<ColliderHitCallForward>();
            var player = forward != null ? forward.Entity as EntityPlayer : null;
            if (player == null) player = __1.transform.GetComponentInParent<EntityPlayer>();
            if (player == null && __1.rigidbody != null)
                player = __1.rigidbody.GetComponentInParent<EntityPlayer>();
            var guard = player != null ? player.GetComponent<VehicleDismountGuard>() : null;
            // Also reject queued contacts: IgnoreCollision cannot undo a callback
            // already queued by the preceding physics step. Preserve every other hit.
            if (guard == null || !guard.Protects(__instance)) return true;
            guard.ObserveContact();
            return false;
        }

        public static void ObserveDamage(EntityAlive __instance, DamageSource __0, int __1)
        {
            if (!(__instance is EntityPlayer) || __1 <= 0) return;
            var guard = __instance.GetComponent<VehicleDismountGuard>();
            if (guard != null) guard.ObserveDamage(__0, __1);
        }
    }

    public sealed class VehicleDismountGuard : MonoBehaviour
    {
        private sealed class Pair { public Collider Player, Vehicle; }
        private readonly List<Pair> pairs = new List<Pair>();
        private EntityPlayer player;
        private EntityVehicle vehicle;
        private float started, nextScan;
        private bool protecting, contactLogged;
        private int damageLogs;

        public void Begin(EntityPlayer occupant, EntityVehicle departed)
        {
            Restore();
            player = occupant; vehicle = departed; started = Time.time; nextScan = 0;
            protecting = true; contactLogged = false; damageLogs = 0; enabled = true;
            RefreshPairs();
        }

        public bool Protects(EntityVehicle candidate)
        {
            return enabled && protecting && candidate == vehicle && player != null &&
                vehicle != null && !player.IsDead() && player.AttachedToEntity == null &&
                Time.time - started < VehicleDismountSafety.MaxGraceSeconds;
        }

        private void RefreshPairs()
        {
            if (player == null || vehicle == null) return;
            // Enumerate after native detach has re-enabled the character controller.
            // Refresh periodically for controller/model activation changes on clients.
            var people = player.GetComponentsInChildren<Collider>(true);
            var hull = vehicle.GetComponentsInChildren<Collider>(true);
            foreach (var p in people)
                foreach (var v in hull)
                {
                    if (!Usable(p) || !Usable(v) || p == v || v.GetComponentInParent<Entity>() != vehicle) continue;
                    bool known = false;
                    foreach (var pair in pairs) if (pair.Player == p && pair.Vehicle == v) { known = true; break; }
                    if (known || Physics.GetIgnoreCollision(p, v)) continue;
                    pairs.Add(new Pair { Player = p, Vehicle = v });
                    Physics.IgnoreCollision(p, v, true);
                }
            nextScan = Time.time + .25f;
        }

        private static bool Usable(Collider c)
        { return c != null && c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger; }

        private bool Overlapping()
        {
            foreach (var pair in pairs)
                if (Usable(pair.Player) && Usable(pair.Vehicle))
                {
                    var bounds = pair.Vehicle.bounds;
                    bounds.Expand(.2f);
                    if (bounds.Intersects(pair.Player.bounds)) return true;
                }
            return false;
        }

        private void FixedUpdate() { Tick(); }
        private void Update() { Tick(); }

        public void Tick()
        {
            if (player == null || vehicle == null || player.IsDead() || player.AttachedToEntity != null)
            { enabled = false; Restore(); return; }
            float age = Time.time - started;
            if (protecting)
            {
                if (age >= VehicleDismountSafety.GraceSeconds &&
                    (age >= VehicleDismountSafety.MaxGraceSeconds || !Overlapping()))
                {
                    if (age >= VehicleDismountSafety.MaxGraceSeconds && Overlapping())
                        Log.Out("[Vehicle-Dismount] Grace expired while overlapping: player=" + player.entityId + " vehicle=" + vehicle.entityId);
                    Restore();
                }
                else
                {
                    if (Time.time >= nextScan) RefreshPairs();
                    // Unity can reset ignored pairs when a collider is disabled.
                    foreach (var pair in pairs)
                        if (Usable(pair.Player) && Usable(pair.Vehicle)) Physics.IgnoreCollision(pair.Player, pair.Vehicle, true);
                }
            }
            if (age >= VehicleDismountSafety.DiagnosticSeconds) enabled = false;
        }

        public void ObserveContact()
        {
            if (contactLogged) return;
            contactLogged = true;
            Log.Out("[Vehicle-Dismount] Suppressed queued own-vehicle contact: player=" + player.entityId + " vehicle=" + vehicle.entityId);
        }

        public void ObserveDamage(DamageSource source, int strength)
        {
            if (!enabled || player == null || vehicle == null || player.AttachedToEntity != null ||
                Time.time - started >= VehicleDismountSafety.DiagnosticSeconds || damageLogs >= 4) return;
            damageLogs++;
            Log.Out("[Vehicle-Dismount] Post-exit damage: player=" + player.entityId + " vehicle=" + vehicle.entityId +
                " age=" + (Time.time - started).ToString("F2") + " type=" + (source == null ? "null" : source.damageType.ToString()) +
                " source=" + (source == null ? -1 : source.getEntityId()) + " strength=" + strength + " health=" + player.Health);
            // Diagnostic only. Never cancel weapons, status damage or a real fall.
        }

        private void Restore()
        {
            foreach (var pair in pairs)
                if (pair.Player != null && pair.Vehicle != null) Physics.IgnoreCollision(pair.Player, pair.Vehicle, false);
            pairs.Clear(); protecting = false;
        }
        private void OnDisable() { Restore(); player = null; vehicle = null; }
        private void OnDestroy() { Restore(); player = null; vehicle = null; }
    }
}
