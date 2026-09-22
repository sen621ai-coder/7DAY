using System;
using System.Collections.Generic;

namespace YFAutomation.CargoDrones
{
    // In-memory evidence of positions actually traversed. Collinear samples may
    // merge, but turns and the 16-block motion boundary are preserved. A recorded
    // corridor is historical evidence, never permission to skip a fresh sweep.
    public sealed class CargoReturnTrail
    {
        readonly List<CargoPoint> points=new List<CargoPoint>();
        public CargoPoint Home{get{return points[0];}}
        public int Count{get{return points.Count;}}
        public CargoReturnTrail(CargoPoint home){points.Add(home);}
        internal CargoPoint[] Capture(){return points.ToArray();}
        internal static CargoReturnTrail Restore(CargoPoint[] saved)
        {
            if(saved==null||saved.Length<1||saved.Length>4096)throw new ArgumentException("Invalid saved return trail");
            var trail=new CargoReturnTrail(saved[0]);
            for(int i=1;i<saved.Length;i++)
            {
                double distance=saved[i-1].Distance(saved[i]);
                if(distance<1e-9||distance>16.000001)throw new ArgumentException("Invalid saved return edge");
                trail.points.Add(saved[i]);
            }
            return trail;
        }
        bool CanMerge(CargoPoint next)
        {
            if(points.Count<2)return false;
            var a=points[points.Count-2];var b=points[points.Count-1];
            double ab=a.Distance(b),bn=b.Distance(next),an=a.Distance(next);
            if(an>16||ab<1e-9||bn<1e-9)return false;
            // Cross product bounds perpendicular error, dot product rejects
            // reversals. Do not shortcut a corner merely because it is shallow.
            double x=b.X-a.X,y=b.Y-a.Y,z=b.Z-a.Z;
            double u=next.X-b.X,v=next.Y-b.Y,w=next.Z-b.Z;
            double cx=y*w-z*v,cy=z*u-x*w,cz=x*v-y*u;
            return x*u+y*v+z*w>0&&cx*cx+cy*cy+cz*cz<=1e-20*ab*ab*bn*bn;
        }
        public bool CanRecord(CargoPoint next)
        {return points[points.Count-1].Distance(next)<=16.000001&&(points.Count<4096||CanMerge(next));}
        public void Record(CargoPoint next)
        {
            if(points[points.Count-1].Distance(next)<1e-9)return;
            if(!CanRecord(next))throw new InvalidOperationException("Return trail capacity or segment limit exceeded");
            if(next.Distance(Home)<1e-9){points.RemoveRange(1,points.Count-1);return;}
            if(CanMerge(next))points[points.Count-1]=next;else points.Add(next);
        }
        public CargoPoint[] ReverseWaypoints()
        {
            var result=new CargoPoint[Math.Max(0,points.Count-1)];
            for(int i=0;i<result.Length;i++)result[i]=points[points.Count-2-i];return result;
        }
        public long EstimateReturn(CargoPoint next,double speed,double approachSpeed)
        {
            if(double.IsNaN(speed)||double.IsInfinity(speed)||speed<=0||double.IsNaN(approachSpeed)||approachSpeed<=0||approachSpeed>speed)throw new ArgumentException("Invalid return speeds");
            if(next.Distance(Home)<1e-9)return 0;
            long total=0;
            for(int i=1;i<points.Count;i++)total=checked(total+Cost(points[i-1],points[i],speed,approachSpeed));
            return checked(total+Cost(points[points.Count-1],next,speed,approachSpeed));
        }
        public long EstimateRemaining(CargoPoint from,IEnumerable<CargoPoint> waypoints,double speed,double approachSpeed)
        {
            long total=0;foreach(var to in waypoints){total=checked(total+Cost(from,to,speed,approachSpeed));from=to;}return total;
        }
        long Cost(CargoPoint from,CargoPoint to,double speed,double approachSpeed)
        {
            double length=from.Distance(to);if(length<1e-9)return 0;
            double x=to.X-from.X,y=to.Y-from.Y,z=to.Z-from.Z;
            double t=((Home.X-from.X)*x+(Home.Y-from.Y)*y+(Home.Z-from.Z)*z)/(length*length);
            t=Math.Max(0,Math.Min(1,t));var nearest=new CargoPoint(from.X+x*t,from.Y+y*t,from.Z+z*t);
            // Entire edges touching the final approach sphere are charged at the
            // slow speed. One extra millisecond covers rounded endpoint steps.
            double velocity=nearest.Distance(Home)<=4.000001?approachSpeed:speed;
            return checked((long)Math.Ceiling(length/velocity*1000)+1);
        }
    }
}
