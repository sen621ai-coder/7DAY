using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace PZAEC.Mecha
{
    // Turbo jump for the walker: a sustained thruster force while the seated
    // driver holds jump, capped by burn time and recharged on the ground.
    // Native suspension, drag and speed caps stay fully in charge.
    public static class Locomotion
    {
        public struct JumpInfo
        {
            public bool Ready, Active;
            public float CooldownRemaining, BurnRemaining;
        }
        sealed class JumpState
        {
            public float Burned, Cooldown, LastTime;
            public bool Grounded;
        }
        static bool enabled;
        static readonly ConditionalWeakTable<EntityVehicle, JumpState> jumps = new ConditionalWeakTable<EntityVehicle, JumpState>();

        public static void Install(Harmony harmony)
        {
            try
            {
                harmony.Patch(AccessTools.Method(typeof(EntityVehicle), "FixedUpdateForces"),
                    postfix: new HarmonyMethod(typeof(Locomotion), nameof(AfterForces)));
                enabled = true;
            }
            catch (Exception ex) { Log.Warning("[Mecha] Turbo jump disabled: " + ex.GetBaseException().Message); }
        }

        public static void AfterForces(EntityVehicle __instance)
        {
            if (!enabled || !Weapons.IsMecha(__instance) || __instance.isEntityRemote) return;
            var rb = __instance.vehicleRB;
            var input = __instance.movementInput;
            if (rb == null || rb.isKinematic || input == null || !__instance.RBActive) return;
            bool powered = __instance.hasDriver && __instance.IsEngineRunning && __instance.vehicle.GetHealth() > 0 &&
                (__instance.vehicle.GetFuelLevel() > 0f || EntityVehicle.VehicleFuelUsageModifier == 0f);
            var state = jumps.GetValue(__instance, key => new JumpState());
            float now = Time.fixedTime, dt = Time.fixedDeltaTime;
            if (now - state.LastTime > .5f) { state.Burned = 0; state.Cooldown = 0; }
            state.LastTime = now;
            state.Grounded = __instance.GetWheelsOnGround() > 0;
            if (state.Grounded && rb.velocity.y <= .5f) state.Burned = 0;
            state.Cooldown = Mathf.Max(0, state.Cooldown - dt);
            if (!powered || !input.jump) return;
            if (state.Burned >= Rules.JumpMaxSeconds || state.Cooldown > 0) return;
            if ((rb.rotation * Vector3.up).y < .3f) return;
            rb.AddForce(Vector3.up * Rules.JumpAcceleration, ForceMode.Acceleration);
            state.Burned += dt;
            if (state.Burned >= Rules.JumpMaxSeconds) state.Cooldown = Rules.JumpCooldown;
        }

        public static JumpInfo GetJumpState(EntityVehicle vehicle)
        {
            JumpState state;
            if (!enabled || vehicle == null || !jumps.TryGetValue(vehicle, out state))
                return new JumpInfo { Ready = true };
            return new JumpInfo
            {
                Ready = state.Cooldown <= 0 && state.Burned < Rules.JumpMaxSeconds,
                Active = state.Burned > 0 && state.Burned < Rules.JumpMaxSeconds,
                CooldownRemaining = state.Cooldown,
                BurnRemaining = Mathf.Max(0, Rules.JumpMaxSeconds - state.Burned)
            };
        }
    }
}
