using System;
using System.Collections.Generic;

namespace YFAutomation.CargoDrones
{
    public struct CargoPoint
    {
        public readonly double X,Y,Z;
        public CargoPoint(double x,double y,double z)
        {
            if(!Valid(x)||!Valid(y)||!Valid(z))throw new ArgumentOutOfRangeException("point");X=x;Y=y;Z=z;
        }
        static bool Valid(double value){return !double.IsNaN(value)&&!double.IsInfinity(value)&&Math.Abs(value)<=10000000;}
        public double Distance(CargoPoint other){double x=X-other.X,y=Y-other.Y,z=Z-other.Z;return Math.Sqrt(x*x+y*y+z*z);}
        public CargoPoint Toward(CargoPoint target,double distance)
        {if(double.IsNaN(distance)||double.IsInfinity(distance)||distance<0)throw new ArgumentOutOfRangeException("distance");double length=Distance(target);if(length<=distance||length==0)return target;double t=distance/length;return new CargoPoint(X+(target.X-X)*t,Y+(target.Y-Y)*t,Z+(target.Z-Z)*t);}
        public CargoPosition Cell{get{return new CargoPosition((int)Math.Floor(X),(int)Math.Floor(Y),(int)Math.Floor(Z));}}
    }
    public struct CargoBox
    {
        public readonly CargoPoint Min,Max;
        public CargoBox(CargoPoint min,CargoPoint max)
        {if(min.X>max.X||min.Y>max.Y||min.Z>max.Z)throw new ArgumentException("Inverted collision bounds");Min=min;Max=max;}
        // Continuous segment vs Minkowski-expanded obstacle: the complete body is
        // tested, including obstacles between endpoints and exact corner contacts.
        public bool SweptHit(CargoPoint from,CargoPoint to,double halfWidth=.8,double halfHeight=.6)
        {
            if(double.IsNaN(halfWidth)||double.IsNaN(halfHeight)||double.IsInfinity(halfWidth)||double.IsInfinity(halfHeight)||halfWidth<0||halfHeight<0)throw new ArgumentOutOfRangeException("body");
            double enter=0,leave=1;
            return Axis(from.X,to.X-from.X,Min.X-halfWidth,Max.X+halfWidth,ref enter,ref leave)&&
                Axis(from.Y,to.Y-from.Y,Min.Y-halfHeight,Max.Y+halfHeight,ref enter,ref leave)&&
                Axis(from.Z,to.Z-from.Z,Min.Z-halfWidth,Max.Z+halfWidth,ref enter,ref leave);
        }
        static bool Axis(double origin,double delta,double min,double max,ref double enter,ref double leave)
        {
            if(Math.Abs(delta)<1e-12)return origin>=min&&origin<=max;
            double a=(min-origin)/delta,b=(max-origin)/delta;if(a>b){double swap=a;a=b;b=swap;}
            enter=Math.Max(enter,a);leave=Math.Min(leave,b);return enter<=leave;
        }
    }
    public enum CargoSweep{Clear,Blocked,Unavailable}
    public interface ICargoFlightTrace { void Trace(string kind,string detail); }
    public static class CargoTrace
    {
        public static string Point(CargoPoint p){return string.Format(System.Globalization.CultureInfo.InvariantCulture,"({0:F2},{1:F2},{2:F2})",p.X,p.Y,p.Z);}
        public static void Emit(ICargoAirspace space,string kind,string detail)
        {try{(space as ICargoFlightTrace)?.Trace(kind,detail);}catch{/* Diagnostics must never interrupt cargo or movement. */}}
    }
    public interface ICargoAirspace
    {
        CargoHold Prepare(CargoPoint from,CargoPoint segmentEnd);
        CargoSweep Sweep(CargoPoint from,CargoPoint to);
        void ReachedSegment(CargoPoint at);
    }
    public sealed class CargoMotion
    {
        readonly ICargoAirspace space;
        readonly double speed,approachSpeed;
        CargoPoint segment;
        bool hasSegment;
        readonly Queue<CargoPoint> detour=new Queue<CargoPoint>();
        CargoLocalRoute search;
        double ceiling;
        bool routeFailed;
        long blockedWait;
        const long PathRetryUnits=1000;
        readonly CargoReturnTrail trail;
        readonly long returnReserve;
        public bool ReturningHome{get;private set;}
        public bool EnergyRecall{get;private set;}
        public int ReturnWaypointCount{get{return trail.Count;}}
        public long EstimatedReturnUnits{get{return ReturningHome?(Arrived?0:trail.EstimateRemaining(Position,RemainingReturnPoints(),speed,approachSpeed)):trail.EstimateReturn(Position,speed,approachSpeed);}}
        IEnumerable<CargoPoint> RemainingReturnPoints()
        {if(hasSegment)yield return segment;foreach(var point in detour)yield return point;}
        public int RouteProbes{get;private set;}
        public CargoPoint Position{get;private set;}
        public CargoPoint Target{get;private set;}
        public CargoHold Hold{get;private set;}
        readonly CargoEnergy energy;
        public long Battery{get{return energy.Remaining;}}
        public long MovingUnits{get;private set;}
        public double DistanceTravelled{get;private set;}
        public bool Arrived{get;private set;}
        public CargoMotion(ICargoAirspace space,CargoPoint from,CargoPoint target,long battery,double speed=6,double approachSpeed=2,long returnReserve=-1)
            :this(space,from,target,new CargoEnergy(battery),speed,approachSpeed,returnReserve){}
        public CargoMotion(ICargoAirspace space,CargoPoint from,CargoPoint target,CargoEnergy energy,double speed=6,double approachSpeed=2,long returnReserve=-1)
        {
            if(space==null||energy==null||returnReserve< -1||double.IsNaN(speed)||double.IsInfinity(speed)||speed<=0||speed>100||double.IsNaN(approachSpeed)||approachSpeed<=0||approachSpeed>speed)throw new ArgumentException("Invalid flight motion");
            this.space=space;this.speed=speed;this.approachSpeed=approachSpeed;Position=from;Target=target;this.energy=energy;ceiling=Math.Min(253,Math.Max(from.Y,target.Y)+24);
            trail=new CargoReturnTrail(from);this.returnReserve=returnReserve;
        }
        public void Retarget(CargoPoint target)
        {
            CargoTrace.Emit(space,"retarget","from="+CargoTrace.Point(Position)+" target="+CargoTrace.Point(target));
            if(ReturningHome)throw new InvalidOperationException("A returning motion cannot discard its home corridor; start a new motion after docking");
            Target=target;ceiling=Math.Min(253,Math.Max(Position.Y,target.Y)+24);RetryPath();Arrived=false;Hold=CargoHold.None;
        }
        public void RetargetVia(CargoPoint target,IEnumerable<CargoPoint> waypoints)
        {
            Retarget(target);if(waypoints==null)throw new ArgumentNullException("waypoints");
            var from=Position;double highest=Math.Max(Position.Y,target.Y);
            foreach(var waypoint in waypoints)
            {
                highest=Math.Max(highest,waypoint.Y);
                while(from.Distance(waypoint)>1e-7){from=from.Toward(waypoint,16);detour.Enqueue(from);}
            }
            while(from.Distance(target)>1e-7){from=from.Toward(target,16);detour.Enqueue(from);}
            // A high entrance can sit above both the source and an underground
            // target. Keep twelve more blocks of detour headroom above the
            // authored cruise corridor, equivalent to +24 over its endpoint.
            ceiling=Math.Min(253,Math.Max(ceiling,highest+12));
            CargoTrace.Emit(space,"route-via","target="+CargoTrace.Point(target)+" ceiling="+ceiling+" remaining="+string.Join(";",System.Linq.Enumerable.Select(detour,CargoTrace.Point)));
        }
        internal CargoMotionState Capture()
        {
            var remaining=new List<CargoPoint>();if(hasSegment)remaining.Add(segment);remaining.AddRange(detour);
            return new CargoMotionState(Position,Target,trail.Capture(),remaining.ToArray(),ReturningHome,EnergyRecall,Arrived,routeFailed,Hold,speed,approachSpeed,returnReserve,MovingUnits,DistanceTravelled);
        }
        internal CargoMotion(ICargoAirspace space,CargoEnergy energy,CargoMotionState saved)
            :this(space,saved.Trail[0],saved.Target,energy,saved.Speed,saved.ApproachSpeed,saved.Reserve)
        {
            trail=CargoReturnTrail.Restore(saved.Trail);Position=saved.Position;ReturningHome=saved.Returning;EnergyRecall=saved.EnergyRecall;Arrived=saved.Arrived;
            routeFailed=saved.Blocked;Hold=saved.Hold;MovingUnits=saved.MovingUnits;DistanceTravelled=saved.DistanceTravelled;
            // Persisted waypoints are intentions, not collision clearance. Every
            // resumed edge passes Prepare/Sweep again before the first movement.
            foreach(var point in saved.Remaining)detour.Enqueue(point);
            double highest=Math.Max(Position.Y,Target.Y);foreach(var point in saved.Remaining)highest=Math.Max(highest,point.Y);
            ceiling=Math.Min(253,Math.Max(Math.Max(Position.Y,Target.Y)+24,highest+12));
        }
        public void RetryPath()
        {
            CargoTrace.Emit(space,"retry","pos="+CargoTrace.Point(Position)+" target="+CargoTrace.Point(Target)+" returning="+ReturningHome+" remaining="+detour.Count);
            // On return, keep the current edge and remaining corridor. Clearing
            // them here would silently replace an obstructed route with a shortcut.
            if(!ReturningHome){hasSegment=false;detour.Clear();}search=null;routeFailed=false;blockedWait=0;
        }
        public void ReturnHome()
        {
            CargoTrace.Emit(space,"return","pos="+CargoTrace.Point(Position)+" home="+CargoTrace.Point(trail.Home)+" battery="+Battery+" energyRecall="+EnergyRecall);
            if(ReturningHome){RetryPath();return;}
            hasSegment=false;detour.Clear();search=null;routeFailed=false;ReturningHome=true;
            Target=trail.Home;foreach(var point in trail.ReverseWaypoints())detour.Enqueue(point);
            Arrived=detour.Count==0;Hold=CargoHold.None;
        }
        void Blocked()
        {
            CargoTrace.Emit(space,"blocked","from="+CargoTrace.Point(Position)+" to="+CargoTrace.Point(segment)+" returning="+ReturningHome+" ceiling="+ceiling);
            if(ReturningHome){routeFailed=true;Hold=CargoHold.PathBlocked;return;}
            // Preserve later mandatory waypoints while locally routing around the
            // blocked edge. Entrance and approach points must never disappear.
            var later=detour.ToArray();detour.Clear();search=new CargoLocalRoute(space,Position,segment,ceiling);
            foreach(var point in later)detour.Enqueue(point);Hold=CargoHold.PathBlocked;
        }
        public void Tick(long elapsedUnits,bool ownerOnline=true,bool paused=false)
        {
            if(elapsedUnits<0)throw new ArgumentOutOfRangeException("elapsedUnits");
            if(!ownerOnline||paused){Hold=CargoHold.OwnerOffline;return;}
            if(Arrived){Hold=CargoHold.None;return;}
            if(elapsedUnits==0)return;
            if(routeFailed)
            {
                // Transient entities (including the player operating the hub)
                // must not latch navigation forever. Retry at a bounded rate,
                // keeping the recorded return corridor and all collision checks.
                Hold=CargoHold.PathBlocked;blockedWait+=Math.Min(100,elapsedUnits);
                if(blockedWait<PathRetryUnits)return;
                RetryPath();
            }
            if(search!=null)
            {
                search.Advance();Hold=search.Hold;
                if(!search.Complete&&!search.Failed)return;
                RouteProbes+=search.Probes;
                if(search.Failed){routeFailed=true;return;}
                var later=detour.ToArray();detour.Clear();foreach(var point in search.Waypoints)detour.Enqueue(point);foreach(var point in later)detour.Enqueue(point);
                search=null;hasSegment=false;
                // Planning can leave the native lookahead on the last probe.
                // Reacquire the actual first edge on the next tick before moving.
                return;
            }
            if(!hasSegment){segment=detour.Count>0?detour.Dequeue():Position.Toward(Target,16);hasSegment=true;CargoTrace.Emit(space,"segment","from="+CargoTrace.Point(Position)+" to="+CargoTrace.Point(segment)+" target="+CargoTrace.Point(Target)+" remaining="+detour.Count);}
            Hold=space.Prepare(Position,segment);if(Hold!=CargoHold.None)return;
            var validation=space.Sweep(Position,segment);
            if(validation!=CargoSweep.Clear){if(validation==CargoSweep.Blocked)Blocked();else Hold=CargoHold.ChunkLoading;return;}
            // Never repay stalled frames with a long unswept jump. Movement and
            // energy are advanced together, using at most one 100 ms simulation step.
            long step=Math.Min(100,elapsedUnits);double remaining=Position.Distance(segment);
            double velocity=Position.Distance(Target)<=4?approachSpeed:speed;
            if(remaining<1e-7)
            {space.ReachedSegment(Position);hasSegment=false;Arrived=Position.Distance(Target)<1e-7;return;}
            long used=Math.Min(step,(long)Math.Ceiling(remaining/velocity*1000));
            if(used>Battery){Hold=CargoHold.RecoveryRequired;return;}
            var next=Position.Toward(segment,velocity*used/1000.0);
            if(!ReturningHome)
            {
                if(!trail.CanRecord(next)){Hold=CargoHold.RecoveryRequired;return;}
                if(returnReserve>=0&&(Battery-used<returnReserve||trail.EstimateReturn(next,speed,approachSpeed)>Battery-used-returnReserve))
                {EnergyRecall=true;ReturnHome();return;}
            }
            // Recheck each actual movement slice. New obstacles cannot be ignored
            // merely because a segment passed planning earlier.
            validation=space.Sweep(Position,next);
            if(validation!=CargoSweep.Clear){if(validation==CargoSweep.Blocked)Blocked();else Hold=CargoHold.ChunkLoading;return;}
            double distance=Position.Distance(next);if(!ReturningHome)trail.Record(next);Position=next;energy.Consume(used);MovingUnits+=used;DistanceTravelled+=distance;
            if(Position.Distance(segment)<1e-7){space.ReachedSegment(Position);hasSegment=false;Arrived=Position.Distance(Target)<1e-7;}
        }
    }
}
