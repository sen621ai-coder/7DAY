using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace YFAutomation.CargoDrones
{
    // Rolling windows and authoritative data-based sweeps. This adapter owns its
    // flight's handles, not the shared world lease service or any inventory.
    public sealed class CargoNativeAirspace : ICargoAirspace,ICargoFlightTrace,IDisposable
    {
        readonly World world;
        readonly CargoNativeLeaseService leases;
        readonly Guid flight;
        readonly CargoDiagnostics diagnostics;
        readonly List<CargoNativeLease> windows=new List<CargoNativeLease>();
        CargoPosition[] centers=new CargoPosition[0];
        float waitingSince=-1,nextDiagnostic,nextBudgetRetry;
        bool disposed;
        readonly List<Bounds> boxes=new List<Bounds>();
        CargoPoint? dock;
        bool Hit(CargoBox obstacle,CargoPoint from,CargoPoint to,string reason="block")
        {bool hit=CargoDock.Hit(obstacle,from,to,dock);if(hit)Trace("collision","reason="+reason+" from="+CargoTrace.Point(from)+" to="+CargoTrace.Point(to)+" min="+CargoTrace.Point(obstacle.Min)+" max="+CargoTrace.Point(obstacle.Max));return hit;}
        public void Trace(string kind,string detail){diagnostics.Write(kind,flight,detail,kind=="collision"||kind=="plan-reject"?1:kind=="plan-wait"||kind=="sweep-wait"?10:0);}
        public CargoNativeAirspace(World world,CargoNativeLeaseService leases,Guid flight)
        {if(world==null||leases==null||flight==Guid.Empty)throw new ArgumentException("Invalid airspace");this.world=world;this.leases=leases;this.flight=flight;diagnostics=CargoNativeWorld.Current?.Diagnostics??new CargoDiagnostics(Guid.Empty);}
        static bool SameChunk(CargoPosition a,CargoPosition b){return (a.X>>4)==(b.X>>4)&&(a.Z>>4)==(b.Z>>4);}
        bool Ready(CargoPosition point)
        {return windows.Any(window=>leases.IsDataReadyAt(window.Id,point));}
        public CargoHold Prepare(CargoPoint from,CargoPoint end)
        {
            if(disposed)throw new ObjectDisposedException("CargoNativeAirspace");leases.Poll();CargoHold hold;
            if(Time.realtimeSinceStartup<nextBudgetRetry)return CargoHold.ChunkBudget;
            var required=CargoAirspaceCoverage.Centers(from,end);
            if(required.Length!=centers.Length||required.Where((p,i)=>!SameChunk(p,centers[i])).Any())
            {
                // Preserve overlap: releasing the whole corridor on every chunk
                // boundary forces a moving drone to wait for the same data again.
                foreach(var window in windows.Where(w=>!required.Any(p=>SameChunk(p,w.Center))).ToArray())
                {leases.Release(window.Id,flight);windows.Remove(window);}
                centers=required;
            }
            foreach(var center in centers)
            {
                if(windows.Any(w=>SameChunk(w.Center,center)))continue;
                var window=leases.Request(Guid.NewGuid(),flight,center,out hold);
                if(window==null)
                {
                    // Never let several partially admitted corridors hold all
                    // capacity while each waits for the others to release it.
                    if(hold==CargoHold.ChunkBudget)
                    {foreach(var held in windows)leases.Release(held.Id,flight);windows.Clear();nextBudgetRetry=Time.realtimeSinceStartup+1;}
                    return Waiting(hold,center);
                }
                windows.Add(window);
            }
            if(windows.Any(w=>w.State==CargoNativeLeaseState.Failed||w.State==CargoNativeLeaseState.Released))return CargoHold.RecoveryRequired;
            foreach(var center in centers)if(!Ready(center))return Waiting(CargoHold.ChunkLoading,center);
            if(waitingSince>=0)Trace("loading-resumed","seconds="+(Time.realtimeSinceStartup-waitingSince).ToString("F1")+" from="+CargoTrace.Point(from)+" to="+CargoTrace.Point(end));waitingSince=-1;return CargoHold.None;
        }
        CargoHold Waiting(CargoHold hold,CargoPosition point)
        {
            float now=Time.realtimeSinceStartup;if(waitingSince<0)waitingSince=now;
            diagnostics.Write("loading-wait",flight,CargoDiagnostics.ChunkState(world,point)+" hold="+hold+" seconds="+(now-waitingSince).ToString("F1")+" windows="+windows.Count+" budget="+leases.HeldChunks);
            if(now-waitingSince>=5&&now>=nextDiagnostic)
            {
                nextDiagnostic=now+10;
                var chunk=world.GetChunkFromWorldPos(point.X,point.Z) as Chunk;
                Log.Warning("[YFCargo] Loading wait flight="+flight+" chunk="+(point.X>>4)+","+(point.Z>>4)+" hold="+hold+" seconds="+(now-waitingSince).ToString("F1")+" resident="+(chunk!=null)+" locked="+(chunk!=null&&chunk.IsLocked)+" decoration="+(chunk!=null&&chunk.NeedsDecoration)+" windows="+windows.Count+" budget="+leases.HeldChunks);
            }
            return hold;
        }
        bool RequireData(int x,int z,int y)
        {var p=new CargoPosition(x,y,z);if(Ready(p))return true;Trace("sweep-wait",CargoDiagnostics.ChunkState(world,p)+" windows="+windows.Count+" budget="+leases.HeldChunks);return false;}
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
                    if(Hit(forbidden,from,to,"oversized:"+block.GetBlockName()))return CargoSweep.Blocked;
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
                        if(Hit(new CargoBox(new CargoPoint(x,y,z),new CargoPoint(x+1,y+1,z+1)),from,to,"multiblock-child"))return CargoSweep.Blocked;
                        continue;
                    }
                    var block=value.Block;if(!block.IsCollideMovement)continue;
                    boxes.Clear();block.GetCollisionAABB(value,x,y,z,0,boxes);
                    foreach(var box in boxes)if(Hit(Box(box),from,to,block.GetBlockName()))return CargoSweep.Blocked;
                }
            }
            var envelope=new Bounds();envelope.SetMinMax(new Vector3(minX,minY,minZ),new Vector3(maxX+1,maxY+1,maxZ+1));
            // Players routinely stand beside the pad while using its panel. The
            // cargo drone is cosmetic and deals no collision damage, so players
            // must not be able to trap or grief an autonomous shipment.
            foreach(var entity in world.GetEntitiesInBounds((Entity)null,envelope))
                if(!(entity is EntityPlayer)&&Hit(Box(entity.getBoundingBox()),from,to,"entity:"+entity.entityId))return CargoSweep.Blocked;
            return CargoSweep.Clear;
        }
        public void ReachedSegment(CargoPoint at)
        {
            Trace("segment-reached","pos="+CargoTrace.Point(at));
            // Retain the complete corridor until Prepare selects the next one.
        }
        public void Dispose()
        {
            if(disposed)return;
            foreach(var window in windows)leases.Release(window.Id,flight);windows.Clear();disposed=true;
        }
    }
}
