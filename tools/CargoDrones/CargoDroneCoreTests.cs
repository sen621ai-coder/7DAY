using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using YFAutomation.CargoDrones;

public static class CargoDroneCoreTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("FAIL: "+message);}
    static void Throws(Action action,string message){bool caught=false;try{action();}catch{caught=true;}Check(caught,message);}
    static CargoItem Item(int value,int count=1,int capacity=8){return new CargoItem(new[]{(byte)value,(byte)(value>>8)},count,capacity);}
    static CargoInventory Inventory(CargoItem[] items,Guid? id=null,Guid? incarnation=null,long revision=0,bool busy=false,bool[] locks=null,bool[] allowed=null,string owner="owner")
    {return new CargoInventory(id??Guid.NewGuid(),incarnation??Guid.NewGuid(),revision,owner,items,locks??new bool[items.Length],allowed??Enumerable.Repeat(true,items.Length).ToArray(),busy);}
    static CargoItem[] Empty(){return new CargoItem[6];}
    static Dictionary<string,long> Counts(params CargoItem[][] inventories)
    {
        var counts=new Dictionary<string,long>();foreach(var items in inventories)foreach(var item in items)if(item!=null)
        {string key=Convert.ToBase64String(item.Value);long n;counts.TryGetValue(key,out n);counts[key]=n+item.Count;}return counts;
    }
    static bool Conserved(CargoItem[] a,CargoItem[] b,CargoItem[] c,CargoItem[] d)
    {var first=Counts(a,b);var second=Counts(c,d);return first.Count==second.Count&&first.All(p=>second.ContainsKey(p.Key)&&second[p.Key]==p.Value);}

    sealed class Endpoint : ICargoDurableEndpoint
    {
        public CargoInventory Value;
        public Guid LastTransaction{get;private set;}
        public Guid Fence;
        public CargoSaveResult Save=CargoSaveResult.Pending;
        public bool ThrowApply,ThrowSave;
        public Endpoint(CargoInventory snapshot){Value=snapshot;}
        public CargoInventory Snapshot(){return Value;}
        public bool AcquireFence(Guid tx,long revision){if(Fence!=Guid.Empty||Value.Revision!=revision)return false;Fence=tx;return true;}
        public void Apply(CargoPlan plan,Guid tx)
        {
            if(ThrowApply)throw new IOException("Injected apply failure");
            if(Fence!=tx||!plan.Matches(Value))throw new InvalidOperationException();
            Value=Inventory(plan.EndpointAfter,Value.Id,Value.Incarnation,Value.Revision+1);LastTransaction=tx;
        }
        public void RequestDurableSave(Guid tx){if(ThrowSave)throw new IOException("Injected enqueue failure");}
        public CargoSaveResult PollDurableSave(Guid tx){return Save;}
        public void ReleaseFence(Guid tx){if(Fence!=tx)throw new InvalidOperationException();Fence=Guid.Empty;}
    }
    sealed class FailingJournal : ICargoJournal
    {
        public readonly List<CargoJournalKind> Writes=new List<CargoJournalKind>();
        public CargoJournalKind? Fail;
        public void Append(CargoJournalKind kind,CargoTransaction tx){if(Fail==kind)throw new IOException("Injected journal failure");Writes.Add(kind);}
    }
    sealed class TemporarilyUnavailableEndpoint : ICargoDurableEndpoint
    {
        readonly Endpoint inner;
        readonly int waitAt;
        int calls;
        public int Saves,Releases,Applies;
        public TemporarilyUnavailableEndpoint(Endpoint inner,int waitAt){this.inner=inner;this.waitAt=waitAt;}
        void Wait(){if(++calls==waitAt)throw new CargoEndpointUnavailableException();}
        public CargoInventory Snapshot(){Wait();return inner.Snapshot();}
        public Guid LastTransaction{get{Wait();return inner.LastTransaction;}}
        public bool AcquireFence(Guid tx,long revision){Wait();return inner.AcquireFence(tx,revision);}
        public void Apply(CargoPlan plan,Guid tx){Wait();inner.Apply(plan,tx);Applies++;}
        public void RequestDurableSave(Guid tx){Wait();inner.RequestDurableSave(tx);Saves++;}
        public CargoSaveResult PollDurableSave(Guid tx){Wait();return inner.PollDurableSave(tx);}
        public void ReleaseFence(Guid tx){Wait();inner.ReleaseFence(tx);Releases++;}
    }
    static CargoTransaction Transaction(CargoInventory inventory,Guid world)
    {return new CargoTransaction(Guid.NewGuid(),Guid.NewGuid(),world,0,CargoPlanner.Load(inventory,Empty(),"owner",6));}

    public static string Run(string directory)
    {
        checks=0;var rules=new CargoRules();var hub=new CargoPosition(0,0,0);
        Check(CargoWarehouseFilter.Matches("铁矿",CargoWarehouseKind.All,"钢制储物箱","基地铁矿","cntSteelWritableCrate"),"warehouse search uses authored sign text");
        Check(CargoWarehouseFilter.Matches("钢制",CargoWarehouseKind.All,"钢制储物箱","基地铁矿","cntSteelWritableCrate"),"warehouse block name remains searchable when a sign exists");
        Check(CargoWarehouseFilter.Matches("壁橱",CargoWarehouseKind.Cabinet,"旧式壁橱",null,"cntCupboardCabinetOldTopClosed"),"unsigned cabinet searchable by localized name");
        Check(CargoWarehouseFilter.Matches("",CargoWarehouseKind.Cabinet,"旧式壁橱",null,"cntCupboardCabinetOldTopClosed"),"type-only filter includes unsigned cabinets");
        Check(!CargoWarehouseFilter.Matches("铁矿",CargoWarehouseKind.Input,"钢制储物箱","铁矿","cntSteelWritableCrate"),"warehouse name and type filters combine");
        Check(CargoWarehouseFilter.Kind("yfAutoInput")==CargoWarehouseKind.Input&&CargoWarehouseFilter.Kind("yfAutoOutput")==CargoWarehouseKind.Output,"input and output boxes remain distinct types");
        Check(CargoWarehouseFilter.Matches("iron base",CargoWarehouseKind.All,"IRON crate","Base West","cntIronWritableCrate"),"warehouse keywords match case-insensitively across name and sign");
        Check(!CargoWarehouseFilter.Matches("",(CargoWarehouseKind)255,"箱子","", "cntWoodWritableCrate"),"unknown warehouse filter rejected");
        Check(CargoWarehouseFilter.Display("壁橱","",new CargoPosition(1,2,3))!=CargoWarehouseFilter.Display("壁橱","",new CargoPosition(2,2,3)),"unsigned duplicate names distinguished by coordinates");
        Check(CargoWarehouseFilter.Display("木箱","铁矿",hub).StartsWith("铁矿"),"authored warehouse sign takes display priority");
        Check(CargoWarehouseFilter.Clean("  铁矿\n\t<color> ")=="铁矿 ＜color＞","warehouse text normalizes line breaks and escapes rich-text brackets");
        Check(CargoSourceFilter.Matches("铁矿",CargoSourceKind.All,"Iron Miner","AutoMinerIron"),"miner picker searches the localized resource type");
        Check(CargoSourceFilter.Matches("",CargoSourceKind.Forestry,"自动林场","yfAutoForestry"),"source picker includes forestry by type");
        Check(!CargoSourceFilter.Matches("",CargoSourceKind.Forestry,"铁矿机","AutoMinerIron"),"source type filter excludes other resource producers");
        Check(!CargoSourceFilter.Matches("",CargoSourceKind.All,"露水收集器","cntDewCollector"),"source picker uses supported collector whitelist");
        Check(!CargoSourceFilter.Matches("",(CargoSourceKind)255,"铁矿机","AutoMinerIron"),"source picker rejects invalid types");
        Check(rules.CanCollect(hub,new CargoPosition(64,0,0)),"64 inclusive");
        Check(!rules.CanCollect(hub,new CargoPosition(64,1,0)),"3D radius excludes diagonal overshoot");
        Check(rules.CanCollect(hub,new CargoPosition(0,-64,0)),"vertical radius");
        Check(rules.CanDeliver(hub,new CargoPosition(800,0,0)),"800 delivery independent of collection");
        Check(rules.CanDeliver(hub,new CargoPosition(1000,0,0))&&!rules.CanDeliver(hub,new CargoPosition(1001,0,0)),"delivery boundary");
        Check(!rules.CanCollect(new CargoPosition(int.MinValue,0,0),new CargoPosition(int.MaxValue,0,0)),"coordinates do not overflow");
        Check(rules.CanDepart(600000,420)&&!rules.CanDepart(600000,420.001),"30 percent reserve");
        Check(!rules.CanDepart(600000,double.NaN),"NaN denied");Throws(()=>new CargoRules(speed:double.PositiveInfinity),"infinite speed denied");
        foreach(var name in new[]{"AutoMinerIron","AutoMinerLead","AutoMinerCoal","AutoMinerNitrate","AutoMinerClay","AutoMinerShale","AutoMinerBrass","yfAutoForestry"})Check(CargoRules.IsSource(name),"source "+name);
        Check(!CargoRules.IsSource("cntChickenCoop")&&!CargoRules.IsSource("cntApiary"),"independent source whitelist");
        var native=new[]{Item(1,6),Item(2,6)};var source=Inventory(native);var empty=Empty();var load=CargoPlanner.Load(source,empty,"owner",6);
        Check(load.Moved==6&&load.EndpointAfter[0]==null&&load.EndpointAfter[1].Count==6,"six packages, not six stacks");
        Check(CargoPlanner.Count(load.CargoAfter)==6&&load.CargoAfter.All(i=>i.Count==1),"single package cargo slots");
        Check(source.At(0).Count==6&&empty.All(i=>i==null),"planning has no side effects");
        var clone=load.EndpointAfter;clone[1]=null;Check(load.EndpointAfter[1]!=null,"plan array isolation");
        var payload=source.At(0).Value;payload[0]=99;Check(source.At(0).Value[0]==1,"opaque metadata isolation");
        Check(CargoPlanner.Load(source,Empty(),"other",6).Moved==6,"other placer does not restrict pickup");
        Check(CargoPlanner.Load(Inventory(native,busy:true),Empty(),"owner",6).Moved==0,"busy source denied");
        Check(CargoPlanner.Load(Inventory(native,allowed:new[]{false,true}),Empty(),"owner",6).CargoAfter.All(i=>i.Value[0]==2),"fuel/invalid output untouched");
        Check(CargoPlanner.Load(Inventory(native,locks:new[]{true,false}),Empty(),"owner",6).CargoAfter.All(i=>i.Value[0]==2),"source lock preserved");
        Throws(()=>CargoPlanner.Load(source,load.CargoAfter,"owner",6),"cannot load over stranded cargo");
        var lockedNow=Inventory(native,source.Id,source.Incarnation,locks:new[]{true,false});Check(!load.Matches(lockedNow),"lock changes revalidated even without version bump");
        Check(!load.Matches(Inventory(native,source.Id,Guid.NewGuid())),"same position replacement rejected");
        Check(load.Matches(Inventory(native,source.Id,source.Incarnation,owner:"other")),"owner label does not restrict the same unchanged instance");
        var target=Inventory(new[]{Item(1,6)});var unload=CargoPlanner.Unload(target,load.CargoAfter,"owner");
        Check(unload.Moved==2&&unload.EndpointAfter[0].Count==8&&CargoPlanner.Count(unload.CargoAfter)==4,"partial unload retains four");
        Check(Conserved(target.Items,load.CargoAfter,unload.EndpointAfter,unload.CargoAfter),"partial unload conserves metadata");
        Check(CargoPlanner.Unload(Inventory(new[]{Item(1,8)}),load.CargoAfter,"owner").Moved==0,"full target retains cargo");
        var mix=Inventory(new[]{Item(2,1),Item(1,7),null});var merged=CargoPlanner.Unload(mix,load.CargoAfter,"owner");
        Check(merged.EndpointAfter[0].Count==1&&merged.EndpointAfter[1].Count==8&&merged.EndpointAfter[2].Count==5,"merge before empty; no sample filtering");
        var lockedTarget=Inventory(new[]{Item(1,7),null},locks:new[]{true,false});
        Check(CargoPlanner.Unload(lockedTarget,load.CargoAfter,"owner").EndpointAfter[0].Count==7,"locked target preserved");

        var random=new Random(7919);
        for(int n=0;n<500;n++)
        {
            var inputs=Enumerable.Range(0,6).Select(i=>random.Next(3)==0?null:Item(random.Next(1,5),random.Next(1,7))).ToArray();
            var input=Inventory(inputs,locks:Enumerable.Range(0,6).Select(i=>random.Next(5)==0).ToArray());
            var plan=CargoPlanner.Load(input,Empty(),"owner",random.Next(7));
            Check(Conserved(inputs,Empty(),plan.EndpointAfter,plan.CargoAfter),"random loading conservation "+n);
            Check(CargoPlanner.Count(plan.CargoAfter)<=6,"random cargo bound "+n);
            var outputs=Enumerable.Range(0,random.Next(1,12)).Select(i=>random.Next(3)==0?null:Item(random.Next(1,5),random.Next(1,9))).ToArray();
            var destination=Inventory(outputs,locks:Enumerable.Range(0,outputs.Length).Select(i=>random.Next(4)==0).ToArray());
            var delivered=CargoPlanner.Unload(destination,plan.CargoAfter,"owner");
            Check(Conserved(outputs,plan.CargoAfter,delivered.EndpointAfter,delivered.CargoAfter),"random unload conservation "+n);
            Check(delivered.EndpointAfter.All(i=>i==null||i.Count<=i.Limit),"random target stack limit "+n);
        }
        var older=new CargoCandidate{EndpointId=Guid.NewGuid(),Available=true,Priority=0,WaitingSeconds=301,Occupancy=.1};
        var newer=new CargoCandidate{EndpointId=Guid.NewGuid(),Available=true,Priority=2,WaitingSeconds=20,Occupancy=1};
        Check(CargoScheduler.Select(new[]{newer,older})==older,"starvation protection overrides priority");
        older.Available=false;Check(CargoScheduler.Select(new[]{newer,older})==newer,"unavailable candidate skipped");
        var flight=new CargoFlight(Guid.NewGuid(),Guid.NewGuid(),600000);flight.Depart(false);
        Throws(()=>flight.Arrive(2,0),"remote arrival denied");flight.Arrive(.5,.1);
        Throws(()=>flight.BeginTransfer(),"arrival cannot bypass stable handling time");
        flight.AdvanceHandling(1500,.5,.1);flight.SetHold(CargoHold.OwnerOffline);flight.AdvanceHandling(9000,.5,.1);
        Check(flight.HandlingUnits==1500,"offline handling timer frozen");flight.SetHold(CargoHold.None);
        flight.AdvanceHandling(500,2,0);Check(flight.HandlingUnits==0,"leaving hover point resets stability");
        flight.AdvanceHandling(1999,.5,.1);Throws(()=>flight.BeginTransfer(),"handling boundary requires full two seconds");
        flight.AdvanceHandling(long.MaxValue,.5,.1);flight.BeginTransfer();flight.Recall();
        Check(flight.Phase==CargoPhase.Loading&&flight.TransferPending,"recall waits for transaction");flight.CompleteTransfer(6);Check(flight.Phase==CargoPhase.Returning,"recall returns cargo");
        flight.SetHold(CargoHold.OwnerOffline);flight.Consume(9999999);Check(flight.Battery==600000,"offline no energy consumption");
        flight.SetHold(CargoHold.None);flight.Consume(1000);Check(flight.Battery==599000,"active energy consumed");
        Throws(()=>flight.Consume(600000),"no energy overdraft");flight.Arrive(0,0);flight.Dock();Check(flight.Phase==CargoPhase.Docked,"dock sequence");

        var world=Guid.NewGuid();var transaction=Transaction(source,world);var endpoint=new Endpoint(source);var journal=new FailingJournal();
        var hubConfig=new CargoHubConfiguration(world,Guid.NewGuid(),new CargoPosition(0,0,0),"owner");
        var binding=new CargoBinding(world,Guid.NewGuid(),Guid.NewGuid(),new CargoPosition(64,0,0),"owner","yfAutoForestry");
        var rulesForHub=new CargoRules();var configured=hubConfig.AddSource("owner",0,binding,rulesForHub);
        Check(hubConfig.Sources.Length==0&&configured.Sources.Length==1&&configured.Revision==1,"binding edit keeps prior revision immutable");
        Check(configured.AddSource("owner",1,binding,rulesForHub)==configured,"duplicate binding is idempotent");
        Throws(()=>configured.AddSource("owner",0,binding,rulesForHub),"stale configuration edit rejected");
        Throws(()=>configured.SetPaused("other",1,true),"foreign hub edit rejected");
        var distant=new CargoBinding(world,Guid.NewGuid(),Guid.NewGuid(),new CargoPosition(1000,0,0),"owner","playerBox");
        var withTarget=configured.SetTarget("owner",1,distant,rulesForHub);
        Check(withTarget.Target==distant&&withTarget.Revision==2,"delivery range independent from collection radius");
        Throws(()=>configured.AddSource("owner",1,new CargoBinding(world,Guid.NewGuid(),Guid.NewGuid(),new CargoPosition(65,0,0),"owner","yfAutoForestry"),rulesForHub),"source outside collection radius rejected");
        Throws(()=>configured.SetTarget("owner",1,new CargoBinding(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new CargoPosition(1,0,0),"owner","playerBox"),rulesForHub),"cross-world binding rejected");
        Check(configured.SetTarget("owner",1,new CargoBinding(world,Guid.NewGuid(),Guid.NewGuid(),new CargoPosition(1,0,0),"other","playerBox"),rulesForHub).Target.Owner=="other","foreign endpoint retains metadata without restricting binding");
        Throws(()=>configured.AddSource("owner",1,new CargoBinding(world,Guid.NewGuid(),Guid.NewGuid(),binding.Position,"owner","yfAutoForestry"),rulesForHub),"replacement at same position cannot inherit binding");
        for(int n=1;n<8;n++)configured=configured.AddSource("owner",configured.Revision,new CargoBinding(world,Guid.NewGuid(),Guid.NewGuid(),new CargoPosition(n,0,0),"owner","yfAutoForestry"),rulesForHub);
        Throws(()=>configured.AddSource("owner",configured.Revision,new CargoBinding(world,Guid.NewGuid(),Guid.NewGuid(),new CargoPosition(9,0,0),"owner","yfAutoForestry"),rulesForHub),"eight-source limit enforced");
        var exposed=configured.Sources;exposed[0]=null;Check(configured.Sources[0]!=null,"source binding list cannot be mutated externally");
        var removed=configured.RemoveSource("owner",configured.Revision,binding.EndpointId);Check(removed.Sources.Length==7&&configured.Sources.Length==8,"unbind creates a new revision");
        var budget=new CargoChunkBudget(3,4);var f1=Guid.NewGuid();var f2=Guid.NewGuid();var l1=Guid.NewGuid();var l2=Guid.NewGuid();
        Check(budget.TryAcquire(l1,f1,new long[]{1,2}),"lease admitted");
        Check(budget.TryAcquire(l1,f1,new long[]{1,2})&&budget.LeaseCount==1,"duplicate lease idempotent");
        Check(!budget.TryAcquire(l1,f2,new long[]{1,2}),"lease cannot be stolen");
        Check(!budget.TryAcquire(Guid.NewGuid(),f1,new long[]{3,4})&&budget.HeldChunks==2,"per-flight limit includes current safe window");
        Check(budget.TryAcquire(l2,f2,new long[]{2,3,4})&&budget.HeldChunks==4,"overlap counted as union");
        Check(!budget.TryAcquire(Guid.NewGuid(),Guid.NewGuid(),new long[]{5}),"global budget enforced");
        Check(!budget.Release(l1,f2)&&budget.HeldChunks==4,"other flight cannot release safe lease");
        Check(budget.Release(l1,f1)&&budget.HeldChunks==3,"shared chunks remain held");
        Check(budget.Release(l1,f1),"release idempotent");budget.ReleaseFlight(f2);Check(budget.HeldChunks==0,"flight release returns baseline");
        var reserve=new CargoSourceReservation();var sourceId=Guid.NewGuid();
        Check(reserve.TryReserve(sourceId,f1,0,10),"source reserved");Check(!reserve.TryReserve(sourceId,f2,39,1),"source reservation excludes competing drone");
        Check(reserve.TryReserve(sourceId,f2,40,1),"expired reservation reusable");reserve.Release(sourceId,f1);
        Check(!reserve.TryReserve(sourceId,f1,41,1),"foreign release does not remove reservation");reserve.ReleaseFlight(f2);Check(reserve.TryReserve(sourceId,f1,42,1),"cancel releases source");
        var coordinator=new CargoTransferCoordinator(journal,endpoint,transaction);coordinator.Begin();coordinator.Poll();
        Check(coordinator.State==CargoTransferState.Saving&&endpoint.Fence==transaction.Id&&journal.Writes.Count==1,"no commit when save merely queued");
        endpoint.Save=CargoSaveResult.Durable;coordinator.Poll();coordinator.Poll();
        Check(coordinator.State==CargoTransferState.Committed&&endpoint.Fence==Guid.Empty&&journal.Writes.SequenceEqual(new[]{CargoJournalKind.Prepare,CargoJournalKind.Commit}),"durable ack commits once");
        Throws(()=>coordinator.Begin(),"cannot restart completed coordinator");
        Check(CargoTransferCoordinator.ClassifyRecovery(transaction,source,Guid.Empty,false)==CargoTransferState.Aborted,"prepared before image abort");
        Check(CargoTransferCoordinator.ClassifyRecovery(transaction,endpoint.Value,transaction.Id,false)==CargoTransferState.Committed,"prepared after image commit");
        Check(CargoTransferCoordinator.ClassifyRecovery(transaction,source,Guid.Empty,true)==CargoTransferState.RecoveryRequired,"committed endpoint rollback freezes");
        Check(CargoTransferCoordinator.ClassifyRecovery(transaction,endpoint.Value,Guid.NewGuid(),false)==CargoTransferState.RecoveryRequired,"unknown commit marker freezes");
        foreach(int failure in new[]{0,1,2,3,4})
        {
            var e=new Endpoint(source);var j=new FailingJournal();if(failure==0)j.Fail=CargoJournalKind.Prepare;if(failure==1)e.ThrowApply=true;if(failure==2)e.ThrowSave=true;if(failure==3)j.Fail=CargoJournalKind.Commit;
            var c=new CargoTransferCoordinator(j,e,Transaction(source,world));
            try{c.Begin();e.Save=failure==4?CargoSaveResult.Uncertain:CargoSaveResult.Durable;c.Poll();}catch(IOException){}
            Check(c.State==CargoTransferState.RecoveryRequired&&e.Fence!=Guid.Empty,"fault leaves fence for recovery "+failure);
        }

        string path=Path.Combine(directory,"journal-"+Guid.NewGuid().ToString("N")+".wal");
        long prepareLength;
        using(var file=new CargoFileJournal(path,world))
        {
            file.Append(CargoJournalKind.Prepare,transaction);prepareLength=new FileInfo(path).Length;
            Throws(()=>file.Append(CargoJournalKind.Prepare,transaction),"duplicate prepare denied");
            file.Append(CargoJournalKind.Commit,transaction);Throws(()=>file.Append(CargoJournalKind.Commit,transaction),"duplicate commit denied");
        }
        byte[] complete=File.ReadAllBytes(path);
        using(var reopened=new CargoFileJournal(path,world))Check(reopened.Entries.Count==2&&CargoPlanner.Equal(reopened.Entries[1].Transaction.Plan.CargoAfter,transaction.Plan.CargoAfter),"durable journal roundtrip");
        Throws(()=>{using(var wrongWorld=new CargoFileJournal(path,Guid.NewGuid())){}} ,"foreign world denied");
        foreach(int cut in new[]{0,1,7,8,(int)prepareLength-1,(int)prepareLength,(int)prepareLength+1,complete.Length-1})
        {
            string cutPath=Path.Combine(directory,"tail-"+cut+".wal");File.WriteAllBytes(cutPath,complete.Take(cut).ToArray());
            using(var reopened=new CargoFileJournal(cutPath,world))Check(reopened.Entries.Count==(cut>=prepareLength?1:0),"torn append recovered "+cut);
        }
        var corrupt=(byte[])complete.Clone();corrupt[20]^=1;string corruptPath=Path.Combine(directory,"corrupt.wal");File.WriteAllBytes(corruptPath,corrupt);
        Throws(()=>{using(var bad=new CargoFileJournal(corruptPath,world)){}} ,"checksum mismatch freezes, not silently truncates");
        var lengthCorrupt=(byte[])complete.Clone();lengthCorrupt[4]^=64;string headerPath=Path.Combine(directory,"bad-length.wal");File.WriteAllBytes(headerPath,lengthCorrupt);
        Throws(()=>{using(var bad=new CargoFileJournal(headerPath,world)){}} ,"corrupt length is not mistaken for incomplete final append");
        RecoveryChecks(world,source,directory);
        MotionChecks();
        RoutingChecks();
        ReturnChecks();
        MissionChecks();
        MissionWaitChecks();
        PreparedRecoveryChecks(directory);
        CheckpointChecks(directory);
        AccessSessionChecks();
        RecoveryDeliveryChecks(directory);
        RedeliveryChecks(directory);
        return "PASS checks="+checks+"; scope=L1 core rules, inventory plans, journal faults, continuous motion and swept bounds; native game, visible flight and multiplayer NOT tested.";
    }
    static void PreparedRecoveryChecks(string directory)
    {
        foreach(bool after in new[]{false,true})for(int waitAt=1;waitAt<=12;waitAt++)
        {
            var world=Guid.NewGuid();var source=Inventory(new[]{Item(1,6)});var tx=Transaction(source,world);var native=new Endpoint(source){Save=CargoSaveResult.Durable};
            if(after){native.AcquireFence(tx.Id,source.Revision);native.Apply(tx.Plan,tx.Id);native.ReleaseFence(tx.Id);}
            native.ThrowApply=true;
            var endpoint=new TemporarilyUnavailableEndpoint(native,waitAt);
            using(var journal=new CargoFileJournal(Path.Combine(directory,"recovery-wait-"+Guid.NewGuid().ToString("N")+".wal"),world))
            {
                journal.Append(CargoJournalKind.Prepare,tx);var recovery=new CargoPreparedRecovery(journal,endpoint,tx.Id);recovery.Begin();
                for(int tick=0;tick<30&&recovery.State==CargoTransferState.Saving;tick++)
                {Throws(()=>recovery.Result(),"temporarily unavailable recovery never exposes cargo early");recovery.Poll();}
                Check(recovery.State==(after?CargoTransferState.Committed:CargoTransferState.Aborted),"recovery resumes after native unavailability at each operation boundary");
                Check(endpoint.Applies==0&&endpoint.Saves==(after?1:0)&&endpoint.Releases==1&&native.Fence==Guid.Empty,"recovery retry never repeats apply, save request or completed release");
                Check(journal.Entries.Count==2&&CargoPlanner.Count(recovery.Result().Cargo)==(after?6:0),"temporary lock after WAL resolution does not append a duplicate resolution");
                Check(CargoPlanner.Equal(native.Value.Items,after?tx.Plan.EndpointAfter:tx.Plan.EndpointBefore),"temporary waits preserve exact native inventory image");
            }
        }
        foreach(int scenario in new[]{0,1,2,3})
        {
            var world=Guid.NewGuid();var source=Inventory(new[]{Item(1,6)});var tx=Transaction(source,world);var endpoint=new Endpoint(source);
            string path=Path.Combine(directory,"prepared-recovery-"+Guid.NewGuid().ToString("N")+".wal");
            using(var writer=new CargoFileJournal(path,world))writer.Append(CargoJournalKind.Prepare,tx);
            if(scenario!=0)
            {
                endpoint.AcquireFence(tx.Id,source.Revision);endpoint.Apply(tx.Plan,tx.Id);endpoint.ReleaseFence(tx.Id);
                endpoint.ThrowApply=true; // Recovery must never debit the source twice.
                if(scenario==2)endpoint.Value=Inventory(new[]{Item(99,1)},source.Id,source.Incarnation,source.Revision+1);
            }
            using(var reopened=new CargoFileJournal(path,world))
            {
                var recovery=new CargoPreparedRecovery(reopened,endpoint,tx.Id);recovery.Begin();
                if(scenario==0)
                {
                    Check(recovery.State==CargoTransferState.Aborted&&endpoint.Fence==Guid.Empty&&CargoPlanner.Count(recovery.Result().Cargo)==0,"prepared before-image recovery durably aborts without inventing cargo");
                    Check(reopened.Entries.Count==2&&reopened.Entries[1].Kind==CargoJournalKind.Abort,"before-image recovery records exactly one abort");
                }
                else if(scenario==2)
                {
                    Check(recovery.State==CargoTransferState.RecoveryRequired&&endpoint.Fence==tx.Id&&reopened.Entries.Count==1,"unknown recovered inventory retains fence and unresolved WAL");
                    Throws(()=>recovery.Result(),"unresolved recovery cannot expose spendable cargo");
                }
                else
                {
                    Check(recovery.State==CargoTransferState.Saving&&reopened.Entries.Count==1&&endpoint.Fence==tx.Id,"after-image recovery waits for fresh durable receipt without reapplying inventory");
                    recovery.Poll();Throws(()=>recovery.Result(),"pending native recovery cannot expose cargo");
                    endpoint.Save=scenario==3?CargoSaveResult.Uncertain:CargoSaveResult.Durable;recovery.Poll();recovery.Poll();
                    if(scenario==3)Check(recovery.State==CargoTransferState.RecoveryRequired&&endpoint.Fence==tx.Id&&reopened.Entries.Count==1,"uncertain recovery receipt preserves prepare and fence");
                    else
                    {
                        Check(recovery.State==CargoTransferState.Committed&&endpoint.Fence==Guid.Empty&&CargoPlanner.Count(recovery.Result().Cargo)==6&&reopened.Entries.Count==2,"durable recovered after-image commits cargo once");
                        Throws(()=>new CargoPreparedRecovery(reopened,endpoint,tx.Id),"resolved WAL transaction cannot be recovered a second time");
                    }
                }
                Throws(()=>recovery.Begin(),"prepared recovery cannot be begun twice");
            }
        }
    }
    static void CheckpointChecks(string directory)
    {
        for(int stage=0;stage<7;stage++)
        {
            Guid world=Guid.NewGuid();string root=Path.Combine(directory,"checkpoint-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            var source=Inventory(new[]{Item(1,6)});var target=Inventory(new CargoItem[6]);var input=new Endpoint(source);var output=new Endpoint(target);
            var mission=new CargoMission(world,Guid.NewGuid(),source.Id,target.Id,"owner",new Airspace(),new CargoPoint(0,100,0),new CargoPoint(20,100,0),new CargoPoint(20,100,20),600000);
            using(var journal=new CargoFileJournal(Path.Combine(root,"transactions.wal"),world))
            {
                for(int i=0;i<10;i++)mission.Tick(100);
                if(stage>=1){for(int i=0;i<1000&&mission.Phase!=CargoPhase.Loading;i++)mission.Tick(100);for(int i=0;i<7;i++)mission.Tick(100);}
                if(stage>=2)
                {
                    for(int i=0;i<100&&!mission.TransferReady;i++)mission.Tick(100);mission.BeginTransfer(journal,input);
                    Throws(()=>mission.Capture(),"pending native transfer cannot be hidden inside a navigation checkpoint");
                    input.Save=CargoSaveResult.Durable;mission.Tick(0);
                }
                if(stage>=3)for(int i=0;i<10;i++)mission.Tick(100);
                if(stage>=4){for(int i=0;i<1000&&mission.Phase!=CargoPhase.Unloading;i++)mission.Tick(100);for(int i=0;i<5;i++)mission.Tick(100);}
                if(stage>=5){for(int i=0;i<100&&!mission.TransferReady;i++)mission.Tick(100);mission.BeginTransfer(journal,output);output.Save=CargoSaveResult.Durable;mission.Tick(0);for(int i=0;i<5;i++)mission.Tick(100);}
                if(stage>=6)for(int i=0;i<1000&&mission.Phase!=CargoPhase.Docking;i++)mission.Tick(100);
                var captured=mission.Capture();int entries=journal.Entries.Count;
                using(var store=new CargoCheckpointStore(root,world))
                {
                    store.Save(new[]{captured},journal);store.Save(new[]{captured},journal);
                    Check(File.Exists(Path.Combine(root,"manifest.previous")),"checkpoint publication retains previous manifest");
                    Throws(()=>{using(var competing=new CargoCheckpointStore(root,world)){}},"checkpoint has one writer per directory");
                    var loaded=store.Load(journal).Single();var space=new Airspace();var restored=CargoMission.Restore(loaded,space,journal);
                    Check(restored.Position.Distance(mission.Position)<1e-9&&restored.Battery==mission.Battery&&restored.Phase==mission.Phase&&CargoPlanner.Equal(restored.Cargo,mission.Cargo)&&restored.CargoRevision==mission.CargoRevision,"checkpoint restores complete committed mission stage "+stage);
                    mission.Tick(100);restored.Tick(100);
                    Check(restored.Position.Distance(mission.Position)<1e-9&&restored.Battery==mission.Battery&&restored.Phase==mission.Phase,"restored next tick matches uninterrupted navigation and energy "+stage);
                    if(stage==0||stage==3||stage==5)
                    {
                        var blockedSpace=new Airspace{Wait=CargoHold.ChunkLoading};var blocked=CargoMission.Restore(loaded,blockedSpace,journal);blocked.Tick(100);
                        Check(blocked.Position.Distance(loaded.Motion.Position)<1e-9&&blocked.Battery==loaded.Battery&&blocked.Hold==CargoHold.ChunkLoading,"restored path waits for freshly loaded native data");
                    }
                    for(int tick=0;tick<2000&&restored.Phase!=CargoPhase.Docking;tick++)
                    {
                        if(restored.TransferReady){var endpoint=restored.Phase==CargoPhase.Loading?input:output;restored.BeginTransfer(journal,endpoint);endpoint.Save=CargoSaveResult.Durable;}
                        restored.Tick(100);
                    }
                    Check(restored.Phase==CargoPhase.Docking&&CargoPlanner.Count(restored.Cargo)==0&&CargoPlanner.Count(input.Value.Items)+CargoPlanner.Count(output.Value.Items)==6,"restored mission completes remaining transport without duplication "+stage);
                    if(journal.Entries.Count!=entries)Throws(()=>store.Load(journal),"stale checkpoint cannot roll back newer WAL cargo or energy");
                    store.Save(new[]{restored.Capture()},journal);var current=File.ReadAllBytes(Path.Combine(root,"manifest"));
                    foreach(int cut in new[]{0,1,11,current.Length-1})
                    {File.WriteAllBytes(Path.Combine(root,"manifest"),current.Take(cut).ToArray());Throws(()=>store.Load(journal),"torn manifest does not silently activate previous checkpoint");}
                    var corrupt=(byte[])current.Clone();corrupt[20]^=1;File.WriteAllBytes(Path.Combine(root,"manifest"),corrupt);Throws(()=>store.Load(journal),"corrupt checkpoint manifest rejected");
                    File.WriteAllBytes(Path.Combine(root,"manifest"),current);
                    Check(store.Load(journal).Single().Phase==CargoPhase.Docking,"valid manifest remains recoverable after damaged-file checks");
                    restored.CompleteDock(store,journal,new CargoMissionState[0]);
                    Check(restored.Phase==CargoPhase.Docked&&store.Load(journal).Single().Phase==CargoPhase.Docked,"docking completes only after saved docked checkpoint publication");
                    Throws(()=>restored.CompleteDock(store,journal,new CargoMissionState[0]),"completed docking cannot be published a second time by the same mission");
                }
                using(var reopened=new CargoCheckpointStore(root,world))Check(reopened.Load(journal).Single().Battery<=captured.Battery,"reopening checkpoint never grants new battery energy");
                Throws(()=>{using(var wrongWorld=new CargoCheckpointStore(root,Guid.NewGuid())){}},"another world cannot overwrite the checkpoint manifest");
            }
        }
    }
    static void AccessSessionChecks()
    {
        var connection=Guid.NewGuid();var token=Guid.NewGuid();var incarnation=Guid.NewGuid();
        var random=new Random(75231);
        for(int round=0;round<100;round++)
        {
            var received=new List<byte>();var session=new CargoAccessSession(connection,token,Guid.NewGuid(),incarnation,b=>received.Add(b[0]));
            var sequence=Enumerable.Range(1,12).OrderBy(i=>random.Next()).ToArray();
            Check(session.Close(connection,token,12)&&!session.Drained,"close notification alone never drains in-flight native snapshots");
            foreach(int seq in sequence)
            {
                session.Receive(connection,token,seq,new[]{(byte)seq});
                Check(session.Receive(connection,token,seq,new[]{(byte)seq})==CargoSessionDelivery.Duplicate,"duplicate inventory envelope is not applied twice");
            }
            Check(received.SequenceEqual(Enumerable.Range(1,12).Select(i=>(byte)i))&&session.Drained,"shuffled inventory tail is applied once in sequence before handoff");
            Check(!session.CanHandOff(true,incarnation)&&!session.CanHandOff(false,Guid.NewGuid())&&session.CanHandOff(false,incarnation),"native lock and endpoint incarnation independently gate cargo handoff");
        }
        var applied=new List<byte>();var gate=new CargoAccessSession(connection,token,Guid.NewGuid(),incarnation,b=>applied.Add(b[0]));
        Check(gate.Receive(Guid.NewGuid(),token,1,new byte[]{1})==CargoSessionDelivery.Rejected&&!gate.Faulted,"reconnected or foreign transport cannot consume another session");
        Check(!gate.Close(connection,Guid.NewGuid(),0)&&!gate.Drained,"stale close token cannot release endpoint");
        gate.Receive(connection,token,2,new byte[]{2});gate.Close(connection,token,2);
        Check(!gate.Drained&&!gate.CanHandOff(false,incarnation),"missing inventory envelope keeps endpoint unavailable without timeout guessing");
        gate.Receive(connection,token,1,new byte[]{1});Check(gate.Drained&&applied.SequenceEqual(new byte[]{1,2}),"late final gap completes exact close sequence");
        Check(gate.Receive(connection,token,3,new byte[]{3})==CargoSessionDelivery.Rejected&&gate.Faulted&&!gate.Drained,"writes beyond acknowledged close freeze the protocol instead of overwriting cargo");
        gate=new CargoAccessSession(connection,token,Guid.NewGuid(),incarnation,b=>{});gate.Receive(connection,token,2,new byte[]{2});
        Check(gate.Receive(connection,token,2,new byte[]{99})==CargoSessionDelivery.Rejected&&gate.Faulted,"conflicting duplicate buffered envelope faults session");
        gate=new CargoAccessSession(connection,token,Guid.NewGuid(),incarnation,b=>{});gate.Close(connection,token,0);gate.Disconnect();
        Check(!gate.CanHandOff(false,incarnation),"disconnect is not evidence that client inventory tail was delivered");
        gate=new CargoAccessSession(connection,token,Guid.NewGuid(),incarnation,b=>{throw new IOException("partial native read");});
        Throws(()=>gate.Receive(connection,token,1,new byte[]{1}),"native inventory application failure surfaces");
        Check(gate.Faulted&&gate.AppliedSequence==0&&!gate.Drained,"failed native application never acknowledges processed inventory");
        gate=new CargoAccessSession(connection,token,Guid.NewGuid(),incarnation,b=>{});
        Check(gate.Receive(connection,token,65,new byte[]{1})==CargoSessionDelivery.Rejected&&gate.Faulted,"session reordering buffer is bounded");
    }
    static void RecoveryDeliveryChecks(string directory)
    {
        foreach(bool uncertain in new[]{false,true})
        {
            var world=Guid.NewGuid();var source=Inventory(new[]{Item(5,6)});var target=Inventory(new CargoItem[6]);
            var mission=new CargoMission(world,Guid.NewGuid(),source.Id,target.Id,"owner",new Airspace(),new CargoPoint(0,100,0),new CargoPoint(4,100,0),new CargoPoint(10,100,0),600000);
            using(var journal=new CargoFileJournal(Path.Combine(directory,"recovery-delivery-"+Guid.NewGuid().ToString("N")+".wal"),world))
            {
                for(int i=0;i<300&&!mission.TransferReady;i++)mission.Tick(100);var input=new Endpoint(source);mission.BeginTransfer(journal,input);
                Check(!mission.FreezeForRecovery(),"destruction recovery cannot cancel a prepared source transfer");input.Save=CargoSaveResult.Durable;mission.Tick(0);
                Check(mission.FreezeForRecovery()&&mission.Phase==CargoPhase.RecoveryOnly,"committed flight cargo can freeze for destruction recovery");var frozen=mission.Position;long energy=mission.Battery;mission.Tick(1000);
                Check(mission.Position.Distance(frozen)==0&&mission.Battery==energy&&CargoPlanner.Count(mission.Cargo)==6,"recovery-only phase preserves goods and stops flight");
                var crate=Inventory(new CargoItem[6]);var endpoint=new Endpoint(crate);var recovery=new CargoRecoveryDelivery(mission,journal,crate.Id,crate.Incarnation);
                Throws(()=>recovery.Begin(new Endpoint(Inventory(new CargoItem[6],id:crate.Id))),"replacement recovery crate cannot inherit original delivery identity");
                recovery.Begin(endpoint);Check(CargoPlanner.Count(mission.Cargo)==6&&recovery.State==CargoRecoveryDeliveryState.Saving,"recovery cargo stays aboard until native crate save is confirmed");
                endpoint.Save=uncertain?CargoSaveResult.Uncertain:CargoSaveResult.Durable;recovery.Poll();
                if(uncertain)Check(recovery.State==CargoRecoveryDeliveryState.RecoveryRequired&&CargoPlanner.Count(mission.Cargo)==6&&endpoint.Fence!=Guid.Empty,"unknown recovery save keeps cargo quarantined and native crate fenced");
                else
                {
                    Check(recovery.State==CargoRecoveryDeliveryState.Completed&&CargoPlanner.Count(mission.Cargo)==0&&CargoPlanner.Count(endpoint.Value.Items)==6&&journal.Entries.Last().Transaction.Plan.Kind==CargoTransferKind.RecoverAtHub,"durable recovery transfers exact committed cargo once");
                    Throws(()=>recovery.Begin(endpoint),"completed recovery refuses repeated delivery");
                    Check(new CargoRecoveryDelivery(mission,journal,crate.Id,crate.Incarnation).State==CargoRecoveryDeliveryState.Completed,"reconstructed empty recovery cannot generate a second crate payload");
                }
            }
        }
    }
    static void RedeliveryChecks(string directory)
    {
        var world=Guid.NewGuid();var source=Inventory(new[]{Item(5,6)});var target=Inventory(new[]{Item(5,7)});var input=new Endpoint(source){Save=CargoSaveResult.Durable};var output=new Endpoint(target){Save=CargoSaveResult.Durable};
        var mission=new CargoMission(world,Guid.NewGuid(),source.Id,target.Id,"owner",new Airspace(),new CargoPoint(0,100,0),new CargoPoint(4,100,0),new CargoPoint(10,100,0),600000);
        string root=Path.Combine(directory,"redelivery-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        using(var journal=new CargoFileJournal(Path.Combine(root,"transactions.wal"),world))using(var store=new CargoCheckpointStore(root,world))
        {
            for(int tick=0;tick<1000&&mission.Phase!=CargoPhase.Docking;tick++)
            {if(mission.TransferReady)mission.BeginTransfer(journal,mission.Phase==CargoPhase.Loading?input:output);mission.Tick(100);}
            Check(mission.Phase==CargoPhase.Docking&&CargoPlanner.Count(mission.Cargo)==5,"partial delivery brings five committed packages back to hub");
            mission.CompleteDock(store,journal,new CargoMissionState[0]);long battery=mission.Battery;
            mission.ChargeDocked(100,false);mission.ChargeDocked(100,true,false);mission.ChargeDocked(100,true,true,true);
            Check(mission.Battery==battery,"charging freezes when unpowered offline or world paused");
            mission.ChargeDocked(100,true);Check(mission.Battery==Math.Min(600000,battery+1000),"docked charging uses the configured sixty-second full charge rate");
            for(int i=0;i<600;i++)mission.ChargeDocked(100,true);Check(mission.Battery==600000,"docked charge never exceeds battery capacity");
            output.Value=Inventory(new CargoItem[1],target.Id,target.Incarnation,output.Value.Revision+1);
            var retry=mission.RetryDelivery(new Airspace());store.Save(new[]{retry.Capture()},journal);
            Throws(()=>mission.RetryDelivery(new Airspace()),"old docked object cannot dispatch the same shipment twice");
            Throws(()=>mission.Capture(),"retired sortie cannot overwrite active redelivery checkpoint");
            Check(retry.Phase==CargoPhase.ToTarget&&retry.CargoRevision==mission.CargoRevision&&CargoPlanner.Equal(retry.Cargo,mission.Cargo),"redelivery preserves shipment identity and goes straight to immutable target");
            for(int tick=0;tick<1000&&retry.Phase!=CargoPhase.Docking;tick++)
            {if(retry.TransferReady){Check(retry.Phase==CargoPhase.Unloading,"redelivery never visits loading phase");retry.BeginTransfer(journal,output);}retry.Tick(100);}
            Check(retry.Phase==CargoPhase.Docking&&CargoPlanner.Count(retry.Cargo)==0&&CargoPlanner.Count(output.Value.Items)==5&&journal.Entries.Count(e=>e.Kind==CargoJournalKind.Prepare&&e.Transaction.Plan.Kind==CargoTransferKind.Load)==1,"old cargo is delivered without taking another source batch");
            var stale=new CargoTransaction(Guid.NewGuid(),retry.Capture().Id,world,2,CargoPlanner.Unload(output.Value,mission.Cargo,"owner"));
            int entries=journal.Entries.Count;
            Throws(()=>journal.Append(CargoJournalKind.Prepare,stale),"durable journal refuses obsolete cargo revision from another active object");
            Check(journal.Entries.Count==entries&&CargoPlanner.Count(output.Value.Items)==5,"rejected stale shipment leaves journal and native inventory unchanged");
        }
    }
    static void MissionChecks()
    {
        var world=Guid.NewGuid();var source=Inventory(new[]{Item(1,6)});var target=Inventory(new CargoItem[6]);
        var start=new CargoPoint(0,100,0);var pickup=new CargoPoint(8,100,0);var delivery=new CargoPoint(8,100,8);
        Func<long,CargoMission> create=battery=>new CargoMission(world,Guid.NewGuid(),source.Id,target.Id,"owner",new Airspace(),start,pickup,delivery,battery);
        var mission=create(600000);var input=new Endpoint(source);var output=new Endpoint(target);var journal=new FailingJournal();
        for(int i=0;i<300&&mission.Phase!=CargoPhase.Loading;i++)mission.Tick(100);
        Check(mission.Phase==CargoPhase.Loading&&!mission.TransferReady,"navigation arrival enters loading without early transfer");
        long before=mission.Battery;mission.Tick(5000,endpointHold:CargoHold.ContainerBusy);
        Check(mission.Battery==before-100&&!mission.TransferReady&&mission.BusyWaitUnits==100,"busy hover charges one capped simulation step without advancing handling");
        before=mission.Battery;
        for(int i=0;i<19;i++)mission.Tick(100);
        Check(!mission.TransferReady&&mission.Battery==before-1900,"handling consumes shared energy and requires full stable interval");
        mission.Tick(100);Check(mission.TransferReady,"two seconds makes load eligible");
        Throws(()=>mission.BeginTransfer(journal,output),"mission rejects wrong load endpoint");
        mission.BeginTransfer(journal,input);
        Throws(()=>mission.BeginTransfer(journal,input),"pending mission transfer cannot start twice");
        Check(mission.Hold==CargoHold.PersistencePending&&CargoPlanner.Count(mission.Cargo)==0&&mission.CargoRevision==0,"prepared native after-image is not yet spendable cargo");
        before=mission.Battery;mission.Tick(5000);Check(mission.Battery==before&&mission.Phase==CargoPhase.Loading,"pending save freezes navigation and energy");
        input.Save=CargoSaveResult.Durable;mission.Tick(0);
        Check(mission.Phase==CargoPhase.ToTarget&&CargoPlanner.Count(mission.Cargo)==6&&mission.CargoRevision==1,"durable commit advances mission and cargo exactly once");
        var visible=mission.Cargo;visible[0]=null;Check(CargoPlanner.Count(mission.Cargo)==6,"mission cargo exposure is immutable");
        for(int i=0;i<400&&!mission.TransferReady;i++)mission.Tick(100);
        Check(mission.Phase==CargoPhase.Unloading&&mission.TransferReady,"target arrival completes separate unload handling interval");
        mission.BeginTransfer(journal,output);output.Save=CargoSaveResult.Durable;mission.Tick(0);
        Check(mission.Phase==CargoPhase.Returning&&CargoPlanner.Count(mission.Cargo)==0&&mission.CargoRevision==2,"committed unload starts recorded home return");
        for(int i=0;i<500&&mission.Phase!=CargoPhase.Docking;i++)mission.Tick(100);
        Check(mission.Phase==CargoPhase.Docking&&mission.Position.Distance(start)<1e-7&&mission.Hold==CargoHold.PersistencePending,"home arrival awaits real durable docking checkpoint, not fake completed sortie");
        Check(journal.Writes.SequenceEqual(new[]{CargoJournalKind.Prepare,CargoJournalKind.Commit,CargoJournalKind.Prepare,CargoJournalKind.Commit}),"integrated mission writes exactly one prepare and commit per transfer");
        mission=create(600000);input=new Endpoint(source);journal=new FailingJournal();
        for(int i=0;i<400&&!mission.TransferReady;i++)mission.Tick(100);
        mission.BeginTransfer(journal,input);mission.Recall();var held=mission.Position;input.Save=CargoSaveResult.Durable;mission.Tick(100,false);
        Check(mission.Phase==CargoPhase.Returning&&mission.Hold==CargoHold.OwnerOffline&&CargoPlanner.Count(mission.Cargo)==6&&mission.Position.Distance(held)==0,"recall and owner disconnect resolve prepared load before freezing return");
        for(int i=0;i<300&&mission.Phase!=CargoPhase.Docking;i++)mission.Tick(100);
        Check(mission.Phase==CargoPhase.Docking&&CargoPlanner.Count(mission.Cargo)==6,"recalled committed cargo remains aboard at home");
        mission=create(600000);input=new Endpoint(source);journal=new FailingJournal();
        for(int i=0;i<400&&!mission.TransferReady;i++)mission.Tick(100);
        mission.BeginTransfer(journal,input);input.Save=CargoSaveResult.Uncertain;mission.Tick(100);held=mission.Position;mission.Recall();mission.Tick(100);
        Check(mission.Hold==CargoHold.RecoveryRequired&&mission.Position.Distance(held)==0&&CargoPlanner.Count(mission.Cargo)==0&&input.Fence!=Guid.Empty,"uncertain save keeps fence and freezes mission without inventing cargo or returning");
        mission=create(600000);input=new Endpoint(source);journal=new FailingJournal();
        for(int i=0;i<400&&!mission.TransferReady;i++)mission.Tick(100);
        var foreignFence=Guid.NewGuid();input.Fence=foreignFence;mission.BeginTransfer(journal,input);
        Check(mission.Phase==CargoPhase.Returning&&CargoPlanner.Count(mission.Cargo)==0&&journal.Writes.Count==0&&input.Fence==foreignFence,"failed fence admission aborts without writing journal or releasing another transaction");
        var small=Inventory(new[]{Item(1,7)});
        mission=new CargoMission(world,Guid.NewGuid(),source.Id,small.Id,"owner",new Airspace(),start,pickup,delivery,600000);
        input=new Endpoint(source);output=new Endpoint(small);journal=new FailingJournal();
        for(int i=0;i<400&&!mission.TransferReady;i++)mission.Tick(100);
        mission.BeginTransfer(journal,input);input.Save=CargoSaveResult.Durable;mission.Tick(0);
        for(int i=0;i<400&&!mission.TransferReady;i++)mission.Tick(100);
        mission.BeginTransfer(journal,output);output.Save=CargoSaveResult.Durable;mission.Tick(0);
        for(int i=0;i<500&&mission.Phase!=CargoPhase.Docking;i++)mission.Tick(100);
        Check(mission.Phase==CargoPhase.Docking&&CargoPlanner.Count(mission.Cargo)==5&&output.Value.Items[0].Count==8&&mission.CargoRevision==2,"partial unload returns only undelivered cargo and retains committed revision");
        mission=create(184000);
        for(int i=0;i<500&&mission.Phase!=CargoPhase.Docking;i++)mission.Tick(100);
        Check(mission.Phase==CargoPhase.Docking&&mission.Battery>=180000&&!mission.TransferReady,"handling reserve shortage recalls before spending return margin");
    }
    static void MissionWaitChecks()
    {
        var world=Guid.NewGuid();var home=new CargoPoint(0,100,0);var source=Inventory(new[]{Item(1,6)});var target=Inventory(new[]{Item(2,8)});
        Func<CargoMission> create=()=>new CargoMission(world,Guid.NewGuid(),source.Id,target.Id,"owner",new Airspace(),home,home,new CargoPoint(8,100,0),600000);
        var mission=create();mission.Tick(100);long before=mission.Battery;
        for(int i=0;i<49;i++)mission.Tick(100,endpointHold:CargoHold.ContainerBusy);
        Check(mission.Phase==CargoPhase.Loading&&mission.BusyWaitUnits==4900&&mission.Battery==before-4900,"locked source waits below exact five-second threshold and pays hover cost");
        mission.Tick(999999,false,endpointHold:CargoHold.ContainerBusy);mission.Tick(999999,true,true,endpointHold:CargoHold.ContainerBusy);
        mission.Tick(999999,endpointHold:CargoHold.ChunkLoading);mission.Tick(999999,endpointHold:CargoHold.ChunkBudget);
        Check(mission.BusyWaitUnits==4900&&mission.Battery==before-4900,"offline pause loading and budget holds do not advance busy timeout or energy");
        mission.Tick(100,endpointHold:CargoHold.ContainerBusy);
        Check(mission.Phase==CargoPhase.Returning&&mission.ReturnReason==CargoMissionReturnReason.ContainerBusy&&CargoPlanner.Count(mission.Cargo)==0,"five seconds at source recalls empty drone");
        mission=create();mission.Tick(100);for(int i=0;i<20;i++)mission.Tick(100);
        var emptySource=new Endpoint(Inventory(new CargoItem[1],source.Id,source.Incarnation));var journal=new FailingJournal();mission.BeginTransfer(journal,emptySource);
        Check(mission.ReturnReason==CargoMissionReturnReason.SourceEmpty&&mission.Phase==CargoPhase.Returning&&journal.Writes.Count==0,"empty source is not confused with a lock and returns without transaction");
        mission=create();mission.Tick(100);for(int i=0;i<20;i++)mission.Tick(100);
        var input=new Endpoint(source);mission.BeginTransfer(journal,input);input.Save=CargoSaveResult.Durable;mission.Tick(0);
        for(int i=0;i<400&&!mission.TransferReady;i++)mission.Tick(100);
        int writes=journal.Writes.Count;mission.BeginTransfer(journal,new Endpoint(target));
        Check(mission.ReturnReason==CargoMissionReturnReason.TargetFull&&mission.Phase==CargoPhase.Returning&&CargoPlanner.Count(mission.Cargo)==6&&journal.Writes.Count==writes,"full target returns committed cargo without starting empty unload");
        mission=create();mission.Tick(100);for(int i=0;i<20;i++)mission.Tick(100);
        input=new Endpoint(source);mission.BeginTransfer(new FailingJournal(),input);input.Save=CargoSaveResult.Durable;mission.Tick(0);
        for(int i=0;i<400&&mission.Phase!=CargoPhase.Unloading;i++)mission.Tick(100);
        for(int i=0;i<50;i++)mission.Tick(100,endpointHold:CargoHold.ContainerBusy);
        Check(mission.ReturnReason==CargoMissionReturnReason.ContainerBusy&&mission.Phase==CargoPhase.Returning&&CargoPlanner.Count(mission.Cargo)==6,"locked target timeout retains all committed cargo");
        mission=create();mission.Tick(100);for(int i=0;i<20;i++)mission.Tick(100);
        input=new Endpoint(source);mission.BeginTransfer(new FailingJournal(),input);before=mission.Battery;
        for(int i=0;i<100;i++)mission.Tick(100,endpointHold:CargoHold.ContainerBusy);
        Check(mission.Hold==CargoHold.PersistencePending&&mission.BusyWaitUnits==0&&mission.Battery==before&&mission.ReturnReason==CargoMissionReturnReason.None,"saving transaction is never cancelled by a busy deadline");
        mission=create();mission.Tick(100);for(int i=0;i<20;i++)mission.Tick(100);
        input=new Endpoint(Inventory(source.Items,source.Id,source.Incarnation,busy:true));mission.BeginTransfer(new FailingJournal(),input);mission.Tick(100);
        Check(mission.Hold==CargoHold.ContainerBusy&&mission.BusyWaitUnits==100&&mission.ReturnReason==CargoMissionReturnReason.None,"busy snapshot enters bounded lock wait instead of reporting empty source");
        mission=new CargoMission(world,Guid.NewGuid(),source.Id,target.Id,"owner",new Airspace(),home,home,home,180050);
        mission.Tick(100);mission.Tick(100,endpointHold:CargoHold.ContainerBusy);
        Check(mission.ReturnReason==CargoMissionReturnReason.EnergyLow&&mission.Phase==CargoPhase.Returning&&mission.Battery==180050&&mission.BusyWaitUnits==0,"busy hovering cannot spend protected return reserve even before timeout");
    }
    static void ReturnChecks()
    {
        var home=new CargoPoint(0,100,0);var space=new ObstacleAirspace();
        space.Obstacles.Add(new CargoBox(new CargoPoint(5,98,-2),new CargoPoint(7,102,2)));
        var motion=new CargoMotion(space,home,new CargoPoint(40,100,0),600000,returnReserve:180000);
        for(int i=0;i<2000&&!motion.Arrived;i++)motion.Tick(100);
        Check(motion.Arrived&&!motion.ReturningHome&&motion.ReturnWaypointCount>3&&motion.ReturnWaypointCount<30,"actual detour recorded with turns and bounded collinear compression");
        double outbound=motion.DistanceTravelled;long estimate=motion.EstimatedReturnUnits;long before=motion.Battery;
        motion.ReturnHome();motion.ReturnHome();
        for(int i=0;i<2000&&!motion.Arrived;i++)
        {
            var previous=motion.Position;motion.Tick(100);
            Check(space.Sweep(previous,motion.Position)==CargoSweep.Clear,"return corridor is traversed without crossing outbound wall");
        }
        Check(motion.Arrived&&motion.Position.Distance(home)<1e-7&&Math.Abs(motion.DistanceTravelled-2*outbound)<1e-6,"recall retraces actual outbound detour instead of direct shortcut");
        Check(before-motion.Battery<=estimate&&motion.EstimatedReturnUnits==0,"recorded return energy estimate bounds actual consumption and becomes zero at home");
        motion=new CargoMotion(new Airspace(),home,new CargoPoint(800,100,0),30000,returnReserve:10000);
        double furthest=0;
        for(int i=0;i<3000&&!(motion.ReturningHome&&motion.Arrived);i++){motion.Tick(100);furthest=Math.Max(furthest,motion.Position.Distance(home));}
        Check(motion.EnergyRecall&&motion.Arrived&&motion.Position.Distance(home)<1e-7&&furthest>0&&furthest<800,"insufficient round-trip energy triggers return before deeper flight");
        Check(motion.Battery>=10000&&motion.Battery+motion.MovingUnits==30000,"automatic return preserves reserve and exact movement accounting");
        Throws(()=>motion.Retarget(new CargoPoint(100,100,0)),"active returned motion cannot discard corridor for another sortie");
        var changed=new ObstacleAirspace();motion=new CargoMotion(changed,home,new CargoPoint(40,100,0),600000,returnReserve:180000);
        for(int i=0;i<2000&&!motion.Arrived;i++)motion.Tick(100);
        changed.Obstacles.Add(new CargoBox(new CargoPoint(25,98,-2),new CargoPoint(27,102,2)));motion.ReturnHome();
        for(int i=0;i<100;i++)motion.Tick(100);
        Check(motion.Hold==CargoHold.PathBlocked&&!motion.Arrived&&motion.Position.X>27,"new obstruction on historical return corridor freezes before wall");
        var held=motion.Position;long heldBattery=motion.Battery;for(int i=0;i<20;i++)motion.Tick(100);
        Check(motion.Position.Distance(held)==0&&motion.Battery==heldBattery,"blocked return does not teleport or spend frozen energy");
        changed.Obstacles.Clear();motion.RetryPath();for(int i=0;i<2000&&!motion.Arrived;i++)motion.Tick(100);
        Check(motion.Arrived&&motion.Position.Distance(home)<1e-7,"retry preserves remaining return corridor");
        var trail=new CargoReturnTrail(home);trail.Record(new CargoPoint(1,100,0));trail.Record(new CargoPoint(.5,100,0));
        Check(trail.Count==3,"opposite-direction samples are not merged across reversal");
        Throws(()=>trail.Record(new CargoPoint(100,100,0)),"trail rejects unverified long jump");
        trail.Record(home);Check(trail.Count==1&&trail.EstimateReturn(home,6,2)==0,"actually reaching home clears obsolete excursion history");
        motion=new CargoMotion(new Airspace(),home,new CargoPoint(8,100,0),600000,returnReserve:180000);
        for(int i=0;i<200&&!motion.Arrived;i++)motion.Tick(100);
        motion.Retarget(new CargoPoint(8,100,8));for(int i=0;i<200&&!motion.Arrived;i++)motion.Tick(100);
        double twoLegs=motion.DistanceTravelled;motion.ReturnHome();for(int i=0;i<500&&!motion.Arrived;i++)motion.Tick(100);
        Check(motion.Arrived&&Math.Abs(motion.DistanceTravelled-2*twoLegs)<1e-7,"source-to-target retarget preserves entire two-leg home corridor");
        var account=new CargoEnergy(600000);var flight=new CargoFlight(Guid.NewGuid(),Guid.NewGuid(),account);
        motion=new CargoMotion(new Airspace(),home,new CargoPoint(100,100,0),account,returnReserve:180000);
        motion.Tick(100);Check(flight.Battery==599900&&motion.Battery==flight.Battery,"flight and navigation use one energy account without mirrored movement deductions");
        flight.Consume(2000);Check(motion.Battery==597900,"flight handling consumption is immediately visible to return guard");
        Throws(()=>account.Consume(600000),"shared energy account rejects overdraft");
        Check(account.Remaining==597900,"failed energy debit cannot change balance");
        var random=new Random(7821);
        for(int trial=0;trial<30;trial++)
        {
            motion=new CargoMotion(new Airspace(),home,new CargoPoint(random.Next(-40,41),100,random.Next(-40,41)),600000,returnReserve:180000);
            for(int leg=0;leg<3;leg++)
            {
                for(int i=0;i<2500&&!motion.Arrived;i++)motion.Tick(17+random.Next(84));
                Check(motion.Arrived&&!motion.EnergyRecall,"random multi-leg route completes within initial budget");
                if(leg<2)motion.Retarget(new CargoPoint(random.Next(-40,41),100+random.Next(5),random.Next(-40,41)));
            }
            long expected=motion.EstimatedReturnUnits,balance=motion.Battery;motion.ReturnHome();
            for(int i=0;i<5000&&!motion.Arrived;i++)motion.Tick(17+random.Next(84));
            Check(motion.Arrived&&balance-motion.Battery<=expected&&motion.Battery>=180000,"random multi-leg measured return stays below energy estimate and above reserve");
        }
    }
    sealed class Airspace : ICargoAirspace
    {
        public CargoHold Wait;
        public CargoSweep Result;
        public int Sweeps,Reached,BlockOnSweep;
        public double Longest;
        public CargoHold Prepare(CargoPoint from,CargoPoint end){Longest=Math.Max(Longest,from.Distance(end));return Wait;}
        public CargoSweep Sweep(CargoPoint from,CargoPoint to){Sweeps++;return Sweeps==BlockOnSweep?CargoSweep.Blocked:Result;}
        public void ReachedSegment(CargoPoint at){Reached++;}
    }
    sealed class ObstacleAirspace : ICargoAirspace
    {
        public readonly List<CargoBox> Obstacles=new List<CargoBox>();
        public bool Unavailable;
        public int Sweeps;
        public CargoHold Prepare(CargoPoint from,CargoPoint end){return CargoHold.None;}
        public CargoSweep Sweep(CargoPoint from,CargoPoint to)
        {Sweeps++;if(Unavailable)return CargoSweep.Unavailable;return Obstacles.Any(b=>b.SweptHit(from,to))?CargoSweep.Blocked:CargoSweep.Clear;}
        public void ReachedSegment(CargoPoint at){}
    }
    static void RoutingChecks()
    {
        var origin=new CargoPoint(0,100,0);var goal=new CargoPoint(16,100,0);
        var space=new ObstacleAirspace();space.Obstacles.Add(new CargoBox(new CargoPoint(5,98,-2),new CargoPoint(7,102,2)));
        var route=new CargoLocalRoute(space,origin,goal,124);
        for(int i=0;i<100&&!route.Complete&&!route.Failed;i++)route.Advance();
        Check(route.Complete&&route.Probes<=256&&route.Waypoints.Any(p=>p.Y>100),"bounded local planner raises route above low wall");
        var previous=origin;
        foreach(var point in route.Waypoints){Check(previous.Distance(point)<=16.000001&&space.Sweep(previous,point)==CargoSweep.Clear,"every returned route edge is swept and segmented");previous=point;}
        Check(previous.Distance(goal)<1e-7,"detour rejoins original segment endpoint");
        space.Obstacles.Add(new CargoBox(new CargoPoint(-2,103,-30),new CargoPoint(20,130,30)));
        route=new CargoLocalRoute(space,origin,goal,124);
        for(int i=0;i<100&&!route.Complete&&!route.Failed;i++)route.Advance();
        Check(route.Complete&&route.Waypoints.Any(p=>Math.Abs(p.Z)>2)&&route.Waypoints.All(p=>p.Y<=124),"ceiling obstacle selects lateral detour");
        var motion=new CargoMotion(space,origin,new CargoPoint(40,100,0),600000);
        for(int i=0;i<2000&&!motion.Arrived;i++)
        {
            var before=motion.Position;motion.Tick(100);
            Check(space.Sweep(before,motion.Position)==CargoSweep.Clear&&before.Distance(motion.Position)<=.600001,"actual detour movement remains collision-free and continuous");
        }
        Check(motion.Arrived&&motion.DistanceTravelled>40&&motion.Battery+motion.MovingUnits==600000,"motion follows detour and charges actual longer route");
        var unavailable=new ObstacleAirspace{Unavailable=true};route=new CargoLocalRoute(unavailable,origin,goal,124);
        for(int i=0;i<10;i++)route.Advance();
        Check(!route.Failed&&!route.Complete&&route.Probes==0&&route.Hold==CargoHold.ChunkLoading,"unknown terrain suspends same candidate instead of pretending clear");
        unavailable.Unavailable=false;for(int i=0;i<100&&!route.Complete;i++)route.Advance();Check(route.Complete,"route resumes after data becomes available");
        var closed=new ObstacleAirspace();closed.Obstacles.Add(new CargoBox(new CargoPoint(4,0,-100),new CargoPoint(8,253,100)));
        motion=new CargoMotion(closed,origin,goal,600000);
        for(int i=0;i<100;i++)motion.Tick(100);
        int probes=closed.Sweeps;for(int i=0;i<100;i++)motion.Tick(100);
        Check(motion.Hold==CargoHold.PathBlocked&&motion.Position.Distance(origin)==0&&motion.Battery==600000&&closed.Sweeps==probes&&motion.RouteProbes<=256,"impossible route latches bounded failure without motion or infinite replanning");
        closed.Obstacles.Clear();motion.RetryPath();for(int i=0;i<500&&!motion.Arrived;i++)motion.Tick(100);
        Check(motion.Arrived,"explicit retry recovers after obstruction removed");
        var interrupted=new ObstacleAirspace();interrupted.Obstacles.Add(new CargoBox(new CargoPoint(5,98,-2),new CargoPoint(7,102,2)));
        motion=new CargoMotion(interrupted,origin,goal,600000);motion.Tick(100);int beforePause=interrupted.Sweeps;
        for(int i=0;i<20;i++)motion.Tick(100,false);
        Check(interrupted.Sweeps==beforePause&&motion.Battery==600000,"offline freeze also stops local planner probes");
        motion.Retarget(new CargoPoint(-8,100,0));
        for(int i=0;i<300&&!motion.Arrived;i++)motion.Tick(100);
        Check(motion.Arrived&&Math.Abs(motion.DistanceTravelled-8)<1e-7,"recall cancels unfinished detour without following obsolete waypoints");
        route=new CargoLocalRoute(space,new CargoPoint(0,251,0),new CargoPoint(16,251,0),275);
        for(int i=0;i<100&&!route.Complete&&!route.Failed;i++)route.Advance();
        Check(route.Complete&&route.Waypoints.All(p=>p.Y<=253),"local planner respects world ceiling even if caller ceiling is higher");
    }
    static void MotionChecks()
    {
        var start=new CargoPoint(0,100,0);var end=new CargoPoint(800,100,0);
        var wall=new CargoBox(new CargoPoint(5,99,-1),new CargoPoint(6,101,1));
        Check(wall.SweptHit(start,new CargoPoint(10,100,0)),"continuous sweep catches wall between endpoints");
        Check(wall.SweptHit(new CargoPoint(0,100,1.8),new CargoPoint(10,100,1.8)),"body tangent blocks");
        Check(!wall.SweptHit(new CargoPoint(0,100,1.81),new CargoPoint(10,100,1.81)),"clear parallel path remains clear");
        Check(wall.SweptHit(new CargoPoint(5.5,100,0),new CargoPoint(5.5,100,0)),"stationary overlapping body blocks");
        Check(!wall.SweptHit(start,new CargoPoint(4,100,0)),"obstacle past segment excluded");
        Check(new CargoPoint(-.01,100,-16.01).Cell.X==-1&&new CargoPoint(-.01,100,-16.01).Cell.Z==-17,"negative cell floors correctly");
        Throws(()=>new CargoPoint(double.NaN,0,0),"nonfinite position denied");
        Throws(()=>start.Toward(end,double.PositiveInfinity),"nonfinite movement distance denied");
        foreach(var wait in new[]{CargoHold.ChunkLoading,CargoHold.PathBlocked,CargoHold.RecoveryRequired})
        {
            var space=new Airspace{Wait=wait};var motion=new CargoMotion(space,start,end,600000);motion.Tick(100);
            Check(motion.Position.Distance(start)==0&&motion.Battery==600000&&space.Sweeps==0,"waiting cannot advance or consume movement energy "+wait);
            space.Wait=CargoHold.None;motion.Tick(100);Check(motion.Position.Distance(start)>.59,"cleared hold resumes movement "+wait);
        }
        foreach(var result in new[]{CargoSweep.Blocked,CargoSweep.Unavailable})
        {
            var space=new Airspace{Result=result};var motion=new CargoMotion(space,start,end,600000);motion.Tick(100);
            Check(motion.Position.Distance(start)==0&&motion.Battery==600000,"failed sweep cannot move "+result);
        }
        var dynamicSpace=new Airspace{BlockOnSweep=2};var dynamicMotion=new CargoMotion(dynamicSpace,start,end,600000);dynamicMotion.Tick(100);
        Check(dynamicMotion.Hold==CargoHold.PathBlocked&&dynamicMotion.DistanceTravelled==0&&dynamicMotion.Battery==600000,"obstacle appearing after segment validation blocks actual slice");
        var air=new Airspace();var flight=new CargoMotion(air,start,end,600000);flight.Tick(60000);
        Check(Math.Abs(flight.DistanceTravelled-.6)<1e-8&&flight.MovingUnits==100,"lag spike capped at one movement step");
        var pausedAt=flight.Position;var energy=flight.Battery;flight.Tick(100,false);flight.Tick(100,true,true);
        Check(flight.Position.Distance(pausedAt)==0&&flight.Battery==energy,"offline and paused movement frozen");
        for(int i=0;i<2000&&!flight.Arrived;i++)
        {
            var before=flight.Position;flight.Tick(100);Check(before.Distance(flight.Position)<=.60000001,"outbound flight has no teleport");
        }
        Check(flight.Arrived&&flight.Position.Distance(end)<1e-7&&air.Longest<=16.0000001,"800 block flight uses bounded rolling segments");
        Check(Math.Abs(flight.DistanceTravelled-800)<1e-6&&flight.Battery+flight.MovingUnits==600000,"distance and movement energy conserved");
        flight.Retarget(start);
        for(int i=0;i<2000&&!flight.Arrived;i++)flight.Tick(100);
        Check(flight.Arrived&&Math.Abs(flight.DistanceTravelled-1600)<1e-6&&flight.Battery>300000,"recall returns continuously with remaining energy");
        flight.Tick(100,false);flight.Tick(100,true);
        Check(flight.Hold==CargoHold.None&&flight.Arrived,"arrival clears stale offline hold after reconnection");
        var near=new CargoMotion(new Airspace(),start,new CargoPoint(3,100,0),10000);near.Tick(100);
        Check(Math.Abs(near.DistanceTravelled-.2)<1e-8,"final approach uses slow speed");
        var empty=new CargoMotion(new Airspace(),start,end,99);empty.Tick(100);
        Check(empty.Hold==CargoHold.RecoveryRequired&&empty.DistanceTravelled==0&&empty.Battery==99,"insufficient energy cannot move or become negative");
    }
    static void RecoveryChecks(Guid world,CargoInventory source,string directory)
    {
        var tx=Transaction(source,world);
        var prepare=new CargoJournalEntry(1,CargoJournalKind.Prepare,tx);var commit=new CargoJournalEntry(2,CargoJournalKind.Commit,tx);
        var pending=CargoJournalReplay.Read(world,new[]{prepare}).Single();
        Check(pending.Pending==tx&&pending.Revision==0&&CargoPlanner.Count(pending.Cargo)==0,"unresolved prepare cannot mint usable cargo");
        var committed=CargoJournalReplay.Read(world,new[]{prepare,commit}).Single();
        Check(committed.Pending==null&&committed.Revision==1&&CargoPlanner.Equal(committed.Cargo,tx.Plan.CargoAfter),"replay recovers committed cargo once");
        var array=committed.Cargo;array[0]=null;Check(committed.Cargo[0]!=null,"recovered cargo is immutable");
        var destination=Inventory(new[]{Item(1,7)});
        var delivery=new CargoTransaction(Guid.NewGuid(),tx.FlightId,world,1,CargoPlanner.Unload(destination,committed.Cargo,"owner"));
        var unloadPrepare=new CargoJournalEntry(3,CargoJournalKind.Prepare,delivery);
        var unloadCommit=new CargoJournalEntry(4,CargoJournalKind.Commit,delivery);
        var partial=CargoJournalReplay.Read(world,new[]{prepare,commit,unloadPrepare,unloadCommit}).Single();
        Check(partial.Revision==2&&CargoPlanner.Count(partial.Cargo)==5,"partial unload replay retains undelivered cargo");
        string path=Path.Combine(directory,"replay-"+Guid.NewGuid().ToString("N")+".wal");
        using(var file=new CargoFileJournal(path,world))
        {file.Append(CargoJournalKind.Prepare,tx);file.Append(CargoJournalKind.Commit,tx);file.Append(CargoJournalKind.Prepare,delivery);}
        using(var file=new CargoFileJournal(path,world))
        {
            var resumed=CargoJournalReplay.Read(world,file.Entries).Single();
            Check(resumed.Revision==1&&resumed.Pending.Id==delivery.Id&&CargoPlanner.Count(resumed.Cargo)==6,"real WAL restart preserves uncertain unload and pre-transfer cargo");
            file.Append(CargoJournalKind.Commit,delivery);
        }
        using(var file=new CargoFileJournal(path,world))
        {
            var resumed=CargoJournalReplay.Read(world,file.Entries).Single();
            Check(resumed.Revision==2&&resumed.Pending==null&&CargoPlanner.Equal(resumed.Cargo,partial.Cargo),"real WAL restart rebuilds exactly the partial delivery remainder");
        }
        var foreignTarget=Inventory(new CargoItem[6],owner:"different-owner");
        var stolen=new CargoTransaction(Guid.NewGuid(),tx.FlightId,world,1,CargoPlanner.Unload(foreignTarget,committed.Cargo,"different-owner"));
        Throws(()=>CargoJournalReplay.Read(world,new[]{prepare,commit,new CargoJournalEntry(3,CargoJournalKind.Prepare,stolen)}),"replay cannot transfer cargo to a new flight owner");
        var aborted=CargoJournalReplay.Read(world,new[]{prepare,new CargoJournalEntry(2,CargoJournalKind.Abort,tx)}).Single();
        Check(aborted.Revision==0&&CargoPlanner.Count(aborted.Cargo)==0&&aborted.Pending==null,"aborted load retains empty cargo and revision");
        Throws(()=>CargoJournalReplay.Read(world,new[]{prepare,commit,commit}),"duplicate commit history rejected");
        Throws(()=>CargoJournalReplay.Read(Guid.NewGuid(),new[]{prepare}),"replay rejects foreign world");
        Throws(()=>CargoJournalReplay.Read(world,new[]{new CargoJournalEntry(1,CargoJournalKind.Prepare,delivery)}),"missing cargo history cannot be reconstructed from before image");
        var competing=Transaction(source,world);
        Throws(()=>CargoJournalReplay.Read(world,new[]{prepare,new CargoJournalEntry(2,CargoJournalKind.Prepare,competing)}),"two flights cannot hold one endpoint fence");
        Throws(()=>CargoJournalReplay.Read(world,new[]{prepare,new CargoJournalEntry(2,CargoJournalKind.Commit,competing)}),"mismatched resolution cannot commit another flight");
        var fakeAfter=tx.Plan.EndpointAfter;fakeAfter[0]=Item(99,1);
        Throws(()=>new CargoTransaction(Guid.NewGuid(),tx.FlightId,world,0,new CargoPlan(source,source.Items,fakeAfter,Empty(),tx.Plan.CargoAfter,6,CargoTransferKind.Load)),"invalid transfer cannot replace endpoint value");
        var fakeCargo=tx.Plan.CargoAfter;fakeCargo[0]=Item(99);
        Throws(()=>new CargoTransaction(Guid.NewGuid(),tx.FlightId,world,0,new CargoPlan(source,source.Items,tx.Plan.EndpointAfter,Empty(),fakeCargo,6,CargoTransferKind.Load)),"equal counts cannot hide substituted cargo values");
        Check(CargoTransferCoordinator.ClassifyRecovery(tx,source,tx.Id,false)==CargoTransferState.RecoveryRequired,"before inventory with after stamp freezes instead of aborting");
        Check(CargoTransferCoordinator.ClassifyRecovery(tx,null,Guid.Empty,false)==CargoTransferState.RecoveryRequired,"missing endpoint freezes cargo recovery");
        var endpoint=new Endpoint(source);var journal=new FailingJournal();var coordinator=new CargoTransferCoordinator(journal,endpoint,tx);coordinator.Begin();
        endpoint.Value=Inventory(endpoint.Value.Items,source.Id,source.Incarnation,source.Revision+1,owner:"different-owner");
        endpoint.Save=CargoSaveResult.Durable;coordinator.Poll();
        Check(coordinator.State==CargoTransferState.Committed&&journal.Writes.Count==2&&endpoint.Fence==Guid.Empty,"owner label change does not prevent an unchanged instance from committing");
    }
}
