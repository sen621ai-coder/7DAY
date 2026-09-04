using System;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using UnityEngine;

namespace AECT16RuntimeFix
{
    public static class BloodMoonSiege
    {
        public const int ReplacementChance = 25, HeavyChance = 10, AcidChance = 5,
            DisruptorChance = 4, SpotterChance = 3, LinesmanChance = 3;
        public const int NearbyCap = 8, TargetCap = 4;
        public const float MinRange = 8, MaxRange = 52, SupportRadius = 24;
        private static readonly Regex Names = new Regex(
            @"\APZAECSiege(?:(?:_(?:heavy|acid)_T)|(?:Disruptor|Spotter|Linesman)T)(16|17|18|19)\z",
            RegexOptions.CultureInvariant);
        private static readonly ConditionalWeakTable<EntityAlive, EAIPZAECSiege> Aiming = new ConditionalWeakTable<EntityAlive, EAIPZAECSiege>();

        public static int Tier(string id) { var m = Names.Match(id ?? ""); return m.Success ? int.Parse(m.Groups[1].Value) : 0; }
        public static int TierForGameStage(int gs)
        {
            if (gs >= 300000) return 19;
            if (gs >= 270000) return 18;
            if (gs >= 240000) return 17;
            return gs >= 180000 ? 16 : 0;
        }
        public static string Variant(int tier, int roll)
        {
            if (tier < 16 || tier > 19 || roll < 0 || roll >= ReplacementChance) return null;
            if (roll < HeavyChance) return "PZAECSiege_heavy_T" + tier;
            roll -= HeavyChance;
            if (roll < AcidChance) return "PZAECSiege_acid_T" + tier;
            roll -= AcidChance;
            if (roll < DisruptorChance) return "PZAECSiegeDisruptorT" + tier;
            roll -= DisruptorChance;
            if (roll < SpotterChance) return "PZAECSiegeSpotterT" + tier;
            return "PZAECSiegeLinesmanT" + tier;
        }
        public static bool HasRoom(int nearby, int targeted) { return nearby < NearbyCap && targeted < TargetCap; }

        // Called only from the native Blood Moon selector. Replace eligible
        // ordinary zombies, never bosses/beasts, without adding spawn requests.
        public static int SelectReplacement(int original, int tier, EntityPlayer target,
            bool isEnemy, bool isAnimal, GameRandom random)
        {
            if (target == null || target.world == null || target.world.IsRemote() ||
                !target.world.isEventBloodMoon || !isEnemy || isAnimal || tier < 16 || tier > 19) return original;
            string oldName = EntityClass.GetEntityClassName(original);
            if (oldName == null || !oldName.StartsWith("AECZombie", StringComparison.Ordinal)) return original;
            // The native selector accepts a null GameRandom and resolves it from
            // the world internally. Dynamic selections reach us before that
            // native fallback, so mirror it here before rolling the squad slot.
            GameRandom selectionRandom = random ?? target.world.GetGameRandom();
            if (selectionRandom == null) return original;
            string name = Variant(tier, selectionRandom.RandomRange(100));
            if (name == null) return original;
            int replacement = EntityClass.GetId(name);
            if (replacement == -1) return original;
            var replacementClass = EntityClass.GetEntityClass(replacement);
            // Siege ranks follow the same GS 180000/240000/270000/300000
            // ladder as the ordinary Blood Moon selector.
            if (replacementClass == null || !replacementClass.bIsEnemyEntity || replacementClass.bIsAnimalEntity) return original;
            int nearby = 0, targeted = 0;
            var entities = target.world.Entities;
            if (entities == null || entities.list == null) return original;
            foreach (var entity in entities.list)
            {
                var alive = entity as EntityAlive;
                if (alive == null || alive.IsDead() || Tier(EntityClass.GetEntityClassName(alive.entityClass)) == 0) continue;
                if ((alive.position - target.position).sqrMagnitude <= 96f * 96f) nearby++;
                if (alive.GetAttackTarget() == target) targeted++;
                if (!HasRoom(nearby, targeted)) return original;
            }
            return replacement;
        }

