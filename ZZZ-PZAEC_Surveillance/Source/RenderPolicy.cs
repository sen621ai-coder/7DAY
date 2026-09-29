using System;
using System.Collections.Generic;

namespace PZAEC.Surveillance
{
    // Pure scheduling rules also exercised by the offline harness.
    public sealed class FeedClock
    {
        public double Due,LastSuccess=-1;
        public int Failures;
        public void Success(double now,int hz){LastSuccess=now;Due=now+1d/hz;Failures=0;}
        public void Failure(double now){Failures++;Due=now+(Failures==1?.1:Failures==2?.25:.5);}
        public bool Fresh(double now,int hz)=>LastSuccess>=0&&now-LastSuccess<=Math.Max(.5,1.5d/Math.Max(1,hz));
    }
    public static class RenderPolicy
    {
        public const int MaxActive=4,MaxIdle=4;
        public const float NearDistance=12f;
        public const int DistantHz=2;
        public static bool IsDistant(float distance)=>distance>NearDistance;
        public static int TierForDistance(int tier,float distance)=>IsDistant(distance)?0:tier;
        public static int RateForDistance(int count,bool focused,bool anyFocus,int pressure,bool hasNearViewer)
        {return hasNearViewer?Rate(count,focused,anyFocus,pressure):DistantHz;}
        public static int Rate(int count,bool focused,bool anyFocus,int pressure)
        {return pressure>=3?5:count<=2?10:anyFocus?(focused?10:5):6;}
        public static int Tier(float height,int previous)
        {
            if(previous<0)return height>=480?2:height>=240?1:0;
            if(previous==2)return height<408?(height<204?0:1):2;
            if(previous==1)return height>552?2:height<204?0:1;
            return height>552?2:height>276?1:0;
        }
        public static int Width(int tier)=>tier>=2?768:tier==1?512:384;
        public static int Pick(IList<FeedClock> clocks,double now)
        {
            int chosen=-1;double earliest=double.MaxValue;
            for(int i=0;i<clocks.Count;i++)if(clocks[i].Due<=now&&clocks[i].Due<earliest)
            {chosen=i;earliest=clocks[i].Due;}
            return chosen;
        }
    }
    public sealed class QualityGate
    {
        public int Tier=-1;
        int pending=-1;
        double since,nextChange;
        public int Update(int requested,double now)
        {
            if(Tier<0){Tier=requested;return Tier;}
            if(requested!=pending){pending=requested;since=now;}
            if(requested!=Tier&&now>=nextChange&&now-since>=(requested>Tier?.3:1))
            {Tier=requested;nextChange=now+2;}
            return Tier;
        }
    }
    public sealed class ViewGate
    {
        public const double LeaveGraceSeconds=.85;
        public const float EnterDistance=48f,ExitDistance=52f;
        public bool Watching;
        double seen=-100;
        public bool Update(float distance,bool visible,double now)
        {
            if(distance>(Watching?ExitDistance:EnterDistance)){Watching=false;return false;}
            if(visible){seen=now;Watching=true;}
            if(now-seen>LeaveGraceSeconds)Watching=false;
            return Watching;
        }
    }
}
