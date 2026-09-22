using System;
namespace PZAEC.BasementLight
{
    public static class Photometry
    {
        public const float Range=20f, Intensity=1f, Height=5.4f;
        public const float FillRange=14f, FillIntensity=.14f;
        // Analytic built-in point falloff approximation, for shaping the cookie.
        // Actual game renderer/exposure still requires an in-game visual acceptance pass.
        public static double Falloff(double distance)
        { double s=distance*distance/(Range*Range);return s>=1?0:(1-s)*(1-s)/(1+25*s); }
        public static float Transmission(double x,double y,double z)
        {
            if(y>=0)return 0;
            double length=Math.Sqrt(x*x+y*y+z*z);
            if(length<=0)return 0;
            double cos=Math.Max(0,Math.Min(1,-y/length));
            // Bounded angular distribution, independent of intensity. Do not
            // invert an assumed renderer falloff: that overdrives grazing walls.
            // No projected square cutoff: it drew a bright horizontal band.
            double horizon=Math.Min(1,cos/.28);
            horizon=horizon*horizon*(3-2*horizon);
            return (float)((.026+1.1*Math.Pow(1-cos,3))*horizon);
        }
        // A dim virtual bounce illuminates upper walls/ceiling, with a smooth
        // horizon and suppressed zenith. It is not a second downward floodlight.
        public static float FillTransmission(double x,double y,double z)
        {
            double length=Math.Sqrt(x*x+y*y+z*z);
            if(length<=0)return 0;
            double cos=y/length;
            double fade=Math.Max(0,Math.Min(1,(cos+.35)/.6));
            fade=fade*fade*(3-2*fade);
            double up=Math.Max(0,cos);
            return (float)(fade*(.65-.4*up*up));
        }
    }
}

