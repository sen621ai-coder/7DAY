using System;
using PZAEC.Fishing.Contracts;

namespace PZAEC.Fishing.Presentation
{
    // Rendering geometry only: never feeds tension, force or position back into the simulation.
    public static class PresentationMath
    {
        public static float Saturate(float value) => Scalar.IsFinite(value) ? Scalar.Clamp(value,0,1) : 0;
        public static Vec3 Scene(Vec3 absolute, Vec3 origin) => absolute-origin;
        public static Vec3 RodAim(RodPose pose)
        {
            Vec3 f=new Vec3(pose.Forward.X,0,pose.Forward.Z).Normalized;
            if(f.LengthSquared<.5f)return (pose.Tip-pose.Root).Normalized;
            Vec3 right=new Vec3(f.Z,0,-f.X);
            return (f*(float)Math.Cos(pose.YawRadians)+right*(float)Math.Sin(pose.YawRadians))*(float)Math.Cos(pose.PitchRadians)
                +Vec3.Up*(float)Math.Sin(pose.PitchRadians);
        }
        public static bool CanInterpolate(RenderFrame frame)
        {
            return frame.Previous.SessionId==frame.Current.SessionId && frame.Previous.Tick<=frame.Current.Tick
                && frame.Current.Tick-frame.Previous.Tick<=4 && !frame.Previous.IsTerminal
                && frame.Previous.Phase==frame.Current.Phase;
        }
        public static Vec3 RodPoint(Vec3 root, Vec3 tip, Vec3 forward, float rodLength, float t)
        {
            t=Saturate(t);forward=forward.Normalized;
            if(forward.LengthSquared<.5f)forward=(tip-root).Normalized;
            // Rigid handle followed by a cantilever-shaped blank. Preserve both the physical
            // tip and a straight grip; do not bend the reel seat away from the player's hands.
            float length=Math.Max(0,rodLength),q=Saturate((t-.16f)/.84f);
            float bend=q*q*(3-q)*.5f;
            return root+forward*(length*t)+(tip-root-forward*length)*bend;
        }
        public static float FillRoutedLine(Vec3 start,Vec3 eye,Vec3 mouth,float available,Vec3[] main,Vec3[] leader)
        {
            float a=(eye-start).Length,b=(mouth-eye).Length,total=a+b;
            float budget=Scalar.IsFinite(available)?Math.Max(0,available):0;
            float spare=Math.Max(0,budget-total);
            // Share slack proportionally; never use an independently invented leader length.
            FillLine(start,eye,a+(total>.0001f?spare*a/total:spare),main);
            FillLine(eye,mouth,b+(total>.0001f?spare*b/total:0),leader);
            // Positive means the authoritative endpoints cannot fit in the supplied line.
            // Expose the contradiction; a display component must not silently fix the solver.
            return Math.Max(0,total-budget);
        }
        // Finite parabola fitted to the paid-out length. This is not a second line simulation.
        public static void FillLine(Vec3 from, Vec3 to, float length, Vec3[] points)
        {
            if(points==null||points.Length<2)throw new ArgumentException("At least two line samples required");
            float chord=(to-from).Length;
            if(!Scalar.IsFinite(length))length=chord;
            float target=Math.Max(chord,Math.Min(length,chord+40));
            float low=0,high=target*.5f;
            for(int pass=0;pass<16;pass++)
            {
                float sag=(low+high)*.5f,total=0;Vec3 last=from;
                for(int i=1;i<points.Length;i++){float t=(float)i/(points.Length-1);Vec3 p=Vec3.Lerp(from,to,t)-Vec3.Up*(4*sag*t*(1-t));total+=(p-last).Length;last=p;}
                if(total>target)high=sag;else low=sag;
            }
            float finalSag=target-chord<.0001f?0:(low+high)*.5f;
            for(int i=0;i<points.Length;i++){float t=(float)i/(points.Length-1);points[i]=Vec3.Lerp(from,to,t)-Vec3.Up*(4*finalSag*t*(1-t));}
            points[0]=from;points[points.Length-1]=to;
        }
        public static bool Valid(FishingSnapshot value)
        {
            return value.SessionId!=Guid.Empty && value.Rod.Root.IsFinite && value.Rod.Tip.IsFinite
                && value.Rod.Forward.IsFinite && value.Rod.Right.IsFinite && value.FloatPosition.IsFinite
                && value.FloatUp.IsFinite && value.FishPosition.IsFinite && value.FishForward.IsFinite && value.FishVelocity.IsFinite
                && Scalar.IsFinite(value.Rod.PitchRadians) && Scalar.IsFinite(value.Rod.YawRadians)
                && Scalar.IsFinite(value.LineLengthMeters) && value.LineLengthMeters>=0
                && Scalar.IsFinite(value.LineTensionNewtons) && value.LineTensionNewtons>=0
                && !double.IsNaN(value.TimeSeconds) && !double.IsInfinity(value.TimeSeconds);
        }
    }

    // Per-instance monotonic event stream, independent of Unity/headless operation.
    public sealed class PresentationEventGate
    {
        Guid session;long last;
        public void Reset(Guid id){session=id;last=-1;}
        public bool Accept(FishingEvent value)
        {
            if(session==Guid.Empty||value.SessionId!=session||value.Sequence<=0||value.Sequence<=last
                ||!value.Position.IsFinite||!Scalar.IsFinite(value.Intensity01))return false;
            last=value.Sequence;return true;
        }
    }
}
