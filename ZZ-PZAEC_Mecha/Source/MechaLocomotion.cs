using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace PZAEC.Mecha
{
    // Hover cruise (Q toggle), descent (C hold) and charged jump (Space hold,
    // release to leap). All forces apply on the physics-authority client like
    // the MD500/Apache flight adapters; trample damage is requested through
    // the weapon intent channel and resolved on the server.
    public static class Locomotion
    {
        sealed class MoveState
        {
            public bool HoverOn, JumpWasHeld;
            public float AirborneSince = -1, NextTrampleTick, LastTime, NextJumpReady;
            public float ChargeStart = -1, PendingJump;
        }
        static bool enabled;
        static readonly ConditionalWeakTable<EntityVehicle, MoveState> moves = new ConditionalWeakTable<EntityVehicle, MoveState>();

        // Update-side input cache from the local driver (consumed in FixedUpdate).
        static bool hoverToggle, descendHeld, jumpHeld;

        public static bool HoverOn { get; private set; }
        public static float JumpCooldownRemaining { get; private set; }
        public static float JumpCharge { get; private set; }

        public static void Install(Harmony harmony)
        {
            try
            {
                harmony.Patch(AccessTools.Method(typeof(EntityVehicle), "FixedUpdateForces"),
                    postfix: new HarmonyMethod(typeof(Locomotion), nameof(AfterForces)));
                enabled = true;
            }
            catch (Exception ex) { Log.Warning("[Mecha] Hover/jump disabled: " + ex.GetBaseException().Message); }
        }

        public static void FeedInput(bool toggle, bool descend, bool jump)
        { hoverToggle = toggle; descendHeld = descend; jumpHeld = jump; }

        static bool Powered(EntityVehicle v)
        {
            return v.hasDriver && v.IsEngineRunning && v.vehicle.GetHealth() > 0 &&
                (v.vehicle.GetFuelLevel() > 0f || EntityVehicle.VehicleFuelUsageModifier == 0f);
        }

        // Ground/support height directly under the hull (blocks only).
        static bool SupportHeight(EntityVehicle v, out float height)
        {
            var start = v.position + Vector3.up * .2f;
            if (Weapons.Trace(v, start, Vector3.down, 6f, out var hit))
            { height = hit.hit.pos.y; return true; }
            height = 0f; return false;
        }

        public static void AfterForces(EntityVehicle __instance)
        {
            HoverOn = false; JumpCooldownRemaining = Mathf.Max(0, JumpCooldownRemaining - Time.fixedDeltaTime);
            if (!enabled || !Weapons.IsMecha(__instance) || __instance.isEntityRemote) return;
            var rb = __instance.vehicleRB;
            var input = __instance.movementInput;
            if (rb == null || rb.isKinematic || input == null || !__instance.RBActive) return;
            var state = moves.GetValue(__instance, key => new MoveState());
            float now = Time.fixedTime, dt = Time.fixedDeltaTime;
            if (now - state.LastTime > .5f) { state.HoverOn = false; state.AirborneSince = -1; state.ChargeStart = -1; }
            state.LastTime = now;
            bool grounded = __instance.GetWheelsOnGround() > 0;
            bool powered = Powered(__instance);

            // Airborne tracking feeds the landing stomp intent.
            if (!grounded)
            {
                if (state.AirborneSince < 0) state.AirborneSince = now;
            }
            else if (state.AirborneSince >= 0)
            {
                if (now - state.AirborneSince >= Rules.StompAirborneSeconds)
                    Weapons.SendLocalIntent(__instance, Weapons.Stomp, Vector3.down, __instance.position);
                state.AirborneSince = -1;
            }

            if (hoverToggle)
            {
                hoverToggle = false;
                state.HoverOn = !state.HoverOn;
                if (state.HoverOn && !powered) state.HoverOn = false;
                Audio.Manager.Play(__instance, state.HoverOn ? "electric_fence_on" : "electric_fence_off", 1, false);
            }
            // Auto-off conditions keep the skimmer from fighting the world.
            if (state.HoverOn && (!powered || __instance.timeInWater > 0f || (rb.rotation * Vector3.up).y < .35f))
            { state.HoverOn = false; Audio.Manager.Play(__instance, "electric_fence_off", 1, false); }

            if (state.HoverOn)
            {
                HoverOn = true;
                float target;
                if (SupportHeight(__instance, out var support)) target = support + Rules.HoverHeight;
                else target = rb.position.y - dt * 1.5f; // no floor within reach: sink gently
                // Ceiling clamp: never press the hull into overhead blocks.
                var up = __instance.position + Vector3.up * 2.6f;
                if (Weapons.Trace(__instance, up, Vector3.up, Rules.HoverCeiling, out var ceiling))
                    target = Mathf.Min(target, ceiling.hit.pos.y - 1.6f);
                float vertical = 9.81f + Mathf.Clamp((target - rb.position.y) * 4f - rb.velocity.y * 1.5f, -Rules.HoverThrust, Rules.HoverThrust);
                if (descendHeld) vertical -= 5f;
                rb.AddForce(Vector3.up * vertical, ForceMode.Acceleration);
                var euler = __instance.rotation; // Vector3 yaw/pitch/roll
                var heading = Quaternion.Euler(0, euler.y, 0);
                var forward = heading * Vector3.forward;
                // Car-style skimming: W/S thrust, A/D yaw torque (native wheel
                // steering is dead with the wheels off the ground).
                rb.AddForce(forward * (input.moveForward * 6f), ForceMode.Acceleration);
                rb.AddTorque(Vector3.up * (input.moveStrafe * 2.5f), ForceMode.Acceleration);
                var planar = new Vector3(rb.velocity.x, 0, rb.velocity.z);
                if (planar.sqrMagnitude > Rules.HoverSpeed * Rules.HoverSpeed)
                    rb.AddForce(-planar.normalized * Mathf.Min(6f, (planar.magnitude - Rules.HoverSpeed) * 2f), ForceMode.Acceleration);
                // Direct fuel draw: skimming is cheap per second but never free.
                __instance.vehicle.SetFuelLevel(Mathf.Max(0f, __instance.vehicle.GetFuelLevel() - Rules.HoverFuelPerSecond * dt));
            }

            // Charged jump: hold Space to charge, release to leap.
            if (jumpHeld && !state.JumpWasHeld) state.ChargeStart = Time.time;
            if (!jumpHeld && state.JumpWasHeld && state.ChargeStart > 0 && Time.fixedTime >= state.NextJumpReady && grounded && powered)
            {
                float level = Mathf.Clamp((Time.time - state.ChargeStart) / Rules.JumpChargeSeconds, Rules.JumpMinCharge, 1f);
                state.PendingJump = level;
            }
            state.JumpWasHeld = jumpHeld;
            JumpCharge = state.ChargeStart > 0 && jumpHeld
                ? Mathf.Clamp01((Time.time - state.ChargeStart) / Rules.JumpChargeSeconds) : 0f;
            if (state.PendingJump > 0 && grounded)
            {
                rb.AddForce(Vector3.up * (state.PendingJump * Rules.JumpMaxSpeed), ForceMode.VelocityChange);
                state.NextJumpReady = Time.fixedTime + Rules.JumpCooldown;
                JumpCooldownRemaining = Rules.JumpCooldown;
                state.PendingJump = 0; state.ChargeStart = -1;
            }

            // Moving trample intent: the physics client knows wheels+speed.
            if (grounded && now >= state.NextTrampleTick)
            {
                state.NextTrampleTick = now + Rules.TrampleTickSeconds;
                var planarSpeed = new Vector3(rb.velocity.x, 0, rb.velocity.z).magnitude;
                if (powered && planarSpeed >= Rules.TrampleSpeedThreshold)
                    Weapons.SendLocalIntent(__instance, Weapons.Trample, Vector3.down, __instance.position);
            }
        }
    }
}
