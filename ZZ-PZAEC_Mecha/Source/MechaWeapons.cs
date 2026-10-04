using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace PZAEC.Mecha
{
    // Server-authoritative walker combat: the eye beam and shoulder missiles.
    // Clients send intent rays only; ammo, arcs, tracing, damage and guidance
    // all resolve on the server exactly like the Apache/T16 weapon stack.
    public static class Weapons
    {
        public const byte Aim = 0, Fire = 1, Stop = 2, MissileAim = 3, MissileFire = 4, Repair = 5, RepairStop = 6, BattleRepair = 7, SwitchMelee = 8, MeleeSweep = 9, MeleeHeavy = 10, Trample = 11, Stomp = 12, Motion = 13, SkipBoard = 14, BoardControl = 15;
        public const byte BeamEvent = 0, MissileSpawnEvent = 1, MissileMoveEvent = 2, ImpactEvent = 3, StatusEvent = 4, MeleeSweepEvent = 5, MeleeHeavyEvent = 6, MeleeModeEvent = 7;

        public sealed class State
        {
            public EntityVehicle Vehicle;
            public float Heat, NextBeam, NextMissile, NextAim, NextStatus;
            public bool Overheated, Aiming, MissileAiming, GuidedSpent;
            public Vector3 AimOrigin, AimDirection;
            public byte AimReason;
            public EntityAlive LockTarget;
            public float LockStarted, LockProgress;
            public float LastDamage = -100, LastWeaponUse = -100, RepairStarted = -100, NextBattleRepair;
            public bool MeleeMode;
            public bool TriggerHeld, MissileTrigger;
            public float NextMelee = -100, NextModeSwitch;
            public float NextStomp = -100, NextTrampleTick;
            public readonly Dictionary<int, float> TrampleUntil = new Dictionary<int, float>();
            public readonly TriggerLease RepairTrigger = new TriggerLease();
        }
        sealed class Missile
        {
            public int Id, VehicleId, ShooterId;
            public EntityVehicle Vehicle;
            public Vector3 Position, Velocity;
            public EntityAlive Target; public Vector3 TargetOffset;
            public float Age, NextSync;
        }
        public sealed class TriggerLease
        {
            public int Actor = -1, LastSequence; public float Until;
            public bool Active(float now) { return Actor >= 0 && Until > now; }
            public bool Accept(int actor, int sequence, bool firing, float now)
            {
                if (Actor != actor) { Actor = actor; LastSequence = 0; Until = 0; }
                // Stale sequences are dropped without touching any state: an
                // old replayed message must never extend the session.
                if (sequence <= LastSequence) return false;
                LastSequence = sequence;
                // Any live message (aim or trigger) keeps the aim session up;
                // whether the weapon actually fires is the separate Trigger fields.
                Until = now + Rules.HoldTimeout;
                return true;
            }
            public void Stop() { Until = 0; }
        }

        public static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }

        static bool enabled;
        static World currentWorld;
        static readonly Dictionary<int, State> states = new Dictionary<int, State>();
        static readonly Dictionary<int, TriggerLease> leases = new Dictionary<int, TriggerLease>();
        static readonly List<Missile> missiles = new List<Missile>();
        static readonly List<int> remove = new List<int>();
        static int serial, inputSequence;
        static bool inputHeld, inputMissileHeld;
        static float nextInput, nextAim, nextError;
        static int inputVehicle = -1;
        static float nextFireAudit;
        public static bool Server { get { return ConnectionManager.Instance != null && ConnectionManager.Instance.IsServer; } }
        public static bool IsMecha(EntityVehicle v)
        { return v != null && v.vehicle != null && Rules.VehicleNameMatches(v.vehicle.GetName()); }

        public static bool IsMecha(Vehicle v)
        { return v != null && Rules.VehicleNameMatches(v.GetName()); }

        public static bool IsMecha(string placeableItemName)
        { return Rules.ItemNameMatches(placeableItemName); }

        public static bool UIReady(EntityPlayerLocal player)
        {
            if (player == null || GameManager.Instance == null || !GameManager.Instance.GameIsFocused || GameManager.Instance.IsPaused()) return false;
            var ui = LocalPlayerUI.GetUIForPlayer(player);
            return ui == null || !(LocalPlayerUI.AnyModalWindowOpen() || ui.windowManager.IsCursorWindowOpen() || ui.windowManager.IsInputActive());
        }

        public static void Install(Harmony harmony)
        {
            try
            {
                harmony.Patch(AccessTools.Method(typeof(GameManager), "Update"),
                    postfix: new HarmonyMethod(typeof(Weapons), nameof(Update)));
                harmony.Patch(AccessTools.Method(typeof(GameManager), "SaveAndCleanupWorld"),
                    prefix: new HarmonyMethod(typeof(Weapons), nameof(Clear)));
                harmony.Patch(AccessTools.Method(typeof(EntityPlayerLocal), "OnGUI"),
                    postfix: new HarmonyMethod(typeof(MechaHUD), nameof(MechaHUD.Draw)));
                enabled = true;
                Log.Out("[Mecha] Combat Robot beam / blades / guided missiles enabled; server-authoritative cargo ammunition.");
            }
            catch (Exception ex) { enabled = false; Log.Error("[Mecha] Weapons disabled: " + ex.GetBaseException().Message); }
        }

        public static void Clear()
        {
            states.Clear(); missiles.Clear(); leases.Clear(); currentWorld = null;
            nextInput = nextAim = 0; inputVehicle = -1; inputHeld = inputMissileHeld = false;
            MechaFX.Clear(); Gait.Clear(); Deploy.Clear(); Optics.Clear(); CrewVisibility.Clear(); Boarding.Clear(); Locomotion.Clear(); RobotAudio.Clear(); Model.ClearRuntime(); MechaArmor.ClearTravelProtection();
        }

        static void EnsureWorld(World world) { if (world != currentWorld) { Clear(); currentWorld = world; } }

        public static State GetState(EntityVehicle vehicle)
        {
            State state;
            if (!states.TryGetValue(vehicle.entityId, out state) || state.Vehicle != vehicle)
            { state = new State { Vehicle = vehicle }; states[vehicle.entityId] = state; }
            return state;
        }

        static TriggerLease GetLease(int vehicleId)
        {
            TriggerLease lease;
            if (!leases.TryGetValue(vehicleId, out lease)) { lease = new TriggerLease(); leases[vehicleId] = lease; }
            return lease;
        }

        public static Quaternion BodyRotation(EntityVehicle v)
        { return v.vehicleRB != null ? v.vehicleRB.rotation : v.transform.rotation; }

        public static Vector3 EyeWorld(Model.Rig rig, EntityVehicle v)
        {
            var head = rig != null && rig.Head != null ? rig.Head : null;
            // Palm emitter: fire from the head FPV anchor slightly forward.
            return head != null ? head.position + head.forward * .25f + Origin.position :
                v.position + BodyRotation(v) * new Vector3(0, 2.6f, .8f);
        }

        public static Vector3 MuzzleWorld(Model.Rig rig,EntityVehicle v)
        { return rig!=null&&rig.HandR!=null ? rig.HandR.position+rig.HandR.forward*.65f+Origin.position : EyeWorld(rig,v); }

        static bool ReadyOperator(State state, int actor)
        {
            var player = currentWorld != null ? currentWorld.GetEntity(actor) as EntityPlayer : null;
            var attached = state.Vehicle.GetAttached(0);
            return attached != null && attached.entityId == actor &&
                (player == null || !player.IsDead()) &&
                !state.Vehicle.IsDead() && !Boarding.Active(state.Vehicle) && state.Vehicle.vehicle.GetHealth() > 0;
        }

        static bool Consume(State state, string name)
        {
            var item = ItemClass.GetItem(name, false);
            if (item == null || item.type == 0 || state.Vehicle.bag == null || state.Vehicle.bag.GetItemCount(item) < 1) return false;
            if (state.Vehicle.bag.DecItem(item, 1) != 1) return false;
            return true;
        }

        static int Ammo(State state, string name)
        {
            var item = ItemClass.GetItem(name, false);
            return item != null && item.type != 0 && state.Vehicle.bag != null ? state.Vehicle.bag.GetItemCount(item) : 0;
        }

        // Raycast through the walker and its crew only; everything else is solid.
        public static bool Trace(EntityVehicle vehicle, Vector3 start, Vector3 direction, float range, out WorldRayHitInfo hit)
        {
            hit = null;
            float remaining = range;
            for (int i = 0; i < 32 && remaining > .001f; i++)
            {
                if (!Voxel.Raycast(currentWorld, new Ray(start, direction), remaining, -538750997, 8, 0f)) return false;
                var candidate = Voxel.voxelRayHitInfo.Clone();
                var entity = ItemActionAttack.FindHitEntity(candidate);
                if (entity != null && vehicle != null && (entity == vehicle || entity == vehicle.GetAttached(0)))
                {
                    float travelled = Mathf.Max(.1f, Vector3.Distance(start, candidate.hit.pos) + .15f);
                    start += direction * travelled; remaining -= travelled; continue;
                }
                hit = candidate; return true;
            }
            return false;
        }

        static bool ValidSight(EntityVehicle vehicle, Vector3 origin, Vector3 direction)
        {
            return direction.sqrMagnitude > .0001f && Finite(direction.x) && Finite(direction.y) && Finite(direction.z) &&
                Finite(origin.x) && Finite(origin.y) && Finite(origin.z) &&
                (origin - vehicle.position).sqrMagnitude <= 6 * 6;
        }

        public static bool InArc(EntityVehicle v, Vector3 direction, float maxYaw)
        {
            var local = Quaternion.Inverse(BodyRotation(v)) * direction.normalized;
            float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
            return Mathf.Abs(yaw) <= maxYaw && pitch >= Rules.AimMinPitch && pitch <= Rules.AimMaxPitch;
        }

        // 0 ready, 1 arc invalid, 2 obstructed muzzle, 3 not aiming, 5 acquiring, 6 operator/storage.
        public static byte ResolveBeam(State state, Vector3 origin, Vector3 view, out Vector3 direction)
        {
            direction = view;
            var rig = Model.GetRig(state.Vehicle);
            var pivot = MuzzleWorld(rig, state.Vehicle);
            if (currentWorld == null || !ValidSight(state.Vehicle, origin, view)) return 1;
            view.Normalize();
            var aimPoint=origin+view*Rules.BeamRange;
            if(Trace(state.Vehicle,origin,view,Rules.BeamRange,out var aimHit))aimPoint=aimHit.hit.pos;
            direction = AimFromMuzzle(pivot,aimPoint,view);
            if (!InArc(state.Vehicle, direction, 90f)) return 1;
            var muzzle = pivot + direction * Rules.MuzzleOffset;
            if (Trace(state.Vehicle, pivot, direction, Rules.MuzzleOffset, out var obstruction)) return 2;
            return 0;
        }
        public static Vector3 AimFromMuzzle(Vector3 muzzle,Vector3 target,Vector3 fallback)
        {var delta=target-muzzle;return delta.sqrMagnitude>.0001f?delta.normalized:fallback.normalized;}
        public static Vector3 MissileAimPoint(EntityAlive target){return target.GetPosition()+Vector3.up*.8f;}

        public static void Request(World world, int actor, int vehicleId, byte op, Vector3 direction, Vector3 origin, int sequence)
        {
            if (!enabled || !Server || world == null || op > BoardControl) return;
            EnsureWorld(world);
            var vehicle = world.GetEntity(vehicleId) as EntityVehicle;
            if (!IsMecha(vehicle)) return;
            if(op==SwitchMelee||op==MeleeSweep||op==MeleeHeavy)return; // Reserved old protocol IDs, disabled server-side.
            if(op==BoardControl){if(Finite(direction.x))Boarding.Request(world,vehicle,actor,(int)direction.x);return;}
            if(op==SkipBoard&&Boarding.Owned(vehicle,actor)){Boarding.Skip(vehicle);return;}
            if(op==Motion || op==SkipBoard)
            {
                var driver=vehicle.GetAttached(0);
                if(driver==null || driver.entityId!=actor) return;
                if(op==SkipBoard) { Boarding.Skip(vehicle); return; }
                if(!Locomotion.Receive(vehicle,actor,sequence,direction)) return;
                Broadcast(vehicleId,sequence,9,direction,Vector3.zero,actor,0);
                return;
            }
            // Weapons, repairs and mode flips stay locked during the ceremony.
            if (Boarding.Active(vehicle)) return;
            var state = GetState(vehicle);
            if (op == Repair || op == RepairStop)
            {
                // Field repair intents come from a player standing outside, so
                // the seated-operator checks below never apply to them.
                MechaArmor.RepairRequest(world, actor, state, op, direction, origin, sequence);
                return;
            }
            var attached = vehicle.GetAttached(0);
            if (attached == null || attached.entityId != actor) return;
            if (op == BattleRepair)
            {
                // Instant seated-driver repair; own cooldown gate, no trigger lease.
                BattleRepairRequest(state, actor, Time.time);
                return;
            }
            if (op == SwitchMelee)
            {
                SwitchMeleeRequest(state, Time.time);
                return;
            }
            if (op == Trample || op == Stomp)
            {
                // Movement-channel damage intents from the physics client.
                TrampleRequest(state, actor, op, Time.time);
                return;
            }
            if (op >= MeleeSweep)
            {
                MeleeRequest(state, actor, op, Time.time);
                return;
            }
            // The beam is the ranged-mode trigger; melee mode swaps Mouse0
            // onto the blades and the server rejects beam fire outright.
            if (op == Fire && state.MeleeMode) return;
            var lease = GetLease(vehicleId);
            bool firing = op == Fire || op == MissileFire;
            if (!lease.Accept(actor, sequence, firing, Time.time)) return;
            if (!ReadyOperator(state, actor))
            { lease.Stop(); state.Aiming = false; state.MissileAiming = false; state.TriggerHeld = false; state.MissileTrigger = false; return; }
            if (op == Stop)
            {
                state.Aiming = false; state.MissileAiming = false;
                state.TriggerHeld = false; state.MissileTrigger = false;
                state.LockTarget = null; state.LockProgress = 0; return;
            }
            if (!ValidSight(vehicle, origin, direction))
            {
                state.Aiming = false; state.MissileAiming = false;
                state.TriggerHeld = false; state.MissileTrigger = false; return;
            }
            state.AimOrigin = origin; state.AimDirection = direction.normalized;
            // Aim messages only keep the sight session alive; only Fire/MissileFire
            // ever press a trigger. Idle aim spam can no longer shoot.
            state.Aiming = op == Aim || op == Fire;
            state.MissileAiming = op == MissileAim || op == MissileFire;
            state.TriggerHeld = op == Fire;
            state.MissileTrigger = op == MissileFire;
            if(state.Aiming)state.AimReason=ResolveBeam(state,origin,direction,out var corrected);
            if (!state.MissileAiming) { state.LockTarget = null; state.LockProgress = 0; }
        }

        // Seated-driver emergency repair: burns one vanilla repair kit from the
        // cargo for an instant hull chunk. The main-battle sustain during a
        // Blood Moon, when the 10 s out-of-combat field channel cannot run.
        static void BattleRepairRequest(State state, int actor, float now)
        {
            if (now < state.NextBattleRepair || !ReadyOperator(state, actor)) return;
            var kit = ItemClass.GetItem(Rules.RepairKit, false);
            if (kit == null || kit.type == 0 || state.Vehicle.bag == null ||
                state.Vehicle.bag.GetItemCount(kit) < 1 || state.Vehicle.bag.DecItem(kit, 1) != 1) return;
            state.NextBattleRepair = now + Rules.BattleRepairCooldown;
            int max = state.Vehicle.vehicle.GetMaxHealth();
            int heal = Math.Max(1, (int)Math.Round(max * Rules.BattleRepairFraction));
            state.Vehicle.Health = Math.Min(max, state.Vehicle.Health + heal);
            state.Vehicle.SendSyncData(EntityVehicle.cSyncItem | EntityVehicle.cSyncStorage);
            SendStatus(state, now);
        }

        public static bool Hostile(EntityAlive alive)
        {
            return alive != null && !(alive is EntityPlayer) && !(alive is EntityVehicle) && !alive.IsDead() &&
                (alive is EntityZombie || alive.EntityClass.Tags.Test_AnySet(FastTags<TagGroup.Global>.Parse("hostile")));
        }

        // Local physics client asks the server to resolve movement damage:
        // the driver's machine knows wheels/ground/velocity, the server owns
        // every hit point. Rate limits keep a forged client from spamming.
        static void TrampleRequest(State state, int actor, byte op, float now)
        {
            if (!ReadyOperator(state, actor)) return;
            var position = state.Vehicle.position;
            if (op == Stomp)
            {
                if (now < state.NextStomp) return;
                state.NextStomp = now + Rules.StompCooldown;
                var action = new DynamicProperties(); var explosion = new DynamicProperties();
                action.Classes.Add("Explosion", explosion);
                var blast = new ExplosionData(action, null) { ParticleIndex = 5, BlockRadius = 0,
                    EntityRadius = (byte)Rules.StompRadius, EntityDamage = Rules.StompDamage*Rules.AttributeScale(state.Vehicle), BlockDamage = 0,
                    BlastPower = (int)Rules.StompKnockback };
                GameManager.Instance.ExplosionServer(position, new Vector3i(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.y), Mathf.FloorToInt(position.z)),
                    Quaternion.identity, blast, actor, 0, false, ItemClass.GetItem(Rules.BeamAmmo, false));
                return;
            }
            if (now < state.NextTrampleTick) return;
            state.NextTrampleTick = now + Rules.TrampleTickSeconds;
            var expired = new List<int>();
            foreach (var pair in state.TrampleUntil) if (pair.Value < now) expired.Add(pair.Key);
            foreach (int id in expired) state.TrampleUntil.Remove(id);
            foreach (var entity in currentWorld.Entities.list)
            {
                var alive = entity as EntityAlive;
                if (!Hostile(alive)) continue;
                var delta = alive.GetPosition() - position;
                var planar = new Vector2(delta.x, delta.z);
                // Crush zone: inside the tripod footprint and under the belly.
                if (planar.sqrMagnitude > Rules.TrampleRadius * Rules.TrampleRadius) continue;
                if (delta.y < .1f || delta.y > 2.2f) continue;
                if (state.TrampleUntil.ContainsKey(alive.entityId)) continue;
                state.TrampleUntil[alive.entityId] = now + Rules.TrampleEntityInterval;
                var source = new DamageSourceEntity(EnumDamageSource.External, EnumDamageTypes.Bashing, actor, Vector3.up)
                { AttackingItem = ItemClass.GetItem(Rules.BeamAmmo, false), canHitSpecialBodyParts = false, DismemberChance = 0 };
                alive.DamageEntity(source, (int)(Rules.TrampleDamage*Rules.AttributeScale(state.Vehicle)), false, 0);
            }
        }

        // Public intent entry for the local movement adapter (trample/stomp).
        public static void SendLocalIntent(EntityVehicle vehicle, byte op, Vector3 direction, Vector3 origin)
        {
            var player = currentWorld != null ? currentWorld.GetPrimaryPlayer() as EntityPlayerLocal : null;
            if (player == null || vehicle == null) return;
            int sequence = unchecked(++inputSequence);
            if (Server) Request(currentWorld, player.entityId, vehicle.entityId, op, direction, origin, sequence);
            else ConnectionManager.Instance.SendToServer(NetPackageManager.GetPackage<NetPackagePZAECMechaIntent>().Setup(vehicle.entityId, op, direction, origin, sequence));
        }

        // X-key weapon mode. The server owns the flag, so a client cannot fire
        // the beam or swing blades without switching through this channel.
        static void SwitchMeleeRequest(State state, float now)
        {
            if (now < state.NextModeSwitch) return;
            state.NextModeSwitch = now + Rules.MeleeSwitchCooldown;
            state.MeleeMode = !state.MeleeMode;
            state.Aiming = false; state.MissileAiming = false;
            state.LockTarget = null; state.LockProgress = 0;
            Broadcast(state.Vehicle.entityId, state.MeleeMode ? 1 : 0, MeleeModeEvent, state.Vehicle.position, Vector3.zero, 0, 0);
            SendStatus(state, now);
        }

        // Blade swings: server-validated arc/radius/occlusion, damage via
        // DamageEntity, knockback from a zero-damage high-BlastPower blast.
        static void MeleeRequest(State state, int actor, byte op, float now)
        {
            if (!state.MeleeMode || now < state.NextMelee || !ReadyOperator(state, actor)) return;
            bool heavy = op == MeleeHeavy;
            float radius = heavy ? Rules.HeavyRadius : Rules.SweepRadius;
            float damage = heavy ? Rules.HeavyDamage : Rules.SweepDamage;
            var forward = BodyRotation(state.Vehicle) * Vector3.forward;
            var origin = state.Vehicle.position + Vector3.up * 1.2f;
            int hits = 0;
            var targets = new List<EntityAlive>(currentWorld.Entities.list.Count);
            foreach (var entity in currentWorld.Entities.list)
            {
                var alive = entity as EntityAlive;
                if (!Hostile(alive)) continue;
                var center = alive.GetPosition() + Vector3.up * .8f;
                var delta = center - origin;
                float reach = radius + .8f;
                if (delta.sqrMagnitude > reach * reach) continue;
                if (heavy && Vector3.Angle(forward, delta.normalized) > Rules.HeavyArcDegrees * .5f) continue;
                // Copy before damage: a killed entity may leave the live list.
                targets.Add(alive);
            }
            foreach (var alive in targets)
            {
                var center = alive.GetPosition() + Vector3.up * .8f;
                var delta = center - origin;
                // Occlusion keeps the blades from cutting through a wall.
                if (delta.sqrMagnitude > 1f && Trace(state.Vehicle, origin, delta.normalized, Mathf.Max(.1f, delta.magnitude - .6f), out var barrier) &&
                    ItemActionAttack.FindHitEntity(barrier) != alive) continue;
                var source = new DamageSourceEntity(EnumDamageSource.External, EnumDamageTypes.Electrical, actor, delta.normalized)
                { AttackingItem = ItemClass.GetItem(Rules.BeamAmmo, false), canHitSpecialBodyParts = false, DismemberChance = 0 };
                alive.DamageEntity(source, (int)damage, false, 0);
                hits++;
            }
            state.NextMelee = now + (heavy ? Rules.HeavyCooldown : Rules.SweepCooldown);
            state.LastWeaponUse = now;
            var blast = new DynamicProperties(); var explosion = new DynamicProperties();
            blast.Classes.Add("Explosion", explosion);
            var knockback = new ExplosionData(blast, null) { ParticleIndex = 0, BlockRadius = 0,
                EntityRadius = (byte)Mathf.RoundToInt(radius), EntityDamage = 0, BlockDamage = 0,
                BlastPower = (int)(heavy ? Rules.HeavyKnockback : Rules.SweepKnockback) };
            GameManager.Instance.ExplosionServer(origin, new Vector3i(Mathf.FloorToInt(origin.x), Mathf.FloorToInt(origin.y), Mathf.FloorToInt(origin.z)),
                Quaternion.identity, knockback, actor, 0, false, ItemClass.GetItem(Rules.BeamAmmo, false));
            Broadcast(state.Vehicle.entityId, hits, heavy ? MeleeHeavyEvent : MeleeSweepEvent,
                origin, forward, 0, heavy ? Rules.HeavyCooldown : Rules.SweepCooldown);
            SendStatus(state, now);
        }

        static void FireBeam(State state, int actor, float now)
        {
            if (now < state.NextBeam) return;
            if (state.Overheated) return;
            var reason = ResolveBeam(state, state.AimOrigin, state.AimDirection, out var direction);
            if (reason != 0) { state.AimReason = reason; return; }
            state.AimReason = 0;
            if (!Consume(state, Rules.BeamAmmo)) { state.NextBeam = now + .5f; return; }
            state.NextBeam = now + Rules.BeamInterval;
            state.LastWeaponUse = now;
            AddBeamHeat(state);
            var rig = Model.GetRig(state.Vehicle);
            var start = MuzzleWorld(rig, state.Vehicle) + direction * Rules.MuzzleOffset;
            var end = start + direction * Rules.BeamRange;
            if (Trace(state.Vehicle, start, direction, Rules.BeamRange, out var hit))
            {
                end = hit.hit.pos;
                var ammo = ItemClass.GetItem(Rules.BeamAmmo, false);
                float factor=Rules.AttributeScale(state.Vehicle);int packets=DamagePackets(Rules.BeamEntityDamage*factor);
                for(int part=0;part<packets;part++)
                ItemActionAttack.Hit(hit, actor, EnumDamageTypes.Electrical, Rules.BeamBlockDamage*factor/packets,
                    Rules.BeamEntityDamage*factor/packets, 1, 1, 0, .05f, "metal", new DamageMultiplier(), null,
                    new ItemActionAttack.AttackHitInfo(), 0, 1, 1, null, null, ItemActionAttack.EnumAttackMode.RealNoHarvesting,
                    null, -1, ammo);
                for(int part=0;part<DamagePackets(Rules.BeamSplashDamage*factor);part++)
                    GameManager.Instance.ExplosionServer(end, new Vector3i(Mathf.FloorToInt(end.x), Mathf.FloorToInt(end.y), Mathf.FloorToInt(end.z)),
                        Quaternion.identity, BeamExplosion(state.Vehicle,part), actor, 0, false, ItemClass.GetItem(Rules.BeamAmmo, false));
            }
            // Throttled fire audit: matches in-game gunfire against real ammo
            // consumption (10 s cadence, safe to keep enabled).
            if (Time.time >= nextFireAudit)
            {
                nextFireAudit = Time.time + 10f;
                Log.Out("[Mecha] beam v=" + state.Vehicle.entityId + " ammo=" + Ammo(state, Rules.BeamAmmo) + " heat=" + Mathf.RoundToInt(state.Heat));
            }
            Broadcast(state.Vehicle.entityId, unchecked(++serial), BeamEvent, start, end, state.Heat, 0);
        }

        // Native damage messages contain 16-bit fields. Split only damage delivery,
        // while ammo, heat, cooldown and the authored beam event remain once per shot.
        public static int DamagePackets(float total){return Mathf.Max(1,Mathf.CeilToInt(total/60000f));}
        static ExplosionData BeamExplosion(EntityVehicle vehicle,int part)
        {
            var action = new DynamicProperties(); var explosion = new DynamicProperties();
            action.Classes.Add("Explosion", explosion);
            // Horde-clear splash around the impact; zero block damage keeps the
            // walker safe to fire from inside a player-built kill corridor.
            float damage=Rules.BeamSplashDamage*Rules.AttributeScale(vehicle);
            return new ExplosionData(action, null) { ParticleIndex = part==0?5:0, BlockRadius = 0, EntityRadius = (byte)Rules.BeamSplashRadius,
                EntityDamage = damage/DamagePackets(damage), BlockDamage = 0, BlastPower = part==0?20:0 };
        }
        public static void AddBeamHeat(State state)
        {state.Heat=Mathf.Min(100,state.Heat+Rules.BeamHeatPerShot);if(state.Heat>=100)state.Overheated=true;}
        public static void CoolBeam(State state,float dt)
        {state.Heat=Mathf.Max(0,state.Heat-Rules.BeamCooling*Mathf.Max(0,dt));if(state.Overheated&&state.Heat<=Rules.BeamResumeHeat)state.Overheated=false;}

        static ExplosionData BuildMissileExplosion(EntityVehicle vehicle,int part)
        {
            var action = new DynamicProperties(); var explosion = new DynamicProperties();
            action.Classes.Add("Explosion", explosion);
            float factor=Rules.AttributeScale(vehicle),damage=Rules.MissileDamage*factor;int packets=DamagePackets(damage);
            return new ExplosionData(action, null) { ParticleIndex = part==0?5:0, BlockRadius = (byte)Rules.MissileBlockRadius, EntityRadius = (byte)Rules.MissileEntityRadius,
                EntityDamage = damage/packets, BlockDamage = Rules.MissileBlockDamage*factor/packets, BlastPower = part==0?200:0 };
        }

        // Missile lock resolves against whatever living zombie the sight ray hits,
        // then holds for LockSeconds before a shot may leave the pod.
        static byte ResolveMissile(State state, out EntityAlive target)
        {
            target = null;
            if(!ValidSight(state.Vehicle,state.AimOrigin,state.AimDirection)||!InArc(state.Vehicle,state.AimDirection,90f))return 1;
            var direction=state.AimDirection.normalized;
            var origin = state.AimOrigin;
            if (Trace(state.Vehicle, origin, direction, Rules.MissileRange, out var hit))
            {
                var entity = ItemActionAttack.FindHitEntity(hit) as EntityAlive;
                if (Hostile(entity))
                {
                    if(Vector3.Distance(state.Vehicle.position,entity.position)<Rules.MissileMinDistance)return 7;
                    target=entity;
                }
            }
            return target == null ? (byte)4 : (byte)0;
        }

        static void UpdateMissile(State state, float now)
        {
            if (!state.MissileAiming) return;
            state.AimReason = ResolveMissile(state, out var target);
            if (target == null) { state.LockTarget = null; state.LockProgress = 0; return; }
            if (state.LockTarget != target) { state.LockTarget = target; state.LockStarted = now; }
            state.LockProgress = Mathf.Clamp01((now - state.LockStarted) / Rules.MissileLockSeconds);
            if (state.LockProgress < 1) state.AimReason = 5;
        }

        static void FireMissile(State state, int actor, float now)
        {
            if (state.GuidedSpent || now < state.NextMissile) return;
            if (state.LockTarget == null || state.LockTarget.IsDead() || state.LockProgress < 1) return;
            var reason=ResolveMissile(state,out var target);
            if(reason!=0||target!=state.LockTarget){state.AimReason=reason;return;}
            var rig = Model.GetRig(state.Vehicle);
            var mount = rig != null && rig.Backpack != null ? rig.Backpack : null;
            var start = mount != null ? mount.position + Origin.position : state.Vehicle.position + BodyRotation(state.Vehicle) * new Vector3(0, 2.6f, 0);
            var direction=AimFromMuzzle(start,MissileAimPoint(target),state.AimDirection);
            if(Trace(state.Vehicle,start,direction,1f,out var blocked)){state.AimReason=2;return;}
            if (!Consume(state, Rules.MissileAmmo)) return;
            state.NextMissile = now + Rules.MissileCooldown;
            state.GuidedSpent = true;
            state.LastWeaponUse = now;
            var inherited = state.Vehicle.vehicleRB != null ? Vector3.ClampMagnitude(state.Vehicle.vehicleRB.velocity, 40) : Vector3.zero;
            var velocity = direction * Rules.MissileSpeed + inherited;
            var missile = new Missile { Id = unchecked(++serial), VehicleId = state.Vehicle.entityId, Vehicle = state.Vehicle, ShooterId = actor,
                Position = start, Velocity = velocity, Target = state.LockTarget, TargetOffset = MissileAimPoint(target)-target.position };
            missiles.Add(missile);
            Broadcast(state.Vehicle.entityId, missile.Id, MissileSpawnEvent, start, velocity, 0, 0);
        }

        static void Guide(Missile missile, float dt)
        {
            if (missile.Target == null) return;
            if (missile.Target.IsDead() || currentWorld.GetEntity(missile.Target.entityId) != missile.Target) { missile.Target = null; return; }
            var toward = missile.Target.position + missile.TargetOffset - missile.Position;
            if (toward.sqrMagnitude < .01f) return;
            missile.Velocity = Vector3.RotateTowards(missile.Velocity.normalized, toward.normalized,
                Rules.MissileTurnRate * Mathf.Deg2Rad * dt, 0) * missile.Velocity.magnitude;
        }

        static void AdvanceMissiles(float delta)
        {
            for (int i = missiles.Count - 1; i >= 0; i--)
            {
                var missile = missiles[i];
                float dt = Mathf.Min(Mathf.Max(0, delta), Rules.MissileLifetime - missile.Age);
                missile.Age += dt;
                Guide(missile, dt);
                var step = missile.Velocity * dt;
                WorldRayHitInfo impact = null;
                bool hit = step.sqrMagnitude > .00001f && Trace(missile.Vehicle, missile.Position, step.normalized, step.magnitude, out impact);
                if (hit)
                {
                    missiles.RemoveAt(i);
                    var point = impact.hit.pos;
                    Broadcast(missile.VehicleId, missile.Id, ImpactEvent, point, Vector3.zero, 0, 0);
                    for(int part=0;part<DamagePackets(Rules.MissileDamage*Rules.AttributeScale(missile.Vehicle));part++)
                        GameManager.Instance.ExplosionServer(point, new Vector3i(Mathf.FloorToInt(point.x), Mathf.FloorToInt(point.y), Mathf.FloorToInt(point.z)),
                            Quaternion.identity, BuildMissileExplosion(missile.Vehicle,part), missile.ShooterId, 0, false, ItemClass.GetItem(Rules.MissileAmmo, false));
                }
                else if (missile.Age >= Rules.MissileLifetime)
                { missiles.RemoveAt(i); Broadcast(missile.VehicleId, missile.Id, ImpactEvent, missile.Position, Vector3.zero, 0, 0); }
                else
                {
                    missile.Position += step;
                    if (Time.time >= missile.NextSync) { missile.NextSync = Time.time + .1f; Broadcast(missile.VehicleId, missile.Id, MissileMoveEvent, missile.Position, missile.Velocity, 0, 0); }
                }
            }
        }

        static void SendStatus(State state, float now)
        {
            float max = state.Vehicle.vehicle.GetMaxHealth();
            float hull = max > 0 ? Mathf.Clamp01(state.Vehicle.vehicle.GetHealth() / max) : 1f;
            bool hurt = now - state.LastDamage < Rules.RepairIdleSeconds;
            float repair = state.RepairStarted >= 0 ? Mathf.Clamp01((now - state.RepairStarted) / Rules.RepairSeconds) : 0f;
            int flags = (state.Overheated ? 1 : 0) | (state.AimReason == 1 ? 4 : 0) | (state.AimReason == 2 ? 8 : 0) |
                (state.GuidedSpent ? 16 : 0) | (hurt ? 32 : 0) | (state.AimReason==7?128:0) | (state.AimReason==4?256:0);
            Broadcast(state.Vehicle.entityId, flags, StatusEvent,
                new Vector3(Ammo(state, Rules.BeamAmmo), Ammo(state, Rules.MissileAmmo), Mathf.Max(0, state.NextMissile - now)),
                new Vector3(state.Heat, hull, repair), state.LockProgress, Mathf.Max(0, state.NextBattleRepair - now));
        }

        public static void Update()
        {
            if (!enabled) return;
            try
            {
                var game = GameManager.Instance; var world = game != null ? game.World : null;
                if (world == null) { if (currentWorld != null) Clear(); return; }
                EnsureWorld(world);
                if (game.IsPaused()) return;
                remove.Clear();
                foreach (var pair in states)
                {
                    var state = pair.Value;
                    if (state.Vehicle == null || world.GetEntity(pair.Key) != state.Vehicle) { remove.Add(pair.Key); continue; }
                    if (Server)
                    {
                        CoolBeam(state,Time.deltaTime);
                        // Repair runs whether or not anyone is seated; its state
                        // machine owns its own trigger lease and eligibility.
                        MechaArmor.UpdateRepair(world, state, Time.time);
                        var lease = GetLease(pair.Key);
                        int actor = state.Vehicle.GetAttached(0) != null ? state.Vehicle.GetAttached(0).entityId : -1;
                        bool session = state.Aiming || state.MissileAiming;
                        if (!session || actor != lease.Actor || Time.time >= lease.Until || !ReadyOperator(state, actor))
                        {
                            state.Aiming = false; state.MissileAiming = false;
                            state.TriggerHeld = false; state.MissileTrigger = false;
                            state.LockTarget = null; state.LockProgress = 0;
                        }
                        else
                        {
                            // Lock accumulation follows the sight session; only a
                            // pressed trigger ever consumes ammo or emits events.
                            if (state.MissileAiming) UpdateMissile(state, Time.time);
                            if (state.TriggerHeld && state.Aiming) FireBeam(state, actor, Time.time);
                            else if (state.MissileTrigger && state.MissileAiming) FireMissile(state, actor, Time.time);
                            if (!state.MissileTrigger) state.GuidedSpent = false;
                        }
                        bool occupied = state.Vehicle.GetAttached(0) != null;
                        if ((occupied || state.RepairStarted >= 0) && Time.time >= state.NextStatus)
                        { state.NextStatus = Time.time + .2f; SendStatus(state, Time.time); }
                    }
                }
                foreach (int id in remove) { states.Remove(id); leases.Remove(id); }
                if (Server) AdvanceMissiles(Time.deltaTime);
                Deploy.Update(world);
                Boarding.Update(world);
                Locomotion.Tick(world);
                RobotAudio.Cleanup(world);
                CrewVisibility.Update(world);
                VisualTick(world, Time.deltaTime);
                MechaFX.Update(Time.deltaTime);
                LocalInput(world);
            }
            catch (Exception ex)
            {
                if (Time.time >= nextError) { nextError = Time.time + 10; Log.Error("[Mecha] " + ex); }
            }
        }

        static void VisualTick(World world, float dt)
        {
            foreach (var entity in world.Entities.list)
            {
                var vehicle = entity as EntityVehicle;
                if (!IsMecha(vehicle)) continue;
                var rig = Model.GetRig(vehicle);
                if (rig == null) continue;
                // Gait owns the deploy crouch internally via Deploy.Progress.
                Gait.Update(world, vehicle, rig, dt);
            }
        }

        static void ReleaseInput(World world, EntityPlayerLocal player)
        {
            var previous = world.GetEntity(inputVehicle) as EntityVehicle;
            if (player != null && IsMecha(previous)) SendIntent(player, previous, Stop, Vector3.forward, previous.position);
            inputHeld = inputMissileHeld = false;
            Locomotion.ReleaseInput();
        }

        static void LocalInput(World world)
        {
            var player = world.GetPrimaryPlayer();
            if (player != null && Input.GetKeyDown(KeyCode.F9))
            {
                EntityVehicle closest = null; float best = 25f;
                foreach (var entity in world.Entities.list)
                {
                    var candidate = entity as EntityVehicle;
                    if (!IsMecha(candidate)) continue;
                    float distance = (candidate.position - player.position).sqrMagnitude;
                    if (distance < best) { best = distance; closest = candidate; }
                }
                if (closest != null) MechaDebug.Dump(world, closest);
            }
            var vehicle = player != null ? player.AttachedToEntity as EntityVehicle : null;
            if (!IsMecha(vehicle) || player.IsDead())
            { ReleaseInput(world, player); inputVehicle = -1; Optics.Clear(); RepairInput(world, player); return; }
            var attached = vehicle.GetAttached(0);
            if (attached == null || attached.entityId != player.entityId) { ReleaseInput(world, player); Optics.Clear(); return; }
            if (inputVehicle != vehicle.entityId)
            { ReleaseInput(world, player); inputVehicle = vehicle.entityId; nextInput = Time.time + .25f; nextAim = 0; }
            if (!UIReady(player))
            { ReleaseInput(world, player); nextInput = Time.time + .25f; return; }
            Optics.UpdateInput(player, vehicle);
            if(Boarding.Active(vehicle))
            {
                ReleaseInput(world,player);
                if(Input.GetKeyDown(KeyCode.Space)) { SendIntent(player,vehicle,SkipBoard,Vector3.zero,Vector3.zero); Boarding.SkipLocal(vehicle); }
                return;
            }

            Locomotion.FeedInput(
                Input.GetKeyDown(Rules.Key(vehicle, "pzMechaHoverKey", KeyCode.Q)),
                Input.GetKey(Rules.Key(vehicle, "pzMechaDescendKey", KeyCode.C)),
                Input.GetKey(Rules.Key(vehicle, "pzMechaJumpKey", KeyCode.Space)));
            if (Time.time >= nextInput && Input.GetKeyDown(Rules.Key(vehicle, "pzMechaBattleRepairKey", KeyCode.R)))
                SendIntent(player, vehicle, BattleRepair, Vector3.forward, vehicle.position);
            var beamKey = Rules.Key(vehicle, "pzMechaBeamKey", KeyCode.Mouse0);
            bool beamHeld = Time.time >= nextInput && Input.GetKey(beamKey);
            bool missileHeld = Time.time >= nextInput && Input.GetKey(Rules.Key(vehicle, "pzMechaMissileKey", KeyCode.G));
            Ray sight;
            if (!Optics.TryRay(out sight)) sight = new Ray(vehicle.position, BodyRotation(vehicle) * Vector3.forward);
            // The beam claims the trigger while held; the missile pod is the
            // alternate hold and never fires in the same packet as the beam.
            byte op = beamHeld ? Fire : missileHeld ? MissileFire : inputHeld || inputMissileHeld ? Stop : Aim;
            if (beamHeld != inputHeld || missileHeld != inputMissileHeld || Time.time >= nextAim)
            {
                nextAim = Time.time + .1f;
                SendIntent(player, vehicle, op, sight.direction, sight.origin);
                inputHeld = beamHeld; inputMissileHeld = missileHeld;
            }
        }

        static int repairVehicleId = -1;
        static bool repairHeld;
        static float nextRepairAim;

        // Outside-the-walker repair input: aim at the parked machine within
        // RepairRange and hold the configured key. Sends edge transitions plus
        // a 4 Hz keep-alive while held; the server owns every eligibility check.
        static void RepairInput(World world, EntityPlayerLocal player)
        {
            bool held = false; EntityVehicle target = null; var ray = default(Ray);
            if (player != null && !player.IsDead() && player.AttachedToEntity == null && UIReady(player))
            {
                ray = player.GetLookRay();
                if (Voxel.Raycast(world, ray, Rules.RepairRange, -538750997, 8, 0f))
                {
                    target = ItemActionAttack.FindHitEntity(Voxel.voxelRayHitInfo) as EntityVehicle;
                    if (!IsMecha(target)) target = null;
                }
                if (target != null) held = Input.GetKey(Rules.Key(target, "pzMechaRepairKey", KeyCode.F));
            }
            if (held != repairHeld || held && Time.time >= nextRepairAim)
            {
                nextRepairAim = Time.time + .25f;
                var vehicle = target != null ? target : world.GetEntity(repairVehicleId) as EntityVehicle;
                if (IsMecha(vehicle) && player != null)
                {
                    byte op = held ? Repair : RepairStop;
                    int sequence = unchecked(++inputSequence);
                    if (Server) MechaArmor.RepairRequest(world, player.entityId, GetState(vehicle), op, ray.direction, ray.origin, sequence);
                    else ConnectionManager.Instance.SendToServer(NetPackageManager.GetPackage<NetPackagePZAECMechaIntent>().Setup(vehicle.entityId, op, ray.direction, ray.origin, sequence));
                }
            }
            repairHeld = held;
            repairVehicleId = target != null ? target.entityId : -1;
        }

        static void SendIntent(EntityPlayerLocal player, EntityVehicle vehicle, byte op, Vector3 direction, Vector3 origin)
        {
            int sequence = unchecked(++inputSequence);
            if (Server) Request(currentWorld, player.entityId, vehicle.entityId, op, direction, origin, sequence);
            else ConnectionManager.Instance.SendToServer(NetPackageManager.GetPackage<NetPackagePZAECMechaIntent>().Setup(vehicle.entityId, op, direction, origin, sequence));
        }

        public static void Broadcast(int vehicle, int id, byte kind, Vector3 a, Vector3 b, float value, float c)
        {
            var package = NetPackageManager.GetPackage<NetPackagePZAECMechaEvent>().Setup(vehicle, id, kind, a, b, value, c);
            ConnectionManager.Instance.SendPackage(package, false, -1, -1, vehicle, null, 512);
            try { if (currentWorld != null && currentWorld.GetPrimaryPlayer() != null) MechaFX.Receive(currentWorld, vehicle, id, kind, a, b, value, c); }
            catch (Exception ex) { if (Time.time >= nextError) { nextError = Time.time + 10; Log.Warning("[Mecha] FX: " + ex.Message); } }
        }
    }
}
