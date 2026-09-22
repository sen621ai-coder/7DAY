using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace YFAutomation.CargoDrones
{
    // Server-world integration. Runtime owns normal save lifecycle; the isolated
    // harness can still supply its own files to test individual failure points.
    public sealed class CargoNativeWorld : ICargoWorldAdapter,ICargoRecoveryAdapter,IDisposable
    {
        public static CargoNativeWorld Current{get;private set;}
        static bool updatesInstalled;
        float lastUpdate;
        public static void InstallWorldUpdates()
        {if(updatesInstalled)return;ModEvents.GameUpdate.RegisterHandler(UpdateWorld);updatesInstalled=true;}
        static void UpdateWorld(ref ModEvents.SGameUpdateData data)
        {
            var current=Current;if(current==null)return;
            if(GameManager.Instance?.World!=current.world){current.Dispose();return;}
            if(CargoRuntime.Recovering)return;
            if(current.Service.Faulted)return;
            float now=Time.realtimeSinceStartup;long elapsed=(long)((now-current.lastUpdate)*1000);if(elapsed<1)return;current.lastUpdate=now;
            try{current.Tick(elapsed,GameManager.Instance.IsPaused());}
            catch(Exception ex){Log.Error("[YFCargo] World scheduling stopped: "+ex);}
        }
        sealed class Space : ICargoAirspace
        {
            readonly CargoNativeWorld owner;readonly Guid flight;
            CargoNativeAirspace native;
            public Space(CargoNativeWorld owner,Guid flight){this.owner=owner;this.flight=flight;}
            CargoNativeAirspace Get(){owner.Context();if(native==null)native=new CargoNativeAirspace(owner.world,owner.leases,flight);return native;}
            public CargoHold Prepare(CargoPoint from,CargoPoint to){return Get().Prepare(from,to);}
            public CargoSweep Sweep(CargoPoint from,CargoPoint to){return Get().Sweep(from,to);}
            public void ReachedSegment(CargoPoint at){Get().ReachedSegment(at);}
            public void Release(){native?.Dispose();native=null;}
        }
        readonly World world;
        readonly Guid worldId;
        readonly CargoNativeLeaseService leases;
        readonly Dictionary<Guid,TileEntityComposite> hubs=new Dictionary<Guid,TileEntityComposite>();
        readonly HashSet<Guid> formalHubs=new HashSet<Guid>();
        readonly Dictionary<Guid,Space> spaces=new Dictionary<Guid,Space>();
        readonly Dictionary<Guid,CargoDroneVisual> drones=new Dictionary<Guid,CargoDroneVisual>();
        readonly Dictionary<Guid,GameObject> decks=new Dictionary<Guid,GameObject>();
        readonly Dictionary<Guid,CargoNativeLease> preflight=new Dictionary<Guid,CargoNativeLease>();
        readonly Dictionary<Guid,float> preflightUsed=new Dictionary<Guid,float>();
        readonly Dictionary<Guid,CargoNativeLease> recoveryLeases=new Dictionary<Guid,CargoNativeLease>();
        bool disposed;
        bool validationOwnerOnline=true;
        long sequence;
        public CargoWorldService Service{get;private set;}
        public int HeldChunks{get{return leases.HeldChunks;}}
        public CargoNativeWorld(World world,Guid worldId,CargoFileJournal journal,CargoCheckpointStore checkpoints)
        {
            if(world==null||world.IsRemote()||GameManager.Instance?.World!=world||worldId==Guid.Empty||!CargoNativeAccessSessions.Installed)throw new InvalidOperationException("Server world with inventory session tracking required");
            this.world=world;this.worldId=worldId;leases=new CargoNativeLeaseService(world);
            Service=new CargoWorldService(worldId,journal,checkpoints,this);
            CargoHubUI.Install(new HarmonyLib.Harmony("yf.cargo.qa.hubui"));
            if(Current!=null){leases.Dispose();throw new InvalidOperationException("Cargo world already attached");}lastUpdate=Time.realtimeSinceStartup;Current=this;
        }
        void Context(){if(disposed||GameManager.Instance?.World!=world)throw new InvalidOperationException("Cargo world context changed");}
        public void Restore(CargoWorldState state)
        {
            Context();if(hubs.Count!=0)throw new InvalidOperationException("Restore requires an empty native world");
            foreach(var hub in state.Hubs){hubs.Add(hub.Configuration.HubId,null);formalHubs.Add(hub.Configuration.HubId);}
            Service.Restore(state);
        }
        public CargoHubConfiguration RegisterHub(TileEntityComposite tile)
        {
            Context();var marker=CargoNativeMarkers.Read(tile,worldId);
            if(marker==null||tile.IsRemoving||world.GetTileEntity(tile.ToWorldPos())!=tile)throw new InvalidOperationException("Persistent placed hub required");
            var p=tile.ToWorldPos();var config=new CargoHubConfiguration(worldId,marker.EndpointId,new CargoPosition(p.x,p.y,p.z),marker.Owner);
            hubs.Add(config.HubId,tile);
            if(tile.block.GetBlockName()==CargoRuntime.HubBlock)formalHubs.Add(config.HubId);
            try{Service.Register(config);}catch{hubs.Remove(config.HubId);formalHubs.Remove(config.HubId);throw;}
            return config;
        }
        public CargoBinding ResolveBinding(CargoPosition at,string owner,bool source)
        {
            Context();var tile=world.GetTileEntity(new Vector3i(at.X,at.Y,at.Z));if(tile==null||tile.IsRemoving)return null;
            if(tile.block.GetBlockName()==CargoRuntime.HubBlock)return null;
            if(source?!(tile is TileEntityCollector)||!CargoRules.IsSource(tile.block.GetBlockName()):!((tile as TileEntityComposite)?.GetFeature<TEFeatureStorage>()?.bPlayerStorage??false))return null;
            CargoEndpointMarker marker;
            lock(ChunkTransferLock.For(tile.GetChunk()))
            {
                marker=CargoNativeMarkers.Read(tile,worldId);
                if(marker==null)
                {
                    // Register an instance, not a player ownership claim.
                    marker=new CargoEndpointMarker(worldId,Guid.NewGuid(),Guid.NewGuid(),"shared",tile.block.GetBlockName(),0,Guid.Empty);
                    CargoNativeMarkers.Write(tile,marker);
                }
            }
            return new CargoBinding(worldId,marker.EndpointId,marker.Incarnation,at,marker.Owner,marker.BlockName);
        }
        public bool OwnerOnline(string owner)
        {
            // The harness has no connected player. This explicit switch is only
            // honored in the already validated isolated QA context.
            if(CargoRuntime.IsQA&&Environment.GetCommandLineArgs().Contains("-yfCargoWorldScheduler"))return validationOwnerOnline;
            return GameManager.Instance.GetPersistentPlayerList()?.GetEntityPlayerFromUserId(PlatformUserIdentifierAbs.FromCombinedString(owner,false))!=null;
        }
        public void SetOwnerOnlineForValidation(bool online)
        {Context();if(!Environment.GetCommandLineArgs().Contains("-yfCargoWorldScheduler"))throw new InvalidOperationException("World scheduler QA switch required");validationOwnerOnline=online;}
        public bool HubExists(CargoHubConfiguration hub)
        {
            if(!hubs.ContainsKey(hub.HubId))return false;
            var chunk=world.GetChunkFromWorldPos(hub.Position.X,hub.Position.Z) as Chunk;
            // Absence is evidence of removal only in available native data.
            // A chunk unload/save must not create a destruction tombstone.
            if(chunk==null||chunk.IsLocked||chunk.NeedsDecoration)return true;
            var tile=world.GetTileEntity(new Vector3i(hub.Position.X,hub.Position.Y,hub.Position.Z)) as TileEntityComposite;
            if(tile==null||tile.IsRemoving)return false;
            var marker=CargoNativeMarkers.Read(tile,worldId);bool exists=marker!=null&&marker.EndpointId==hub.HubId&&marker.Owner==hub.Owner;
            if(exists)hubs[hub.HubId]=tile;return exists;
        }
        public bool Powered(CargoHubConfiguration hub)
        {return HubExists(hub)&&(CargoRuntime.IsQA&&Environment.GetCommandLineArgs().Contains("-yfCargoWorldScheduler")||Logistics.Powered(world,new Vector3i(hub.Position.X,hub.Position.Y,hub.Position.Z)));}
        public CargoPoint Home(CargoHubConfiguration hub){return new CargoPoint(hub.Position.X+.5,hub.Position.Y+(formalHubs.Contains(hub.HubId)?0:1)+(double)CargoHubModel.LandingHeight,hub.Position.Z+.5);}
        public bool Resolve(CargoBinding binding,out ICargoDurableEndpoint endpoint,out CargoPoint approach,out CargoHold hold)
        {
            Context();endpoint=null;hold=CargoHold.None;approach=new CargoPoint(binding.Position.X+.5,binding.Position.Y+2.2,binding.Position.Z+.5);
            var chunk=world.GetChunkFromWorldPos(binding.Position.X,binding.Position.Z) as Chunk;
            if(chunk==null||chunk.IsLocked||chunk.NeedsDecoration)
            {
                CargoNativeLease lease;
                if(!preflight.TryGetValue(binding.EndpointId,out lease))
                {lease=leases.Request(binding.EndpointId,binding.EndpointId,binding.Position,out hold);if(lease!=null)preflight.Add(binding.EndpointId,lease);}
                preflightUsed[binding.EndpointId]=Time.realtimeSinceStartup;
                if(hold==CargoHold.None)hold=CargoHold.ChunkLoading;return false;
            }
            ReleasePreflight(binding.EndpointId);
            var current=ResolveBinding(binding.Position,binding.Owner,CargoRules.IsSource(binding.BlockName));if(!binding.Matches(current))return false;
            var tile=world.GetTileEntity(new Vector3i(binding.Position.X,binding.Position.Y,binding.Position.Z));
            if(tile.block.isOversized)approach=new CargoPoint(approach.X,binding.Position.Y+Math.Max(2.2,tile.block.oversizedBounds.max.y+1.5),approach.Z);
            if(tile.bUserAccessing||LockManager.Instance.IsLockedServer(tile,0)||tile is TileEntityComposite composite&&Logistics.Busy(composite)){hold=CargoHold.ContainerBusy;return false;}
            try{CargoNativeAccessSessions.Register(tile,worldId);}catch(CargoNativeAccessPendingException){hold=CargoHold.ContainerBusy;return false;}
            if(!CargoNativeAccessSessions.ReadyForCargo(tile)){hold=CargoHold.ContainerBusy;return false;}
            try
            {
                var provider=world.ChunkCache.ChunkProvider as ChunkProviderGenerateWorld;
                endpoint=new CargoNativeValidationEndpoint(world,tile,worldId,provider.m_RegionFileManager);
                if(endpoint.Snapshot().Busy){hold=CargoHold.ContainerBusy;endpoint=null;return false;}return true;
            }
            catch(CargoEndpointUnavailableException){hold=CargoHold.ChunkLoading;return false;}
        }
        public ICargoAirspace OpenAirspace(Guid flight)
        {Context();Space space;if(!spaces.TryGetValue(flight,out space)){space=new Space(this,flight);spaces.Add(flight,space);}return space;}
        public void ReleaseAirspace(Guid flight)
        {Space space;if(spaces.TryGetValue(flight,out space)){space.Release();spaces.Remove(flight);}}
        void ReleasePreflight(Guid endpoint)
        {CargoNativeLease lease;if(preflight.TryGetValue(endpoint,out lease)){leases.Release(lease.Id,endpoint);preflight.Remove(endpoint);}preflightUsed.Remove(endpoint);}
        public bool ResolveRecovery(CargoHubConfiguration hub,Guid flight,out ICargoDurableEndpoint endpoint)
        {
            Context();endpoint=null;CargoNativeLease lease;CargoHold hold;
            if(!recoveryLeases.TryGetValue(flight,out lease))
            {lease=leases.Request(flight,flight,hub.Position,out hold);if(lease==null)return false;recoveryLeases.Add(flight,lease);}
            if(!leases.IsDataReadyAt(lease.Id,hub.Position))return false;
            var at=new Vector3i(hub.Position.X,hub.Position.Y,hub.Position.Z);var block=world.GetBlock(at);
            if(block.isair){world.SetBlockRPC(new BlockValueRef(at),Block.GetBlockValue(CargoRuntime.RecoveryBlock,false));block=world.GetBlock(at);}
            if(block.Block.GetBlockName()!=CargoRuntime.RecoveryBlock)return false; // Never overwrite a replacement or adjacent player's block.
            var tile=world.GetTileEntity(at) as TileEntityComposite;var storage=tile?.GetFeature<TEFeatureStorage>();if(storage==null)return false;
            var marker=CargoNativeMarkers.Read(tile,worldId);
            if(marker==null||marker.EndpointId!=flight||marker.Incarnation!=hub.HubId)
            {
                // This non-craftable recovery block is adopted only while empty
                // and unowned. Its durable intent is the removed hub checkpoint;
                // both IDs and the reserved position are derived from that intent.
                if(tile.Owner!=null||storage.items.Any(s=>s!=null&&!s.IsEmpty()))return false;
                tile.SetOwner(PlatformUserIdentifierAbs.FromCombinedString(hub.Owner,false));
                marker=new CargoEndpointMarker(worldId,flight,hub.HubId,hub.Owner,CargoRuntime.RecoveryBlock,0,Guid.Empty);
                CargoNativeMarkers.Write(tile,marker);
            }
            try{CargoNativeAccessSessions.Register(tile,worldId);}catch(CargoNativeAccessPendingException){return false;}
            if(!CargoNativeAccessSessions.ReadyForCargo(tile))return false;
            endpoint=new CargoNativeValidationEndpoint(world,tile,worldId,(world.ChunkCache.ChunkProvider as ChunkProviderGenerateWorld).m_RegionFileManager);return true;
        }
        public void Tick(long elapsed,bool paused)
        {
            Context();leases.Poll();foreach(var id in preflightUsed.Where(p=>Time.realtimeSinceStartup-p.Value>5).Select(p=>p.Key).ToArray())ReleasePreflight(id);Service.Tick(elapsed,paused);
            var all=Service.Status();var flights=new HashSet<Guid>(all.Select(s=>s.Flight));var hubIds=new HashSet<Guid>(all.Select(s=>s.Configuration.HubId));
            foreach(var id in recoveryLeases.Keys.Where(id=>!flights.Contains(id)).ToArray()){leases.Release(recoveryLeases[id].Id,id);recoveryLeases.Remove(id);}
            foreach(var id in hubs.Keys.Where(id=>!hubIds.Contains(id)).ToArray()){hubs.Remove(id);formalHubs.Remove(id);}
            CargoClientWorld.Publish(Service.Status());
            // Authority and inventory continue on dedicated servers without
            // constructing Unity models or loading their meshes and textures.
            if(GameManager.IsDedicatedServer)return;
            var states=Service.Status();var live=new HashSet<Guid>(states.Select(s=>s.Configuration.HubId));
            foreach(var id in drones.Keys.Where(k=>!live.Contains(k)).ToArray()){UnityEngine.Object.Destroy(drones[id].gameObject);drones.Remove(id);}
            foreach(var id in decks.Keys.Where(k=>!live.Contains(k)).ToArray()){UnityEngine.Object.Destroy(decks[id]);decks.Remove(id);}
            foreach(var s in states)
            {
                var id=s.Configuration.HubId;
                if(HubExists(s.Configuration)&&world.GetBlock(new Vector3i(s.Configuration.Position.X,s.Configuration.Position.Y,s.Configuration.Position.Z)).Block.GetBlockName()!=CargoRuntime.HubBlock&&!decks.ContainsKey(id))
                {var deck=CargoDroneModel.Hub();deck.transform.position=new Vector3(s.Configuration.Position.X+.5f,s.Configuration.Position.Y+1,s.Configuration.Position.Z+.5f)-Origin.position;decks.Add(id,deck);}
                if(decks.ContainsKey(id))decks[id].transform.position=new Vector3(s.Configuration.Position.X+.5f,s.Configuration.Position.Y+1,s.Configuration.Position.Z+.5f)-Origin.position;
                if(!HubExists(s.Configuration)&&decks.ContainsKey(id)){UnityEngine.Object.Destroy(decks[id]);decks.Remove(id);}
                CargoDroneVisual visual;if(!drones.TryGetValue(id,out visual)){var model=CargoDroneModel.Drone();visual=model.AddComponent<CargoDroneVisual>();drones.Add(id,visual);}
                visual.Apply(++sequence,s.Position,s.Phase,s.Hold,s.Packages,CargoDock.Resting(s.Position,Home(s.Configuration)));
            }
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;foreach(var space in spaces.Values)space.Release();spaces.Clear();leases.Dispose();
            preflight.Clear();preflightUsed.Clear();recoveryLeases.Clear();
            if(Current==this)Current=null;
            foreach(var drone in drones.Values)if(drone!=null)UnityEngine.Object.Destroy(drone.gameObject);foreach(var deck in decks.Values)if(deck!=null)UnityEngine.Object.Destroy(deck);
            drones.Clear();decks.Clear();
        }
    }
}
