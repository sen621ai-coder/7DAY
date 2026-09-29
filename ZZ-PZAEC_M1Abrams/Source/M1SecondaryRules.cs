using System;
namespace PZAEC.M1
{
    public static class SecondaryRules
    {
        public const string Belt="pzM1MGBelt",Missile="pzM1AAMissile";
        public const byte Protocol=1,Main=0,MG=1,AA=2;
        public const float AAMinPitch=-10,AAMaxPitch=75;
        public static bool AASearchPitch(float pitch)=>!float.IsNaN(pitch)&&!float.IsInfinity(pitch)&&pitch>=AAMinPitch&&pitch<=85;
        public static readonly int[] MGDamage={20000,35000,60000,100000},AADamage={5000000,10000000,20000000,35000000};
        public static float Falloff(float d)=>d<0||d>200?0:d<=120?1:1-(d-120)*.005f;
        public static float Spread(float heat,bool stabilizer)=>(.35f+.85f*Math.Max(0,Math.Min(100,heat))/100)*(stabilizer?.75f:1);
        public static bool Allowed(int seat,bool gunner,byte mode)=>seat>=0&&seat<2&&mode<=AA&&(seat==1?mode!=MG:!gunner||mode==MG);
        public static int Tube(float left,float right,int preferred,float now)=>((preferred==0?left:right)<=now)?preferred:((preferred==0?right:left)<=now)?1-preferred:-1;
        // Timers store remaining milliseconds, never absolute process time.
        public static int Remaining(float until,float now)=>Math.Max(0,(int)Math.Ceiling((until-now)*1000));
        public sealed class Gun
        {
            public int Rounds;public float Heat,NextShot,ReloadUntil,LastShot=-100;public bool Loading,Overheated;
            public void Tick(float now,float dt){if(Loading&&now>=ReloadUntil){Loading=false;Rounds=100;}if(now-LastShot>.5f)Heat=Math.Max(0,Heat-20*Math.Min(Math.Max(dt,0),now-LastShot-.5f));if(Overheated&&Heat<=40)Overheated=false;}
            public bool CanShoot(float now)=>!Loading&&!Overheated&&Rounds>0&&now>=NextShot;
            public void Fired(float now){Rounds--;Heat=Math.Min(100,Heat+2);NextShot=now+.1f;LastShot=now;if(Heat>=100)Overheated=true;}
        }
        public sealed class InputLease
        {
            public int Actor=-1,Sequence;public bool Seen,Held,Zoom,Armed;public float Until,SwitchUntil;public byte Mode;
            public bool Accept(int actor,int serial,bool held,bool zoom,float now,byte mode)
            {
                if(Seen&&Actor==actor&&unchecked(serial-Sequence)<=0)return false;
                if(Actor!=actor||Mode!=mode){Armed=false;SwitchUntil=now+.25f;Held=false;}
                Actor=actor;Sequence=serial;Seen=true;Mode=mode;Held=held;Zoom=zoom;Until=now+.35f;
                if(!held)Armed=true;return true;
            }
            public bool Active(float now)=>Seen&&now<=Until;
            public bool Firing(float now)=>Active(now)&&Held&&Armed&&now>=SwitchUntil;
            public void Stop(){Held=false;Zoom=false;Armed=false;Until=-1;}
        }
    }
}
