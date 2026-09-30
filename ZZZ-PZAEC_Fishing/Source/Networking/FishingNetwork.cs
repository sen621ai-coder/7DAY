using System;
using System.Collections.Generic;
using PZAEC.Fishing.Contracts;

namespace PZAEC.Fishing.Networking
{
    // Reliable, ordered transport; critical edges and terminal states must not use unreliable UDP.
    public interface IFishingTransport
    {
        void SendToServer(byte[] payload);
        void SendToPeer(string connectionId, byte[] payload);
    }

    public sealed class FishingNetworkOptions
    {
        public int MaxPeers=64, MaxClientSessions=128;
        public double InputTimeoutSeconds=3, SnapshotTimeoutSeconds=5, SnapshotIntervalSeconds=.1;
        public double MessagesPerSecond=120, MessageBurst=180;
        public int MaxInputSequenceJump=4096, MaxClientLeadTicks=120, MaxClientLagTicks=600;
        internal FishingNetworkOptions Copy()
        {
            if(MaxPeers<1 || MaxPeers>1024 || MaxClientSessions<1 || MaxClientSessions>4096 ||
                !Good(InputTimeoutSeconds,.25,60) || !Good(SnapshotTimeoutSeconds,.5,120) || !Good(SnapshotIntervalSeconds,.016,1) ||
                !Good(MessagesPerSecond,1,1000) || !Good(MessageBurst,1,2000) || MaxInputSequenceJump<1 || MaxInputSequenceJump>100000 ||
                MaxClientLeadTicks<0 || MaxClientLeadTicks>3600 || MaxClientLagTicks<0 || MaxClientLagTicks>36000)
                throw new ArgumentException("Invalid fishing network options");
            return (FishingNetworkOptions)MemberwiseClone();
        }
        static bool Good(double x,double a,double b) {return FishingWire.Finite(x)&&x>=a&&x<=b;}
    }

    /// <summary>
    /// Main-thread network coordinator. A supplies authenticated connections, authoritative router,
    /// interest checks, lifecycle calls and real server ticks. No inventory or rendering dependency.
    /// </summary>
    public sealed class FishingNetwork : IFishingNetwork
    {
        sealed class Peer
        {
            public string Connection, Player;
            public int Entity;
            public long ReceivedSerial, SentSerial, LastRequest;
            public FishingMessage LastReply;
            public double Tokens, TokenTime, StartTokens=3, StartTime;
            public readonly HashSet<Guid> Watching=new HashSet<Guid>();
            public Session Active;
        }
        sealed class Session
        {
            public Guid Id;
            public Peer Owner;
            public long LastInput, LastClientTick, LastEvent, Tick;
            public double LastInputTime, LastPublishTime, LastSampleTime;
            public bool HasSnapshot, Dirty;
            public NetworkSnapshot Snapshot;
            public readonly List<FishingEvent> Events=new List<FishingEvent>();
        }
        readonly IAuthoritySessionRouter router;
        readonly IFishingTransport transport;
        readonly FishingNetworkOptions options;
        readonly Dictionary<string,Peer> peers=new Dictionary<string,Peer>(StringComparer.Ordinal);
        readonly Dictionary<Guid,Session> sessions=new Dictionary<Guid,Session>();
        readonly Dictionary<Guid,ClientSnapshotState> cache=new Dictionary<Guid,ClientSnapshotState>();
        readonly Func<string,string,bool> canObserve;
        readonly List<string> scratchPeers=new List<string>();
        readonly List<Guid> scratchSessions=new List<Guid>();
        bool running;
        AuthorityMode mode;
        double now;
        long clientSerial, serverSerial, requestSerial, pendingRequest;
        double pendingAt, localAcceptedAt;
        public Guid LocalSessionId {get;private set;}
        public long RejectedMessages {get;private set;}
        public long AcceptedInputs {get;private set;}
        public int ActiveSessionCount {get {return sessions.Count;}}
        public int CachedSessionCount {get {return cache.Count;}}
        public event Action<long,bool,Guid,FailureReason> StartResult;
        // A must Clear D and Release C here; this never awards or revokes inventory.
        public event Action<Guid,FailureReason> SessionEnded;

