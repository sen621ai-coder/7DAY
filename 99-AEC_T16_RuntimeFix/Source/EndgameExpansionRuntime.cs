using System;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace AECT16RuntimeFix
{
    /// <summary>
    /// Server-authoritative behavior which cannot be represented safely by XML.
    /// XML still owns item stats, recipes, buffs and block inheritance.
    /// </summary>
    public static class EndgameExpansionRuntime
    {
        private sealed class AnchorState
        {
            public Vector3 Position;
            public float Expires;
        }

        private sealed class DeviceState
        {
            public int Tier;
            public float NextUse;
            public float Heat;
            public float LastHeatTime;
        }

        private static readonly Dictionary<int, AnchorState> Anchors = new Dictionary<int, AnchorState>();
        private static readonly Dictionary<Vector3i, DeviceState> RepairStations = new Dictionary<Vector3i, DeviceState>();
        private static readonly Dictionary<Vector3i, DeviceState> SkyguardArrays = new Dictionary<Vector3i, DeviceState>();
        private static readonly Dictionary<Vector3i, DeviceState> DecoyTowers = new Dictionary<Vector3i, DeviceState>();
        private static readonly Dictionary<Vector3i, float> ReactiveCooldowns = new Dictionary<Vector3i, float>();
        private static readonly Dictionary<Vector3i, float> RepairTargetCooldowns = new Dictionary<Vector3i, float>();
        private static readonly Dictionary<int, float> NextPlayerHeatUpdate = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> NextEnemyDiversionUpdate = new Dictionary<int, float>();
        private static readonly FastTags<TagGroup.Global> ExplosiveTags = FastTags<TagGroup.Global>.Parse("explosive,rocket");

        public static void Install(Harmony harmony)
        {
            var explosion = AccessTools.Method(typeof(GameManager), "explode");
            var damage = AccessTools.Method(typeof(Block), "DamageBlock", new[]
            {
                typeof(WorldBase), typeof(BlockValueRef), typeof(BlockValue), typeof(int), typeof(int),
                typeof(ItemActionAttack.AttackHitInfo), typeof(bool), typeof(bool)
            });
            var loaded = AccessTools.Method(typeof(Block), "OnBlockLoaded", new[] { typeof(WorldBase), typeof(Vector3i), typeof(BlockValue) });
            var added = AccessTools.Method(typeof(Block), "OnBlockAdded", new[] { typeof(WorldBase), typeof(Chunk), typeof(Vector3i), typeof(BlockValue), typeof(PlatformUserIdentifierAbs) });
            var removed = AccessTools.Method(typeof(Block), "OnBlockRemoved", new[] { typeof(WorldBase), typeof(Chunk), typeof(Vector3i), typeof(BlockValue) });
            var projectileUpdate = AccessTools.Method(typeof(ProjectileMoveScript), "FixedUpdate", Type.EmptyTypes);
            var playerUpdate = AccessTools.Method(typeof(EntityPlayer), "OnUpdateLive", Type.EmptyTypes);
            var enemyUpdate = AccessTools.Method(typeof(EntityAlive), "updateTasks", Type.EmptyTypes);
            var damageEntity = AccessTools.Method(typeof(EntityAlive), "DamageEntity", new[] { typeof(DamageSource), typeof(int), typeof(bool), typeof(float) });
            if (explosion == null || damage == null || loaded == null || added == null || removed == null ||
                projectileUpdate == null || playerUpdate == null || enemyUpdate == null || damageEntity == null)
            {
                throw new MissingMethodException("Endgame expansion runtime targets changed.");
            }

            harmony.Patch(explosion, postfix: new HarmonyMethod(typeof(EndgameExpansionRuntime), nameof(JammerExplosionPostfix)));
            harmony.Patch(damage,
                prefix: new HarmonyMethod(typeof(EndgameExpansionRuntime), nameof(DamageBlockPrefix)),
                postfix: new HarmonyMethod(typeof(EndgameExpansionRuntime), nameof(DamageBlockPostfix)));
            harmony.Patch(loaded, postfix: new HarmonyMethod(typeof(EndgameExpansionRuntime), nameof(BlockLoadedPostfix)));
            harmony.Patch(added, postfix: new HarmonyMethod(typeof(EndgameExpansionRuntime), nameof(BlockAddedPostfix)));
            harmony.Patch(removed, postfix: new HarmonyMethod(typeof(EndgameExpansionRuntime), nameof(BlockRemovedPostfix)));
            harmony.Patch(projectileUpdate, prefix: new HarmonyMethod(typeof(EndgameExpansionRuntime), nameof(ProjectileUpdatePrefix)));
            harmony.Patch(playerUpdate, postfix: new HarmonyMethod(typeof(EndgameExpansionRuntime), nameof(PlayerUpdatePostfix)));
            harmony.Patch(enemyUpdate, postfix: new HarmonyMethod(typeof(EndgameExpansionRuntime), nameof(EnemyUpdatePostfix)));
            harmony.Patch(damageEntity, postfix: new HarmonyMethod(typeof(EndgameExpansionRuntime), nameof(DamageEntityPostfix)));
        }

        public static bool CanUseFieldItem(EntityPlayer player, ItemValue value, out string reason)
        {
            reason = "当前无法使用。";
            if (player == null || player.world == null || player.IsDead()) return false;
            string name = value?.ItemClass?.GetItemName() ?? "";
            if (name.StartsWith("itemPZAECFieldRepairKitT", StringComparison.Ordinal))
            {
                reason = "没有需要维修的已穿护甲；维修包未消耗。";
                if (player.equipment == null) return false;
                foreach (var armor in player.equipment.GetItems())
                    if (armor != null && !armor.IsEmpty() && armor.MaxUseTimes > 0 && armor.UseTimes > 0) return true;
                return false;
            }
            if (name == "itemPZAECResonanceInjector")
            {
                reason = "需要同阶三件套且共鸣未满；注射剂未消耗。";
                foreach (string family in new[] { "Harrier", "Storm", "Tremor", "Warden" })
                for (int tier = 16; tier <= 19; tier++)
                    if (player.Buffs.HasBuff("buffPZAEC" + family + "T" + tier + "Set3") &&
                        player.Buffs.GetCustomVar("$PZAEC" + family + "T" + tier + "Resonance") < 100) return true;
                return false;
            }
            if (name == "itemPZAECQuickArmorGel")
            {
                reason = "请对准6米内受损建筑，且不能在商人保护区使用；装甲胶未消耗。";
                if (!Voxel.Raycast(player.world, player.GetLookRay(), 6f, true, false)) return false;
                var hit = Voxel.voxelRayHitInfo;
                if (!hit.bHitValid || hit.tag != "B_Mesh" || !BlockValueRef.Create(hit).TryGetBlockPos(out var pos) || player.world.IsWithinTraderArea(pos)) return false;
                var block = player.world.GetBlock(pos);
                return !block.isair && !block.isTerrain && block.damage > 0;
            }
            if (name == "itemPZAECEvacAnchor")
            {
                reason = "请下车并离开商人保护区后使用撤离锚。";
                return player.AttachedMainEntity == null && !player.world.IsWithinTraderArea(new Vector3i(player.position));
            }
            return true;
        }

        public static void UseFieldItem(EntityPlayer player, ItemValue itemValue)
        {
            try
            {
                if (itemValue == null || itemValue.IsEmpty() || itemValue.ItemClass == null)
                {
                    return;
                }

                if (player == null || player.world == null || player.IsDead())
                {
                    return;
                }

                string itemName = itemValue.ItemClass.GetItemName();
                if (itemName == "itemPZAECQuickArmorGel")
                {
                    ApplyArmorGel(player);
                }
                else if (itemName == "itemPZAECResonanceInjector")
                {
                    ApplyResonance(player);
                }
                else if (itemName == "itemPZAECDecoyBeacon")
                {
                    ApplyDecoy(player);
                }
                else if (itemName == "itemPZAECEvacAnchor")
                {
                    UseEvacAnchor(player);
                }
                else if (itemName != null && itemName.StartsWith("itemPZAECFieldRepairKitT", StringComparison.Ordinal))
                {
                    ApplyFieldRepair(player, ParseTier(itemName));
                }
            }
            catch (Exception ex)
            {
                T16RuntimeFixMod.SafeLog("[AEC-Endgame] Field item failed safely: " + ex.GetBaseException().Message);
            }
        }

        private static void ApplyArmorGel(EntityPlayer player)
        {
            Ray ray = player.GetLookRay();
            if (!Voxel.Raycast(player.world, ray, 6f, true, false))
            {
                return;
            }
            var hit = Voxel.voxelRayHitInfo;
            if (!hit.bHitValid || hit.tag != "B_Mesh" || !BlockValueRef.Create(hit).TryGetBlockPos(out var pos) ||
                player.world.IsWithinTraderArea(pos))
            {
                return;
            }
            BlockValue value = player.world.GetBlock(pos);
            if (value.isair || value.isTerrain || value.damage <= 0)
            {
                return;
            }
            int tier = TierForPlayer(player);
            int repair = tier == 19 ? 3400 : tier == 18 ? 2600 : tier == 17 ? 2000 : 1500;
            value.damage = Math.Max(0, value.damage - repair);
            player.world.SetBlockRPC(new BlockValueRef(pos), value);
        }

        private static void ApplyResonance(EntityPlayer player)
        {
            foreach (string setName in new[] { "Harrier", "Storm", "Tremor", "Warden" })
            {
                foreach (int tier in new[] { 16, 17, 18, 19 })
                {
                    if (!player.Buffs.HasBuff("buffPZAEC" + setName + "T" + tier + "Set3"))
                    {
                        continue;
                    }
                    string cvar = "$PZAEC" + setName + "T" + tier + "Resonance";
                    float next = Math.Min(100f, player.Buffs.GetCustomVar(cvar) + 25f);
                    player.Buffs.SetCustomVar(cvar, next, true, CVarOperation.set);
                    if (next >= 100f)
                    {
                        player.Buffs.AddBuff("buffPZAEC" + setName + "T" + tier + "Ready", player.entityId, true);
                    }
                    return;
                }
            }
        }

        private static void ApplyDecoy(EntityPlayer player)
        {
            foreach (var entity in player.world.Entities.list)
            {
                var enemy = entity as EntityAlive;
                var entityClass = enemy == null ? null : EntityClass.GetEntityClass(enemy.entityClass);
                if (enemy == null || enemy == player || enemy.IsDead() || entityClass == null || !entityClass.bIsEnemyEntity)
                {
                    continue;
                }
                string name = EntityClass.GetEntityClassName(enemy.entityClass) ?? string.Empty;
                if (BloodMoonSiege.Tier(name) != 0 || name.IndexOf("Boss", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (enemy.position - player.position).sqrMagnitude > 25f * 25f)
                {
                    continue;
                }
                enemy.SetAttackTarget(null, 0);
                enemy.SetInvestigatePosition(player.position, 300, true);
            }
        }

        public static void JammerExplosionPostfix(Vector3 _worldPos, int _entityId, ItemValue _itemValueExplosionSource)
        {
            try
            {
                var world = GameManager.Instance == null ? null : GameManager.Instance.World;
                string name = _itemValueExplosionSource?.ItemClass?.GetItemName();
                if (world == null || world.IsRemote() || name == null || !name.StartsWith("thrownPZAECCounterJammerT", StringComparison.Ordinal)) return;
                ApplyJammer(world, _worldPos, _entityId, ParseTier(name));
            }
            catch (Exception ex)
            {
                T16RuntimeFixMod.SafeLog("[AEC-Endgame] Jammer failed: " + ex.GetBaseException().Message);
            }
        }

        public static bool IsJammerTarget(EntityAlive enemy)
        {
            if (enemy == null || enemy is EntityPlayer || enemy.IsDead()) return false;
            var definition = EntityClass.GetEntityClass(enemy.entityClass);
            return definition != null && definition.bIsEnemyEntity;
        }

        public static void ApplyJammer(World world, Vector3 position, int sourceId, int tier)
        {
            if (tier < 16 || tier > 19)
            {
                return;
            }
            string buff = "buffPZAECCounterJammerT" + tier;
            foreach (var entity in world.Entities.list)
            {
                var enemy = entity as EntityAlive;
                if (!IsJammerTarget(enemy) || (enemy.position - position).sqrMagnitude > 8f * 8f)
                {
                    continue;
                }
                enemy.Buffs.AddBuff(buff, sourceId, true);
                enemy.bodyDamage.CurrentStun = EnumEntityStunType.Stumble;
            }
        }

        private static void ApplyFieldRepair(EntityPlayer player, int tier)
        {
            float fraction = tier == 19 ? .65f : tier == 18 ? .55f : tier == 17 ? .45f : .35f;
            ItemValue[] equipped = player.equipment == null ? null : player.equipment.GetItems();
            if (equipped == null)
            {
                return;
            }
            int bestSlot = -1;
            float bestDamage = 0f;
            for (int i = 0; i < equipped.Length; i++)
            {
                ItemValue value = equipped[i];
                if (value == null || value.IsEmpty() || value.MaxUseTimes <= 0 || value.UseTimes <= bestDamage)
                {
                    continue;
                }
                bestSlot = i;
                bestDamage = value.UseTimes;
            }
            if (bestSlot < 0)
            {
                return;
            }
            ItemValue repaired = equipped[bestSlot];
            repaired.UseTimes = Math.Max(0f, repaired.UseTimes - repaired.MaxUseTimes * fraction);
            player.equipment.SetSlotItem(bestSlot, repaired, true);
        }

        private static void ApplyCalibration(EntityPlayer player, ItemValue helmet, string family)
        {
            if (helmet == null || helmet.Modifications == null)
            {
                return;
            }
            foreach (ItemValue modification in helmet.Modifications)
            {
                if (modification == null || modification.IsEmpty() || modification.ItemClass == null)
                {
                    continue;
                }
                string name = modification.ItemClass.GetItemName();
                if (name == "modPZAEC" + family + "StableT19" || name == "modPZAEC" + family + "OverloadT19")
                {
                    string stem = name.Substring("modPZAEC".Length, name.Length - "modPZAEC".Length - "T19".Length);
                    player.Buffs.AddBuff("buffPZAEC" + stem + "CalibrationT19", player.entityId, true);
                    return;
                }
            }
        }

        public static void UpdateAutomaticResonance(EntityPlayer player)
        {
            if (player == null || player.world == null || player.world.IsRemote() || player.IsDead() || player.equipment == null) return;
            foreach (string family in new[] { "Harrier", "Storm", "Tremor", "Warden" })
            foreach (int tier in new[] {16,17,18,19})
            {
                string prefix = "buffPZAEC" + family + "T" + tier;
                string charge = "$PZAEC" + family + "T" + tier + "Resonance";
                int pieces = player.equipment.GetArmorGroupCount("groupPZAEC" + family + "T" + tier);
                if (pieces < 3)
                {
                    if (player.Buffs.GetCustomVar(charge) != 0) player.Buffs.SetCustomVar(charge, 0, true, CVarOperation.set);
                    if (player.Buffs.HasBuff(prefix + "Ready")) player.Buffs.RemoveBuff(prefix + "Ready", -1, true);
                }
                if (pieces < 4)
                {
                    if (player.Buffs.HasBuff(prefix + "Active")) player.Buffs.RemoveBuff(prefix + "Active", -1, true);
                    if (tier == 19)
                    foreach (string mode in new[] {"Stable", "Overload"})
                    {
                        string calibration = "buffPZAEC" + family + mode + "CalibrationT19";
                        if (player.Buffs.HasBuff(calibration)) player.Buffs.RemoveBuff(calibration, -1, true);
                    }
                    continue;
                }
                if (player.Buffs.GetCustomVar(charge) < 100 || player.Buffs.HasBuff(prefix + "Cooldown") || player.Buffs.HasBuff(prefix + "Active")) continue;
                // Commit the charge/cooldown before applying effects so repeated
                // updates cannot consume or activate the same charge twice.
                player.Buffs.SetCustomVar(charge, 0, true, CVarOperation.set);
                player.Buffs.RemoveBuff(prefix + "Ready", -1, true);
                player.Buffs.AddBuff(prefix + "Cooldown", player.entityId, true);
                player.Buffs.AddBuff(prefix + "Active", player.entityId, true);
                if (tier == 19)
                {
                    var helmet = player.equipment.GetItems().FirstOrDefault(v => v != null && v.ItemClass != null && v.ItemClass.GetItemName() == "armorPZAEC" + family + "HelmetT19");
                    ApplyCalibration(player, helmet, family);
                }
            }
        }

        private static void UseEvacAnchor(EntityPlayer player)
        {
            float now = Time.time;
            if (Anchors.TryGetValue(player.entityId, out var state) && state != null && now <= state.Expires)
            {
                Vector3i destination = new Vector3i(state.Position);
                if (player.AttachedMainEntity == null && !player.world.IsWithinTraderArea(destination) &&
                    player.world.GetBlock(destination).isair && player.world.GetBlock(destination + Vector3i.up).isair)
                {
                    player.Teleport(state.Position, player.rotation.y);
                }
                Anchors.Remove(player.entityId);
                return;
            }
            if (player.AttachedMainEntity != null || player.world.IsWithinTraderArea(new Vector3i(player.position)))
            {
                return;
            }
            Anchors[player.entityId] = new AnchorState { Position = player.position, Expires = now + 20f };
        }

        public static void DamageBlockPrefix(BlockValueRef _blockValueRef, BlockValue _blockValue,
            ItemActionAttack.AttackHitInfo _attackHitInfo, ref int _damagePoints)
        {
            try
            {
                string name = _blockValue.Block == null ? string.Empty : _blockValue.Block.GetBlockName();
                if (name.StartsWith("PZAECBlastGateT", StringComparison.Ordinal) && _attackHitInfo != null &&
                    _attackHitInfo.WeaponTypeTag.Test_AnySet(ExplosiveTags))
                {
                    _damagePoints = Mathf.Max(1, Mathf.RoundToInt(_damagePoints * .75f));
                }
                if (!name.StartsWith("PZAECReactiveWallT", StringComparison.Ordinal) || _damagePoints <= 800)
                {
                    return;
                }
                float now = Time.time;
                if (ReactiveCooldowns.TryGetValue(_blockValueRef.BlockPosition, out float ready) && now < ready)
                {
                    return;
                }
                int tier = ParseTier(name);
                float reduction = tier == 19 ? .30f : tier == 18 ? .26f : tier == 17 ? .22f : .18f;
                _damagePoints = 800 + Mathf.RoundToInt((_damagePoints - 800) * (1f - reduction));
                ReactiveCooldowns[_blockValueRef.BlockPosition] = now + 12f;
            }
            catch
            {
                // Block damage must always continue through the native path.
            }
        }

        public static void DamageBlockPostfix(WorldBase _world, BlockValueRef _blockValueRef)
        {
            try
            {
                if (_world == null || _world.IsRemote() || RepairStations.Count == 0)
                {
                    return;
                }
                Vector3i target = _blockValueRef.BlockPosition;
                float now = Time.time;
                if (RepairTargetCooldowns.TryGetValue(target, out float targetReady) && now < targetReady)
                {
                    return;
                }
                foreach (var pair in new List<KeyValuePair<Vector3i, DeviceState>>(RepairStations))
                {
                    Vector3i stationPos = pair.Key;
                    DeviceState station = pair.Value;
                    int range = station.Tier == 19 ? 16 : station.Tier == 18 ? 14 : station.Tier == 17 ? 12 : 10;
                    int dx = stationPos.x - target.x;
                    int dy = stationPos.y - target.y;
                    int dz = stationPos.z - target.z;
                    if (station.NextUse > now || dx * dx + dy * dy + dz * dz > range * range)
                    {
                        continue;
                    }
                    BlockValue stationValue = _world.GetBlock(stationPos);
                    if (stationValue.Block == null || !stationValue.Block.GetBlockName().StartsWith("PZAECHiveRepairStationT", StringComparison.Ordinal))
                    {
                        RepairStations.Remove(stationPos);
                        continue;
                    }
                    var powered = _world.GetTileEntity(stationPos) as TileEntityPowered;
                    if (powered == null || !powered.IsPowered)
                    {
                        continue;
                    }
                    BlockValue value = _world.GetBlock(target);
                    if (value.isair || value.isTerrain || value.damage <= 0 || target == stationPos)
                    {
                        return;
                    }
                    int amount = station.Tier == 19 ? 560 : station.Tier == 18 ? 430 : station.Tier == 17 ? 330 : 250;
                    value.damage = Math.Max(0, value.damage - amount);
                    _world.SetBlockRPC(new BlockValueRef(target), value);
                    station.NextUse = now + 4f;
                    RepairTargetCooldowns[target] = now + 12f;
                    return;
                }
            }
            catch
            {
                // Automatic repair failure cannot interrupt the damage transaction.
            }
        }

        public static void BlockLoadedPostfix(WorldBase _world, Vector3i _blockPos, BlockValue _blockValue)
        {
            RegisterBlock(_world, _blockPos, _blockValue);
        }

        public static void BlockAddedPostfix(WorldBase _world, Vector3i _blockPos, BlockValue _blockValue)
        {
            RegisterBlock(_world, _blockPos, _blockValue);
        }

        public static void BlockRemovedPostfix(Vector3i _blockPos)
        {
            RepairStations.Remove(_blockPos);
            SkyguardArrays.Remove(_blockPos);
            DecoyTowers.Remove(_blockPos);
            ReactiveCooldowns.Remove(_blockPos);
            RepairTargetCooldowns.Remove(_blockPos);
        }

        private static void RegisterBlock(WorldBase world, Vector3i position, BlockValue value)
        {
            if (world == null || world.IsRemote() || value.Block == null)
            {
                return;
            }
            string name = value.Block.GetBlockName();
            if (name.StartsWith("PZAECHiveRepairStationT", StringComparison.Ordinal))
            {
                RepairStations[position] = new DeviceState { Tier = ParseTier(name) };
            }
            else if (name.StartsWith("PZAECSkyguardArrayT", StringComparison.Ordinal))
            {
                SkyguardArrays[position] = new DeviceState { Tier = ParseTier(name), LastHeatTime = Time.time };
            }
            else if (name.StartsWith("PZAECHoundDecoyTowerT", StringComparison.Ordinal))
            {
                DecoyTowers[position] = new DeviceState { Tier = ParseTier(name) };
            }
        }

        public static bool ProjectileUpdatePrefix(ProjectileMoveScript __instance)
        {
            try
            {
                if (__instance == null || __instance.firingEntity == null || __instance.firingEntity.world == null ||
                    __instance.firingEntity.world.IsRemote() || SkyguardArrays.Count == 0 ||
                    BloodMoonSiege.Tier(EntityClass.GetEntityClassName(__instance.firingEntity.entityClass)) == 0)
                {
                    return true;
                }
                float now = Time.time;
                // Projectile transforms use the floating scene origin; block
                // positions are absolute (same conversion as native Fire).
                Vector3 projectilePosition = __instance.transform.position + Origin.position;
                WorldBase world = __instance.firingEntity.world;
                foreach (var pair in new List<KeyValuePair<Vector3i, DeviceState>>(SkyguardArrays))
                {
                    Vector3i position = pair.Key;
                    DeviceState state = pair.Value;
                    BlockValue value = world.GetBlock(position);
                    if (value.Block == null || !value.Block.GetBlockName().StartsWith("PZAECSkyguardArrayT", StringComparison.Ordinal))
                    {
                        SkyguardArrays.Remove(position);
                        continue;
                    }
                    if (!IsPowered(world, position) || now < state.NextUse)
                    {
                        continue;
                    }
                    int range = state.Tier == 19 ? 40 : state.Tier == 18 ? 36 : state.Tier == 17 ? 32 : 28;
                    if ((projectilePosition - new Vector3(position.x + .5f, position.y + .8f, position.z + .5f)).sqrMagnitude > range * range)
                    {
                        continue;
                    }
                    state.Heat = Math.Max(0f, state.Heat - Math.Max(0f, now - state.LastHeatTime) * 4f);
                    state.LastHeatTime = now;
                    int chance = state.Tier == 19 ? 88 : state.Tier == 18 ? 82 : state.Tier == 17 ? 76 : 70;
                    if (world.GetGameRandom().RandomRange(100) >= chance)
                    {
                        state.NextUse = now + .15f;
                        continue;
                    }
                    state.Heat += state.Tier == 19 ? 9f : state.Tier == 18 ? 10f : state.Tier == 17 ? 11f : 12f;
                    state.NextUse = state.Heat >= 100f ? now + 8f : now + .15f;
                    if (state.Heat >= 100f) state.Heat = 0f;
                    __instance.explosionDisabled = true;
                    UnityEngine.Object.Destroy(__instance.gameObject);
                    return false;
                }
            }
            catch
            {
                // Preserve the projectile when interception cannot be evaluated.
            }
            return true;
        }

        public static void PlayerUpdatePostfix(EntityPlayer __instance)
        {
            try
            {
                if (__instance == null || __instance.world == null || __instance.world.IsRemote() || __instance.inventory == null)
                {
                    return;
                }
                float now = Time.time;
                if (NextPlayerHeatUpdate.TryGetValue(__instance.entityId, out float next) && now < next)
                {
                    return;
                }
                NextPlayerHeatUpdate[__instance.entityId] = now + .5f;
                UpdateAutomaticResonance(__instance);
                ItemClass held = __instance.inventory.holdingItem;
                string name = held == null ? string.Empty : held.GetItemName();
                if (!name.StartsWith("gunPZAECStormReservoirT", StringComparison.Ordinal))
                {
                    return;
                }
                int tier = ParseTier(name);
                string cvar = "$PZAECStormHeatT" + tier;
                float heat = __instance.Buffs.GetCustomVar(cvar);
                if (heat >= 100f)
                {
                    __instance.Buffs.SetCustomVar(cvar, 0f, true, CVarOperation.set);
                    __instance.Buffs.AddBuff("buffPZAECStormOverheatedT" + tier, __instance.entityId, true);
                    return;
                }
                float cooling = tier == 19 ? 12.5f : tier == 18 ? 11f : tier == 17 ? 10f : 9f;
                heat = Math.Max(0f, heat - cooling * .5f);
                __instance.Buffs.SetCustomVar(cvar, heat, true, CVarOperation.set);
            }
            catch
            {
                // Heat management must not interrupt the player update.
            }
        }

        public static void EnemyUpdatePostfix(EntityAlive __instance)
        {
            try
            {
                if (__instance == null || __instance.world == null || __instance.world.IsRemote() || __instance.IsDead() || DecoyTowers.Count == 0)
                {
                    return;
                }
                EntityClass entityClass = EntityClass.GetEntityClass(__instance.entityClass);
                string name = EntityClass.GetEntityClassName(__instance.entityClass) ?? string.Empty;
                if (entityClass == null || !entityClass.bIsEnemyEntity || entityClass.bIsAnimalEntity ||
                    name.IndexOf("Boss", StringComparison.OrdinalIgnoreCase) >= 0 || BloodMoonSiege.Tier(name) != 0)
                {
                    return;
                }
                float now = Time.time;
                if (NextEnemyDiversionUpdate.TryGetValue(__instance.entityId, out float next) && now < next)
                {
                    return;
                }
                NextEnemyDiversionUpdate[__instance.entityId] = now + 2f;
                foreach (var pair in new List<KeyValuePair<Vector3i, DeviceState>>(DecoyTowers))
                {
                    Vector3i position = pair.Key;
                    DeviceState state = pair.Value;
                    BlockValue value = __instance.world.GetBlock(position);
                    if (value.Block == null || !value.Block.GetBlockName().StartsWith("PZAECHoundDecoyTowerT", StringComparison.Ordinal))
                    {
                        DecoyTowers.Remove(position);
                        continue;
                    }
                    int range = state.Tier == 19 ? 35 : state.Tier == 18 ? 31 : state.Tier == 17 ? 28 : 25;
                    Vector3 destination = new Vector3(position.x + .5f, position.y, position.z + .5f);
                    if (!IsPowered(__instance.world, position) || (__instance.position - destination).sqrMagnitude > range * range)
                    {
                        continue;
                    }
                    __instance.SetAttackTarget(null, 0);
                    __instance.SetInvestigatePosition(destination, 120, true);
                    return;
                }
            }
            catch
            {
                // Diversion is optional and must not interrupt native AI.
            }
        }

        public static void DamageEntityPostfix(EntityAlive __instance, DamageSource _damageSource)
        {
            try
            {
                if (__instance == null || __instance.world == null || __instance.world.IsRemote())
                {
                    return;
                }
                BlockValue source = __instance.world.GetBlock(_damageSource.BlockPosition);
                string name = source.Block == null ? string.Empty : source.Block.GetBlockName();
                if (name.StartsWith("PZAECArmorBreakTurretT", StringComparison.Ordinal))
                {
                    int tier = ParseTier(name);
                    __instance.Buffs.AddBuff("buffPZAECArmorBreakT" + tier, _damageSource.BlockPosition, _damageSource.ownerEntityId, true);
                }
            }
            catch
            {
                // Preserve native damage when the source cannot be resolved.
            }
        }

        private static bool IsPowered(WorldBase world, Vector3i position)
        {
            var powered = world.GetTileEntity(position) as TileEntityPowered;
            return powered != null && powered.IsPowered;
        }

        private static int TierForPlayer(EntityPlayer player)
        {
            int tier = BloodMoonSiege.TierForGameStage(player.gameStage);
            return tier == 0 ? 16 : tier;
        }

        private static int ParseTier(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length < 2)
            {
                return 0;
            }
            return int.TryParse(name.Substring(name.Length - 2), out int tier) ? tier : 0;
        }
    }
}
