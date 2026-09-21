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
        private static readonly HashSet<Renderer> currentRenderers = new HashSet<Renderer>();
        private static readonly HashSet<Light> currentLights = new HashSet<Light>();
        private static readonly List<Renderer> removedRenderers = new List<Renderer>();
        private static readonly List<Light> removedLights = new List<Light>();

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
            currentRenderers.Clear();
            currentLights.Clear();
            HideTree(root);
            // Occupants have independent entity hierarchies, outside the aircraft mesh.
            // Include equipment beneath the entity root as well as the body model.
            for (int seat = 0; seat < 2; seat++)
            {
                var occupant = vehicle.GetAttached(seat);
                if (occupant != null) HideTree(occupant.transform);
            }
            // A passenger can dismount or change equipment while the viewer keeps aiming.
            // Restore objects as soon as they leave the current hidden set.
            removedRenderers.Clear();
            foreach (var pair in renderers)
                if (!currentRenderers.Contains(pair.Key))
                {
                    if (pair.Key != null) pair.Key.forceRenderingOff = pair.Value;
                    removedRenderers.Add(pair.Key);
                }
            foreach (var renderer in removedRenderers) renderers.Remove(renderer);
            removedLights.Clear();
            foreach (var pair in lights)
                if (!currentLights.Contains(pair.Key))
                {
                    if (pair.Key != null) pair.Key.enabled = pair.Value;
                    removedLights.Add(pair.Key);
                }
            foreach (var light in removedLights) lights.Remove(light);
        }

        private static void HideTree(Transform root)
        {
            if (root == null) return;
            // Repeat while aiming so a cannon created after the camera update is
            // picked up on the next frame without losing its original state.
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                currentRenderers.Add(renderer);
                if (!renderers.ContainsKey(renderer)) renderers[renderer] = renderer.forceRenderingOff;
                // Body/armor animation may toggle enabled each frame. Keep that native
                // state intact and suppress rendering independently on this client.
                renderer.forceRenderingOff = true;
            }
            foreach (var light in root.GetComponentsInChildren<Light>(true))
            {
                if (light == null) continue;
                currentLights.Add(light);
                if (!lights.ContainsKey(light)) lights[light] = light.enabled;
                light.enabled = false;
            }
        }

        private static void Restore()
        {
            foreach (var pair in renderers) if (pair.Key != null) pair.Key.forceRenderingOff = pair.Value;
            foreach (var pair in lights) if (pair.Key != null) pair.Key.enabled = pair.Value;
            renderers.Clear();
            lights.Clear();
            currentRenderers.Clear();currentLights.Clear();
            removedRenderers.Clear();removedLights.Clear();
            hiddenVehicle = null;
        }

        public static void Clear()
        {
            Restore();
        }
    }
}
