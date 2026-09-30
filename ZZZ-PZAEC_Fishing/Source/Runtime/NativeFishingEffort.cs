using System;

namespace PZAEC.Fishing.Runtime
{
    public static class NativeFishingEffort
    {
        // Add only this frame's fishing cost. Native sprinting, buffs and recovery remain owned by the game.
        public static void Spend(EntityPlayerLocal player,float amount)
        {
            if(player==null||float.IsNaN(amount)||float.IsInfinity(amount)||amount<=0)return;
            player.AddStamina(-Math.Min(Math.Max(0,player.Stamina),amount));
        }
    }
}
