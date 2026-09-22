using System;
using System.Collections.Generic;
using System.Linq;

namespace YFAutomation.CargoDrones
{
    // Engine-independent contracts. No Unity object or native inventory may enter this layer.
    public struct CargoPosition : IEquatable<CargoPosition>
    {
        public readonly int X,Y,Z;
        public CargoPosition(int x,int y,int z){X=x;Y=y;Z=z;}
        public double DistanceSquared(CargoPosition b)
        {double x=(double)X-b.X,y=(double)Y-b.Y,z=(double)Z-b.Z;return x*x+y*y+z*z;}
        public bool Equals(CargoPosition b){return X==b.X&&Y==b.Y&&Z==b.Z;}
        public override bool Equals(object b){return b is CargoPosition&&Equals((CargoPosition)b);}
        public override int GetHashCode(){unchecked{return (X*397^Y)*397^Z;}}
    }

    public sealed class CargoRules
    {
        public const int Slots=6,MaxSources=8,MaxItemBytes=65536,MaxRecordBytes=4*1024*1024;
        public const long UnitsPerSecond=1000;
        public readonly int CollectionRadius,DeliveryRadius;
        public readonly long BatteryCapacity;
        public readonly double CruiseSpeed;
        public CargoRules(int collection=64,int delivery=1000,long battery=600000,double speed=6)
        {
            if(collection<1||collection>4096||delivery<1||delivery>65536||battery<1000||battery>86400000||
               double.IsNaN(speed)||double.IsInfinity(speed)||speed<=0||speed>100)
                throw new ArgumentOutOfRangeException("rules");
            CollectionRadius=collection;DeliveryRadius=delivery;BatteryCapacity=battery;CruiseSpeed=speed;
        }
        public bool CanCollect(CargoPosition hub,CargoPosition source){return hub.DistanceSquared(source)<=(double)CollectionRadius*CollectionRadius;}
        public bool CanDeliver(CargoPosition hub,CargoPosition target){return hub.DistanceSquared(target)<=(double)DeliveryRadius*DeliveryRadius;}
        public bool CanDepart(long battery,double flightSeconds)
        {return battery>=0&&battery<=BatteryCapacity&&flightSeconds>=0&&!double.IsNaN(flightSeconds)&&!double.IsInfinity(flightSeconds)&&
            flightSeconds*UnitsPerSecond+Math.Ceiling(BatteryCapacity*.30)<=battery;}
        public static bool IsSource(string name)
        {
            switch(name){case "AutoMinerIron":case "AutoMinerLead":case "AutoMinerCoal":case "AutoMinerNitrate":
            case "AutoMinerClay":case "AutoMinerShale":case "AutoMinerBrass":case "yfAutoForestry":return true;default:return false;}
        }
    }

    public sealed class CargoItem
    {
        readonly byte[] value;
        public readonly int Count,Limit;
        public CargoItem(byte[] serializedValue,int count,int stackLimit)
        {
            if(serializedValue==null||serializedValue.Length==0||serializedValue.Length>CargoRules.MaxItemBytes||count<1||stackLimit<1)
                throw new ArgumentException("Invalid complete item value/count/limit");
            value=(byte[])serializedValue.Clone();Count=count;Limit=stackLimit;
        }
        public byte[] Value{get{return (byte[])value.Clone();}}
        public bool SameValue(CargoItem other){return other!=null&&value.SequenceEqual(other.value);}
        public CargoItem WithCount(int count){return new CargoItem(value,count,Limit);}
    }

