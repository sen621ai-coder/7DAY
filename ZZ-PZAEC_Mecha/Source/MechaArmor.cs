using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace PZAEC.Mecha
{
    // Hull and sealed-cabin crew protection plus the field repair channel.
    // Damage reduction follows the ApacheArmor response-once pattern; crew
    // hull-transfer is zeroed like the closed-cabin M1. The 99999 destruction
    // sentinel is composed with ApacheArmor/M1 via HarmonyAfter so all three
    // vehicle mods can coexist on the same ApplyDamage IL.
    public static class MechaArmor
    {
        static bool enabled;
        [ThreadStatic] static int explosionDepth;
        [ThreadStatic] static EntityVehicle responseVehicle;
        [ThreadStatic] static bool explicitSuicide;
        public struct ResponseScope { public EntityVehicle Vehicle; public bool Suicide; }
        static readonly Dictionary<EntityAlive,float> safeExit=new Dictionary<EntityAlive,float>();
        public static void ClearTravelProtection(){safeExit.Clear();}
        public static void SafeDismount(EntityAlive actor,EntityVehicle v)
        {if(actor!=null&&v!=null&&v.GetWheelsOnGround()>0&&v.vehicleRB!=null&&v.vehicleRB.velocity.sqrMagnitude<.16f)safeExit[actor]=Time.time+1.5f;}
        public static bool ProtectTravel(EntityAlive actor,EnumDamageTypes type)
        {
            if(actor==null||actor.IsDead()||(type!=EnumDamageTypes.Falling&&type!=EnumDamageTypes.VehicleInside))return false;
            var v=actor.AttachedToEntity as EntityVehicle;
            if(Weapons.IsMecha(v)&&!v.IsDead()&&v.GetAttached(0)==actor)return true;
            float until;if(safeExit.TryGetValue(actor,out until)){if(Time.time<=until&&type==EnumDamageTypes.Falling)return true;safeExit.Remove(actor);}
            return false;
        }
        static bool TravelDamage(EntityPlayer __instance,DamageSource __0,ref int __result)
        {if(__0==null||!ProtectTravel(__instance,__0.damageType))return true;__result=0;return false;}
        static bool TravelFall(EntityPlayerLocal __instance){return !ProtectTravel(__instance,EnumDamageTypes.Falling);}

        public static void Install(Harmony h)
        {
            try
            {
                var signature = new[] { typeof(DamageSource), typeof(int), typeof(bool), typeof(float) };
                h.Patch(AccessTools.Method(typeof(EntityPlayer),"DamageEntity",signature),prefix:new HarmonyMethod(typeof(MechaArmor),nameof(TravelDamage)));
                h.Patch(AccessTools.Method(typeof(EntityPlayerLocal),"DamageEntity",signature),prefix:new HarmonyMethod(typeof(MechaArmor),nameof(TravelDamage)));
                h.Patch(AccessTools.Method(typeof(EntityPlayerLocal),"FallImpact"),prefix:new HarmonyMethod(typeof(MechaArmor),nameof(TravelFall)));
                h.Patch(AccessTools.Method(typeof(EntityAlive), "damageEntityLocal", signature),
                    transpiler: new HarmonyMethod(typeof(MechaArmor), nameof(ResponseTranspiler)));
                h.Patch(AccessTools.Method(typeof(EntityVehicle), "damageEntityLocal", signature),
                    transpiler: new HarmonyMethod(typeof(MechaArmor), nameof(ResponseTranspiler)));
                h.Patch(AccessTools.Method(typeof(EntityVehicle), "ProcessDamageResponseLocal", new[] { typeof(DamageResponse) }),
                    prefix: new HarmonyMethod(typeof(MechaArmor), nameof(BeginResponse)),
                    finalizer: new HarmonyMethod(typeof(MechaArmor), nameof(EndResponse)));
                h.Patch(AccessTools.Method(typeof(EntityVehicle), "ApplyDamage", new[] { typeof(int) }),
                    prefix: new HarmonyMethod(typeof(MechaArmor), nameof(BeforeApply)),
                    transpiler: new HarmonyMethod(typeof(MechaArmor), nameof(DamageSentinel)));
                h.Patch(AccessTools.Method(typeof(Explosion), "AttackEntites", new[] { typeof(int), typeof(ItemValue), typeof(EnumDamageTypes) }),
                    prefix: new HarmonyMethod(typeof(MechaArmor), nameof(BeginExplosion)),
                    finalizer: new HarmonyMethod(typeof(MechaArmor), nameof(EndExplosion)));
                h.Patch(AccessTools.Method(typeof(Vehicle), "GetPlayerDamagePercent"),
                    postfix: new HarmonyMethod(typeof(MechaArmor), nameof(CrewProtection)));
                h.Patch(AccessTools.Method(typeof(XUiM_Vehicle), "RepairVehicle"),
                    prefix: new HarmonyMethod(typeof(MechaArmor), nameof(BlockNativeRepair)));
                h.Patch(AccessTools.Method(typeof(Vehicle), "RepairParts"),
                    prefix: new HarmonyMethod(typeof(MechaArmor), nameof(BlockRepairParts)));
                h.Patch(AccessTools.Method(typeof(ItemActionEntryRepair), "OnActivated"),
                    prefix: new HarmonyMethod(typeof(MechaArmor), nameof(BlockInventoryRepair)));
                enabled = true;
                Log.Out("[Mecha] Hull 15/30/50% damage channels (10% per-hit cap), sealed cabin (crew transfer zeroed, direct hits 10/20% capped at 20% HP), field + battle repair active.");
            }
            catch (Exception ex)
            {
                enabled = false;
                Log.Error("[Mecha] Armor disabled: " + ex);
            }
        }

        static bool Hull(EntityVehicle v)
        { return enabled && Weapons.IsMecha(v) && !v.IsDead() && v.vehicle.GetHealth() > 0; }

        public static int Reduce(int damage, int maxHealth, double fraction, double capFraction)
        {
            if (damage <= 0 || maxHealth <= 0) return damage;
            return (int)Math.Min(Math.Max(1, Math.Floor(maxHealth * capFraction)), Math.Max(1, Math.Ceiling(damage * fraction)));
        }

        public static bool Combat(DamageSource source)
        {
            if (source == null || source.damageType == EnumDamageTypes.Suicide) return false;
            if (source.damageType == EnumDamageTypes.VehicleInside || source.damageType == EnumDamageTypes.Falling) return true;
            // Environmental/illness ticks keep their native sealed-cabin rules.
            return source.damageSource == EnumDamageSource.External;
        }

        public static void ProtectResponse(ref DamageResponse response, EntityAlive entity)
        {
            if(response.Source!=null&&entity is EntityPlayer&&ProtectTravel(entity,response.Source.damageType))
            {response.Strength=response.ModStrength=0;response.Fatal=false;return;}
            if (!enabled || response.Strength <= 0 || response.Source == null || response.Source.damageType == EnumDamageTypes.Suicide) return;
            int max; double fraction, cap;
            var hull = entity as EntityVehicle;
            bool collision = response.Source.damageType == EnumDamageTypes.VehicleInside || response.Source.damageType == EnumDamageTypes.Falling;
            if (Hull(hull))
            {
                // Main-battle channels: a 2M hull at 15% combat keeps a full
                // T19 Blood Moon night survivable without mid-fight repairs.
                max = hull.vehicle.GetMaxHealth();
                fraction = explosionDepth > 0 ? .3 : collision ? .5 : .15;
                cap = .1;
                if(!collision && response.Source.damageSource==EnumDamageSource.External)
                    fraction = Math.Round(fraction * Samurai.ShieldFactor(hull,response.Source,response.Strength,explosionDepth>0),6);
            }
            else
            {
                var player = entity as EntityPlayer;
                var vehicle = player != null ? player.AttachedToEntity as EntityVehicle : null;
                if (player == null || player.IsDead() || !Hull(vehicle) || !Combat(response.Source) ||
                    vehicle.GetAttached(0) != player) return;
                // The walker is a single-seat closed cabin: hull transfer is
                // zeroed separately, so this branch only catches direct hits
                // (e.g. an explosion enumerating the seated player entity).
                max = player.GetMaxHealth();
                fraction = responseVehicle == vehicle ? .1 : explosionDepth > 0 ? .2 : collision ? .25 : .1;
                cap = .2;
            }
            // Runs once at response creation; received packets must not reduce again.
            response.Strength = Reduce(response.Strength, max, fraction, cap);
            response.ModStrength = Reduce(response.ModStrength, max, fraction, cap);
            if (response.Strength < entity.Health) response.Fatal = false;
        }

        public static void BeginExplosion(out int __state) { __state = explosionDepth; explosionDepth++; }
        public static Exception EndExplosion(Exception __exception, int __state) { explosionDepth = __state; return __exception; }
        public static void BeginResponse(EntityVehicle __instance, DamageResponse __0, out ResponseScope __state)
        {
            __state = new ResponseScope { Vehicle = responseVehicle, Suicide = explicitSuicide };
            responseVehicle = __instance;
            explicitSuicide = __0.Source != null && __0.Source.damageType == EnumDamageTypes.Suicide;
        }
        public static Exception EndResponse(Exception __exception, ResponseScope __state)
        { responseVehicle = __state.Vehicle; explicitSuicide = __state.Suicide; return __exception; }

        public static void BeforeApply(EntityVehicle __instance, ref int __0)
        {
            if (!enabled || !Weapons.IsMecha(__instance)) return;
            if (__0 > 0 && __0 < 99999)
            {
                var state = Weapons.GetState(__instance);
                state.LastDamage = Time.time;
                state.RepairStarted = -1;
                state.RepairTrigger.Stop();
            }
            // Unwrapped ApplyDamage is the native accumulated collision/wear path.
            if (Hull(__instance) && responseVehicle != __instance)
                __0 = Reduce(__0, __instance.vehicle.GetMaxHealth(), .5, .1);
        }

        // Closed cabin: seated pilots never take hull-transfer percentages.
        public static void CrewProtection(Vehicle __instance, ref float __result)
        { if (enabled && Weapons.IsMecha(__instance)) __result = 0f; }

        static int MechaThreshold(EntityVehicle vehicle)
        { return Hull(vehicle) && !(responseVehicle == vehicle && explicitSuicide) ? int.MaxValue : 99999; }

        static int MechaComposite(int previous, EntityVehicle vehicle)
        { return Hull(vehicle) && !(responseVehicle == vehicle && explicitSuicide) ? int.MaxValue : previous; }

        // Must run after ApacheArmor and the M1 tank so their threshold calls
        // are already in the IL; compose with whichever chain tail exists.
        [HarmonyAfter("pzaec.apache.armor.v1", "pzaec.m1.abrams")]
        static IEnumerable<CodeInstruction> DamageSentinel(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            int composite = FindCalls(code, "PZAEC.M1.Combat", "CombinedThreshold");
            int apache = composite < 0 ? FindCalls(code, "AECT16RuntimeFix.ApacheArmor", "KillThreshold") : -1;
            if (composite >= 0 || apache >= 0)
            {
                int at = composite >= 0 ? composite : apache;
                code.InsertRange(at + 1, new[]
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MechaArmor), nameof(MechaComposite)))
                });
                return code;
            }
            int matches = 0;
            for (int i = 0; i < code.Count; i++)
                if (code[i].opcode == OpCodes.Ldc_I4 && Equals(code[i].operand, 99999))
                {
                    code[i].opcode = OpCodes.Ldarg_0; code[i].operand = null;
                    code.Insert(++i, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MechaArmor), nameof(MechaThreshold))));
                    matches++;
                }
            if (matches != 1) throw new InvalidOperationException("Unsupported vehicle instant-explosion threshold");
            return code;
        }

        static int FindCalls(List<CodeInstruction> code, string typeFullName, string method)
        {
            for (int i = 0; i < code.Count; i++)
                if (code[i].opcode == OpCodes.Call && code[i].operand is MethodInfo method2 &&
                    method2.DeclaringType != null && method2.DeclaringType.FullName == typeFullName &&
                    method2.Name == method)
                    return i;
            return -1;
        }

        public static IEnumerable<CodeInstruction> ResponseTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var code = new List<CodeInstruction>(instructions);
            string target = __originalMethod.DeclaringType == typeof(EntityVehicle) ? "ProcessDamageResponseLocal" : "FireAttackedEvents";
            int matches = 0;
            for (int i = 2; i < code.Count; i++)
                if (code[i].operand is MethodInfo method && method.Name == target)
                {
                    if (code[i - 2].opcode != OpCodes.Ldarg_0 || code[i - 1].opcode != OpCodes.Ldloc_0)
                        throw new InvalidOperationException("Unsupported damage response local layout");
                    var start = new CodeInstruction(OpCodes.Ldloca_S, (byte)0);
                    start.labels.AddRange(code[i - 2].labels); code[i - 2].labels.Clear();
                    start.blocks.AddRange(code[i - 2].blocks); code[i - 2].blocks.Clear();
                    code.InsertRange(i - 2, new[]
                    {
                        start,
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MechaArmor), nameof(ProtectResponse)))
                    });
                    i += 3; matches++;
                }
            if (matches != 1) throw new InvalidOperationException("Unsupported damage response application sites");
            return code;
        }

        // ---------------- field repair ----------------

        static bool BlockNativeRepair(XUi __0, Vehicle __1, ref bool __result)
        {
            var v = __1 ?? (__0 != null && __0.Vehicle != null && __0.Vehicle.CurrentVehicle != null ? __0.Vehicle.CurrentVehicle.vehicle : null);
            if (v == null || !Weapons.IsMecha(v)) return true;
            __result = false;
            var p = __0 != null && __0.playerUI != null ? __0.playerUI.entityPlayer : null;
            if (p != null) GameManager.ShowTooltip(p, "机甲维修：将原版维修包放入货箱，停车后在车外对准机甲按住 F 键 8 秒。");
            return false;
        }

        static bool BlockRepairParts(Vehicle __instance)
        { return !(enabled && Weapons.IsMecha(__instance)); }

        static bool BlockInventoryRepair(ItemActionEntryRepair __instance)
        {
            var slot = __instance.ItemController as XUiC_ItemStack;
            var item = slot != null && slot.ItemStack != null ? slot.ItemStack.itemValue : null;
            if (item == null || item.ItemClass == null || !Weapons.IsMecha(item.ItemClass.GetItemName())) return true;
            GameManager.ShowTooltip(slot.xui.playerUI.entityPlayer, "请先放置机甲，将原版维修包放入货箱，在车外对准机甲按住 F 键维修。");
            return false;
        }

        static bool Eligible(World w, Weapons.State s, int actor)
        {
            var v = s.Vehicle;
            var p = w.GetEntity(actor) as EntityPlayer;
            return p != null && !p.IsDead() && p.AttachedToEntity == null && !v.IsDead() && v.vehicle.GetHealth() > 0 && !v.hasDriver &&
                (p.position - v.position).sqrMagnitude <= Rules.RepairRange * Rules.RepairRange &&
                (v.vehicleRB == null || v.vehicleRB.velocity.sqrMagnitude < .04f) &&
                Time.time - s.LastDamage >= Rules.RepairIdleSeconds && Time.time - s.LastWeaponUse >= Rules.RepairIdleSeconds &&
                v.vehicle.GetRepairAmountNeeded() > 0 &&
                (LockManager.Instance == null || !LockManager.Instance.IsLockedServer(v, 0)) &&
                (v.GetOwner() == null || (p.PersistentPlayerData != null && v.IsUserAllowed(p.PersistentPlayerData.PrimaryId)));
        }

        public static void RepairRequest(World w, int actor, Weapons.State s, byte op, Vector3 origin, Vector3 direction, int sequence)
        {
            var p = w.GetEntity(actor) as EntityPlayer;
            if (s.RepairTrigger.Active(Time.time) && s.RepairTrigger.Actor != actor) return;
            if (op == Weapons.RepairStop)
            {
                if (s.RepairTrigger.Actor == actor && s.RepairTrigger.Accept(actor, sequence, false, Time.time)) s.RepairStarted = -1;
                return;
            }
            if (!Eligible(w, s, actor) || s.Vehicle.bag == null ||
                s.Vehicle.bag.GetItemCount(ItemClass.GetItem(Rules.RepairKit, false)) < 1 ||
                (origin - p.position).sqrMagnitude > 16 ||
                !Voxel.Raycast(w, new Ray(origin, direction), Rules.RepairRange, -538750997, 8, 0f) ||
                ItemActionAttack.FindHitEntity(Voxel.voxelRayHitInfo) != s.Vehicle) return;
            bool continued = s.RepairTrigger.Active(Time.time) && s.RepairTrigger.Actor == actor;
            if (!s.RepairTrigger.Accept(actor, sequence, true, Time.time)) return;
            if (!continued || s.RepairStarted < 0) s.RepairStarted = Time.time;
        }

        public static void UpdateRepair(World w, Weapons.State s, float now)
        {
            if (s.RepairStarted < 0) return;
            if (!s.RepairTrigger.Active(now) || !Eligible(w, s, s.RepairTrigger.Actor))
            { s.RepairStarted = -1; s.RepairTrigger.Stop(); return; }
            if (now - s.RepairStarted < Rules.RepairSeconds) return;
            var kit = ItemClass.GetItem(Rules.RepairKit, false);
            if (kit != null && kit.type != 0 && s.Vehicle.bag != null && s.Vehicle.bag.DecItem(kit, 1) == 1)
            {
                s.Vehicle.Health = s.Vehicle.vehicle.GetMaxHealth();
                s.Vehicle.SendSyncData(EntityVehicle.cSyncItem | EntityVehicle.cSyncStorage);
            }
            s.RepairStarted = -1;
            s.RepairTrigger.Stop();
        }
    }
}
