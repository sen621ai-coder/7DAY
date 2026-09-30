using UnityEngine;

namespace PZAEC.Mecha
{
    // Walker HUD: centre reticle, heat bar, ammo, missile lock progress and
    // turbo jump charge. All strings follow the M1 convention (hard-coded).
    public static class MechaHUD
    {
        static GUIStyle label, bold;
        static int outsideFrame = -1;
        static EntityVehicle outsideTarget;

        public static void Draw(EntityPlayerLocal __instance)
        {
            var player = __instance;
            if (player == null || player != GameManager.Instance?.World?.GetPrimaryPlayer() || player.IsDead()) return;
            if (!GameManager.Instance.GameIsFocused) return;
            var ui = LocalPlayerUI.GetUIForPlayer(player);
            if (ui != null && (LocalPlayerUI.AnyModalWindowOpen() || ui.windowManager.IsCursorWindowOpen() || ui.windowManager.IsInputActive())) return;
            var vehicle = player.AttachedToEntity as EntityVehicle;
            if (!Weapons.IsMecha(vehicle)) { DrawOutside(player); return; }
            var attached = vehicle.GetAttached(0);
            if (attached == null || attached.entityId != player.entityId) return;
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { fontSize = 13 };
                bold = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            }
            var status = MechaFX.GetStatus(vehicle.entityId);
            bool fresh = Time.time - status.Time < 1f;
            var old = GUI.color;
            try
            {
                float cx = Screen.width * .5f, cy = Screen.height * .5f;
                bool overheated = (status.Flags & 1) != 0;
                var reticle = overheated ? new Color(1f, .45f, .2f, .95f) : fresh && status.BeamAmmo <= 0 ? new Color(1f, .8f, .3f, .95f) : new Color(.45f, 1f, .9f, .95f);
                GUI.color = reticle;
                Rect(cx - 16, cy, 11, 1); Rect(cx + 5, cy, 11, 1); Rect(cx, cy - 16, 1, 11); Rect(cx, cy + 5, 1, 11);
                Rect(cx - 3, cy - 3, 2, 2);
                var jump = Locomotion.GetJumpState(vehicle);
                float left = Screen.width - 236, top = Screen.height * .5f - 130;
                GUI.color = new Color(.1f, .14f, .16f, .78f);
                Rect(left - 10, top - 8, 250, 288);
                GUI.color = new Color(.55f, 1f, .95f, .95f);
                GUI.Label(new Rect(left, top, 210, 24), "Buster 战斗步行机", bold);
                bool melee = Weapons.MeleeModeLocal;
                GUI.color = melee ? new Color(1f, .8f, .3f) : new Color(.6f, .85f, 1f);
                GUI.Label(new Rect(left, top + 16, 210, 20), melee ? "近战模式 · 能量刃展开" : "远程模式 · 光束/导弹");
                // Hull integrity bar: server snapshot, red while recently hit.
                bool hurt = (status.Flags & 32) != 0;
                float hull = fresh ? status.HullFraction : 1f;
                GUI.color = new Color(.15f, .18f, .2f); Rect(left, top + 30, 200, 10);
                GUI.color = hurt ? new Color(1f, .35f, .3f) : Color.Lerp(new Color(1f, .5f, .3f), new Color(.4f, .95f, .6f), hull);
                Rect(left, top + 30, 200 * hull, 10);
                GUI.color = new Color(.75f, .9f, .95f);
                GUI.Label(new Rect(left, top + 44, 210, 20), hurt ? "装甲受创 · 维修冷却中" : "机体完整 " + Mathf.RoundToInt(hull * 100) + "%");
                float heat = Mathf.Clamp01(status.Heat / 100f);
                GUI.color = new Color(.15f, .18f, .2f); Rect(left, top + 66, 200, 10);
                GUI.color = overheated ? new Color(1f, .4f, .2f) : new Color(.3f, .9f, 1f); Rect(left, top + 66, 200 * heat, 10);
                GUI.color = new Color(.75f, .9f, .95f);
                GUI.Label(new Rect(left, top + 80, 210, 20), "光束温度 " + (overheated ? "过热！" : Mathf.RoundToInt(status.Heat).ToString()));
                GUI.Label(new Rect(left, top + 102, 210, 20), "能量电池 " + (fresh ? status.BeamAmmo.ToString() : "--"));
                GUI.Label(new Rect(left, top + 124, 210, 20), "制导导弹 " + (fresh ? status.MissileAmmo.ToString() : "--"));
                if (fresh && status.MissileWait > 0)
                    GUI.Label(new Rect(left, top + 146, 210, 20), "导弹装填 " + status.MissileWait.ToString("0.0") + "s");
                else if (fresh && status.LockProgress > 0 && status.LockProgress < 1)
                {
                    GUI.color = new Color(1f, .75f, .3f); Rect(left, top + 148, 200 * status.LockProgress, 6);
                    GUI.Label(new Rect(left, top + 156, 210, 20), "锁定中 " + Mathf.RoundToInt(status.LockProgress * 100) + "%");
                }
                else if (fresh && (status.Flags & 16) != 0)
                { GUI.color = new Color(.5f, 1f, .6f); GUI.Label(new Rect(left, top + 146, 210, 20), "已发射，松开后可再次锁定"); }
                GUI.color = new Color(.75f, .9f, .95f);
                string jumpText = jump.Ready ? "推进器 就绪" : "推进器 充能 " + Mathf.CeilToInt(jump.CooldownRemaining) + "s";
                GUI.Label(new Rect(left, top + 168, 210, 20), jumpText + (jump.Active ? " · 喷射中" : ""));
                if (melee)
                {
                    float meleeWait = MechaFX.MeleeCooldownRemaining(vehicle.entityId);
                    GUI.color = meleeWait <= 0 ? new Color(.5f, 1f, .6f) : new Color(.85f, .7f, .55f);
                    GUI.Label(new Rect(left, top + 188, 210, 20), meleeWait <= 0 ? "能量刃 就绪（左键横扫/长按重斩）" : "能量刃 冷却 " + meleeWait.ToString("0.0") + "s");
                }
                else
                {
                    bool repairReady = status.BattleRepairWait <= 0;
                    GUI.color = repairReady ? new Color(.5f, 1f, .6f) : new Color(.85f, .7f, .55f);
                    GUI.Label(new Rect(left, top + 188, 210, 20), repairReady ? "紧急维修 就绪（货箱维修包）" : "紧急维修 充能 " + Mathf.CeilToInt(status.BattleRepairWait) + "s");
                }
                if (Weapons.MeleeCharge > 0)
                {
                    float cy0 = Screen.height * .5f + 70;
                    GUI.color = new Color(.15f, .18f, .2f); Rect(Screen.width * .5f - 90, cy0, 180, 8);
                    GUI.color = Weapons.MeleeCharge >= 1f ? new Color(1f, .5f, .25f) : new Color(.35f, .9f, 1f);
                    Rect(Screen.width * .5f - 90, cy0, 180 * Weapons.MeleeCharge, 8);
                    if (Weapons.MeleeCharge >= 1f)
                    { GUI.color = new Color(1f, .8f, .4f); GUI.Label(new Rect(Screen.width * .5f - 90, cy0 + 10, 180, 20), "重斩蓄力完成 · 松开发射", label); }
                }
                GUI.color = new Color(.75f, .9f, .95f);
                GUI.Label(new Rect(left, top + 210, 240, 60), (melee ? "[左键]横扫/长按重斩  [X]收刃" : "[左键]光束  [X]拔刃  [R]紧急维修") + "\n[G]制导导弹  [右键]瞄准镜  [Z]变倍  [空格]喷射\n停机维修：车外对准机甲按住 F 8 秒");
            }
            finally { GUI.color = old; }
        }

