using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace AECT16RuntimeFix
{
    public static class ApacheArmor
    {
        private static bool enabled;
        [ThreadStatic] private static int explosionDepth;
        [ThreadStatic] private static EntityVehicle responseVehicle;
        [ThreadStatic] private static bool explicitSuicide;
        public struct ResponseScope { public EntityVehicle Vehicle; public bool Suicide; }

        public static void Install()
        {
            var h=new Harmony("pzaec.apache.armor.v1");
            try
            {
                var signature=new[]{typeof(DamageSource),typeof(int),typeof(bool),typeof(float)};
                h.Patch(AccessTools.Method(typeof(EntityAlive),"damageEntityLocal",signature),transpiler:new HarmonyMethod(typeof(ApacheArmor),nameof(ResponseTranspiler)));
                h.Patch(AccessTools.Method(typeof(EntityVehicle),"damageEntityLocal",signature),transpiler:new HarmonyMethod(typeof(ApacheArmor),nameof(ResponseTranspiler)));
                h.Patch(AccessTools.Method(typeof(EntityVehicle),"ProcessDamageResponseLocal",new[]{typeof(DamageResponse)}),
                    prefix:new HarmonyMethod(typeof(ApacheArmor),nameof(BeginResponse)),finalizer:new HarmonyMethod(typeof(ApacheArmor),nameof(EndResponse)));
                h.Patch(AccessTools.Method(typeof(EntityVehicle),"ApplyDamage",new[]{typeof(int)}),
                    prefix:new HarmonyMethod(typeof(ApacheArmor),nameof(BeforeApply)),transpiler:new HarmonyMethod(typeof(ApacheArmor),nameof(ThresholdTranspiler)));
                h.Patch(AccessTools.Method(typeof(Explosion),"AttackEntites",new[]{typeof(int),typeof(ItemValue),typeof(EnumDamageTypes)}),
                    prefix:new HarmonyMethod(typeof(ApacheArmor),nameof(BeginExplosion)),finalizer:new HarmonyMethod(typeof(ApacheArmor),nameof(EndExplosion)));
                enabled=true;
                Log.Out("[Apache-Armor] Hull and occupied-seat protection enabled; all peers must update.");
            }
            catch(Exception ex){enabled=false;h.UnpatchSelf();Log.Error("[Apache-Armor] Disabled: "+ex);}
        }

        private static bool Hull(EntityVehicle v)
        {return enabled&&ApacheWeapons.IsApache(v)&&!v.IsDead()&&v.vehicle.GetHealth()>0;}

        public static int Reduce(int damage,int maxHealth,double fraction,double capFraction)
        {
            if(damage<=0||maxHealth<=0)return damage;
            return (int)Math.Min(Math.Max(1,Math.Floor(maxHealth*capFraction)),Math.Max(1,Math.Ceiling(damage*fraction)));
        }

        public static bool Combat(DamageSource source)
        {
            if(source==null||source.damageType==EnumDamageTypes.Suicide)return false;
            if(source.damageType==EnumDamageTypes.VehicleInside||source.damageType==EnumDamageTypes.Falling)return true;
            // Environmental/illness ticks retain their existing sealed-cabin and status rules.
            return source.damageSource==EnumDamageSource.External;
        }

        public static void ProtectResponse(ref DamageResponse response,EntityAlive entity)
        {
            if(!enabled||response.Strength<=0||response.Source==null||response.Source.damageType==EnumDamageTypes.Suicide)return;
            int max;double fraction,cap;
            var hull=entity as EntityVehicle;
            bool collision=response.Source.damageType==EnumDamageTypes.VehicleInside||response.Source.damageType==EnumDamageTypes.Falling;
            if(Hull(hull))
            {max=hull.vehicle.GetMaxHealth();fraction=explosionDepth>0?.4:collision?.5:.25;cap=.15;}
            else
            {
                var player=entity as EntityPlayer;
                var vehicle=player?.AttachedToEntity as EntityVehicle;
                if(player==null||player.IsDead()||!Hull(vehicle)||!Combat(response.Source)||
                    (vehicle.GetAttached(0)!=player&&vehicle.GetAttached(1)!=player))return;
                // Native hull-to-seat damage is anonymous Bashing on every peer;
                // classify it consistently even when replaying an explosion packet.
                max=player.GetMaxHealth();fraction=responseVehicle==vehicle?.1:explosionDepth>0?.2:collision?.25:.1;cap=.2;
            }
            // Runs once at response creation, after native armor calculations and before
            // local application/serialization. Received packets must NOT reduce again.
            response.Strength=Reduce(response.Strength,max,fraction,cap);
            response.ModStrength=Reduce(response.ModStrength,max,fraction,cap);
            if(response.Strength<entity.Health)response.Fatal=false;
        }

        public static void BeginExplosion(out int __state){__state=explosionDepth;explosionDepth++;}
        public static Exception EndExplosion(Exception __exception,int __state){explosionDepth=__state;return __exception;}
        public static void BeginResponse(EntityVehicle __instance,DamageResponse __0,out ResponseScope __state)
        {
            __state=new ResponseScope{Vehicle=responseVehicle,Suicide=explicitSuicide};
            responseVehicle=__instance;explicitSuicide=__0.Source!=null&&__0.Source.damageType==EnumDamageTypes.Suicide;
        }
        public static Exception EndResponse(Exception __exception,ResponseScope __state)
        {responseVehicle=__state.Vehicle;explicitSuicide=__state.Suicide;return __exception;}
        public static void BeforeApply(EntityVehicle __instance,ref int __0)
        {
            // Unwrapped ApplyDamage is the native accumulated collision/wear path.
            if(Hull(__instance)&&responseVehicle!=__instance)__0=Reduce(__0,__instance.vehicle.GetMaxHealth(),.5,.15);
        }
        public static int KillThreshold(EntityVehicle vehicle)
        {return Hull(vehicle)&&!(responseVehicle==vehicle&&explicitSuicide)?int.MaxValue:99999;}

        public static IEnumerable<CodeInstruction> ThresholdTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code=new List<CodeInstruction>(instructions);int matches=0;
            for(int i=0;i<code.Count;i++)if(code[i].opcode==OpCodes.Ldc_I4&&Equals(code[i].operand,99999))
            {
                code[i].opcode=OpCodes.Ldarg_0;code[i].operand=null;
                code.Insert(++i,new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(ApacheArmor),nameof(KillThreshold))));matches++;
            }
            if(matches!=1)throw new InvalidOperationException("Unsupported vehicle instant-explosion threshold");
            return code;
        }
        public static IEnumerable<CodeInstruction> ResponseTranspiler(IEnumerable<CodeInstruction> instructions,MethodBase __originalMethod)
        {
            var code=new List<CodeInstruction>(instructions);
            string target=__originalMethod.DeclaringType==typeof(EntityVehicle)?"ProcessDamageResponseLocal":"FireAttackedEvents";
            int matches=0;
            for(int i=2;i<code.Count;i++)if(code[i].operand is MethodInfo method&&method.Name==target)
            {
                if(code[i-2].opcode!=OpCodes.Ldarg_0||code[i-1].opcode!=OpCodes.Ldloc_0)
                    throw new InvalidOperationException("Unsupported damage response local layout");
                var start=new CodeInstruction(OpCodes.Ldloca_S,(byte)0);
                start.labels.AddRange(code[i-2].labels);code[i-2].labels.Clear();
                start.blocks.AddRange(code[i-2].blocks);code[i-2].blocks.Clear();
                code.InsertRange(i-2,new[]{start,new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(ApacheArmor),nameof(ProtectResponse)))});
                i+=3;matches++;
            }
            if(matches!=1)throw new InvalidOperationException("Unsupported damage response application sites");
            return code;
        }
    }
}
