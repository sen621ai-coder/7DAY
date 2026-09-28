using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace YFAutomation.CargoDrones
{
    public enum CargoNavigationStatus { Ready,Moving,Planning,Queued,WaitingData,WaitingBudget,DynamicBlocked,GoalBlocked,NoPathInBounds,SearchBudgetExceeded,RouteInvalidated,MigrationRequired }
    public enum CargoLegKind { Outdoor,Entrance,Indoor,Return }
    public struct CargoAnchor
    {
        public readonly CargoPoint Point;public readonly CargoLegKind Kind;
        public CargoAnchor(CargoPoint point,CargoLegKind kind){Point=point;Kind=kind;}
    }
    public sealed class CargoRouteFailure
    {
        public readonly CargoPoint From,Goal;public readonly bool Indoor;
        public CargoRouteFailure(CargoPoint from,CargoPoint goal,bool indoor){From=from;Goal=goal;Indoor=indoor;}
    }
    // Optional native services. Tests use the same search with pure geometry.
    public interface ICargoPlanningSpace
    {
        bool BeginPlanning(object search);
        void EndPlanning(double milliseconds,int probes);
        void CancelPlanning(object search);
        CargoSweep PlanningSweep(CargoPoint from,CargoPoint to);
        bool DynamicObstacle{get;}
    }
    public sealed class CargoPathSearch
    {
        struct Key : IEquatable<Key>
        {
            public int X,Y,Z;public Key(int x,int y,int z){X=x;Y=y;Z=z;}
            public bool Equals(Key k){return X==k.X&&Y==k.Y&&Z==k.Z;}
            public override bool Equals(object o){return o is Key&&Equals((Key)o);}
            public override int GetHashCode(){unchecked{return (X*397^Y)*397^Z;}}
        }
        sealed class Node { public Key Key;public CargoPoint Point;public double G=double.PositiveInfinity,H;public Node Parent;public int Revision; }
        struct Entry { public Node Node;public double G,F;public int Revision,Order; }
        readonly List<Entry> heap=new List<Entry>();
        readonly Dictionary<Key,Node> nodes=new Dictionary<Key,Node>();
        readonly ICargoAirspace space;readonly ICargoPlanningSpace native;
        readonly CargoPoint origin,goal;readonly bool indoor;
        readonly int maxNodes,maxProbes;readonly double maxCpu;
        Node current,finish;List<CargoPoint> neighbors;int neighborIndex,stage,order,stageNodes;
        double grid,minX,maxX,minY,maxY,minZ,maxZ;
        bool goalChecked,startChecked,incompleteStage;
        readonly Stopwatch wallClock=Stopwatch.StartNew();double lastProgress;
        CargoPoint[] raw;readonly List<CargoPoint> smooth=new List<CargoPoint>();int smoothAt,smoothTry;
        public int Nodes{get;private set;}public int Probes{get;private set;}public double CpuMilliseconds{get;private set;}
        public CargoNavigationStatus Status{get;private set;}
        public CargoHold Hold{get;private set;}
        public bool Complete{get;private set;}public bool Failed{get;private set;}
        public CargoPoint[] Waypoints{get;private set;}
        public CargoPathSearch(ICargoAirspace space,CargoPoint origin,CargoPoint goal,bool indoor,int maxNodes=8192,int maxProbes=16384,double maxCpu=100)
        {
            this.space=space??throw new ArgumentNullException("space");native=space as ICargoPlanningSpace;
            this.origin=origin;this.goal=goal;this.indoor=indoor;this.maxNodes=maxNodes;this.maxProbes=maxProbes;this.maxCpu=native!=null&&maxCpu==100?1500:maxCpu;
            if(maxNodes<1||maxProbes<1||maxCpu<=0)throw new ArgumentOutOfRangeException("budget");
            Status=CargoNavigationStatus.Planning;
            CargoTrace.Emit(space,"search-start","from="+CargoTrace.Point(origin)+" goal="+CargoTrace.Point(goal)+" indoor="+indoor);
            if(Math.Abs(origin.X-goal.X)>80||Math.Abs(origin.Z-goal.Z)>80||Math.Abs(origin.Y-goal.Y)>48){Fail(CargoNavigationStatus.SearchBudgetExceeded,"range");return;}
            Stage();
        }
        bool Less(Entry a,Entry b)
        {
            if(a.F!=b.F)return a.F<b.F;
            if(!indoor&&a.Node.Point.Y!=b.Node.Point.Y)return a.Node.Point.Y>b.Node.Point.Y;
            if(a.Node.H!=b.Node.H)return a.Node.H<b.Node.H;return a.Order<b.Order;
        }
        void Push(Node n)
        {
            if(heap.Count>=32768){Fail(CargoNavigationStatus.SearchBudgetExceeded,"heap");return;}
            var e=new Entry{Node=n,G=n.G,F=n.G+n.H,Revision=n.Revision,Order=order++};heap.Add(e);int i=heap.Count-1;
            while(i>0){int p=(i-1)/2;if(!Less(e,heap[p]))break;heap[i]=heap[p];i=p;}heap[i]=e;
        }
        Node Pop()
        {
            while(heap.Count>0)
            {
                var e=heap[0];var last=heap[heap.Count-1];heap.RemoveAt(heap.Count-1);
                if(heap.Count>0){int i=0;while(i*2+1<heap.Count){int c=i*2+1;if(c+1<heap.Count&&Less(heap[c+1],heap[c]))c++;if(!Less(heap[c],last))break;heap[i]=heap[c];i=c;}heap[i]=last;}
                if(e.Revision==e.Node.Revision&&e.G==e.Node.G)return e.Node;
            }
            return null;
        }
        void Stage()
        {
            nodes.Clear();heap.Clear();current=null;neighbors=null;neighborIndex=0;stageNodes=0;
            grid=indoor?(stage<3?1:.5):(stage==0?2:stage<3?1:.5);
            int pad=stage==0||stage==3?8:stage==1?16:24,vertical=pad/2;
            // Always include exact endpoints while clamping the final dimensions.
            double cx=(origin.X+goal.X)/2,cz=(origin.Z+goal.Z)/2,cy=(origin.Y+goal.Y)/2;
            minX=Math.Max(cx-48,Math.Min(origin.X,goal.X)-pad);maxX=Math.Min(cx+48,Math.Max(origin.X,goal.X)+pad);
            minZ=Math.Max(cz-48,Math.Min(origin.Z,goal.Z)-pad);maxZ=Math.Min(cz+48,Math.Max(origin.Z,goal.Z)+pad);
            minY=Math.Max(1,Math.Max(cy-32,Math.Min(origin.Y,goal.Y)-vertical));maxY=Math.Min(253,Math.Min(cy+32,Math.Max(origin.Y,goal.Y)+vertical));
            var start=new Node{Key=new Key(int.MinValue,0,0),Point=origin,G=0,H=origin.Distance(goal)};
            nodes.Add(start.Key,start);Nodes++;Push(start);
            finish=new Node{Key=new Key(int.MaxValue,0,0),Point=goal,H=0};
            CargoTrace.Emit(space,"search-stage","stage="+stage+" grid="+grid+" min="+CargoTrace.Point(new CargoPoint(minX,minY,minZ))+" max="+CargoTrace.Point(new CargoPoint(maxX,maxY,maxZ)));
        }
        Key GridKey(CargoPoint p){return new Key((int)Math.Round((p.X-.5)/grid),(int)Math.Round((p.Y-.5)/grid),(int)Math.Round((p.Z-.5)/grid));}
        CargoPoint Point(Key k){return new CargoPoint(.5+k.X*grid,.5+k.Y*grid,.5+k.Z*grid);}
        bool InBounds(CargoPoint p){return p.X>=minX&&p.X<=maxX&&p.Y>=minY&&p.Y<=maxY&&p.Z>=minZ&&p.Z<=maxZ;}
        List<CargoPoint> Neighbors(Node n)
        {
            var result=new List<CargoPoint>(28);
            if(n.Point.Distance(goal)<=16)result.Add(goal);
            if(n.Key.X==int.MinValue)
            {
                int x=(int)Math.Floor((origin.X-.5)/grid),y=(int)Math.Floor((origin.Y-.5)/grid),z=(int)Math.Floor((origin.Z-.5)/grid);
                for(int a=0;a<=1;a++)for(int b=0;b<=1;b++)for(int c=0;c<=1;c++){var p=Point(new Key(x+a,y+b,z+c));if(InBounds(p))result.Add(p);}
            }
            else for(int dy=1;dy>=-1;dy--)for(int dx=-1;dx<=1;dx++)for(int dz=-1;dz<=1;dz++)
            {if(dx==0&&dy==0&&dz==0)continue;var p=Point(new Key(n.Key.X+dx,n.Key.Y+dy,n.Key.Z+dz));if(InBounds(p))result.Add(p);}
            return result;
        }
        // Returns null on unknown data: the edge cursor is retained, never closed.
        CargoSweep? Check(CargoPoint a,CargoPoint b)
        {
            Hold=space.Prepare(a,b);
            if(Hold!=CargoHold.None){Status=Hold==CargoHold.ChunkBudget?CargoNavigationStatus.WaitingBudget:CargoNavigationStatus.WaitingData;return null;}
            Probes++;var r=native==null?space.Sweep(a,b):native.PlanningSweep(a,b);
            if(r==CargoSweep.Unavailable){Hold=CargoHold.ChunkLoading;Status=CargoNavigationStatus.WaitingData;return null;}
            return r;
        }
        void Fail(CargoNavigationStatus status,string reason)
        {
            Failed=true;Status=status;Hold=CargoHold.PathBlocked;
            CargoTrace.Emit(space,"search-finished","result="+status+" reason="+reason+" nodes="+Nodes+" probes="+Probes+" cpuMs="+CpuMilliseconds.ToString("F2"));
            heap.Clear();nodes.Clear();neighbors=null;
        }
        void Reconstruct()
        {
            var points=new List<CargoPoint>();for(var n=finish;n!=null;n=n.Parent){points.Add(n.Point);if(points.Count>8192){Fail(CargoNavigationStatus.SearchBudgetExceeded,"parents");return;}}
            points.Reverse();raw=points.ToArray();smoothAt=0;smoothTry=raw.Length-1;
        }
        void Smooth()
        {
            while(smoothTry>smoothAt+1&&raw[smoothAt].Distance(raw[smoothTry])>16)smoothTry--;
            var result=Check(raw[smoothAt],raw[smoothTry]);if(!result.HasValue)return;
            if(result==CargoSweep.Blocked)
            {
                if(smoothTry==smoothAt+1){Fail(CargoNavigationStatus.RouteInvalidated,"geometry-changed");return;}
                smoothTry--;return;
            }
            smooth.Add(raw[smoothTry]);smoothAt=smoothTry;smoothTry=raw.Length-1;
            if(smoothAt==raw.Length-1)
            {
                Waypoints=smooth.ToArray();Complete=true;Status=CargoNavigationStatus.Ready;Hold=CargoHold.None;
                CargoTrace.Emit(space,"search-finished","result=ready nodes="+Nodes+" probes="+Probes+" cpuMs="+CpuMilliseconds.ToString("F2")+" points="+Waypoints.Length);
                heap.Clear();nodes.Clear();
            }
        }
        void Progress()
        {if(wallClock.Elapsed.TotalSeconds-lastProgress<5)return;lastProgress=wallClock.Elapsed.TotalSeconds;CargoTrace.Emit(space,"search-wait","status="+Status+" stage="+stage+" grid="+grid+" nodes="+Nodes+" open="+heap.Count+" probes="+Probes+" cpuMs="+CpuMilliseconds.ToString("F2")+" wallSeconds="+lastProgress.ToString("F1"));}
        public void Advance()
        {
            if(Complete||Failed)return;Progress();
            if(native!=null&&!native.BeginPlanning(this)){Status=CargoNavigationStatus.Queued;Hold=CargoHold.PathBlocked;return;}
            var clock=Stopwatch.StartNew();int before=Probes;
            try
            {
                Status=CargoNavigationStatus.Planning;Hold=CargoHold.PathBlocked;
                while(!Complete&&!Failed&&Probes-before<4&&clock.Elapsed.TotalMilliseconds<1)
                {
                    if(Nodes>=maxNodes||Probes>=maxProbes||CpuMilliseconds+clock.Elapsed.TotalMilliseconds>=maxCpu){Fail(CargoNavigationStatus.SearchBudgetExceeded,"work-budget");break;}
                    if(!goalChecked){var r=Check(goal,goal);if(!r.HasValue)break;if(r==CargoSweep.Blocked){Fail(native!=null&&native.DynamicObstacle?CargoNavigationStatus.DynamicBlocked:CargoNavigationStatus.GoalBlocked,"goal-occupied");break;}goalChecked=true;continue;}
                    if(!startChecked){var r=Check(origin,origin);if(!r.HasValue)break;if(r==CargoSweep.Blocked){Fail(native!=null&&native.DynamicObstacle?CargoNavigationStatus.DynamicBlocked:CargoNavigationStatus.GoalBlocked,"start-occupied");break;}startChecked=true;continue;}
                    if(raw!=null){Smooth();if(Status==CargoNavigationStatus.WaitingData||Status==CargoNavigationStatus.WaitingBudget)break;continue;}
                    if(current==null)
                    {
                        current=Pop();
                        if(current==null||stageNodes>=1536)
                        {
                            if(current!=null)incompleteStage=true;
                            if(++stage>=5){Fail(incompleteStage?CargoNavigationStatus.SearchBudgetExceeded:CargoNavigationStatus.NoPathInBounds,"stages-exhausted");break;}
                            Stage();continue;
                        }
                        if(current==finish){Reconstruct();continue;}
                        stageNodes++;neighbors=Neighbors(current);neighborIndex=0;
                    }
                    if(neighborIndex>=neighbors.Count){current=null;continue;}
                    var p=neighbors[neighborIndex];bool isGoal=p.Distance(goal)<1e-8;Key key=isGoal?finish.Key:GridKey(p);Node next;
                    if(!nodes.TryGetValue(key,out next))next=isGoal?finish:new Node{Key=key,Point=p,H=p.Distance(goal)};
                    double cost=current.G+current.Point.Distance(p);
                    if(cost+1e-8>=next.G){neighborIndex++;continue;}
                    var edge=Check(current.Point,p);if(!edge.HasValue)break;neighborIndex++;
                    if(edge==CargoSweep.Blocked)continue;
                    if(!nodes.ContainsKey(key)){nodes.Add(key,next);Nodes++;}
                    next.G=cost;next.Parent=current;next.Revision++;Push(next);
                }
            }
            finally{if(!Complete&&!Failed&&Hold==CargoHold.None)Hold=CargoHold.PathBlocked;double ms=clock.Elapsed.TotalMilliseconds;CpuMilliseconds+=ms;if(native!=null){native.EndPlanning(ms,Probes-before);if(Complete||Failed)native.CancelPlanning(this);}}
        }
    }
}
