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
            if(label==null){label=new GUIStyle(GUI.skin.label){fontSize=13};bold=new GUIStyle(GUI.skin.label){fontSize=15,fontStyle=FontStyle.Bold};}
            DrawBoardingOverlay(player);
            if(Boarding.CurrentNotice!=null)GUI.Label(new Rect(Screen.width*.5f-320,Screen.height*.7f,680,40),Boarding.CurrentNotice);
            if (!Weapons.IsMecha(vehicle)) { DrawOutside(player); return; }
            var attached = vehicle.GetAttached(0);
            if (attached == null || attached.entityId != player.entityId) return;
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { fontSize = 13 };
                bold = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            }
                var status = MechaFX.GetStatus(vehicle.entityId);
                var motion=Locomotion.Get(vehicle);bool complete=Rules.Complete(vehicle);
                bool fresh = Time.time - status.Time < 1f;
                var old = GUI.color;
                try
                {
                float cx = Screen.width * .5f, cy = Screen.height * .5f;
                if(!Boarding.Active(vehicle))
                {
                    GUI.color=new Color(.04f,.09f,.11f,.7f);Rect(cx-200,70,400,48);
                    GUI.color=new Color(.5f,1f,.9f,.95f);
                    GUI.Label(new Rect(cx-188,72,380,22),Rules.DisplayName(vehicle)+(Optics.ThirdPerson?" / 第三人称":" / 第一视角")+"  [·]切换",bold);
                    float speed=vehicle.vehicleRB!=null?Vector3.ProjectOnPlane(vehicle.vehicleRB.velocity,Vector3.up).magnitude:0;
                    GUI.Label(new Rect(cx-188,96,380,20),string.Format("速度 {0:0.0} m/s    {1}    舱内冲击防护在线",speed,complete?Flight.Status(motion):Locomotion.HoverOn?"悬浮":"地面 / 推进"),label);
                    GUI.color=new Color(.35f,.9f,.85f,.6f);
                    Rect(32,130,45,2);Rect(32,130,2,45);Rect(Screen.width-77,130,45,2);Rect(Screen.width-34,130,2,45);
                }
                DrawDirection(vehicle,cx,cy);
                bool overheated = (status.Flags & 1) != 0;
                var reticle = overheated ? new Color(1f, .45f, .2f, .95f) : fresh && status.BeamAmmo <= 0 ? new Color(1f, .8f, .3f, .95f) : new Color(.45f, 1f, .9f, .95f);
                GUI.color = reticle;
                Rect(cx - 16, cy, 11, 1); Rect(cx + 5, cy, 11, 1); Rect(cx, cy - 16, 1, 11); Rect(cx, cy + 5, 1, 11);
                Rect(cx - 3, cy - 3, 2, 2);
                float left = Screen.width - 236, top = Screen.height * .5f - 130;
                GUI.color = new Color(.1f, .14f, .16f, .78f);
                Rect(left - 10, top - 8, 250, 366);
                GUI.color = new Color(.55f, 1f, .95f, .95f);
                GUI.Label(new Rect(left, top, 210, 24), Rules.DisplayName(vehicle), bold);
                GUI.color = new Color(.6f, .85f, 1f);
                GUI.Label(new Rect(left, top + 16, 210, 20), Rules.Complete(vehicle)?"剑盾近战 · 额头辅助炮":"远程火控 · 光束 / 制导导弹");
                // Hull integrity bar: server snapshot, red while recently hit.
                bool hurt = (status.Flags & 32) != 0;
                float hull = fresh ? status.HullFraction : 1f;
                GUI.color = new Color(.15f, .18f, .2f); Rect(left, top + 30, 200, 10);
                GUI.color = hurt ? new Color(1f, .35f, .3f) : Color.Lerp(new Color(1f, .5f, .3f), new Color(.4f, .95f, .6f), hull);
                Rect(left, top + 30, 200 * hull, 10);
                GUI.color = new Color(.75f, .9f, .95f);
                GUI.Label(new Rect(left, top + 44, 210, 20), hurt ? "装甲受创 · 维修冷却中" : "机体完整 " + Mathf.RoundToInt(hull * 100) + "%");
                var samurai=Rules.Complete(vehicle)?Samurai.Get(vehicle):null;
                float heat = samurai!=null?samurai.Energy/100f:Mathf.Clamp01(status.Heat / 100f);
                GUI.color = new Color(.15f, .18f, .2f); Rect(left, top + 66, 200, 10);
                GUI.color = overheated ? new Color(1f, .4f, .2f) : new Color(.3f, .9f, 1f); Rect(left, top + 66, 200 * heat, 10);
                GUI.color = new Color(.75f, .9f, .95f);
                GUI.Label(new Rect(left, top + 80, 210, 20), samurai!=null?(Time.time<samurai.BrokenUntil?"盾能耗尽 · 防御失效":"盾能 "+Mathf.RoundToInt(samurai.Energy)+" / 100") : "光束温度 " + (overheated ? "过热！" : Mathf.RoundToInt(status.Heat).ToString()));
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
                else if(fresh)GUI.Label(new Rect(left,top+146,210,20),status.MissileAmmo<=0?"导弹缺弹 · 请补充货箱":"导弹就绪 · 按住 G 锁定");
                GUI.color=new Color(1f,.75f,.3f);
                string reason=!fresh?"等待火控同步":(status.Flags&8)!=0?"发射口被遮挡":(status.Flags&4)!=0?"超出射界 · 请转动机体":(status.Flags&128)!=0?"目标过近 · 至少 10 米":(status.Flags&256)!=0?"未锁定敌对目标":overheated?"过热冷却 · 降至 30 后恢复":status.BeamAmmo<=0?"光束缺弹 · 请补充货箱":"火控正常";
                if(samurai!=null && reason=="火控正常")reason=samurai.LaserWait>0?"头炮冷却 "+samurai.LaserWait.ToString("0.0")+"s":samurai.LaserCharge>0?"头炮蓄能 "+Mathf.RoundToInt(samurai.LaserCharge*100)+"%":"头炮就绪 · 按住 F 蓄能";
                GUI.Label(new Rect(left,top+170,230,20),reason);
                if(samurai!=null){GUI.color=new Color(.6f,1f,.85f);GUI.Label(new Rect(cx-160,cy+46,340,25),Flight.AirPose(motion)?"飞行收束 · 头炮 / 导弹可用":samurai.Swing?((samurai.Heavy?"重劈 · ":"剑击 · ")+SwordMotion.Stage(samurai,Time.time)):samurai.Charging?"重劈蓄力 "+Mathf.RoundToInt(Mathf.Clamp01((Time.time-samurai.PressedAt)/Samurai.HeavyCharge)*100)+"%":samurai.Guarding?"正面格挡生效":samurai.Alert>.1f?"低位警戒":"日常姿态",bold);}
                var hit=CombatFeedback.Get(vehicle.entityId);
                if(CombatFeedback.Fresh(vehicle.entityId)){
                    GUI.color=hit.Blocked?new Color(1,.65f,.2f):new Color(.65f,1,.8f);
                    if(hit.Damage>0){Rect(cx-10,cy-10,5,2);Rect(cx+5,cy-10,5,2);Rect(cx-10,cy+8,5,2);Rect(cx+5,cy+8,5,2);}
                    GUI.Label(new Rect(cx-100,cy+80,260,22),hit.Blocked?"剑刃受阻 · 收招":hit.Damage>0?"命中  "+Mathf.RoundToInt(hit.Damage):"接触",bold);
                }
                GUI.color = new Color(.75f, .9f, .95f);
                float fuel = vehicle.vehicle.GetFuelLevel();
                bool fueled = fuel > 0 || EntityVehicle.VehicleFuelUsageModifier == 0f;
                GUI.color = fueled ? new Color(.5f, 1f, .6f) : new Color(1f, .35f, .3f);
                GUI.Label(new Rect(left, top + 210, 210, 20),
                    fueled ? "燃料 " + Mathf.RoundToInt(fuel) + "L" : complete?"燃油耗尽 · 飞行升力关闭":"⚠ 燃料耗尽——加油后才能驱动/悬浮/跳跃");
                GUI.color = new Color(.75f, .9f, .95f);
                string hoverText = complete?(Flight.Active(motion)?"飞行推进 "+(EntityVehicle.VehicleFuelUsageModifier==0?"0":(motion.Boost?Rules.FlightBoostFuel:Rules.FlightFuel).ToString("0.##"))+"L/s":"[Q]飞行 关") : Locomotion.HoverOn ? "悬浮巡航中 · 耗油 0.5L/s" : "[Q]悬浮 关";
                GUI.Label(new Rect(left, top + 232, 210, 20), hoverText);
                bool jumpReady = Locomotion.JumpCooldownRemaining <= 0;
                GUI.color = jumpReady ? new Color(.5f, 1f, .6f) : new Color(.85f, .7f, .55f);
                GUI.Label(new Rect(left, top + 188, 210, 20), complete&&Flight.AirPose(motion)?"离地 "+(motion.FlightHeight<0?"—":motion.FlightHeight.ToString("0.0")+"m")+"  升降 "+(vehicle.vehicleRB!=null?vehicle.vehicleRB.velocity.y:0).ToString("+0.0;-0.0;0.0")+"m/s":jumpReady ? "[空格]蓄力跳 就绪（按住蓄力）" : "跳跃 充能 " + Mathf.CeilToInt(Locomotion.JumpCooldownRemaining) + "s");
                bool repairReady = status.BattleRepairWait <= 0;
                GUI.color = repairReady ? new Color(.5f, 1f, .6f) : new Color(.85f, .7f, .55f);
                GUI.Label(new Rect(left, top + 254, 210, 20), repairReady ? "紧急维修 就绪（货箱维修包）" : "紧急维修 充能 " + Mathf.CeilToInt(status.BattleRepairWait) + "s");
                if (Locomotion.JumpCharge > 0)
                {
                    float cy1 = Screen.height * .5f + 96;
                    GUI.color = new Color(.15f, .18f, .2f); Rect(Screen.width * .5f - 90, cy1, 180, 8);
                    GUI.color = Locomotion.JumpCharge >= 1f ? new Color(.5f, 1f, .6f) : new Color(.6f, .85f, 1f);
                    Rect(Screen.width * .5f - 90, cy1, 180 * Locomotion.JumpCharge, 8);
                    if (Locomotion.JumpCharge >= 1f)
                    { GUI.color = new Color(.6f, 1f, .7f); GUI.Label(new Rect(Screen.width * .5f - 90, cy1 + 10, 180, 20), "满蓄力 · 松开跳跃", label); }
                }
                GUI.color = new Color(.75f, .9f, .95f);
                GUI.Label(new Rect(left, top + 278, 250, 75), Rules.Complete(vehicle)?"[左键]剑击/按住蓄力  [右键]举盾\n[F]头炮  [G]导弹  [V]瞄准 [Z]倍率\n[Q]飞行/降落  [空格]上升 [C]下降\n[Shift]加速 [R]维修 / 车外[F]维修":"[左键]光束  [G按住]锁定并发射\n[右键]瞄准镜  [Z]切换倍率\n[Q]悬浮  [空格]蓄力跳  [C]下降\n[R]紧急维修  车外按住[F]维修");
            }
            finally { GUI.color = old; }
        }

        static void DrawDirection(EntityVehicle v,float cx,float cy)
        {
            var body=Weapons.BodyRotation(v).eulerAngles.y;var look=Optics.Look.eulerAngles.y;float relative=Mathf.DeltaAngle(look,body);
            var matrix=GUI.matrix;GUI.color=new Color(.4f,.9f,1f);GUIUtility.RotateAroundPivot(relative,new Vector2(cx,151));Rect(cx-1,143,2,17);Rect(cx-5,143,10,2);GUI.matrix=matrix;
            GUI.color=new Color(.7f,.9f,.95f);GUI.Label(new Rect(cx-155,165,330,22),string.Format("机体 {0:000}°   视线偏角 {1:+0;-0;0}°",body,Mathf.DeltaAngle(body,look)));
            var rig=Model.GetRig(v);if(rig==null)return;var muzzle=Weapons.MuzzleWorld(rig,v);var dir=Optics.WeaponDirection(v,rig);
            if(Optics.ProjectWorld(muzzle+dir*20,out var p)){GUI.color=new Color(.9f,.75f,.35f,.8f);Rect(p.x-4,p.y,8,1);Rect(p.x,p.y-4,1,8);}
        }

        // Cinematic letterbox + system-online typewriter during the ceremony.
        static void DrawBoardingOverlay(EntityPlayerLocal player)
        {
            float progress; bool dismount, rider;
            if (!Boarding.Describe(player, out progress, out dismount, out rider)) return;
            if (!rider) return;
            var old = GUI.color;
            try
            {
                // Bars fade in during the first 15% and out over the last 15%.
                float fade = Mathf.Min(Mathf.Clamp01(progress / .15f), Mathf.Clamp01((1f - progress) / .15f));
                float bar = 62f * fade;
                if (bar > .5f)
                {
                    GUI.color = new Color(0f, 0f, 0f, .85f);
                    GUI.DrawTexture(new Rect(0, 0, Screen.width, bar), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(0, Screen.height - bar, Screen.width, bar), Texture2D.whiteTexture);
                }
                // Typewriter runs across the second half (after the lift).
                if (progress >= .45f && progress <= .98f)
                {
                    string full = dismount ? "停机开舱 · 安全离舱" : "战斗系统联机 · [空格] 跳过";
                    float t = Mathf.Clamp01((progress - .45f) / .5f);
                    int chars = Mathf.Clamp(Mathf.FloorToInt(full.Length * t) + 1, 1, full.Length);
                    GUI.color = new Color(.55f, 1f, .95f, .95f);
                    if (bold != null)
                        GUI.Label(new Rect(Screen.width * .5f - 160, Screen.height - bar - 44, 420, 30), full.Substring(0, chars), bold);
                }
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
                GUI.Label(new Rect(cx - 98, cy + 42, 210, 22), Rules.DisplayName(target), bold);
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
