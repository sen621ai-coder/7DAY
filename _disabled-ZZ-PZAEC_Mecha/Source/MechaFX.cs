using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PZAEC.Mecha
{
    // Client-side beam tracers, missile bodies and impact effects fed by the
    // authoritative event stream. Purely cosmetic; no damage or state here.
    public static class MechaFX
    {
        sealed class Projectile
        {
            public GameObject Object; public Vector3 Position, Velocity; public float Life;
        }
        sealed class Tracer { public LineRenderer Line; public Vector3 A, B; public float Life; }
        public sealed class Status
        {
            public float Time = -100, Heat, LockProgress;
            public float HullFraction = 1, RepairProgress, BattleRepairWait;
            public int BeamAmmo, MissileAmmo, Flags; public float MissileWait;
            public bool MeleeMode;
            public Vector3 Direction;
        }
        static readonly Dictionary<int, float> meleeCooldownUntil = new Dictionary<int, float>();

        public static float MeleeCooldownRemaining(int vehicleId)
        {
            float until;
            return meleeCooldownUntil.TryGetValue(vehicleId, out until) ? Mathf.Max(0, until - Time.time) : 0;
        }
        static readonly Dictionary<int, Projectile> projectiles = new Dictionary<int, Projectile>();
        static readonly Dictionary<int, Status> statuses = new Dictionary<int, Status>();
        static readonly List<Tracer> tracers = new List<Tracer>();
        static readonly Stack<Tracer> pool = new Stack<Tracer>();
        static readonly List<int> remove = new List<int>();
        static Material beamMaterial, bodyMaterial;

        static Material Material(bool beam)
        {
            var current = beam ? beamMaterial : bodyMaterial;
            if (current != null) return current;
            var shader = Shader.Find(beam ? "Sprites/Default" : "Standard");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) return null;
            var material = new Material(shader) { color = beam ? new Color(.35f, .95f, 1f) : new Color(.2f, .24f, .28f) };
            if (!beam && material.HasProperty("_Metallic")) material.SetFloat("_Metallic", .6f);
            if (beam) beamMaterial = material; else bodyMaterial = material;
            return material;
        }

        public static Status GetStatus(int vehicleId)
        {
            Status status;
            if (!statuses.TryGetValue(vehicleId, out status)) { status = new Status(); statuses[vehicleId] = status; }
            return status;
        }

        public static void Receive(World world, int vehicleId, int id, byte kind, Vector3 a, Vector3 b, float value, float c)
        {
            if (world == null || world.GetPrimaryPlayer() == null) return;
            if (kind == Weapons.StatusEvent)
            {
                var status = GetStatus(vehicleId);
                status.Time = Time.time; status.Flags = id; status.Heat = b.x;
                status.HullFraction = Mathf.Clamp01(b.y);
                status.RepairProgress = Mathf.Clamp01(b.z);
                status.BattleRepairWait = Mathf.Max(0, c);
                status.BeamAmmo = Mathf.Max(0, Mathf.RoundToInt(a.x));
                status.MissileAmmo = Mathf.Max(0, Mathf.RoundToInt(a.y));
                status.MissileWait = Mathf.Max(0, a.z);
                status.MeleeMode = (id & 64) != 0;
                status.Direction = Vector3.zero;
                status.LockProgress = Mathf.Clamp01(value);
                return;
            }
            if (kind == Weapons.MeleeModeEvent)
            {
                // id carries the new mode; animate the blade deploy/stow.
                SetBladeMode(world, vehicleId, id != 0);
                var vehicle = world.GetEntity(vehicleId) as EntityVehicle;
                if (vehicle != null) Audio.Manager.Play(vehicle, id != 0 ? "electric_fence_on" : "electric_fence_off", 1, false);
                return;
            }
            if (kind == Weapons.MeleeSweepEvent || kind == Weapons.MeleeHeavyEvent)
            {
                meleeCooldownUntil[vehicleId] = Time.time + Mathf.Max(0, c);
                PlaySwing(world, vehicleId, kind == Weapons.MeleeHeavyEvent, a, b);
                var vehicle = world.GetEntity(vehicleId) as EntityVehicle;
                if (vehicle != null) Audio.Manager.Play(vehicle, "turret_fire", 1, false);
                return;
            }
            if (kind == Weapons.BeamEvent)
            {
                var tracer = pool.Count > 0 ? pool.Pop() : new Tracer();
                if (tracer.Line == null) tracer.Line = new GameObject("MechaBeam").AddComponent<LineRenderer>();
                tracer.Line.gameObject.SetActive(true);
                tracer.Line.sharedMaterial = Material(true);
                tracer.Line.startColor = new Color(.5f, 1f, 1f, .95f); tracer.Line.endColor = new Color(.1f, .6f, 1f, .2f);
                tracer.Line.startWidth = .12f; tracer.Line.endWidth = .03f; tracer.Line.positionCount = 2; tracer.Line.useWorldSpace = true;
                tracer.Line.SetPosition(0, a - Origin.position); tracer.Line.SetPosition(1, b - Origin.position);
                tracer.Line.shadowCastingMode = ShadowCastingMode.Off;
                tracer.A = a; tracer.B = b; tracer.Life = .09f; tracers.Add(tracer);
                var vehicle = world.GetEntity(vehicleId) as EntityVehicle;
                if (vehicle != null) Audio.Manager.Play(vehicle, "turret_fire", 1, false);
                return;
            }
            if (kind == Weapons.MissileSpawnEvent)
            {
                if (projectiles.ContainsKey(id)) return;
                var obj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                obj.name = "MechaMissile"; obj.hideFlags = HideFlags.DontSave;
                var collider = obj.GetComponent<Collider>();
                if (collider != null) { collider.enabled = false; UnityEngine.Object.Destroy(collider); }
                var renderer = obj.GetComponent<Renderer>();
                renderer.sharedMaterial = Material(false);
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                obj.transform.localScale = new Vector3(.09f, .38f, .09f);
                obj.transform.position = a - Origin.position;
                obj.transform.rotation = Quaternion.FromToRotation(Vector3.up, b.normalized);
                var trail = obj.AddComponent<TrailRenderer>();
                trail.sharedMaterial = Material(true);
                trail.time = .5f; trail.startWidth = .1f; trail.endWidth = .015f;
                trail.startColor = new Color(.4f, .9f, 1f); trail.endColor = new Color(.5f, .5f, .5f, 0);
                projectiles[id] = new Projectile { Object = obj, Position = a, Velocity = b, Life = Rules.MissileLifetime + .5f };
                var vehicle = world.GetEntity(vehicleId) as EntityVehicle;
                if (vehicle != null) Audio.Manager.Play(vehicle, "m136_fire", 1, false);
                return;
            }
            if (kind == Weapons.MissileMoveEvent)
            {
                Projectile projectile;
                if (projectiles.TryGetValue(id, out projectile)) { projectile.Position = a; projectile.Velocity = b; }
                return;
            }
            if (kind == Weapons.ImpactEvent)
            {
                Projectile projectile;
                if (projectiles.TryGetValue(id, out projectile))
                {
                    if (projectile.Object != null) UnityEngine.Object.Destroy(projectile.Object);
                    projectiles.Remove(id);
                }
            }
        }

        public static void Update(float dt)
        {
            remove.Clear();
            foreach (var pair in projectiles)
            {
                var projectile = pair.Value;
                projectile.Position += projectile.Velocity * Mathf.Min(Mathf.Max(0, dt), projectile.Life);
                projectile.Life -= dt;
                if (projectile.Life <= 0 || projectile.Object == null)
                { if (projectile.Object != null) UnityEngine.Object.Destroy(projectile.Object); remove.Add(pair.Key); }
                else
                {
                    projectile.Object.transform.position = projectile.Position - Origin.position;
                    if (projectile.Velocity.sqrMagnitude > .01f)
                        projectile.Object.transform.rotation = Quaternion.FromToRotation(Vector3.up, projectile.Velocity.normalized);
                }
            }
            foreach (int id in remove) projectiles.Remove(id);
            for (int i = tracers.Count - 1; i >= 0; i--)
            {
                var tracer = tracers[i]; tracer.Life -= dt;
                if (tracer.Life <= 0 || tracer.Line == null)
                {
                    if (tracer.Line != null)
                    {
                        if (pool.Count < 32) { tracer.Line.gameObject.SetActive(false); pool.Push(tracer); }
                        else UnityEngine.Object.Destroy(tracer.Line.gameObject);
                    }
                    tracers.RemoveAt(i);
                }
                else { tracer.Line.SetPosition(0, tracer.A - Origin.position); tracer.Line.SetPosition(1, tracer.B - Origin.position); }
            }
            var world = GameManager.Instance != null ? GameManager.Instance.World : null;
            if (world != null) UpdateBlades(world, dt);
        }

        // ---------------- energy blades ----------------

        sealed class Blade
        {
            public EntityVehicle Vehicle;
            public Transform Root, Edge;
            public TrailRenderer Trail;
            public Vector3 EdgeScale;
            public bool DeployTarget;
            public float DeployState;
            public float Charge;
            public int Swing; public float SwingStart;
        }
        static readonly Dictionary<int, Blade[]> blades = new Dictionary<int, Blade[]>();
        static readonly List<int> removeBlades = new List<int>();
        static Material bladeMaterial;

        static Material BladeMaterial()
        {
            if (bladeMaterial != null) return bladeMaterial;
            var shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) return null;
            bladeMaterial = new Material(shader) { name = "MechaBlade" };
            if (bladeMaterial.HasProperty("_EmissionColor"))
            { bladeMaterial.EnableKeyword("_EMISSION"); bladeMaterial.SetColor("_EmissionColor", new Color(.2f, .9f, 1f) * 1.5f); }
            if (bladeMaterial.HasProperty("_Metallic")) bladeMaterial.SetFloat("_Metallic", .8f);
            return bladeMaterial;
        }

        // Two blades mount under the upper panels; they exist only as runtime
        // geometry so the GLB stays untouched.
        static Blade[] GetBlades(World world, EntityVehicle vehicle)
        {
            Blade[] pair;
            if (blades.TryGetValue(vehicle.entityId, out pair) && pair[0].Root != null) return pair;
            if (pair != null) foreach (var blade in pair) if (blade.Root != null) UnityEngine.Object.Destroy(blade.Root.gameObject);
            var rig = Model.GetRig(vehicle);
            if (rig == null) return null;
            var material = BladeMaterial();
            pair = new Blade[2];
            for (int side = 0; side < 2; side++)
            {
                var mount = side == 0 ? rig.PanelL != null ? rig.PanelL : rig.Body : rig.PanelR != null ? rig.PanelR : rig.Body;
                if (mount == null) return null;
                var root = new GameObject("MechaBlade").transform;
                root.SetParent(mount, false);
                root.localPosition = new Vector3(side == 0 ? -.06f : .06f, -.02f, .1f);
                root.localRotation = Quaternion.Euler(52, 0, 0);
                root.gameObject.hideFlags = HideFlags.DontSave;
                var edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
                edge.name = "MechaBladeEdge"; edge.hideFlags = HideFlags.DontSave;
                var collider = edge.GetComponent<Collider>();
                if (collider != null) { collider.enabled = false; UnityEngine.Object.Destroy(collider); }
                var renderer = edge.GetComponent<Renderer>();
                if (material != null) renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                edge.transform.SetParent(root, false);
                edge.transform.localPosition = new Vector3(0, -.72f, 0);
                edge.transform.localScale = new Vector3(.05f, 1.4f, .1f);
                var tip = new GameObject("MechaBladeTip").transform;
                tip.SetParent(edge.transform, false);
                tip.localPosition = new Vector3(0, -.7f, 0);
                var trail = tip.gameObject.AddComponent<TrailRenderer>();
                trail.sharedMaterial = Material(true);
                trail.time = .22f; trail.startWidth = .09f; trail.endWidth = .005f;
                trail.startColor = new Color(.5f, 1f, 1f, .9f); trail.endColor = new Color(.2f, .7f, 1f, 0);
                trail.shadowCastingMode = ShadowCastingMode.Off;
                trail.enabled = false;
                pair[side] = new Blade { Vehicle = vehicle, Root = root, Edge = edge.transform, Trail = trail, EdgeScale = edge.transform.localScale };
                pair[side].Edge.localScale = new Vector3(pair[side].EdgeScale.x, .03f, pair[side].EdgeScale.z);
            }
            blades[vehicle.entityId] = pair;
            return pair;
        }

        public static void SetCharge(int vehicleId, float level)
        {
            Blade[] pair;
            if (!blades.TryGetValue(vehicleId, out pair)) return;
            foreach (var blade in pair) blade.Charge = level;
        }

        static void SetBladeMode(World world, int vehicleId, bool on)
        {
            var vehicle = world.GetEntity(vehicleId) as EntityVehicle;
            var pair = vehicle != null ? GetBlades(world, vehicle) : null;
            if (pair != null) foreach (var blade in pair) blade.DeployTarget = on;
        }

        static void PlaySwing(World world, int vehicleId, bool heavy, Vector3 origin, Vector3 forward)
        {
            var vehicle = world.GetEntity(vehicleId) as EntityVehicle;
            var pair = vehicle != null ? GetBlades(world, vehicle) : null;
            if (pair == null) return;
            for (int side = 0; side < pair.Length; side++)
            {
                pair[side].Swing = heavy ? 2 : 1;
                pair[side].SwingStart = Time.time;
                pair[side].DeployTarget = true;
                if (pair[side].Trail != null) pair[side].Trail.enabled = true;
            }
        }

        static void UpdateBlades(World world, float dt)
        {
            if (blades.Count == 0) return;
            removeBlades.Clear();
            foreach (var entry in blades)
            {
                var pair = entry.Value;
                var vehicle = world.GetEntity(entry.Key) as EntityVehicle;
                if (vehicle == null || pair[0].Vehicle != vehicle)
                { foreach (var blade in pair) if (blade.Root != null) UnityEngine.Object.Destroy(blade.Root.gameObject); removeBlades.Add(entry.Key); continue; }
                var status = GetStatus(entry.Key);
                for (int side = 0; side < pair.Length; side++)
                {
                    var blade = pair[side];
                    if (blade.Root == null) continue;
                    blade.DeployTarget = status.Time > -90 && status.MeleeMode;
                    blade.DeployState = Mathf.MoveTowards(blade.DeployState, blade.DeployTarget ? 1 : 0, dt / .3f);
                    if (blade.Edge != null)
                        blade.Edge.localScale = new Vector3(blade.EdgeScale.x, Mathf.Max(.03f, blade.EdgeScale.y * blade.DeployState), blade.EdgeScale.z);
                    var rotation = Quaternion.Euler(52, 0, 0);
                    float swingTime = .35f;
                    float t = blade.Swing != 0 ? (Time.time - blade.SwingStart) / swingTime : -1;
                    if (t >= 0 && t <= 1)
                    {
                        float ease = 1 - Mathf.Pow(1 - t, 3);
                        rotation *= blade.Swing == 1
                            ? Quaternion.Euler(0, (side == 0 ? 1f : -1f) * 175f * ease, 0)
                            : Quaternion.Euler(-95f * ease + 55f * ease * ease, 0, 0);
                    }
                    else if (blade.Swing != 0)
                    {
                        blade.Swing = 0;
                        if (blade.Trail != null) blade.Trail.enabled = false;
                    }
                    blade.Root.localRotation = rotation;
                    var material = bladeMaterial;
                    if (material != null && material.HasProperty("_EmissionColor"))
                        material.SetColor("_EmissionColor", new Color(.2f, .9f, 1f) * (1.5f + blade.Charge * 3.5f));
                }
            }
            foreach (int id in removeBlades) blades.Remove(id);
        }

        public static void Clear()
        {
            foreach (var projectile in projectiles.Values) if (projectile.Object != null) UnityEngine.Object.Destroy(projectile.Object);
            foreach (var tracer in tracers) if (tracer.Line != null) UnityEngine.Object.Destroy(tracer.Line.gameObject);
            foreach (var tracer in pool) if (tracer.Line != null) UnityEngine.Object.Destroy(tracer.Line.gameObject);
            foreach (var pair in blades.Values) foreach (var blade in pair)
                if (blade.Root != null) UnityEngine.Object.Destroy(blade.Root.gameObject);
            projectiles.Clear(); tracers.Clear(); pool.Clear(); statuses.Clear(); blades.Clear(); meleeCooldownUntil.Clear();
            if (beamMaterial != null) UnityEngine.Object.Destroy(beamMaterial);
            if (bodyMaterial != null) UnityEngine.Object.Destroy(bodyMaterial);
            if (bladeMaterial != null) UnityEngine.Object.Destroy(bladeMaterial);
            beamMaterial = bodyMaterial = bladeMaterial = null;
        }
    }
}
