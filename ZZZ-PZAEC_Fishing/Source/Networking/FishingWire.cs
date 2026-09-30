using System;
using System.IO;
using PZAEC.Fishing.Contracts;

namespace PZAEC.Fishing.Networking
{
    public enum FishingMessageKind : byte { Start = 1, StartReply, Input, Snapshot, End }

    // Transport metadata and start/stop messages are F-owned; shared DTOs remain unchanged.
    public sealed class FishingMessage
    {
        public FishingMessageKind Kind;
        public long Serial;
        public long Request;
        public Guid SessionId;
        public Vec3 Target;
        public bool Accepted;
        public FailureReason Reason;
        public NetworkInput Input;
        public NetworkSnapshot Snapshot;
    }

    /// <summary>Explicit, bounded binary format. Never deserializes runtime types or strings.</summary>
    public static class FishingWire
    {
        public const int MaxBytes = 8192;
        public const int MaxEvents = 64;
        const uint Magic = 0x31485346; // FSH1, independent of game package IDs.

        public static byte[] Encode(FishingMessage message)
        {
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream))
            {
                w.Write(Magic); w.Write(FishingContract.Version); w.Write((byte)message.Kind); w.Write(message.Serial);
                switch (message.Kind)
                {
                    case FishingMessageKind.Start: w.Write(message.Request); Vector(w, message.Target); break;
                    case FishingMessageKind.StartReply:
                        w.Write(message.Request); w.Write(message.Accepted); GuidValue(w, message.SessionId); w.Write((byte)message.Reason); break;
                    case FishingMessageKind.Input: WriteInput(w, message.Input); break;
                    case FishingMessageKind.Snapshot: WriteSnapshot(w, message.Snapshot); break;
                    case FishingMessageKind.End: GuidValue(w, message.SessionId); w.Write((byte)message.Reason); break;
                    default: throw new InvalidDataException("Unknown fishing message");
                }
                if (stream.Length > MaxBytes) throw new InvalidDataException("Fishing message too large");
                var bytes = stream.ToArray();
                FishingMessage check;
                if (!TryDecode(bytes, out check)) throw new InvalidDataException("Invalid fishing message fields");
                return bytes;
            }
        }

        public static bool TryDecode(byte[] bytes, out FishingMessage value)
        {
            value = null;
            if (bytes == null || bytes.Length < 17 || bytes.Length > MaxBytes) return false;
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var r = new BinaryReader(stream))
                {
                    if (r.ReadUInt32() != Magic || r.ReadInt32() != FishingContract.Version) return false;
                    var m = new FishingMessage { Kind = (FishingMessageKind)r.ReadByte(), Serial = r.ReadInt64() };
                    if (m.Serial <= 0) return false;
                    switch (m.Kind)
                    {
                        case FishingMessageKind.Start:
                            m.Request = r.ReadInt64(); m.Target = Vector(r);
                            if (m.Request <= 0 || !Position(m.Target)) return false;
                            break;
                        case FishingMessageKind.StartReply:
                            m.Request = r.ReadInt64(); m.Accepted = Bool(r); m.SessionId = GuidValue(r); m.Reason = (FailureReason)r.ReadByte();
                            if (m.Request <= 0 || !Reason(m.Reason) || (m.Accepted ? m.SessionId == Guid.Empty : m.SessionId != Guid.Empty)) return false;
                            break;
                        case FishingMessageKind.Input:
                            m.Input = ReadInput(r); if (!ValidInput(m.Input)) return false; break;
                        case FishingMessageKind.Snapshot:
                            m.Snapshot = ReadSnapshot(r); if (!ValidSnapshot(m.Snapshot)) return false; break;
                        case FishingMessageKind.End:
                            m.SessionId = GuidValue(r); m.Reason = (FailureReason)r.ReadByte();
                            if (m.SessionId == Guid.Empty || !Reason(m.Reason)) return false;
                            break;
                        default: return false;
                    }
                    if (stream.Position != stream.Length) return false;
                    value = m; return true;
                }
            }
            catch (InvalidDataException) { return false; }
            catch (IOException) { return false; }
            catch (ArgumentException) { return false; }
        }

        static bool Bool(BinaryReader r) { byte b = r.ReadByte(); if (b > 1) throw new InvalidDataException("Invalid bool"); return b == 1; }
        static void GuidValue(BinaryWriter w, Guid g) { w.Write(g.ToByteArray()); }
        static Guid GuidValue(BinaryReader r) { var b = r.ReadBytes(16); if (b.Length != 16) throw new EndOfStreamException(); return new Guid(b); }
        static void Vector(BinaryWriter w, Vec3 v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
        static Vec3 Vector(BinaryReader r) { return new Vec3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); }
        static void Rod(BinaryWriter w, RodPose p)
        { Vector(w,p.Root); Vector(w,p.Tip); Vector(w,p.Forward); Vector(w,p.Right); w.Write(p.PitchRadians); w.Write(p.YawRadians); }
        static RodPose Rod(BinaryReader r)
        { return new RodPose {Root=Vector(r),Tip=Vector(r),Forward=Vector(r),Right=Vector(r),PitchRadians=r.ReadSingle(),YawRadians=r.ReadSingle()}; }

        static void WriteInput(BinaryWriter w, NetworkInput n)
        {
            w.Write(n.ContractVersion); GuidValue(w,n.SessionId); w.Write(n.Sequence); w.Write(n.ClientTick);
            var x=n.Input; w.Write(x.Sequence); w.Write(x.SampleTimeSeconds); w.Write(x.DurationSeconds);
            w.Write(x.MouseRightDelta); w.Write(x.MouseBackDelta); w.Write(x.MoveRight); w.Write(x.MoveForward); w.Write(x.DragAdjustDelta);
            byte bits=(byte)((x.CastPressed?1:0)|(x.StrikePressed?2:0)|(x.ReelHeld?4:0)|(x.FreeLookHeld?8:0)|(x.RecenterHeld?16:0)|(x.CancelPressed?32:0)|(x.InputAllowed?64:0));
            w.Write(bits);
        }
        static NetworkInput ReadInput(BinaryReader r)
        {
            var n=new NetworkInput {ContractVersion=r.ReadInt32(),SessionId=GuidValue(r),Sequence=r.ReadInt64(),ClientTick=r.ReadInt64()};
            var x=new RawInputFrame {Sequence=r.ReadInt64(),SampleTimeSeconds=r.ReadDouble(),DurationSeconds=r.ReadSingle(),
                MouseRightDelta=r.ReadSingle(),MouseBackDelta=r.ReadSingle(),MoveRight=r.ReadSingle(),MoveForward=r.ReadSingle(),DragAdjustDelta=r.ReadSingle()};
            byte b=r.ReadByte(); if(b>127)throw new InvalidDataException("Invalid input flags");
            x.CastPressed=(b&1)!=0;x.StrikePressed=(b&2)!=0;x.ReelHeld=(b&4)!=0;x.FreeLookHeld=(b&8)!=0;x.RecenterHeld=(b&16)!=0;x.CancelPressed=(b&32)!=0;x.InputAllowed=(b&64)!=0;
            n.Input=x;return n;
        }
        static void WriteSnapshot(BinaryWriter w, NetworkSnapshot n)
        {
            w.Write(n.ContractVersion);w.Write(n.LastAcceptedInputSequence);var s=n.Snapshot;
            GuidValue(w,s.SessionId);w.Write(s.Tick);w.Write(s.LastInputSequence);w.Write(s.TimeSeconds);
            w.Write((byte)s.Phase);w.Write((byte)s.FishBehavior);w.Write((byte)s.Authority);
            Vector(w,s.FishPosition);Vector(w,s.FishVelocity);Vector(w,s.FishForward);
            w.Write(s.FishMassKg);w.Write(s.FishStamina01);w.Write(s.FishBurstForceNewtons);w.Write(s.BurstIndex);
            w.Write(s.LineLengthMeters);w.Write(s.LineExtensionMeters);w.Write(s.LineTensionNewtons);w.Write(s.LineDamage01);
            Rod(w,s.Rod);Vector(w,s.RodLoadNewtons);Vector(w,s.FloatPosition);Vector(w,s.FloatUp);
            w.Write(s.FloatSubmerged01);w.Write(s.HookQuality01);w.Write(s.SlackSeconds);w.Write(s.Drag01);w.Write((byte)s.Failure);
            int count=n.Events==null?0:n.Events.Length;if(count>MaxEvents)throw new InvalidDataException("Too many events");w.Write((byte)count);
            for(int i=0;i<count;i++) {var e=n.Events[i];GuidValue(w,e.SessionId);w.Write(e.Sequence);w.Write(e.Tick);w.Write((byte)e.Kind);w.Write((byte)e.Reason);Vector(w,e.Position);w.Write(e.Intensity01);}
        }
        static NetworkSnapshot ReadSnapshot(BinaryReader r)
        {
            var n=new NetworkSnapshot {ContractVersion=r.ReadInt32(),LastAcceptedInputSequence=r.ReadInt64()};
            var s=new FishingSnapshot {SessionId=GuidValue(r),Tick=r.ReadInt64(),LastInputSequence=r.ReadInt64(),TimeSeconds=r.ReadDouble(),
                Phase=(FishingPhase)r.ReadByte(),FishBehavior=(FishBehavior)r.ReadByte(),Authority=(AuthorityMode)r.ReadByte(),
                FishPosition=Vector(r),FishVelocity=Vector(r),FishForward=Vector(r),FishMassKg=r.ReadSingle(),FishStamina01=r.ReadSingle(),FishBurstForceNewtons=r.ReadSingle(),BurstIndex=r.ReadInt32(),
                LineLengthMeters=r.ReadSingle(),LineExtensionMeters=r.ReadSingle(),LineTensionNewtons=r.ReadSingle(),LineDamage01=r.ReadSingle(),Rod=Rod(r),
                RodLoadNewtons=Vector(r),FloatPosition=Vector(r),FloatUp=Vector(r),FloatSubmerged01=r.ReadSingle(),HookQuality01=r.ReadSingle(),SlackSeconds=r.ReadSingle(),Drag01=r.ReadSingle(),Failure=(FailureReason)r.ReadByte()};
            n.Snapshot=s;int count=r.ReadByte();if(count>MaxEvents)throw new InvalidDataException("Too many events");
            n.Events=new FishingEvent[count];for(int i=0;i<count;i++)n.Events[i]=new FishingEvent {SessionId=GuidValue(r),Sequence=r.ReadInt64(),Tick=r.ReadInt64(),Kind=(FishingEventKind)r.ReadByte(),Reason=(FailureReason)r.ReadByte(),Position=Vector(r),Intensity01=r.ReadSingle()};
            return n;
        }

        public static bool ValidInput(NetworkInput n)
        {
            var x=n.Input;
            return n.ContractVersion==FishingContract.Version && n.SessionId!=Guid.Empty && n.Sequence>0 && n.Sequence==x.Sequence && n.ClientTick>=0 &&
                Finite(x.SampleTimeSeconds) && x.SampleTimeSeconds>=0 && Range(x.DurationSeconds,0,.25f) &&
                Range(x.MouseRightDelta,-256,256) && Range(x.MouseBackDelta,-256,256) && Range(x.MoveRight,-1,1) && Range(x.MoveForward,-1,1) && Range(x.DragAdjustDelta,-1,1);
        }
        public static bool ValidSnapshot(NetworkSnapshot n)
        {
            var s=n.Snapshot;
            if(n.ContractVersion!=FishingContract.Version || n.LastAcceptedInputSequence<0 || s.SessionId==Guid.Empty || s.Tick<0 || s.LastInputSequence<0 ||
                s.LastInputSequence>n.LastAcceptedInputSequence || !Finite(s.TimeSeconds) || s.TimeSeconds<0 ||
                (int)s.Phase<0 || (int)s.Phase>(int)FishingPhase.HookLost || (int)s.FishBehavior<0 || (int)s.FishBehavior>(int)FishBehavior.Exhausted ||
                (s.Authority!=AuthorityMode.Server && s.Authority!=AuthorityMode.Standalone) || !Reason(s.Failure) ||
                !Position(s.FishPosition) || !Position(s.FloatPosition) || !Position(s.Rod.Root) || !Position(s.Rod.Tip) ||
                !s.FishVelocity.IsFinite || !s.FishForward.IsFinite || !s.RodLoadNewtons.IsFinite || !s.FloatUp.IsFinite || !s.Rod.Forward.IsFinite || !s.Rod.Right.IsFinite ||
                !Scalar.IsFinite(s.Rod.PitchRadians) || !Scalar.IsFinite(s.Rod.YawRadians) ||
                !Range(s.FishMassKg,0,100000) || !Range(s.FishStamina01,0,1) || !Range(s.FishBurstForceNewtons,0,1e8f) || s.BurstIndex<0 ||
                !Range(s.LineLengthMeters,0,100000) || !Range(s.LineExtensionMeters,0,100000) || !Range(s.LineTensionNewtons,0,1e8f) || !Range(s.LineDamage01,0,1) ||
                !Range(s.FloatSubmerged01,0,1) || !Range(s.HookQuality01,0,1) || !Range(s.SlackSeconds,0,1e8f) || !Range(s.Drag01,0,1)) return false;
            int count=n.Events==null?0:n.Events.Length;if(count>MaxEvents)return false;long sequence=0;
            for(int i=0;i<count;i++)
            {
                var e=n.Events[i];
                if(e.SessionId!=s.SessionId || e.Sequence<=sequence || e.Tick<0 || e.Tick>s.Tick || (int)e.Kind<0 || (int)e.Kind>(int)FishingEventKind.Cancelled || !Reason(e.Reason) || !Position(e.Position) || !Range(e.Intensity01,0,1))return false;
                sequence=e.Sequence;
            }
            return true;
        }
        static bool Position(Vec3 p) { return p.IsFinite && Math.Abs(p.X)<=1e7f && Math.Abs(p.Y)<=1e7f && Math.Abs(p.Z)<=1e7f; }
        static bool Reason(FailureReason r) {return (int)r>=0 && (int)r<=(int)FailureReason.ModuleError;}
        static bool Range(float v,float min,float max) {return Scalar.IsFinite(v)&&v>=min&&v<=max;}
        internal static bool Finite(double x) {return !double.IsNaN(x)&&!double.IsInfinity(x);}
    }
}
