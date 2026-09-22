using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace YFAutomation.CargoDrones
{
    public sealed class CargoMotionState
    {
        readonly CargoPoint[] trail,remaining;
        public CargoPoint[] Trail{get{return (CargoPoint[])trail.Clone();}}
        public CargoPoint[] Remaining{get{return (CargoPoint[])remaining.Clone();}}
        public readonly CargoPoint Position,Target;
        public readonly bool Returning,EnergyRecall,Arrived,Blocked;
        public readonly CargoHold Hold;
        public readonly double Speed,ApproachSpeed,DistanceTravelled;
        public readonly long Reserve,MovingUnits;
        internal CargoMotionState(CargoPoint position,CargoPoint target,CargoPoint[] trail,CargoPoint[] remaining,bool returning,bool energyRecall,bool arrived,bool blocked,CargoHold hold,double speed,double approachSpeed,long reserve,long movingUnits,double distance)
        {
            CargoReturnTrail.Restore(trail);
            if(remaining==null||remaining.Length>4096||!Enum.IsDefined(typeof(CargoHold),hold)||!Finite(speed)||speed<=0||speed>100||!Finite(approachSpeed)||approachSpeed<=0||approachSpeed>speed||reserve<0||movingUnits<0||!Finite(distance)||distance<0)throw new InvalidDataException("Invalid motion checkpoint");
            if(!returning&&trail[trail.Length-1].Distance(position)>1e-7||arrived&&position.Distance(target)>1e-7||returning&&target.Distance(trail[0])>1e-7)throw new InvalidDataException("Motion checkpoint position disagrees with route");
            var from=position;foreach(var to in remaining){if(from.Distance(to)>16.000001)throw new InvalidDataException("Saved route skips a segment");from=to;}
            if(returning&&!arrived&&(remaining.Length==0||from.Distance(trail[0])>1e-7))throw new InvalidDataException("Incomplete saved return corridor");
            Position=position;Target=target;this.trail=(CargoPoint[])trail.Clone();this.remaining=(CargoPoint[])remaining.Clone();Returning=returning;EnergyRecall=energyRecall;Arrived=arrived;Blocked=blocked;Hold=hold;Speed=speed;ApproachSpeed=approachSpeed;Reserve=reserve;MovingUnits=movingUnits;DistanceTravelled=distance;
        }
        static bool Finite(double value){return !double.IsNaN(value)&&!double.IsInfinity(value);}
    }

    public sealed class CargoMissionState
    {
        readonly CargoItem[] cargo;
        public CargoItem[] Cargo{get{return (CargoItem[])cargo.Clone();}}
        public readonly Guid World,Id,Source,Target;
        public readonly string Owner;
        public readonly CargoPoint Destination;
        public readonly long Reserve,Battery,Revision,Handling,BusyWait;
        public readonly CargoPhase Phase;
        public readonly CargoHold Hold;
        public readonly bool Recall,BusyObserved;
        public readonly CargoMissionReturnReason ReturnReason;
        public readonly CargoMotionState Motion;
        internal CargoMissionState(Guid world,Guid id,Guid source,Guid target,string owner,CargoPoint destination,long reserve,CargoPhase phase,CargoHold hold,bool recall,long handling,long busyWait,bool busyObserved,CargoMissionReturnReason reason,long battery,long revision,CargoItem[] cargo,CargoMotionState motion)
        {
            CargoPlanner.ValidateCargo(cargo);
            if(world==Guid.Empty||id==Guid.Empty||source==Guid.Empty||target==Guid.Empty||source==target||string.IsNullOrEmpty(owner)||Encoding.UTF8.GetByteCount(owner)>512||reserve<0||battery<0||battery>86400000||revision<0||handling<0||handling>2000||busyWait<0||busyWait>5000||motion==null||motion.Reserve!=reserve||!Enum.IsDefined(typeof(CargoPhase),phase)||!Enum.IsDefined(typeof(CargoHold),hold)||!Enum.IsDefined(typeof(CargoMissionReturnReason),reason))throw new InvalidDataException("Invalid mission checkpoint");
            if(phase!=CargoPhase.RecoveryOnly&&(phase==CargoPhase.Returning||phase==CargoPhase.Docking||phase==CargoPhase.Docked)!=motion.Returning)throw new InvalidDataException("Saved phase disagrees with navigation");
            if((phase==CargoPhase.Loading||phase==CargoPhase.Unloading||phase==CargoPhase.Docking||phase==CargoPhase.Docked)&&!motion.Arrived)throw new InvalidDataException("Saved handling/docking phase is not at destination");
            int count=CargoPlanner.Count(cargo);
            if((phase==CargoPhase.ToSource||phase==CargoPhase.Loading)&&count!=0||(phase==CargoPhase.ToTarget||phase==CargoPhase.Unloading)&&(count==0||motion.Target.Distance(destination)>1e-7))throw new InvalidDataException("Saved cargo phase disagrees with destination or cargo");
            World=world;Id=id;Source=source;Target=target;Owner=owner;Destination=destination;Reserve=reserve;Phase=phase;Hold=hold;Recall=recall;Handling=handling;BusyWait=busyWait;BusyObserved=busyObserved;ReturnReason=reason;Battery=battery;Revision=revision;this.cargo=(CargoItem[])cargo.Clone();Motion=motion;
        }
        public void ValidateJournal(CargoFileJournal journal,int count=-1)
        {
            if(journal==null||journal.WorldId!=World)throw new InvalidDataException("Checkpoint world mismatch");
            var saved=CargoJournalReplay.Read(World,count<0?journal.Entries:journal.Entries.Take(count)).SingleOrDefault(f=>f.FlightId==Id);
            if(saved==null?Revision!=0||CargoPlanner.Count(cargo)!=0:saved.Pending!=null||saved.Owner!=Owner||saved.Revision!=Revision||!CargoPlanner.Equal(saved.Cargo,cargo))throw new InvalidDataException("Checkpoint cargo does not match resolved WAL history");
        }
    }

    // Authoritative navigation checkpoint publication. The complete WAL is kept;
    // no log prefix is truncated and no stale manifest is silently rolled back.
    // Native endpoint/world epoch validation remains required before activation.
    public sealed class CargoCheckpointStore : IDisposable
    {
        const int Magic=0x31504343,MaxBytes=16*1024*1024;
        readonly string directory;
        readonly Guid world;
        readonly FileStream lease;
        bool failed;
        bool disposed;
        public CargoCheckpointStore(string directory,Guid world)
        {
            if(world==Guid.Empty)throw new ArgumentException("World required");
            this.directory=Path.GetFullPath(directory);this.world=world;Directory.CreateDirectory(this.directory);
            lease=new FileStream(Path.Combine(this.directory,"checkpoint.writer"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            try{ValidateManifestIdentity();}catch{lease.Dispose();throw;}
        }
        string PathFor(string name){return Path.Combine(directory,name);}
        void ValidateManifestIdentity()
        {
            if(!File.Exists(PathFor("manifest")))return;
            using(var memory=new MemoryStream(Unframe(ReadBounded(PathFor("manifest"))),false))using(var reader=new BinaryReader(memory))
            {if(Id(reader)!=world||Id(reader)==Guid.Empty)throw new InvalidDataException("Existing manifest belongs to another world or has invalid generation");Exact(reader,32);End(memory);}
        }
        public Guid Save(IEnumerable<CargoMissionState> missions,CargoFileJournal journal)
        {return SaveCore(missions,journal,null);}
        public Guid SaveWorld(CargoWorldState state,CargoFileJournal journal)
        {
            if(state==null||state.Hubs.Any(h=>h.Configuration.WorldId!=world))throw new InvalidDataException("Invalid world checkpoint");
            return SaveCore(state.Missions,journal,state.Hubs);
        }
        Guid SaveCore(IEnumerable<CargoMissionState> missions,CargoFileJournal journal,CargoHubState[] hubs)
        {
            if(disposed)throw new ObjectDisposedException("CargoCheckpointStore");
            if(failed)throw new InvalidOperationException("Checkpoint store requires reopening after failed publication");
            ValidateManifestIdentity();
            var states=missions.ToArray();Validate(states,journal);var digest=journal.HistoryDigest();
            Guid generation=Guid.NewGuid();byte[] body;
            using(var memory=new MemoryStream())using(var writer=new BinaryWriter(memory,Encoding.UTF8,true))
            {
                writer.Write(world.ToByteArray());writer.Write(generation.ToByteArray());writer.Write(digest);writer.Write(states.Length);
                foreach(var state in states)Write(writer,state);
                writer.Write(hubs!=null);if(hubs!=null){writer.Write(hubs.Length);foreach(var hub in hubs)WriteHub(writer,hub);}
                writer.Flush();body=memory.ToArray();
            }
            var snapshot=Frame(body);string name="ledger-"+generation.ToString("N")+".snapshot";
            byte[] manifest;
            using(var memory=new MemoryStream())using(var writer=new BinaryWriter(memory,Encoding.UTF8,true))
            {writer.Write(world.ToByteArray());writer.Write(generation.ToByteArray());writer.Write(Hash(snapshot));writer.Flush();manifest=Frame(memory.ToArray());}
            try
            {
                DurableCreate(PathFor(name),snapshot);
                if(!ReadBounded(PathFor(name)).SequenceEqual(snapshot))throw new IOException("Checkpoint readback mismatch");
                string temporary=PathFor("manifest-"+generation.ToString("N")+".tmp");DurableCreate(temporary,manifest);
                if(!journal.HistoryDigest().SequenceEqual(digest))throw new InvalidOperationException("WAL changed while publishing checkpoint");
                string current=PathFor("manifest");
                if(File.Exists(current))File.Replace(temporary,current,PathFor("manifest.previous"));else File.Move(temporary,current);
                if(!ReadBounded(current).SequenceEqual(manifest))throw new IOException("Manifest readback mismatch");
                return generation;
            }
            catch{failed=true;throw;}
        }
        public CargoMissionState[] Load(CargoFileJournal journal)
        {CargoHubState[] hubs;return LoadCore(journal,out hubs);}
        public CargoWorldState LoadWorld(CargoFileJournal journal)
        {CargoHubState[] hubs;var missions=LoadCore(journal,out hubs);if(hubs==null)throw new InvalidDataException("Legacy navigation checkpoint has no world hub associations");return new CargoWorldState(missions,hubs);}
        // The world coordinator admits only one transaction before its next
        // checkpoint: at most PREPARE + COMMIT/ABORT may follow that checkpoint.
        // Prove the exact prefix digest; never accept an arbitrary stale ledger.
        public CargoWorldState LoadWorldForRecovery(CargoFileJournal journal)
        {CargoHubState[] hubs;var missions=LoadCore(journal,out hubs,2);if(hubs==null)throw new InvalidDataException("Recovery requires hub associations");return new CargoWorldState(missions,hubs);}
        CargoMissionState[] LoadCore(CargoFileJournal journal,out CargoHubState[] hubs,int maxTail=0)
        {
            hubs=null;
            if(disposed)throw new ObjectDisposedException("CargoCheckpointStore");
            if(journal==null||journal.WorldId!=world)throw new InvalidDataException("Checkpoint journal world mismatch");
            Guid generation;byte[] expected;
            using(var memory=new MemoryStream(Unframe(ReadBounded(PathFor("manifest"))),false))using(var reader=new BinaryReader(memory))
            {if(Id(reader)!=world)throw new InvalidDataException("Manifest world mismatch");generation=Id(reader);expected=Exact(reader,32);End(memory);}
            byte[] snapshot=ReadBounded(PathFor("ledger-"+generation.ToString("N")+".snapshot"));
            if(!Hash(snapshot).SequenceEqual(expected))throw new InvalidDataException("Manifest/snapshot mismatch");
            CargoMissionState[] states;int prefix=journal.Entries.Count;
            using(var memory=new MemoryStream(Unframe(snapshot),false))using(var reader=new BinaryReader(memory,new UTF8Encoding(false,true)))
            {
                if(Id(reader)!=world||Id(reader)!=generation)throw new InvalidDataException("Snapshot identity mismatch");
                var digest=Exact(reader,32);
                while(prefix>=Math.Max(0,journal.Entries.Count-maxTail)&&!digest.SequenceEqual(journal.HistoryDigest(prefix)))prefix--;
                if(prefix<Math.Max(0,journal.Entries.Count-maxTail))throw new InvalidDataException("Snapshot WAL checkpoint mismatch; recovery required");
                int count=reader.ReadInt32();if(count<0||count>64)throw new InvalidDataException("Checkpoint flight limit");
                states=new CargoMissionState[count];for(int i=0;i<count;i++)states[i]=Read(reader);
                // Old navigation-only snapshots end here. They remain readable,
                // but cannot be promoted to world checkpoints by guessing hubs.
                if(memory.Position<memory.Length&&Bool(reader))
                {int n=reader.ReadInt32();if(n<0||n>16)throw new InvalidDataException("Invalid hub count");hubs=new CargoHubState[n];for(int i=0;i<n;i++)hubs[i]=ReadHub(reader);}
                End(memory);
            }
            Validate(states,journal,prefix);return states;
        }
        static void Text(BinaryWriter w,string value)
        {var bytes=new UTF8Encoding(false,true).GetBytes(value);if(bytes.Length<1||bytes.Length>512)throw new InvalidDataException("Invalid checkpoint text");w.Write(bytes.Length);w.Write(bytes);}
        static string Text(BinaryReader r)
        {int n=r.ReadInt32();if(n<1||n>512)throw new InvalidDataException("Invalid checkpoint text");return new UTF8Encoding(false,true).GetString(Exact(r,n));}
        static void Cell(BinaryWriter w,CargoPosition p){w.Write(p.X);w.Write(p.Y);w.Write(p.Z);}
        static CargoPosition Cell(BinaryReader r){return new CargoPosition(r.ReadInt32(),r.ReadInt32(),r.ReadInt32());}
        static void Binding(BinaryWriter w,CargoBinding b)
        {w.Write(b!=null);if(b==null)return;w.Write(b.WorldId.ToByteArray());w.Write(b.EndpointId.ToByteArray());w.Write(b.Incarnation.ToByteArray());Cell(w,b.Position);Text(w,b.Owner);Text(w,b.BlockName);}
        static CargoBinding Binding(BinaryReader r)
        {return !Bool(r)?null:new CargoBinding(Id(r),Id(r),Id(r),Cell(r),Text(r),Text(r));}
        static void WriteHub(BinaryWriter w,CargoHubState h)
        {
            var c=h.Configuration;w.Write(c.WorldId.ToByteArray());w.Write(c.HubId.ToByteArray());Cell(w,c.Position);Text(w,c.Owner);w.Write(c.Revision);w.Write(c.Paused);
            var sources=c.Sources;w.Write(sources.Length);foreach(var s in sources)Binding(w,s);Binding(w,c.Target);
            w.Write(h.Flight.ToByteArray());Binding(w,h.ShipmentSource);Binding(w,h.ShipmentTarget);w.Write(h.Battery);w.Write(h.SourceCursor);w.Write(h.Removed);
        }
        static CargoHubState ReadHub(BinaryReader r)
        {
            Guid world=Id(r),hub=Id(r);var at=Cell(r);string owner=Text(r);long revision=r.ReadInt64();bool paused=Bool(r);
            int n=r.ReadInt32();if(n<0||n>CargoRules.MaxSources)throw new InvalidDataException("Invalid source count");var sources=new CargoBinding[n];for(int i=0;i<n;i++)sources[i]=Binding(r);
            var config=CargoHubConfiguration.Restore(world,hub,at,owner,revision,sources,Binding(r),paused);
            return new CargoHubState(config,Id(r),Binding(r),Binding(r),r.ReadInt64(),r.ReadInt32(),Bool(r));
        }
        void Validate(CargoMissionState[] states,CargoFileJournal journal,int count=-1)
        {
            if(states.Length>64||states.Any(s=>s==null||s.World!=world)||states.Select(s=>s.Id).Distinct().Count()!=states.Length||journal==null||journal.WorldId!=world)throw new InvalidDataException("Invalid checkpoint flight set");
            foreach(var state in states)state.ValidateJournal(journal,count);
            foreach(var flight in CargoJournalReplay.Read(world,count<0?journal.Entries:journal.Entries.Take(count)))
                if(flight.Pending!=null||CargoPlanner.Count(flight.Cargo)!=0&&!states.Any(s=>s.Id==flight.FlightId))throw new InvalidDataException("Checkpoint omits unresolved or carrying flight");
        }
        static void Write(BinaryWriter w,CargoMissionState s)
        {
            w.Write(s.World.ToByteArray());w.Write(s.Id.ToByteArray());w.Write(s.Source.ToByteArray());w.Write(s.Target.ToByteArray());
            var owner=Encoding.UTF8.GetBytes(s.Owner);w.Write(owner.Length);w.Write(owner);Point(w,s.Destination);
            w.Write(s.Reserve);w.Write((byte)s.Phase);w.Write((byte)s.Hold);w.Write(s.Recall);w.Write(s.Handling);w.Write(s.BusyWait);w.Write(s.BusyObserved);w.Write((byte)s.ReturnReason);w.Write(s.Battery);w.Write(s.Revision);
            foreach(var item in s.Cargo){w.Write(item!=null);if(item!=null){var value=item.Value;w.Write(item.Limit);w.Write(value.Length);w.Write(value);}}
            var m=s.Motion;Point(w,m.Position);Point(w,m.Target);Points(w,m.Trail);Points(w,m.Remaining);
            w.Write(m.Returning);w.Write(m.EnergyRecall);w.Write(m.Arrived);w.Write(m.Blocked);w.Write((byte)m.Hold);w.Write(m.Speed);w.Write(m.ApproachSpeed);w.Write(m.Reserve);w.Write(m.MovingUnits);w.Write(m.DistanceTravelled);
        }
        static CargoMissionState Read(BinaryReader r)
        {
            Guid world=Id(r),id=Id(r),source=Id(r),target=Id(r);int length=r.ReadInt32();if(length<1||length>512)throw new InvalidDataException("Invalid owner length");
            string owner=new UTF8Encoding(false,true).GetString(Exact(r,length));var destination=Point(r);
            long reserve=r.ReadInt64();var phase=(CargoPhase)r.ReadByte();var hold=(CargoHold)r.ReadByte();bool recall=Bool(r);long handling=r.ReadInt64(),busy=r.ReadInt64();bool observed=Bool(r);var reason=(CargoMissionReturnReason)r.ReadByte();long battery=r.ReadInt64(),revision=r.ReadInt64();
            var cargo=new CargoItem[6];for(int i=0;i<6;i++)if(Bool(r)){int limit=r.ReadInt32(),size=r.ReadInt32();if(size<1||size>CargoRules.MaxItemBytes)throw new InvalidDataException("Invalid saved item length");cargo[i]=new CargoItem(Exact(r,size),1,limit);}
            var position=Point(r);var targetPoint=Point(r);var trail=Points(r);var remaining=Points(r);
            var motion=new CargoMotionState(position,targetPoint,trail,remaining,Bool(r),Bool(r),Bool(r),Bool(r),(CargoHold)r.ReadByte(),r.ReadDouble(),r.ReadDouble(),r.ReadInt64(),r.ReadInt64(),r.ReadDouble());
            return new CargoMissionState(world,id,source,target,owner,destination,reserve,phase,hold,recall,handling,busy,observed,reason,battery,revision,cargo,motion);
        }
        static void Point(BinaryWriter w,CargoPoint p){w.Write(p.X);w.Write(p.Y);w.Write(p.Z);}
        static CargoPoint Point(BinaryReader r){return new CargoPoint(r.ReadDouble(),r.ReadDouble(),r.ReadDouble());}
        static void Points(BinaryWriter w,CargoPoint[] points){w.Write(points.Length);foreach(var p in points)Point(w,p);}
        static CargoPoint[] Points(BinaryReader r){int count=r.ReadInt32();if(count<0||count>4096)throw new InvalidDataException("Invalid waypoint count");var points=new CargoPoint[count];for(int i=0;i<count;i++)points[i]=Point(r);return points;}
        static bool Bool(BinaryReader r){byte value=r.ReadByte();if(value>1)throw new InvalidDataException("Invalid boolean");return value!=0;}
        static Guid Id(BinaryReader r){return new Guid(Exact(r,16));}
        static byte[] Exact(BinaryReader r,int length){var b=r.ReadBytes(length);if(b.Length!=length)throw new EndOfStreamException();return b;}
        static void End(MemoryStream s){if(s.Position!=s.Length)throw new InvalidDataException("Trailing checkpoint data");}
        static byte[] Hash(byte[] bytes){using(var sha=SHA256.Create())return sha.ComputeHash(bytes);}
        static byte[] Frame(byte[] body)
        {
            if(body.Length>MaxBytes-44)throw new InvalidDataException("Checkpoint too large");
            using(var m=new MemoryStream())using(var w=new BinaryWriter(m)){w.Write(Magic);w.Write(body.Length);w.Write(~body.Length);w.Write(body);w.Write(Hash(body));w.Flush();return m.ToArray();}
        }
        static byte[] Unframe(byte[] bytes)
        {
            using(var m=new MemoryStream(bytes,false))using(var r=new BinaryReader(m))
            {if(r.ReadInt32()!=Magic)throw new InvalidDataException("Unknown checkpoint format");int n=r.ReadInt32();if(n<0||n>MaxBytes-44||r.ReadInt32()!=~n||bytes.Length!=n+44)throw new InvalidDataException("Invalid checkpoint frame");var body=Exact(r,n);if(!Hash(body).SequenceEqual(Exact(r,32)))throw new InvalidDataException("Checkpoint checksum failed");return body;}
        }
        static byte[] ReadBounded(string path){using(var s=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){if(s.Length<44||s.Length>MaxBytes)throw new InvalidDataException("Checkpoint file length invalid");using(var r=new BinaryReader(s))return Exact(r,(int)s.Length);}}
        static void DurableCreate(string path,byte[] bytes){using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read)){file.Write(bytes,0,bytes.Length);file.Flush(true);}}
        public void Dispose(){if(disposed)return;disposed=true;lease.Dispose();}
    }
}
