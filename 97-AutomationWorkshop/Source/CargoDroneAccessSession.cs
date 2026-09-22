using System;
using System.Collections.Generic;
using System.Linq;

namespace YFAutomation.CargoDrones
{
    public enum CargoSessionDelivery{Applied,Buffered,Duplicate,Rejected}
    // Ordered inventory envelopes for one server-granted native access session.
    // The native bridge must wrap ALL writes before allowing this session to open.
    // Connection identity is supplied by the authenticated transport, never wire data.
    public sealed class CargoAccessSession
    {
        const int MaxBuffered=64,MaxBufferedBytes=4*1024*1024;
        readonly SortedDictionary<long,byte[]> waiting=new SortedDictionary<long,byte[]>();
        readonly Action<byte[]> apply;
        int bufferedBytes;
        long? closeSequence;
        bool applying;
        public readonly Guid Connection,Token,Endpoint,Incarnation;
        public long AppliedSequence{get;private set;}
        public bool Faulted{get;private set;}
        public bool Drained{get{return !Faulted&&!applying&&closeSequence.HasValue&&AppliedSequence==closeSequence.Value&&waiting.Count==0;}}
        public int BufferedCount{get{return waiting.Count;}}
        public CargoAccessSession(Guid connection,Guid token,Guid endpoint,Guid incarnation,Action<byte[]> apply)
        {
            if(connection==Guid.Empty||token==Guid.Empty||endpoint==Guid.Empty||incarnation==Guid.Empty||apply==null)throw new ArgumentException("Invalid access session");
            Connection=connection;Token=token;Endpoint=endpoint;Incarnation=incarnation;this.apply=apply;
        }
        bool Authorized(Guid connection,Guid token){return !Faulted&&Connection==connection&&Token==token;}
        public CargoSessionDelivery Receive(Guid connection,Guid token,long sequence,byte[] payload)
        {
            if(!Authorized(connection,token))return CargoSessionDelivery.Rejected;
            if(applying||sequence<=0||payload==null||payload.Length==0||payload.Length>CargoRules.MaxRecordBytes||closeSequence.HasValue&&sequence>closeSequence.Value)return Fault();
            if(sequence<=AppliedSequence)return CargoSessionDelivery.Duplicate;
            byte[] existing;
            if(waiting.TryGetValue(sequence,out existing))return existing.SequenceEqual(payload)?CargoSessionDelivery.Duplicate:Fault();
            if(waiting.Count>=MaxBuffered||payload.Length>MaxBufferedBytes-bufferedBytes||sequence-AppliedSequence>MaxBuffered)return Fault();
            waiting.Add(sequence,(byte[])payload.Clone());bufferedBytes+=payload.Length;
            while(AppliedSequence<long.MaxValue&&waiting.TryGetValue(AppliedSequence+1,out existing))
            {
                applying=true;
                try{apply((byte[])existing.Clone());}
                catch{Fault();throw;}
                finally{applying=false;}
                // A reentrant call must never make this session eligible for cargo.
                if(Faulted)return CargoSessionDelivery.Rejected;
                waiting.Remove(AppliedSequence+1);bufferedBytes-=existing.Length;AppliedSequence++;
            }
            return sequence<=AppliedSequence?CargoSessionDelivery.Applied:CargoSessionDelivery.Buffered;
        }
        public bool Close(Guid connection,Guid token,long finalSequence)
        {
            if(!Authorized(connection,token))return false;
            if(applying||finalSequence<AppliedSequence||finalSequence<0||finalSequence-AppliedSequence>MaxBuffered||closeSequence.HasValue&&closeSequence.Value!=finalSequence||waiting.Keys.Any(s=>s>finalSequence)){Fault();return false;}
            closeSequence=finalSequence;return true;
        }
        public bool CanHandOff(bool nativeAccessLocked,Guid currentIncarnation)
        {return Drained&&!nativeAccessLocked&&currentIncarnation==Incarnation;}
        public void Disconnect(){Fault();}
        CargoSessionDelivery Fault(){Faulted=true;waiting.Clear();bufferedBytes=0;return CargoSessionDelivery.Rejected;}
    }
}
