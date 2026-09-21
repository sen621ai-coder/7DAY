using UnityEngine;

namespace AECT16RuntimeFix
{
    // Client-side floating sight shared by pilot rockets, guided lock, gunner cannon
    // and HUD. The server still validates the resulting world ray and weapon arcs.
    public static class ApacheAimCursor
    {
        private static readonly Vector2 DefaultPoint = new Vector2(.5f, .64f);
        private static Vector2 point = DefaultPoint;
        private static int vehicleId = -1, seat = -1;

        public static Vector2 Point { get { return point; } }

        public static Vector2 Advance(Vector2 current, float mouseX, float mouseY)
        {
            current.x = Mathf.Clamp(current.x + mouseX * .014f, .16f, .84f);
            current.y = Mathf.Clamp(current.y + mouseY * .014f, .36f, .84f);
            return current;
        }

        public static void Update(EntityVehicle vehicle, int currentSeat)
        {
            if (vehicle == null || vehicle.entityId != vehicleId || currentSeat != seat)
            {
                vehicleId = vehicle != null ? vehicle.entityId : -1;
                seat = currentSeat;
                point = DefaultPoint;
            }
            if (vehicle == null || currentSeat < 0) return;
            point = Advance(point, Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
        }

        public static Ray Ray(EntityPlayerLocal player)
        {
            if (player?.playerCamera == null) return player != null ? player.GetLookRay() : new Ray();
            var ray = ApacheFiringFeedback.StabilizeRay(player.playerCamera,
                player.playerCamera.ViewportPointToRay(new Vector3(point.x, point.y, 0)));
            ray.origin += Origin.position;
            return ray;
        }

        // Coordinates inside the centered 1280x720 HUD matrix.
        public static Vector2 CanvasPoint()
        {
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            float left = (Screen.width - 1280 * scale) * .5f;
            float top = (Screen.height - 720 * scale) * .5f;
            return new Vector2(
                (point.x * Screen.width - left) / scale,
                ((1 - point.y) * Screen.height - top) / scale);
        }

        public static void Clear()
        {
            vehicleId = seat = -1;
            point = DefaultPoint;
        }
    }
}
