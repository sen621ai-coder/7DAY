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
        CargoPoint segment;bool hasSegment;
        readonly Queue<CargoPoint> detour=new Queue<CargoPoint>();
        readonly Queue<CargoAnchor> anchors=new Queue<CargoAnchor>();
        CargoLocalRoute quick;CargoPathSearch search;CargoPoint searchGoal;
        CargoRouteFailure lastFailure;CargoPathSearch preflight;ICargoAirspace preflightSpace;double nextPreflight;
        double ceiling;bool routeFailed;long blockedWait,fullRetryWait,dynamicWait;
        long generation;int windowChoice;
        readonly CargoReturnTrail trail;readonly long returnReserve;
        public CargoNavigationStatus Navigation{get;private set;}
        public bool ReturningHome{get;private set;}
        public bool EnergyRecall{get;private set;}
        public CargoPoint Home{get{return trail.Home;}}
        public int ReturnWaypointCount{get{return trail.Count;}}
        public long EstimatedReturnUnits{get{return ReturningHome?new CargoReturnTrail(trail.Home).EstimateRemaining(Position,RemainingReturnPoints(),speed,approachSpeed):trail.EstimateReturn(Position,speed,approachSpeed);}}
        IEnumerable<CargoPoint> RemainingReturnPoints(){return ExecutionPoints();}
        public int RouteProbes{get;private set;}
        public CargoPoint Position{get;private set;}
        public CargoPoint Target{get;private set;}
        public long Battery{get{return energy.Remaining;}}
        readonly CargoEnergy energy;
        public CargoHold Hold{get;private set;}
        public bool Arrived{get;private set;}
        public long MovingUnits{get;private set;}
        public double DistanceTravelled{get;private set;}
        public CargoMotion(ICargoAirspace space,CargoPoint from,CargoPoint target,long battery,double speed=6,double approachSpeed=2,long returnReserve=-1)
            :this(space,from,target,new CargoEnergy(battery),speed,approachSpeed,returnReserve){}
        public CargoMotion(ICargoAirspace space,CargoPoint from,CargoPoint target,CargoEnergy energy,double speed=6,double approachSpeed=2,long returnReserve=-1)
        {
            if(space==null||energy==null||returnReserve< -1||double.IsNaN(speed)||double.IsInfinity(speed)||speed<=0||speed>100||double.IsNaN(approachSpeed)||approachSpeed<=0||approachSpeed>speed)throw new ArgumentException("Invalid motion");
            this.space=space;this.speed=speed;this.approachSpeed=approachSpeed;this.energy=energy;this.returnReserve=returnReserve;
            Position=from;Target=target;trail=new CargoReturnTrail(from);ceiling=Math.Min(253,Math.Max(from.Y,target.Y)+24);
            anchors.Enqueue(new CargoAnchor(target,CargoLegKind.Outdoor));
        }
        void CancelSearch(){var native=space as ICargoPlanningSpace;if(native!=null){if(quick!=null)native.CancelPlanning(quick);if(search!=null)native.CancelPlanning(search);}if(preflight!=null)(preflightSpace as ICargoPlanningSpace)?.CancelPlanning(preflight);quick=null;search=null;preflight=null;preflightSpace=null;generation++;}
        public void Retarget(CargoPoint target)
        {
            if(ReturningHome)throw new InvalidOperationException("Returning corridor cannot be retargeted");
            CancelSearch();lastFailure=null;preflight=null;hasSegment=false;detour.Clear();anchors.Clear();Target=target;anchors.Enqueue(new CargoAnchor(target,CargoLegKind.Outdoor));
            ceiling=Math.Min(253,Math.Max(Position.Y,target.Y)+24);routeFailed=false;blockedWait=fullRetryWait=dynamicWait=0;windowChoice=0;Arrived=false;Hold=CargoHold.None;Navigation=CargoNavigationStatus.Ready;
            CargoTrace.Emit(space,"retarget","from="+CargoTrace.Point(Position)+" target="+CargoTrace.Point(target)+" generation="+generation);
        }
        public void RetargetVia(CargoPoint target,IEnumerable<CargoPoint> waypoints,int indoorFrom=-1)
        {
            if(waypoints==null)throw new ArgumentNullException("waypoints");Retarget(target);anchors.Clear();int i=0;double highest=Math.Max(Position.Y,target.Y);
            foreach(var p in waypoints){highest=Math.Max(highest,p.Y);anchors.Enqueue(new CargoAnchor(p,indoorFrom>=0&&i>=indoorFrom?CargoLegKind.Entrance:CargoLegKind.Outdoor));i++;}
            anchors.Enqueue(new CargoAnchor(target,indoorFrom>=0?CargoLegKind.Indoor:CargoLegKind.Outdoor));
            ceiling=Math.Min(253,Math.Max(ceiling,highest+12));
            CargoTrace.Emit(space,"route-via","generation="+generation+" target="+CargoTrace.Point(target)+" anchors="+string.Join(";",System.Linq.Enumerable.Select(anchors,a=>a.Kind+":"+CargoTrace.Point(a.Point))));
        }
        CargoPoint[] ExecutionPoints()
        {
            var result=new List<CargoPoint>();var from=Position;
            Action<CargoPoint> add=to=>{while(from.Distance(to)>1e-7){from=from.Toward(to,16);result.Add(from);}};
            if(hasSegment)add(segment);foreach(var p in detour)add(p);
            foreach(var a in anchors)add(a.Point);
            return result.ToArray();
        }
        internal CargoMotionState Capture()
        {return new CargoMotionState(Position,Target,trail.Capture(),ExecutionPoints(),ReturningHome,EnergyRecall,Arrived,routeFailed,Hold,speed,approachSpeed,returnReserve,MovingUnits,DistanceTravelled,anchors.ToArray(),generation,Navigation,lastFailure);}
        internal CargoMotion(ICargoAirspace space,CargoEnergy energy,CargoMotionState saved)
            :this(space,saved.Trail[0],saved.Target,energy,saved.Speed,saved.ApproachSpeed,saved.Reserve)
        {
            trail=CargoReturnTrail.Restore(saved.Trail);Position=saved.Position;ReturningHome=saved.Returning;EnergyRecall=saved.EnergyRecall;Arrived=saved.Arrived;
            routeFailed=saved.Blocked;Hold=saved.Hold;MovingUnits=saved.MovingUnits;DistanceTravelled=saved.DistanceTravelled;Navigation=saved.NavigationStatus;generation=saved.Generation+1;lastFailure=saved.LastFailure;anchors.Clear();
            if(saved.Navigation!=null)
            {
                foreach(var a in saved.Navigation)anchors.Enqueue(a);
                if(anchors.Count>0)foreach(var p in saved.Remaining){detour.Enqueue(p);if(p.Distance(anchors.Peek().Point)<1e-7)break;}
            }
            else
            {
                // Legacy points at turns are conservative constraints; collinear
                // 16-block samples are not destinations. Never shortcut returns.
                var points=new List<CargoPoint>(saved.Remaining);if(points.Count==0&&!Arrived)points.Add(Target);
                var last=Position;
                for(int i=0;i<points.Count;i++)
                {
                    if(!ReturningHome&&i+1<points.Count&&Math.Abs(last.Distance(points[i])+points[i].Distance(points[i+1])-last.Distance(points[i+1]))<1e-7)continue;
                    anchors.Enqueue(new CargoAnchor(points[i],ReturningHome?CargoLegKind.Return:CargoLegKind.Indoor));last=points[i];
                }
            }
            double highest=Math.Max(Position.Y,Target.Y);foreach(var a in anchors)highest=Math.Max(highest,a.Point.Y);ceiling=Math.Min(253,highest+24);
        }
        public void SuspendPlanning(){CancelSearch();}
        public void InvalidateFailure(){lastFailure=null;nextPreflight=0;CancelSearch();}
        internal void RequireMigration(){CancelSearch();Navigation=CargoNavigationStatus.MigrationRequired;Hold=CargoHold.PathBlocked;routeFailed=true;}
        public void RetryPath()
        {
            CancelSearch();routeFailed=false;blockedWait=fullRetryWait=dynamicWait=0;windowChoice=0;
            CargoTrace.Emit(space,"retry","pos="+CargoTrace.Point(Position)+" generation="+generation+" anchors="+anchors.Count+" returning="+ReturningHome);
        }
        public void ReturnHome()
        {
            if(ReturningHome){RetryPath();return;}
            CancelSearch();hasSegment=false;detour.Clear();anchors.Clear();routeFailed=false;ReturningHome=true;Target=trail.Home;
            foreach(var p in trail.ReverseWaypoints())anchors.Enqueue(new CargoAnchor(p,CargoLegKind.Return));
            blockedWait=fullRetryWait=dynamicWait=0;Arrived=anchors.Count==0;Hold=CargoHold.None;Navigation=CargoNavigationStatus.Ready;
            CargoTrace.Emit(space,"return","pos="+CargoTrace.Point(Position)+" home="+CargoTrace.Point(Target)+" battery="+Battery+" energyRecall="+EnergyRecall);
        }
        public void ReturnHomeVia(IEnumerable<CargoAnchor> route)
        {
            if(ReturningHome){RetryPath();return;}
            var points=new List<CargoAnchor>(route);
            points.Add(new CargoAnchor(trail.Home,CargoLegKind.Outdoor));
            // Retain the proven corridor for emergency recalls. Only a completed
            // delivery may request this new route; every edge still gets swept.
            long cost=new CargoReturnTrail(trail.Home).EstimateRemaining(Position,System.Linq.Enumerable.Select(points,p=>p.Point),speed,approachSpeed);
            if(cost>Battery){ReturnHome();return;}
            CancelSearch();hasSegment=false;detour.Clear();anchors.Clear();routeFailed=false;ReturningHome=true;Target=trail.Home;
            double highest=Math.Max(Position.Y,Target.Y);
            foreach(var point in points){anchors.Enqueue(point);highest=Math.Max(highest,point.Point.Y);}
            ceiling=Math.Min(253,highest+24);blockedWait=fullRetryWait=dynamicWait=0;windowChoice=0;Arrived=false;Hold=CargoHold.None;Navigation=CargoNavigationStatus.Ready;
            CargoTrace.Emit(space,"return-direct","pos="+CargoTrace.Point(Position)+" home="+CargoTrace.Point(Target)+" anchors="+string.Join(";",System.Linq.Enumerable.Select(points,p=>p.Kind+":"+CargoTrace.Point(p.Point))));
        }
        void StartSearch(bool skipQuick=false)
        {
            if(anchors.Count==0)return;
            var anchor=anchors.Peek();searchGoal=anchor.Point;
            bool indoor=anchor.Kind!=CargoLegKind.Outdoor;
            if(!indoor&&Position.Distance(searchGoal)>32)
            {
                // Window exits are replaceable. A blocked sample can be moved
                // laterally; it must never become an immutable point in a wall.
                var goal=Position.Toward(searchGoal,32);double dx=searchGoal.X-Position.X,dz=searchGoal.Z-Position.Z,len=Math.Sqrt(dx*dx+dz*dz);
                if(windowChoice>0&&len>1e-7){double offset=((windowChoice+1)/2)*2*(windowChoice%2==1?1:-1);goal=new CargoPoint(goal.X-dz/len*offset,goal.Y,goal.Z+dx/len*offset);}
                searchGoal=goal;
            }
            detour.Clear();hasSegment=false;routeFailed=false;blockedWait=0;
            if(!indoor&&!skipQuick&&Position.Distance(searchGoal)<=32.001)quick=new CargoLocalRoute(space,Position,searchGoal,ceiling);
            else search=new CargoPathSearch(space,Position,searchGoal,indoor);
            Navigation=CargoNavigationStatus.Planning;Hold=CargoHold.PathBlocked;
        }
        bool SpendRoute(CargoPoint[] points)
        {
            if(returnReserve<0)return true;

            long needed;
            if(ReturningHome)
            {
                var remaining=new List<CargoPoint>(points);bool first=true;var from=searchGoal;
                foreach(var a in anchors){if(first){first=false;continue;}while(from.Distance(a.Point)>1e-7){from=from.Toward(a.Point,16);remaining.Add(from);}}
                needed=new CargoReturnTrail(trail.Home).EstimateRemaining(Position,remaining,speed,approachSpeed);
            }
            else
            {
                var future=new List<CargoPoint>(points);var from=searchGoal;bool indoor=false;
                foreach(var a in anchors){indoor|=a.Kind==CargoLegKind.Indoor||a.Kind==CargoLegKind.Entrance;while(from.Distance(a.Point)>1e-7){from=from.Toward(a.Point,16);future.Add(from);}}
                long outbound=new CargoReturnTrail(Target).EstimateRemaining(Position,future,speed,approachSpeed);
                long reverse=new CargoReturnTrail(trail.Home).EstimateRemaining(Position,future,speed,approachSpeed);
                needed=outbound+reverse+trail.EstimateReturn(Position,speed,approachSpeed)+4000+(indoor?20000:0);
            }
            CargoTrace.Emit(space,"route-energy","needed="+needed+" battery="+Battery+" reserve="+returnReserve+" returning="+ReturningHome);
            if(Battery-returnReserve>=needed)return true;
            Navigation=CargoNavigationStatus.SearchBudgetExceeded;
            if(!ReturningHome){EnergyRecall=true;ReturnHome();}else{routeFailed=true;Hold=CargoHold.PathBlocked;}
            return false;
        }
        void Accept(CargoPoint[] points)
        {
            if(!SpendRoute(points))return;if(!ReturningHome)lastFailure=null;
            foreach(var p in points)detour.Enqueue(p);hasSegment=false;Navigation=CargoNavigationStatus.Ready;Hold=CargoHold.None;windowChoice=0;
        }
        void Failed(CargoNavigationStatus status)
        {
            if(!ReturningHome&&anchors.Count>0&&status!=CargoNavigationStatus.DynamicBlocked)lastFailure=new CargoRouteFailure(Position,searchGoal,anchors.Peek().Kind!=CargoLegKind.Outdoor);
            Navigation=status;routeFailed=true;Hold=CargoHold.PathBlocked;blockedWait=fullRetryWait=0;quick=null;search=null;
            CargoTrace.Emit(space,"navigation-failed","status="+status+" generation="+generation+" pos="+CargoTrace.Point(Position));
        }
        // A failed cargo delivery must pass an on-dock route check before it
        // can repeat the same flight. No cargo or movement happens here.
        public bool PreflightFailure(ICargoAirspace probeSpace,double now)
        {
            if(lastFailure==null)return true;
            if(now<nextPreflight)return false;
            if(preflight==null){preflightSpace=probeSpace;preflight=new CargoPathSearch(probeSpace,lastFailure.From,lastFailure.Goal,lastFailure.Indoor);}
            preflight.Advance();Navigation=preflight.Status;
            if(preflight.Complete){lastFailure=null;preflight=null;Navigation=CargoNavigationStatus.Ready;return true;}
            if(preflight.Failed){preflight=null;nextPreflight=now+5;}
            return false;
        }
        void Blocked()
        {
            var native=space as ICargoPlanningSpace;
            if(native!=null&&native.DynamicObstacle){Navigation=CargoNavigationStatus.DynamicBlocked;routeFailed=true;Hold=CargoHold.PathBlocked;blockedWait=0;return;}
            StartSearch();
        }
        public void Tick(long elapsedUnits,bool ownerOnline=true,bool paused=false)
        {
            if(elapsedUnits<0)throw new ArgumentOutOfRangeException("elapsedUnits");
            if(!ownerOnline||paused){CancelSearch();Hold=CargoHold.OwnerOffline;return;}
            if(Arrived){Hold=CargoHold.None;return;}if(Navigation==CargoNavigationStatus.MigrationRequired){Hold=CargoHold.PathBlocked;return;}if(elapsedUnits==0)return;
            if(routeFailed)
            {
                long step=Math.Min(100,elapsedUnits);blockedWait+=step;fullRetryWait+=step;Hold=CargoHold.PathBlocked;
                if(blockedWait<1000)return;blockedWait=0;
                if(!hasSegment&&anchors.Count>0){segment=Position.Toward(anchors.Peek().Point,16);hasSegment=true;}
                var hold=space.Prepare(Position,segment);if(hold!=CargoHold.None){Hold=hold;return;}
                var sweep=space.Sweep(Position,segment);
                if(sweep==CargoSweep.Clear){RetryPath();Navigation=CargoNavigationStatus.Ready;}
                else
                {
                    if(sweep==CargoSweep.Unavailable){Hold=CargoHold.ChunkLoading;return;}
                    if(Navigation==CargoNavigationStatus.DynamicBlocked){dynamicWait+=1000;if(dynamicWait<3000)return;}
                    else if(fullRetryWait<5000)return;
                    StartSearch();return;
                }
            }
            if(quick!=null)
            {
                quick.Advance();Hold=quick.Hold;Navigation=Hold==CargoHold.ChunkLoading?CargoNavigationStatus.WaitingData:Hold==CargoHold.ChunkBudget?CargoNavigationStatus.WaitingBudget:CargoNavigationStatus.Planning;
                if(!quick.Complete&&!quick.Failed)return;
                RouteProbes+=quick.Probes;
                if(quick.Failed){quick=null;search=new CargoPathSearch(space,Position,searchGoal,false);return;}
                var points=quick.Waypoints;quick=null;Accept(points);return;
            }
            if(search!=null)
            {
                search.Advance();Hold=search.Hold;Navigation=search.Status;
                if(!search.Complete&&!search.Failed)return;
                RouteProbes+=search.Probes;
                if(search.Failed)
                {
                    if(search.Status==CargoNavigationStatus.GoalBlocked&&anchors.Count>0&&searchGoal.Distance(anchors.Peek().Point)>1e-7&&windowChoice<8){windowChoice++;search=null;StartSearch(true);return;}
                    Failed(search.Status);return;
                }
                var points=search.Waypoints;search=null;Accept(points);return;
            }
            while(anchors.Count>0&&Position.Distance(anchors.Peek().Point)<1e-7){anchors.Dequeue();windowChoice=0;}
            if(anchors.Count==0){Arrived=Position.Distance(Target)<1e-7;Hold=CargoHold.None;return;}
            if(!hasSegment){segment=detour.Count>0?detour.Dequeue():Position.Toward(anchors.Peek().Point,16);hasSegment=true;CargoTrace.Emit(space,"segment","from="+CargoTrace.Point(Position)+" to="+CargoTrace.Point(segment)+" anchor="+CargoTrace.Point(anchors.Peek().Point));}
            Hold=space.Prepare(Position,segment);if(Hold!=CargoHold.None)return;
            var validation=space.Sweep(Position,segment);
            if(validation!=CargoSweep.Clear){if(validation==CargoSweep.Blocked)Blocked();else Hold=CargoHold.ChunkLoading;return;}
            long elapsed=Math.Min(100,elapsedUnits);double remaining=Position.Distance(segment);double velocity=Position.Distance(Target)<=4?approachSpeed:speed;
            if(remaining<1e-7){space.ReachedSegment(Position);hasSegment=false;return;}
            long used=Math.Min(elapsed,(long)Math.Ceiling(remaining/velocity*1000));if(used>Battery){Hold=CargoHold.RecoveryRequired;return;}
            var next=Position.Toward(segment,velocity*used/1000.0);
            if(!ReturningHome)
            {
                if(!trail.CanRecord(next)){Hold=CargoHold.RecoveryRequired;return;}
                if(returnReserve>=0&&(Battery-used<returnReserve||trail.EstimateReturn(next,speed,approachSpeed)>Battery-used-returnReserve)){EnergyRecall=true;ReturnHome();return;}
            }
            validation=space.Sweep(Position,next);
            if(validation!=CargoSweep.Clear){if(validation==CargoSweep.Blocked)Blocked();else Hold=CargoHold.ChunkLoading;return;}
            double distance=Position.Distance(next);if(!ReturningHome)trail.Record(next);Position=next;energy.Consume(used);MovingUnits+=used;DistanceTravelled+=distance;Navigation=CargoNavigationStatus.Moving;
            if(Position.Distance(segment)<1e-7){space.ReachedSegment(Position);hasSegment=false;while(anchors.Count>0&&Position.Distance(anchors.Peek().Point)<1e-7)anchors.Dequeue();Arrived=anchors.Count==0&&Position.Distance(Target)<1e-7;}
        }
    }
}
