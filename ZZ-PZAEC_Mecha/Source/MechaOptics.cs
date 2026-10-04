using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace PZAEC.Mecha
{
    // First-person walker view: the camera rides the drone eye mount while the
    // local pilot is seated. Mouse axes accumulate an independent yaw/pitch in
    // world space, so hull rotation never shakes the aim. Rendering never
    // supplies fire authority; the server re-validates every intent ray.
    public static class Optics
    {
        static EntityVehicle vehicle;
        static Transform eye, lens;
        static float yaw, pitch;
        static bool zoom;
        static int zoomStep;
        static readonly Dictionary<Renderer, bool> hidden = new Dictionary<Renderer, bool>();
        static Camera applied;
        static Vector3 savedPosition, lastPosition;
        static Quaternion savedRotation, lastRotation;
        static float savedFov, lastFov;

        public static bool Active(EntityVehicle v) { return vehicle == v && eye != null; }

        public static void Install(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(GameManager), "Update"), prefix: new HarmonyMethod(typeof(Optics), nameof(RestoreCamera)));
            // The player's own LateUpdate can rewrite FOV after vp_FPCamera;
            // applying on pre-cull is the only slot that always sticks.
            Camera.onPreCull += BeforeRender;
            Camera.onPostRender += AfterRender;
        }

        static float Magnification() { return zoom ? (zoomStep == 0 ? 2f : 4f) : 1f; }

        public static float Fov(float normal, float magnification)
        { return 2 * Mathf.Atan(Mathf.Tan(normal * Mathf.Deg2Rad * .5f) / magnification) * Mathf.Rad2Deg; }

        public static Quaternion Advance(ref float yawAngle, ref float pitchAngle, float dx, float dy, float magnification)
        {
            yawAngle = Mathf.Repeat(yawAngle + dx * 2f / magnification, 360f);
            pitchAngle = Mathf.Clamp(pitchAngle + dy * 2f / magnification, -85f, 85f);
            return Quaternion.Euler(-pitchAngle, yawAngle, 0);
        }

        public static void UpdateInput(EntityPlayerLocal player, EntityVehicle v)
        {
            var rig = Model.GetRig(v);
            if (rig == null || rig.Head == null) return;
            if (vehicle != v || eye == null)
            {
                Clear();
                vehicle = v; eye = rig.Head; lens = rig.Head;
                var forward = Weapons.BodyRotation(v) * Vector3.forward;
                yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
                pitch = 8f;
            }
            zoom = Input.GetKey(KeyCode.Mouse1);
            if (zoom && Input.GetKeyDown(KeyCode.Z)) zoomStep = (zoomStep + 1) % 2;
            var look = Advance(ref yaw, ref pitch, Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"), Magnification());
            StoredLook = look;
        }

        static Quaternion StoredLook = Quaternion.identity;

        public static bool TryRay(out Ray ray)
        {
            ray = default(Ray);
            if (vehicle == null || eye == null) return false;
            ray = new Ray(eye.position + Origin.position, StoredLook * Vector3.forward);
            return true;
        }

        public static void RestoreCamera()
        {
            if (applied != null)
            {
                var t = applied.transform;
                if ((t.position - lastPosition).sqrMagnitude < .00001f) t.position = savedPosition;
                if (Quaternion.Angle(t.rotation, lastRotation) < .01f) t.rotation = savedRotation;
                if (Mathf.Abs(applied.fieldOfView - lastFov) < .01f) applied.fieldOfView = savedFov;
            }
            applied = null;
        }

        static void Visibility(bool hide)
        {
            if (!hide)
            {
                foreach (var pair in hidden) if (pair.Key != null) pair.Key.forceRenderingOff = pair.Value;
                hidden.Clear(); return;
            }
            // Electronic cockpit feed: hide only this pilot's own visual model.
            // Restore after this camera so observers and other cameras retain it.
            var rig=vehicle!=null?Model.GetRig(vehicle):null;
            if(rig!=null)Hide(rig.Visual);
        }

        static void Hide(Transform root)
        {
            if (root == null) return;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || hidden.ContainsKey(renderer)) continue;
                hidden.Add(renderer, renderer.forceRenderingOff);
                renderer.forceRenderingOff = true;
            }
        }

        static void BeforeRender(Camera camera)
        {
            var player = GameManager.Instance != null && GameManager.Instance.World != null ? GameManager.Instance.World.GetPrimaryPlayer() : null;
            if (player == null || camera != player.playerCamera) return;
            // The boarding camera ride takes precedence over the seated FPV pose.
            Vector3 ride; Quaternion rideRot; float rideFov;
            if (Boarding.CameraRide(out ride, out rideRot, out rideFov))
            {
                Visibility(Boarding.CockpitCamera(vehicle));
                RestoreCamera();
                applied = camera;
                savedPosition = camera.transform.position; savedRotation = camera.transform.rotation; savedFov = camera.fieldOfView;
                lastPosition=ride; lastRotation=rideRot; lastFov=rideFov;
                camera.transform.SetPositionAndRotation(ride, rideRot);
                camera.fieldOfView = rideFov;
                return;
            }
            if (vehicle == null || player.AttachedToEntity != vehicle || eye == null ||
                !Weapons.UIReady(player) || GameManager.Instance.IsPaused())
            { RestoreCamera(); Visibility(false); return; }
            Visibility(true);
            RestoreCamera();
            applied = camera;
            savedPosition = camera.transform.position; savedRotation = camera.transform.rotation; savedFov = camera.fieldOfView;
            // eye.position is Unity-space; the camera lives under the same
            // world root, so no Origin offset is applied to camera placement.
            var position = eye.position + StoredLook * Vector3.forward * .18f;
            lastPosition = position; lastRotation = StoredLook; lastFov = Fov(player.GetCameraFOV(), Magnification());
            camera.transform.SetPositionAndRotation(position, StoredLook);
            camera.fieldOfView = lastFov;
        }

        static void AfterRender(Camera camera){if(camera==applied)Visibility(false);}

        public static void Clear()
        {
            RestoreCamera();
            Visibility(false);
            vehicle = null; eye = lens = null; zoom = false; zoomStep = 0;
        }
    }

    // Seated pilots ride inside the walker shell; suppress their presentation
    // on every client exactly like the closed-cabin M1 crew handling.
    public static class CrewVisibility
    {
        static readonly Dictionary<Renderer, bool> original = new Dictionary<Renderer, bool>();
        static readonly HashSet<Renderer> current = new HashSet<Renderer>();
        static readonly List<Renderer> removed = new List<Renderer>();

        public static void Update(World world)
        {
            current.Clear();
            foreach (var entity in world.Entities.list)
            {
                var mecha = entity as EntityVehicle;
                if (!Weapons.IsMecha(mecha)) continue;
                var occupant = mecha.GetAttached(0) as EntityPlayer;
                if (occupant == null) continue;
                foreach (var renderer in occupant.transform.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null) continue;
                    current.Add(renderer);
                    if (!original.ContainsKey(renderer)) original.Add(renderer, renderer.forceRenderingOff);
                    renderer.forceRenderingOff = true;
                }
            }
            removed.Clear();
            foreach (var pair in original)
                if (!current.Contains(pair.Key))
                {
                    if (pair.Key != null) pair.Key.forceRenderingOff = pair.Value;
                    removed.Add(pair.Key);
                }
            foreach (var renderer in removed) original.Remove(renderer);
        }

        public static void Clear()
        {
            foreach (var pair in original) if (pair.Key != null) pair.Key.forceRenderingOff = pair.Value;
            original.Clear(); current.Clear(); removed.Clear();
        }
    }
}
