using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace YFAutomation.CargoDrones
{
    // Owns the normal world's files and lifecycle. The isolated harness keeps its
    // own journal and is never silently promoted into a player's world.
    public static class CargoRuntime
    {
        public const string HubBlock="yfCargoHub";
        public const string RecoveryBlock="yfCargoRecoveryCrate";
        public const string EntranceBeaconBlock="yfCargoEntranceBeacon";
        static World world;
        static CargoFileJournal journal;
        static CargoCheckpointStore checkpoints;
        static float scanAt;
        static bool installed;
        static CargoWorldState recoveryState;
        static Queue<CargoTransaction> recoveryJobs;
        static CargoPreparedRecovery recovery;
        static CargoNativeLeaseService recoveryLoads;
        static CargoNativeLease recoveryLease;
        public static bool Recovering{get;private set;}
        public static string Failure{get;private set;}
        public static bool IsQA=>Environment.GetCommandLineArgs().Contains("-yfCargoDroneNativeQA");
        public static void Install(Harmony harmony)
        {
            if(installed)return;
            CargoHubUI.Install(harmony);
            CargoNativeAccessSessions.Install(harmony);
            CargoNativeValidationEndpoint.Install(harmony);
            CargoClientWorld.Install(harmony);
            ModEvents.GameStartDone.RegisterHandler(Start);
            ModEvents.GameUpdate.RegisterHandler(Update);
            ModEvents.WorldShuttingDown.RegisterHandler(Stopping);
            ModEvents.GameShutdown.RegisterHandler(Stopped);
            installed=true;
        }
        static void Start(ref ModEvents.SGameStartDoneData data)
        {if(!IsQA)StartWorld(GameManager.Instance?.World);}
        public static void StartWorld(World value)
        {
            if(value==null||value.IsRemote()||world==value)return;
            StopWorld();world=value;Failure=null;
            try
            {
                Guid id=CargoCollectorOwnership.WorldIdentity(value);
                string directory=Path.Combine(GameIO.GetSaveGameDir(),"CargoDrones");Directory.CreateDirectory(directory);
                journal=new CargoFileJournal(Path.Combine(directory,"inventory.wal"),id);
                CargoNativeValidationEndpoint.SetStartupQuarantine(id,CargoJournalReplay.Read(id,journal.Entries).Where(f=>f.Pending!=null).Select(f=>f.Pending.Plan.EndpointId));
                string ledger=Path.Combine(directory,"checkpoint");checkpoints=new CargoCheckpointStore(ledger,id);
                CargoWorldState saved=null;
                if(File.Exists(Path.Combine(ledger,"manifest")))saved=checkpoints.LoadWorldForRecovery(journal);
                else if(journal.Entries.Any())throw new InvalidDataException("Cargo WAL exists without its world checkpoint; recovery required");
                var runtime=new CargoNativeWorld(value,id,journal,checkpoints);
                if(saved!=null)
                {
                    recoveryState=saved;recoveryJobs=new Queue<CargoTransaction>(CargoJournalReplay.Read(id,journal.Entries).Where(f=>f.Pending!=null).Select(f=>f.Pending));
                    Recovering=true;
                    if(recoveryJobs.Count>0){recoveryLoads=new CargoNativeLeaseService(world);return;}
                    FinishRecovery();
                }
                scanAt=0;Scan();
                Log.Out("[YFCargo] World attached; persisted hubs restored.");
            }
            catch(Exception error)
            {
                Failure=error.Message;recoveryLoads?.Dispose();recoveryLoads=null;CargoNativeWorld.Current?.Dispose();
                // Preserve the files on any identity/WAL/checkpoint disagreement.
                // Never replace them with an empty world or manufacture cargo.
                Log.Error("[YFCargo] World startup blocked: "+error);
            }
        }
        static void Update(ref ModEvents.SGameUpdateData data)
        {
            if(world!=null&&world!=GameManager.Instance?.World){StopWorld();return;}
            if(world!=null&&Failure==null&&Recovering)
            {
                try{Recover();}
                catch(CargoEndpointUnavailableException){ }
                catch(CargoNativeAccessPendingException){ }
                catch(Exception error){Failure=error.Message;recoveryLoads?.Dispose();recoveryLoads=null;Log.Error("[YFCargo] Startup recovery blocked: "+error);}
                return;
            }
            if(world==null||Failure!=null||Time.realtimeSinceStartup<scanAt)return;
            scanAt=Time.realtimeSinceStartup+2;
            try{Scan();}catch(Exception error){Log.Error("[YFCargo] Hub discovery deferred: "+error.Message);}
        }
        static void Scan()
        {
            var runtime=CargoNativeWorld.Current;if(runtime==null||runtime.Service.Faulted)return;
            foreach(long key in world.ChunkCache.GetChunkKeysCopySync())
            {
                var chunk=world.ChunkCache.GetChunkSync(key) as Chunk;
                if(chunk==null||chunk.IsLocked||chunk.NeedsDecoration)continue;
                foreach(var tile in chunk.GetTileEntities().dict.Values.OfType<TileEntityComposite>().ToArray())
                {
                    if(tile.IsRemoving||tile.block.GetBlockName()!=HubBlock)continue;
                    var at=tile.ToWorldPos();
                    if(runtime.Service.Status().Any(s=>s.Configuration.Position.Equals(new CargoPosition(at.x,at.y,at.z))))continue;
                    var marker=CargoNativeMarkers.Read(tile,CargoCollectorOwnership.WorldIdentity(world));
                    if(marker==null)continue; // A world-authored prop must not acquire a made-up owner.
                    try{runtime.RegisterHub(tile);}catch(InvalidOperationException){return;} // transaction/capacity: retry next scan
                }
            }
        }
        static void Recover()
        {
            if(recoveryJobs.Count==0){FinishRecovery();return;}
            var tx=recoveryJobs.Peek();var hub=recoveryState.Hubs.Single(h=>h.Flight==tx.FlightId);
            var binding=new[]{hub.ShipmentSource,hub.ShipmentTarget}.FirstOrDefault(b=>b!=null&&b.EndpointId==tx.Plan.EndpointId);
            if(binding==null&&hub.Removed&&tx.Plan.EndpointId==hub.Flight)binding=new CargoBinding(tx.WorldId,hub.Flight,hub.Configuration.HubId,hub.Configuration.Position,hub.Configuration.Owner,RecoveryBlock);
            if(binding==null||binding.Incarnation!=tx.Plan.Incarnation)throw new InvalidDataException("Prepared endpoint has no checkpoint association");
            recoveryLoads.Poll();CargoHold hold;
            if(recoveryLease==null){recoveryLease=recoveryLoads.Request(Guid.NewGuid(),tx.FlightId,binding.Position,out hold);return;}
            if(recoveryLease.State==CargoNativeLeaseState.Failed||recoveryLease.State==CargoNativeLeaseState.Released)throw new InvalidOperationException("Recovery region could not be loaded safely");
            if(!recoveryLoads.IsDataReadyAt(recoveryLease.Id,binding.Position))return;
            if(recovery==null)
            {
                var at=new Vector3i(binding.Position.X,binding.Position.Y,binding.Position.Z);var tile=world.GetTileEntity(at);
                var marker=CargoNativeMarkers.Read(tile,tx.WorldId);
                if(marker==null||marker.EndpointId!=binding.EndpointId||marker.Incarnation!=binding.Incarnation)throw new InvalidDataException("Prepared endpoint was removed or replaced");
                CargoNativeAccessSessions.Register(tile,tx.WorldId);
                var endpoint=new CargoNativeValidationEndpoint(world,tile,tx.WorldId,(world.ChunkCache.ChunkProvider as ChunkProviderGenerateWorld).m_RegionFileManager,true);
                recovery=new CargoPreparedRecovery(journal,endpoint,tx.Id);recovery.Begin();
            }
            else recovery.Poll();
            if(recovery.State==CargoTransferState.RecoveryRequired)throw new InvalidDataException(recovery.Failure??"Prepared inventory does not match a recoverable before/after image");
            if(recovery.State!=CargoTransferState.Committed&&recovery.State!=CargoTransferState.Aborted)return;
            recoveryLoads.Release(recoveryLease.Id,tx.FlightId);recoveryLease=null;recovery=null;recoveryJobs.Dequeue();
        }
        static void FinishRecovery()
        {
            var runtime=CargoNativeWorld.Current;var missions=new List<CargoMissionState>();var hubs=new List<CargoHubState>();
            foreach(var hub in recoveryState.Hubs)
            {
                if(hub.Flight==Guid.Empty){hubs.Add(hub);continue;}
                var saved=recoveryState.Missions.Single(m=>m.Id==hub.Flight);
                var state=CargoMission.Reconcile(saved,journal,runtime.OpenAirspace(hub.Flight),hub.Removed);missions.Add(state);
                hubs.Add(new CargoHubState(hub.Configuration,hub.Flight,hub.ShipmentSource,hub.ShipmentTarget,state.Battery,hub.SourceCursor,hub.Removed));
            }
            var restored=new CargoWorldState(missions,hubs);
            checkpoints.SaveWorld(restored,journal);runtime.Restore(restored);
            recoveryLoads?.Dispose();recoveryLoads=null;recoveryState=null;recoveryJobs=null;recoveryLease=null;recovery=null;
            CargoNativeValidationEndpoint.SetStartupQuarantine(journal.WorldId,new Guid[0]);Recovering=false;
        }
        static void Stopping(ref ModEvents.SWorldShuttingDownData data){StopWorld();}
        static void Stopped(ref ModEvents.SGameShutdownData data){StopWorld();CargoClientWorld.Clear();}
        public static void StopWorld()
        {
            if(world==null&&journal==null&&checkpoints==null)return;
            var runtime=CargoNativeWorld.Current;
            try{if(runtime!=null&&!Recovering&&!runtime.Service.Faulted)runtime.Service.Checkpoint();}
            catch(Exception error){Log.Error("[YFCargo] Shutdown retains WAL for recovery: "+error.Message);}
            finally
            {
                recoveryLoads?.Dispose();recoveryLoads=null;runtime?.Dispose();checkpoints?.Dispose();journal?.Dispose();
                checkpoints=null;journal=null;world=null;Failure=null;
                recoveryState=null;recoveryJobs=null;recoveryLease=null;recovery=null;Recovering=false;
                CargoNativeAccessSessions.ResetWorld();CargoNativeValidationEndpoint.ResetWorld();
            }
        }
    }
}
