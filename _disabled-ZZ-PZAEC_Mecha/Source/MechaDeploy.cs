using System;
using System.Collections.Generic;
using UnityEngine;

namespace PZAEC.Mecha
{
    // Plays the GLB "Start_Liftoff" clip when a walker is placed, then hands
    // the same joint set to the procedural gait. Sampling is client-side only;
    // each client that witnesses the spawn applies it to its own instance.
    public static class Deploy
    {
        sealed class Channel
        {
            public string NodeName; public byte Path;
            public float[] Times, Values;
        }
        sealed class State
        {
            public float Started;
            public bool Finished;
            public Transform[] Targets; public byte[] Paths; public float[][] Times, Values;
        }
        static readonly Dictionary<int, State> states = new Dictionary<int, State>();
        static readonly List<int> remove = new List<int>();
        static Channel[] channels;
        static float worldReadyAt = -1;
        static bool worldSeen;

        public static bool Ready { get { return channels != null; } }

        public static void LoadClip(GlbFile source)
        {
            var glb = source;
            if (glb.animations == null || glb.animations.Length == 0) return;
            var animation = glb.animations[0];
            var list = new List<Channel>();
            foreach (var channel in animation.channels)
            {
                if (channel.target == null) continue;
                var nodeIndex = channel.target.node;
                if (nodeIndex < 0 || nodeIndex >= glb.nodes.Length) continue;
                var nodeName = glb.nodes[nodeIndex].name;
                if (nodeName == null) continue;
                byte path;
                switch (channel.target.path)
                {
                    case "translation": path = 0; break;
                    case "rotation": path = 1; break;
                    case "scale": path = 2; break;
                    default: continue;
                }
                var sampler = animation.samplers[channel.sampler];
                list.Add(new Channel
                {
                    NodeName = nodeName,
                    Path = path,
                    Times = glb.ReadFloats(sampler.input),
                    Values = glb.ReadFloats(sampler.output)
                });
            }
            channels = list.ToArray();
        }

        public static void Begin(EntityVehicle v)
        {
            if (channels == null || states.ContainsKey(v.entityId)) return;
            // Restored world content arrives in a burst right after load; only
            // walkers that appear later (fresh placements) play the clip.
            if (!worldSeen) { worldSeen = true; worldReadyAt = Time.time; return; }
            if (Time.time - worldReadyAt < 10f) return;
            var rig = Model.GetRig(v);
            if (rig == null) return;
            var targets = new Transform[channels.Length];
            var paths = new byte[channels.Length];
            var times = new float[channels.Length][];
            var values = new float[channels.Length][];
            for (int i = 0; i < channels.Length; i++)
            {
                var target = Find(rig.Visual, channels[i].NodeName);
                if (target == null) continue;
                targets[i] = target; paths[i] = channels[i].Path;
                times[i] = channels[i].Times; values[i] = channels[i].Values;
            }
            states[v.entityId] = new State { Started = Time.time, Targets = targets, Paths = paths, Times = times, Values = values };
        }

        static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        public static bool Playing(EntityVehicle v)
        {
            return v != null && states.TryGetValue(v.entityId, out var state) && !state.Finished;
        }

        public static void Update(World world)
        {
            if (world == null) return;
            remove.Clear();
            foreach (var pair in states)
            {
                var state = pair.Value;
                var vehicle = world.GetEntity(pair.Key) as EntityVehicle;
                if (vehicle == null || !Weapons.IsMecha(vehicle)) { remove.Add(pair.Key); continue; }
                float age = Time.time - state.Started;
                // Mounting fast-forwards the remainder instead of stranding the pilot.
                float duration = Rules.DeploySeconds;
                float t = vehicle.hasDriver ? duration : Mathf.Min(age, duration);
                if (!state.Finished && (t >= duration || age >= duration * 3f))
                {
                    state.Finished = true;
                    t = duration;
                }
                Apply(state, t);
                if (state.Finished) remove.Add(pair.Key);
            }
            foreach (int id in remove) states.Remove(id);
        }

        static void Apply(State state, float time)
        {
            for (int i = 0; i < state.Targets.Length; i++)
            {
                var target = state.Targets[i];
                if (target == null) continue;
                var times = state.Times[i]; var values = state.Values[i];
                if (times == null || times.Length == 0) continue;
                int segment = state.Paths[i] == 1 ? 4 : state.Paths[i] == 0 ? 3 : 3;
                int last = times.Length - 1;
                int k = 0;
                while (k < last && times[k + 1] < time) k++;
                float span = k < last ? Mathf.Max(1e-5f, times[k + 1] - times[k]) : 1f;
                float alpha = k < last ? Mathf.Clamp01((time - times[k]) / span) : 0f;
                int baseA = k * segment, baseB = (k + 1) * segment;
                switch (state.Paths[i])
                {
                    case 0:
                        target.localPosition = new Vector3(
                            -Mathf.Lerp(values[baseA], values[baseB], alpha),
                            Mathf.Lerp(values[baseA + 1], values[baseB + 1], alpha),
                            Mathf.Lerp(values[baseA + 2], values[baseB + 2], alpha));
                        break;
                    case 1:
                        var a = new Quaternion(-values[baseA], values[baseA + 1], values[baseA + 2], values[baseA + 3]).normalized;
                        var b = new Quaternion(-values[baseB], values[baseB + 1], values[baseB + 2], values[baseB + 3]).normalized;
                        target.localRotation = Quaternion.Slerp(a, b, alpha);
                        break;
                    default:
                        target.localScale = new Vector3(
                            Mathf.Lerp(values[baseA], values[baseB], alpha),
                            Mathf.Lerp(values[baseA + 1], values[baseB + 1], alpha),
                            Mathf.Lerp(values[baseA + 2], values[baseB + 2], alpha));
                        break;
                }
            }
        }

