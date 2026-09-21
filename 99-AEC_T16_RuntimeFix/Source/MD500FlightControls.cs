using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace AECT16RuntimeFix
{
    // Shared flight adapter, restricted to explicitly supported helicopter entities.
    public static class MD500FlightControls
    {
        private static bool enabled;
        private sealed class ThrustState
        {
            public float Forward, Right, Yaw, VerticalCorrection, LastTime, HoldAltitude, ContactTime, AirBlend;
            public bool HoldingAltitude, Initialized, Airborne;
        }
        // Weak ownership avoids retaining despawned vehicles or old worlds.
        private static readonly ConditionalWeakTable<EntityVehicle, ThrustState> thrustStates =
            new ConditionalWeakTable<EntityVehicle, ThrustState>();
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
                // Vehicle's native constructor normalizes its name to lowercase.
                (string.Equals(vehicle.GetName(), VehicleName, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(vehicle.GetName(), ApacheVehicleName, StringComparison.OrdinalIgnoreCase));
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
            var tilt = AccessTools.PropertyGetter(typeof(Vehicle), "TiltUpForce");
            int patched = 0, tiltPatched = 0;
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].Calls(tilt))
                {
                    code.Insert(++i, new CodeInstruction(OpCodes.Ldarg_0));
                    code.Insert(++i, new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(MD500FlightControls), nameof(NativeTiltForce))));
                    tiltPatched++;
                    continue;
                }
                if (!code[i].LoadsField(jump) && !code[i].LoadsField(down)) continue;
                code.Insert(++i, new CodeInstruction(OpCodes.Ldarg_0));
                code.Insert(++i, new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(MD500FlightControls), nameof(GroundAction))));
                patched++;
            }
            if (patched != 3) throw new InvalidOperationException("Unexpected V3.2 ground-input layout: " + patched);
            if (tiltPatched != 2) throw new InvalidOperationException("Unexpected V3.2 tilt-force layout: " + tiltPatched);
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

        public static float NativeTiltForce(float original, EntityVehicle entity)
        {
            // Native wheel-based roll stabilization otherwise fights our bank target.
            return entity != null && Applies(entity.vehicle) ? 0f : original;
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
                input == null || !Powered(__instance) || __instance.timeInWater > 0f)
            { thrustStates.Remove(__instance); return false; }
            Vector3 bodyUp = rb.rotation * Vector3.up;
            if (bodyUp.y < .25f) { thrustStates.Remove(__instance); return false; }
            var motors = __instance.motors;
            if (motors == null || motors.Length == 0 || motors[0] == null || motors[0].rpmMax <= 0f)
            { thrustStates.Remove(__instance); return false; }
            float power = Mathf.Clamp01(motors[0].rpm / motors[0].rpmMax);
            Vector3 forward = Vector3.ProjectOnPlane(rb.rotation * Vector3.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 velocity = rb.velocity;
            var vehicle = __instance.vehicle;
            var profile = MD500FlightMath.Handling(string.Equals(vehicle.GetName(),ApacheVehicleName,StringComparison.OrdinalIgnoreCase));
            float forwardMax = vehicle.IsTurbo ? vehicle.VelocityMaxTurboForward : vehicle.VelocityMaxForward;
            float backwardMax = vehicle.IsTurbo ? vehicle.VelocityMaxTurboBackward : vehicle.VelocityMaxBackward;
            var thrust = thrustStates.GetValue(__instance, key => new ThrustState());
            float now = Time.fixedTime;
            if (now < thrust.LastTime || now - thrust.LastTime > .2f)
            { thrust.Forward=thrust.Right=thrust.Yaw=thrust.VerticalCorrection=0f; thrust.HoldingAltitude=false; thrust.Initialized=false; }
            float dt = Time.fixedDeltaTime;
            bool offGround = __instance.GetWheelsOnGround() == 0;
            if (!thrust.Initialized)
            { thrust.Initialized=true; thrust.Airborne=offGround; thrust.AirBlend=offGround ? 1f : 0f; thrust.ContactTime=0f; }
            if (offGround==thrust.Airborne) thrust.ContactTime=0f;
            else
            {
                thrust.ContactTime+=dt;
                if (thrust.ContactTime >= (offGround ? .12f : .16f))
                { thrust.Airborne=offGround; thrust.ContactTime=0f; }
            }
            thrust.AirBlend=MD500FlightMath.SmoothAxis(thrust.AirBlend,thrust.Airborne ? 1f : 0f,dt,1f,
                thrust.Airborne ? 2f : 4f);
            bool verticalCommand = input.jump != input.down;
            // Brake vertical motion first, then capture the settled altitude. Raw
            // wheel contact releases hold immediately so it cannot fight a slope.
            if (!offGround || !thrust.Airborne || verticalCommand) thrust.HoldingAltitude = false;
            else if (!thrust.HoldingAltitude && Math.Abs(velocity.y)<.15f)
            { thrust.HoldAltitude = __instance.position.y; thrust.HoldingAltitude = true; }
            var force = MD500FlightMath.Calculate(input.moveForward, input.moveStrafe, input.jump, input.down,
                Vector3.Dot(velocity, forward), Vector3.Dot(velocity, right), velocity.y,
                rb.angularVelocity.y, __instance.position.y, thrust.HoldAltitude, thrust.HoldingAltitude, power,
                forwardMax * vehicle.EffectVelocityMaxPer, backwardMax * vehicle.EffectVelocityMaxPer,
                vehicle.EffectMotorTorquePer,profile);
            float torqueScale=MD500FlightMath.Clamp(vehicle.EffectMotorTorquePer,.25f,3f);
            float accelerationLimit=profile.Acceleration*torqueScale*power;
            thrust.Forward=MD500FlightMath.SmoothAxis(thrust.Forward,force.Forward,dt,accelerationLimit,profile.Jerk*torqueScale);
            thrust.Right=MD500FlightMath.SmoothAxis(thrust.Right,force.Right,dt,MD500FlightMath.LateralLimit(power,torqueScale,profile),profile.LateralJerk*torqueScale);
            thrust.Yaw=MD500FlightMath.SmoothAxis(thrust.Yaw,force.Yaw,dt,2f*power,profile.YawJerk);
            float liftPower=MD500FlightMath.LiftPower(power);
            thrust.VerticalCorrection=MD500FlightMath.SmoothAxis(thrust.VerticalCorrection,force.Up-9.81f*liftPower,
                dt,6f*liftPower,profile.VerticalJerk);
            thrust.LastTime = now;
            force.Forward=thrust.Forward; force.Right=thrust.Right; force.Yaw=thrust.Yaw;
            // Native air drag has already reduced angular velocity before this callback.
            // Compensate only while actively turning in the same direction; retain
            // native braking on release/reversal and never bypass the torque bound.
            if(offGround && thrust.Airborne && Math.Abs(input.moveStrafe)>.01f && input.moveStrafe*rb.angularVelocity.y>0f)
                force.Yaw=MD500FlightMath.Clamp(force.Yaw+MD500FlightMath.DragAcceleration(rb.angularVelocity.y,vehicle.AirDragAngVelScale,dt)*power,-2f*power,2f*power);
            force.Up=9.81f*liftPower+thrust.VerticalCorrection;
            // Native PhysicsFixedUpdate already adds gravity (-9.81). Replace
            // only the supported helicopter XML forces; collisions/drag/speed caps remain.
            rb.AddForce(forward * force.Forward + right * force.Right + Vector3.up * force.Up,
                ForceMode.Acceleration);
            float forwardVelocity=Vector3.Dot(velocity,forward);
            float netAcceleration=force.Forward-MD500FlightMath.DragAcceleration(forwardVelocity,vehicle.AirDragVelScale,dt);
            var attitude = MD500FlightMath.TargetAttitude(forwardVelocity,input.moveForward,force.Forward,
                netAcceleration,force.Right,velocity.y,thrust.Airborne,vehicle.IsTurbo,profile);
            float pitch = attitude.Pitch * thrust.AirBlend * (float)(Math.PI / 180.0);
            float bank = attitude.Bank * thrust.AirBlend * (float)(Math.PI / 180.0);
            Vector3 targetUp = (Vector3.up + forward * (float)Math.Tan(pitch) +
                right * (float)Math.Tan(bank)).normalized;
            // Damped torque changes the real body gradually, so native networking,
            // camera and weapon mounts all follow the same attitude.
            // On contact, damp remaining rocking without pulling a slope-parked
            // airframe back to world-horizontal. Both profiles are independent of aim.
            if (!offGround) targetUp=bodyUp;
            // Pitch/roll stabilization must not inject a second yaw command.
            Vector3 levelTorque = Vector3.ProjectOnPlane(Vector3.Cross(bodyUp, targetUp),Vector3.up) * profile.AttitudeGain -
                Vector3.ProjectOnPlane(rb.angularVelocity, Vector3.up) * profile.AttitudeDamping;
            rb.AddTorque(Vector3.ClampMagnitude(levelTorque, 3f) * (power*thrust.AirBlend) + Vector3.up * force.Yaw,
                ForceMode.Acceleration);
            return false;
        }
    }
}
