using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using YFAutomation.CargoDrones;

public static class CargoDroneWorldTests
{
    static int checks;
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception("World FAIL: "+message);}
    static void Reject(Action action,string message){bool threw=false;try{action();}catch{threw=true;}Check(threw,message);}
    sealed class Air : ICargoAirspace
    {public bool Released,Loading;public int Prepares;public CargoHold Prepare(CargoPoint a,CargoPoint b){if(Released)throw new Exception("Released airspace reused");Prepares++;return Loading?CargoHold.ChunkLoading:CargoHold.None;}public CargoSweep Sweep(CargoPoint a,CargoPoint b){if(Released)throw new Exception("Released airspace reused");return CargoSweep.Clear;}public void ReachedSegment(CargoPoint p){}}
    sealed class Endpoint : ICargoDurableEndpoint
    {
        public CargoInventory Value;
        public Guid LastTransaction{get;private set;}
        public bool Pending;
        public bool Uncertain;
        Guid fence;
        public CargoInventory Snapshot(){return Value;}
        public bool AcquireFence(Guid id,long revision){if(fence!=Guid.Empty||revision!=Value.Revision)return false;fence=id;return true;}
        public void Apply(CargoPlan p,Guid id){if(fence!=id||!p.Matches(Value))throw new Exception("Plan/fence mismatch");Value=new CargoInventory(Value.Id,Value.Incarnation,Value.Revision+1,Value.Owner,p.EndpointAfter,new bool[Value.Length],Enumerable.Repeat(true,Value.Length).ToArray());LastTransaction=id;}
        public void RequestDurableSave(Guid id){}
        public CargoSaveResult PollDurableSave(Guid id){return Uncertain?CargoSaveResult.Uncertain:Pending?CargoSaveResult.Pending:CargoSaveResult.Durable;}
        public void ReleaseFence(Guid id){if(fence!=id)throw new Exception("Wrong fence release");fence=Guid.Empty;}
    }
    sealed class Adapter : ICargoWorldAdapter,ICargoRecoveryAdapter
    {
        public bool Online=true,Power=true,Exists=true;
        public readonly Dictionary<Guid,Endpoint> Endpoints=new Dictionary<Guid,Endpoint>();
        public int Releases;
        public readonly Dictionary<Guid,Air> Spaces=new Dictionary<Guid,Air>();
        public Action<Guid> BeforeRelease;
        public bool NewSpaceLoading;
        public bool RecoveryEnabled,RecoveryPending;
        public Endpoint RecoveryEndpoint;
        public bool ResolveRecovery(CargoHubConfiguration hub,Guid flight,out ICargoDurableEndpoint endpoint)
        {
            endpoint=null;if(!RecoveryEnabled)return false;
            if(RecoveryEndpoint==null)RecoveryEndpoint=new Endpoint{Pending=RecoveryPending,Value=new CargoInventory(flight,hub.HubId,0,hub.Owner,new CargoItem[6],new bool[6],Enumerable.Repeat(true,6).ToArray())};
            endpoint=RecoveryEndpoint;return true;
        }
        public bool OwnerOnline(string owner){return Online;}
        public bool HubExists(CargoHubConfiguration hub){return Exists;}
        public bool Powered(CargoHubConfiguration hub){return Power;}
        public CargoPoint Home(CargoHubConfiguration hub){return new CargoPoint(hub.Position.X,hub.Position.Y,hub.Position.Z);}
        public bool Resolve(CargoBinding b,out ICargoDurableEndpoint endpoint,out CargoPoint point,out CargoHold hold)
        {Endpoint e;hold=CargoHold.None;point=new CargoPoint(b.Position.X,b.Position.Y,b.Position.Z);endpoint=null;if(!Endpoints.TryGetValue(b.EndpointId,out e)||e.Value.Incarnation!=b.Incarnation)return false;endpoint=e;return true;}
        public ICargoAirspace OpenAirspace(Guid id){var space=new Air{Loading=NewSpaceLoading};Spaces[id]=space;return space;}
        public void ReleaseAirspace(Guid id){BeforeRelease?.Invoke(id);Releases++;Air space;if(Spaces.TryGetValue(id,out space))space.Released=true;}
        public CargoBinding Bind(Guid world,string owner,CargoPosition at,bool source,int count=0)
        {var b=new CargoBinding(world,Guid.NewGuid(),Guid.NewGuid(),at,owner,source?"AutoMinerIron":"crate");Endpoints.Add(b.EndpointId,new Endpoint{Value=new CargoInventory(b.EndpointId,b.Incarnation,0,owner,new[]{count==0?null:new CargoItem(new byte[]{1,2,3},count,6000)},new bool[1],new[]{true})});return b;}
    }
    static CargoHubConfiguration Config(Guid world,Adapter adapter,int x=0,string owner="owner",int target=8)
    {
        var c=new CargoHubConfiguration(world,Guid.NewGuid(),new CargoPosition(x,100,0),owner);var rules=new CargoRules();
        c=c.AddSource(owner,c.Revision,adapter.Bind(world,owner+"-miner",new CargoPosition(x+2,100,0),true,4),rules);
        return c.SetTarget(owner,c.Revision,adapter.Bind(world,owner+"-warehouse",new CargoPosition(x+target,100,0),false),rules);
    }
    public static int Run(string directory)
    {
        checks=0;var root=Path.Combine(directory,"world-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        Guid world=Guid.NewGuid();var adapter=new Adapter();var config=Config(world,adapter);
        using(var journal=new CargoFileJournal(Path.Combine(root,"world.wal"),world))using(var store=new CargoCheckpointStore(Path.Combine(root,"checkpoint"),world))
        {
            var service=new CargoWorldService(world,journal,store,adapter);service.Register(config);
            Reject(()=>service.Register(config),"duplicate hub registration refused");
            service.Tick(100);Check(service.ActiveFlights==1,"configured powered hub autonomously departs");
            var saved=store.LoadWorld(journal);Check(saved.Hubs[0].Flight==service.Status()[0].Flight&&saved.Missions.Length==1,"departure and hub association published together");
            Check(saved.Hubs[0].ShipmentSource.Matches(config.Sources[0])&&saved.Hubs[0].ShipmentTarget.Matches(config.Target),"persistent shipment retains exact incarnations");
            var position=service.Status()[0].Position;long battery=service.Status()[0].Battery;
            Guid initialFlight=service.Status()[0].Flight;var oldSpace=adapter.Spaces[initialFlight];
            adapter.BeforeRelease=id=>Check(store.LoadWorld(journal).Missions.Single(m=>m.Id==id).Motion.Position.Distance(position)==0,"offline checkpoint is readable before observer release");
            adapter.Online=false;for(int i=0;i<10;i++)service.Tick(100);
            Check(adapter.Releases==1&&oldSpace.Released,"offline releases the flight adapter exactly once");adapter.BeforeRelease=null;
            int snapshots=Directory.GetFiles(Path.Combine(root,"checkpoint"),"ledger-*.snapshot").Length;
            for(int i=0;i<150;i++)service.Tick(100);
            Check(Directory.GetFiles(Path.Combine(root,"checkpoint"),"ledger-*.snapshot").Length==snapshots,"unchanged offline world does not emit periodic snapshots");
            Check(service.Status()[0].Position.Distance(position)==0&&service.Status()[0].Battery==battery,"offline freezes movement and energy");adapter.Online=true;
            service.Tick(100,true);Check(service.Status()[0].Position.Distance(position)==0,"world pause freezes motion");
            Check(adapter.Spaces[initialFlight]==oldSpace,"paused world does not reacquire offline resources");
            adapter.NewSpaceLoading=true;service.Tick(100);
            var resumedSpace=adapter.Spaces[initialFlight];
            Check(resumedSpace!=oldSpace&&resumedSpace.Prepares>0&&service.Status()[0].Hold==CargoHold.ChunkLoading,"reconnect creates fresh navigation and waits for loaded data");
            Check(service.Status()[0].Position.Distance(position)==0&&service.Status()[0].Battery==battery,"reloading consumes no movement or battery");resumedSpace.Loading=false;adapter.NewSpaceLoading=false;
            // Manual pause prevents the next sortie, not safe completion of this one.
            service.Configure(config.HubId,"owner",config.Revision,config.SetPaused("owner",config.Revision,true));
            for(int i=0;i<500&&service.Status()[0].Phase!=CargoPhase.Docked;i++)service.Tick(100);
            Check(service.Status()[0].Phase==CargoPhase.Docked&&!service.Faulted,"world coordinator completes pickup, delivery and docking");
            Check(CargoPlanner.Count(adapter.Endpoints[config.Sources[0].EndpointId].Value.Items)==0&&CargoPlanner.Count(adapter.Endpoints[config.Target.EndpointId].Value.Items)==4,"world transfers real endpoint counts exactly once");
            Check(journal.Entries.All(e=>e.Transaction.Plan.Owner==config.Owner),"cargo ledger retains hub owner across differently owned endpoints");
            Check(config.Sources[0].Owner!=config.Owner&&config.Target.Owner!=config.Owner&&config.Sources[0].Owner!=config.Target.Owner,"source, warehouse and hub have independent placer identities");
            Check(adapter.Releases>0&&store.LoadWorld(journal).Missions[0].Phase==CargoPhase.Docked,"docking persisted before lease release");
            battery=service.Status()[0].Battery;service.Tick(100);Check(service.Status()[0].Battery>battery,"manual pause still charges");
            battery=service.Status()[0].Battery;service.Tick(100,true);Check(service.Status()[0].Battery==battery,"world pause suppresses charging");
            service.Checkpoint();saved=store.LoadWorld(journal);var restored=new CargoWorldService(world,journal,store,adapter);restored.Restore(saved);
            Check(restored.Status()[0].Configuration.Paused&&restored.Status()[0].Battery==battery,"world checkpoint restores config and battery");
            Reject(()=>new CargoWorldState(saved.Missions,new CargoHubState[0]),"orphaned missions rejected");
            Reject(()=>service.Configure(config.HubId,"intruder",config.Revision,config),"configuration ownership enforced");
        }
        // More than four eligible hubs must queue; no hub can claim a fifth slot.
        world=Guid.NewGuid();adapter=new Adapter();root=Path.Combine(root,"limits");Directory.CreateDirectory(root);
        using(var journal=new CargoFileJournal(Path.Combine(root,"world.wal"),world))using(var store=new CargoCheckpointStore(Path.Combine(root,"checkpoint"),world))
        {
            var service=new CargoWorldService(world,journal,store,adapter);
            for(int i=0;i<16;i++)service.Register(Config(world,adapter,i*20,"owner"+(i/4)));
            Reject(()=>service.Register(Config(world,adapter,900,"newowner")),"global 16 hub limit");
            for(int i=0;i<5;i++)service.Tick(100);
            Check(service.ActiveFlights==4&&service.Status().Count(s=>s.Flight==Guid.Empty)==12,"global four-flight admission bound");
            Check(store.LoadWorld(journal).Hubs.Length==16,"all queued hubs included in checkpoint");
        }
        // A long delivery is admitted independently of the 64-cell collection radius.
        world=Guid.NewGuid();adapter=new Adapter();root=Path.Combine(root,"long");Directory.CreateDirectory(root);
        using(var journal=new CargoFileJournal(Path.Combine(root,"world.wal"),world))using(var store=new CargoCheckpointStore(Path.Combine(root,"checkpoint"),world))
        {
            var service=new CargoWorldService(world,journal,store,adapter);config=Config(world,adapter,target:1000);service.Register(config);service.Tick(100);
            Check(service.ActiveFlights==1,"1000-cell target with nearby source admitted at full battery");
            adapter.Exists=false;service.Tick(100);
            Check(service.Status()[0].Phase==CargoPhase.RecoveryOnly&&store.LoadWorld(journal).Hubs[0].Removed,"hub removal freezes shipment and persists tombstone");
        }
        // Prepared saves continue to resolve while the owner is offline; neither
        // a concurrent edit nor a checkpoint may drop this pending transaction.
        world=Guid.NewGuid();adapter=new Adapter();root=Path.Combine(root,"pending");Directory.CreateDirectory(root);
        using(var journal=new CargoFileJournal(Path.Combine(root,"world.wal"),world))using(var store=new CargoCheckpointStore(Path.Combine(root,"checkpoint"),world))
        {
            var service=new CargoWorldService(world,journal,store,adapter);config=Config(world,adapter);service.Register(config);var source=adapter.Endpoints[config.Sources[0].EndpointId];source.Pending=true;
            for(int i=0;i<100&&journal.Entries.Count==0;i++)service.Tick(100);
            Check(service.Status()[0].Hold==CargoHold.PersistencePending,"pending native save holds world transfer");
            Reject(()=>service.Configure(config.HubId,"owner",config.Revision,config.SetPaused("owner",config.Revision,true)),"editing during prepared transfer is refused");
            Reject(()=>service.Checkpoint(),"manual checkpoint waits for prepared transfer");Check(!service.Faulted,"requesting a premature checkpoint does not permanently fault the world");
            adapter.Online=false;service.Tick(100);Check(adapter.Releases==0,"offline pending save retains native resources");
            adapter.BeforeRelease=id=>Check(store.LoadWorld(journal).Missions.Single(m=>m.Id==id).Revision==1,"pending commit is checkpointed before offline release");
            adapter.Online=false;source.Pending=false;service.Tick(100);
            Check(service.Status()[0].Packages==4&&store.LoadWorld(journal).Missions[0].Revision==1,"offline pending save resolves and checkpoints committed cargo");
            Check(adapter.Releases==1,"settled offline transfer releases its resources");
            adapter.BeforeRelease=null;service.Configure(config.HubId,"owner",config.Revision,config.SetPaused("owner",config.Revision,true));adapter.Online=true;
            for(int i=0;i<500&&service.Status()[0].Phase!=CargoPhase.Docked;i++)service.Tick(100);
            Check(service.Status()[0].Phase==CargoPhase.Docked&&service.Status()[0].Packages==0&&CargoPlanner.Count(adapter.Endpoints[config.Target.EndpointId].Value.Items)==4,"committed offline cargo resumes and delivers exactly once");
        }
        // Changing configuration while carrying goods cannot retarget them or
        // allow the next sortie to mix in newly produced source items.
        world=Guid.NewGuid();adapter=new Adapter();root=Path.Combine(root,"redelivery");Directory.CreateDirectory(root);
        using(var journal=new CargoFileJournal(Path.Combine(root,"world.wal"),world))using(var store=new CargoCheckpointStore(Path.Combine(root,"checkpoint"),world))
        {
            var service=new CargoWorldService(world,journal,store,adapter);config=Config(world,adapter);service.Register(config);
            for(int i=0;i<100&&service.Status()[0].Packages==0;i++)service.Tick(100);
            Check(service.Status()[0].Packages==4,"world fixture has committed shipment before changing target");
            var oldTarget=adapter.Endpoints[config.Target.EndpointId];var v=oldTarget.Value;
            oldTarget.Value=new CargoInventory(v.Id,v.Incarnation,v.Revision+1,v.Owner,new[]{new CargoItem(new byte[]{9},6000,6000)},new bool[1],new[]{true});
            var newTarget=adapter.Bind(world,"owner",new CargoPosition(12,100,0),false);
            var changed=config.SetTarget("owner",config.Revision,newTarget,new CargoRules());service.Configure(config.HubId,"owner",config.Revision,changed);
            for(int i=0;i<500&&service.Status()[0].Phase!=CargoPhase.Docked;i++)service.Tick(100);
            Check(service.Status()[0].Phase==CargoPhase.Docked&&service.Status()[0].Packages==4,"full original target returns loaded drone without changing destination");
            Check(CargoPlanner.Count(adapter.Endpoints[newTarget.EndpointId].Value.Items)==0,"new configured target receives no old cargo");
            var saved=store.LoadWorld(journal);Check(saved.Hubs[0].ShipmentTarget.Matches(config.Target)&&saved.Hubs[0].Configuration.Target.Matches(newTarget),"world checkpoint distinguishes shipment target from future configuration");
            var source=adapter.Endpoints[config.Sources[0].EndpointId];v=source.Value;
            source.Value=new CargoInventory(v.Id,v.Incarnation,v.Revision+1,v.Owner,new[]{new CargoItem(new byte[]{7},3,6000)},new bool[1],new[]{true});
            v=oldTarget.Value;oldTarget.Value=new CargoInventory(v.Id,v.Incarnation,v.Revision+1,v.Owner,new CargoItem[1],new bool[1],new[]{true});
            Guid flight=service.Status()[0].Flight;service.Tick(100);
            Check(service.Status()[0].Flight==flight&&service.Status()[0].Phase==CargoPhase.ToTarget,"redelivery retains shipment identity and bypasses source");
            service.Configure(changed.HubId,"owner",changed.Revision,changed.SetPaused("owner",changed.Revision,true));
            for(int i=0;i<500&&service.Status()[0].Phase!=CargoPhase.Docked;i++)service.Tick(100);
            Check(CargoPlanner.Count(oldTarget.Value.Items)==4&&service.Status()[0].Packages==0&&CargoPlanner.Count(source.Value.Items)==3,"old shipment delivered once without picking up new source goods");
        }
        world=Guid.NewGuid();adapter=new Adapter();root=Path.Combine(root,"failed-suspend");Directory.CreateDirectory(root);
        using(var journal=new CargoFileJournal(Path.Combine(root,"world.wal"),world))using(var store=new CargoCheckpointStore(Path.Combine(root,"checkpoint"),world))
        {
            var service=new CargoWorldService(world,journal,store,adapter);config=Config(world,adapter);service.Register(config);service.Tick(100);
            store.Dispose();adapter.Online=false;Reject(()=>service.Tick(100),"failed offline publication requires recovery");
            Check(service.Faulted&&adapter.Releases==0,"failed checkpoint retains the old flight resources");
        }
        world=Guid.NewGuid();adapter=new Adapter();root=Path.Combine(root,"uncertain");Directory.CreateDirectory(root);
        using(var journal=new CargoFileJournal(Path.Combine(root,"world.wal"),world))using(var store=new CargoCheckpointStore(Path.Combine(root,"checkpoint"),world))
        {
            var service=new CargoWorldService(world,journal,store,adapter);config=Config(world,adapter);service.Register(config);adapter.Endpoints[config.Sources[0].EndpointId].Uncertain=true;
            Reject(()=>{for(int i=0;i<100;i++)service.Tick(100);},"uncertain native save explicitly stops the world service");
            Check(service.Faulted&&adapter.Releases==0&&journal.Entries.Last().Kind==CargoJournalKind.Prepare,"uncertain transfer keeps prepared evidence and resources for recovery");
        }
        world=Guid.NewGuid();adapter=new Adapter{RecoveryEnabled=true,RecoveryPending=true};root=Path.Combine(root,"destroy-recovery");Directory.CreateDirectory(root);
        using(var journal=new CargoFileJournal(Path.Combine(root,"world.wal"),world))using(var store=new CargoCheckpointStore(Path.Combine(root,"checkpoint"),world))
        {
            var service=new CargoWorldService(world,journal,store,adapter);config=Config(world,adapter);service.Register(config);
            for(int i=0;i<100&&service.Status()[0].Packages==0;i++)service.Tick(100);
            Check(service.Status()[0].Packages==4,"destruction fixture holds committed cargo");adapter.Exists=false;service.Tick(100);
            Check(service.Status()[0].Phase==CargoPhase.RecoveryOnly&&CargoPlanner.Count(adapter.RecoveryEndpoint.Value.Items)==4,"removed hub starts handoff into its one deterministic recovery endpoint");
            Reject(()=>service.Checkpoint(),"pending recovery save cannot publish an empty cargo checkpoint");
            Check(journal.Entries.Last().Kind==CargoJournalKind.Prepare&&!service.Faulted,"pending recovery retains prepared evidence without faulting normal scheduling");
            adapter.RecoveryEndpoint.Pending=false;service.Tick(100);service.Tick(100);
            Check(service.Status().Length==0&&store.LoadWorld(journal).Hubs.Length==0,"complete durable recovery releases destroyed hub identity and capacity");
            Check(CargoPlanner.Count(adapter.RecoveryEndpoint.Value.Items)==4&&CargoPlanner.Count(adapter.Endpoints[config.Target.EndpointId].Value.Items)==0,"destruction recovers goods exactly once without sending them to the old target");
            adapter.Exists=true;service.Register(Config(world,adapter));Check(service.Status().Length==1,"the former hub position can be reused after recovery");
        }
        world=Guid.NewGuid();adapter=new Adapter();root=Path.Combine(root,"wal-tail-recovery");Directory.CreateDirectory(root);
        using(var journal=new CargoFileJournal(Path.Combine(root,"world.wal"),world))using(var store=new CargoCheckpointStore(Path.Combine(root,"checkpoint"),world))
        {
            var service=new CargoWorldService(world,journal,store,adapter);config=Config(world,adapter);service.Register(config);
            adapter.Endpoints[config.Sources[0].EndpointId].Pending=true;
            for(int i=0;i<100&&journal.Entries.Count==0;i++)service.Tick(100);
            Reject(()=>store.LoadWorld(journal),"strict checkpoint read rejects an unresolved transaction tail");
            var saved=store.LoadWorldForRecovery(journal);var before=saved.Missions.Single();var tx=journal.Entries.Last().Transaction;
            Check(saved.Hubs.Single().Flight==tx.FlightId&&before.Revision==0,"recovery read proves the exact pre-transaction checkpoint prefix");
            Reject(()=>CargoMission.Reconcile(before,journal,new Air(),false),"reconciliation cannot invent a result for pending inventory");
            journal.Append(CargoJournalKind.Commit,tx);
            Check(store.LoadWorldForRecovery(journal).Missions.Single().Revision==0,"recovery also accepts the single committed transaction tail");
            var recovered=CargoMission.Reconcile(before,journal,new Air(),false);
            Check(recovered.Revision==1&&CargoPlanner.Count(recovered.Cargo)==4&&recovered.Phase==CargoPhase.Returning&&recovered.Motion.Returning,"resolved cargo tail returns home using the checkpoint route");
            Check(recovered.Motion.Trail.SequenceEqual(before.Motion.Trail)&&recovered.Battery==before.Battery-5000,"recovery preserves the corridor and conservatively charges unpublished energy");
            var removed=CargoMission.Reconcile(before,journal,new Air(),true);
            Check(removed.Phase==CargoPhase.RecoveryOnly&&CargoPlanner.Count(removed.Cargo)==4,"removed hub recovery keeps committed cargo frozen for its unique crate");
            var unload=CargoPlanner.Unload(adapter.Endpoints[config.Target.EndpointId].Value,tx.Plan.CargoAfter,config.Owner);
            journal.Append(CargoJournalKind.Prepare,new CargoTransaction(Guid.NewGuid(),tx.FlightId,world,1,unload));
            Reject(()=>store.LoadWorldForRecovery(journal),"recovery refuses a tail spanning another uncheckpointed transaction");
        }
        return checks;
    }
}
