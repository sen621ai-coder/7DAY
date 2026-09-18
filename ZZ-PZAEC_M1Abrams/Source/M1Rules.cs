using System;
namespace PZAEC.M1
{
    // Pure rules are shared by authority and presentation, and exercised offline.
    public static class Rules
    {
        public const string Vehicle="vehicleM1Abrams", Ammo="pzM1Shell", APAmmo="pzM1ShellAP", RepairKit="pzM1RepairKit";
        public const float Reload=4.8f, RecoilDistance=.25f, Speed=250, Lifetime=2.4f, Range=600;
        public const int EntityDamage=400000, BlockDamage=80;
        public sealed class Spec
        {
            public readonly int Health,HE,AP,Horsepower;public readonly float Reload,Yaw,Pitch,Turn,Forward,Turbo,Reverse;
            public Spec(int hp,int he,int ap,int power,float reload,float yaw,float pitch,float turn,float forward,float turbo,float reverse)
            {Health=hp;HE=he;AP=ap;Horsepower=power;Reload=reload;Yaw=yaw;Pitch=pitch;Turn=turn;Forward=forward/3.6f;Turbo=turbo/3.6f;Reverse=reverse/3.6f;}
        }
        public static readonly Spec[] Specs={
            new Spec(1000000,400000,4000000,1500,4.8f,25,12,20,36,48,14),
            new Spec(1500000,650000,8000000,1650,4.6f,28,13,22,38,50,15),
            new Spec(2200000,1000000,16000000,1800,4.4f,31,14,24,40,52,16),
            new Spec(3200000,1500000,28000000,2000,4.2f,34,15,26,42,54,18)};
        public static int Index(string name)
        {if(name==Vehicle||name==Vehicle+"Placeable")return 0;for(int i=1;i<4;i++)if(name==Vehicle+"T"+(16+i)||name==Vehicle+"T"+(16+i)+"Placeable")return i;return -1;}
        public static string AmmoName(bool ap)=>ap?APAmmo:Ammo;
        public static float ShellSpeed(bool ap)=>ap?350:250;
        public static float HEFalloff(float metres)=>metres<=2?1:metres>=8?0:(8-metres)/6;
        // Region 0 front, 1 side, 2 rear, 3 roof/unknown. Acid halves protection.
        public static float Armor(int tier,int region,bool acid)
        {float value=region==0?.70f+tier*.02f:region==1?.50f+tier*.02f:region==2?.25f+tier*.03f:.35f+tier*.03f;return Math.Min(.8f,value)*(acid?.5f:1);}
        public static int ProtectedDamage(int damage,int tier,int region,bool acid)
        {return damage<=0?0:Math.Max(1,(int)Math.Round(damage*(1.0-Armor(tier,region,acid))));}
        public static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        public static bool Operator(int seat,bool gunnerPresent)=>seat==1||(seat==0&&!gunnerPresent);
        public static float Recoil(float age)
        {
            if(age<0||age>=.6f)return 0;
            if(age<.1f){float t=age/.1f;return RecoilDistance*(1-(1-t)*(1-t));}
            if(age<.16f)return RecoilDistance;
            float u=(age-.16f)/.44f;return RecoilDistance*(1-u*u*(3-2*u));
        }
        // Engine deck clearance: gradually raise the lower limit behind the tank.
        public static float MinPitch(float yaw)
        {float a=Math.Abs(((yaw+180)%360+360)%360-180);return a<=95?-6:a>=145?4:-6+(a-95)*.20f;}
        public sealed class Trigger
        {
            public int Actor=-1,Sequence; public float Until; public bool Held,Seen;
            public bool Accept(int actor,int sequence,bool held,float now)
            {
                if(Seen&&Actor==actor&&unchecked(sequence-Sequence)<=0)return false;
                Actor=actor;Sequence=sequence;Seen=true;Held=held;Until=now+.35f;return true;
            }
            public bool Active(float now)=>Held&&now<=Until;
            public void Stop(){Held=false;}
        }
    }
}
