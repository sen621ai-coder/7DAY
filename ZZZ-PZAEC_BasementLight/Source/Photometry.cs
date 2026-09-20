using System;
namespace PZAEC.BasementLight
{
    public static class Photometry
    {
        public const float Range=22f, Intensity=12f, Height=4.2f;
        // Analytic built-in point falloff approximation, for shaping the cookie.
        // Actual game renderer/exposure still requires an in-game visual acceptance pass.
        public static double Falloff(double distance)
        { double s=distance*distance/(Range*Range);return s>=1?0:(1-s)*(1-s)/(1+25*s); }
        public static float Transmission(double x,double y,double z)
        {
            if(y>=-0.001)return 0;
            double length=Math.Sqrt(x*x+y*y+z*z),cos=-y/length;
            double px=Height*x/-y,pz=Height*z/-y;
            double edge=Math.Max(Math.Abs(px),Math.Abs(pz));
            double fade=Math.Max(0,Math.Min(1,(11.5-edge)/1.4));
            fade=fade*fade*(3-2*fade);
            double incident=Intensity*Falloff(Height/cos)*cos;
            return incident<=0?0:(float)(Math.Min(1,0.08/incident)*fade);
        }
    }
}
