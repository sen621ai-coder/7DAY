using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace YFAutomation.CargoDrones
{
    // Rolling windows and authoritative data-based sweeps. This adapter owns its
    // flight's handles, not the shared world lease service or any inventory.
    public sealed class CargoNativeAirspace : ICargoAirspace,IDisposable
    {
        readonly World world;
        readonly CargoNativeLeaseService leases;
        readonly Guid flight;
        CargoNativeLease current,next,probe;
        CargoPosition? missing;
        bool disposed;
        readonly List<Bounds> boxes=new List<Bounds>();
        CargoPoint? dock;
        bool Hit(CargoBox obstacle,CargoPoint from,CargoPoint to){return CargoDock.Hit(obstacle,from,to,dock);}
        public CargoNativeAirspace(World world,CargoNativeLeaseService leases,Guid flight)
        {if(world==null||leases==null||flight==Guid.Empty)throw new ArgumentException("Invalid airspace");this.world=world;this.leases=leases;this.flight=flight;}
        static bool SameChunk(CargoPosition a,CargoPosition b){return (a.X>>4)==(b.X>>4)&&(a.Z>>4)==(b.Z>>4);}
        CargoNativeLease Acquire(CargoPoint point,out CargoHold hold){return leases.Request(Guid.NewGuid(),flight,point.Cell,out hold);}
        bool Ready(CargoPosition point)
        {return current!=null&&leases.IsDataReadyAt(current.Id,point)||next!=null&&leases.IsDataReadyAt(next.Id,point)||probe!=null&&leases.IsDataReadyAt(probe.Id,point);}
        public CargoHold Prepare(CargoPoint from,CargoPoint end)
        {
            if(disposed)throw new ObjectDisposedException("CargoNativeAirspace");leases.Poll();CargoHold hold;
            if(current==null){current=Acquire(from,out hold);if(current==null)return hold;}
            if(current.State==CargoNativeLeaseState.Failed||current.State==CargoNativeLeaseState.Released)return CargoHold.RecoveryRequired;
            if(next!=null&&!SameChunk(next.Center,end.Cell)){leases.Release(next.Id,flight);next=null;}
            if(!SameChunk(current.Center,end.Cell)&&next==null){next=Acquire(end,out hold);if(next==null)return hold;}
            if(next!=null&&(next.State==CargoNativeLeaseState.Failed||next.State==CargoNativeLeaseState.Released))return CargoHold.RecoveryRequired;
            if(missing.HasValue&&!Ready(missing.Value))
            {
                var point=missing.Value;
                if(probe!=null&&!SameChunk(probe.Center,point)){leases.Release(probe.Id,flight);probe=null;}
                if(probe==null){probe=Acquire(new CargoPoint(point.X+.5,point.Y,point.Z+.5),out hold);if(probe==null)return hold;}
                if(probe.State==CargoNativeLeaseState.Failed||probe.State==CargoNativeLeaseState.Released)return CargoHold.RecoveryRequired;
            }
            return Ready(from.Cell)&&Ready(end.Cell)?CargoHold.None:CargoHold.ChunkLoading;
        }
        bool RequireData(int x,int z,int y)
        {var point=new CargoPosition(x,y,z);if(Ready(point))return true;missing=point;return false;}
        static CargoBox Box(Bounds bounds)
        {return new CargoBox(new CargoPoint(bounds.min.x,bounds.min.y,bounds.min.z),new CargoPoint(bounds.max.x,bounds.max.y,bounds.max.z));}
        public CargoSweep Sweep(CargoPoint from,CargoPoint to)
        {
            if(disposed||GameManager.Instance==null||GameManager.Instance.World!=world)return CargoSweep.Unavailable;
            var state=CargoNativeWorld.Current?.Service.Status().SingleOrDefault(s=>s.Flight==flight);
            dock=state==null?(CargoPoint?)null:CargoNativeWorld.Current.Home(state.Configuration);
            if(from.Distance(to)>32.001)throw new ArgumentException("Flight sweeps must be segmented");
            if(from.Y<1||to.Y<1||from.Y>253||to.Y>253)return CargoSweep.Blocked;
            if(!world.IsPositionInBounds(new Vector3((float)from.X,(float)from.Y,(float)from.Z))||!world.IsPositionInBounds(new Vector3((float)to.X,(float)to.Y,(float)to.Z)))return CargoSweep.Blocked;
            int minX=(int)Math.Floor(Math.Min(from.X,to.X)-1.8),maxX=(int)Math.Floor(Math.Max(from.X,to.X)+1.8);
            int minZ=(int)Math.Floor(Math.Min(from.Z,to.Z)-1.8),maxZ=(int)Math.Floor(Math.Max(from.Z,to.Z)+1.8);
            int minY=Math.Max(0,(int)Math.Floor(Math.Min(from.Y,to.Y)-1.6)),maxY=Math.Min(255,(int)Math.Floor(Math.Max(from.Y,to.Y)+1.6));
            // Registered source models extend beyond their root voxel. Their XML
            // oversized bounds fit this 8-block search margin; use an enclosing
            // horizontal radius so every supported 90-degree rotation is covered.
            for(int cx=(minX-8)>>4;cx<=((maxX+8)>>4);cx++)for(int cz=(minZ-8)>>4;cz<=((maxZ+8)>>4);cz++)
            {
                if(!RequireData(cx*16+8,cz*16+8,minY))return CargoSweep.Unavailable;
                var chunk=world.GetChunkFromWorldPos(cx*16,cz*16) as Chunk;
                foreach(var tile in chunk.GetTileEntities().dict.Values)
                {
                    var block=tile.block;if(!CargoRules.IsSource(block.GetBlockName())||!block.isOversized)continue;
                    var b=block.oversizedBounds;var p=tile.ToWorldPos();double rx=Math.Abs(b.center.x)+b.extents.x,rz=Math.Abs(b.center.z)+b.extents.z;
                    double radius=Math.Sqrt(rx*rx+rz*rz)+.5;
                    if(radius>8)return CargoSweep.Blocked; // unsupported enlarged model, never silently clip it
                    var forbidden=new CargoBox(new CargoPoint(p.x+.5-radius,p.y+b.min.y-.2,p.z+.5-radius),new CargoPoint(p.x+.5+radius,p.y+b.max.y+.2,p.z+.5+radius));
                    if(Hit(forbidden,from,to))return CargoSweep.Blocked;
                }
            }
            for(int x=minX;x<=maxX;x++)for(int z=minZ;z<=maxZ;z++)
            {
                if(!RequireData(x,z,minY))return CargoSweep.Unavailable;
                for(int y=minY;y<=maxY;y++)
                {
                    var value=world.GetBlock(new Vector3i(x,y,z));if(value.isair)continue;
                    if(value.ischild)
                    {
                        // Child voxels form part of native multiblock occupancy.
                        if(Hit(new CargoBox(new CargoPoint(x,y,z),new CargoPoint(x+1,y+1,z+1)),from,to))return CargoSweep.Blocked;
                        continue;
                    }
                    var block=value.Block;if(!block.IsCollideMovement)continue;
                    boxes.Clear();block.GetCollisionAABB(value,x,y,z,0,boxes);
                    foreach(var box in boxes)if(Hit(Box(box),from,to))return CargoSweep.Blocked;
                }
            }
            var envelope=new Bounds();envelope.SetMinMax(new Vector3(minX,minY,minZ),new Vector3(maxX+1,maxY+1,maxZ+1));
            // Players routinely stand beside the pad while using its panel. The
            // cargo drone is cosmetic and deals no collision damage, so players
            // must not be able to trap or grief an autonomous shipment.
            foreach(var entity in world.GetEntitiesInBounds((Entity)null,envelope))
                if(!(entity is EntityPlayer)&&Hit(Box(entity.getBoundingBox()),from,to))return CargoSweep.Blocked;
            missing=null;return CargoSweep.Clear;
        }
        public void ReachedSegment(CargoPoint at)
        {
            if(next!=null){leases.Release(current.Id,flight);current=next;next=null;}
            if(probe!=null){leases.Release(probe.Id,flight);probe=null;}missing=null;
        }
        public void Dispose()
        {
            if(disposed)return;
            if(current!=null)leases.Release(current.Id,flight);if(next!=null)leases.Release(next.Id,flight);if(probe!=null)leases.Release(probe.Id,flight);disposed=true;
        }
    }
}
