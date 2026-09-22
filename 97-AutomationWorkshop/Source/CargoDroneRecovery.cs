using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace YFAutomation.CargoDrones
{
    public static class CargoPlanIntegrity
    {
        public static void Validate(CargoPlan plan)
        {
            var before=plan.EndpointBefore;var after=plan.EndpointAfter;
            var oldCargo=plan.CargoBefore;var newCargo=plan.CargoAfter;
            CargoPlanner.ValidateCargo(oldCargo);CargoPlanner.ValidateCargo(newCargo);
            if(before.Length!=after.Length||plan.Moved<1||plan.Moved>CargoRules.Slots||!Enum.IsDefined(typeof(CargoTransferKind),plan.Kind))
                throw new InvalidDataException("Invalid inventory transfer shape");
            bool loading=plan.Kind==CargoTransferKind.Load;
            if(loading&&CargoPlanner.Count(oldCargo)!=0)throw new InvalidDataException("Cannot load existing cargo");
            long endpointDelta=0;
            for(int i=0;i<before.Length;i++)
            {
                var a=before[i];var b=after[i];
                if(a!=null&&b!=null&&(!a.SameValue(b)||a.Limit!=b.Limit))throw new InvalidDataException("Transfer replaces slot value");
                long delta=(b==null?0L:b.Count)-(a==null?0L:a.Count);
                if(loading?delta>0:delta<0)throw new InvalidDataException("Transfer changes inventory in the wrong direction");
                if(!loading&&delta>0&&b.Count>b.Limit)throw new InvalidDataException("Transfer exceeds native stack limit");
                endpointDelta+=delta;
            }
            for(int i=0;i<oldCargo.Length;i++)
            {
                if(!loading&&newCargo[i]!=null&&(oldCargo[i]==null||!CargoPlanner.Equal(new[]{oldCargo[i]},new[]{newCargo[i]})))
                    throw new InvalidDataException("Unload creates or replaces cargo");
            }
            int cargoDelta=CargoPlanner.Count(newCargo)-CargoPlanner.Count(oldCargo);
            if(cargoDelta!=(loading?plan.Moved:-plan.Moved)||endpointDelta!=-cargoDelta)
                throw new InvalidDataException("Transfer count does not match its declared movement");
            var quantities=new Dictionary<string,long>();
            Accumulate(quantities,before,1);Accumulate(quantities,oldCargo,1);
            Accumulate(quantities,after,-1);Accumulate(quantities,newCargo,-1);
            if(quantities.Values.Any(n=>n!=0))throw new InvalidDataException("Transfer does not conserve complete item values");
        }
        static void Accumulate(Dictionary<string,long> values,CargoItem[] items,int sign)
        {
            foreach(var item in items)if(item!=null)
            {string key=Convert.ToBase64String(item.Value)+":"+item.Limit;long value;values.TryGetValue(key,out value);values[key]=checked(value+(long)sign*item.Count);}
        }
    }

    public sealed class CargoRecoveredFlight
    {
        readonly CargoItem[] cargo;
        public readonly Guid FlightId;
        public readonly string Owner;
        public readonly long Revision;
        public readonly CargoTransaction Pending;
        public CargoItem[] Cargo{get{return (CargoItem[])cargo.Clone();}}
        internal CargoRecoveredFlight(Guid flight,string owner,long revision,CargoItem[] items,CargoTransaction pending)
        {FlightId=flight;Owner=owner;Revision=revision;cargo=(CargoItem[])items.Clone();Pending=pending;}
    }

    // Replays complete WAL history. Checkpoint/truncated histories must use a
    // future explicit checkpoint decoder, never infer missing cargo from a PREPARE.
    // This reconstructs cargo only; native endpoints still need startup fences.
    public static class CargoJournalReplay
    {
        sealed class Flight{public string Owner;public long Revision;public CargoItem[] Items=new CargoItem[CargoRules.Slots];public CargoTransaction Pending;}
        public static IList<CargoRecoveredFlight> Read(Guid world,IEnumerable<CargoJournalEntry> entries)
        {
            if(world==Guid.Empty||entries==null)throw new ArgumentException("World/history required");
            var flights=new Dictionary<Guid,Flight>();var transactions=new HashSet<Guid>();
            var endpointFences=new Dictionary<Guid,Guid>();long sequence=0;
            foreach(var entry in entries)
            {
                var tx=entry.Transaction;
                if(entry.Sequence!=checked(++sequence)||tx.WorldId!=world)throw new InvalidDataException("Recovery history world/sequence mismatch");
                CargoPlanIntegrity.Validate(tx.Plan);
                Flight flight;
                if(!flights.TryGetValue(tx.FlightId,out flight)){flight=new Flight{Owner=tx.Plan.Owner};flights.Add(tx.FlightId,flight);}
                if(flight.Owner!=tx.Plan.Owner)throw new InvalidDataException("Cargo history changes flight owner");
                if(entry.Kind==CargoJournalKind.Prepare)
                {
                    if(!transactions.Add(tx.Id)||flight.Pending!=null||endpointFences.ContainsKey(tx.Plan.EndpointId))throw new InvalidDataException("Overlapping or repeated prepared transfer");
                    if(tx.CargoRevision!=flight.Revision||!CargoPlanner.Equal(tx.Plan.CargoBefore,flight.Items))throw new InvalidDataException("Missing or divergent cargo history");
                    flight.Pending=tx;endpointFences.Add(tx.Plan.EndpointId,tx.Id);
                }
                else
                {
                    if(flight.Pending==null||!CargoFileJournal.SameTransaction(flight.Pending,tx))throw new InvalidDataException("Resolution lacks matching PREPARE");
                    if(entry.Kind==CargoJournalKind.Commit){flight.Items=tx.Plan.CargoAfter;flight.Revision=checked(flight.Revision+1);}
                    else if(entry.Kind!=CargoJournalKind.Abort)throw new InvalidDataException("Unknown recovery record");
                    endpointFences.Remove(tx.Plan.EndpointId);flight.Pending=null;
                }
            }
            return flights.Select(p=>new CargoRecoveredFlight(p.Key,p.Value.Owner,p.Value.Revision,p.Value.Items,p.Value.Pending)).ToList().AsReadOnly();
        }
    }
}
