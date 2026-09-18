using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace AECT16RuntimeFix
{
    // Shared flight adapter, restricted to explicitly supported helicopter entities.
    public static class MD500FlightControls
    {
        private static bool enabled;
        public const string VehicleName = "vehicleMD500";
        public const string ApacheVehicleName = "vehicleApacheHelicopter";

        public static void Install(Harmony harmony)
        {
            try
            {
                // All helpers remain inert until every patch has installed.
                harmony.Patch(AccessTools.Method(typeof(VPEngine), "Update", new[] { typeof(float) }),
                    transpiler: new HarmonyMethod(typeof(MD500FlightControls), nameof(FuelTranspiler)));
                harmony.Patch(AccessTools.Method(typeof(EntityVehicle), "PhysicsFixedUpdate"),
                    transpiler: new HarmonyMethod(typeof(MD500FlightControls), nameof(GroundActionsTranspiler)));
                harmony.Patch(AccessTools.Method(typeof(Vehicle), "UpdateSimulation"),
                    prefix: new HarmonyMethod(typeof(MD500FlightControls), nameof(BeforeEngineSimulation)));
                harmony.Patch(AccessTools.Method(typeof(EntityVehicle), "FixedUpdateForces"),
                    prefix: new HarmonyMethod(typeof(MD500FlightControls), nameof(BeforeForces)));
                enabled = true;
                Log.Out("[MD500-VTOL] Independent lift/translation active for vehicleMD500 and vehicleApacheHelicopter; native fuel and networking retained.");
            }
            catch (Exception ex)
            {
                enabled = false;
                Log.Error("[MD500-VTOL] Adapter disabled; original controls retained: " + ex.GetBaseException().Message);
            }
        }

        public static bool Applies(Vehicle vehicle)
        {
            return enabled && vehicle != null &&
                (vehicle.GetName() == VehicleName || vehicle.GetName() == ApacheVehicleName);
        }

        public static bool GroundAction(bool pressed, EntityVehicle entity)
        {
            // Jump/Brake (Space) and Crouch/Hop (C) are flight axes here.
            // Suppress wheel braking/hopping only in native ground physics;
            // do not mutate MovementInput or global/player key bindings.
            return entity == null || !Applies(entity.vehicle) ? pressed : false;
        }

        public static IEnumerable<CodeInstruction> GroundActionsTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var jump = AccessTools.Field(typeof(MovementInput), "jump");
            var down = AccessTools.Field(typeof(MovementInput), "down");
            int patched = 0;
            for (int i = 0; i < code.Count; i++)
            {
                if (!code[i].LoadsField(jump) && !code[i].LoadsField(down)) continue;
                code.Insert(++i, new CodeInstruction(OpCodes.Ldarg_0));
                code.Insert(++i, new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(MD500FlightControls), nameof(GroundAction))));
                patched++;
            }
            if (patched != 3) throw new InvalidOperationException("Unexpected V3.2 ground-input layout: " + patched);
            return code;
        }

        public static IEnumerable<CodeInstruction> FuelTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var magnitude = AccessTools.PropertyGetter(typeof(Vector3), "magnitude");
            int patched = 0;
            for (int i = 0; i < code.Count; i++)
            {
                if (!code[i].Calls(magnitude)) continue;
                code.Insert(++i, new CodeInstruction(OpCodes.Ldarg_0));
                code.Insert(++i, new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(MD500FlightControls), nameof(EffectiveFuelSpeed))));
                patched++;
            }
            if (patched != 1) throw new InvalidOperationException("Unexpected V3.2 engine fuel layout: " + patched);
            return code;
        }

        public static float EffectiveFuelSpeed(float speed, VPEngine engine)
        {
            return engine != null && Applies(engine.vehicle)
                ? MD500FlightMath.FuelSpeed(speed, engine.vehicle.VelocityMaxForward) : speed;
        }

        private static bool Powered(EntityVehicle entity)
        {
            return entity != null && entity.hasDriver && entity.IsEngineRunning &&
                entity.vehicle.GetHealth() > 0 &&
                (entity.vehicle.GetFuelLevel() > 0f || EntityVehicle.VehicleFuelUsageModifier == 0f);
        }

        public static void BeforeEngineSimulation(Vehicle __instance)
        {
            if (!Applies(__instance)) return;
            var entity = __instance.entity;
            // Remote instances already receive the native acceleration flag.
            if (entity == null || entity.isEntityRemote || !Powered(entity)) return;
            var input = entity.movementInput;
            if (input == null) return;
            // Keep the rotor loop at flight RPM while hovering/descending.
            // Set before native simulation and native sync, not after it.
            if (entity.GetWheelsOnGround() == 0 || input.jump || input.down)
                __instance.CurrentIsAccel = true;
        }

        public static bool BeforeForces(EntityVehicle __instance)
        {
            if (__instance == null || !Applies(__instance.vehicle)) return true;
            var rb = __instance.vehicleRB;
            var input = __instance.movementInput;
            // Only the native physics authority drives the body. No powered
            // lift after dismount, fuel exhaustion, destruction or submersion.
            if (__instance.isEntityRemote || !__instance.RBActive || rb == null || rb.isKinematic ||
                input == null || !Powered(__instance) || __instance.timeInWater > 0f) return false;
            Vector3 bodyUp = rb.rotation * Vector3.up;
            if (bodyUp.y < .25f) return false;
            var motors = __instance.motors;
            if (motors == null || motors.Length == 0 || motors[0] == null || motors[0].rpmMax <= 0f) return false;
            float power = Mathf.Clamp01(motors[0].rpm / motors[0].rpmMax);
            Vector3 forward = Vector3.ProjectOnPlane(rb.rotation * Vector3.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 velocity = rb.velocity;
            var vehicle = __instance.vehicle;
            float forwardMax = vehicle.IsTurbo ? vehicle.VelocityMaxTurboForward : vehicle.VelocityMaxForward;
            float backwardMax = vehicle.IsTurbo ? vehicle.VelocityMaxTurboBackward : vehicle.VelocityMaxBackward;
            var force = MD500FlightMath.Calculate(input.moveForward, input.moveStrafe, input.jump, input.down,
                Vector3.Dot(velocity, forward), Vector3.Dot(velocity, right), velocity.y,
                rb.angularVelocity.y, __instance.position.y, power,
                forwardMax * vehicle.EffectVelocityMaxPer, backwardMax * vehicle.EffectVelocityMaxPer,
                vehicle.EffectMotorTorquePer);
            // Native PhysicsFixedUpdate already adds gravity (-9.81). Replace
            // only the supported helicopter XML forces; collisions/drag/speed caps remain.
            rb.AddForce(forward * force.Forward + right * force.Right + Vector3.up * force.Up,
                ForceMode.Acceleration);
            Vector3 levelTorque = Vector3.Cross(bodyUp, Vector3.up) * 6f -
                Vector3.ProjectOnPlane(rb.angularVelocity, Vector3.up) * 3f;
            rb.AddTorque(Vector3.ClampMagnitude(levelTorque, 3f) * power + Vector3.up * force.Yaw,
                ForceMode.Acceleration);
            return false;
        }
    }
}
