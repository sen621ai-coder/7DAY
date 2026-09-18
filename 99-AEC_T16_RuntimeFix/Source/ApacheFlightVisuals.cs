using System;
using HarmonyLib;
using UnityEngine;

namespace AECT16RuntimeFix
{
    // The original weapon manager normally stops these prefab emitters.
    // The native V3.2 weapons use separate visual projectiles and damage authority;
    // keep the legacy particle emitters stopped to avoid duplicate ghost missiles.
    public static class ApacheFlightVisuals
    {
        public static void Install(Harmony harmony)
        {
            try
            {
                harmony.Patch(AccessTools.Method(typeof(EntityVehicle), "PostInit"),
                    postfix: new HarmonyMethod(typeof(ApacheFlightVisuals), nameof(AfterInit)));
            }
            catch (Exception ex)
            {
                Log.Error("[Apache-Flight] Unable to suppress weapon visuals: " + ex.GetBaseException().Message);
            }
        }

        public static void AfterInit(EntityVehicle __instance)
        {
            if (__instance == null || __instance.vehicle == null ||
                __instance.vehicle.GetName() != MD500FlightControls.ApacheVehicleName) return;
            foreach (var particles in __instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                bool weapon = false;
                for (var node = particles.transform; node != null && node != __instance.transform; node = node.parent)
                    if (node.name == "Rocket01" || node.name == "Rocket02") { weapon = true; break; }
                if (!weapon) continue;
                var main = particles.main;
                main.playOnAwake = false;
                var emission = particles.emission;
                emission.enabled = false;
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }
    }
}
