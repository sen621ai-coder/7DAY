using System;
using System.Collections.Generic;
using HarmonyLib;

namespace PZAEC.SealedCabins
{
    public static class Protection
    {
        public static readonly HashSet<string> ExposureBuffs=new HashSet<string>(StringComparer.OrdinalIgnoreCase){
            "buffElementHot","buffElementSweltering","buffElementCold","buffElementFreezing",
            "buffRadiation01","buffRadiation03","buffRadiationPool",
            "buffActiveRadiationImpact1","buffActiveRadiationImpact2","buffActiveRadiationImpact3"};

        public static bool VehicleName(string name)
        {
            return name=="vehicleMD500"||name=="vehicleApacheHelicopter"||name=="vehicleM1Abrams"||
                name=="vehicleM1AbramsT17"||name=="vehicleM1AbramsT18"||name=="vehicleM1AbramsT19";
        }
        public static bool Inside(EntityAlive entity)
        {
            // Live attachment applies equally to every occupied seat, including
            // remote passengers. No proximity aura, fuel test or saved immunity.
            if(!(entity is EntityPlayer)||entity.IsDead())return false;
            var vehicle=entity.AttachedToEntity as EntityVehicle;
            return vehicle!=null&&!vehicle.IsDead()&&vehicle.vehicle!=null&&
                vehicle.vehicle.GetHealth()>0&&VehicleName(vehicle.vehicle.GetName());
        }
        public static bool BeforeAddBuff(EntityBuffs __instance,string __0,ref EntityBuffs.BuffStatus __result)
        {
            if(__0==null||!ExposureBuffs.Contains(__0)||!Inside(__instance.parent))return true;
            __result=EntityBuffs.BuffStatus.FailedImmune;return false;
        }
    }
    // Assembly-qualified XML requirement. Evaluate current attachment every
    // time: exit, death, switching vehicles and disconnect cannot leave a lease.
    public sealed class CabinProtected:RequirementBase
    {
        public override bool IsValid(MinEventParams p)
        {bool inside=Protection.Inside(p?.Self);return invert?!inside:inside;}
    }
    public sealed class ModApi:IModApi
    {
        public void InitMod(Mod mod)
        {
            var method=AccessTools.Method(typeof(EntityBuffs),"AddBuff",new[]{typeof(string),typeof(Vector3i),typeof(int),typeof(bool),typeof(bool),typeof(float)});
            if(method==null)throw new MissingMethodException("Sealed cabins: native AddBuff overload changed");
            new Harmony("pzaec.sealedcabins").Patch(method,prefix:new HarmonyMethod(typeof(Protection),nameof(Protection.BeforeAddBuff)));
            Log.Out("[SealedCabins] M1, MD-500 and Apache occupied-cabin environment protection installed.");
        }
    }
}