        public FishingNetwork(IAuthoritySessionRouter router,IFishingTransport transport,
            Func<string,string,bool> canObserve=null,FishingNetworkOptions options=null)
        {
            this.router=router??throw new ArgumentNullException(nameof(router));
            this.transport=transport??throw new ArgumentNullException(nameof(transport));
            this.canObserve=canObserve??((owner,viewer)=>false);
            this.options=(options??new FishingNetworkOptions()).Copy();
        }
        bool Server {get {return mode==AuthorityMode.Server || mode==AuthorityMode.Standalone;}}
        public void Start(AuthorityMode value)
        {
            if(running)throw new InvalidOperationException("Stop fishing network before starting again");
            if(!Enum.IsDefined(typeof(AuthorityMode),value))throw new ArgumentOutOfRangeException(nameof(value));
            mode=value;running=true;now=0;clientSerial=serverSerial=requestSerial=pendingRequest=0;
            LocalSessionId=Guid.Empty;RejectedMessages=AcceptedInputs=0;
        }
        // Only A/native transport may register identities. Never call this from payload values.
        // connectionId must include connection generation, not just an entity ID.
        public bool RegisterPeer(string connectionId,string authenticatedPlayerId,int entityId)
        {
            if(!running || !Server || string.IsNullOrWhiteSpace(connectionId) || string.IsNullOrWhiteSpace(authenticatedPlayerId) || entityId<0)return false;
            Peer existing;
            if(peers.TryGetValue(connectionId,out existing))return existing.Player==authenticatedPlayerId && existing.Entity==entityId;
            // A reconnect supersedes the old generation and cancels its fishing session.
            scratchPeers.Clear();foreach(var pair in peers)if(pair.Value.Player==authenticatedPlayerId)scratchPeers.Add(pair.Key);
            foreach(var key in scratchPeers)DisconnectPeer(key);
            if(peers.Count>=options.MaxPeers)return false;
            peers.Add(connectionId,new Peer {Connection=connectionId,Player=authenticatedPlayerId,Entity=entityId,Tokens=options.MessageBurst,TokenTime=now,StartTime=now});
            return true;
        }
        public void DisconnectPeer(string connectionId)
        {
            Peer p;if(!peers.TryGetValue(connectionId,out p))return;
            peers.Remove(connectionId); // Do not send to a closed connection.
            if(p.Active!=null)Close(p.Active,FailureReason.Disconnected,true);
        }
        public long RequestStart(Vec3 target)
        {
            if(!running || Server || mode==AuthorityMode.Observer || pendingRequest!=0 || LocalSessionId!=Guid.Empty)
                throw new InvalidOperationException("Cannot request a fishing session now");
            long request=checked(++requestSerial);
            var packet=FishingWire.Encode(new FishingMessage {Kind=FishingMessageKind.Start,Serial=checked(++clientSerial),Request=request,Target=target});
            pendingRequest=request;pendingAt=now;
            transport.SendToServer(packet);return request;
        }
        public void SendInput(NetworkInput input)
        {
            if(!running || Server || mode==AuthorityMode.Observer || input.SessionId!=LocalSessionId || LocalSessionId==Guid.Empty || !FishingWire.ValidInput(input))
                throw new InvalidOperationException("Input requires an accepted local session");
            transport.SendToServer(FishingWire.Encode(new FishingMessage {Kind=FishingMessageKind.Input,Serial=checked(++clientSerial),Input=input}));
        }
        // Hosting player uses this same authenticated receive path via a loopback transport.
        public bool ReceiveFromPeer(string connectionId,byte[] payload)
        {
            Peer p;
            if(!running || !Server || !peers.TryGetValue(connectionId,out p))return Reject();
            // Charge malformed packets too, before allocating message/event objects.
            p.Tokens=Math.Min(options.MessageBurst,p.Tokens+Math.Max(0,now-p.TokenTime)*options.MessagesPerSecond);p.TokenTime=now;
            if(p.Tokens<1)return Reject();p.Tokens-=1;
            FishingMessage m;
            if(!FishingWire.TryDecode(payload,out m) || m.Serial<=p.ReceivedSerial ||
                (m.Kind!=FishingMessageKind.Start && m.Kind!=FishingMessageKind.Input))return Reject();
            p.ReceivedSerial=m.Serial;
            if(m.Kind==FishingMessageKind.Start)return ReceiveStart(p,m);
            var s=p.Active;var n=m.Input;
            if(s==null || n.SessionId!=s.Id || n.Sequence<=s.LastInput || n.Sequence-s.LastInput>options.MaxInputSequenceJump ||
                n.ClientTick<s.LastClientTick || n.ClientTick>s.Tick+options.MaxClientLeadTicks || n.ClientTick<Math.Max(0,s.Tick-options.MaxClientLagTicks) ||
                n.Input.SampleTimeSeconds<s.LastSampleTime)return Reject();
            double clientSampleTime=n.Input.SampleTimeSeconds;
            // Client time/duration never drives simulation. A queues input and advances at server time.
            n.Input.SampleTimeSeconds=now;
            n.Input.DurationSeconds=FishingContract.FixedStepSeconds;
            if(!router.AcceptInput(p.Player,n))return Reject();
            s.LastInput=n.Sequence;s.LastClientTick=n.ClientTick;s.LastInputTime=now;s.LastSampleTime=clientSampleTime;
            AcceptedInputs++;
            if(n.Input.CancelPressed || !n.Input.InputAllowed)
                Close(s,n.Input.CancelPressed?FailureReason.CancelledByPlayer:FailureReason.MenuOpened,true);
            return true;
        }
        bool ReceiveStart(Peer p,FishingMessage m)
        {
            if(m.Request<p.LastRequest)return Reject();
            if(m.Request==p.LastRequest)
            {
                // Only retransmit an accepted reply while its session still exists.
                if(p.LastReply!=null && (!p.LastReply.Accepted || (p.Active!=null && p.Active.Id==p.LastReply.SessionId)))Send(p,p.LastReply);
                return true;
            }
            p.StartTokens=Math.Min(3,p.StartTokens+Math.Max(0,now-p.StartTime)*2);p.StartTime=now;
            bool allowed=p.StartTokens>=1;p.StartTokens=Math.Max(0,p.StartTokens-1);
            p.LastRequest=m.Request;
            Guid id=Guid.Empty;
            bool accepted=allowed && p.Active==null && router.TryStart(p.Player,p.Entity,m.Target,out id);
            if(accepted && (id==Guid.Empty || sessions.ContainsKey(id)))
            {router.CancelPlayer(p.Player,FailureReason.ModuleError);accepted=false;}
            if(accepted)
            {
                var s=new Session {Id=id,Owner=p,LastInputTime=now,LastPublishTime=now};
                sessions.Add(id,s);p.Active=s;
            }
            p.LastReply=new FishingMessage {Kind=FishingMessageKind.StartReply,Request=m.Request,Accepted=accepted,
                SessionId=accepted?id:Guid.Empty,Reason=accepted?FailureReason.None:FailureReason.InvalidInput};
            Send(p,p.LastReply);return accepted;
        }
        public void Publish(NetworkSnapshot snapshot)
        {
            if(!running || !Server)throw new InvalidOperationException("Only authority may publish");
            Session s;
            if(!sessions.TryGetValue(snapshot.Snapshot.SessionId,out s))throw new InvalidOperationException("Unknown authority session");
            snapshot.LastAcceptedInputSequence=s.LastInput;
            if(!FishingWire.ValidSnapshot(snapshot) || snapshot.Snapshot.Tick<s.Tick ||
                (s.HasSnapshot && snapshot.Snapshot.TimeSeconds<s.Snapshot.Snapshot.TimeSeconds))throw new ArgumentException("Invalid authority snapshot");
            if(s.HasSnapshot && snapshot.Snapshot.Tick==s.Tick && !snapshot.Snapshot.IsTerminal)return;
            var events=snapshot.Events??new FishingEvent[0];
            int newEvents=0;foreach(var e in events)if(e.Sequence>s.LastEvent)newEvents++;
            if(s.Events.Count+newEvents>FishingWire.MaxEvents)throw new InvalidOperationException("Publish/flush event budget exceeded; do not drop authoritative events");
            foreach(var e in events)
            {
                if(e.Sequence<=s.LastEvent)continue;
                s.Events.Add(e);s.LastEvent=e.Sequence;
            }
            // Never retain the caller-owned event array.
            snapshot.Events=null;s.Snapshot=snapshot;s.HasSnapshot=true;s.Dirty=true;s.Tick=snapshot.Snapshot.Tick;
            if(snapshot.Snapshot.IsTerminal)
            {Flush(s);Close(s,snapshot.Snapshot.Failure,false);}
        }
        void Flush(Session s)
        {
            if(!s.HasSnapshot || !s.Dirty)return;
            var n=s.Snapshot;n.Events=s.Events.ToArray();
            foreach(var p in peers.Values)
            {
                bool interested=p==s.Owner || canObserve(s.Owner.Player,p.Player);
                if(interested)
                {
                    Send(p,new FishingMessage {Kind=FishingMessageKind.Snapshot,Snapshot=n});p.Watching.Add(s.Id);
                }
                else if(p.Watching.Remove(s.Id))Send(p,new FishingMessage {Kind=FishingMessageKind.End,SessionId=s.Id,Reason=FailureReason.TooFar});
            }
            s.Events.Clear();s.Dirty=false;s.LastPublishTime=now;
        }
        void Close(Session s,FailureReason reason,bool cancelRouter)
        {
            if(!sessions.Remove(s.Id))return;
            s.Owner.Active=null;
            try {if(cancelRouter)router.CancelPlayer(s.Owner.Player,reason);}
            finally
            {
                foreach(var p in peers.Values)
                {
                    bool watching=p.Watching.Remove(s.Id);
                    if(watching || p==s.Owner)Send(p,new FishingMessage {Kind=FishingMessageKind.End,SessionId=s.Id,Reason=reason});
                }
            }
        }
        void Send(Peer p,FishingMessage m)
        {m.Serial=checked(++p.SentSerial);transport.SendToPeer(p.Connection,FishingWire.Encode(m));}
        bool Reject() {RejectedMessages++;return false;}

