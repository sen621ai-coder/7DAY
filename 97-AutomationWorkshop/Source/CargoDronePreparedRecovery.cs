using System;
using System.Linq;

namespace YFAutomation.CargoDrones
{
    // Resolves a PREPARE from a complete, validated WAL. This does not rebuild
    // navigation state or waive the native startup fence/checkpoint requirements.
    public sealed class CargoPreparedRecovery
    {
        readonly CargoFileJournal journal;
        readonly ICargoDurableEndpoint endpoint;
        readonly CargoTransaction transaction;
        bool begun;
        bool fenced,requested,resolved;
        CargoTransferState resolution;
        public CargoTransferState State{get;private set;}
        public string Failure{get;private set;}
        public CargoPreparedRecovery(CargoFileJournal journal,ICargoDurableEndpoint endpoint,Guid transactionId)
        {
            if(journal==null||endpoint==null||transactionId==Guid.Empty)throw new ArgumentException("Invalid prepared recovery");
            this.journal=journal;this.endpoint=endpoint;
            var entry=journal.Entries.LastOrDefault(e=>e.Transaction.Id==transactionId);
            if(entry==null||entry.Kind!=CargoJournalKind.Prepare)throw new InvalidOperationException("Transaction is not pending in this WAL");
            transaction=entry.Transaction;ValidatePending();State=CargoTransferState.RecoveryRequired;
        }
        void ValidatePending()
        {
            var flight=CargoJournalReplay.Read(transaction.WorldId,journal.Entries).Single(f=>f.FlightId==transaction.FlightId);
            if(flight.Pending==null||!CargoFileJournal.SameTransaction(flight.Pending,transaction))throw new InvalidOperationException("Prepared recovery history changed");
        }
        public void Begin()
        {
            if(begun)throw new InvalidOperationException("Recovery already begun");begun=true;ValidatePending();
            State=CargoTransferState.Saving;Poll();
        }
        public void Poll()
        {
            if(State!=CargoTransferState.Saving)return;
            try
            {
                if(!fenced)
                {
                    var snapshot=endpoint.Snapshot();var p=transaction.Plan;
                    if(snapshot==null||snapshot.Id!=p.EndpointId||snapshot.Incarnation!=p.Incarnation||snapshot.Busy||!endpoint.AcquireFence(transaction.Id,snapshot.Revision)){Failure="Recovery could not acquire an idle matching endpoint";State=CargoTransferState.RecoveryRequired;return;}
                    fenced=true;
                }
                if(resolved){endpoint.ReleaseFence(transaction.Id);State=resolution;return;}
                if(!requested)
                {
                    var classification=CargoTransferCoordinator.ClassifyRecovery(transaction,endpoint.Snapshot(),endpoint.LastTransaction,false);
                    if(classification==CargoTransferState.Aborted)
                    {
                        ValidatePending();journal.Append(CargoJournalKind.Abort,transaction);resolved=true;resolution=CargoTransferState.Aborted;
                        endpoint.ReleaseFence(transaction.Id);State=resolution;return;
                    }
                    if(classification!=CargoTransferState.Committed){Failure="Prepared inventory differs from both recorded images";State=CargoTransferState.RecoveryRequired;return;}
                    // Never re-apply. Retain each completed step across temporary
                    // native chunk locks, including an already written resolution.
                    endpoint.RequestDurableSave(transaction.Id);requested=true;return;
                }
                var status=endpoint.PollDurableSave(transaction.Id);if(status==CargoSaveResult.Pending)return;
                if(status!=CargoSaveResult.Durable||CargoTransferCoordinator.ClassifyRecovery(transaction,endpoint.Snapshot(),endpoint.LastTransaction,false)!=CargoTransferState.Committed)
                {Failure="Recovery save receipt or after-image could not be confirmed";State=CargoTransferState.RecoveryRequired;return;}
                ValidatePending();journal.Append(CargoJournalKind.Commit,transaction);resolved=true;resolution=CargoTransferState.Committed;
                endpoint.ReleaseFence(transaction.Id);State=resolution;
            }
            catch(CargoEndpointUnavailableException){/* Retain the exact completed recovery step and fence. */}
            catch{State=CargoTransferState.RecoveryRequired;throw;}
        }
        public CargoRecoveredFlight Result()
        {
            if(State!=CargoTransferState.Aborted&&State!=CargoTransferState.Committed)throw new InvalidOperationException("Recovery has no resolved cargo result");
            return CargoJournalReplay.Read(transaction.WorldId,journal.Entries).Single(f=>f.FlightId==transaction.FlightId);
        }
    }
}
