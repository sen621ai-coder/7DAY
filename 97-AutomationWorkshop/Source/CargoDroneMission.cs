using System;
using System.Collections.Generic;
using System.Linq;

namespace YFAutomation.CargoDrones
{
    public enum CargoMissionReturnReason{None,Requested,EnergyLow,ContainerBusy,SourceEmpty,TargetFull,TransactionAborted}
    // A cargo sortie with explicit checkpointed docking and old-cargo redelivery.
    // Endpoint discovery and preflight admission belong to the world service.
    // No native inventory adapter is assumed or enabled by this coordinator.
    public sealed class CargoMission
    {
        readonly Guid world,source,target;
        readonly string owner;
        readonly long reserve;
        readonly CargoPoint destination;
        readonly CargoFlight flight;
        readonly CargoMotion motion;
        readonly CargoEnergy energy;
        CargoItem[] cargo=new CargoItem[CargoRules.Slots];
        CargoTransferCoordinator transfer;
        bool failed;
        bool retired;
        bool busyObserved;
        public long BusyWaitUnits{get;private set;}
        public CargoMissionReturnReason ReturnReason{get;private set;}
        public CargoPhase Phase{get{return flight.Phase;}}
        public CargoHold Hold{get{return flight.Hold;}}
        public CargoPoint Position{get{return motion.Position;}}
        public long Battery{get{return flight.Battery;}}
        public long CargoRevision{get;private set;}
        public CargoItem[] Cargo{get{return (CargoItem[])cargo.Clone();}}
        public Guid Id{get{return flight.Id;}}
        public Guid SourceId{get{return source;}}
        public Guid TargetId{get{return target;}}
        public bool CheckpointReady{get{return !retired&&!failed&&transfer==null&&!flight.TransferPending;}}
        public bool TransferReady{get{return !failed&&!flight.TransferPending&&flight.Hold==CargoHold.None&&!flight.RecallRequested&&flight.HandlingUnits>=2000&&(Phase==CargoPhase.Loading||Phase==CargoPhase.Unloading);}}
        public CargoMission(Guid world,Guid id,Guid source,Guid target,string owner,ICargoAirspace airspace,CargoPoint home,CargoPoint pickup,CargoPoint destination,long battery,long reserve=180000)
        {
            if(world==Guid.Empty||source==Guid.Empty||target==Guid.Empty||source==target||string.IsNullOrEmpty(owner)||reserve<0)throw new ArgumentException("Invalid mission");
            this.world=world;this.source=source;this.target=target;this.owner=owner;this.reserve=reserve;this.destination=destination;
            energy=new CargoEnergy(battery);flight=new CargoFlight(id,target,energy);
            motion=new CargoMotion(airspace,home,pickup,energy,returnReserve:reserve);flight.Depart(false);
        }
        public void Recall()
        {RequestReturn(CargoMissionReturnReason.Requested);}
        public bool FreezeForRecovery()
        {
            if(retired||failed||transfer!=null||flight.TransferPending)return false;
            flight.Restore(CargoPhase.RecoveryOnly,CargoHold.RecoveryRequired,true,0);return true;
        }
        public void ChargeDocked(long elapsed,bool powered,bool ownerOnline=true,bool paused=false)
        {
            if(elapsed<0)throw new ArgumentOutOfRangeException("elapsed");
            if(retired)throw new InvalidOperationException("Shipment was handed to a new sortie");
            if(Phase!=CargoPhase.Docked||!powered||!ownerOnline||paused)return;
            energy.Charge(Math.Min(elapsed,100)*10,600000);
        }
        public CargoMission RetryDelivery(ICargoAirspace airspace)
        {
            if(retired||Phase!=CargoPhase.Docked||failed||CargoPlanner.Count(cargo)==0||Battery<reserve)throw new InvalidOperationException("Docked committed cargo required for redelivery");
            // Retain the shipment's WAL identity and immutable target until all
            // goods are delivered. This sortie cannot load any new source goods.
            var next=new CargoMission(world,flight.Id,source,target,owner,airspace,Position,Position,destination,Battery,reserve);
            next.cargo=Cargo;next.CargoRevision=CargoRevision;next.flight.Restore(CargoPhase.ToTarget,CargoHold.None,false,0);next.motion.Retarget(destination);retired=true;return next;
        }
        internal void AcceptRecovery(CargoRecoveredFlight recovered,CargoFileJournal journal)
        {
            if(Phase!=CargoPhase.RecoveryOnly||transfer!=null||recovered==null||recovered.FlightId!=flight.Id||recovered.Owner!=owner||recovered.Pending!=null||recovered.Revision<CargoRevision||CargoPlanner.Count(recovered.Cargo)!=0)throw new InvalidOperationException("Invalid recovery handoff");
            var actual=CargoJournalReplay.Read(world,journal.Entries).Single(f=>f.FlightId==flight.Id);
            if(actual.Pending!=null||actual.Revision!=recovered.Revision||!CargoPlanner.Equal(actual.Cargo,recovered.Cargo))throw new InvalidOperationException("Recovery result is not current WAL state");
            cargo=recovered.Cargo;CargoRevision=recovered.Revision;
        }
        public CargoMissionState Capture()
        {
            if(retired||failed||transfer!=null||flight.TransferPending)throw new InvalidOperationException("Resolve prepared transfers and use the active sortie before checkpointing");
            return new CargoMissionState(world,flight.Id,source,target,owner,destination,reserve,Phase,Hold,flight.RecallRequested,flight.HandlingUnits,BusyWaitUnits,busyObserved,ReturnReason,Battery,CargoRevision,cargo,motion.Capture());
        }
        public void CompleteDock(CargoCheckpointStore store,CargoFileJournal journal,IEnumerable<CargoMissionState> otherFlights)
        {
            if(Phase!=CargoPhase.Docking||failed||transfer!=null||store==null||otherFlights==null)throw new InvalidOperationException("Mission is not ready to checkpoint docking");
            var saved=Capture();
            var docked=new CargoMissionState(saved.World,saved.Id,saved.Source,saved.Target,saved.Owner,saved.Destination,saved.Reserve,CargoPhase.Docked,CargoHold.None,saved.Recall,0,0,false,saved.ReturnReason,saved.Battery,saved.Revision,saved.Cargo,saved.Motion);
            store.Save(otherFlights.Concat(new[]{docked}),journal);
            // Docking becomes observable only after the new manifest was durably
            // written and read back. Any publication exception leaves us Docking.
            flight.SetHold(CargoHold.None);flight.Dock();BusyWaitUnits=0;busyObserved=false;
        }
        internal CargoMissionState CaptureDocked()
        {
            if(Phase!=CargoPhase.Docking)throw new InvalidOperationException("Docking required");
            var s=Capture();
            return new CargoMissionState(s.World,s.Id,s.Source,s.Target,s.Owner,s.Destination,s.Reserve,CargoPhase.Docked,CargoHold.None,s.Recall,0,0,false,s.ReturnReason,s.Battery,s.Revision,s.Cargo,s.Motion);
        }
        internal void ConfirmDocked()
        {if(Phase!=CargoPhase.Docking||!CheckpointReady)throw new InvalidOperationException("Docking required");flight.SetHold(CargoHold.None);flight.Dock();BusyWaitUnits=0;busyObserved=false;}
        public static CargoMission Restore(CargoMissionState state,ICargoAirspace airspace,CargoFileJournal journal)
        {state.ValidateJournal(journal);return new CargoMission(state,airspace);}
        public static CargoMissionState Reconcile(CargoMissionState saved,CargoFileJournal journal,ICargoAirspace airspace,bool removed)
        {
            var committed=CargoJournalReplay.Read(journal.WorldId,journal.Entries).SingleOrDefault(f=>f.FlightId==saved.Id);
            if(committed==null){saved.ValidateJournal(journal);return saved;}
            if(committed.Pending!=null||committed.Owner!=saved.Owner||committed.Revision<saved.Revision)throw new InvalidOperationException("Unresolved or divergent recovery cargo");
            if(committed.Revision==saved.Revision){saved.ValidateJournal(journal);return saved;}
            // Cargo comes exclusively from resolved WAL. Navigation comes from
            // the proven checkpoint, never from an invented straight return route.
            // Conservatively charge the maximum unpublished five-second budget.
            var frozen=new CargoMissionState(saved.World,saved.Id,saved.Source,saved.Target,saved.Owner,saved.Destination,saved.Reserve,CargoPhase.RecoveryOnly,CargoHold.RecoveryRequired,true,0,0,false,CargoMissionReturnReason.Requested,Math.Max(0,saved.Battery-5000),committed.Revision,committed.Cargo,saved.Motion);
            var mission=Restore(frozen,airspace,journal);
            if(!removed){mission.motion.ReturnHome();mission.flight.Restore(CargoPhase.Returning,CargoHold.None,true,0);}
            return mission.Capture();
        }
        CargoMission(CargoMissionState state,ICargoAirspace airspace)
        {
            world=state.World;source=state.Source;target=state.Target;owner=state.Owner;destination=state.Destination;reserve=state.Reserve;
            energy=new CargoEnergy(state.Battery);flight=new CargoFlight(state.Id,target,energy);flight.Restore(state.Phase,state.Hold,state.Recall,state.Handling);
            motion=new CargoMotion(airspace,energy,state.Motion);cargo=state.Cargo;CargoRevision=state.Revision;BusyWaitUnits=state.BusyWait;busyObserved=state.BusyObserved;ReturnReason=state.ReturnReason;
        }
        void RequestReturn(CargoMissionReturnReason reason)
        {if(Phase==CargoPhase.RecoveryOnly||Phase==CargoPhase.Docked)return;if(ReturnReason==CargoMissionReturnReason.None)ReturnReason=reason;flight.Recall();if(!flight.TransferPending&&!failed)motion.ReturnHome();}
        bool CanSpend(long units)
        {return Battery>=reserve&&Battery-reserve>=units&&motion.EstimatedReturnUnits<=Battery-reserve-units;}
        public void RetryPath(){if(!failed&&!flight.TransferPending)motion.RetryPath();}
        public void Tick(long elapsed,bool ownerOnline=true,bool paused=false,CargoHold endpointHold=CargoHold.None)
        {
            if(elapsed<0)throw new ArgumentOutOfRangeException("elapsed");
            if(retired)throw new InvalidOperationException("Shipment was handed to a new sortie");
            if(endpointHold!=CargoHold.None&&endpointHold!=CargoHold.ContainerBusy&&endpointHold!=CargoHold.ChunkLoading&&endpointHold!=CargoHold.ChunkBudget)throw new ArgumentException("Invalid endpoint hold");
            if(failed){flight.SetHold(CargoHold.RecoveryRequired);return;}
            if(Phase==CargoPhase.RecoveryOnly){flight.SetHold(CargoHold.RecoveryRequired);return;}
            // A prepared transaction resolves even if the owner disconnected or
            // requested recall. Cargo remains the last committed image until then.
            if(transfer!=null)
            {
                try{transfer.Poll();ResolveTransfer();}
                catch{failed=true;flight.SetHold(CargoHold.RecoveryRequired);throw;}
                if(failed||transfer!=null)return;
            }
            if(!ownerOnline||paused){flight.SetHold(CargoHold.OwnerOffline);return;}
            if(Phase==CargoPhase.Docking){flight.SetHold(CargoHold.PersistencePending);return;}
            flight.SetHold(CargoHold.None);
            if(Phase==CargoPhase.ToSource||Phase==CargoPhase.ToTarget||Phase==CargoPhase.Returning)
            {
                motion.Tick(elapsed);
                if(motion.ReturningHome&&Phase!=CargoPhase.Returning)RequestReturn(CargoMissionReturnReason.EnergyLow);
                flight.SetHold(motion.Hold);
                if(motion.Arrived&&motion.Hold==CargoHold.None)
                {flight.Arrive(0,0);BusyWaitUnits=0;busyObserved=false;if(Phase==CargoPhase.Docking)flight.SetHold(CargoHold.PersistencePending);}
                return;
            }
            if(Phase!=CargoPhase.Loading&&Phase!=CargoPhase.Unloading)return;
            if(endpointHold==CargoHold.ChunkLoading||endpointHold==CargoHold.ChunkBudget){flight.SetHold(endpointHold);return;}
            if(endpointHold==CargoHold.ContainerBusy||busyObserved)
            {
                flight.SetHold(CargoHold.ContainerBusy);if(elapsed==0)return;
                busyObserved=false;
                long wait=Math.Min(Math.Min(elapsed,100),5000-BusyWaitUnits);
                if(!CanSpend(wait)){RequestReturn(CargoMissionReturnReason.EnergyLow);return;}
                // A loaded but player-locked endpoint requires active hovering.
                // It costs energy; world/owner/I/O freezes above do not.
                energy.Consume(wait);BusyWaitUnits+=wait;
                if(BusyWaitUnits>=5000)RequestReturn(CargoMissionReturnReason.ContainerBusy);
                return;
            }
            flight.SetHold(CargoHold.None);if(elapsed==0)return;
            long remaining=2000-flight.HandlingUnits;
            // Reserve the complete remaining stable handling interval before any
            // of it is charged. Never spend the safe return margin while loading.
            if(!CanSpend(remaining)){RequestReturn(CargoMissionReturnReason.EnergyLow);return;}
            long step=Math.Min(Math.Min(elapsed,100),remaining);
            flight.Consume(step);flight.AdvanceHandling(step,0,0);
        }
        public void BeginTransfer(ICargoJournal journal,ICargoDurableEndpoint endpoint)
        {
            if(!TransferReady||journal==null||endpoint==null)throw new InvalidOperationException("Transfer not ready");
            var snapshot=endpoint.Snapshot();var expected=Phase==CargoPhase.Loading?source:target;
            if(snapshot==null||snapshot.Id!=expected)throw new InvalidOperationException("Unexpected endpoint identity");
            if(snapshot.Busy){busyObserved=true;flight.SetHold(CargoHold.ContainerBusy);return;}
            var plan=Phase==CargoPhase.Loading?CargoPlanner.Load(snapshot,cargo,owner,CargoRules.Slots):CargoPlanner.Unload(snapshot,cargo,owner);
            if(plan.Moved==0){RequestReturn(Phase==CargoPhase.Loading?CargoMissionReturnReason.SourceEmpty:CargoMissionReturnReason.TargetFull);return;}
            if(CargoRevision==long.MaxValue)throw new InvalidOperationException("Cargo revision exhausted");
            var transaction=new CargoTransaction(Guid.NewGuid(),flight.Id,world,CargoRevision,plan);
            transfer=new CargoTransferCoordinator(journal,endpoint,transaction);flight.BeginTransfer();
            flight.SetHold(CargoHold.PersistencePending);
            try{transfer.Begin();ResolveTransfer();}
            catch{failed=true;flight.SetHold(CargoHold.RecoveryRequired);throw;}
        }
        void ResolveTransfer()
        {
            if(transfer.State==CargoTransferState.Saving){flight.SetHold(CargoHold.PersistencePending);return;}
            if(transfer.State==CargoTransferState.RecoveryRequired){failed=true;flight.SetHold(CargoHold.RecoveryRequired);return;}
            if(transfer.State==CargoTransferState.Committed){cargo=transfer.Transaction.Plan.CargoAfter;CargoRevision++;}
            // Aborted before applying a plan retains committed cargo and returns
            // safely; it cannot be mistaken for a successful load or unload.
            if(transfer.State==CargoTransferState.Aborted)RequestReturn(CargoMissionReturnReason.TransactionAborted);
            flight.CompleteTransfer(CargoPlanner.Count(cargo));transfer=null;flight.SetHold(CargoHold.None);
            if(Phase==CargoPhase.Returning)motion.ReturnHome();else motion.Retarget(destination);
        }
    }
}
