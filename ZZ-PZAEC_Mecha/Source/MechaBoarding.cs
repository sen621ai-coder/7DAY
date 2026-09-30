using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace PZAEC.Mecha
{
    // Boarding ceremony: intercepts EnterVehicle/DetachEntity on the walker,
    // plays a four-phase joint-animated show (expand -> greet -> lift -> close)
    // with a mirrored dismount, cinematic camera ride and skip support. The
    // real attach happens inside the lift phase via a pass-through guard.
    public static class Boarding
    {
        public const byte BoardEvent = 8;

        sealed class Show
        {
            public EntityVehicle Vehicle;
            public int Actor;
            public float Started;
            public bool Dismount;
            public bool Released;
            public Vector3 CameraFrom; public Quaternion CameraFromRot; public float CameraFromFov;
        }
        static readonly Dictionary<int, Show> shows = new Dictionary<int, Show>();
        static readonly List<int> remove = new List<int>();
        static bool passThrough;
        static bool enabled;
        static bool liftCameraActive;
        static float liftAge;
        static Vector3 liftFrom, liftTo; static Quaternion liftFromRot, liftToRot; static float liftFromFov, liftToFov;
        static GameObject palmGlowL, palmGlowR, chestSeam;
        static Material glowMaterial;

        public static void Install(Harmony h)
        {
            try
            {
                h.Patch(AccessTools.Method(typeof(EntityVehicle), "EnterVehicle"),
                    prefix: new HarmonyMethod(typeof(Boarding), nameof(EnterGate)));
                h.Patch(AccessTools.Method(typeof(EntityVehicle), "DetachEntity"),
                    prefix: new HarmonyMethod(typeof(Boarding), nameof(ExitGate)));
                enabled = true;
                Log.Out("[Mecha] Boarding ceremony active (expand-greet-lift-close, mirrored dismount).");
            }
            catch (Exception ex) { enabled = false; Log.Warning("[Mecha] Boarding ceremony disabled: " + ex); }
        }

        public static bool Active(EntityVehicle v)
        { return enabled && v != null && shows.ContainsKey(v.entityId); }

        // HUD overlay query: overall progress 0-1, dismount flag, rider flag.
        public static bool Describe(EntityPlayerLocal player, out float progress, out bool dismount, out bool rider)
        {
            progress = 0f; dismount = false; rider = false;
            if (!enabled) return false;
            foreach (var pair in shows)
            {
                dismount = pair.Value.Dismount;
                rider = player != null && pair.Value.Actor == player.entityId;
                progress = Mathf.Clamp01((Time.time - pair.Value.Started) / Total(dismount));
                return true;
            }
            return false;
        }

        public static float Total(bool dismount)
        {
            return dismount
                ? Rules.DismountExpandSeconds + Rules.DismountPlaceSeconds + Rules.DismountRiseSeconds
                : Rules.BoardExpandSeconds + Rules.BoardGreetSeconds + Rules.BoardLiftSeconds + Rules.BoardCloseSeconds;
        }

        // ---------------- entry gates ----------------

        static bool EnterGate(EntityVehicle __instance, EntityAlive _entity)
        {
            if (passThrough || !enabled || !Rules.BoardingEnabled || !Weapons.IsMecha(__instance)) return true;
            var player = _entity as EntityPlayer;
            if (player == null || Active(__instance)) return !Active(__instance);
            if (__instance.GetAttached(0) != null) return true;
            var local = player as EntityPlayerLocal;
            Start(__instance, player.entityId, false, local != null && local.playerCamera != null ? local.playerCamera.transform : null);
            return false;
        }

        static bool ExitGate(EntityVehicle __instance, Entity _entity)
        {
            if (passThrough || !enabled || !Rules.BoardingEnabled || !Weapons.IsMecha(__instance)) return true;
            if (Active(__instance)) return false;
            var player = _entity as EntityPlayer;
            if (player == null || __instance.GetAttached(0) == null) return true;
            Start(__instance, player.entityId, true, null);
            return false;
        }

        static void Start(EntityVehicle vehicle, int actor, bool dismount, Transform cameraAnchor)
        {
            // Capture the rider's ground-level camera pose before the lift.
            var show = new Show { Vehicle = vehicle, Actor = actor, Started = Time.time, Dismount = dismount };
            if (cameraAnchor != null)
            {
                show.CameraFrom = cameraAnchor.position; show.CameraFromRot = cameraAnchor.rotation;
                show.CameraFromFov = 60f;
            }
            else show.CameraFromFov = -1f;
            shows[vehicle.entityId] = show;
            if (Weapons.Server)
                Weapons.Broadcast(vehicle.entityId, actor, BoardEvent,
                    new Vector3(dismount ? 1 : 0, 0, 0), Vector3.zero, 0, 0);
            Audio.Manager.Play(vehicle, "electric_fence_on", 1, false);
        }

        // Server: the skip intent fast-forwards the current show.
        public static void SkipRequest(World world, int actor, int vehicleId)
        {
            Show show;
            if (!shows.TryGetValue(vehicleId, out show) || show.Actor != actor) return;
            Release(world, show);
            show.Started = Mathf.Min(show.Started, Time.time - Total(show.Dismount) + .1f);
        }

        static void Release(World world, Show show)
        {
            if (show.Released) return;
            show.Released = true;
            if (!Weapons.Server) return;
            var vehicle = show.Vehicle;
            if (show.Dismount)
            {
                var rider = world.GetEntity(show.Actor) as EntityAlive;
                if (rider != null && vehicle != null && vehicle.GetAttached(0) == rider)
                { passThrough = true; try { vehicle.DetachEntity(rider); } finally { passThrough = false; } }
            }
            else
            {
                var rider = world.GetEntity(show.Actor) as EntityAlive;
                if (rider != null && vehicle != null && vehicle.GetAttached(0) == null)
                { passThrough = true; try { vehicle.EnterVehicle(rider); } finally { passThrough = false; } }
            }
        }

        public static void Update(World world)
        {
            if (!enabled) return;
            remove.Clear();
            foreach (var pair in shows)
            {
                var show = pair.Value;
                var vehicle = world.GetEntity(pair.Key) as EntityVehicle;
                if (vehicle == null || show.Vehicle != vehicle) { remove.Add(pair.Key); continue; }
                float age = Time.time - show.Started;
                float liftAt = show.Dismount ? Rules.DismountExpandSeconds : Rules.BoardExpandSeconds + Rules.BoardGreetSeconds;
                if (!show.Released && age >= liftAt)
                {
                    if (show.Dismount) BeginDismountCamera(show);
                    Release(world, show);
                }
                if (age >= Total(show.Dismount)) remove.Add(pair.Key);
            }
            foreach (int id in remove) shows.Remove(id);
            if (liftCameraActive) liftAge += Time.deltaTime;
        }

        // ---------------- local client hooks ----------------

        // Riders skip the show by pressing E or moving; checked from Weapons.
        public static void ClientSkipCheck(World world)
        {
            var player = world.GetPrimaryPlayer();
            if (player == null) return;
            foreach (var pair in shows)
            {
                var show = pair.Value;
                if (show.Actor != player.entityId || show.Released) continue;
                bool pressed = Input.GetKeyDown(KeyCode.E);
                bool moved = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D);
                if (!pressed && !moved) continue;
                if (Weapons.Server) SkipRequest(world, player.entityId, pair.Key);
                else
                {
                    int sequence = 0;
                    ConnectionManager.Instance.SendToServer(NetPackageManager.GetPackage<NetPackagePZAECMechaIntent>()
                        .Setup(pair.Key, 13, Vector3.forward, player.position, sequence));
                    show.Released = true; // optimistic local fast-forward
                }
                return;
            }
        }

        // Camera transition during lift/place phases; consumed by Optics.
        public static bool CameraRide(out Vector3 position, out Quaternion rotation, out float fov)
        {
            position = Vector3.zero; rotation = Quaternion.identity; fov = -1;
            if (!liftCameraActive) return false;
            float t = Mathf.Clamp01(liftAge / Rules.BoardLiftSeconds);
            float ease = t * t * (3f - 2f * t);
            position = Vector3.Lerp(liftFrom, liftTo, ease);
            rotation = Quaternion.Slerp(liftFromRot, liftToRot, ease);
            fov = Mathf.Lerp(liftFromFov, liftToFov, ease);
            if (t >= 1f) liftCameraActive = false;
            return true;
        }

        static void BeginLiftCamera(Show show, Model.Rig rig, EntityPlayerLocal player)
        {
            if (player == null || player.playerCamera == null || rig == null || rig.Head == null) return;
            liftFrom = show.CameraFromFov >= 0f ? show.CameraFrom : player.playerCamera.transform.position;
            liftFromRot = show.CameraFromFov >= 0f ? show.CameraFromRot : player.playerCamera.transform.rotation;
            liftFromFov = show.CameraFromFov >= 0f ? show.CameraFromFov : player.playerCamera.fieldOfView;
            liftTo = rig.Head.position + rig.Head.forward * .18f;
            liftToRot = Quaternion.LookRotation(ProjectOnPlaneForward(rig), Vector3.up);
            liftToFov = player.GetCameraFOV();
            liftAge = 0f;
            liftCameraActive = true;
        }

        static void BeginDismountCamera(Show show)
        {
            var player = GameManager.Instance != null && GameManager.Instance.World != null
                ? GameManager.Instance.World.GetPrimaryPlayer() : null;
            var rig = show.Vehicle != null ? Model.GetRig(show.Vehicle) : null;
            if (player == null || player.playerCamera == null || rig == null || rig.Head == null) return;
            if (player.entityId != show.Actor) return;
            liftFrom = player.playerCamera.transform.position;
            liftFromRot = player.playerCamera.transform.rotation;
            liftFromFov = player.playerCamera.fieldOfView;
            var drop = show.Vehicle.position + show.Vehicle.transform.forward * 2.4f + Vector3.up * 1.6f;
            liftTo = drop;
            liftToRot = Quaternion.LookRotation(show.Vehicle.transform.forward, Vector3.up);
            liftToFov = player.GetCameraFOV();
            liftAge = 0f;
            liftCameraActive = true;
        }

        static Vector3 ProjectOnPlaneForward(Model.Rig rig)
        {
            var forward = rig.Head.forward;
            forward.y = 0f;
            return forward.sqrMagnitude < .001f ? Vector3.forward : forward.normalized;
        }

        // ---------------- pose timeline (Gait yields here) ----------------

        struct Pose
        {
            public float Knee, TorsoPitch, ArmRoll, ArmPitchR, ArmPitchL, TorsoDrop;
        }

        static Pose Lerp(Pose a, Pose b, float t)
        {
            return new Pose
            {
                Knee = Mathf.Lerp(a.Knee, b.Knee, t),
                TorsoPitch = Mathf.Lerp(a.TorsoPitch, b.TorsoPitch, t),
                ArmRoll = Mathf.Lerp(a.ArmRoll, b.ArmRoll, t),
                ArmPitchR = Mathf.Lerp(a.ArmPitchR, b.ArmPitchR, t),
                ArmPitchL = Mathf.Lerp(a.ArmPitchL, b.ArmPitchL, t),
                TorsoDrop = Mathf.Lerp(a.TorsoDrop, b.TorsoDrop, t)
            };
        }

        static readonly Pose StandPose = new Pose { Knee = 6f, TorsoPitch = 0f, ArmRoll = 0f, ArmPitchR = 0f, ArmPitchL = 0f, TorsoDrop = 0f };
        static readonly Pose ExpandPose = new Pose { Knee = 25f, TorsoPitch = 12f, ArmRoll = 60f, ArmPitchR = 0f, ArmPitchL = 0f, TorsoDrop = .22f };
        static readonly Pose GreetPose = new Pose { Knee = 28f, TorsoPitch = 18f, ArmRoll = 60f, ArmPitchR = 42f, ArmPitchL = 8f, TorsoDrop = .26f };
        static readonly Pose LiftPose = new Pose { Knee = 16f, TorsoPitch = 10f, ArmRoll = 35f, ArmPitchR = 18f, ArmPitchL = 4f, TorsoDrop = .12f };

        // Drives joint rotations for the active show; called by Gait at its head.
        public static bool ApplyPose(Model.Rig rig, EntityVehicle vehicle)
        {
            if (!enabled) return false;
            Show show;
            if (rig == null || !shows.TryGetValue(vehicle.entityId, out show)) return false;
            float age = Time.time - show.Started;
            Pose pose;
            float e = Rules.BoardExpandSeconds, g = Rules.BoardGreetSeconds, l = Rules.BoardLiftSeconds;
            if (show.Dismount)
            {
                if (age < Rules.DismountExpandSeconds)
                    pose = Lerp(StandPose, ExpandPose, Mathf.Clamp01(age / Rules.DismountExpandSeconds));
                else if (age < Rules.DismountExpandSeconds + Rules.DismountPlaceSeconds)
                    pose = ExpandPose;
                else
                    pose = Lerp(ExpandPose, StandPose, Mathf.Clamp01((age - Rules.DismountExpandSeconds - Rules.DismountPlaceSeconds) / Rules.DismountRiseSeconds));
            }
            else
            {
                if (age < e) pose = Lerp(StandPose, ExpandPose, Mathf.Clamp01(age / e));
                else if (age < e + g) pose = Lerp(ExpandPose, GreetPose, Mathf.Clamp01((age - e) / g));
                else if (age < e + g + l) pose = Lerp(GreetPose, LiftPose, Mathf.Clamp01((age - e - g) / l));
                else pose = Lerp(LiftPose, StandPose, Mathf.Clamp01((age - e - g - l) / Rules.BoardCloseSeconds));
            }
            Apply(rig, pose);
            UpdateFx(rig, show, age);
            return true;
        }

        static void Apply(Model.Rig rig, Pose pose)
        {
            float knee = pose.Knee;
            float hip = -knee * .55f;
            if (rig.HipL != null) rig.HipL.localRotation = Quaternion.Euler(hip, 0, 0);
            if (rig.HipR != null) rig.HipR.localRotation = Quaternion.Euler(hip, 0, 0);
            if (rig.KneeL != null) rig.KneeL.localRotation = Quaternion.Euler(knee, 0, 0);
            if (rig.KneeR != null) rig.KneeR.localRotation = Quaternion.Euler(knee, 0, 0);
            if (rig.Torso != null)
            {
                rig.Torso.localRotation = Quaternion.Euler(pose.TorsoPitch, 0, 0);
                rig.Torso.localPosition = new Vector3(0, -pose.TorsoDrop, 0);
            }
            if (rig.ShoulderL != null) rig.ShoulderL.localRotation = Quaternion.Euler(pose.ArmPitchL, 0, -pose.ArmRoll);
            if (rig.ShoulderR != null) rig.ShoulderR.localRotation = Quaternion.Euler(pose.ArmPitchR, 0, pose.ArmRoll);
        }

        // ---------------- ceremony FX (palm glow, chest seam flash) ----------------

        static GameObject Glow(string name, Transform parent, Vector3 localPosition, Vector3 scale)
        {
            if (glowMaterial == null)
            {
                var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
                if (shader == null) return null;
                glowMaterial = new Material(shader) { name = "MechaBoardGlow", color = new Color(.4f, .95f, 1f, .85f) };
            }
            var obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            obj.name = name; obj.hideFlags = HideFlags.DontSave;
            var collider = obj.GetComponent<Collider>();
            if (collider != null) { collider.enabled = false; UnityEngine.Object.Destroy(collider); }
            var renderer = obj.GetComponent<Renderer>();
            renderer.sharedMaterial = glowMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = localPosition;
            obj.transform.localScale = scale;
            return obj;
        }

        static void UpdateFx(Model.Rig rig, Show show, float age)
        {
            float e = Rules.BoardExpandSeconds, g = Rules.BoardGreetSeconds, l = Rules.BoardLiftSeconds;
            bool palms;
            bool seam;
            if (show.Dismount)
            {
                palms = age < Rules.DismountExpandSeconds + Rules.DismountPlaceSeconds;
                seam = false;
            }
            else
            {
                palms = age < e + g + l * .5f;
                seam = age >= e + g + l && age < e + g + l + .3f;
            }
            if (palms && palmGlowL == null && rig.HandL != null && rig.HandR != null)
            {
                palmGlowL = Glow("MechaPalmGlowL", rig.HandL, new Vector3(0, -.08f, .04f), new Vector3(.22f, .22f, .22f));
                palmGlowR = Glow("MechaPalmGlowR", rig.HandR, new Vector3(0, -.08f, .04f), new Vector3(.22f, .22f, .22f));
            }
            if (!palms)
            {
                if (palmGlowL != null) { UnityEngine.Object.Destroy(palmGlowL); palmGlowL = null; }
                if (palmGlowR != null) { UnityEngine.Object.Destroy(palmGlowR); palmGlowR = null; }
            }
            if (seam && chestSeam == null && rig.Torso != null)
                chestSeam = Glow("MechaChestSeam", rig.Torso, new Vector3(0, .55f, .12f), new Vector3(.5f, .04f, .03f));
            if (!seam && chestSeam != null) { UnityEngine.Object.Destroy(chestSeam); chestSeam = null; }
            // The rider's camera ride starts at lift time, once attached.
            if (!show.Dismount && rig != null)
            {
                var player = GameManager.Instance != null && GameManager.Instance.World != null ? GameManager.Instance.World.GetPrimaryPlayer() : null;
                var local = player as EntityPlayerLocal;
                if (local != null && local.entityId == show.Actor && local.AttachedToEntity == show.Vehicle &&
                    local.playerCamera != null && !liftCameraActive &&
                    age >= e + g && age < e + g + l && show.CameraFromFov >= 0f)
                    BeginLiftCamera(show, rig, local);
            }
        }

        // Event receiver: remote spectators replay the show locally; the
        // rider's own show was already created by the entry gate.
        public static void Receive(World world, int vehicleId, int actor, float mode)
        {
            if (!enabled || shows.ContainsKey(vehicleId)) return;
            var vehicle = world.GetEntity(vehicleId) as EntityVehicle;
            if (vehicle == null) return;
            var local = world.GetPrimaryPlayer();
            if (local != null && local.entityId == actor) return;
            shows[vehicleId] = new Show { Vehicle = vehicle, Actor = actor, Started = Time.time, Dismount = mode > .5f, CameraFromFov = -1f };
        }

        public static void Clear()
        {
            shows.Clear(); remove.Clear(); liftCameraActive = false;
            if (palmGlowL != null) UnityEngine.Object.Destroy(palmGlowL);
            if (palmGlowR != null) UnityEngine.Object.Destroy(palmGlowR);
            if (chestSeam != null) UnityEngine.Object.Destroy(chestSeam);
            palmGlowL = palmGlowR = chestSeam = null;
            if (glowMaterial != null) UnityEngine.Object.Destroy(glowMaterial);
            glowMaterial = null;
        }
    }
}
