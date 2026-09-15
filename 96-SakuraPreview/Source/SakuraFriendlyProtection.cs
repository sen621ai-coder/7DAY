using HarmonyLib;
namespace SakuraPreview
{
    public static class SakuraFriendlyProtection
    {
        public static bool IsFriendlyDamage(EntitySakura npc,DamageSource source)
        {
            if(source==null || npc.world==null)return false;
            var owner=npc.world.GetEntity(source.getEntityId());
            var creator=npc.world.GetEntity(source.CreatorEntityId);
            if(Friendly(owner)||Friendly(creator))return true;
            // Powered turret bullets may identify their block rather than a currently loaded owner.
            return npc.world.GetTileEntity(source.BlockPosition) is TileEntityPoweredRangedTrap;
        }
        static bool Friendly(Entity entity)=>entity is EntityPlayer || entity is EntitySakura || entity is EntityTurret || entity is EntityDrone;
        public static void Install(Harmony harmony)
        {
            var prefix=new HarmonyMethod(typeof(SakuraFriendlyProtection),nameof(IgnoreCompanion));
            harmony.Patch(AccessTools.Method(typeof(AutoTurretFireController),"shouldIgnoreTarget"),prefix:prefix);
            harmony.Patch(AccessTools.Method(typeof(MiniTurretFireController),"shouldIgnoreTarget"),prefix:prefix);
        }
        public static bool IgnoreCompanion(Entity __0,ref bool __result)
        {
            if(!(__0 is EntitySakura))return true;
            __result=true;return false;
        }
    }
}


