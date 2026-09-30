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

    // Procedural biped gait: alternating step cycles with per-leg two-bone IK
    // (hip/knee), feet planted on raycast ground height, hip sway, torso lean
    // and arm counter-swing. Hover tucks the legs; standing settles to a
    // breathing idle. All joint rotations are authored in mount-local space.
    public static class Gait
    {
        sealed class Leg
        {
            public bool Swinging;
            public float SwingStart;
            public Vector3 Foot;      // planted foot target, mount-local
            public Vector3 From, To;  // swing arc endpoints, mount-local
        }
        sealed class Walker
        {
            public EntityVehicle Vehicle;
            public readonly Leg[] Legs = { new Leg(), new Leg() };
            public float Phase;
            public Vector3 SettledFootL, SettledFootR;
        }
        static readonly Dictionary<int, Walker> walkers = new Dictionary<int, Walker>();

        static Vector3 Home(Model.Rig rig, Transform hip)
        {
            return hip.localPosition + Vector3.down * (rig.LegUpper + rig.LegLower) * .94f;
        }

        static float GroundUnder(Model.Rig rig, Transform hip, EntityVehicle v)
        {
            var world = hip.position + Vector3.up * .3f;
            if (Weapons.Trace(v, world, Vector3.down, (rig.LegUpper + rig.LegLower) * 1.2f, out var hit))
                return hit.hit.pos.y;
            return v.position.y;
        }

        // Two-bone analytic IK in the sagittal plane; knee bends backwards.
        // Signs are QA-tunable because the runtime rig axes are procedural.
        const float HipSign = 1f, KneeSign = 1f;

        static void SolveLeg(Model.Rig rig, Transform hip, Transform knee, Vector3 footLocal)
        {
            float l1 = rig.LegUpper, l2 = rig.LegLower;
            Vector3 d = footLocal - hip.localPosition;
            float forward = d.z, down = -d.y;
            float reach = Mathf.Sqrt(forward * forward + down * down);
            float total = l1 + l2;
            float dist = Mathf.Clamp(reach, total * .25f, total * .96f);
            float cosKnee = Mathf.Clamp((l1 * l1 + l2 * l2 - dist * dist) / (2f * l1 * l2), -1f, 1f);
            float kneeAngle = Mathf.PI - Mathf.Acos(cosKnee);
            float cosHip = Mathf.Clamp((l1 * l1 + dist * dist - l2 * l2) / (2f * l1 * dist), -1f, 1f);
            float offHip = Mathf.Acos(cosHip);
            float pitchToFoot = Mathf.Atan2(forward, Mathf.Max(.001f, down));
            float hipPitch = HipSign * (pitchToFoot - offHip);
            hip.localRotation = Quaternion.Euler(hipPitch * Mathf.Rad2Deg, 0f, 0f);
            knee.localRotation = Quaternion.Euler(KneeSign * kneeAngle * Mathf.Rad2Deg, 0f, 0f);
        }

        public static void Update(World world, EntityVehicle vehicle, Model.Rig rig, float dt)
        {
            if (rig == null || rig.HipL == null || rig.HipR == null || rig.KneeL == null || rig.KneeR == null) return;
            // The boarding ceremony owns the joints for its whole duration.
            if (Boarding.ApplyPose(rig, vehicle)) return;
            Walker walker;
            if (!walkers.TryGetValue(vehicle.entityId, out walker) || walker.Vehicle != vehicle)
            {
                walker = new Walker { Vehicle = vehicle };
                walker.Legs[0].Foot = Home(rig, rig.HipL);
                walker.Legs[1].Foot = Home(rig, rig.HipR);
                walker.SettledFootL = walker.Legs[0].Foot;
                walker.SettledFootR = walker.Legs[1].Foot;
                walkers[vehicle.entityId] = walker;
            }

            var rb = vehicle.vehicleRB;
            var velocity = rb != null ? rb.velocity : Vector3.zero;
            var planar = new Vector3(velocity.x, 0, velocity.z);
            float speed = planar.magnitude;
            var mount = rig.HipL.parent;
            var localVelocity = mount.InverseTransformDirection(planar);
            float deploy = Deploy.Progress(vehicle);
            bool hovering = Locomotion.HoverOn;

            float stepSeconds = Rules.StepSeconds / Mathf.Clamp(.55f + speed * .12f, .7f, 1.8f);

            for (int side = 0; side < 2; side++)
            {
                var leg = walker.Legs[side];
                var hip = side == 0 ? rig.HipL : rig.HipR;
                var home = Home(rig, hip);
                Vector3 target = home;
                if (hovering)
                    target = home + Vector3.down * (rig.LegUpper + rig.LegLower) * .3f + Vector3.back * .25f;
                else if (speed > .3f)
                    target = home + localVelocity * Rules.StrideLookahead;
                if (deploy < 1f && !hovering)
                    target -= Vector3.up * (1f - deploy) * (rig.LegUpper + rig.LegLower) * .55f;

                if (hovering || speed <= .3f)
                {
                    // Settle toward home/hover pose; no stepping.
                    leg.Swinging = false;
                    leg.Foot = Vector3.MoveTowards(leg.Foot, target, dt * 3f);
                }
                else if (!leg.Swinging)
                {
                    var other = walker.Legs[1 - side];
                    float dx = target.x - leg.Foot.x, dz = target.z - leg.Foot.z, dy = target.y - leg.Foot.y;
                    bool behind = dx * dx + dz * dz > Rules.StepTriggerDistance * Rules.StepTriggerDistance ||
                        Mathf.Abs(dy) > .45f;
                    if (behind && !other.Swinging)
                    {
                        leg.Swinging = true;
                        leg.SwingStart = Time.time;
                        leg.From = leg.Foot;
                        var worldAim = mount.TransformPoint(target);
                        leg.To = mount.InverseTransformPoint(new Vector3(worldAim.x,
                            GroundUnder(rig, hip, vehicle), worldAim.z));
                    }
                }
                else
                {
                    float t = Mathf.Clamp01((Time.time - leg.SwingStart) / stepSeconds);
                    float ease = t * t * (3f - 2f * t);
                    leg.Foot = Vector3.Lerp(leg.From, leg.To, ease) +
                        Vector3.up * (Mathf.Sin(Mathf.PI * t) * Rules.StepLiftHeight * (hovering ? 0f : 1f));
                    if (t >= 1f) { leg.Swinging = false; leg.Foot = leg.To; }
                }
                SolveLeg(rig, hip, side == 0 ? rig.KneeL : rig.KneeR, leg.Foot);
            }

            // Body dynamics: hip sway, speed-proportional lean, arm swing.
            walker.Phase += speed * dt * 2.4f;
            float stride01 = Mathf.Clamp01(speed / 4f);
            if (rig.Torso != null)
            {
                float sway = hovering ? 0f : Mathf.Sin(walker.Phase) * Rules.HipSwayMeters * stride01;
                float breath = hovering ? 0f : Mathf.Sin(Time.time * 1.7f) * .012f;
                rig.Torso.localPosition = new Vector3(sway, breath, 0f);
                float lean = hovering ? 6f : Mathf.Min(Rules.TorsoLeanDegrees, speed * 1.6f);
                rig.Torso.localRotation = Quaternion.Euler(-lean, 0f, 0f);
            }
            if (rig.ShoulderL != null && rig.ShoulderR != null)
            {
                float swing = hovering ? 8f : Mathf.Sin(walker.Phase) * Rules.ArmSwingDegrees * stride01;
                rig.ShoulderL.localRotation = Quaternion.Euler(swing, 0f, 0f);
                rig.ShoulderR.localRotation = Quaternion.Euler(-swing, 0f, 0f);
            }
        }

        public static void Forget(EntityVehicle v) { walkers.Remove(v.entityId); }

        public static void Clear() { walkers.Clear(); }
    }
}
