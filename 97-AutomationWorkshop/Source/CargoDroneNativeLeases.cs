using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace YFAutomation.CargoDrones
{
    public enum CargoNativeLeaseState{Loading,Ready,Released,Failed}
    public sealed class CargoNativeLease
    {
        public readonly Guid Id,Flight;
        public readonly CargoPosition Center;
        internal readonly HashSet<long> Footprint;
        internal readonly ChunkManager.ChunkObserver Observer;
        public CargoNativeLeaseState State{get;internal set;}
        public string Failure{get;internal set;}
        public int ResidentChunks{get;internal set;}
        public int InitializedChunks{get;internal set;}
        public int DataReadyChunks{get;internal set;}
        internal CargoNativeLease(Guid id,Guid flight,CargoPosition center,HashSet<long> footprint,ChunkManager.ChunkObserver observer)
        {Id=id;Flight=flight;Center=center;Footprint=footprint;Observer=observer;State=CargoNativeLeaseState.Loading;}
    }
    // Main-thread native handle owner. Scheduling fairness and power-network
    // dependencies belong to the world service; this class enforces admission and
    // actual observed coverage, without claiming physics/collision readiness.
    public sealed class CargoNativeLeaseService : IDisposable
    {
        readonly World world;
        readonly ChunkManager manager;
        readonly int thread;
        readonly CargoChunkBudget budget;
        readonly Dictionary<Guid,CargoNativeLease> leases=new Dictionary<Guid,CargoNativeLease>();
        bool disposed;
        float nextRequest;
        public int HeldChunks{get{return budget.HeldChunks;}}
        public int Count{get{return leases.Count;}}
        public CargoNativeLeaseService(World world,int perFlight=24,int total=128)
        {
            if(world==null||world.IsRemote()||world.m_ChunkManager==null)throw new ArgumentException("Live server world required");
            if(typeof(Chunk).Module.ModuleVersionId!=new Guid("229796d0-95ca-4662-b426-1a6f1f1596ed"))throw new NotSupportedException("Observer footprint unverified on this game build");
            this.world=world;manager=world.m_ChunkManager;thread=Thread.CurrentThread.ManagedThreadId;budget=new CargoChunkBudget(perFlight,total);
        }
        void MainThread(){if(Thread.CurrentThread.ManagedThreadId!=thread)throw new InvalidOperationException("Native leases require their world thread");}
        static HashSet<long> Footprint(CargoPosition center)
        {
            // Native viewDim=0 includes its one-chunk padding: a measured 3x3.
            var result=new HashSet<long>();int x=center.X>>4,z=center.Z>>4;
            for(int dx=-1;dx<=1;dx++)for(int dz=-1;dz<=1;dz++)
            {checked{int wx=(x+dx)*16,wz=(z+dz)*16;result.Add(WorldChunkCache.MakeChunkKey(wx>>4,wz>>4));}}
            return result;
        }
        public CargoNativeLease Request(Guid id,Guid flight,CargoPosition center,out CargoHold hold)
        {
            MainThread();if(disposed)throw new ObjectDisposedException("CargoNativeLeaseService");
            if(GameManager.Instance==null||GameManager.Instance.World!=world)throw new InvalidOperationException("World changed; dispose old leases");
            hold=CargoHold.None;CargoNativeLease existing;
            if(leases.TryGetValue(id,out existing))
            {
                if(existing.Flight!=flight||!existing.Center.Equals(center))throw new InvalidOperationException("Lease identity reused with a different request");
                if(existing.State==CargoNativeLeaseState.Loading)hold=CargoHold.ChunkLoading;
                return existing;
            }
            var footprint=Footprint(center);
            if(!budget.TryAcquire(id,flight,footprint)){hold=CargoHold.ChunkBudget;return null;}
            if(Time.realtimeSinceStartup<nextRequest){budget.Release(id,flight);hold=CargoHold.ChunkLoading;return null;}
            ChunkManager.ChunkObserver observer=null;
            try
            {
                observer=manager.AddChunkObserver(new Vector3(center.X,center.Y,center.Z),false,0,-1);
                var lease=new CargoNativeLease(id,flight,center,footprint,observer);leases.Add(id,lease);
                nextRequest=Time.realtimeSinceStartup+.5f;hold=CargoHold.ChunkLoading;return lease;
            }
            catch{if(observer!=null)manager.RemoveChunkObserver(observer);budget.Release(id,flight);throw;}
        }
        public void Poll()
        {
            MainThread();if(disposed)return;
            if(GameManager.Instance==null||GameManager.Instance.World!=world){Dispose();return;}
            foreach(var lease in leases.Values.ToArray())
            {
                var observer=lease.Observer;
                if(observer.curChunkPos.x!=(lease.Center.X>>4)||observer.curChunkPos.z!=(lease.Center.Z>>4)||observer.chunksAround.list.Count==0)
                {lease.State=CargoNativeLeaseState.Loading;continue;}
                if(!lease.Footprint.SetEquals(observer.chunksAround.list))
                {
                    Release(lease.Id,lease.Flight);lease.State=CargoNativeLeaseState.Failed;lease.Failure="Native observer exceeded or changed admitted footprint";continue;
                }
                int resident=0,initialized=0,dataReady=0;int x=lease.Center.X>>4,z=lease.Center.Z>>4;
                for(int dx=-1;dx<=1;dx++)for(int dz=-1;dz<=1;dz++)
                {var chunk=world.GetChunkFromWorldPos((x+dx)*16,(z+dz)*16) as Chunk;if(chunk!=null){resident++;if(!chunk.IsLocked&&chunk.IsInitialized)initialized++;if(DataReady(chunk))dataReady++;}}
                lease.ResidentChunks=resident;lease.InitializedChunks=initialized;lease.DataReadyChunks=dataReady;
                // Native padding is charged to the budget, but is not guaranteed
                // to have its own lighting neighbours. Ready means the requested
                // center is available, not that every padded chunk is readable.
                lease.State=IsDataReadyAt(lease.Id,lease.Center)?CargoNativeLeaseState.Ready:CargoNativeLeaseState.Loading;
            }
        }
        static bool DataReady(Chunk chunk){return chunk!=null&&!chunk.IsLocked&&!chunk.NeedsDecoration;}
        public bool IsDataReadyAt(Guid id,CargoPosition position)
        {
            MainThread();CargoNativeLease lease;if(disposed||GameManager.Instance==null||GameManager.Instance.World!=world||!leases.TryGetValue(id,out lease))return false;
            if(!lease.Footprint.Contains(WorldChunkCache.MakeChunkKey(position.X>>4,position.Z>>4)))return false;
            var chunk=world.GetChunkFromWorldPos(position.X,position.Z) as Chunk;
            // Server-only observers can leave NeedsLightCalculation true forever.
            // Block/inventory data readiness is distinct from lighting and meshes.
            return DataReady(chunk);
        }
        public bool Release(Guid id,Guid flight)
        {
            MainThread();CargoNativeLease lease;if(!leases.TryGetValue(id,out lease))return true;if(lease.Flight!=flight)return false;
            manager.RemoveChunkObserver(lease.Observer);budget.Release(id,flight);leases.Remove(id);lease.State=CargoNativeLeaseState.Released;return true;
        }
        public void Dispose()
        {
            MainThread();if(disposed)return;
            foreach(var lease in leases.Values.ToArray())Release(lease.Id,lease.Flight);disposed=true;
        }
    }
}
