using System;
using PZAEC.Fishing.Contracts;

namespace PZAEC.Fishing.Runtime
{
    public enum FloatSignal { Casting, Settling, Waiting, Tapping, Downstroke, Submerged, Rising, Traveling, Fighting }
    public struct FloatReadoutState
    {
        public float VisibleMarks, RestMarks;
        public FloatSignal Signal;
        public bool CanStrike;
    }
    // Geometry matches the six .018 m bands of the .27 m source float prefab.
    // Read the simulation once per tick; GUI repaints never drive or invent a bite.
    public sealed class FloatReadout
    {
        Guid session;
        long tick=-1;
        double time,downUntil;
        float lastMarks;
        Vec3 restPosition;
        public FloatReadoutState Current {get;private set;}
        public void Reset(){session=Guid.Empty;tick=-1;time=downUntil=0;Current=default(FloatReadoutState);}
        public static float Marks(float submerged,float upY=1)
        {
            if(!Scalar.IsFinite(submerged)||!Scalar.IsFinite(upY))return 0;
            return (float)Math.Max(0,Math.Min(6,((.5-submerged)*.27+.133*Math.Max(0,Math.Min(1,upY)))/.018));
        }
        public void Observe(FishingSnapshot snapshot,FloatConfig config)
        {
            if(config==null)return;
            if(snapshot.SessionId!=session){Reset();session=snapshot.SessionId;lastMarks=Marks(config.RestSubmerged01);restPosition=snapshot.FloatPosition;}
            if(snapshot.Tick==tick)return;
            float marks=Marks(snapshot.FloatSubmerged01,snapshot.FloatUp.Y);
            float rest=Marks(config.RestSubmerged01);
            double dt=snapshot.TimeSeconds-time;
            bool watching=snapshot.Phase==FishingPhase.Waiting||snapshot.Phase==FishingPhase.Nibbling||snapshot.Phase==FishingPhase.BiteWindow;
            var travel=snapshot.FloatPosition-restPosition;
            if(watching&&dt>0&&dt<=.25&&lastMarks-marks>.025f&&(lastMarks-marks)/dt>2)downUntil=snapshot.TimeSeconds+.32;
            var signal=FloatSignal.Waiting;
            if(snapshot.Phase==FishingPhase.Casting)signal=FloatSignal.Casting;
            else if(snapshot.Phase==FishingPhase.Settling)signal=FloatSignal.Settling;
            else if(snapshot.Phase==FishingPhase.Hooked||snapshot.Phase==FishingPhase.Fighting||snapshot.Phase==FishingPhase.Landing)signal=FloatSignal.Fighting;
            else if(watching) {
                if(marks<=.15f)signal=FloatSignal.Submerged;
                else if(marks>=rest+.65f)signal=FloatSignal.Rising;
                else if(snapshot.TimeSeconds<downUntil)signal=FloatSignal.Downstroke;
                else if(travel.X*travel.X+travel.Z*travel.Z>.0036f)signal=FloatSignal.Traveling;
                else if(Math.Abs(marks-rest)>.12f)signal=FloatSignal.Tapping;
            }
            if(snapshot.Phase==FishingPhase.Settling)restPosition=snapshot.FloatPosition;
            // No hidden bite eligibility escapes into the readout or its accent colour.
            Current=new FloatReadoutState {VisibleMarks=marks,RestMarks=rest,Signal=signal,CanStrike=false};
            lastMarks=marks;time=snapshot.TimeSeconds;tick=snapshot.Tick;
        }
    }
}