        public static bool InRange(Vector3 muzzle, Vector3 point)
        {
            float d = (point - muzzle).sqrMagnitude;
            return d >= MinRange * MinRange && d <= MaxRange * MaxRange;
        }
        public static bool IsStructure(BlockValue value)
        {
            if (value.isair || value.isTerrain || value.ischild) return false;
            var block = value.Block;
            if (block == null || block.MaxDamage <= 0 || block.IsDecoration || block.IsTerrainDecoration || block.bIsPlant) return false;
            // Do not choose stored loot, crafting machinery or protected utility
            // blocks as siege objectives. Native projectile collisions still apply.
            string type = block.GetType().Name;
            string name = block.GetBlockName();
            return type.IndexOf("Loot", StringComparison.OrdinalIgnoreCase) < 0 &&
                type.IndexOf("Secure", StringComparison.OrdinalIgnoreCase) < 0 &&
                type.IndexOf("Workstation", StringComparison.OrdinalIgnoreCase) < 0 &&
                name.IndexOf("landClaim", StringComparison.OrdinalIgnoreCase) < 0 &&
                name.IndexOf("bedroll", StringComparison.OrdinalIgnoreCase) < 0 &&
                name.IndexOf("bed", StringComparison.OrdinalIgnoreCase) != 0;
        }

        public static bool VisibleStructure(World world, Vector3 origin, Vector3 toward, out Vector3i blockPos, out Vector3 aim)
        {
            blockPos = Vector3i.zero; aim = Vector3.zero;
            Vector3 delta = toward - origin;
            float distance = delta.magnitude;
            if (distance < MinRange || float.IsNaN(distance) || float.IsInfinity(distance)) return false;
            if (!Voxel.Raycast(world, new Ray(origin, delta / distance), Math.Min(MaxRange, distance + 1f), true, false)) return false;
            var hit = Voxel.voxelRayHitInfo;
            if (!hit.bHitValid || hit.tag != "B_Mesh" || !BlockValueRef.Create(hit).TryGetBlockPos(out blockPos)) return false;
            if (world.IsWithinTraderArea(blockPos) || !IsStructure(world.GetBlock(blockPos))) return false;
            // The point sits just inside the first visible face, not behind it.
            aim = hit.hit.pos + delta.normalized * 0.08f;
            return InRange(origin, aim);
        }

        public static bool FindTarget(EntityAlive shooter, EntityPlayer target, out Vector3i position, out Vector3 aim)
        {
            Vector3 origin = shooter.GetLookRay().origin;
            // 28 bounded rays per acquisition, once per four seconds on failure.
            // Rays hit the exposed perimeter first; there is no scan of hidden
            // foundations, land-claim contents, or all nearby blocks.
            foreach (float y in new[] { -1f, -4f, -8f, -14f })
            foreach (var offset in new[] { Vector3.zero, new Vector3(8,0,0), new Vector3(-8,0,0),
                new Vector3(0,0,8), new Vector3(0,0,-8), new Vector3(5,0,5), new Vector3(-5,0,-5) })
            {
                Vector3 toward = target.position + offset + new Vector3(0,y,0);
                if (VisibleStructure(shooter.world, origin, toward, out position, out aim) &&
                    (aim - target.position).sqrMagnitude <= SupportRadius * SupportRadius) return true;
            }
            position = Vector3i.zero; aim = Vector3.zero;
            return false;
        }
        public static void BeginAim(EntityAlive entity, EAIPZAECSiege task) { Aiming.Remove(entity); Aiming.Add(entity, task); }
        public static void EndAim(EntityAlive entity) { Aiming.Remove(entity); }
        public static bool TryAim(EntityAlive entity, out Vector3 aim)
        {
            aim = Vector3.zero;
            if (entity == null || !Aiming.TryGetValue(entity, out var task)) return false;
            aim = task.Aim;
            return true;
        }
    }
}

// Assembly-qualified XML names resolve through the game's native EAI/ItemAction
// factories. Keep these two types in the global namespace.
public sealed class EAIPZAECSiege : EAIRangedAttackTarget
{
    public Vector3 Aim { get; private set; }
    private Vector3i blockPos;
    private bool locked;
    private float nextCheck;
    private bool recovering;
    private float recoverUntil;

