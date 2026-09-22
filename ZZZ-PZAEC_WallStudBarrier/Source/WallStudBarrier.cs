using System;
using HarmonyLib;
using UnityEngine;

namespace PZAEC.WallStudBarrier
{
    public static class Barrier
    {
        public static bool Enemy(Entity entity)
        {
            if (!(entity is EntityAlive) || entity is EntityPlayer) return false;
            EntityClass definition;
            return EntityClass.list.TryGetValue(entity.entityClass, out definition) && definition.bIsEnemyEntity;
        }
        public static bool Stud(BlockValue value)
        {
            var block = value.Block;
            return block != null && string.Equals(block.GetAutoShapeShapeName(), "wallStud", StringComparison.OrdinalIgnoreCase);
        }
        // Traverse whole voxel cells deliberately: the stud is a rejection barrier,
        // so attacks cannot pass through the visual gaps of its mesh.
        public static bool Crosses(Vector3 start, Vector3 end, Func<int,int,int,bool> blocked)
        {
            Vector3 delta = end - start;
            if (delta.sqrMagnitude > 128 * 128) return false;
            int x = Mathf.FloorToInt(start.x), y = Mathf.FloorToInt(start.y), z = Mathf.FloorToInt(start.z);
            int ex = Mathf.FloorToInt(end.x), ey = Mathf.FloorToInt(end.y), ez = Mathf.FloorToInt(end.z);
            int sx = Math.Sign(delta.x), sy = Math.Sign(delta.y), sz = Math.Sign(delta.z);
            float dx = sx == 0 ? float.PositiveInfinity : Math.Abs(1 / delta.x);
            float dy = sy == 0 ? float.PositiveInfinity : Math.Abs(1 / delta.y);
            float dz = sz == 0 ? float.PositiveInfinity : Math.Abs(1 / delta.z);
            float tx = sx == 0 ? float.PositiveInfinity : ((sx > 0 ? x + 1 : x) - start.x) / delta.x;
            float ty = sy == 0 ? float.PositiveInfinity : ((sy > 0 ? y + 1 : y) - start.y) / delta.y;
            float tz = sz == 0 ? float.PositiveInfinity : ((sz > 0 ? z + 1 : z) - start.z) / delta.z;
            for (int i = 0; i < 400; i++)
            {
                if (blocked(x,y,z)) return true;
                if (x == ex && y == ey && z == ez) return false;
                if (tx <= ty && tx <= tz) { x += sx; tx += dx; }
                else if (ty <= tz) { y += sy; ty += dy; }
                else { z += sz; tz += dz; }
            }
            return false;
        }
        public static bool Protected(WorldBase world, EntityAlive enemy, Vector3i target)
        {
            if (world == null || !Enemy(enemy)) return false;
            // The stud itself can be damaged; only a target behind it is shielded.
            if (Stud(world.GetBlock(target))) return false;
            var start = enemy.position + new Vector3(0, Mathf.Clamp(enemy.physicsHeight * .5f, .2f, 1.5f), 0);
            var end = new Vector3(target.x + .5f, target.y + .5f, target.z + .5f);
            return Crosses(start,end,(x,y,z) => Stud(world.GetBlock(x,y,z)));
        }
        public static bool BlockedHit(EntityAlive enemy)
        {
            if (!Enemy(enemy) || enemy.world == null || enemy.moveHelper == null) return false;
            var hit = enemy.moveHelper.HitInfo;
            return hit != null && hit.bHitValid && AvoidTarget(enemy.world, enemy, hit.hit.blockPos);
        }
        public static bool AvoidTarget(WorldBase world, EntityAlive enemy, Vector3i target)
        {
            // AI target refusal is independent of damage resistance. Even old
            // demolition tasks cannot choose a stud while a detour is pending.
            return world != null && Enemy(enemy) &&
                (Stud(world.GetBlock(target)) || Protected(world, enemy, target));
        }
        public static void CanBreakPostfix(EntityAlive ___theEntity, ref bool __result)
        { if (__result && (Recovery.CoolingDown(___theEntity) || BlockedHit(___theEntity))) __result = false; }
        public static bool BreakPrefix(EntityAlive ___theEntity) { return !BlockedHit(___theEntity); }
        public static bool AttackPrefix(EntityAlive __instance, ref bool __result)
        {
            // A stale obstacle hit must not veto normal entity combat.
            if (!__instance.IsBreakingBlocks || !BlockedHit(__instance)) return true;
            __result = false; return false;
        }
        public static void FindDestroyPostfix(EntityAlive ___entity, Vector3 __0, ref bool __result)
        {
            if (__result && Enemy(___entity) && AvoidTarget(___entity.world, ___entity,
                new Vector3i(Mathf.FloorToInt(__0.x),Mathf.FloorToInt(__0.y),Mathf.FloorToInt(__0.z)))) __result = false;
        }
        public static int ReducedDamage(int damage)
        {
            // Round positive damage up: weak hits must not become absolute immunity.
            return damage <= 0 ? damage : damage / 5 + (damage % 5 == 0 ? 0 : 1);
        }
        public static void ExplosionResistancePostfix(Block __instance, ref float __result)
        {
            if (string.Equals(__instance.GetAutoShapeShapeName(), "wallStud", StringComparison.OrdinalIgnoreCase))
                __result = 1f - (1f - __result) * .2f;
        }
        public static bool DamagePrefix(WorldBase _world, BlockValueRef _blockValueRef,
            int _entityIdThatDamaged, ItemActionAttack.AttackHitInfo _attackHitInfo,
            ref int _damagePoints, ref int __result)
        {
            if (_world == null || _damagePoints <= 0) return true;
            var attacker = _world == null ? null : _world.GetEntity(_entityIdThatDamaged) as EntityAlive;
            if (Stud(_world.GetBlock(_blockValueRef.BlockPosition)))
            {
                // Native player tool/weapon hits provide attack details; environmental
                // damage has none. Native explosions use GetExplosionResistance instead.
                if (!(attacker is EntityPlayer) || _attackHitInfo == null)
                    _damagePoints = ReducedDamage(_damagePoints);
                return true;
            }
            if (!Protected(_world, attacker, _blockValueRef.BlockPosition)) return true;
            // Returning before native damage avoids destruction, drops and damage callbacks.
            __result = 0; return false;
        }
    }
    public sealed class ModApi : IModApi
    {
        public void InitMod(Mod mod)
        {
            var harmony = new Harmony("pzaec.wallstudbarrier");
            Navigation.Install(harmony);
            Recovery.Install(harmony);
            harmony.Patch(AccessTools.Method(typeof(Block), "GetExplosionResistance"), postfix:new HarmonyMethod(typeof(Barrier), nameof(Barrier.ExplosionResistancePostfix)));
            harmony.Patch(AccessTools.Method(typeof(EAIBreakBlock),"CanExecute"),postfix:new HarmonyMethod(typeof(Barrier),nameof(Barrier.CanBreakPostfix)));
            harmony.Patch(AccessTools.Method(typeof(EAIBreakBlock),"AttackBlock"),prefix:new HarmonyMethod(typeof(Barrier),nameof(Barrier.BreakPrefix)));
            harmony.Patch(AccessTools.Method(typeof(EntityAlive),"Attack",new[]{typeof(bool)}),prefix:new HarmonyMethod(typeof(Barrier),nameof(Barrier.AttackPrefix)));
            harmony.Patch(AccessTools.Method(typeof(EntityMoveHelper),"FindDestroyPos",new[]{typeof(Vector3).MakeByRefType(),typeof(int),typeof(bool)}),postfix:new HarmonyMethod(typeof(Barrier),nameof(Barrier.FindDestroyPostfix)));
            harmony.Patch(AccessTools.Method(typeof(Block),"DamageBlock",new[]{typeof(WorldBase),typeof(BlockValueRef),typeof(BlockValue),typeof(int),typeof(int),typeof(ItemActionAttack.AttackHitInfo),typeof(bool),typeof(bool)}),prefix:new HarmonyMethod(typeof(Barrier),nameof(Barrier.DamagePrefix)));
            Log.Out("[WallStudBarrier] Stud damage reduced by 80%; explosion resistance, rear-wall guards and navigation enabled.");
        }
    }
}