    public sealed class CargoInventory
    {
        readonly CargoItem[] items;
        readonly bool[] locked,allowed;
        public readonly Guid Id,Incarnation;
        public readonly long Revision;
        public readonly string Owner;
        public readonly bool Busy;
        public int Length{get{return items.Length;}}
        public CargoInventory(Guid id,Guid incarnation,long revision,string owner,CargoItem[] values,bool[] locks,bool[] permitted,bool busy=false)
        {
            if(id==Guid.Empty||incarnation==Guid.Empty||revision<0||string.IsNullOrEmpty(owner)||values==null||values.Length>4096||
                locks==null||permitted==null||locks.Length!=values.Length||permitted.Length!=values.Length)
                throw new ArgumentException("Invalid endpoint snapshot");
            Id=id;Incarnation=incarnation;Revision=revision;Owner=owner;Busy=busy;
            items=(CargoItem[])values.Clone();locked=(bool[])locks.Clone();allowed=(bool[])permitted.Clone();
        }
        public CargoItem At(int i){return items[i];}
        public bool Writable(int i){return !Busy&&!locked[i]&&allowed[i];}
        public CargoItem[] Items{get{return (CargoItem[])items.Clone();}}
    }

    public sealed class CargoManifest
    {
        public readonly Guid SourceId,TargetId,OriginFlightId;
        public readonly long ConfigurationRevision;
        public CargoManifest(Guid source,Guid target,Guid flight,long revision)
        {if(target==Guid.Empty||flight==Guid.Empty||revision<0)throw new ArgumentException("Invalid cargo manifest");SourceId=source;TargetId=target;OriginFlightId=flight;ConfigurationRevision=revision;}
    }

    public enum CargoPhase { Docked,Preflight,ToSource,Loading,ToTarget,Unloading,Returning,Docking,RecoveryOnly }
    public enum CargoHold { None,ChunkBudget,ChunkLoading,ContainerBusy,OwnerOffline,PathBlocked,PersistencePending,RecoveryRequired }
    public enum CargoTransferKind { Load,Unload,RecoverAtHub }

    public sealed class CargoPlan
    {
        // Owner belongs to the flight/cargo ledger. Endpoint.Owner is legacy
        // placement metadata and is not an access rule or transaction predicate.
        readonly CargoItem[] endpointBefore,endpointAfter,cargoBefore,cargoAfter;
        public readonly Guid EndpointId,Incarnation;
        public readonly string Owner;
        public readonly long ExpectedRevision;
        public readonly int Moved;
        public readonly CargoTransferKind Kind;
        internal CargoPlan(CargoInventory endpoint,CargoItem[] before,CargoItem[] after,CargoItem[] oldCargo,CargoItem[] newCargo,int moved,CargoTransferKind kind,string cargoOwner=null)
        {EndpointId=endpoint.Id;Incarnation=endpoint.Incarnation;Owner=cargoOwner??endpoint.Owner;if(string.IsNullOrEmpty(Owner))throw new ArgumentException("Cargo owner required");ExpectedRevision=endpoint.Revision;endpointBefore=(CargoItem[])before.Clone();endpointAfter=(CargoItem[])after.Clone();cargoBefore=(CargoItem[])oldCargo.Clone();cargoAfter=(CargoItem[])newCargo.Clone();Moved=moved;Kind=kind;}
        public CargoItem[] EndpointBefore{get{return (CargoItem[])endpointBefore.Clone();}}
        public CargoItem[] EndpointAfter{get{return (CargoItem[])endpointAfter.Clone();}}
        public CargoItem[] CargoBefore{get{return (CargoItem[])cargoBefore.Clone();}}
        public CargoItem[] CargoAfter{get{return (CargoItem[])cargoAfter.Clone();}}
        public bool Matches(CargoInventory current)
        {
            if(current.Busy||current.Id!=EndpointId||current.Incarnation!=Incarnation||current.Revision!=ExpectedRevision||!CargoPlanner.Equal(endpointBefore,current.Items))return false;
            for(int i=0;i<endpointBefore.Length;i++)if(!CargoPlanner.Equal(new[]{endpointBefore[i]},new[]{endpointAfter[i]})&&!current.Writable(i))return false;
            return true;
        }
    }

