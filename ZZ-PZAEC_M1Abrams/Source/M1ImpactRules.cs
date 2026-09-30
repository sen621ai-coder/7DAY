namespace PZAEC.M1
{
    public enum ImpactSurface { Unknown,Metal,Stone,Earth,Sand,Snow,Wood,Glass,Water,Organic,Cloth }
    public static class ImpactRules
    {
        public static ImpactSurface Classify(string surface,string damage,string id,bool liquid=false)
        {
            string category=(string.IsNullOrEmpty(surface)?damage??"":surface).ToLowerInvariant();
            string name=(id??"").ToLowerInvariant();
            if(liquid||category=="water")return ImpactSurface.Water;
            if(category=="snow"||category=="earth"&&name.Contains("snow"))return ImpactSurface.Snow;
            if(category=="sand"||category=="earth"&&(name.Contains("sand")||name.Contains("desert")))return ImpactSurface.Sand;
            if(category=="glass")return ImpactSurface.Glass;
            if(category=="cloth")return ImpactSurface.Cloth;
            if(category=="wood"||category=="plant"||category=="plants"||category=="leaves")return ImpactSurface.Wood;
            if(category=="earth")return ImpactSurface.Earth;
            if(category=="stone"||category=="concrete"||category=="boulder")return ImpactSurface.Stone;
            if(category=="metal")return ImpactSurface.Metal;
            if(category=="organic"||category=="flesh")return ImpactSurface.Organic;
            return ImpactSurface.Unknown;
        }
        // X=1 remains a shell timeout. Negative X carries the pre-destruction
        // surface without changing the packet layout; both peers need 0.4.10.
        public static float Encode(ImpactSurface surface)=>-(int)surface;
        public static ImpactSurface Decode(float value)=>value<=0&&value>=-10&&(int)value==value?(ImpactSurface)(-(int)value):ImpactSurface.Unknown;
        // AP always has a brief core flash plus a small fireball, including
        // organic/water impacts. These are cosmetic and never ignite targets.
        public static int FireCount(bool ap,ImpactSurface s)=>ap?2:s==ImpactSurface.Water?0:4;
        public static int Sparks(bool ap,ImpactSurface s)=>s==ImpactSurface.Metal?(ap?10:6):s==ImpactSurface.Stone?(ap?3:2):0;
        public static int Dust(bool ap,ImpactSurface s)
        {
            switch(s){case ImpactSurface.Earth:case ImpactSurface.Sand:case ImpactSurface.Snow:case ImpactSurface.Stone:case ImpactSurface.Water:return ap?5:10;
                case ImpactSurface.Wood:case ImpactSurface.Glass:case ImpactSurface.Cloth:return ap?2:4;default:return 0;}
        }
        public static int Smoke(bool ap,ImpactSurface s)=>ap||s==ImpactSurface.Water?0:4;
    }
}