        public static void Forget(EntityVehicle v) { states.Remove(v.entityId); }

        public static void Clear() { states.Clear(); remove.Clear(); worldSeen = false; worldReadyAt = -1; }
    }

    // Procedural tripod gait and turbine idle, driven by measured ground speed.
    // Joint swings are offsets from whatever pose Deploy last wrote (end of the
    // liftoff clip), so both systems can share the same transforms.
    public static class Gait
    {
        sealed class Joint
        {
            public Transform Transform;
            public Quaternion Base; public Vector3 BasePosition;
            public float Phase;
        }
        static readonly Dictionary<int, Joint[]> legs = new Dictionary<int, Joint[]>();
        static readonly Dictionary<int, float> phases = new Dictionary<int, float>();
        static readonly Dictionary<int, Quaternion> turbines = new Dictionary<int, Quaternion>();

        public static void Update(World world, EntityVehicle vehicle, Model.Rig rig, float dt)
        {
            if (rig == null || rig.LegSegments.Count == 0) return;
            Joint[] joints;
            if (!legs.TryGetValue(vehicle.entityId, out joints) || joints[0].Transform == null)
            {
                var list = new List<Joint>();
                for (int legIndex = 0; legIndex < 3; legIndex++)
                {
                    var root = rig.LegRoots.Count > legIndex ? rig.LegRoots[legIndex] : null;
                    foreach (var segment in rig.LegSegments)
                    {
                        bool belongs = false;
                        for (var node = segment; node != null; node = node.parent)
                            if (node == root) { belongs = true; break; }
                        if (!belongs) continue;
                        list.Add(new Joint
                        {
                            Transform = segment,
                            Base = segment.localRotation,
                            BasePosition = segment.localPosition,
                            Phase = legIndex * (2f * Mathf.PI / 3f) + list.Count * .35f
                        });
                    }
                }
                joints = list.ToArray();
                legs[vehicle.entityId] = joints;
                if (rig.TurbineL != null) turbines[vehicle.entityId] = rig.TurbineL.localRotation;
                phases[vehicle.entityId] = 0f;
            }

            float speed = 0f;
            var rb = vehicle.vehicleRB;
            if (rb != null)
            {
                var velocity = rb.velocity; velocity.y = 0;
                speed = velocity.magnitude;
            }
            float stride = Mathf.Clamp(speed * .55f, .8f, 3.2f);
            float previous; phases.TryGetValue(vehicle.entityId, out previous);
            float phase = previous + stride * dt * Mathf.PI * 2f;
            phases[vehicle.entityId] = phase;
            float swing = Mathf.Clamp01(speed / 4f) * 8f;

            for (int i = 0; i < joints.Length; i++)
            {
                var joint = joints[i];
                if (joint.Transform == null) continue;
                float wave = Mathf.Sin(phase + joint.Phase);
                float lift = Mathf.Max(0, Mathf.Cos(phase + joint.Phase));
                // Alternate bend direction per segment so the limb reads as
                // articulation rather than a rigid pendulum.
                float direction = (i % 2 == 0) ? 1f : -1f;
                joint.Transform.localRotation = joint.Base * Quaternion.AngleAxis(wave * swing * direction, Vector3.right);
                joint.Transform.localPosition = joint.BasePosition + Vector3.up * (lift * swing * .012f);
            }

            Quaternion turbineBase;
            if (turbines.TryGetValue(vehicle.entityId, out turbineBase))
            {
                var spin = Quaternion.AngleAxis(Rules.TurbineSpinDegreesPerSecond * dt, Vector3.forward);
                turbines[vehicle.entityId] = turbineBase * spin;
                if (rig.TurbineL != null) rig.TurbineL.localRotation = turbines[vehicle.entityId];
                if (rig.TurbineR != null) rig.TurbineR.localRotation = turbines[vehicle.entityId];
            }
        }

        public static void Forget(EntityVehicle v)
        {
            legs.Remove(v.entityId); phases.Remove(v.entityId); turbines.Remove(v.entityId);
        }

        public static void Clear() { legs.Clear(); phases.Clear(); turbines.Clear(); }
    }
}
