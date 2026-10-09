using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace PZAEC.FlyingSword
{
    public static class SwordSafety
    {
        [ThreadStatic] public static bool Detaching;
        static readonly Dictionary<EntityPlayer,float> granted=new Dictionary<EntityPlayer,float>();
        public static void Grant(EntityPlayer p){p.Buffs.SetCustomVar(SwordRules.FallKey,1);granted[p]=Time.time;}
        public static bool Protected(EntityAlive p){return p is EntityPlayer&&!p.IsDead()&&p.AttachedToEntity==null&&p.Buffs.GetCustomVar(SwordRules.FallKey)>0;}
        public static void Install(Harmony h)
        {
            var sig=new[]{typeof(DamageSource),typeof(int),typeof(bool),typeof(float)};
            foreach(var t in new[]{typeof(EntityPlayer),typeof(EntityPlayerLocal)})h.Patch(AccessTools.Method(t,"DamageEntity",sig),prefix:new HarmonyMethod(typeof(SwordSafety),nameof(Damage)));
            h.Patch(AccessTools.Method(typeof(EntityPlayerLocal),"FallImpact"),prefix:new HarmonyMethod(typeof(SwordSafety),nameof(Fall)));
            foreach(var t in new[]{typeof(EntityAlive),typeof(EntityPlayerLocal)})h.Patch(AccessTools.Method(t,"ProcessDamageResponseLocal",new[]{typeof(DamageResponse)}),prefix:new HarmonyMethod(typeof(SwordSafety),nameof(Response)));
            h.Patch(AccessTools.Method(typeof(EntityAlive),"ProcessDamageResponse",new[]{typeof(DamageResponse)}),prefix:new HarmonyMethod(typeof(SwordSafety),nameof(Response)));
            h.Patch(AccessTools.Method(typeof(EntityPlayer),"OnUpdateEntity"),postfix:new HarmonyMethod(typeof(SwordSafety),nameof(Tick)));
        }
        static void Consume(EntityPlayer p){p.Buffs.SetCustomVar(SwordRules.FallKey,0);granted.Remove(p);}
        static bool Damage(EntityPlayer __instance,DamageSource __0,ref int __result){if(__0==null||__0.damageType!=EnumDamageTypes.Falling||!Protected(__instance))return true;Consume(__instance);__result=0;return false;}
        static bool Fall(EntityPlayerLocal __instance){if(!Protected(__instance))return true;Consume(__instance);return false;}
        static bool Response(EntityAlive __instance,DamageResponse __0){return __0.Source==null||__0.Source.damageType!=EnumDamageTypes.Falling||!Protected(__instance);}
        static void Tick(EntityPlayer __instance)
        {var p=__instance;if(p.Buffs.GetCustomVar(SwordRules.FallKey)<=0)return;float at;if(!granted.TryGetValue(p,out at)){at=Time.time;granted[p]=at;}if(p.IsDead()||p.AttachedToEntity!=null||p.onGround&&Time.time-at>.4f){p.Buffs.SetCustomVar(SwordRules.FallKey,0);granted.Remove(p);}}
    }
}
