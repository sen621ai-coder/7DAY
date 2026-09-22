using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using YFAutomation.CargoDrones;

// Opt-in isolated harness. Normal runtime paths are tested in its own UserData,
// never an existing player save; native entity fixtures are not connected clients.
public sealed class CargoDroneNativeQA : IModApi
{
    static readonly List<string> lines=new List<string>();
    static ChunkManager.ChunkObserver observer;
    static CargoNativeLeaseService leaseService;
    static CargoNativeLease nativeLease;
    static CargoNativeQueuedSave queuedSave;
    static CargoNativeValidationEndpoint inventoryEndpoint;
    static CargoTransferCoordinator inventoryTransfer;
    static CargoFileJournal inventoryJournal;
    static CargoInventory inventoryBefore;
    static TileEntityCollector inventoryCollector;
    static TileEntityComposite inventoryTarget;
    static CargoNativeValidationEndpoint targetEndpoint;
    static CargoPreparedRecovery preparedRecovery;
    static CargoNativeAirspace airspace;
    static CargoMotion motion;
    static float motionTime;
    static int maxMotionChunks;
    static double maxReturnHeight;
    static World world;
    static int phase,checks,baseline,baselineChunks;
    static float deadline;
    static string report;
    static int crashOriginalCount;
    static CargoNativeWorld cargoWorld;
    static CargoCheckpointStore worldCheckpoints;
    static bool worldDeparted;
    static int worldOfflineStage;
    static CargoPoint worldOfflinePosition;
    static long worldOfflineBattery;
    static CargoHubConfiguration recoveryHub;
    static Guid destroyedFlight;
    static bool hubDestroyed;
    static CargoTransaction startupTx;
    static CargoFileJournal startupJournal;
    static CargoNativeValidationEndpoint startupEndpoint;
    public void InitMod(Mod mod)
    {
        if(!Environment.GetCommandLineArgs().Contains("-yfCargoDroneNativeQA"))return;
        CargoNativeQueuedSaves.InstallForValidation(new HarmonyLib.Harmony("yf.cargo.qa.receipts"));
        ModEvents.GameStartDone.RegisterHandler(Ready);ModEvents.GameUpdate.RegisterHandler(Update);
    }
    static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);checks++;lines.Add("PASS "+message);}
    static void Ready(ref ModEvents.SGameStartDoneData data)
    {
        if(GamePrefs.GetString(EnumGamePrefs.GameName)!="CargoDroneQA_Isolated")return;
        report=Path.Combine(GameIO.GetSaveGameDir(),"cargo-native-report.txt");world=GameManager.Instance.World;
        try
        {
            CargoNativeAccessSessions.InstallForValidation(new HarmonyLib.Harmony("yf.cargo.qa.access"));
            VerifyAccessPackets();
            VerifyCargoModels();
            bool resumeCrash=Environment.GetCommandLineArgs().Contains("-yfCargoResumeCrash");
            if(resumeCrash)
            {
                CargoNativeValidationEndpoint.InstallForValidation(new HarmonyLib.Harmony("yf.cargo.qa.inventory"));
                CargoNativeValidationEndpoint.SetRecoveryQuarantineForValidation(true);
            }
            var readback=Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith("-yfCargoReadback="));
            if(readback!=null)VerifyPreviousRegion(readback.Substring("-yfCargoReadback=".Length));
            var native=new ItemStack(ItemClass.GetItem("resourceWood"),6);native.itemValue.SetMetadata("cargo-qa","完整元数据");
            var encoded=CargoNativeItems.Encode(native);var decoded=CargoNativeItems.Decode(encoded);
            Check(decoded.count==6&&decoded.itemValue.Equals(native.itemValue),"native complete ItemValue roundtrip");
            Check(CargoNativeItems.Encode(decoded).SameValue(encoded),"stable native value encoding");
            var marker=new CargoEndpointMarker(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"QA-owner","cntDewCollector",7,Guid.NewGuid());
            byte[] payload=CargoNativeMarkers.Encode(marker);var dataRecord=new ChunkCustomData("yf.cargo.qa",ulong.MaxValue,false){data=payload};
            using(var memory=new MemoryStream())
            {
                var writer=new BinaryWriter(memory);dataRecord.Write(writer);writer.Flush();memory.Position=0;
                var copy=new ChunkCustomData();copy.Read(new BinaryReader(memory));var restored=CargoNativeMarkers.Decode(copy.data);
                Check(restored.EndpointId==marker.EndpointId&&restored.LastTransaction==marker.LastTransaction&&restored.Owner==marker.Owner,"native custom-data framing preserves marker");
                Check(memory.Position==memory.Length&&!copy.isSavedToNetwork,"native metadata bounded and server-only");
            }
            baseline=world.m_ChunkManager.m_ObservedEntities.Count;
            baselineChunks=world.ChunkCache.GetChunkKeysCopySync().Count;
            observer=GameManager.Instance.AddChunkObserver(new Vector3(8,140,8),false,0,-1);
            Check(observer.entityIdToSendChunksTo==-1,"observer fourth argument is network entity id, not a radius");
            phase=resumeCrash?20:Environment.GetCommandLineArgs().Contains("-yfCargoInventoryOnly")?10:1;deadline=Time.realtimeSinceStartup+120;
        }
        catch(Exception ex){Finish(ex);}
    }
    static void Update(ref ModEvents.SGameUpdateData data)
    {
        if(phase==0)return;
        try
        {
            if(Time.realtimeSinceStartup>deadline)throw new TimeoutException("Native load timeout; phase="+phase+"; residents="+(nativeLease==null?-1:nativeLease.ResidentChunks)+"; initialized="+(nativeLease==null?-1:nativeLease.InitializedChunks));
            if(phase==90)return; // Await the external exact-PID forced termination.
            if(phase==50)
            {
                var status=CargoNativeWorld.Current.Service.Status().Single();if(status.Phase!=CargoPhase.Loading)return;
                Check(status.Packages==0,"startup recovery fixture reaches loading before its inventory transaction");
                CargoRuntime.StopWorld();
                var marker=CargoNativeMarkers.Read(inventoryCollector,recoveryHub.WorldId);var plan=CargoPlanner.Load(CargoNativeMarkers.Snapshot(inventoryCollector,marker),new CargoItem[6],recoveryHub.Owner,1);
                startupTx=new CargoTransaction(Guid.NewGuid(),status.Flight,recoveryHub.WorldId,0,plan);
                using(var journal=new CargoFileJournal(Path.Combine(GameIO.GetSaveGameDir(),"CargoDrones","inventory.wal"),recoveryHub.WorldId))journal.Append(CargoJournalKind.Prepare,startupTx);
                CargoRuntime.StartWorld(world);
                Check(CargoRuntime.Recovering&&CargoNativeValidationEndpoint.IsFenced(inventoryCollector),"normal startup quarantines a prepared source before exposing restored flights");
                phase=51;deadline=Time.realtimeSinceStartup+120;return;
            }
            if(phase==51)
            {
                if(CargoRuntime.Failure!=null)throw new InvalidOperationException(CargoRuntime.Failure);if(CargoRuntime.Recovering)return;
                Check(CargoNativeWorld.Current.Service.Status().Single().Packages==0&&inventoryCollector.Items.Sum(s=>s==null?0:s.count)==2,"normal startup aborts an unchanged before-image without manufacturing cargo");
                CargoRuntime.StopWorld();
                startupJournal=new CargoFileJournal(Path.Combine(GameIO.GetSaveGameDir(),"CargoDrones","inventory.wal"),recoveryHub.WorldId);
                Check(startupJournal.Entries.Last().Kind==CargoJournalKind.Abort&&startupJournal.Entries.Last().Transaction.Id==startupTx.Id,"automatic startup writes the before-image ABORT exactly once");
                CargoNativeAccessSessions.Register(inventoryCollector,recoveryHub.WorldId);
                startupEndpoint=new CargoNativeValidationEndpoint(world,inventoryCollector,recoveryHub.WorldId,(world.ChunkCache.ChunkProvider as ChunkProviderGenerateWorld).m_RegionFileManager);
                var plan=CargoPlanner.Load(startupEndpoint.Snapshot(),new CargoItem[6],recoveryHub.Owner,1);
                startupTx=new CargoTransaction(Guid.NewGuid(),startupTx.FlightId,recoveryHub.WorldId,0,plan);
                var transfer=new CargoTransferCoordinator(startupJournal,startupEndpoint,startupTx);transfer.Begin();
                phase=52;deadline=Time.realtimeSinceStartup+120;return;
            }
            if(phase==52)
            {
                var saved=startupEndpoint.PollDurableSave(startupTx.Id);if(saved==CargoSaveResult.Pending)return;
                Check(saved==CargoSaveResult.Durable&&startupJournal.Entries.Last().Kind==CargoJournalKind.Prepare,"startup after-image fixture saves native inventory while retaining only PREPARE in the WAL");
                // Simulated loss of volatile process state, not an external kill.
                startupEndpoint.ReleaseFence(startupTx.Id);startupJournal.Dispose();startupJournal=null;startupEndpoint=null;
                CargoRuntime.StartWorld(world);phase=53;deadline=Time.realtimeSinceStartup+120;return;
            }
            if(phase==53)
            {
                if(CargoRuntime.Failure!=null)throw new InvalidOperationException(CargoRuntime.Failure);if(CargoRuntime.Recovering)return;
                var status=CargoNativeWorld.Current.Service.Status().Single();
                Check(status.Packages==1&&status.Phase==CargoPhase.Returning&&inventoryCollector.Items.Sum(s=>s==null?0:s.count)==1,"normal startup commits the durable after-image once and resumes the saved return corridor");
                CargoRuntime.StopWorld();
                using(var journal=new CargoFileJournal(Path.Combine(GameIO.GetSaveGameDir(),"CargoDrones","inventory.wal"),recoveryHub.WorldId))
                using(var store=new CargoCheckpointStore(Path.Combine(GameIO.GetSaveGameDir(),"CargoDrones","checkpoint"),recoveryHub.WorldId))
                {Check(journal.Entries.Count(e=>e.Transaction.Id==startupTx.Id&&e.Kind==CargoJournalKind.Commit)==1&&store.LoadWorld(journal).Missions.Single().Revision==1,"recovered WAL and world checkpoint agree before ordinary startup is permitted");}
                CargoRuntime.StartWorld(world);
                Check(CargoNativeWorld.Current.Service.Status().Single().Packages==1&&inventoryCollector.Items.Sum(s=>s==null?0:s.count)==1,"repeated normal startup cannot replay the already recovered deduction");
                CargoRuntime.StopWorld();GameManager.Instance.RemoveChunkObserver(observer);observer=null;phase=9;deadline=Time.realtimeSinceStartup+120;return;
            }
            if(phase==40)
            {
                var runtime=CargoNativeWorld.Current;if(runtime==null||runtime.Service.Faulted)throw new InvalidOperationException("Normal runtime recovery failed: "+runtime?.Service.Failure);
                var statuses=runtime.Service.Status();
                if(!hubDestroyed)
                {
                    var status=statuses.Single();if(status.Packages==0)return;
                    Check(status.Packages==3,"normal runtime flight loads the three recovery fixture packages");destroyedFlight=status.Flight;
                    world.SetBlockRPC(new BlockValueRef(new Vector3i(recoveryHub.Position.X,recoveryHub.Position.Y,recoveryHub.Position.Z)),BlockValue.Air);hubDestroyed=true;return;
                }
                if(statuses.Length!=0)return;
                var tile=world.GetTileEntity(new Vector3i(recoveryHub.Position.X,recoveryHub.Position.Y,recoveryHub.Position.Z)) as TileEntityComposite;
                Check(tile!=null&&tile.block.GetBlockName()==CargoRuntime.RecoveryBlock,"destroyed formal hub is replaced by its recovery crate");
                var marker=CargoNativeMarkers.Read(tile,recoveryHub.WorldId);
                Check(marker.EndpointId==destroyedFlight&&marker.Incarnation==recoveryHub.HubId&&marker.LastTransaction!=Guid.Empty,"native recovery crate retains deterministic flight and hub identities with a committed inventory marker");
                Check(tile.GetFeature<TEFeatureStorage>().items.Sum(s=>s==null?0:s.count)==3&&CargoPlanner.Count(targetEndpoint.Snapshot().Items)==4,"recovery preserves all three carried goods without duplicating the earlier delivered goods");
                Check(runtime.HeldChunks==0,"durable recovery releases its native observers and hub capacity");
                CargoRuntime.StopWorld();CargoRuntime.StartWorld(world);
                Check(CargoRuntime.Failure==null&&CargoNativeWorld.Current.Service.Status().Length==0&&tile.GetFeature<TEFeatureStorage>().items.Sum(s=>s==null?0:s.count)==3,"normal reload keeps recovered goods and does not resurrect the destroyed hub shipment");
                PrepareStartupRecovery();phase=50;deadline=Time.realtimeSinceStartup+120;return;
            }
            if(phase==30)
            {
                float now=Time.realtimeSinceStartup;
                if(cargoWorld.Service.Faulted)throw new InvalidOperationException("Native world scheduler failed: "+cargoWorld.Service.Failure);
                var status=cargoWorld.Service.Status().Single();
                if(!worldDeparted&&status.Flight!=Guid.Empty)
                {
                    worldDeparted=true;Check(status.Phase==CargoPhase.ToSource,"native world scheduler starts one authoritative flight");
                    var c=status.Configuration;cargoWorld.Service.Configure(c.HubId,c.Owner,c.Revision,c.SetPaused(c.Owner,c.Revision,true));
                }
                if(worldOfflineStage==0&&status.Phase==CargoPhase.ToSource&&cargoWorld.HeldChunks>0&&status.Position.Distance(cargoWorld.Home(status.Configuration))>1)
                {worldOfflinePosition=status.Position;worldOfflineBattery=status.Battery;worldOfflineStage=1;cargoWorld.SetOwnerOnlineForValidation(false);return;}
                if(worldOfflineStage==1)
                {
                    if(status.Hold!=CargoHold.OwnerOffline)return;
                    Check(cargoWorld.HeldChunks==0,"offline world checkpoint releases actual native flight observers");
                    var frozen=worldCheckpoints.LoadWorld(inventoryJournal).Missions.Single();
                    Check(frozen.Motion.Position.Distance(worldOfflinePosition)<1e-7&&frozen.Battery==worldOfflineBattery,"offline native route and energy are durable before suspension");
                    Check(status.Position.Distance(worldOfflinePosition)<1e-7&&status.Battery==worldOfflineBattery,"offline native flight neither moves nor drains energy");
                    cargoWorld.SetOwnerOnlineForValidation(true);worldOfflineStage=2;return;
                }
                if(worldOfflineStage==2&&status.Position.Distance(worldOfflinePosition)>1e-7)
                {
                    Check(cargoWorld.HeldChunks>0&&status.Position.Distance(worldOfflinePosition)<=.600001,"resumed native flight reacquires observers and advances without teleporting");worldOfflineStage=3;
                }
                if(!worldDeparted||status.Phase!=CargoPhase.Docked)return;
                Check(worldOfflineStage==3,"native delivery includes completed offline release and resume cycle");
                Check(CargoPlanner.Count(inventoryEndpoint.Snapshot().Items)==0&&CargoPlanner.Count(targetEndpoint.Snapshot().Items)==CargoPlanner.Count(inventoryBefore.Items),"native world scheduled flight moves all remaining real goods and returns home");
                Check(status.Packages==0&&status.Battery<600000,"scheduled native flight accounts for cargo and energy");
                var saved=worldCheckpoints.LoadWorld(inventoryJournal);
                Check(saved.Hubs.Length==1&&saved.Missions.Length==1&&saved.Hubs[0].Flight==status.Flight&&saved.Missions[0].Phase==CargoPhase.Docked,"native world checkpoint persists hub shipment and docked route together");
                Check(cargoWorld.HeldChunks==0,"native world docking releases all owned flight observers after checkpoint");
                Check(UnityEngine.Object.FindObjectsOfType<CargoDroneVisual>().Length==(GameManager.IsDedicatedServer?0:1),"dedicated scheduler creates no visual drone; local scheduler maintains one controller per hub");
                cargoWorld.Dispose();cargoWorld=null;worldCheckpoints.Dispose();worldCheckpoints=null;
                VerifyNormalWorldLifecycle();
                phase=40;deadline=now+120;return;
            }
            if(phase==20)
            {
                var crashChunk=world.GetChunkFromWorldPos(8,8) as Chunk;if(crashChunk==null||crashChunk.IsLocked||crashChunk.NeedsDecoration)return;
                var evidence=File.ReadAllLines(Path.Combine(GameIO.GetSaveGameDir(),"cargo-crash-ready.txt"));
                if(evidence.Length!=4)throw new Exception("Invalid crash checkpoint evidence");
                Guid worldId=Guid.Parse(evidence[0]),txId=Guid.Parse(evidence[1]),flightId=Guid.Parse(evidence[2]);crashOriginalCount=int.Parse(evidence[3]);
                inventoryJournal=new CargoFileJournal(Path.Combine(GameIO.GetSaveGameDir(),"cargo-native-inventory.wal"),worldId);
                var entry=inventoryJournal.Entries.Last();var tx=entry.Transaction;
                Check(entry.Kind==CargoJournalKind.Prepare&&tx.Id==txId&&tx.FlightId==flightId,"killed process restarts with exact uncommitted PREPARE in real WAL");
                inventoryCollector=crashChunk.GetTileEntity(new Vector3i(3,150,1)) as TileEntityCollector;
                inventoryTarget=crashChunk.GetTileEntity(new Vector3i(5,150,1)) as TileEntityComposite;
                Check(inventoryCollector!=null&&inventoryTarget!=null,"native source and target reload from forcibly terminated save");
                var provider=world.ChunkCache.ChunkProvider as ChunkProviderGenerateWorld;
                inventoryEndpoint=new CargoNativeValidationEndpoint(world,inventoryCollector,worldId,provider.m_RegionFileManager);
                targetEndpoint=new CargoNativeValidationEndpoint(world,inventoryTarget,worldId,provider.m_RegionFileManager);
                Check(CargoPlanner.Equal(inventoryEndpoint.Snapshot().Items,tx.Plan.EndpointAfter)&&inventoryEndpoint.LastTransaction==tx.Id,"native post-crash source has exact durable after-image and transaction marker");
                preparedRecovery=new CargoPreparedRecovery(inventoryJournal,inventoryEndpoint,tx.Id);preparedRecovery.Begin();
                Check(preparedRecovery.State==CargoTransferState.Saving,"post-crash recovery waits for fresh native durable save");
                phase=21;deadline=Time.realtimeSinceStartup+120;return;
            }
            if(phase==21)
            {
                preparedRecovery.Poll();if(preparedRecovery.State==CargoTransferState.RecoveryRequired)throw new Exception("Post-crash native recovery failed");
                if(preparedRecovery.State!=CargoTransferState.Committed)return;
                var result=preparedRecovery.Result();
                Check(result.Revision==3&&CargoPlanner.Count(result.Cargo)==1&&inventoryJournal.Entries.Count==6,"post-crash recovery commits once without duplicate source debit");
                Check(CargoPlanner.Count(inventoryEndpoint.Snapshot().Items)+CargoPlanner.Count(targetEndpoint.Snapshot().Items)+CargoPlanner.Count(result.Cargo)==crashOriginalCount,"post-crash source target and recovered cargo conserve original quantity");
                CargoNativeValidationEndpoint.SetRecoveryQuarantineForValidation(false);
                GameManager.Instance.RemoveChunkObserver(observer);observer=null;phase=9;deadline=Time.realtimeSinceStartup+120;return;
            }
            if(phase==10)
            {
                var ready=world.GetChunkFromWorldPos(8,8) as Chunk;
                if(ready==null||ready.IsLocked||ready.NeedsDecoration)return;
                StartInventoryTransfer(ready);phase=11;deadline=Time.realtimeSinceStartup+120;return;
            }
            if(phase==11)
            {
                inventoryTransfer.Poll();if(inventoryTransfer.State==CargoTransferState.RecoveryRequired)throw new Exception("Native inventory transaction requires recovery");
                if(inventoryTransfer.State!=CargoTransferState.Committed)return;
                var after=inventoryEndpoint.Snapshot();var tx=inventoryTransfer.Transaction;
                Check(CargoPlanner.Equal(after.Items,tx.Plan.EndpointAfter)&&after.Revision==inventoryBefore.Revision+1&&inventoryEndpoint.LastTransaction==tx.Id,"real collector inventory and transaction revision match committed after-image");
                Check(!CargoNativeValidationEndpoint.IsFenced(inventoryCollector),"native endpoint fence releases only after durable transaction commit");
                StartInventoryUnload(tx);phase=12;deadline=Time.realtimeSinceStartup+120;return;
            }
            if(phase==12)
            {
                inventoryTransfer.Poll();if(inventoryTransfer.State==CargoTransferState.RecoveryRequired)throw new Exception("Native target inventory transaction requires recovery");
                if(inventoryTransfer.State!=CargoTransferState.Committed)return;
                var tx=inventoryTransfer.Transaction;var targetAfter=targetEndpoint.Snapshot();
                Check(CargoPlanner.Equal(targetAfter.Items,tx.Plan.EndpointAfter)&&targetAfter.Revision==1&&targetEndpoint.LastTransaction==tx.Id,"real player-storage target durably receives exact item metadata and transaction marker");
                Check(CargoPlanner.Count(inventoryEndpoint.Snapshot().Items)+CargoPlanner.Count(targetAfter.Items)==CargoPlanner.Count(inventoryBefore.Items),"real source and target inventories conserve total quantity after both commits");
                Check(!CargoNativeValidationEndpoint.IsFenced(inventoryTarget),"native target fence releases after unload commit");
                string journalPath=Path.Combine(GameIO.GetSaveGameDir(),"cargo-native-inventory.wal");inventoryJournal.Dispose();inventoryJournal=null;
                using(var read=new CargoFileJournal(journalPath,tx.WorldId))
                {
                    var recovered=CargoJournalReplay.Read(tx.WorldId,read.Entries).Single();
                    Check(recovered.Revision==2&&CargoPlanner.Count(recovered.Cargo)==0,"real WAL rebuilds empty cargo only after native source and target commits");
                }
                if(Environment.GetCommandLineArgs().Contains("-yfCargoWorldScheduler")){StartWorldScheduler(tx.WorldId);phase=30;deadline=Time.realtimeSinceStartup+240;return;}
                StartPreparedRecoveryFixture(tx.WorldId,tx.FlightId);phase=13;deadline=Time.realtimeSinceStartup+120;return;
            }
            if(phase==13)
            {
                var tx=inventoryTransfer.Transaction;var status=inventoryEndpoint.PollDurableSave(tx.Id);
                if(status==CargoSaveResult.Pending)return;if(status!=CargoSaveResult.Durable)throw new Exception("Prepared recovery fixture was not durably saved");
                Check(inventoryJournal.Entries.Last().Kind==CargoJournalKind.Prepare,"native recovery fixture has durable after-image but no WAL commit");
                if(Environment.GetCommandLineArgs().Contains("-yfCargoCrashAfterNativeSave"))
                {
                    lines.Add("ARMED external forced termination after native save, before WAL commit");File.WriteAllLines(report,lines);
                    var evidence=System.Text.Encoding.UTF8.GetBytes(tx.WorldId+"\n"+tx.Id+"\n"+tx.FlightId+"\n"+CargoPlanner.Count(inventoryBefore.Items));
                    using(var file=new FileStream(Path.Combine(GameIO.GetSaveGameDir(),"cargo-crash-ready.txt"),FileMode.CreateNew,FileAccess.Write,FileShare.Read)){file.Write(evidence,0,evidence.Length);file.Flush(true);}
                    phase=90;deadline=Time.realtimeSinceStartup+300;return;
                }
                // Simulate loss of process-local ownership in this no-player QA
                // world. This is not a forced crash or restart acceptance test.
                inventoryEndpoint.ReleaseFence(tx.Id);inventoryJournal.Dispose();
                inventoryJournal=new CargoFileJournal(Path.Combine(GameIO.GetSaveGameDir(),"cargo-native-inventory.wal"),tx.WorldId);
                var provider=world.ChunkCache.ChunkProvider as ChunkProviderGenerateWorld;
                inventoryEndpoint=new CargoNativeValidationEndpoint(world,inventoryCollector,tx.WorldId,provider.m_RegionFileManager);
                preparedRecovery=new CargoPreparedRecovery(inventoryJournal,inventoryEndpoint,tx.Id);preparedRecovery.Begin();
                Check(preparedRecovery.State==CargoTransferState.Saving&&CargoNativeValidationEndpoint.IsFenced(inventoryCollector),"native prepared recovery reacquires fence and requests new durable confirmation");
                phase=14;return;
            }
            if(phase==14)
            {
                preparedRecovery.Poll();if(preparedRecovery.State==CargoTransferState.RecoveryRequired)throw new Exception("Native prepared recovery failed");
                if(preparedRecovery.State!=CargoTransferState.Committed)return;
                var result=preparedRecovery.Result();var tx=inventoryTransfer.Transaction;
                Check(CargoPlanner.Equal(inventoryEndpoint.Snapshot().Items,tx.Plan.EndpointAfter),"native prepared recovery never subtracts the source package a second time");
                Check(result.Revision==3&&CargoPlanner.Count(result.Cargo)==1&&inventoryJournal.Entries.Count==6,"native prepared recovery resolves one existing PREPARE with one COMMIT");
                Check(CargoPlanner.Count(inventoryEndpoint.Snapshot().Items)+CargoPlanner.Count(targetEndpoint.Snapshot().Items)+CargoPlanner.Count(result.Cargo)==CargoPlanner.Count(inventoryBefore.Items),"native inventories plus recovered cargo conserve all original packages");
                GameManager.Instance.RemoveChunkObserver(observer);observer=null;phase=9;deadline=Time.realtimeSinceStartup+120;return;
            }
            if(phase==4)
            {
                int current=world.ChunkCache.GetChunkKeysCopySync().Count;
                if(current>baselineChunks)return;
                Check(current<=baselineChunks,"resident chunks return to pre-observer baseline");
                leaseService=new CargoNativeLeaseService(world,9,9);CargoHold hold;
                nativeLease=leaseService.Request(Guid.NewGuid(),Guid.NewGuid(),new CargoPosition(-808,140,8),out hold);
                Check(nativeLease!=null&&leaseService.HeldChunks==9,"native service admits measured footprint before creating observer");
                Check(leaseService.Request(nativeLease.Id,nativeLease.Flight,nativeLease.Center,out hold)==nativeLease&&leaseService.Count==1,"native duplicate lease does not create another observer");
                Check(leaseService.Request(Guid.NewGuid(),nativeLease.Flight,new CargoPosition(8,140,8),out hold)==null&&hold==CargoHold.ChunkBudget,"native lease refuses over-budget expansion and retains safe window");
                phase=5;deadline=Time.realtimeSinceStartup+120;return;
            }
            if(phase==5)
            {
                leaseService.Poll();if(nativeLease.State==CargoNativeLeaseState.Failed)throw new Exception(nativeLease.Failure);
                if(nativeLease.State!=CargoNativeLeaseState.Ready)return;
                Check(leaseService.IsDataReadyAt(nativeLease.Id,nativeLease.Center),"native service validates requested block data at negative coordinates");
                Check(!leaseService.IsDataReadyAt(nativeLease.Id,new CargoPosition(8,140,8)),"lease readiness cannot authorize reads outside its footprint");
                lines.Add("Native service resident="+nativeLease.ResidentChunks+" initialized="+nativeLease.InitializedChunks+" dataReady="+nativeLease.DataReadyChunks+" charged="+leaseService.HeldChunks);
                Check(!leaseService.Release(nativeLease.Id,Guid.NewGuid())&&leaseService.HeldChunks==9,"foreign flight cannot release native safe window");
                leaseService.Dispose();leaseService.Dispose();
                Check(leaseService.Count==0&&leaseService.HeldChunks==0&&world.m_ChunkManager.m_ObservedEntities.Count==baseline,"native service disposal releases budget and observer exactly once");
                phase=6;deadline=Time.realtimeSinceStartup+120;return;
            }
            if(phase==6)
            {
                if(world.ChunkCache.GetChunkKeysCopySync().Count>baselineChunks)return;
                if(queuedSave.Status==CargoSaveResult.Uncertain)throw new Exception("Native queued save failed: "+queuedSave.Failure);
                if(queuedSave.Status!=CargoSaveResult.Durable)return;
                Check(true,"world asynchronous save queue confirms exact endpoint snapshot after native flush and fresh-header readback");
                Check(true,"native service disposal returns resident chunks to baseline");
                leaseService=new CargoNativeLeaseService(world,24,128);
                airspace=new CargoNativeAirspace(world,leaseService,Guid.NewGuid());
                motion=new CargoMotion(airspace,new CargoPoint(8,240,8),new CargoPoint(136,240,8),600000,returnReserve:180000);
                motionTime=Time.realtimeSinceStartup;phase=7;deadline=motionTime+180;return;
            }
            if(phase==7||phase==8)
            {
                float now=Time.realtimeSinceStartup;long elapsed=(long)((now-motionTime)*1000);
                if(elapsed<=0)return;motionTime=now;
                var before=motion.Position;motion.Tick(elapsed);
                if(phase==8)maxReturnHeight=Math.Max(maxReturnHeight,motion.Position.Y);
                if(before.Distance(motion.Position)>.600001)throw new Exception("Native movement teleported");
                maxMotionChunks=Math.Max(maxMotionChunks,leaseService.HeldChunks);
                if(maxMotionChunks>24)throw new Exception("Native rolling window exceeded flight budget");
                if(motion.Hold==CargoHold.RecoveryRequired)throw new Exception("Native high-altitude route failed: "+motion.Hold);
                if(!motion.Arrived)return;
                Check(world.Players.Count==0,"continuous native-data swept flight reached endpoint without players, phase="+phase);
                if(phase==7)
                {
                    var at=motion.Position;long battery=motion.Battery;motion.Tick(100,false);
                    Check(motion.Position.Distance(at)==0&&motion.Battery==battery,"native route freezes when owner offline");
                    // A real voxel wall in this disposable QA world forces the
                    // return leg to exercise native collision and local routing.
                    var wallChunk=world.GetChunkFromWorldPos(130,8) as Chunk;
                    Check(wallChunk!=null&&!wallChunk.IsLocked,"return wall fixture chunk is resident");
                    var stone=Block.GetBlockValue("terrStone",false);
                    Check(!stone.isair&&stone.Block.IsCollideMovement,"native wall fixture uses movement-colliding stone");
                    for(int y=239;y<=242;y++)for(int z=6;z<=10;z++)
                    {
                        if(!world.GetBlock(new Vector3i(130,y,z)).isair)throw new Exception("Expected empty QA wall fixture");
                        wallChunk.SetBlockRaw(130&15,y,z&15,stone);
                    }
                    motion.Retarget(new CargoPoint(8,240,8));phase=8;deadline=now+180;return;
                }
                Check(motion.DistanceTravelled>256&&motion.Battery+motion.MovingUnits==600000,"native out-and-back charges actual obstacle detour distance and battery");
                Check(motion.RouteProbes>0&&maxReturnHeight>242&&maxReturnHeight<=253,"native wall triggers bounded elevation route without exceeding world ceiling");
                lines.Add("Native motion peak charged chunks="+maxMotionChunks+" moving milliseconds="+motion.MovingUnits);
                airspace.Dispose();airspace.Dispose();leaseService.Dispose();
                Check(leaseService.HeldChunks==0&&world.m_ChunkManager.m_ObservedEntities.Count==baseline,"native airspace releases all rolling windows");
                phase=9;deadline=now+120;return;
            }
            if(phase==9)
            {
                if(world.ChunkCache.GetChunkKeysCopySync().Count>baselineChunks)return;
                Check(!Resources.FindObjectsOfTypeAll<Mesh>().Any(m=>m.name.StartsWith("BusterMesh",StringComparison.Ordinal))&&!Resources.FindObjectsOfTypeAll<Material>().Any(m=>m.name=="BusterBody"||m.name=="BusterLegs"),"last Buster instance releases shared native meshes and materials");
                Check(true,"test observer cleanup returns resident chunks to baseline");Finish(null);return;
            }
            int x=phase==2?808:8;var chunk=world.GetChunkFromWorldPos(x,8) as Chunk;
            // chunksLoaded tracks chunks sent to network clients, not resident server
            // chunks. A server-only observer never populates that set.
            if(chunk==null||chunk.IsLocked||observer.curChunkPos.x!=(x>>4)||observer.curChunkPos.z!=0||observer.chunksAround.list.Count==0)return;
            if(phase==1)
            {
                Check(world.Players.Count==0,"observer loads local chunk without a player");
                lines.Add("Local observer requested footprint="+observer.chunksAround.list.Count);
                RegionRoundtrip(chunk);
                QueueNativeSave(chunk);
                observer.SetPosition(new Vector3(808,140,8));phase=2;deadline=Time.realtimeSinceStartup+120;
            }
            else if(phase==2)
            {
                Check(world.Players.Count==0,"observer moves 800 blocks without a player");
                lines.Add("Remote observer requested footprint="+observer.chunksAround.list.Count);
                Check(observer.chunksAround.list.Count<=24,"minimum observer footprint fits flight budget");
                observer.SetPosition(new Vector3(8,140,8));phase=3;deadline=Time.realtimeSinceStartup+120;
            }
            else
            {
                Check(world.Players.Count==0,"observer returns 800 blocks without a player");
                GameManager.Instance.RemoveChunkObserver(observer);observer=null;
                Check(world.m_ChunkManager.m_ObservedEntities.Count==baseline,"observer registration released");
                lines.Add("Pre-observer resident baseline="+baselineChunks);
                phase=4;deadline=Time.realtimeSinceStartup+120;
            }
        }
        catch(Exception ex){Finish(ex);}
    }
    static void Finish(Exception error)
    {
        phase=0;
        CargoRuntime.StopWorld();
        startupJournal?.Dispose();startupJournal=null;
        cargoWorld?.Dispose();cargoWorld=null;worldCheckpoints?.Dispose();worldCheckpoints=null;
        if(inventoryJournal!=null){inventoryJournal.Dispose();inventoryJournal=null;}
        if(airspace!=null)airspace.Dispose();
        if(leaseService!=null)leaseService.Dispose();
        if(observer!=null&&GameManager.Instance!=null){GameManager.Instance.RemoveChunkObserver(observer);observer=null;}
        lines.Add(error==null?"FINISHED checks="+checks+" failures=0":"FAIL "+error);
        lines.Add(Environment.GetCommandLineArgs().Contains("-yfCargoWorldScheduler")?
            "Scope: isolated server, native inventory transport, formal pad collision, normal hub lifecycle/reload, native EntityPlayer command fixture, destruction recovery, simulated volatile-loss startup recovery with native durable inventory; NOT connected clients, interactive UI, real power or full crash acceptance.":
            Environment.GetCommandLineArgs().Contains("-yfCargoResumeCrash")?
            "Scope: actual forced-termination restart after native save and before WAL commit; NOT full P0-P5 matrix, production startup recovery, multiplayer or complete drone acceptance.":
            Environment.GetCommandLineArgs().Contains("-yfCargoInventoryOnly")?
            "Scope: isolated no-player native inventory transaction adapter, snapshot receipts and WAL; NOT production writer coverage, crash recovery, multiplayer or complete drone acceptance.":
            "Scope: native item/custom-data, save receipts, normal-restart readback, leases, continuous data-based high-altitude movement; NOT visible drone, crash recovery, writer fencing, complete collision or multiplayer acceptance.");
        if(report!=null)File.WriteAllLines(report,lines);Application.Quit();
    }
    static void RegionRoundtrip(Chunk original)
    {
        // Work on an unattached clone and a separate region directory. Never replace
        // a live chunk or use the world's RegionFileManager for this experiment.
        var snapshot=new RegionFileChunkSnapshot();var fixtureSnapshot=new RegionFileChunkSnapshot();
        try
        {
            snapshot.Update(original,true);
            var fixture=new Chunk(original.X,original.Z);
            using(var memory=new MemoryStream(snapshot.stream.ToArray(),false))
            using(var reader=MemoryPools.poolBinaryReader.AllocSync(false))
            {reader.SetBaseStream(memory);reader.ReadBytes(4);uint version=reader.ReadUInt32();fixture.load(reader,version);}
            var position=new Vector3i(1,150,1);
            Check(fixture.GetTileEntity(position)==null,"isolated collector fixture position is empty");
            fixture.SetBlockRaw(position.x,position.y,position.z,Block.GetBlockValue("yfAutoForestry",false));
            var collector=new TileEntityCollector(fixture){localChunkPos=position};fixture.AddTileEntity(collector);
            Check(collector.Items.Length>0,"native collector fixture has inventory slots");
            var stack=new ItemStack(ItemClass.GetItem("resourceWood"),6);stack.itemValue.SetMetadata("cargo-save-proof","保留完整值");
            collector.Items[0]=stack;
            var marker=new CargoEndpointMarker(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"QA-owner","yfAutoForestry",1,Guid.NewGuid());
            CargoNativeMarkers.Write(collector,marker);
            fixtureSnapshot.Update(fixture,true);var expected=fixtureSnapshot.stream.ToArray();
            string directory=Path.Combine(GameIO.GetSaveGameDir(),"cargo-region-probe-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            var receipt=CargoNativeSaveReceipts.Watch(fixtureSnapshot,directory,fixture.X,fixture.Z);
            Check(receipt.Status==CargoSaveResult.Pending,"snapshot receipt stays pending before native write");
            var output=new RegionFileAccessRaw();
            try{fixtureSnapshot.Write(new RegionFileChunkWriter(output),directory,fixture.X,fixture.Z);}finally{output.Close();}
            Check(receipt.Status==CargoSaveResult.Durable,"exact snapshot receives native flush/readback receipt: "+receipt.Failure);
            var files=Directory.GetFiles(directory);Check(files.Length>0,"native region writer produced disk files");
            foreach(string file in files)using(var handle=new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.Read)){handle.Flush(true);}
            var input=new RegionFileAccessRaw();
            try
            {
                var reader=new RegionFileChunkReader(input);uint version;
                var binary=reader.readIntoLoadStream(directory,fixture.X,fixture.Z,"7rg",out version);
                Check(binary!=null&&reader.loadChunkMemoryStream.ToArray().SequenceEqual(expected.Skip(8)),"fresh native reader returns exact flushed snapshot body");
                var restored=new Chunk(fixture.X,fixture.Z);restored.load(binary,version);
                var saved=restored.GetTileEntity(position) as TileEntityCollector;
                Check(saved!=null&&saved.Items[0].count==6&&saved.Items[0].itemValue.Equals(stack.itemValue),"native region preserves collector inventory and full item value");
                var savedMarker=CargoNativeMarkers.Read(saved,marker.WorldId);
                Check(savedMarker!=null&&savedMarker.LastTransaction==marker.LastTransaction&&savedMarker.EndpointId==marker.EndpointId&&savedMarker.Revision==1,"inventory and commit marker share native region snapshot");
                lines.Add("Region flush/readback fixture="+Path.GetFileName(directory));
                using(var file=new FileStream(Path.Combine(directory,"expected.snapshot"),FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {file.Write(expected,0,expected.Length);file.Flush(true);}
                File.WriteAllText(Path.Combine(directory,"coordinates.txt"),fixture.X+"\n"+fixture.Z);
            }
            finally{input.Close();}
            var changed=new RegionFileChunkSnapshot();
            try
            {
                string rejectedDirectory=Path.Combine(directory,"rejected");Directory.CreateDirectory(rejectedDirectory);
                changed.Update(fixture,true);var invalid=CargoNativeSaveReceipts.Watch(changed,rejectedDirectory,fixture.X,fixture.Z);
                collector.Items[0]=new ItemStack(stack.itemValue.Clone(),5);changed.Update(fixture,true);
                // Different destination prevents this deliberately invalid request
                // from replacing the fixture retained for next-process readback.
                var rejectedOutput=new RegionFileAccessRaw();
                try{changed.Write(new RegionFileChunkWriter(rejectedOutput),rejectedDirectory,fixture.X,fixture.Z);}finally{rejectedOutput.Close();}
                Check(invalid.Status==CargoSaveResult.Uncertain,"mutated snapshot cannot obtain durable receipt");
            }
            finally{if(changed.stream!=null)MemoryPools.poolMS.FreeSync(changed.stream);}
        }
        finally
        {
            if(snapshot.stream!=null)MemoryPools.poolMS.FreeSync(snapshot.stream);
            if(fixtureSnapshot.stream!=null)MemoryPools.poolMS.FreeSync(fixtureSnapshot.stream);
        }
    }
    static void VerifyPreviousRegion(string directory)
    {
        var coordinates=File.ReadAllLines(Path.Combine(directory,"coordinates.txt"));int x=int.Parse(coordinates[0]),z=int.Parse(coordinates[1]);
        var expected=File.ReadAllBytes(Path.Combine(directory,"expected.snapshot"));var input=new RegionFileAccessRaw();
        try
        {
            var reader=new RegionFileChunkReader(input);uint version;
            var binary=reader.readIntoLoadStream(directory,x,z,"7rg",out version);
            Check(binary!=null&&reader.loadChunkMemoryStream.ToArray().SequenceEqual(expected.Skip(8)),"previous process snapshot survives restart with exact bytes");
            var restored=new Chunk(x,z);restored.load(binary,version);
            var collector=restored.GetTileEntity(new Vector3i(1,150,1)) as TileEntityCollector;
            Check(collector!=null&&collector.Items[0].count==6,"previous process native collector inventory survives restart");
        }
        finally{input.Close();}
    }
    static void QueueNativeSave(Chunk chunk)
    {
        var provider=world.ChunkCache.ChunkProvider as ChunkProviderGenerateWorld;
        if(provider==null)throw new NotSupportedException("Native QA world uses an untested save provider");
        var position=new Vector3i(2,150,1);
        Check(chunk.GetTileEntity(position)==null,"isolated world async fixture position is empty");
        chunk.SetBlockRaw(position.x,position.y,position.z,Block.GetBlockValue("yfAutoForestry",false));
        var collector=new TileEntityCollector(chunk){localChunkPos=position};chunk.AddTileEntity(collector);
        var stack=new ItemStack(ItemClass.GetItem("resourceWood"),4);stack.itemValue.SetMetadata("queue-proof","异步快照");collector.Items[0]=stack;
        var marker=new CargoEndpointMarker(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"QA-owner","yfAutoForestry",2,Guid.NewGuid());CargoNativeMarkers.Write(collector,marker);
        lines.Add("Native queue region access="+provider.m_RegionFileManager.regionFileAccess.GetType().Name);
        queuedSave=CargoNativeQueuedSaves.EnqueueForValidation(provider.m_RegionFileManager,collector,marker,collector.Items.Select(CargoNativeItems.Encode).ToArray());
        Check(queuedSave.SnapshotCaptured&&queuedSave.Status!=CargoSaveResult.Uncertain,"world save queue captures matching inventory and transaction marker: "+queuedSave.Failure);
    }
    static void StartInventoryTransfer(Chunk chunk)
    {
        CargoNativeValidationEndpoint.InstallForValidation(new HarmonyLib.Harmony("yf.cargo.qa.inventory"));
        var p=new Vector3i(3,150,1);Check(chunk.GetTileEntity(p)==null,"native transaction fixture is empty");
        string selected=Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith("-yfCargoSourceBlock=",StringComparison.Ordinal));
        string sourceName=selected==null?"yfAutoForestry":selected.Substring("-yfCargoSourceBlock=".Length);
        if(!CargoRules.IsSource(sourceName))throw new InvalidOperationException("Unrecognized QA source block");
        lines.Add("AUDIT actual scheduled source="+sourceName+"; seeded native output inventory; no player production/placement interaction");
        chunk.SetBlockRaw(p.x,p.y,p.z,Block.GetBlockValue(sourceName,false));
        inventoryCollector=new TileEntityCollector(chunk){localChunkPos=p};chunk.AddTileEntity(inventoryCollector);
        var output=inventoryCollector.GetSlotOutputType(0);if(output==null)throw new Exception("Missing collector output definition");
        var value=ItemClass.GetItem(output.OutputItem);value.SetMetadata("native-cargo-proof","真实库存事务");
        inventoryCollector.Items[0]=new ItemStack(value,Math.Min(4,value.ItemClass.Stacknumber.Value));
        var owner=PlatformUserIdentifierAbs.FromPlatformAndId("Steam","76561198000000000",false);
        var marker=new CargoEndpointMarker(CargoCollectorOwnership.WorldIdentity(world),Guid.NewGuid(),Guid.NewGuid(),owner.CombinedString,sourceName,0,Guid.Empty);
        CargoNativeMarkers.Write(inventoryCollector,marker);
        CargoNativeAccessSessions.Register(inventoryCollector,marker.WorldId);
        Check(CargoNativeAccessSessions.ReadyForCargo(inventoryCollector),"new isolated collector registration has no untracked player session");
        var provider=world.ChunkCache.ChunkProvider as ChunkProviderGenerateWorld;
        inventoryEndpoint=new CargoNativeValidationEndpoint(world,inventoryCollector,marker.WorldId,provider.m_RegionFileManager);
        inventoryBefore=inventoryEndpoint.Snapshot();var plan=CargoPlanner.Load(inventoryBefore,new CargoItem[6],marker.Owner,1);
        Check(plan.Moved==1,"native whitelist and output slot allow exactly one package load");
        var tx=new CargoTransaction(Guid.NewGuid(),Guid.NewGuid(),marker.WorldId,0,plan);
        inventoryJournal=new CargoFileJournal(Path.Combine(GameIO.GetSaveGameDir(),"cargo-native-inventory.wal"),marker.WorldId);
        inventoryTransfer=new CargoTransferCoordinator(inventoryJournal,inventoryEndpoint,tx);inventoryTransfer.Begin();
        Check(inventoryTransfer.State==CargoTransferState.Saving&&CargoNativeValidationEndpoint.IsFenced(inventoryCollector),"real inventory application remains fenced while awaiting native async save");
        var competitor=new CargoNativeValidationEndpoint(world,inventoryCollector,marker.WorldId,provider.m_RegionFileManager);
        Check(competitor.Snapshot().Busy&&!competitor.AcquireFence(Guid.NewGuid(),1),"second native adapter sees busy endpoint and cannot acquire transaction fence");
        Check(CargoPlanner.Equal(inventoryEndpoint.Snapshot().Items,plan.EndpointAfter),"native ItemStack array contains exact planned after-image including metadata");
        var offered=CargoNativeItems.Decode(plan.CargoAfter.First(i=>i!=null));int count=offered.count;
        Check(!inventoryCollector.AddItem(offered)&&offered.count==count,"fenced native collector AddItem refuses without consuming offered stack");
        var stacked=inventoryCollector.TryStackItem(0,offered);
        Check(!stacked.Item1&&!stacked.Item2&&offered.count==count,"fenced native collector stacking refuses without changing caller input");
        RejectNativeWrite(()=>inventoryCollector.UpdateSlot(0,ItemStack.Empty),"fenced native collector UpdateSlot explicitly rejects mutation");
        RejectNativeWrite(()=>inventoryCollector.Items=inventoryCollector.Items,"fenced native collector setter rejects unauthorized replacement");
        inventoryCollector.UpdateTick(world);
        Check(CargoPlanner.Equal(inventoryEndpoint.Snapshot().Items,plan.EndpointAfter),"fenced native production tick cannot alter prepared collector inventory");
    }
    static void StartWorldScheduler(Guid worldId)
    {
        inventoryJournal=new CargoFileJournal(Path.Combine(GameIO.GetSaveGameDir(),"cargo-native-inventory.wal"),worldId);
        var chunk=inventoryCollector.GetChunk();var p=new Vector3i(12,170,12);var block=Block.GetBlockValue(CargoRuntime.HubBlock,false);
        Check(chunk.GetTileEntity(p)==null,"world hub fixture position is unused");chunk.SetBlockRaw(p.x,p.y,p.z,block);
        var actor=PlatformUserIdentifierAbs.FromPlatformAndId("Steam","76561198000000002",false);var owner=actor.CombinedString;
        var point=chunk.GetWorldPos()+p;block.Block.OnBlockAdded(world,chunk,point,block,actor);
        var hub=world.GetTileEntity(point) as TileEntityComposite;
        Check(hub!=null&&CargoNativeMarkers.Read(hub,worldId)?.Owner==owner,"formal cargo hub is placed and receives identity through native callbacks");
        var bounds=new List<Bounds>();block.Block.GetCollisionAABB(block,point.x,point.y,point.z,0,bounds);
        Check(bounds.Any(b=>b.size.x>1.9f&&b.max.y>=point.y+1),"formal pad has its real solid two-block-wide collision envelope");
        worldCheckpoints=new CargoCheckpointStore(Path.Combine(GameIO.GetSaveGameDir(),"cargo-world-checkpoint"),worldId);
        cargoWorld=new CargoNativeWorld(world,worldId,inventoryJournal,worldCheckpoints);var config=cargoWorld.RegisterHub(hub);
        if(Environment.GetCommandLineArgs().Contains("-yfCargoBindingAudit")){VerifyBindingPath(chunk,worldId,owner);VerifyWarehouseSearch(chunk,worldId,owner);VerifySourceSearch(chunk,config);}
        var sourceAt=inventoryCollector.ToWorldPos();var targetAt=inventoryTarget.ToWorldPos();
        // Select real catalogue rows and round-trip their wire payloads. This deliberately
        // does not pretend that an empty QA server can authenticate a player or click XUi.
        inventoryTarget.GetFeature<TEFeatureSignable>().SetText("QA 流程收货箱",true,inventoryTarget.Owner);
        var sourcePosition=SelectTransportFixture(config,true,sourceAt);
        var source=cargoWorld.ResolveBinding(sourcePosition,owner,true);
        config=config.AddSource(owner,config.Revision,source,new CargoRules());cargoWorld.Service.Configure(config.HubId,owner,config.Revision-1,config);
        var selected=CargoSourceSearch.Find(world,config,"",CargoSourceKind.All,0,true);
        Check(selected.Total==1&&selected.Rows[0].Position.Equals(sourcePosition)&&selected.Rows[0].Bound,"transport source chosen from catalogue refreshes as the sole bound producer");
        var targetPosition=SelectTransportFixture(config,false,targetAt);
        var target=cargoWorld.ResolveBinding(targetPosition,owner,false);
        config=config.SetTarget(owner,config.Revision,target,new CargoRules());cargoWorld.Service.Configure(config.HubId,owner,config.Revision-1,config);
        Check(config.Sources[0].Position.Equals(sourcePosition)&&config.Target.Position.Equals(targetPosition),"world scheduler uses exactly the source and target selected from wire catalogue rows");
        lines.Add("AUDIT SCOPE catalogue query/reply and selected action codecs feed real binding/configuration and transport; authenticated Handle success and XUi clicks are NOT exercised.");
        motionTime=Time.realtimeSinceStartup;
    }
    static void VerifyNormalWorldLifecycle()
    {
        CargoRuntime.StartWorld(world);
        Check(CargoRuntime.Failure==null&&CargoNativeWorld.Current!=null,"normal world startup creates the cargo service without harness journals");
        var runtime=CargoNativeWorld.Current;var config=runtime.Service.Status().Single().Configuration;
        Check(world.GetBlock(new Vector3i(config.Position.X,config.Position.Y,config.Position.Z)).Block.GetBlockName()==CargoRuntime.HubBlock,"normal discovery registers the actually placed cargo hub");
        var sourceAt=inventoryCollector.ToWorldPos();var targetAt=inventoryTarget.ToWorldPos();
        config=config.AddSource(config.Owner,config.Revision,runtime.ResolveBinding(new CargoPosition(sourceAt.x,sourceAt.y,sourceAt.z),config.Owner,true),new CargoRules());runtime.Service.Configure(config.HubId,config.Owner,config.Revision-1,config);
        config=config.SetTarget(config.Owner,config.Revision,runtime.ResolveBinding(new CargoPosition(targetAt.x,targetAt.y,targetAt.z),config.Owner,false),new CargoRules());runtime.Service.Configure(config.HubId,config.Owner,config.Revision-1,config);
        config=config.SetPaused(config.Owner,config.Revision,true);runtime.Service.Configure(config.HubId,config.Owner,config.Revision-1,config);
        CargoRuntime.StopWorld();Check(CargoNativeWorld.Current==null,"normal shutdown detaches service and closes files");
        CargoRuntime.StartWorld(world);Check(CargoRuntime.Failure==null,"normal checkpoint reopens after shutdown");
        var restored=CargoNativeWorld.Current.Service.Status().Single().Configuration;
        Check(restored.HubId==config.HubId&&restored.Revision==config.Revision&&restored.Paused&&restored.Sources.Single().Matches(config.Sources.Single())&&restored.Target.Matches(config.Target),"normal startup restores source target pause and exact hub revision without duplicate discovery");
        VerifyPlayerCommands(restored);
        var stack=CargoNativeItems.Decode(inventoryBefore.Items[0]);stack.count=3;inventoryCollector.Items[0]=stack;inventoryCollector.SetChunkModified();
        recoveryHub=restored.SetPaused(restored.Owner,restored.Revision,false);
        CargoNativeWorld.Current.Service.Configure(restored.HubId,restored.Owner,restored.Revision,recoveryHub);
    }
    static void PrepareStartupRecovery()
    {
        CargoRuntime.StopWorld();var chunk=inventoryCollector.GetChunk();var local=new Vector3i(8,170,12);var block=Block.GetBlockValue(CargoRuntime.HubBlock,false);var at=chunk.GetWorldPos()+local;
        Check(chunk.GetTileEntity(local)==null,"startup recovery hub position is unused");chunk.SetBlockRaw(local.x,local.y,local.z,block);
        block.Block.OnBlockAdded(world,chunk,at,block,PlatformUserIdentifierAbs.FromCombinedString(recoveryHub.Owner,false));
        var stack=CargoNativeItems.Decode(inventoryBefore.Items[0]);stack.count=2;inventoryCollector.Items[0]=stack;inventoryCollector.SetChunkModified();
        CargoRuntime.StartWorld(world);var runtime=CargoNativeWorld.Current;var config=runtime.Service.Status().Single().Configuration;
        var source=inventoryCollector.ToWorldPos();var target=inventoryTarget.ToWorldPos();
        config=config.AddSource(config.Owner,config.Revision,runtime.ResolveBinding(new CargoPosition(source.x,source.y,source.z),config.Owner,true),new CargoRules());runtime.Service.Configure(config.HubId,config.Owner,config.Revision-1,config);
        config=config.SetTarget(config.Owner,config.Revision,runtime.ResolveBinding(new CargoPosition(target.x,target.y,target.z),config.Owner,false),new CargoRules());runtime.Service.Configure(config.HubId,config.Owner,config.Revision-1,config);recoveryHub=config;
    }
    static void VerifyPlayerCommands(CargoHubConfiguration config)
    {
        // A native entity/map fixture exercises authorization and player-present
        // inventory guards; it is NOT a connected client or a graphical UI test.
        var actor=PlatformUserIdentifierAbs.FromCombinedString(config.Owner,false);
        var point=new Vector3(config.Position.X+.5f,config.Position.Y+2,config.Position.Z+.5f);
        var fixture=new GameObject("CargoCommandFixture");fixture.SetActive(false);
        var player=fixture.AddComponent<EntityPlayer>();player.entityId=int.MaxValue-10;player.position=point;
        Check(player!=null,"server command fixture uses the native EntityPlayer type without graphics or a network connection");
        var players=GameManager.Instance.GetPersistentPlayerList();int id=player.entityId;
        Check(world.GetEntity(id)==null&&!players.EntityToPlayerMap.ContainsKey(id),"QA actor does not replace an existing player");
        world.Entities.Add(id,player);world.Players.Add(id,player);
        players.EntityToPlayerMap.Add(id,new PersistentPlayerData(actor,actor,new AuthoredText("Cargo QA",actor),(Platform.EPlayGroup)0){EntityId=id});
        try
        {
            var at=new Vector3i(config.Position.X,config.Position.Y,config.Position.Z);
            var request=new NetPackageYFCargoHubRequest{At=at,Hub=config.HubId,Revision=config.Revision,Request=2200,Action=CargoHubAction.Read};
            var reply=request.Evaluate(world,id);
            Check(reply!=null&&reply.Allowed&&reply.Revision==config.Revision&&reply.Details.Contains("采集设备"),"actual command authorization reads fresh state for the nearby hub owner");NetPackageManager.FreePackage(reply);
            request.Action=CargoHubAction.ClearTarget;reply=request.Evaluate(world,id);
            Check(reply.Allowed&&reply.Message.Contains("过快")&&reply.Revision==config.Revision&&CargoNativeWorld.Current.Service.Status().Single().Configuration.Target.Matches(config.Target),"rate-limited action returns retryable authorized state without clearing the target");NetPackageManager.FreePackage(reply);
            player.position=point+new Vector3(30,0,0);reply=request.Evaluate(world,id);
            Check(!reply.Allowed&&reply.Hub==Guid.Empty,"distance failure revokes edit readiness and does not disclose hub state");NetPackageManager.FreePackage(reply);
            player.position=point;
            CargoNativeAccessSessions.Register(inventoryCollector,config.WorldId);
            var endpoint=new CargoNativeValidationEndpoint(world,inventoryCollector,config.WorldId,(world.ChunkCache.ChunkProvider as ChunkProviderGenerateWorld).m_RegionFileManager);
            var tx=Guid.NewGuid();Check(world.Players.Count>0&&endpoint.AcquireFence(tx,endpoint.Snapshot().Revision),"player presence alone no longer blocks a drained native endpoint transaction");
            bool canOpen=true;Check(!CargoNativeValidationEndpoint.BeforeLock(inventoryCollector,ref canOpen)&&!canOpen&&CargoNativeValidationEndpoint.IsFenced(inventoryCollector),"native open hook refuses access during a transaction with a player present");
            endpoint.ReleaseFence(tx);
            var endpointId=CargoNativeMarkers.Read(inventoryCollector,config.WorldId).EndpointId;
            CargoNativeValidationEndpoint.SetStartupQuarantine(config.WorldId,new[]{endpointId});
            Check(CargoNativeValidationEndpoint.IsFenced(inventoryCollector)&&endpoint.Snapshot().Busy&&!endpoint.AcquireFence(Guid.NewGuid(),endpoint.Snapshot().Revision),"unresolved startup endpoint remains quarantined even before an active transfer exists");
            CargoNativeValidationEndpoint.SetStartupQuarantine(config.WorldId,new Guid[0]);
        }
        finally{players.EntityToPlayerMap.Remove(id);world.Players.Remove(id);world.Entities.Remove(id);UnityEngine.Object.Destroy(player.gameObject);}
        lines.Add("AUDIT SCOPE native EntityPlayer and persistent-ID fixtures exercise server commands; connected-client transport and mouse interaction remain untested.");
    }
    static CargoPosition SelectTransportFixture(CargoHubConfiguration config,bool sources,Vector3i expected)
    {
        var at=new Vector3i(config.Position.X,config.Position.Y,config.Position.Z);
        var wanted=new CargoPosition(expected.x,expected.y,expected.z);
        CargoWarehouseRow chosen=null;
        for(int page=0;page<32;page++)
        {
            var query=new NetPackageYFCargoWarehouseRequest{At=at,Hub=config.HubId,Request=1000+page,Page=page,Sources=sources,
                SourceKind=sources?CargoSourceFilter.Kind(world.GetBlock(expected).Block.GetBlockName()):CargoSourceKind.All,
                Kind=CargoWarehouseKind.Crate,Query=sources?"":"QA 流程收货箱"};
            var receivedQuery=new NetPackageYFCargoWarehouseRequest();CopyPacket(query,receivedQuery);
            var result=receivedQuery.Sources?CargoSourceSearch.Find(world,config,receivedQuery.Query,receivedQuery.SourceKind,receivedQuery.Page,receivedQuery.BoundOnly)
                :CargoWarehouseSearch.Find(world,config.Position,receivedQuery.Query,receivedQuery.Kind,receivedQuery.Page);
            var reply=new NetPackageYFCargoWarehouseReply{At=receivedQuery.At,Hub=receivedQuery.Hub,Request=receivedQuery.Request,Sources=receivedQuery.Sources,Success=true,Result=result};
            var receivedReply=new NetPackageYFCargoWarehouseReply();CopyPacket(reply,receivedReply);
            Check(receivedReply.At==at&&receivedReply.Hub==config.HubId&&receivedReply.Request==query.Request&&receivedReply.Sources==sources,"transport catalogue response retains hub request and picker identity");
            chosen=receivedReply.Result.Rows.SingleOrDefault(r=>r.Position.Equals(wanted));
            if(chosen!=null||(page+1)*8>=receivedReply.Result.Total)break;
        }
        Check(chosen!=null&&chosen.Available&&!chosen.Bound,"transport fixture is selectable from the actual "+(sources?"producer":"warehouse")+" catalogue");
        if(chosen==null)throw new InvalidOperationException("Transport fixture absent from catalogue");
        if(!sources)Check(chosen.Sign=="QA 流程收货箱","transport destination is discovered by its authored Chinese sign");
        var action=new NetPackageYFCargoHubRequest{At=at,Hub=config.HubId,Revision=config.Revision,Request=sources?2001:2002,
            Action=sources?CargoHubAction.AddSource:CargoHubAction.SetTarget,Endpoint=new Vector3i(chosen.Position.X,chosen.Position.Y,chosen.Position.Z)};
        var receivedAction=new NetPackageYFCargoHubRequest();CopyPacket(action,receivedAction);
        Check(receivedAction.Action==action.Action&&receivedAction.At==at&&receivedAction.Endpoint==expected&&receivedAction.Hub==config.HubId&&receivedAction.Revision==config.Revision,"selected row produces the exact configuration action endpoint and revision");
        // Negative real-handler probe only: never create a fake player or relax native safety gates.
        int absentActor=sources?int.MaxValue:int.MaxValue-1;
        Check(world.GetEntity(absentActor)==null,"unauthenticated configuration probe has no player entity");
        receivedAction.Handle(world,absentActor);
        var after=cargoWorld.Service.Status().Single(s=>s.Configuration.HubId==config.HubId).Configuration;
        Check(after.Revision==config.Revision&&after.Sources.Length==config.Sources.Length&&after.Target==config.Target,"actual configuration handler rejects an absent actor without mutating the hub");
        return new CargoPosition(receivedAction.Endpoint.x,receivedAction.Endpoint.y,receivedAction.Endpoint.z);
    }
    static void VerifyBindingPath(Chunk chunk,Guid worldId,string owner)
    {
        var actor=PlatformUserIdentifierAbs.FromCombinedString(owner,false);
        string[] names={"AutoMinerIron","AutoMinerLead","AutoMinerCoal","AutoMinerNitrate","AutoMinerClay","AutoMinerShale","AutoMinerBrass","yfAutoForestry"};
        for(int i=0;i<names.Length;i++)
        {
            var local=new Vector3i(i+1,180,8);Check(chunk.GetTileEntity(local)==null,"binding fixture is unused: "+names[i]);
            var block=Block.GetBlockValue(names[i],false);Check(block.Block is BlockCollector,"source uses actual native Collector class: "+names[i]);
            chunk.SetBlockRaw(local.x,local.y,local.z,block);var tile=new TileEntityCollector(chunk){localChunkPos=local};chunk.AddTileEntity(tile);
            var at=tile.ToWorldPos();var position=new CargoPosition(at.x,at.y,at.z);
            Check(cargoWorld.ResolveBinding(position,owner,true)?.Owner=="shared","legacy source receives instance identity without ownership claim: "+names[i]);
            // Invoke the real ownership callback with an explicit synthetic placing actor.
            // This is native hook testing, NOT a player placing a block through the UI.
            CargoCollectorOwnership.Placed(world,chunk,at,block,actor);
            var binding=cargoWorld.ResolveBinding(position,owner,true);
            Check(binding!=null&&binding.Owner==owner&&binding.WorldId==worldId,"placement hook creates resolvable source identity: "+names[i]);
            Check(binding.Matches(cargoWorld.ResolveBinding(position,owner+"-other",true)),"any player resolves the same source instance: "+names[i]);
            var output=tile.GetSlotOutputType(0);Check(output!=null,"native source exposes output definition: "+names[i]);
            var value=ItemClass.GetItem(output.OutputItem);value.SetMetadata("binding-audit",names[i]);tile.Items[0]=new ItemStack(value,2);
            var snapshot=CargoNativeMarkers.Snapshot(tile,CargoNativeMarkers.Read(tile,worldId));
            var plan=CargoPlanner.Load(snapshot,new CargoItem[6],owner,6);
            Check(plan.Moved==2&&plan.CargoAfter.Where(v=>v!=null).All(v=>v.SameValue(CargoNativeItems.Encode(tile.Items[0]))),"native output whitelist preserves complete value: "+names[i]);
            tile.Items[0]=new ItemStack(ItemClass.GetItem("resourceWood"),2);
            Check(CargoPlanner.Load(CargoNativeMarkers.Snapshot(tile,CargoNativeMarkers.Read(tile,worldId)),new CargoItem[6],owner,6).Moved==0,"non-output item cannot be collected: "+names[i]);
            tile.Items[0]=ItemStack.Empty.Clone();
            CargoCollectorOwnership.Removed(world,chunk,at,block);
            Check(CargoNativeMarkers.Read(tile,worldId)==null,"removal clears source placement identity: "+names[i]);
            CargoCollectorOwnership.Placed(world,chunk,at,block,actor);
            Check(!binding.Matches(cargoWorld.ResolveBinding(position,owner,true)),"replacement at same position invalidates old binding: "+names[i]);
        }
        var storageAt=new Vector3i(10,180,8);var storageBlock=Block.GetBlockValue("cntWoodWritableCrate",false);
        Check(chunk.GetTileEntity(storageAt)==null,"natural target binding fixture is unused");chunk.SetBlockRaw(storageAt.x,storageAt.y,storageAt.z,storageBlock);
        var storage=new TileEntityComposite(chunk,storageBlock){localChunkPos=storageAt};chunk.AddTileEntity(storage);storage.SetOwner(actor);
        var storageWorld=storage.ToWorldPos();
        var storagePosition=new CargoPosition(storageWorld.x,storageWorld.y,storageWorld.z);
        Check(cargoWorld.ResolveBinding(storagePosition,owner,false)?.Owner=="shared","legacy storage receives an instance identity without claiming ownership");
        // Exercise the patched native block callbacks, not the cargo postfix directly.
        storageBlock.Block.OnBlockAdded(world,chunk,storageWorld,storageBlock,actor);
        storage=world.GetTileEntity(storageWorld) as TileEntityComposite;
        var storageBinding=cargoWorld.ResolveBinding(storagePosition,owner,false);
        Check(storageBinding!=null,"native storage placement creates a bindable cargo identity without harness marker injection");
        CargoNativeAccessSessions.Register(storage,worldId);
        Check(storageBinding.Matches(cargoWorld.ResolveBinding(storagePosition,owner+"-other",false)),"foreign player binds the same storage instance");
        storage.SetOwner(PlatformUserIdentifierAbs.FromPlatformAndId("Steam","76561198000000003",false));
        Check(storageBinding.Matches(cargoWorld.ResolveBinding(storagePosition,owner,false)),"native ownership change does not invalidate the physical storage instance");
        storage.SetOwner(actor);
        storageBlock.Block.OnBlockRemoved(world,chunk,storageWorld,storageBlock);
        Check(cargoWorld.ResolveBinding(storagePosition,owner,false)==null,"native removal invalidates storage binding");
        storageBlock.Block.OnBlockAdded(world,chunk,storageWorld,storageBlock,actor);
        Check(!storageBinding.Matches(cargoWorld.ResolveBinding(storagePosition,owner,false)),"replaced storage receives a new incarnation");
        var replacementStorage=world.GetTileEntity(storageWorld) as TileEntityComposite;CargoNativeAccessSessions.Register(replacementStorage,worldId);
        Check(CargoNativeAccessSessions.ReadyForCargo(replacementStorage),"replacement storage can register after the old physical instance drained without inheriting its access ticket");
        storageBlock.Block.OnBlockRemoved(world,chunk,storageWorld,storageBlock);
        storageBlock.Block.OnBlockAdded(world,chunk,storageWorld,storageBlock,null);
        Check(cargoWorld.ResolveBinding(storagePosition,owner,false)==null,"non-player storage remains unsupported regardless of unrestricted ownership");
        (world.GetTileEntity(storageWorld) as TileEntityComposite).GetFeature<TEFeatureStorage>().bPlayerStorage=true;
        Check(cargoWorld.ResolveBinding(storagePosition,owner,false)!=null&&!storageBinding.Matches(cargoWorld.ResolveBinding(storagePosition,owner,false)),"unowned replacement is bindable but cannot inherit old instance identity");
        Check(!CargoRules.IsSource("yfAutoMiner"),"AUDIT SCOPE newer yfAutoMiner is not one of the seven supported ProjectZ collectors");
        lines.Add("AUDIT SCOPE collector callbacks and native storage placement exercised; UI clicks, connected clients and real power remain untested.");
    }
    static void VerifyWarehouseSearch(Chunk chunk,Guid worldId,string owner)
    {
        var actor=PlatformUserIdentifierAbs.FromCombinedString(owner,false);
        var tiles=new System.Collections.Generic.List<TileEntityComposite>();
        for(int i=0;i<10;i++)
        {
            var local=new Vector3i(i+1,200,10);var value=Block.GetBlockValue(i==9?"cntCupboardCabinetOldTopClosed":"cntWoodWritableCrate",false);
            Check(chunk.GetTileEntity(local)==null,"warehouse search fixture position is unused");
            chunk.SetBlockRaw(local.x,local.y,local.z,value);var tile=new TileEntityComposite(chunk,value){localChunkPos=local};tile.SetOwner(actor);chunk.AddTileEntity(tile);tiles.Add(tile);
            if(i<9)tile.GetFeature<TEFeatureSignable>().SetText("QA 仓库 铁矿 "+i,true,actor);
        }
        var wp=chunk.GetWorldPos();var hub=new CargoPosition(wp.x,200,wp.z);
        var first=CargoWarehouseSearch.Find(world,hub,"QA 铁矿",CargoWarehouseKind.Crate,0);
        var second=CargoWarehouseSearch.Find(world,hub,"QA 铁矿",CargoWarehouseKind.Crate,1);
        Check(first.Total==9&&first.Rows.Length==8&&second.Rows.Length==1,"native warehouse search filters authored Chinese signs and paginates results");
        Check(!first.Rows.Any(a=>second.Rows.Any(b=>a.Position.Equals(b.Position))),"native warehouse pages do not duplicate entries");
        Check(first.Rows.Zip(first.Rows.Skip(1),(a,b)=>a.Distance<=b.Distance).All(b=>b),"warehouse search is ordered by distance");
        Check(CargoWarehouseSearch.Find(world,hub,"QA 铁矿",CargoWarehouseKind.Cabinet,0).Total==0,"native warehouse type and text filters combine");
        var cabinet=tiles[9];var cp=cabinet.ToWorldPos();
        var cabinets=CargoWarehouseSearch.Find(world,hub,"",CargoWarehouseKind.Cabinet,0);
        Check(cabinets.Rows.Any(r=>r.Position.Equals(new CargoPosition(cp.x,cp.y,cp.z))&&r.Sign==""&&r.Name.Length>0),"unsigned native cupboard appears with localized name and coordinates");
        Check(CargoWarehouseSearch.Find(world,hub,cabinet.block.GetLocalizedBlockName(),CargoWarehouseKind.Cabinet,0).Rows.Any(r=>r.Position.Equals(new CargoPosition(cp.x,cp.y,cp.z))),"unsigned cupboard can be searched by its localized name");
        Check(tiles.All(t=>CargoNativeMarkers.Read(t,worldId)==null),"warehouse discovery does not create endpoint identities or claim containers");
        Check(cargoWorld.ResolveBinding(new CargoPosition(cp.x,cp.y,cp.z),owner,false)!=null,"listed native cupboard is accepted by the actual target binding path");
        bool invalid=false;try{CargoWarehouseSearch.Find(world,hub,"",(CargoWarehouseKind)255,0);}catch(ArgumentException){invalid=true;}Check(invalid,"native warehouse query rejects invalid type");
    }
    static void VerifySourceSearch(Chunk chunk,CargoHubConfiguration config)
    {
        var all=CargoSourceSearch.Find(world,config,"",CargoSourceKind.All,0);
        Check(all.Total>=8&&all.Rows.All(r=>CargoRules.IsSource(r.Block)&&r.Distance<=64),"source picker lists supported miners and forestry within 64 blocks");
        var iron=CargoSourceSearch.Find(world,config,"铁矿",CargoSourceKind.Iron,0);
        Check(iron.Rows.Length>0&&iron.Rows.All(r=>r.Block=="AutoMinerIron"),"native miner keyword and resource type filters combine");
        var forest=CargoSourceSearch.Find(world,config,"",CargoSourceKind.Forestry,0);
        Check(forest.Rows.Length>0&&forest.Rows.All(r=>r.Block=="yfAutoForestry"),"native picker offers forestry without coordinate input");
        var row=iron.Rows[0];var binding=cargoWorld.ResolveBinding(row.Position,config.Owner,true);
        var configured=config.AddSource(config.Owner,config.Revision,binding,new CargoRules());
        var bound=CargoSourceSearch.Find(world,configured,"",CargoSourceKind.All,0,true);
        Check(bound.Total==1&&bound.Rows[0].Bound&&bound.Rows[0].Available,"bound-only picker shows selected miner state");
        var marker=new CargoBinding(config.WorldId,Guid.NewGuid(),Guid.NewGuid(),new CargoPosition(config.Position.X,config.Position.Y+1,config.Position.Z),"shared","AutoMinerIron");
        configured=configured.AddSource(config.Owner,configured.Revision,marker,new CargoRules());
        bound=CargoSourceSearch.Find(world,configured,"",CargoSourceKind.All,0,true);
        var missing=bound.Rows.Single(r=>r.Position.Equals(marker.Position));
        Check(missing.Bound&&!missing.Available,"missing miner remains selectable for removing its binding");
        configured=configured.RemoveSource(config.Owner,configured.Revision,marker.EndpointId);
        Check(configured.Sources.Length==1,"missing miner binding can be removed through selected record");
        var block=Block.GetBlockValue("AutoMinerIron",false);var actor=PlatformUserIdentifierAbs.FromCombinedString(config.Owner,false);
        for(int delta=64;delta<=65;delta++)
        {
            var point=new Vector3i(config.Position.X,config.Position.Y+delta,config.Position.Z);var local=World.toBlock(point);
            Check(chunk.GetTileEntity(local)==null,"source radius fixture position is unused");chunk.SetBlockRaw(local.x,local.y,local.z,block);
            block.Block.OnBlockAdded(world,chunk,point,block,actor);
        }
        iron=CargoSourceSearch.Find(world,config,"",CargoSourceKind.Iron,0);
        Check(iron.Rows.Any(r=>r.Position.Y==config.Position.Y+64)&&!iron.Rows.Any(r=>r.Position.Y==config.Position.Y+65),"native source picker enforces inclusive 3D 64-block collection radius");
        Check(CargoSourceSearch.Find(world,config,"",CargoSourceKind.All,1).Rows.Length>0,"source picker paginates more than eight discovered producers");
    }
    static void StartInventoryUnload(CargoTransaction load)
    {
        var chunk=inventoryCollector.GetChunk();var p=new Vector3i(Environment.GetCommandLineArgs().Contains("-yfCargoWorldScheduler")?13:5,150,1);
        Check(chunk.GetTileEntity(p)==null,"native target fixture is empty");
        var block=Block.GetBlockValue("cntWoodWritableCrate",false);chunk.SetBlockRaw(p.x,p.y,p.z,block);
        var targetPosition=chunk.GetWorldPos()+p;
        var targetPlacer=PlatformUserIdentifierAbs.FromPlatformAndId("Steam","76561198000000001",false);
        block.Block.OnBlockAdded(world,chunk,targetPosition,block,targetPlacer);
        inventoryTarget=world.GetTileEntity(targetPosition) as TileEntityComposite;
        var storage=inventoryTarget.GetFeature<TEFeatureStorage>();Check(storage!=null&&storage.bPlayerStorage,"native target has player storage feature");
        var marker=CargoNativeMarkers.Read(inventoryTarget,load.WorldId);
        Check(marker!=null&&marker.Owner==targetPlacer.CombinedString,"scheduled delivery target identity comes from native placement hook");
        CargoNativeAccessSessions.Register(inventoryTarget,load.WorldId);
        var provider=world.ChunkCache.ChunkProvider as ChunkProviderGenerateWorld;
        targetEndpoint=new CargoNativeValidationEndpoint(world,inventoryTarget,load.WorldId,provider.m_RegionFileManager);
        var plan=CargoPlanner.Unload(targetEndpoint.Snapshot(),load.Plan.CargoAfter,load.Plan.Owner);
        Check(plan.Moved==1,"native target accepts the package loaded from source");
        var tx=new CargoTransaction(Guid.NewGuid(),load.FlightId,load.WorldId,1,plan);
        inventoryTransfer=new CargoTransferCoordinator(inventoryJournal,targetEndpoint,tx);inventoryTransfer.Begin();
        Check(inventoryTransfer.State==CargoTransferState.Saving&&CargoNativeValidationEndpoint.IsFenced(inventoryTarget),"real target inventory remains fenced until native unload save completes");
        var offered=CargoNativeItems.Decode(load.Plan.CargoAfter.First(i=>i!=null));int count=offered.count;
        Check(!storage.AddItem(offered)&&offered.count==count,"fenced storage AddItem refuses without consuming offered stack");
        Check(storage.RemoveItems(offered.itemValue,1)==0,"fenced storage quantity removal reports zero removed");
        var stacked=storage.TryStackItem(0,offered);
        Check(!stacked.Item1&&!stacked.Item2&&offered.count==count,"fenced storage stacking preserves input and reports no change");
        RejectNativeWrite(()=>storage.UpdateSlot(0,ItemStack.Empty),"fenced storage UpdateSlot explicitly rejects mutation");
        RejectNativeWrite(()=>storage.SetEmpty(),"fenced storage cannot be emptied");
        RejectNativeWrite(()=>storage.SlotLocks=new PackedBoolArray(storage.items.Length),"fenced storage rejects lock metadata replacement while saving");
        Check(!storage.CanLockOnServer(0,null,0),"fenced composite storage rejects actual server feature lock request");
        Check(YFAutomation.Production.Step(inventoryTarget,inventoryTarget,inventoryTarget,null)=="库存正在使用，生产暂停","production entry refuses a fenced participant before planning or progress");
        Check(YFAutomation.WaterSystem.Pump(world,inventoryTarget)=="库存正在使用，抽水暂停","water pump entry refuses a fenced internal inventory before planning");
        Check(CargoPlanner.Equal(targetEndpoint.Snapshot().Items,plan.EndpointAfter),"all refused storage writers preserve exact prepared after-image");
    }
    static void RejectNativeWrite(Action action,string message)
    {bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}Check(rejected,message);}
    static void VerifyAccessPackets()
    {
        var visual=new NetPackageYFCargoVisual{Epoch=Guid.NewGuid(),Sequence=4,States=new[]{new CargoVisualState{Hub=Guid.NewGuid(),Position=new CargoPoint(-4,155,9),Phase=CargoPhase.ToSource,Hold=CargoHold.None,Packages=2,RestPose=true}}};
        var visualCopy=new NetPackageYFCargoVisual();CopyPacket(visual,visualCopy);
        Check(visualCopy.Epoch==visual.Epoch&&visualCopy.Sequence==4&&visualCopy.States.Single().Hub==visual.States[0].Hub&&visualCopy.States[0].RestPose&&visualCopy.States[0].Packages==2,"visual snapshot codec preserves world epoch sequence pose and cargo");
        var request=new NetPackageYFCargoHubRequest{At=new Vector3i(1,2,3),Endpoint=new Vector3i(-100,150,400),Request=71,Action=CargoHubAction.AddSource,Hub=Guid.NewGuid(),Revision=long.MaxValue};var requestCopy=new NetPackageYFCargoHubRequest();CopyPacket(request,requestCopy);
        Check(requestCopy.Hub==request.Hub&&requestCopy.Revision==long.MaxValue&&requestCopy.Endpoint==request.Endpoint&&requestCopy.Action==CargoHubAction.AddSource,"hub request preserves incarnation token revision and coordinates");
        var reply=new NetPackageYFCargoHubReply{At=request.At,Request=71,Allowed=true,Hub=request.Hub,Revision=99,Paused=true,Message="已保存",Details="采集范围 64；搬运范围 1000"};var replyCopy=new NetPackageYFCargoHubReply();CopyPacket(reply,replyCopy);
        Check(replyCopy.Details==reply.Details&&replyCopy.Paused&&replyCopy.Revision==99,"hub reply preserves bounded Chinese UI state");
        var search=new NetPackageYFCargoWarehouseRequest{At=request.At,Hub=request.Hub,Request=72,Page=1,Kind=CargoWarehouseKind.Cabinet,Query="铁矿 壁橱"};var searchCopy=new NetPackageYFCargoWarehouseRequest();CopyPacket(search,searchCopy);
        Check(searchCopy.Query==search.Query&&searchCopy.Kind==search.Kind&&searchCopy.Page==1&&searchCopy.Hub==search.Hub,"warehouse request preserves query type page and hub identity");
        search.Sources=true;search.SourceKind=CargoSourceKind.Forestry;search.BoundOnly=true;CopyPacket(search,searchCopy);
        Check(searchCopy.Sources&&searchCopy.SourceKind==CargoSourceKind.Forestry&&searchCopy.BoundOnly,"source request preserves picker mode resource type and bound-only filter");
        var result=new NetPackageYFCargoWarehouseReply{At=request.At,Hub=request.Hub,Request=72,Success=true,Message="选择仓库",Result=new CargoWarehousePage{Total=1,Rows=new[]{new CargoWarehouseRow{Position=new CargoPosition(4,5,6),Name="旧式壁橱",Sign="",Block="cntCupboardCabinetOldTopClosed",Distance=42.5}}}};
        var resultCopy=new NetPackageYFCargoWarehouseReply();CopyPacket(result,resultCopy);
        Check(resultCopy.Result.Rows.Length==1&&resultCopy.Result.Rows[0].Name=="旧式壁橱"&&resultCopy.Result.Rows[0].Sign==""&&resultCopy.Result.Rows[0].Distance==42.5,"warehouse reply preserves unsigned cabinet names and coordinates");
        result.Sources=true;result.Result.Rows[0].Block="AutoMinerIron";result.Result.Rows[0].Bound=true;result.Result.Rows[0].Available=false;CopyPacket(result,resultCopy);
        Check(resultCopy.Sources&&resultCopy.Result.Rows[0].Bound&&!resultCopy.Result.Rows[0].Available,"source reply preserves unavailable bound record for list removal");
        var token=Guid.NewGuid();var at=new Vector3i(-20,155,19);
        var source=new NetPackageCargoAccessData{At=at,Token=token,Sequence=21,Payload=Enumerable.Range(0,32000).Select(i=>(byte)i).ToArray()};
        var copy=new NetPackageCargoAccessData();CopyPacket(source,copy);
        Check(copy.At==at&&copy.Token==token&&copy.Sequence==21&&copy.Payload.SequenceEqual(source.Payload),"native inventory envelope codec preserves session sequence and full payload");
        var close=new NetPackageCargoAccessClose{At=at,Token=token,Sequence=21};var closed=new NetPackageCargoAccessClose();CopyPacket(close,closed);
        Check(closed.At==at&&closed.Token==token&&closed.Sequence==21,"native close codec carries final inventory sequence");
        var start=new NetPackageCargoAccessStart{At=at,Token=token};var started=new NetPackageCargoAccessStart();CopyPacket(start,started);
        Check(started.At==at&&started.Token==token,"native session ticket codec preserves endpoint and server nonce");
        CopyPacket(new NetPackageCargoAccessHello(),new NetPackageCargoAccessHello());
    }
    static void CopyPacket(NetPackage source,NetPackage destination)
    {
        using(var memory=new MemoryStream())
        {
            using(var writer=MemoryPools.poolBinaryWriter.AllocSync(false)){writer.SetBaseStream(memory);source.write(writer);writer.Flush();}
            Check(memory.Length==source.GetLength(),"native cargo protocol packet length matches serialized bytes");memory.Position=0;
            using(var reader=MemoryPools.poolBinaryReader.AllocSync(false)){reader.SetBaseStream(memory);Check(reader.ReadUInt16()==destination.PackageId,"native cargo protocol packet id registered");destination.read(reader);Check(memory.Position==memory.Length,"native cargo protocol decoder consumes entire packet");}
        }
    }
    static void VerifyCargoModels()
    {
        var drone=CargoDroneModel.Drone();var hub=CargoDroneModel.Hub();GameObject pair=null;
        try
        {
            var rig=drone.GetComponent<CargoBusterRig>();
            Check(rig!=null&&rig.RotorCount==2,"Buster model exposes its two actual turbine blade pivots");
            for(int yaw=0;yaw<360;yaw+=15)
            {
                drone.transform.rotation=Quaternion.Euler(0,yaw,0);
                bool inside=true;
                for(int step=0;step<=20;step++)for(int rotor=0;rotor<360;rotor+=30)
                {
                    rig.Sample(step/20f,rotor);
                    var bounds=new Bounds(Vector3.zero,Vector3.zero);foreach(var renderer in drone.GetComponentsInChildren<Renderer>())bounds.Encapsulate(renderer.bounds);
                    inside&=bounds.min.x>=-.8f&&bounds.max.x<=.8f&&bounds.min.z>=-.8f&&bounds.max.z<=.8f&&bounds.min.y>=-.6f&&bounds.max.y<=.6f;
                }
                Check(inside,"Buster mechanical poses and spinning blades fit swept collision body at yaw "+yaw);
            }
            drone.transform.rotation=Quaternion.identity;
            Check(drone.GetComponentsInChildren<Collider>().All(c=>!c.enabled)&&hub.GetComponentsInChildren<Collider>().All(c=>!c.enabled),"cosmetic model does not introduce client-authoritative physics colliders");
            Check(drone.GetComponentsInChildren<MeshRenderer>().Length==38&&!drone.GetComponentsInChildren<Transform>().Any(t=>t.name=="Env")&&hub.transform.Find("LandingAnchor")!=null,"Buster retains 38 original meshes, removes display scenery and keeps hub landing anchor");
            Check(drone.GetComponentsInChildren<Renderer>().All(r=>r.sharedMaterial.shader.name=="Standard"&&r.sharedMaterial.mainTexture!=null&&r.sharedMaterial.GetTexture("_BumpMap")!=null),"Buster native PBR materials retain base and normal textures");
            var second=CargoDroneModel.Drone();
            try{Check(second.GetComponentInChildren<MeshFilter>().sharedMesh==drone.GetComponentInChildren<MeshFilter>().sharedMesh&&second.GetComponentInChildren<Renderer>().sharedMaterial==drone.GetComponentInChildren<Renderer>().sharedMaterial,"Buster instances share mesh and texture resources");}
            finally{second.SetActive(false);UnityEngine.Object.Destroy(second);}
            rig.SetFlight(true,6);for(int i=0;i<20;i++)rig.Advance(.1f);
            Check(rig.CargoCount==6&&drone.transform.position==Vector3.zero,"mechanical flight animation preserves authoritative root and six-slot cargo status");
            hub.SetActive(false);rig.Sample(1,45);
            Check(drone.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name=="Drone_Turb_Blade_L_body_0"||r.name=="Drone_Turb_Blade_R_body_0").All(r=>r.bounds.size.y<.14f),"flight turbine planes remain horizontal after removing demonstration body banking");
            RenderModel(drone,Path.Combine(GameIO.GetSaveGameDir(),"cargo-drone-preview.png"),new Vector3(1.65f,1.05f,2.1f));
            rig.Sample(0,0);
            Check(rig.RestBounds.min.y>=CargoDock.RestCenter-CargoDock.RestHalfHeight&&rig.RestBounds.max.y<=CargoDock.RestCenter+CargoDock.RestHalfHeight,"actual landed native vertices fit the asymmetric dock collision body");
            var deckBox=new CargoBox(new CargoPoint(-.97,0,-.97),new CargoPoint(.97,1,.97));var dockPoint=new CargoPoint(0,CargoHubModel.LandingHeight,0);var upPoint=new CargoPoint(0,CargoHubModel.LandingHeight+3,0);
            Check(!CargoDock.Hit(deckBox,dockPoint,upPoint,dockPoint)&&!CargoDock.Hit(deckBox,upPoint,dockPoint,dockPoint),"solid deck permits continuous takeoff and landing across the pose boundary");
            Check(CargoDock.Hit(deckBox,dockPoint,new CargoPoint(0,.9,0),dockPoint),"dock collision still prevents passing down through its own solid pad");
            var roof=new CargoBox(new CargoPoint(-1,1.7,-1),new CargoPoint(1,1.8,1));Check(CargoDock.Hit(roof,dockPoint,upPoint,dockPoint),"rest-pose sweep still blocks a low ceiling");
            RenderModel(drone,Path.Combine(GameIO.GetSaveGameDir(),"cargo-drone-landed.png"),new Vector3(1.65f,1.05f,2.1f));
            drone.SetActive(false);hub.SetActive(true);RenderModel(hub,Path.Combine(GameIO.GetSaveGameDir(),"cargo-hub-preview.png"),new Vector3(2.4f,2.3f,-3.1f));
            var surfaces=hub.GetComponentsInChildren<MeshRenderer>();
            Check(surfaces.Length==6,"industrial hub batches armor bolts vents and lights into six material surfaces");
            Check(surfaces.All(r=>r.bounds.min.x>=-1&&r.bounds.max.x<=1&&r.bounds.min.z>=-1&&r.bounds.max.z<=1&&r.bounds.min.y>=-.001f&&r.bounds.max.y<=1),"industrial hub fits its two by two footprint and one-block body height");
            var anchor=hub.transform.Find("LandingAnchor");
            Check(Math.Abs(anchor.localPosition.y-CargoHubModel.LandingHeight)<1e-5,"visible hub landing anchor matches authoritative home height");
            drone.SetActive(true);drone.transform.position=anchor.position;
            float lowest=drone.GetComponentsInChildren<Renderer>().Min(r=>r.bounds.min.y);
            Check(Math.Abs(lowest-CargoHubModel.DeckHeight)<.065f,"landed Buster feet meet deck instead of hovering above the old pad");
            pair=new GameObject("BusterDockPreview");hub.transform.SetParent(pair.transform,true);drone.transform.SetParent(pair.transform,true);pair.transform.position=new Vector3(0,-.75f,0);
            RenderModel(pair,Path.Combine(GameIO.GetSaveGameDir(),"cargo-dock-paired.png"),new Vector3(2.8f,2.2f,3.5f));
        }
        finally{UnityEngine.Object.Destroy(drone);UnityEngine.Object.Destroy(hub);if(pair!=null)UnityEngine.Object.Destroy(pair);}
    }
    static void RenderModel(GameObject model,string path,Vector3 cameraPosition)
    {
        foreach(var child in model.GetComponentsInChildren<Transform>(true))child.gameObject.layer=31;
        var cameraObject=new GameObject("CargoPreviewCamera");var lightObject=new GameObject("CargoPreviewLight");var fillObject=new GameObject("CargoPreviewFill");var render=new RenderTexture(960,720,24);Texture2D pixels=null;var previous=RenderTexture.active;
        var previousAmbient=RenderSettings.ambientMode;var previousAmbientLight=RenderSettings.ambientLight;bool previousFog=RenderSettings.fog;
        try
        {
            var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.05f,.075f);camera.nearClipPlane=.05f;camera.farClipPlane=20;camera.fieldOfView=35;camera.transform.position=cameraPosition;camera.transform.LookAt(Vector3.zero);camera.targetTexture=render;
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.35f,.35f,.35f);RenderSettings.fog=false;
            var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.cullingMask=1<<31;light.intensity=.9f;light.transform.rotation=Quaternion.Euler(45,-145,0);
            var fill=fillObject.AddComponent<Light>();fill.type=LightType.Directional;fill.cullingMask=1<<31;fill.intensity=.45f;fill.transform.rotation=Quaternion.Euler(25,45,0);
            camera.Render();RenderTexture.active=render;pixels=new Texture2D(960,720,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,960,720),0,0);pixels.Apply();File.WriteAllBytes(path,pixels.EncodeToPNG());
            Check(new FileInfo(path).Length>1000,"native model preview rendered to PNG");
        }
        finally{RenderSettings.ambientMode=previousAmbient;RenderSettings.ambientLight=previousAmbientLight;RenderSettings.fog=previousFog;RenderTexture.active=previous;render.Release();UnityEngine.Object.Destroy(render);if(pixels!=null)UnityEngine.Object.Destroy(pixels);UnityEngine.Object.Destroy(cameraObject);UnityEngine.Object.Destroy(lightObject);UnityEngine.Object.Destroy(fillObject);}
    }
    static void StartPreparedRecoveryFixture(Guid worldId,Guid flightId)
    {
        inventoryJournal=new CargoFileJournal(Path.Combine(GameIO.GetSaveGameDir(),"cargo-native-inventory.wal"),worldId);
        var snapshot=inventoryEndpoint.Snapshot();var plan=CargoPlanner.Load(snapshot,new CargoItem[6],snapshot.Owner,1);
        Check(plan.Moved==1,"native prepared-recovery fixture has another source package");
        var tx=new CargoTransaction(Guid.NewGuid(),flightId,worldId,2,plan);
        inventoryTransfer=new CargoTransferCoordinator(inventoryJournal,inventoryEndpoint,tx);inventoryTransfer.Begin();
        var chunk=inventoryCollector.GetChunk();bool networking=chunk.InProgressNetworking;
        try
        {
            chunk.InProgressNetworking=true;
            Check(inventoryEndpoint.PollDurableSave(tx.Id)==CargoSaveResult.Pending&&CargoNativeValidationEndpoint.IsFenced(inventoryCollector),"native chunk lock defers requested snapshot while retaining inventory fence");
        }
        finally{chunk.InProgressNetworking=networking;}
    }
}