        // This entry point may ONLY be invoked for the authenticated server connection.
        public bool ReceiveFromServer(byte[] payload)
        {
            FishingMessage m;
            if(!running || Server || !FishingWire.TryDecode(payload,out m) || m.Serial<=serverSerial ||
                (m.Kind!=FishingMessageKind.StartReply && m.Kind!=FishingMessageKind.Snapshot && m.Kind!=FishingMessageKind.End))return Reject();
            serverSerial=m.Serial;
            if(m.Kind==FishingMessageKind.StartReply)
            {
                if(m.Request!=pendingRequest)return Reject();
                pendingRequest=0;
                if(m.Accepted){LocalSessionId=m.SessionId;localAcceptedAt=now;}
                StartResult?.Invoke(m.Request,m.Accepted,m.SessionId,m.Reason);return true;
            }
            if(m.Kind==FishingMessageKind.End)
            {EndLocal(m.SessionId,m.Reason);return true;}
            var id=m.Snapshot.Snapshot.SessionId;
            ClientSnapshotState state;
            if(!cache.TryGetValue(id,out state))
            {
                if(cache.Count>=options.MaxClientSessions)return Reject();
                state=new ClientSnapshotState();cache.Add(id,state);
            }
            NetworkSnapshot clean;
            if(!state.Accept(m.Snapshot,now,out clean))return Reject();
            router.ApplySnapshot(clean);return true;
        }
        public bool TryGetSnapshots(Guid id,out FishingSnapshot previous,out FishingSnapshot current)
        {
            ClientSnapshotState s;
            if(cache.TryGetValue(id,out s)){previous=s.Previous;current=s.Current;return true;}
            previous=current=default(FishingSnapshot);return false;
        }
        void EndLocal(Guid id,FailureReason reason)
        {
            bool known=cache.Remove(id);
            if(LocalSessionId==id){LocalSessionId=Guid.Empty;known=true;}
            if(known)SessionEnded?.Invoke(id,reason);
        }
        public void Tick(double nowSeconds)
        {
            if(!running)return;
            if(!FishingWire.Finite(nowSeconds) || nowSeconds<now)throw new ArgumentOutOfRangeException(nameof(nowSeconds),"Use monotonic server time");
            now=nowSeconds;
            if(Server)
            {
                scratchSessions.Clear();foreach(var s in sessions.Values)if(now-s.LastInputTime>options.InputTimeoutSeconds)scratchSessions.Add(s.Id);
                foreach(var id in scratchSessions)Close(sessions[id],FailureReason.Timeout,true);
                foreach(var s in sessions.Values)if(now-s.LastPublishTime>=options.SnapshotIntervalSeconds)Flush(s);
            }
            else
            {
                if(pendingRequest!=0 && now-pendingAt>options.SnapshotTimeoutSeconds)
                {long request=pendingRequest;pendingRequest=0;StartResult?.Invoke(request,false,Guid.Empty,FailureReason.Timeout);}
                scratchSessions.Clear();foreach(var pair in cache)if(now-pair.Value.ReceivedAt>options.SnapshotTimeoutSeconds)scratchSessions.Add(pair.Key);
                foreach(var id in scratchSessions)EndLocal(id,FailureReason.Timeout);
                if(LocalSessionId!=Guid.Empty && !cache.ContainsKey(LocalSessionId) && now-localAcceptedAt>options.SnapshotTimeoutSeconds)
                    EndLocal(LocalSessionId,FailureReason.Timeout);
            }
        }
        public void Stop()
        {
            if(!running)return;
            if(Server)
            {
                scratchPeers.Clear();scratchPeers.AddRange(peers.Keys);
                foreach(var p in scratchPeers)DisconnectPeer(p);
            }
            else
            {
                scratchSessions.Clear();scratchSessions.AddRange(cache.Keys);
                if(LocalSessionId!=Guid.Empty && !cache.ContainsKey(LocalSessionId))scratchSessions.Add(LocalSessionId);
                foreach(var id in scratchSessions)EndLocal(id,FailureReason.Disconnected);
            }
            peers.Clear();sessions.Clear();cache.Clear();pendingRequest=0;LocalSessionId=Guid.Empty;running=false;
        }
    }

