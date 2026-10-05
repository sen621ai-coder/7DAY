using UnityEngine;

namespace PZAEC.Mecha
{
    public static class Rules
    {
        public const string VehicleName = "vehicleCombatRobot";
        public const string PlaceableItem = "vehicleCombatRobotPlaceable";
        public const string CompleteVehicle = "vehicleCombatRobotComplete", CompleteItem = "vehicleCombatRobotCompletePlaceable";
        static bool Same(string a,string b){return string.Equals(a,b,System.StringComparison.OrdinalIgnoreCase);}
        public static bool VehicleNameMatches(string name){return Same(name,VehicleName)||Same(name,CompleteVehicle);}
        public static bool ItemNameMatches(string name){return Same(name,PlaceableItem)||Same(name,CompleteItem);}
        public static bool Complete(EntityVehicle v){return v!=null&&v.vehicle!=null&&Same(v.vehicle.GetName(),CompleteVehicle);}
        public static float AttributeScale(EntityVehicle v){return Complete(v)?1.5f:1f;}
        public static string DisplayName(EntityVehicle v){return Complete(v)?"初号机（完全体）":"初号机（试验体）";}
        public const string BeamAmmo = "ammoPZAECMechaCell";
        public const string MissileAmmo = "ammoPZAECMechaMissile";

        // Beam cannon (palm emitter). Direct hit + impact splash; every damage
        // event stays under the 16-bit network limit (no Damage32 dependency).
        public const float BeamInterval = .5f, BeamHeatPerShot = 18f, BeamCooling = 12f, BeamResumeHeat = 30f;
        public const bool MeleeEnabled = false;
        public const float BeamRange = 250f, BeamEntityDamage = 60000f, BeamBlockDamage = 15f;
        public const float BeamSplashDamage = 45000f, BeamSplashRadius = 3f;
        public const float BeamRadiusBlocks = 1f;

        // Shoulder missiles: 2 s lock, guided, one per trigger pull.
        public const float MissileLockSeconds = 2f, MissileCooldown = 8f, MissileRange = 350f;
        public const float MissileDamage = 60000f, MissileBlockDamage = 40f, MissileSpeed = 55f;
        public const float MissileLifetime = 8f, MissileTurnRate = 45f, MissileMinDistance = 10f;
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

        // Hover cruise: Q toggles a PD-controlled skimming mode over craters.
        public const float HoverHeight = 1.2f, HoverCeiling = 3f, HoverSpeed = 6f, HoverThrust = 6f;
        public const float HoverFuelPerSecond = .5f;
        public const float FlightSpeed=12f, FlightBoostSpeed=20f, FlightReverseSpeed=6f;
        public const float FlightRiseSpeed=5f, FlightDescendSpeed=4f, FlightAcceleration=4f, FlightBraking=8f, FlightVerticalAcceleration=6f;
        public const float FlightFuel=.75f, FlightBoostFuel=1.5f, FlightTakeoffHeight=2f, FlightDeploySeconds=.8f, FlightContactSeconds=.3f;
        // Charged jump: hold Space, release to leap 2.5-5 m.
        public const float JumpMaxSpeed = 9.9f, JumpChargeSeconds = .5f, JumpMinCharge = .3f, JumpCooldown = 4f;
        // Trample: moving crush plus the landing stomp.
        public const float TrampleDamage = 12000f, TrampleRadius = 2.2f, TrampleTickSeconds = .35f;
        public const float TrampleEntityInterval = .5f, TrampleSpeedThreshold = 2f;
        public const float StompDamage = 30000f, StompRadius = 3.5f, StompKnockback = 120f;
        public const float StompAirborneSeconds = .5f, StompCooldown = 1f;

        // Biped framing: runtime-rigged leg chains and IK targets.
        public const float TargetHeight = 3.2f;
        public const float StepTriggerDistance = .4f, StepSeconds = .42f, StepLiftHeight = .4f;
        public const float StrideLookahead = .45f;
        // Ground support / traversal rules, shared by both chassis and native QA.
        public const float AutoStepHeight=.30f,ActiveStepHeight=1.00f,ActiveGapWidth=.75f;
        public const float NormalWalkSlope=35f,MaxWalkSlope=45f;
        public const float FootWidth=.45f,FootDepth=.65f,FootResidual=.08f,SoleClearance=.015f;
        public const float TraverseToeClearance=.12f,TraverseSafeSpeed=.45f;
        public const float PrototypeTraverseSeconds=1f,CompleteTraverseSeconds=.8f;
        public const float TorsoLeanDegrees = 8f, HipSwayMeters = .08f, ArmSwingDegrees = 15f;
        public const float DeployRiseSeconds = 2.5f;

        // Articulated rigid rig, world-planted gait and boarding timeline (0.7.0).
        public const bool BoardingEnabled = true;
        public const bool GaitIkEnabled = true;
        public const bool RigRebuildEnabled = true;
        public const float BoardExpandSeconds = 1.2f, BoardGreetSeconds = .7f, BoardLiftSeconds = .8f, BoardCloseSeconds = 1.5f;
        public const float DismountExpandSeconds = .8f, DismountPlaceSeconds = .8f, DismountRiseSeconds = .6f;

        // GLB mount tuning (auto-scaled to TargetHeight; orientation QA-tuned).
        public const float MountScale = 1f;
        public static readonly Quaternion MountRotation = Quaternion.identity;

        public const float DeploySeconds = 2.5f, DeployBlendSeconds = 1.2f;
        public const float TurbineSpinDegreesPerSecond = 720f;

        public static KeyCode Key(EntityVehicle v, string name, KeyCode fallback)
        {
            return v != null && v.vehicle != null && v.vehicle.Properties.Values.TryGetValue(name, out var value) &&
                System.Enum.TryParse<KeyCode>(value, true, out var key) && key != KeyCode.None ? key : fallback;
        }
    }
}
