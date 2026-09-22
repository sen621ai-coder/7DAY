using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace YFAutomation.CargoDrones
{
    public enum CargoJournalKind : byte { Prepare=1,Commit=2,Abort=3 }
    public sealed class CargoTransaction
    {
        public readonly Guid Id,FlightId,WorldId;
        public readonly long CargoRevision;
        public readonly CargoPlan Plan;
        public CargoTransaction(Guid id,Guid flight,Guid world,long cargoRevision,CargoPlan plan)
        {
            if(id==Guid.Empty||flight==Guid.Empty||world==Guid.Empty||cargoRevision<0||cargoRevision==long.MaxValue||plan==null||plan.Moved<1||plan.ExpectedRevision==long.MaxValue)
                throw new ArgumentException("Invalid transaction");
            CargoPlanIntegrity.Validate(plan);
            Id=id;FlightId=flight;WorldId=world;CargoRevision=cargoRevision;Plan=plan;
        }
    }
    public sealed class CargoJournalEntry
    {
        public readonly long Sequence;
        public readonly CargoJournalKind Kind;
        public readonly CargoTransaction Transaction;
        public CargoJournalEntry(long sequence,CargoJournalKind kind,CargoTransaction transaction)
        {if(sequence<1||!Enum.IsDefined(typeof(CargoJournalKind),kind)||transaction==null)throw new InvalidDataException("Invalid journal entry");Sequence=sequence;Kind=kind;Transaction=transaction;}
    }
    public interface ICargoJournal
    {
        // Returns only after a durable append. Throwing leaves the transaction unresolved.
        void Append(CargoJournalKind kind,CargoTransaction transaction);
    }
    public sealed class CargoFileJournal : ICargoJournal,IDisposable
    {
        const int Magic=0x324A4443; // CDJ2: complemented length detects corrupt (not torn) frame headers.
        readonly FileStream stream;
        readonly Guid world;
        long sequence;
        bool faulted;
        bool disposed;
        readonly List<CargoJournalEntry> entries;
        public IList<CargoJournalEntry> Entries{get{return entries.AsReadOnly();}}
        public Guid WorldId{get{return world;}}
        public byte[] HistoryDigest(int count=-1)
        {
            if(disposed)throw new ObjectDisposedException("CargoFileJournal");
            if(faulted)throw new InvalidOperationException("Cannot checkpoint an uncertain journal");
            if(count<0)count=entries.Count;if(count>entries.Count)throw new ArgumentOutOfRangeException("count");
            using(var sha=SHA256.Create())
            {
                var id=world.ToByteArray();sha.TransformBlock(id,0,id.Length,id,0);
                foreach(var entry in entries.Take(count)){var bytes=Encode(entry);sha.TransformBlock(bytes,0,bytes.Length,bytes,0);}
                sha.TransformFinalBlock(new byte[0],0,0);return sha.Hash;
            }
        }
        public CargoFileJournal(string path,Guid worldId)
        {
            if(worldId==Guid.Empty)throw new ArgumentException("Empty world identity");world=worldId;
            stream=new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.Read);
            try
            {
                long validLength;entries=Read(stream,world,out validLength);
                if(validLength!=stream.Length){stream.SetLength(validLength);stream.Flush(true);}
                stream.Position=validLength;sequence=Entries.Count==0?0:Entries[Entries.Count-1].Sequence;
            }
            catch{stream.Dispose();throw;}
        }
        public void Append(CargoJournalKind kind,CargoTransaction tx)
        {
            if(faulted)throw new InvalidOperationException("Journal requires recovery after an uncertain append");
            if(tx.WorldId!=world)throw new InvalidOperationException("World identity mismatch");
            var previous=entries.LastOrDefault(e=>e.Transaction.Id==tx.Id);
            if(kind==CargoJournalKind.Prepare?previous!=null:previous==null||previous.Kind!=CargoJournalKind.Prepare||!SameTransaction(previous.Transaction,tx))
                throw new InvalidOperationException("Invalid or duplicate journal transition");
            var entry=new CargoJournalEntry(checked(sequence+1),kind,tx);
            // Reject stale shipment revisions and overlapping endpoint/flight
            // prepares BEFORE they enter the durable log or mutate native goods.
            CargoJournalReplay.Read(world,entries.Concat(new[]{entry}));
            byte[] payload=Encode(entry),hash;
            using(var sha=SHA256.Create())hash=sha.ComputeHash(payload);
            try
            {
                using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))
                {writer.Write(Magic);writer.Write(payload.Length);writer.Write(~payload.Length);writer.Write(payload);writer.Write(hash);writer.Flush();}
                stream.Flush(true);sequence=entry.Sequence;entries.Add(entry);
            }
            catch{faulted=true;throw;}
        }
        static List<CargoJournalEntry> Read(Stream input,Guid worldId,out long validLength)
        {
            var result=new List<CargoJournalEntry>();validLength=0;var states=new Dictionary<Guid,CargoJournalEntry>();
            using(var reader=new BinaryReader(input,System.Text.Encoding.UTF8,true))
            while(input.Position<input.Length)
            {
                if(input.Length-input.Position<12)break;
                if(reader.ReadInt32()!=Magic)throw new InvalidDataException("Journal frame magic mismatch");
                int size=reader.ReadInt32();int complement=reader.ReadInt32();if(size<1||size>CargoRules.MaxRecordBytes||complement!=~size)throw new InvalidDataException("Invalid journal frame length");
                if(input.Length-input.Position<(long)size+32)break;
                var payload=reader.ReadBytes(size);var storedHash=reader.ReadBytes(32);byte[] actual;
                using(var sha=SHA256.Create())actual=sha.ComputeHash(payload);
                if(!actual.SequenceEqual(storedHash))throw new InvalidDataException("Journal checksum mismatch; automatic recovery refused");
                var entry=Decode(payload);
                if(entry.Transaction.WorldId!=worldId||entry.Sequence!=(long)result.Count+1)throw new InvalidDataException("Journal world or sequence mismatch");
                CargoJournalEntry previous;
                if(entry.Kind==CargoJournalKind.Prepare)
                {if(states.ContainsKey(entry.Transaction.Id))throw new InvalidDataException("Duplicate PREPARE");}
                else
                {
                    if(!states.TryGetValue(entry.Transaction.Id,out previous)||previous.Kind!=CargoJournalKind.Prepare||!SameTransaction(previous.Transaction,entry.Transaction))
                        throw new InvalidDataException("Commit/abort without matching PREPARE");
                }
                states[entry.Transaction.Id]=entry;result.Add(entry);validLength=input.Position;
            }
            return result;
        }
        internal static bool SameTransaction(CargoTransaction a,CargoTransaction b)
        {
            return a.Id==b.Id&&a.WorldId==b.WorldId&&a.FlightId==b.FlightId&&a.CargoRevision==b.CargoRevision&&a.Plan.EndpointId==b.Plan.EndpointId&&
                a.Plan.Incarnation==b.Plan.Incarnation&&a.Plan.Owner==b.Plan.Owner&&a.Plan.ExpectedRevision==b.Plan.ExpectedRevision&&a.Plan.Kind==b.Plan.Kind&&a.Plan.Moved==b.Plan.Moved&&
                CargoPlanner.Equal(a.Plan.EndpointBefore,b.Plan.EndpointBefore)&&CargoPlanner.Equal(a.Plan.EndpointAfter,b.Plan.EndpointAfter)&&
                CargoPlanner.Equal(a.Plan.CargoBefore,b.Plan.CargoBefore)&&CargoPlanner.Equal(a.Plan.CargoAfter,b.Plan.CargoAfter);
        }
        static void Items(BinaryWriter writer,CargoItem[] items)
        {
            writer.Write(items.Length);
            foreach(var item in items)
            {
                writer.Write(item!=null);if(item==null)continue;var value=item.Value;
                writer.Write(item.Count);writer.Write(item.Limit);writer.Write(value.Length);writer.Write(value);
            }
        }
        static CargoItem[] Items(BinaryReader reader,int max)
        {
            int count=reader.ReadInt32();if(count<0||count>max)throw new InvalidDataException("Invalid slot count");
            var items=new CargoItem[count];
            for(int i=0;i<count;i++)if(reader.ReadBoolean())
            {
                int n=reader.ReadInt32(),limit=reader.ReadInt32(),bytes=reader.ReadInt32();
                if(bytes<1||bytes>CargoRules.MaxItemBytes)throw new InvalidDataException("Invalid item byte count");
                var value=reader.ReadBytes(bytes);if(value.Length!=bytes)throw new EndOfStreamException();
                items[i]=new CargoItem(value,n,limit);
            }
            return items;
        }
        static Guid Id(BinaryReader reader){var bytes=reader.ReadBytes(16);if(bytes.Length!=16)throw new EndOfStreamException();return new Guid(bytes);}
        static byte[] Encode(CargoJournalEntry entry)
        {
            using(var memory=new MemoryStream())using(var writer=new BinaryWriter(memory))
            {
                var tx=entry.Transaction;var plan=tx.Plan;
                writer.Write(entry.Sequence);writer.Write((byte)entry.Kind);writer.Write(tx.WorldId.ToByteArray());writer.Write(tx.Id.ToByteArray());writer.Write(tx.FlightId.ToByteArray());writer.Write(tx.CargoRevision);
                var owner=System.Text.Encoding.UTF8.GetBytes(plan.Owner);if(owner.Length>512)throw new InvalidDataException("Owner identity too long");
                writer.Write(plan.EndpointId.ToByteArray());writer.Write(plan.Incarnation.ToByteArray());writer.Write(plan.ExpectedRevision);writer.Write((byte)plan.Kind);writer.Write(plan.Moved);writer.Write(owner.Length);writer.Write(owner);
                Items(writer,plan.EndpointBefore);Items(writer,plan.EndpointAfter);Items(writer,plan.CargoBefore);Items(writer,plan.CargoAfter);
                writer.Flush();if(memory.Length>CargoRules.MaxRecordBytes)throw new InvalidDataException("Transaction exceeds log record limit");return memory.ToArray();
            }
        }
        static CargoJournalEntry Decode(byte[] bytes)
        {
            using(var memory=new MemoryStream(bytes,false))using(var reader=new BinaryReader(memory))
            {
                long sequence=reader.ReadInt64();var kind=(CargoJournalKind)reader.ReadByte();Guid worldId=Id(reader),txId=Id(reader),flight=Id(reader);long cargoRevision=reader.ReadInt64();
                Guid endpoint=Id(reader),incarnation=Id(reader);long revision=reader.ReadInt64();var operation=(CargoTransferKind)reader.ReadByte();int moved=reader.ReadInt32();
                int ownerLength=reader.ReadInt32();if(ownerLength<1||ownerLength>512)throw new InvalidDataException("Invalid owner identity");
                var ownerBytes=reader.ReadBytes(ownerLength);if(ownerBytes.Length!=ownerLength)throw new EndOfStreamException();string owner=new System.Text.UTF8Encoding(false,true).GetString(ownerBytes);
                var before=Items(reader,4096);var after=Items(reader,4096);var cargoBefore=Items(reader,6);var cargoAfter=Items(reader,6);
                CargoPlanner.ValidateCargo(cargoBefore);CargoPlanner.ValidateCargo(cargoAfter);
                if(memory.Position!=memory.Length||before.Length!=after.Length||!Enum.IsDefined(typeof(CargoTransferKind),operation)||moved<1||moved>6)
                    throw new InvalidDataException("Invalid transaction content");
                var snapshot=new CargoInventory(endpoint,incarnation,revision,owner,before,new bool[before.Length],new bool[before.Length]);
                var plan=new CargoPlan(snapshot,before,after,cargoBefore,cargoAfter,moved,operation);
                return new CargoJournalEntry(sequence,kind,new CargoTransaction(txId,flight,worldId,cargoRevision,plan));
            }
        }
        public void Dispose(){disposed=true;stream.Dispose();}
    }

    public enum CargoSaveResult { Pending,Durable,Uncertain }
    // Adapters may throw this only before the requested operation changes state.
    // Identity loss, unknown saves and partial mutations are never transient waits.
    public sealed class CargoEndpointUnavailableException : InvalidOperationException
    {public CargoEndpointUnavailableException():base("Endpoint temporarily unavailable") {}}
    public enum CargoTransferState { Saving,Committed,Aborted,RecoveryRequired }
    public interface ICargoDurableEndpoint
    {
        // Native adapters MUST prove G03 before implementing this contract.
        CargoInventory Snapshot();
        Guid LastTransaction{get;}
        bool AcquireFence(Guid transaction,long expectedRevision);
        void Apply(CargoPlan plan,Guid transaction);
        void RequestDurableSave(Guid transaction);
        CargoSaveResult PollDurableSave(Guid transaction);
        void ReleaseFence(Guid transaction);
    }
    public sealed class CargoTransferCoordinator
    {
        readonly ICargoJournal journal;
        readonly ICargoDurableEndpoint endpoint;
        readonly CargoTransaction transaction;
        public CargoTransferState State{get;private set;}
        public CargoTransaction Transaction{get{return transaction;}}
        public CargoTransferCoordinator(ICargoJournal journal,ICargoDurableEndpoint endpoint,CargoTransaction transaction)
        {
            if(journal==null||endpoint==null||transaction==null)throw new ArgumentNullException();
            this.journal=journal;this.endpoint=endpoint;this.transaction=transaction;State=CargoTransferState.RecoveryRequired;
        }
        bool begun;
        public void Begin()
        {
            if(begun)throw new InvalidOperationException("Transaction was already started");begun=true;
            var plan=transaction.Plan;
            if(!plan.Matches(endpoint.Snapshot())||!endpoint.AcquireFence(transaction.Id,plan.ExpectedRevision))
            {State=CargoTransferState.Aborted;return;}
            try
            {
                if(!plan.Matches(endpoint.Snapshot())){endpoint.ReleaseFence(transaction.Id);State=CargoTransferState.Aborted;return;}
                journal.Append(CargoJournalKind.Prepare,transaction);
                endpoint.Apply(plan,transaction.Id);endpoint.RequestDurableSave(transaction.Id);State=CargoTransferState.Saving;
            }
            catch{State=CargoTransferState.RecoveryRequired;throw;}
        }
        public void Poll()
        {
            if(State!=CargoTransferState.Saving)return;
            try
            {
                var status=endpoint.PollDurableSave(transaction.Id);if(status==CargoSaveResult.Pending)return;
                if(status!=CargoSaveResult.Durable){State=CargoTransferState.RecoveryRequired;return;}
                var current=endpoint.Snapshot();var plan=transaction.Plan;
                if(current.Id!=plan.EndpointId||current.Incarnation!=plan.Incarnation||current.Revision!=plan.ExpectedRevision+1||endpoint.LastTransaction!=transaction.Id||!CargoPlanner.Equal(current.Items,plan.EndpointAfter))
                {State=CargoTransferState.RecoveryRequired;return;}
                journal.Append(CargoJournalKind.Commit,transaction);State=CargoTransferState.Committed;
                endpoint.ReleaseFence(transaction.Id);
            }
            catch{State=CargoTransferState.RecoveryRequired;throw;}
        }
        public static CargoTransferState ClassifyRecovery(CargoTransaction tx,CargoInventory saved,Guid lastTransaction,bool hasCommit)
        {
            if(tx==null)throw new ArgumentNullException("tx");
            if(saved==null)return CargoTransferState.RecoveryRequired;
            var p=tx.Plan;
            if(saved.Id!=p.EndpointId||saved.Incarnation!=p.Incarnation)return CargoTransferState.RecoveryRequired;
            bool before=saved.Revision==p.ExpectedRevision&&CargoPlanner.Equal(saved.Items,p.EndpointBefore);
            bool after=saved.Revision==p.ExpectedRevision+1&&lastTransaction==tx.Id&&CargoPlanner.Equal(saved.Items,p.EndpointAfter);
            if(after)return CargoTransferState.Committed;
            if(before&&!hasCommit&&lastTransaction!=tx.Id)return CargoTransferState.Aborted;
            // Higher revisions need a proven native checkpoint chain; never guess from quantities.
            return CargoTransferState.RecoveryRequired;
        }
    }
}