    // Reconciliation is presentation-only. No physical prediction or tension correction feeds B.
    // Thus a late authority correction cannot manufacture a local line break or award a fish.
    public sealed class ClientSnapshotState
    {
        public FishingSnapshot Previous {get;private set;}
        public FishingSnapshot Current {get;private set;}
        public double ReceivedAt {get;private set;}
        long lastEvent,lastAck;
        bool initialized;
        public bool Accept(NetworkSnapshot value,double now,out NetworkSnapshot clean)
        {
            clean=default(NetworkSnapshot);
            if(!FishingWire.Finite(now) || !FishingWire.ValidSnapshot(value) ||
                (initialized && (value.Snapshot.SessionId!=Current.SessionId || value.Snapshot.Tick<Current.Tick ||
                (value.Snapshot.Tick==Current.Tick && !value.Snapshot.IsTerminal) ||
                value.LastAcceptedInputSequence<lastAck || value.Snapshot.TimeSeconds<Current.TimeSeconds || Current.IsTerminal)))return false;
            var events=new List<FishingEvent>();
            if(value.Events!=null)foreach(var e in value.Events)if(e.Sequence>lastEvent){events.Add(e);lastEvent=e.Sequence;}
            Previous=initialized?Current:value.Snapshot;Current=value.Snapshot;ReceivedAt=now;lastAck=value.LastAcceptedInputSequence;initialized=true;
            clean=value;clean.Events=events.ToArray();return true;
        }
    }
}
