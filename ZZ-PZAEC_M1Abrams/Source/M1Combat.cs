using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
namespace PZAEC.M1
{
    public static class Combat
    {
        public static void Install(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(EntityVehicle),"ApplyDamage"),prefix:new HarmonyMethod(typeof(Combat),nameof(Damaged)),transpiler:new HarmonyMethod(typeof(Combat),nameof(DamageSentinel)));
            h.Patch(AccessTools.Method(typeof(EntityVehicle),"ProcessDamageResponseLocal"),prefix:new HarmonyMethod(typeof(Combat),nameof(Armor)));
            h.Patch(AccessTools.Method(typeof(Equipment),"GetTotalPhysicalArmorRating"),postfix:new HarmonyMethod(typeof(Combat),nameof(Penetration)));
            h.Patch(AccessTools.Method(typeof(Vehicle),"GetPlayerDamagePercent"),postfix:new HarmonyMethod(typeof(Combat),nameof(CrewProtection)));
        }
        // Vanilla >=99999 is a destruction sentinel, not normal health damage.
        // Keep its death/backpack/explosion machinery but scope a larger sentinel to M1.
        public static int Threshold(EntityVehicle v)=>Weapons.IsTank(v)?int.MaxValue:99999;
        static IEnumerable<CodeInstruction> DamageSentinel(IEnumerable<CodeInstruction> input)
        {
            int count=0;
            foreach(var c in input){if(c.opcode==OpCodes.Ldc_I4&&c.operand is int n&&n==99999){
                var first=new CodeInstruction(OpCodes.Ldarg_0);first.labels.AddRange(c.labels);first.blocks.AddRange(c.blocks);yield return first;
                yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(Combat),nameof(Threshold)));count++;
            }else yield return c;}
            if(count!=1)throw new InvalidOperationException("M1 vehicle damage sentinel changed in this game version");
        }
        static void Damaged(EntityVehicle __instance,int __0)
        {if(Weapons.IsTank(__instance)&&__0>0){var s=Weapons.Register(__instance);s.LastDamage=Time.time;s.RepairTrigger.Stop();s.RepairStarted=-1;}}
        static void Armor(EntityVehicle __instance,ref DamageResponse __0)
        {
            if(!Weapons.IsTank(__instance)||__0.Strength<=0||__0.Source==null)return;
            var source=__0.Source;if(source.GetSource()!=EnumDamageSource.External||source.damageType==EnumDamageTypes.Falling||source.damageType==EnumDamageTypes.Suicide)return;
            var toward=-source.getDirection();int region=3;
            if(source.BuffClass==null&&toward.sqrMagnitude>.001f){
                var d=Quaternion.Inverse(Weapons.Body(__instance))*toward.normalized;
                if(Mathf.Abs(d.y)<.6f){float angle=Mathf.Abs(Mathf.Atan2(d.x,d.z)*Mathf.Rad2Deg);region=angle<=60?0:angle>=135?2:1;}
            }
            __0.Strength=Rules.ProtectedDamage(__0.Strength,Weapons.Tier(__instance),region,source.damageType==EnumDamageTypes.Corrosive);
        }
        static void CrewProtection(Vehicle __instance,ref float __result)
        {if(Rules.Index(__instance.GetName())>=0)__result=0;}
        static void Penetration(Equipment __instance,ItemValue attackingItem,ref float __result)
        {
            string name=attackingItem?.ItemClass?.GetItemName();if(name!=Rules.Ammo&&name!=Rules.APAmmo)return;
            // Resolve defender armor without importing the gunner's handheld penetration bonuses.
            var tags=FastTags<TagGroup.Global>.Parse("coredamageresist");
            float armor=EffectManager.GetValue(PassiveEffects.PhysicalDamageResist,null,0,__instance.m_entity,null,tags);
            __result=Mathf.Clamp(armor,0,100)*(name==Rules.APAmmo?.5f:1);
        }
        static bool Hostile(EntityAlive e)=>e!=null&&!(e is EntityPlayer)&&!(e is EntityVehicle)&&!e.IsDead()&&
            (e is EntityZombie||e.EntityClass.Tags.Test_AnySet(FastTags<TagGroup.Global>.Parse("hostile")));
        static void Hit(EntityAlive e,int actor,int damage,bool ap,Vector3 direction,Vector3 point)
        {
            if(!Hostile(e)||damage<=0)return;
            var source=new DamageSourceEntity(EnumDamageSource.External,EnumDamageTypes.Piercing,actor,direction){
                AttackingItem=ItemClass.GetItem(Rules.AmmoName(ap),false),hitTransformPosition=point,canHitSpecialBodyParts=false,DismemberChance=0};
            e.DamageEntity(source,damage,false,0);
        }
        public static void Impact(World w,EntityVehicle tank,int actor,int tier,bool ap,WorldRayHitInfo hit,Vector3 direction)
        {
            if(!Weapons.Server)return;var point=hit.hit.pos;var direct=ItemActionAttack.FindHitEntity(hit) as EntityAlive;
            if(ap)Hit(direct,actor,Rules.Specs[tier].AP,true,direction,point);
            else{
                // Copy before damage: a killed entity may be removed from the live collection.
                foreach(var entity in w.Entities.list.ToArray())if(entity is EntityAlive target&&Hostile(target)){
                    var center=target.GetPosition()+Vector3.up*.8f;float distance=Vector3.Distance(point,center);
                    if(target==direct)distance=0;float factor=Rules.HEFalloff(distance);if(factor<=0)continue;
                    var origin=point-direction*.08f;var delta=center-origin;
                    if(target!=direct&&Weapons.Trace(tank,origin,delta.normalized,delta.magnitude,out var barrier)&&ItemActionAttack.FindHitEntity(barrier)!=target)continue;
                    Hit(target,actor,Mathf.RoundToInt(Rules.Specs[tier].HE*factor),false,delta.normalized,center);
                }
            }
            // Native protected-world block damage and visual explosion only; never duplicate entity damage.
            var properties=new DynamicProperties();properties.Classes.Add("Explosion",new DynamicProperties());
            var blast=new ExplosionData(properties,null){ParticleIndex=ap?0:5,BlockRadius=ap?1:2,EntityRadius=0,EntityDamage=0,BlockDamage=ap?150:80,BlastPower=0};
            GameManager.Instance.ExplosionServer(point,new Vector3i(Mathf.FloorToInt(point.x),Mathf.FloorToInt(point.y),Mathf.FloorToInt(point.z)),Quaternion.identity,blast,actor,0,false,ItemClass.GetItem(Rules.AmmoName(ap),false));
        }
    }
}
