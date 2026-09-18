using System;
using HarmonyLib;

namespace AECT16RuntimeFix
{
    public static class ArmorFamilySets
    {
        public static readonly string[] Families={"Harrier","Storm","Tremor","Warden"};
        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(Equipment),"GetArmorGroupCount",new[]{typeof(string)}),
                prefix:new HarmonyMethod(typeof(ArmorFamilySets),nameof(CountPrefix)));
        }
        public static int SelectTier(int a,int b,int c,int d)
        {return a>0?16:b>0?17:c>0?18:d>0?19:0;}
        public static bool Parse(string group,out string family,out int tier)
        {
            family=null;tier=0;
            foreach(string f in Families){
                string stem="groupPZAEC"+f;
                if(group==stem){family=f;return true;}
                for(int t=16;t<=19;t++)if(group==stem+"T"+t){family=f;tier=t;return true;}
            }
            return false;
        }
        static int Raw(Equipment e,string family,int tier)
        {return e.ArmorGroupEquipped.TryGetValue("groupPZAEC"+family+"T"+tier,out var info)?info.Count:0;}
        public static bool CountPrefix(Equipment __instance,string __0,ref int __result)
        {
            if(!Parse(__0,out var family,out int tier))return true;
            int a=Raw(__instance,family,16),b=Raw(__instance,family,17),c=Raw(__instance,family,18),d=Raw(__instance,family,19);
            __result=tier==0||tier==SelectTier(a,b,c,d)?a+b+c+d:0;
            return false;
        }
        public static string Charge(string family)=>"$PZAEC"+family+"Resonance";
        public static bool Busy(EntityPlayer player,string family)
        {
            for(int tier=16;tier<=19;tier++){
                string stem="buffPZAEC"+family+"T"+tier;
                if(player.Buffs.HasBuff(stem+"Cooldown")||player.Buffs.HasBuff(stem+"Active"))return true;
            }
            return false;
        }
        public static void MigrateCharge(EntityPlayer player,string family)
        {
            string shared=Charge(family);float amount=player.Buffs.GetCustomVar(shared);
            for(int tier=16;tier<=19;tier++){
                string old="$PZAEC"+family+"T"+tier+"Resonance";float value=player.Buffs.GetCustomVar(old);
                if(value<=0)continue;amount=Math.Max(amount,value);player.Buffs.SetCustomVar(old,0,true,CVarOperation.set);
            }
            amount=player.equipment.GetArmorGroupCount("groupPZAEC"+family)>=3?Math.Min(100,amount):0;
            if(amount!=player.Buffs.GetCustomVar(shared))player.Buffs.SetCustomVar(shared,amount,true,CVarOperation.set);
        }
    }
}
