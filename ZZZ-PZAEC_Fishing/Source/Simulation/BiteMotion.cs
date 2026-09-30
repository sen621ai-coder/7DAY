using System;

namespace PZAEC.Fishing.Simulation
{
    // Sample once per cast, evaluate continuously. No per-tick RNG: replay is tick-rate independent.
    internal sealed class BiteMotion
    {
        private readonly double[] centers = new double[3], widths = new double[3], strengths = new double[3];
        private readonly double takeSeconds;
        internal BiteMotion(Func<double> random, double duration)
        {
            for (int i=0;i<3;i++)
            {
                centers[i]=duration*((i+.3+.35*random())/3);
                widths[i]=duration*(.075+.035*random());
                strengths[i]=.13+.2*random();
            }
            takeSeconds=.12+.15*random();
        }
        internal double Sample(double seconds, bool taking)
        {
            if(taking) return Smooth(seconds/takeSeconds)*(.9+.1*Math.Sin(seconds*3));
            double force=0;
            for(int i=0;i<3;i++)
            {
                double x=Math.Abs(seconds-centers[i])/widths[i];
                if(x<1)force+=strengths[i]*.5*(1+Math.Cos(Math.PI*x));
            }
            return force;
        }
        internal static double Smooth(double value)
        {double x=Numbers.Clamp(value,0,1);return x*x*(3-2*x);}
    }
}
