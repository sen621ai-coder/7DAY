using System;
using System.Linq;

namespace YFAutomation.CargoDrones
{
    public enum CargoRecoveryDeliveryState{WaitingForContainer,Saving,Completed,RecoveryRequired}
    // The world layer must persist the recovery-container identity and position
    // before creating it. This coordinator moves goods only into that exact identity;
    // it never creates a loot bag, guesses a new destination or clears cargo early.
    public sealed class CargoRecoveryDelivery
    {
        readonly CargoMission mission;
        readonly CargoFileJournal journal;
        readonly Guid endpointId,incarnation;
        CargoTransferCoordinator transfer;
        public CargoRecoveryDeliveryState State{get;private set;}
        public CargoRecoveryDelivery(CargoMission mission,CargoFileJournal journal,Guid endpointId,Guid incarnation)
        {
            if(mission==null||journal==null||endpointId==Guid.Empty||incarnation==Guid.Empty||mission.Phase!=CargoPhase.RecoveryOnly)throw new ArgumentException("Frozen mission and persistent recovery-container identity required");
            mission.Capture().ValidateJournal(journal);this.mission=mission;this.journal=journal;this.endpointId=endpointId;this.incarnation=incarnation;
            State=CargoPlanner.Count(mission.Cargo)==0?CargoRecoveryDeliveryState.Completed:CargoRecoveryDeliveryState.WaitingForContainer;
        }
        public void Begin(ICargoDurableEndpoint endpoint)
        {
            if(State!=CargoRecoveryDeliveryState.WaitingForContainer||endpoint==null)throw new InvalidOperationException("Recovery container not requested");
            var state=mission.Capture();state.ValidateJournal(journal);var inventory=endpoint.Snapshot();
            if(inventory.Id!=endpointId||inventory.Incarnation!=incarnation||inventory.Owner!=state.Owner)throw new InvalidOperationException("Recovery container identity or owner mismatch");
            if(inventory.Busy)return;
            var plan=CargoPlanner.Unload(inventory,state.Cargo,state.Owner,true);
            // A recovery crate is a complete handoff. A partial destination stays
            // untouched, rather than splitting one recovery across unknown crates.
            if(plan.Moved!=CargoPlanner.Count(state.Cargo))return;
            var tx=new CargoTransaction(Guid.NewGuid(),state.Id,state.World,state.Revision,plan);
            transfer=new CargoTransferCoordinator(journal,endpoint,tx);
            try{transfer.Begin();State=CargoRecoveryDeliveryState.Saving;Resolve();}
            catch{State=CargoRecoveryDeliveryState.RecoveryRequired;throw;}
        }
        public void Poll()
        {
            if(State!=CargoRecoveryDeliveryState.Saving)return;
            try{transfer.Poll();Resolve();}catch{State=CargoRecoveryDeliveryState.RecoveryRequired;throw;}
        }
        void Resolve()
        {
            if(transfer.State==CargoTransferState.Saving)return;
            if(transfer.State==CargoTransferState.Aborted){transfer=null;State=CargoRecoveryDeliveryState.WaitingForContainer;return;}
            if(transfer.State!=CargoTransferState.Committed){State=CargoRecoveryDeliveryState.RecoveryRequired;return;}
            var flight=CargoJournalReplay.Read(journal.WorldId,journal.Entries).Single(f=>f.FlightId==transfer.Transaction.FlightId);
            mission.AcceptRecovery(flight,journal);State=CargoRecoveryDeliveryState.Completed;
        }
    }
}