        // Aiming at a parked walker from outside shows its state and the
        // repair prompt. The aim ray is sampled once per rendered frame.
        static void DrawOutside(EntityPlayerLocal player)
        {
            var world = GameManager.Instance.World;
            if (outsideFrame != Time.frameCount)
            {
                outsideFrame = Time.frameCount;
                outsideTarget = null;
                var ray = player.GetLookRay();
                if (Voxel.Raycast(world, ray, Rules.RepairRange, -538750997, 8, 0f))
                {
                    var candidate = ItemActionAttack.FindHitEntity(Voxel.voxelRayHitInfo) as EntityVehicle;
                    if (Weapons.IsMecha(candidate)) outsideTarget = candidate;
                }
            }
            var target = outsideTarget;
            if (target == null) return;
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { fontSize = 13 };
                bold = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            }
            var status = MechaFX.GetStatus(target.entityId);
            bool fresh = Time.time - status.Time < 2f;
            bool hurt = (status.Flags & 32) != 0;
            float hull = fresh ? status.HullFraction : 1f;
            var old = GUI.color;
            try
            {
                float cx = Screen.width * .5f, cy = Screen.height * .5f;
                GUI.color = new Color(.1f, .14f, .16f, .78f);
                Rect(cx - 110, cy + 36, 230, fresh && status.RepairProgress > 0 ? 104 : 86);
                GUI.color = new Color(.55f, 1f, .95f, .95f);
                GUI.Label(new Rect(cx - 98, cy + 42, 210, 22), "Buster 战斗步行机", bold);
                GUI.color = new Color(.15f, .18f, .2f); Rect(cx - 98, cy + 66, 196, 8);
                GUI.color = hurt ? new Color(1f, .35f, .3f) : Color.Lerp(new Color(1f, .5f, .3f), new Color(.4f, .95f, .6f), hull);
                Rect(cx - 98, cy + 66, 196 * hull, 8);
                GUI.color = new Color(.8f, .92f, .95f);
                if (!fresh)
                    GUI.Label(new Rect(cx - 98, cy + 78, 210, 20), "机体完整 --");
                else if (hurt)
                    GUI.Label(new Rect(cx - 98, cy + 78, 210, 20), "装甲受创 · 冷却后可维修");
                else if (status.RepairProgress > 0)
                {
                    GUI.color = new Color(1f, .75f, .3f); Rect(cx - 98, cy + 96, 196 * status.RepairProgress, 6);
                    GUI.Label(new Rect(cx - 98, cy + 104, 210, 20), "维修中 " + Mathf.RoundToInt(status.RepairProgress * 100) + "%");
                }
                else if (hull < 1f)
                    GUI.Label(new Rect(cx - 98, cy + 78, 210, 20), "货箱有维修包时按住 [F] 维修");
                else
                    GUI.Label(new Rect(cx - 98, cy + 78, 210, 20), "机体完好");
            }
            finally { GUI.color = old; }
        }

        static void Rect(float x, float y, float w, float h) { GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture); }
    }
}
