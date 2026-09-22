using System;
using System.Collections.Generic;
using UnityEngine;

namespace YFAutomation.CargoDrones
{
    // The landed mechanical pose has its own measured envelope. The pad remains
    // solid: only the actual drone body changes, not the set of tested obstacles.
    public static class CargoDock
    {
        public const double RestCenter=.15,RestHalfHeight=.39,PoseRadius=1;
        public static bool Resting(CargoPoint at,CargoPoint home)=>at.Distance(home)<=PoseRadius;
        public static bool Hit(CargoBox obstacle,CargoPoint from,CargoPoint to,CargoPoint? home)
        {
            if(!home.HasValue)return obstacle.SweptHit(from,to);
            bool a=Resting(from,home.Value),b=Resting(to,home.Value);
            if(a&&b)return RestHit(obstacle,from,to);
            if(!a&&!b)return obstacle.SweptHit(from,to);
            double dx=to.X-from.X,dy=to.Y-from.Y,dz=to.Z-from.Z,x=from.X-home.Value.X,y=from.Y-home.Value.Y,z=from.Z-home.Value.Z;
            double aa=dx*dx+dy*dy+dz*dz,bb=2*(x*dx+y*dy+z*dz),cc=x*x+y*y+z*z-PoseRadius*PoseRadius;
            double t=(-bb+(a?1:-1)*Math.Sqrt(Math.Max(0,bb*bb-4*aa*cc)))/(2*aa);
            var edge=new CargoPoint(from.X+dx*t,from.Y+dy*t,from.Z+dz*t);
            return a?RestHit(obstacle,from,edge)||obstacle.SweptHit(edge,to):obstacle.SweptHit(from,edge)||RestHit(obstacle,edge,to);
        }
        static bool RestHit(CargoBox obstacle,CargoPoint from,CargoPoint to)=>obstacle.SweptHit(new CargoPoint(from.X,from.Y+RestCenter,from.Z),new CargoPoint(to.X,to.Y+RestCenter,to.Z),.8,RestHalfHeight);
        public static Bounds Deck(int x,int y,int z)=>new Bounds(new Vector3(x+.5f,y+.5f,z+.5f),new Vector3(1.94f,1,1.94f));
        public static void Collision(Block __instance,int __1,int __2,int __3,List<Bounds> __5)
        {if(__instance.GetBlockName()==CargoRuntime.HubBlock)__5.Add(Deck(__1,__2,__3));}
    }
}