    public static class CargoPlanner
    {
        public static int Count(CargoItem[] items){if(items==null)throw new ArgumentNullException("items");return items.Sum(i=>i==null?0:i.Count);}
        public static void ValidateCargo(CargoItem[] cargo)
        {if(cargo==null||cargo.Length!=CargoRules.Slots||cargo.Any(i=>i!=null&&i.Count!=1))throw new ArgumentException("Cargo must have six single-package slots");}
        public static bool Equal(CargoItem[] a,CargoItem[] b)
        {
            if(a==null||b==null||a.Length!=b.Length)return false;
            for(int i=0;i<a.Length;i++)if(a[i]==null?b[i]!=null:b[i]==null||a[i].Count!=b[i].Count||a[i].Limit!=b[i].Limit||!a[i].SameValue(b[i]))return false;
            return true;
        }
        public static CargoPlan Load(CargoInventory source,CargoItem[] cargo,string owner,int budget)
        {
            ValidateCargo(cargo);if(source==null)throw new ArgumentNullException("source");
            if(Count(cargo)!=0)throw new InvalidOperationException("Existing cargo must be delivered or recovered first");
            if(budget<0||budget>CargoRules.Slots)throw new ArgumentOutOfRangeException("budget");
            var before=source.Items;var after=source.Items;var result=(CargoItem[])cargo.Clone();int moved=0;
            if(!source.Busy)
            for(int i=0;i<after.Length&&moved<budget;i++)
            {
                if(!source.Writable(i)||after[i]==null)continue;
                int take=Math.Min(after[i].Count,budget-moved);
                for(int n=0;n<take;n++)result[moved++]=after[i].WithCount(1);
                after[i]=after[i].Count==take?null:after[i].WithCount(after[i].Count-take);
            }
            return new CargoPlan(source,before,after,cargo,result,moved,CargoTransferKind.Load,owner);
        }
        public static CargoPlan Unload(CargoInventory target,CargoItem[] cargo,string owner,bool recovery=false)
        {
            ValidateCargo(cargo);if(target==null)throw new ArgumentNullException("target");
            var before=target.Items;var after=target.Items;var result=(CargoItem[])cargo.Clone();int moved=0;
            if(!target.Busy)
            for(int i=0;i<result.Length;i++)
            {
                var item=result[i];if(item==null)continue;bool placed=false;
                for(int pass=0;pass<2&&!placed;pass++)for(int j=0;j<after.Length;j++)
                {
                    if(!target.Writable(j))continue;var slot=after[j];
                    if(pass==0?(slot==null||!slot.SameValue(item)||slot.Count>=Math.Min(slot.Limit,item.Limit)):slot!=null)continue;
                    after[j]=slot==null?item.WithCount(1):slot.WithCount(slot.Count+1);result[i]=null;moved++;placed=true;break;
                }
            }
            return new CargoPlan(target,before,after,cargo,result,moved,recovery?CargoTransferKind.RecoverAtHub:CargoTransferKind.Unload,owner);
        }
    }

    public sealed class CargoCandidate
    {
        public Guid EndpointId;
        public int Priority;
        public double WaitingSeconds,Occupancy,LastServed;
        public bool Available;
    }
    public static class CargoScheduler
    {
        public static CargoCandidate Select(IEnumerable<CargoCandidate> candidates)
        {
            return candidates.Where(c=>c.Available).OrderByDescending(c=>c.WaitingSeconds>=300)
                .ThenByDescending(c=>c.WaitingSeconds>=300?c.WaitingSeconds:0).ThenByDescending(c=>c.Priority)
                .ThenByDescending(c=>c.Occupancy).ThenBy(c=>c.LastServed).ThenBy(c=>c.EndpointId).FirstOrDefault();
        }
    }

