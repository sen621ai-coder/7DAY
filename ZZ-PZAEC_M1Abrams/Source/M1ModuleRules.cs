using System;
namespace PZAEC.M1
{
    public static class ModuleRules
    {
        public const int Power=1,Economy=2,Armor=4,Corrosion=8,Stabilizer=16,Autoloader=32;
        public static readonly string[] Names={"modPZAECM1Powertrain","modPZAECM1Economy","modPZAECM1Armor","modPZAECM1Corrosion","modPZAECM1Stabilizer","modPZAECM1Autoloader"};
        public static int Bit(string name){for(int i=0;i<Names.Length;i++)if(Names[i]==name)return 1<<i;return 0;}
        public static int Slots(int tier)=>tier<0?0:tier<2?2:3;
        public static bool Validate(int tier,string[] names,out int mask)
        {
            mask=0;int count=0;
            foreach(var name in names){if(string.IsNullOrEmpty(name))continue;int bit=Bit(name);if(bit==0||(mask&bit)!=0){mask=0;return false;}mask|=bit;count++;}
            if(tier<0||count>Slots(tier)||(mask&3)==3||(mask&48)==48){mask=0;return false;}return true;
        }
        public static float Speed(int mask)=>(mask&Power)!=0?1.25f:1;
        public static float Torque(int mask)=>(mask&Power)!=0?1.30f:1;
        public static float Fuel(int mask)=>(mask&Economy)!=0?.60f:1;
        public static float Tracking(int mask)=>(mask&Stabilizer)!=0?1.50f:1;
        public static float Recoil(int mask)=>(mask&Stabilizer)!=0?.40f:1;
        public static float Reload(int mask)=>(mask&Autoloader)!=0?.75f:1;
        public static float Protection(int tier,int region,bool acid,int mask)
        {
            float extra=(mask&Armor)==0?0:region==0||region==4?.04f:region==1?.12f:region==2?.10f:0;
            float normal=Math.Min(.80f,Rules.Armor(tier,region,false)+extra);
            return normal*(acid&&(mask&Corrosion)==0?.50f:1);
        }
        public static int Damage(int damage,int tier,int region,bool acid,int mask)=>damage<=0?0:Math.Max(1,(int)Math.Round(damage*(1.0-Protection(tier,region,acid,mask))));
    }
}
