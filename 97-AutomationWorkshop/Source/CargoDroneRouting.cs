using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace YFAutomation.CargoDrones
{
    // Cover the entire swept corridor, including oversized-model roots, before
    // testing any voxel. Native observers each retain a 3x3 chunk square.
    public static class CargoAirspaceCoverage
    {
        public static CargoPosition[] Centers(CargoPoint from,CargoPoint to)
        {
            if(from.Distance(to)>32.001)throw new ArgumentException("Flight sweeps must be segmented");
            int x0=((int)Math.Floor(Math.Min(from.X,to.X)-1.8)-8)>>4;
            int x1=((int)Math.Floor(Math.Max(from.X,to.X)+1.8)+8)>>4;
            int z0=((int)Math.Floor(Math.Min(from.Z,to.Z)-1.8)-8)>>4;
            int z1=((int)Math.Floor(Math.Max(from.Z,to.Z)+1.8)+8)>>4;
            var points=new List<CargoPosition>();
            // Padding may remain undecorated: every queried chunk needs its own center.
            for(int x=x0;x<=x1;x++)for(int z=z0;z<=z1;z++)
                points.Add(new CargoPosition(x*16+8,(int)from.Y,z*16+8));
            return points.ToArray();
        }
    }
    // Bounded local doglegs, not a global pathfinder. Each candidate is verified
    // completely before motion may use it; unknown data suspends the same probe.
    public sealed class CargoLocalRoute
    {
        readonly ICargoAirspace space;
        readonly CargoPoint origin,goal;
        readonly double ceiling;
        readonly List<CargoPoint> candidate=new List<CargoPoint>();
        int candidateIndex,edge;
        public int Probes{get;private set;}
        public bool Complete{get;private set;}
        public bool Failed{get;private set;}
        public CargoHold Hold{get;private set;}
        public CargoPoint[] Waypoints{get;private set;}
        public CargoLocalRoute(ICargoAirspace space,CargoPoint origin,CargoPoint goal,double ceiling)
        {
            if(space==null||origin.Distance(goal)>16.001||double.IsNaN(ceiling)||double.IsInfinity(ceiling))throw new ArgumentException("Invalid local route");
            this.space=space;this.origin=origin;this.goal=goal;this.ceiling=Math.Min(253,ceiling);
        }
        public void Advance()
        {
            if(Complete||Failed)return;
            var clock=Stopwatch.StartNew();int work=0;
            while(work<4&&clock.Elapsed.TotalMilliseconds<2)
            {
                if(candidate.Count==0&&!BuildCandidate()){Failed=true;Hold=CargoHold.PathBlocked;return;}
                if(Probes>=256){Failed=true;Hold=CargoHold.PathBlocked;return;}
                var from=edge==0?origin:candidate[edge-1];var to=candidate[edge];
                Hold=space.Prepare(from,to);if(Hold!=CargoHold.None)return;
                var result=space.Sweep(from,to);work++;
                if(result==CargoSweep.Unavailable){Hold=CargoHold.ChunkLoading;return;}
                Probes++;
                if(result==CargoSweep.Blocked){candidate.Clear();edge=0;continue;}
                edge++;
                if(edge==candidate.Count){Waypoints=candidate.ToArray();Complete=true;Hold=CargoHold.None;return;}
            }
            Hold=CargoHold.PathBlocked;
        }
        bool BuildCandidate()
        {
            // Raise first in two-block steps through the full 24-block ceiling.
            // Only after every vertical candidate fails do we alternate left and
            // right offsets, also in two-block steps. The ceiling is fixed by the
            // caller for the whole leg and never grows without bound on retries.
            while(candidateIndex<36)
            {
                int index=candidateIndex++;double ox=0,oy=0,oz=0;
                if(index<12)oy=(index+1)*2;
                else
                {
                    double dx=goal.X-origin.X,dz=goal.Z-origin.Z,length=Math.Sqrt(dx*dx+dz*dz);
                    if(length<1e-7){dx=1;dz=0;length=1;}
                    int lateral=index-12;
                    double offset=(lateral/2+1)*2*((lateral%2==0)?1:-1);
                    ox=-dz/length*offset;oz=dx/length*offset;
                }
                if(Math.Max(origin.Y,goal.Y)+oy>ceiling)continue;
                var a=new CargoPoint(origin.X+ox,origin.Y+oy,origin.Z+oz);
                var b=new CargoPoint(goal.X+ox,goal.Y+oy,goal.Z+oz);
                Append(origin,a);Append(a,b);Append(b,goal);return true;
            }
            return false;
        }
        void Append(CargoPoint from,CargoPoint to)
        {
            while(from.Distance(to)>1e-7){from=from.Toward(to,16);candidate.Add(from);}
        }
    }
}
