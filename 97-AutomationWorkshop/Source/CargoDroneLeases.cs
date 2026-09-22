using System;
using System.Collections.Generic;
using System.Linq;

namespace YFAutomation.CargoDrones
{
    // Pure allocation: callers supply the actual native observer footprint, never a guessed radius.
    // No native observer may be created/moved before its footprint has been admitted.
    public sealed class CargoChunkBudget
    {
        sealed class Lease
        {
            public Guid Flight;
            public HashSet<long> Chunks;
            public Lease(Guid flight,HashSet<long> chunks){Flight=flight;Chunks=chunks;}
        }
        readonly int perFlight,total;
        readonly Dictionary<Guid,Lease> leases=new Dictionary<Guid,Lease>();
        public CargoChunkBudget(int perFlight=24,int total=128)
        {if(perFlight<1||total<perFlight)throw new ArgumentOutOfRangeException("budget");this.perFlight=perFlight;this.total=total;}
        public int HeldChunks{get{return leases.Values.SelectMany(l=>l.Chunks).Distinct().Count();}}
        public int LeaseCount{get{return leases.Count;}}
        public bool TryAcquire(Guid leaseId,Guid flight,IEnumerable<long> actualFootprint)
        {
            if(leaseId==Guid.Empty||flight==Guid.Empty||actualFootprint==null)throw new ArgumentException("Invalid lease request");
            // Bound enumeration as well as final allocation; untrusted infinite enumerators cannot consume unlimited memory.
            var footprint=new HashSet<long>();int entries=0;
            foreach(var chunk in actualFootprint){if(++entries>4096)return false;footprint.Add(chunk);if(footprint.Count>perFlight)return false;}
            if(footprint.Count==0)return false;
            Lease old;
            if(leases.TryGetValue(leaseId,out old))return old.Flight==flight&&old.Chunks.SetEquals(footprint);
            var sameFlight=new HashSet<long>(leases.Values.Where(l=>l.Flight==flight).SelectMany(l=>l.Chunks));sameFlight.UnionWith(footprint);
            if(sameFlight.Count>perFlight)return false;
            var all=new HashSet<long>(leases.Values.SelectMany(l=>l.Chunks));all.UnionWith(footprint);if(all.Count>total)return false;
            leases.Add(leaseId,new Lease(flight,footprint));return true;
        }
        // Acquire the next window while the safe/current window is still held, then release the old window.
        public bool Release(Guid leaseId,Guid flight)
        {Lease lease;if(!leases.TryGetValue(leaseId,out lease))return true;if(lease.Flight!=flight)return false;leases.Remove(leaseId);return true;}
        public void ReleaseFlight(Guid flight){foreach(var id in leases.Where(p=>p.Value.Flight==flight).Select(p=>p.Key).ToArray())leases.Remove(id);}
        public void Clear(){leases.Clear();}
    }

    public sealed class CargoSourceReservation
    {
        sealed class Reservation{public Guid Flight;public double Expires;}
        readonly Dictionary<Guid,Reservation> reservations=new Dictionary<Guid,Reservation>();
        double lastTime;
        static bool Finite(double value){return !double.IsInfinity(value)&&!double.IsNaN(value);}
        public bool TryReserve(Guid source,Guid flight,double activeTime,double eta)
        {
            if(source==Guid.Empty||flight==Guid.Empty||!Finite(activeTime)||!Finite(eta)||activeTime<lastTime||eta<0)throw new ArgumentException("Invalid reservation clock or identity");
            lastTime=activeTime;Reservation current;
            if(reservations.TryGetValue(source,out current)&&current.Expires>activeTime&&current.Flight!=flight)return false;
            reservations[source]=new Reservation{Flight=flight,Expires=activeTime+Math.Min(180,eta+30)};return true;
        }
        public void Release(Guid source,Guid flight)
        {Reservation current;if(reservations.TryGetValue(source,out current)&&current.Flight==flight)reservations.Remove(source);}
        public void ReleaseFlight(Guid flight){foreach(var source in reservations.Where(p=>p.Value.Flight==flight).Select(p=>p.Key).ToArray())reservations.Remove(source);}
        public void Clear(){reservations.Clear();lastTime=0;}
    }
}
