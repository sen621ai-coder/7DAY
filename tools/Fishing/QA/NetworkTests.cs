using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using PZAEC.Fishing.Contracts;
using PZAEC.Fishing.Networking;

internal static class NetworkTests
{
    public static int Checks;
    public static void Check(bool value,string name)
    {if(!value)throw new Exception(name);Checks++;Console.WriteLine("PASS "+name);}
    static void Throws(Action action,string name)
    {bool failed=false;try{action();}catch(ArgumentException){failed=true;}catch(InvalidOperationException){failed=true;}catch(System.IO.InvalidDataException){failed=true;}Check(failed,name);}
    internal sealed class Router : IAuthoritySessionRouter
    {
        public int Starts,Cancels,Inputs,Applies;
        public string LastPlayer;
        public int LastEntity;
        public Guid Next=Guid.NewGuid();
        public bool Allow=true;
        public NetworkInput Input;
        public NetworkSnapshot Snapshot;
        public FailureReason CancelReason;
        public bool TryStart(string player,int entity,Vec3 target,out Guid id)
        {Starts++;LastPlayer=player;LastEntity=entity;id=Next;return Allow;}
        public bool AcceptInput(string player,NetworkInput input){Inputs++;LastPlayer=player;Input=input;return Allow;}
        public void CancelPlayer(string player,FailureReason reason){Cancels++;LastPlayer=player;CancelReason=reason;}
        public void ApplySnapshot(NetworkSnapshot value){Applies++;Snapshot=value;}
    }
    internal sealed class Transport : IFishingTransport
    {
        public readonly List<byte[]> ToServer=new List<byte[]>();
        public readonly List<Tuple<string,byte[]>> ToPeers=new List<Tuple<string,byte[]>>();
        public void SendToServer(byte[] p){ToServer.Add((byte[])p.Clone());}
        public void SendToPeer(string id,byte[] p){ToPeers.Add(Tuple.Create(id,(byte[])p.Clone()));}
    }
    sealed class CountingTransport : IFishingTransport
    {
        public long Bytes,Messages;
        public void SendToServer(byte[] p){Bytes+=p.Length;Messages++;}
        public void SendToPeer(string id,byte[] p){Bytes+=p.Length;Messages++;}
    }
    internal static byte[] Start(long serial=1,long request=1)
    {return FishingWire.Encode(new FishingMessage {Kind=FishingMessageKind.Start,Serial=serial,Request=request,Target=new Vec3(0,0,8)});}
    internal static NetworkInput Input(Guid id,long sequence=1,long tick=1)
    {return new NetworkInput {ContractVersion=1,SessionId=id,Sequence=sequence,ClientTick=tick,Input=new RawInputFrame {Sequence=sequence,SampleTimeSeconds=sequence/60.0,DurationSeconds=1f/60,InputAllowed=true}};}
    static byte[] InputBytes(NetworkInput n,long serial=2)
    {return FishingWire.Encode(new FishingMessage {Kind=FishingMessageKind.Input,Serial=serial,Input=n});}
    internal static NetworkSnapshot Snapshot(Guid id,long tick=1,long ack=0)
    {return new NetworkSnapshot {ContractVersion=1,LastAcceptedInputSequence=ack,Snapshot=new FishingSnapshot {SessionId=id,Tick=tick,TimeSeconds=tick/60.0,Authority=AuthorityMode.Server,Phase=FishingPhase.Fighting,FishMassKg=2,FishStamina01=.8f,FloatUp=Vec3.Up,LineLengthMeters=8},Events=Array.Empty<FishingEvent>()};}
    static FishingEvent Event(Guid id,long sequence,long tick)
    {return new FishingEvent {SessionId=id,Sequence=sequence,Tick=tick,Kind=FishingEventKind.Sprint,Intensity01=.5f};}
    static FishingMessage Decode(byte[] b){FishingMessage m;if(!FishingWire.TryDecode(b,out m))throw new Exception("decode");return m;}

