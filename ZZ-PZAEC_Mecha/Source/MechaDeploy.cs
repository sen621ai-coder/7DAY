using System;
using System.Collections.Generic;
using UnityEngine;

namespace PZAEC.Mecha
{
    // Startup rise: a freshly placed robot crouches up from its shipping pose
    // over DeployRiseSeconds. Purely procedural (the GLB has no animation).
    public static class Deploy
    {
        sealed class State { public float Started; public bool Finished; }
        static readonly Dictionary<int, State> states = new Dictionary<int, State>();
        static readonly List<int> remove = new List<int>();
        static float worldReadyAt = -1;
        static bool worldSeen;

        public static void Begin(EntityVehicle v)
        {
            if (states.ContainsKey(v.entityId)) return;
            // Restored world content arrives in a burst right after load; only
            // walkers placed later (fresh) play the rise.
            if (!worldSeen) { worldSeen = true; worldReadyAt = Time.time; return; }
            if (Time.time - worldReadyAt < 10f) return;
            states[v.entityId] = new State { Started = Time.time };
        }

        public static bool Playing(EntityVehicle v)
        {
            return v != null && states.TryGetValue(v.entityId, out var state) && !state.Finished;
        }

        public static float Progress(EntityVehicle v)
        {
            State state;
            if (v == null || !states.TryGetValue(v.entityId, out state)) return 1f;
            return Mathf.Clamp01((Time.time - state.Started) / Rules.DeployRiseSeconds);
        }

        public static void Update(World world)
        {
            if (world == null) return;
            remove.Clear();
            foreach (var pair in states)
            {
                var vehicle = world.GetEntity(pair.Key) as EntityVehicle;
                if (vehicle == null || !Weapons.IsMecha(vehicle)) { remove.Add(pair.Key); continue; }
                var state = pair.Value;
                float t = Mathf.Clamp01((Time.time - state.Started) / Rules.DeployRiseSeconds);
                // Mounting fast-forwards the rise.
                if (vehicle.hasDriver && t < 1f)
                { state.Started = Time.time - Rules.DeployRiseSeconds; t = 1f; }
                if (t >= 1f) { state.Finished = true; remove.Add(pair.Key); }
            }
            foreach (int id in remove) states.Remove(id);
        }

        public static void Forget(EntityVehicle v) { states.Remove(v.entityId); }

        public static void Clear() { states.Clear(); remove.Clear(); worldSeen = false; worldReadyAt = -1; }
    }

}
