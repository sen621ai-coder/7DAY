using UnityEngine;

namespace PZAEC.Mecha
{
    public static class Rules
    {
        public const string VehicleName = "vehicleBusterDrone";
        public const string PlaceableItem = "vehicleBusterDronePlaceable";
        public const string BeamAmmo = "ammoPZAECMechaCell";
        public const string MissileAmmo = "ammoPZAECMechaMissile";

        // Beam cannon (eye). Direct hit + impact splash; every damage event
        // stays under the 16-bit network limit (no Damage32 dependency).
        public const float BeamInterval = .5f, BeamHeatPerShot = 8f, BeamCooling = 20f, BeamResumeHeat = 30f;
        public const float BeamRange = 250f, BeamEntityDamage = 60000f, BeamBlockDamage = 15f;
        public const float BeamSplashDamage = 45000f, BeamSplashRadius = 3f;
        public const float BeamRadiusBlocks = 1f;

        // Shoulder missiles: 2 s lock, guided, one per trigger pull.
        public const float MissileLockSeconds = 2f, MissileCooldown = 8f, MissileRange = 350f;
        public const float MissileDamage = 60000f, MissileBlockDamage = 40f, MissileSpeed = 55f;
        public const float MissileLifetime = 6f, MissileTurnRate = 45f, MissileMinDistance = 10f;
        public const float MissileEntityRadius = 5f, MissileBlockRadius = 3f;

        public const float HoldTimeout = .5f, MuzzleOffset = .6f;
        public const float AimMinPitch = -85f, AimMaxPitch = 25f;

        // Field repair: outside, aim at the parked walker, hold the repair key.
        public const string RepairKit = "resourceRepairKit";
        public const float RepairSeconds = 8f, RepairRange = 8f, RepairIdleSeconds = 10f;

        // Battle repair: seated driver burns a kit from cargo for instant hull.
        public const float BattleRepairFraction = .15f, BattleRepairCooldown = 60f;

        // Blade: X toggles ranged/melee; swings are Mouse0 while in melee mode.
        public const float SweepDamage = 45000f, SweepRadius = 4.5f, SweepCooldown = 3f, SweepKnockback = 80f;
        public const float HeavyDamage = 60000f, HeavyRadius = 5.5f, HeavyCooldown = 4f, HeavyKnockback = 140f;
        public const float HeavyArcDegrees = 120f, HeavyChargeSeconds = .5f, MeleeSwitchCooldown = .5f;

        // Turbo jump: sustained thrust while jump is held, capped and cooled down.
        public const float JumpAcceleration = 14f, JumpMaxSeconds = 1.6f, JumpCooldown = 5f;

        // GLB mount tuning (model units are centimetres; deployed size ~3 m).
        public const float MountScale = .038f;
        public static readonly Quaternion MountRotation = Quaternion.Euler(0, 180, 0);

        public const float DeploySeconds = 25f, DeployBlendSeconds = 1.2f;
        public const float TurbineSpinDegreesPerSecond = 720f;

        public static KeyCode Key(EntityVehicle v, string name, KeyCode fallback)
        {
            return v != null && v.vehicle != null && v.vehicle.Properties.Values.TryGetValue(name, out var value) &&
                System.Enum.TryParse<KeyCode>(value, true, out var key) && key != KeyCode.None ? key : fallback;
        }
    }
}