    static void Codec()
    {
        var id=Guid.NewGuid();var n=Input(id);n.Input.MouseBackDelta=.73f;n.Input.StrikePressed=true;n.Input.ReelHeld=true;
        var decoded=Decode(InputBytes(n));
        Check(decoded.Input.Input.MouseBackDelta==.73f && decoded.Input.Input.StrikePressed && decoded.Input.Input.ReelHeld && decoded.Input.SessionId==id,"input binary round-trip");
        var s=Snapshot(id,90,9);s.Snapshot.Rod=new RodPose {Root=new Vec3(1,2,3),Tip=new Vec3(4,5,6),Forward=new Vec3(0,0,1),Right=new Vec3(1,0,0),PitchRadians=.4f};s.Events=new[]{Event(id,1,89),Event(id,2,90)};
        var bytes=FishingWire.Encode(new FishingMessage {Kind=FishingMessageKind.Snapshot,Serial=3,Snapshot=s});
        var back=Decode(bytes).Snapshot;
        Check(back.Snapshot.Rod.Tip.Z==6 && back.Events.Length==2 && back.Events[1].Sequence==2 && back.LastAcceptedInputSequence==9,"snapshot geometry, events and acknowledgement round-trip");
        bool allRejected=true;for(int i=0;i<bytes.Length;i++){FishingMessage ignored;if(FishingWire.TryDecode(bytes.Take(i).ToArray(),out ignored))allRejected=false;}
        Check(allRejected,"every truncated snapshot rejected");
        FishingMessage junk;
        Check(!FishingWire.TryDecode(bytes.Concat(new byte[]{0}).ToArray(),out junk),"trailing bytes rejected");
        var wrong=(byte[])bytes.Clone();wrong[4]=2;Check(!FishingWire.TryDecode(wrong,out junk),"contract version mismatch rejected");
        Check(!FishingWire.TryDecode(new byte[FishingWire.MaxBytes+1],out junk),"oversize payload rejected before allocation");
        var invalid=n;invalid.Input.MouseBackDelta=float.NaN;Check(!FishingWire.ValidInput(invalid),"NaN input rejected");
        invalid=n;invalid.Input.MoveRight=1.01f;Check(!FishingWire.ValidInput(invalid),"out-of-range movement rejected");
        invalid=n;invalid.Input.Sequence=2;Check(!FishingWire.ValidInput(invalid),"inner/outer sequence mismatch rejected");
        invalid=n;invalid.Input.SampleTimeSeconds=double.PositiveInfinity;Check(!FishingWire.ValidInput(invalid),"nonfinite client timestamp rejected");
        var bad=s;bad.Events=new[]{Event(Guid.NewGuid(),1,1)};Check(!FishingWire.ValidSnapshot(bad),"cross-session event rejected");
        bad=s;bad.Events=new[]{Event(id,2,1),Event(id,1,2)};Check(!FishingWire.ValidSnapshot(bad),"unordered events rejected");
        bad=s;bad.Events=new[]{Event(id,1,91)};Check(!FishingWire.ValidSnapshot(bad),"future event rejected");
        bad=s;bad.Snapshot.Authority=AuthorityMode.PredictedClient;Check(!FishingWire.ValidSnapshot(bad),"prediction cannot pose as authority");
        var random=new Random(29);for(int i=0;i<10000;i++){var b=new byte[random.Next(0,1024)];random.NextBytes(b);FishingWire.TryDecode(b,out junk);}
        Check(true,"10,000 seeded malformed messages safely decoded/rejected");
        // Corrupt a real packet, exercising parsers beyond the header.
        for(int i=0;i<5000;i++){var b=(byte[])bytes.Clone();int index=random.Next(17,b.Length);b[index]=(byte)random.Next(256);FishingWire.TryDecode(b,out junk);}
        Check(true,"5,000 valid-header mutations handled without parser exceptions");
    }
    static void Server()
    {
        var r=new Router();var t=new Transport();var net=new FishingNetwork(r,t,(owner,viewer)=>viewer=="near");net.Start(AuthorityMode.Server);
        Check(net.RegisterPeer("a","alice",7),"trusted peer registration");
        Check(!net.RegisterPeer("a","mallory",8),"connection identity cannot be rebound");
        Check(!net.ReceiveFromPeer("unknown",Start()),"unknown sender cannot allocate session");
        Check(net.ReceiveFromPeer("a",Start()) && r.Starts==1 && r.LastPlayer=="alice" && r.LastEntity==7,"start uses transport identity/entity");
        Check(!net.ReceiveFromPeer("a",Start()) && r.Starts==1,"identical replay is rejected");
        Check(net.ReceiveFromPeer("a",Start(2,1)) && r.Starts==1,"retried request with fresh envelope does not duplicate bait/start");
        Check(!net.ReceiveFromPeer("a",Start(3,2)) && r.Starts==1,"second simultaneous session rejected");
        Check(!net.ReceiveFromPeer("a",InputBytes(Input(Guid.NewGuid()),4)),"foreign session input rejected");
        net.Tick(.05);var input=Input(r.Next);input.Input.MouseBackDelta=1;
        Check(net.ReceiveFromPeer("a",InputBytes(input,5)) && r.Inputs==1,"valid input accepted once");
        Check(r.Input.Input.SampleTimeSeconds==.05 && r.Input.Input.DurationSeconds==FishingContract.FixedStepSeconds,"server replaces client simulation clock");
        Check(!net.ReceiveFromPeer("a",InputBytes(input,6)) && r.Inputs==1,"same input sequence cannot execute twice");
        Check(!net.ReceiveFromPeer("a",InputBytes(Input(r.Next,2,999),7)),"far-future client tick rejected");
        Check(!net.ReceiveFromPeer("a",InputBytes(Input(r.Next,10000,2),8)),"sequence poisoning jump rejected");
        net.RegisterPeer("near","near",8);net.RegisterPeer("far","far",9);
        var snap=Snapshot(r.Next,2,999);snap.Events=new[]{Event(r.Next,1,2)};net.Publish(snap);
        snap.Events[0]=Event(Guid.NewGuid(),999,1); // F must not hold caller's array.
        net.Tick(.15);
        var snapshots=t.ToPeers.Where(x=>Decode(x.Item2).Kind==FishingMessageKind.Snapshot).ToArray();
        Check(snapshots.Length==2 && snapshots.All(x=>x.Item1!="far"),"snapshots only go to owner and permitted observers");
        Check(Decode(snapshots[0].Item2).Snapshot.LastAcceptedInputSequence==1 && Decode(snapshots[0].Item2).Snapshot.Events[0].SessionId==r.Next,"ack is server-owned and event array copied");
        var forged=FishingWire.Encode(new FishingMessage {Kind=FishingMessageKind.Snapshot,Serial=10,Snapshot=Snapshot(r.Next)});
        Check(!net.ReceiveFromPeer("a",forged),"client cannot submit success/weight snapshot");
        var terminal=Snapshot(r.Next,3,1);terminal.Snapshot.Phase=FishingPhase.Resolved;net.Publish(terminal);
        Check(net.ActiveSessionCount==0 && r.Cancels==0,"authority terminal closes once without another reward/cancel path");
        Check(!net.ReceiveFromPeer("a",InputBytes(Input(r.Next,2,2),11)),"post-terminal input rejected");
        int starts=r.Starts;net.ReceiveFromPeer("a",Start(12,1));Check(r.Starts==starts,"old start cannot resurrect completed session");
        r.Next=Guid.NewGuid();net.Tick(1);Check(net.ReceiveFromPeer("a",Start(13,3)),"new request may start next session");
        var old=r.Next;Check(net.RegisterPeer("reconnected","alice",10) && net.ActiveSessionCount==0 && r.CancelReason==FailureReason.Disconnected,"reconnect replaces generation and cancels old session");
        Check(!net.ReceiveFromPeer("a",InputBytes(Input(old),14)),"old connection invalid after reconnect");
        r.Next=Guid.NewGuid();net.ReceiveFromPeer("reconnected",Start());net.Tick(4.1);
        Check(net.ActiveSessionCount==0 && r.CancelReason==FailureReason.Timeout,"idle session cancels and releases after timeout");
        Throws(()=>net.Tick(2),"monotonic clock regression rejected");net.Stop();
        var rr=new Router();var tt=new Transport();var limited=new FishingNetwork(rr,tt,options:new FishingNetworkOptions {MessageBurst=3,MessagesPerSecond=1,MaxPeers=1});limited.Start(AuthorityMode.Server);
        limited.RegisterPeer("x","x",1);Check(!limited.RegisterPeer("y","y",2),"peer allocation bounded");
        limited.ReceiveFromPeer("x",new byte[]{1});limited.ReceiveFromPeer("x",new byte[]{1});limited.ReceiveFromPeer("x",new byte[]{1});
        Check(!limited.ReceiveFromPeer("x",Start()) && rr.Starts==0,"malformed packets consume rate budget");limited.Tick(1);
        Check(limited.ReceiveFromPeer("x",Start()),"rate bucket recovers with server time");limited.Stop();
    }
    static void Client()
    {
        var r=new Router();var t=new Transport();var c=new FishingNetwork(r,t);c.Start(AuthorityMode.PredictedClient);
        long request=c.RequestStart(new Vec3(0,0,8));Guid id=Guid.NewGuid();
        var reply=FishingWire.Encode(new FishingMessage {Kind=FishingMessageKind.StartReply,Serial=1,Request=request,Accepted=true,SessionId=id});
        Check(c.ReceiveFromServer(reply) && c.LocalSessionId==id,"client accepts matched authority start reply");
        Check(!c.ReceiveFromServer(reply),"duplicate server envelope rejected");
        c.SendInput(Input(id));Check(t.ToServer.Count==2,"client input sent only after accepted start");
        var s=Snapshot(id,10,1);s.Events=new[]{Event(id,1,10)};
        Check(c.ReceiveFromServer(FishingWire.Encode(new FishingMessage {Kind=FishingMessageKind.Snapshot,Serial=2,Snapshot=s})) && r.Applies==1,"authoritative snapshot applied");
        s.Snapshot.Tick=20;s.Snapshot.TimeSeconds=20/60.0;
        c.ReceiveFromServer(FishingWire.Encode(new FishingMessage {Kind=FishingMessageKind.Snapshot,Serial=3,Snapshot=s}));
        Check(r.Snapshot.Events.Length==0,"one-shot events deduplicated across snapshots");
        FishingSnapshot previous,current;Check(c.TryGetSnapshots(id,out previous,out current) && previous.Tick==10 && current.Tick==20,"interpolation retains previous and current authority state");
        var stale=Snapshot(id,15,1);Check(!c.ReceiveFromServer(FishingWire.Encode(new FishingMessage {Kind=FishingMessageKind.Snapshot,Serial=4,Snapshot=stale})),"stale snapshot tick rejected despite newer envelope");
        stale=Snapshot(id,21,0);Check(!c.ReceiveFromServer(FishingWire.Encode(new FishingMessage {Kind=FishingMessageKind.Snapshot,Serial=5,Snapshot=stale})),"ack regression rejected");
        var end=Snapshot(id,20,1);end.Snapshot.Phase=FishingPhase.LineBroken;end.Snapshot.Failure=FailureReason.Overload;
        Check(c.ReceiveFromServer(FishingWire.Encode(new FishingMessage {Kind=FishingMessageKind.Snapshot,Serial=6,Snapshot=end})),"same-tick terminal correction accepted once");
        int ended=0;c.SessionEnded+=(g,reason)=>ended++;
        c.ReceiveFromServer(FishingWire.Encode(new FishingMessage {Kind=FishingMessageKind.End,Serial=7,SessionId=id,Reason=FailureReason.Overload}));
        Check(ended==1 && c.LocalSessionId==Guid.Empty && c.CachedSessionCount==0,"end clears local state and requests control release");
        Check(!c.ReceiveFromServer(FishingWire.Encode(new FishingMessage {Kind=FishingMessageKind.Snapshot,Serial=3,Snapshot=s})),"late reordered packet cannot resurrect ended session");
        Throws(()=>c.SendInput(Input(id)),"ended client cannot send input");
        int timeouts=0;c.StartResult+=(req,ok,g,reason)=>{if(reason==FailureReason.Timeout)timeouts++;};c.RequestStart(new Vec3(0,0,8));c.Tick(6);
        Check(timeouts==1,"missing start response times out");c.Stop();
        var empty=new FishingNetwork(new Router(),new Transport());empty.Start(AuthorityMode.PredictedClient);long q=empty.RequestStart(Vec3.Zero);
        empty.ReceiveFromServer(FishingWire.Encode(new FishingMessage {Kind=FishingMessageKind.StartReply,Serial=1,Request=q,Accepted=true,SessionId=Guid.NewGuid()}));empty.Tick(6);
        Check(empty.LocalSessionId==Guid.Empty,"accepted start without first snapshot also expires");empty.Stop();
        var observer=new FishingNetwork(new Router(),new Transport(),options:new FishingNetworkOptions {MaxClientSessions=1});observer.Start(AuthorityMode.Observer);
        Throws(()=>observer.RequestStart(Vec3.Zero),"observer cannot create authority session");
        observer.ReceiveFromServer(FishingWire.Encode(new FishingMessage {Kind=FishingMessageKind.Snapshot,Serial=1,Snapshot=Snapshot(Guid.NewGuid())}));
        Check(!observer.ReceiveFromServer(FishingWire.Encode(new FishingMessage {Kind=FishingMessageKind.Snapshot,Serial=2,Snapshot=Snapshot(Guid.NewGuid())})),"client snapshot cache bounded");
        observer.Tick(6);Check(observer.CachedSessionCount==0,"stale observer state expires");observer.Stop();
    }
    static void Buffers()
    {
        var b=new AuthorityInputBuffer();Guid id=Guid.NewGuid();var x=Input(id);x.Input.MouseBackDelta=2;x.Input.StrikePressed=true;x.Input.ReelHeld=true;
        Check(b.Push(x,0),"authority buffer accepts validated input");x=Input(id,2);x.Input.MouseBackDelta=3;x.Input.ReelHeld=true;b.Push(x,.01);
        var first=b.Consume(.02,1f/60);var next=b.Consume(.03,1f/60);
        Check(first.MouseBackDelta==5 && first.StrikePressed && next.MouseBackDelta==0 && !next.StrikePressed && next.ReelHeld,"deltas and edges consumed once while held buttons persist briefly");
        Check(next.Sequence>first.Sequence && b.LastConsumedNetworkSequence==2,"server frame sequence separate from network ack");
        var stalled=b.Consume(.4,1f/60);Check(!stalled.ReelHeld && stalled.InputAllowed && stalled.MoveForward==0,"250ms stalled input neutralizes held actions");
        var pub=b.ForPublication(Snapshot(id).Snapshot,null);Check(pub.Snapshot.LastInputSequence==2,"publication restores consumed network sequence");
        b.Clear();x=Input(id);x.Input.MouseBackDelta=200;b.Push(x,0);x=Input(id,2);x.Input.MouseBackDelta=200;
        Check(!b.Push(x,.01),"aggregate input cannot exceed per-update budget");
        var guard=new FishingMovementGuard();Check(guard.Observe(Vec3.Zero,0,5) && guard.Observe(new Vec3(0,0,.5f),.1,5),"movement guard allows measured normal movement");
        Check(!guard.Observe(new Vec3(0,0,50),.2,5),"movement guard rejects teleport during fishing");
        guard.Reset();guard.Observe(Vec3.Zero,0,0,.75f);bool rejected=false;for(int i=1;i<20;i++)if(!guard.Observe(new Vec3(0,0,.1f*i),i*.01,0,.75f)){rejected=true;break;}
        Check(rejected,"movement slack is a budget, not a per-packet speed exploit");
    }
    static void LatencyAndInterest()
    {
        var sr=new Router();var st=new Transport();bool near=true;
        var server=new FishingNetwork(sr,st,(owner,viewer)=>near);server.Start(AuthorityMode.Server);server.RegisterPeer("a","a",1);server.RegisterPeer("b","b",2);server.ReceiveFromPeer("a",Start());
        server.Publish(Snapshot(sr.Next,1));server.Tick(.11);near=false;server.Publish(Snapshot(sr.Next,2));server.Tick(.22);
        Check(st.ToPeers.Any(x=>x.Item1=="b" && Decode(x.Item2).Kind==FishingMessageKind.End),"leaving interest radius clears observer scene");
        var cr=new Router();var ct=new Transport();var client=new FishingNetwork(cr,ct);client.Start(AuthorityMode.Observer);
        // Drop snapshot 1, deliver 3 before 2 (simulated 200ms latency/reordering).
        var id=Guid.NewGuid();var n2=FishingWire.Encode(new FishingMessage {Kind=FishingMessageKind.Snapshot,Serial=2,Snapshot=Snapshot(id,12)});
        var n3=FishingWire.Encode(new FishingMessage {Kind=FishingMessageKind.Snapshot,Serial=3,Snapshot=Snapshot(id,18)});
        client.Tick(.2);Check(client.ReceiveFromServer(n3) && !client.ReceiveFromServer(n2) && cr.Snapshot.Snapshot.Tick==18,"delay/loss/reorder converges to newest authority without resimulation");
        Check(cr.Cancels==0 && cr.Starts==0 && cr.Inputs==0,"snapshot corrections cannot run physics or settle rewards");
        client.Stop();server.Stop();
    }
    static void Benchmark()
    {
        var m=new FishingMessage {Kind=FishingMessageKind.Snapshot,Serial=1,Snapshot=Snapshot(Guid.NewGuid(),1)};
        for(int i=0;i<100;i++)Decode(FishingWire.Encode(m));
        long before=GC.GetAllocatedBytesForCurrentThread();var sw=Stopwatch.StartNew();const int count=10000;int size=0;
        for(int i=0;i<count;i++){var bytes=FishingWire.Encode(m);size=bytes.Length;Decode(bytes);}
        sw.Stop();long allocation=GC.GetAllocatedBytesForCurrentThread()-before;
        Console.WriteLine("BENCH codec_roundtrip iterations="+count+" bytes="+size+" mean_us="+(sw.Elapsed.TotalMilliseconds*1000/count).ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+" allocated_bytes_per_op="+(allocation/count));
        Check(sw.Elapsed.TotalSeconds<30,"codec smoke performance budget (<30s / 10,000; not game FPS)");
        foreach(int players in new[]{0,1,8,32})
        {
            var transport=new CountingTransport();var router=new Router();var net=new FishingNetwork(router,transport);
            net.Start(AuthorityMode.Server);var ids=new Guid[players];var connections=new string[players];
            for(int p=0;p<players;p++)
            {connections[p]="p"+p;ids[p]=Guid.NewGuid();router.Next=ids[p];net.RegisterPeer(connections[p],connections[p],p);net.ReceiveFromPeer(connections[p],Start());}
            var timings=new double[600];before=GC.GetAllocatedBytesForCurrentThread();sw.Restart();
            for(int frame=0;frame<600;frame++)
            {
                long stamp=Stopwatch.GetTimestamp();net.Tick(frame/60.0);
                for(int p=0;p<players;p++)
                {
                    net.ReceiveFromPeer(connections[p],InputBytes(Input(ids[p],frame+1,frame),frame+2));
                    net.Publish(Snapshot(ids[p],frame+1,frame+1));
                }
                timings[frame]=(Stopwatch.GetTimestamp()-stamp)*1000000.0/Stopwatch.Frequency;
            }
            sw.Stop();allocation=GC.GetAllocatedBytesForCurrentThread()-before;Array.Sort(timings);
            Console.WriteLine("BENCH pure_network players="+players+" frames=600 mean_us="+(sw.Elapsed.TotalMilliseconds*1000/600).ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+
                " p95_us="+timings[570].ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+" allocated_bytes_per_frame="+(allocation/600)+" outbound_bytes_per_simulated_second="+(transport.Bytes/10));
            Check(net.AcceptedInputs==600L*players,"network benchmark accepted all inputs for "+players+" players");net.Stop();
        }
    }
    static void Loopback()
    {
        var remote=new Transport();var multiplex=new FishingServerTransport(remote);var router=new Router();
        var authority=new FishingNetwork(router,multiplex);authority.Start(AuthorityMode.Standalone);
        using(var link=new FishingLoopback(authority,multiplex))
        {
            var clientRouter=new Router();var client=new FishingNetwork(clientRouter,link);client.Start(AuthorityMode.PredictedClient);
            link.Attach(client,"host",7);client.RequestStart(new Vec3(0,0,8));
            Check(client.LocalSessionId==router.Next && router.Starts==1,"host loopback performs full request/reply handshake");
            client.SendInput(Input(router.Next));Check(router.Inputs==1,"host input passes same identity and sequence checks");
            authority.Publish(Snapshot(router.Next,1));authority.Tick(.11);
            Check(clientRouter.Applies==1 && remote.ToPeers.Count==0,"host receives authoritative snapshot without remote broadcast");
        }
        Check(authority.ActiveSessionCount==0 && router.Cancels==1,"disposing host loopback cancels exactly once");authority.Stop();
    }
    public static int Main(string[] args)
    {
        try {Codec();Server();Client();Buffers();LatencyAndInterest();Loopback();RealModulesTests.Run(args[0]);Benchmark();Console.WriteLine("RESULT PASS checks="+Checks);return 0;}
        catch(Exception e){Console.WriteLine("RESULT FAIL "+e);return 1;}
    }
}