    // A navigation controller and flight state machine can share one account.
    // Navigation charges movement; flight handling charges only its own activity.
    public sealed class CargoEnergy
    {
        public long Remaining{get;private set;}
        public CargoEnergy(long remaining){if(remaining<0)throw new ArgumentException("Negative energy");Remaining=remaining;}
        public void Consume(long units)
        {if(units<0||units>Remaining)throw new InvalidOperationException("Cannot overdraw energy");Remaining-=units;}
        internal void Charge(long units,long capacity)
        {if(units<0||capacity<Remaining)throw new ArgumentException("Invalid charge");Remaining+=Math.Min(units,capacity-Remaining);}
    }
    public sealed class CargoFlight
    {
        public readonly Guid Id,TargetId;
        public CargoPhase Phase{get;private set;}
        public CargoHold Hold{get;private set;}
        public bool RecallRequested{get;private set;}
        public bool TransferPending{get;private set;}
        readonly CargoEnergy energy;
        public long Battery{get{return energy.Remaining;}}
        public long HandlingUnits{get;private set;}
        public CargoFlight(Guid id,Guid target,long battery)
            :this(id,target,new CargoEnergy(battery)){}
        public CargoFlight(Guid id,Guid target,CargoEnergy energy)
        {if(id==Guid.Empty||target==Guid.Empty||energy==null)throw new ArgumentException("Invalid flight");Id=id;TargetId=target;this.energy=energy;Phase=CargoPhase.Preflight;}
        public void Depart(bool carrying){Require(CargoPhase.Preflight);Phase=carrying?CargoPhase.ToTarget:CargoPhase.ToSource;}
        public void SetHold(CargoHold reason){Hold=reason;}
        public void Arrive(double distance,double speed)
        {
            if(Hold!=CargoHold.None||double.IsNaN(distance)||double.IsNaN(speed)||distance<0||distance>1||speed<0||speed>.5)throw new InvalidOperationException("Not at authoritative arrival point");
            if(Phase==CargoPhase.ToSource)Phase=CargoPhase.Loading;else if(Phase==CargoPhase.ToTarget)Phase=CargoPhase.Unloading;
            else if(Phase==CargoPhase.Returning)Phase=CargoPhase.Docking;else throw new InvalidOperationException("Cannot arrive in this phase");
        }
        public void AdvanceHandling(long elapsedUnits,double distance,double speed)
        {
            if(elapsedUnits<0)throw new ArgumentOutOfRangeException("elapsedUnits");
            if(Phase!=CargoPhase.Loading&&Phase!=CargoPhase.Unloading)throw new InvalidOperationException("Not handling cargo");
            if(TransferPending||Hold!=CargoHold.None)return;
            if(double.IsNaN(distance)||double.IsNaN(speed)||distance<0||distance>1||speed<0||speed>.5){HandlingUnits=0;return;}
            HandlingUnits+=Math.Min(elapsedUnits,2000-HandlingUnits);
        }
        public void BeginTransfer(){if(Hold!=CargoHold.None||TransferPending||RecallRequested||HandlingUnits<2000||(Phase!=CargoPhase.Loading&&Phase!=CargoPhase.Unloading))throw new InvalidOperationException("Cannot start transfer");TransferPending=true;HandlingUnits=0;}
        public void CompleteTransfer(int cargoCount)
        {if(!TransferPending||cargoCount<0||cargoCount>CargoRules.Slots)throw new InvalidOperationException("Invalid completion");TransferPending=false;Phase=RecallRequested||Phase==CargoPhase.Unloading||cargoCount==0?CargoPhase.Returning:CargoPhase.ToTarget;}
        public void Recall(){RecallRequested=true;if(!TransferPending&&Phase!=CargoPhase.Docked&&Phase!=CargoPhase.RecoveryOnly)Phase=CargoPhase.Returning;}
        public void Dock(){Require(CargoPhase.Docking);Phase=CargoPhase.Docked;}
        internal void Restore(CargoPhase phase,CargoHold hold,bool recall,long handling)
        {if(!Enum.IsDefined(typeof(CargoPhase),phase)||!Enum.IsDefined(typeof(CargoHold),hold)||handling<0||handling>2000)throw new ArgumentException("Invalid saved flight");Phase=phase;Hold=hold;RecallRequested=recall;HandlingUnits=handling;}
        public void Consume(long elapsedUnits)
        {if(elapsedUnits<0)throw new ArgumentOutOfRangeException("elapsedUnits");if(Hold!=CargoHold.None)return;energy.Consume(elapsedUnits);}
        void Require(CargoPhase phase){if(Phase!=phase||Hold!=CargoHold.None)throw new InvalidOperationException("Unexpected flight phase");}
    }
}