    private bool Enabled()
    {
        return theEntity != null && !theEntity.IsDead() && theEntity.world != null && !theEntity.world.IsRemote() &&
            theEntity.IsBloodMoon && theEntity.world.isEventBloodMoon && GameStats.GetBool(EnumGameStats.EnemySpawnMode) &&
            !theEntity.world.IsWithinTraderArea(new Vector3i(theEntity.position));
    }
    private bool TargetValid()
    {
        return entityTarget is EntityPlayer && !entityTarget.IsDead() &&
            !theEntity.world.IsWithinTraderArea(new Vector3i(entityTarget.position)) &&
            (theEntity.position - entityTarget.position).sqrMagnitude <= 72f * 72f;
    }
    private bool WallValid()
    {
        return locked && (Aim - entityTarget.position).sqrMagnitude <= AECT16RuntimeFix.BloodMoonSiege.SupportRadius * AECT16RuntimeFix.BloodMoonSiege.SupportRadius &&
            AECT16RuntimeFix.BloodMoonSiege.VisibleStructure(theEntity.world, theEntity.GetLookRay().origin, Aim,
                out var first, out _) && first == blockPos;
    }
    public override bool CanExecute()
    {
        if (cooldown > 0) { cooldown -= executeWaitTime; return false; }
        if (!Enabled() || theEntity.IsDancing || !theEntity.IsAttackValid() || theEntity.bodyDamage.IsAnyArmOrLegMissing) return false;
        entityTarget = theEntity.GetAttackTarget();
        if (!TargetValid()) return false;
        if (!WallValid())
        {
            locked = AECT16RuntimeFix.BloodMoonSiege.FindTarget(theEntity, (EntityPlayer)entityTarget, out blockPos, out var point);
            if (!locked) { cooldown = 4; return false; }
            Aim = point;
        }
        return true;
    }
    public override void Start()
    {
        theEntity.getNavigator().clearPath();
        theEntity.moveHelper.Stop();
        AECT16RuntimeFix.BloodMoonSiege.BeginAim(theEntity, this);
        nextCheck = 0;
        recovering = false;
        base.Start();
    }
    public override bool Continue()
    {
        if (!Enabled() || !TargetValid() || theEntity.bodyDamage.CurrentStun != EnumEntityStunType.None ||
            theEntity.Electrocuted || theEntity.bodyDamage.IsAnyArmOrLegMissing || theEntity.painHitsFelt - painHitsFelt >= 1f) return false;
        if (Time.time >= nextCheck)
        {
            nextCheck = Time.time + 0.5f;
            if (!WallValid()) { locked = false; return false; }
        }
        if (recovering)
        {
            if (Time.time >= recoverUntil)
            {
                recovering = false;
                AECT16RuntimeFix.BloodMoonSiege.BeginAim(theEntity, this);
                base.Start();
            }
            return true;
        }
        if (!base.Continue())
        {
            // Remain on station during reload; otherwise the ordinary approach
            // task would walk the gunner into the ditch between every shot.
            if (elapsedTime < attackDuration) return false;
            var player = entityTarget;
            AECT16RuntimeFix.BloodMoonSiege.EndAim(theEntity);
            base.Reset();
            entityTarget = player;
            recovering = true;
            recoverUntil = Time.time + cooldown;
            cooldown = 0;
        }
        return true;
    }
    public override void Update()
    {
        if (!recovering) base.Update();
        theEntity.SetLookPosition(Aim);
        theEntity.SeekYawToPos(Aim, 30f);
    }
    public override void Reset()
    {
        AECT16RuntimeFix.BloodMoonSiege.EndAim(theEntity);
        recovering = false;
        base.Reset();
    }
}

public sealed class ItemActionPZAECSiegeVomit : ItemActionVomit
{
    public override int GetActionEffectsValues(ItemActionData data, out Vector3 startPos, out Vector3 direction)
    {
        int result = base.GetActionEffectsValues(data, out startPos, out direction);
        if (!AECT16RuntimeFix.BloodMoonSiege.TryAim(data.invData.holdingEntity, out var point)) return result;
        direction = point;
        // Native negative-FlyTime projectiles interpret userData=1 as a target
        // point. Both this point and the launch origin are sent to joining peers.
        return 1;
    }
}
