using System.Collections.Generic;
using UnityEngine;

namespace AECT16RuntimeFix
{
    // Local presentation only. Aiming never disables the entity, colliders,
    // weapon objects or network state; it only suppresses their local renderers.
    public static class ApacheAimVisibility
    {
        private static EntityVehicle hiddenVehicle;
        private static readonly Dictionary<Renderer, bool> renderers = new Dictionary<Renderer, bool>();
        private static readonly Dictionary<Light, bool> lights = new Dictionary<Light, bool>();

        public static void Update(EntityVehicle vehicle, bool aiming)
        {
            if (!aiming || !ApacheWeapons.IsApache(vehicle))
            {
                Restore();
                return;
            }
            if (hiddenVehicle != vehicle)
            {
                Restore();
                hiddenVehicle = vehicle;
            }
            var state = ApacheWeapons.GetState(vehicle);
            var root = state.Mesh ?? vehicle.ModelTransform;
            if (root == null) return;
            // Repeat while aiming so a cannon created after the camera update is
            // picked up on the next frame without losing its original state.
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                if (!renderers.ContainsKey(renderer)) renderers[renderer] = renderer.enabled;
                renderer.enabled = false;
            }
            foreach (var light in root.GetComponentsInChildren<Light>(true))
            {
                if (light == null) continue;
                if (!lights.ContainsKey(light)) lights[light] = light.enabled;
                light.enabled = false;
            }
        }

        private static void Restore()
        {
            foreach (var pair in renderers) if (pair.Key != null) pair.Key.enabled = pair.Value;
            foreach (var pair in lights) if (pair.Key != null) pair.Key.enabled = pair.Value;
            renderers.Clear();
            lights.Clear();
            hiddenVehicle = null;
        }

        public static void Clear()
        {
            Restore();
        }
    }
}
