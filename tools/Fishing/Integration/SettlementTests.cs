using System;
using PZAEC.Fishing.Contracts;
using PZAEC.Fishing.Runtime;

internal static class IntegrationEntryPoint
{
    public static int Main(string[] args)
    {
        if(ModulesHarness.Main(args)!=0)return 1;
        try {
            var content=new PZAEC.Fishing.Content.FishingContent();var config=content.Load(args[0]);
            SettlementTests.Run(content,config);return 0;
        } catch(Exception error){Console.WriteLine("RESULT FAIL "+error);return 1;}
    }
}

internal static class SettlementTests
{
    sealed class Record : ICatchRecord {
        public CatchResult Result {get;private set;}
        public bool Completed {get;private set;}
        public void Write(CatchResult result,bool completed){Result=result;Completed=completed;}
    }
    sealed class Inventory : ICatchInventory {
        public bool Full,Throw;public int Added;
        public bool TryAdd(RewardSpec spec,Guid id){if(Throw)throw new Exception("native failure");if(Full)return false;Added+=spec.Count;return true;}
    }
    sealed class Water : IWaterQuery {
        public bool Blocked,Unloaded,Dry;public float Depth=3;
        public SegmentHit TraceSolid(Vec3 a,Vec3 b)=>new SegmentHit{Obstructed=Blocked,Unloaded=Unloaded};
        public WaterSample SampleColumn(Vec3 p,float above,float below)=>new WaterSample{
            Status=Dry?WaterSampleStatus.Dry:WaterSampleStatus.Valid,DepthMeters=Depth,SurfacePoint=new Vec3(p.X,0,p.Z)};
    }
    static int checks;
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
    public static void Run(IFishingContent content,FishingConfig config)
    {
        var record=new Record();var bag=new Inventory();var settlement=new CatchSettlement(content,record,bag,"local");
        var state=new FishingSnapshot {SessionId=Guid.NewGuid(),Tick=500,Authority=AuthorityMode.Standalone,Phase=FishingPhase.Fighting,FishMassKg=config.Fish.MassKg};
        Check(settlement.OpenSession(state.SessionId),"session authorized before accepting a landing");
        Check(!settlement.RecordLanding(state,config.Fish.Id),"non-terminal physics cannot authorize reward");
        state.Phase=FishingPhase.Resolved;state.Authority=AuthorityMode.PredictedClient;
        Check(!settlement.RecordLanding(state,config.Fish.Id),"predicted client cannot authorize reward");
        state.Authority=AuthorityMode.Standalone;
        Check(settlement.RecordLanding(state,config.Fish.Id)&&settlement.HasPending,"authoritative landing creates pending record");
        var valid=settlement.Pending;var forged=valid;forged.MassKg+=1;
        Check(settlement.TrySettle(forged)==SettlementStatus.Invalid&&bag.Added==0,"altered reward mass is rejected");
        forged=valid;forged.PlayerPersistentId="other";
        Check(settlement.TrySettle(forged)==SettlementStatus.Invalid,"foreign player reward rejected");
        bag.Full=true;Check(settlement.TrySettle(valid)==SettlementStatus.InventoryFull&&settlement.HasPending&&bag.Added==0,"full bag retains pending catch without mutation");
        var next=state;next.SessionId=Guid.NewGuid();
        Check(!settlement.RecordLanding(next,config.Fish.Id),"pending catch cannot be overwritten by new landing");
        settlement=new CatchSettlement(content,record,bag,"local");bag.Full=false;
        Check(settlement.TrySettle(settlement.Pending)==SettlementStatus.Granted&&bag.Added==FishCatalog.MeatCount(state.FishMassKg)&&!settlement.HasPending,"reconstructed runtime delivers retained catch");
        Check(settlement.RecordLanding(state,config.Fish.Id)&&settlement.TrySettle(valid)==SettlementStatus.AlreadyGranted&&bag.Added==FishCatalog.MeatCount(state.FishMassKg),"replayed landing does not duplicate reward");
        settlement=new CatchSettlement(content,record,bag,"local");
        Check(settlement.TrySettle(valid)==SettlementStatus.AlreadyGranted&&bag.Added==FishCatalog.MeatCount(state.FishMassKg),"completed record survives runtime reconstruction");
        Check(!settlement.RecordLanding(next,config.Fish.Id),"unknown session cannot fabricate a landing");
        Check(settlement.OpenSession(next.SessionId)&&settlement.RecordLanding(next,config.Fish.Id)&&settlement.TrySettle(valid)==SettlementStatus.Invalid,"old catch rejected after next authorized landing");
        bag.Throw=true;try{settlement.TrySettle(settlement.Pending);}catch(Exception){}
        bag.Throw=false;Check(settlement.TrySettle(settlement.Pending)==SettlementStatus.Deferred&&bag.Added==FishCatalog.MeatCount(state.FishMassKg),"uncertain inventory failure prevents automatic retry");
        foreach(var species in FishCatalog.Ids){
            var sized=FishCatalog.Size(FishCatalog.Profile(config.Fish,species),1.75f);
            var speciesRecord=new Record();var speciesBag=new Inventory{Full=true};
            var speciesSettlement=new CatchSettlement(content,speciesRecord,speciesBag,"local");
            var terminal=new FishingSnapshot{SessionId=Guid.NewGuid(),Tick=123,Authority=AuthorityMode.Standalone,Phase=FishingPhase.Resolved,FishMassKg=sized.MassKg};
            Check(speciesSettlement.OpenSession(terminal.SessionId)&&speciesSettlement.RecordLanding(terminal,species),"weighted species landing recorded");
            Check(speciesSettlement.TrySettle(speciesSettlement.Pending)==SettlementStatus.InventoryFull,"weighted meat batch retained when full");
            speciesBag.Full=false;speciesSettlement=new CatchSettlement(content,speciesRecord,speciesBag,"local");
            Check(speciesSettlement.TrySettle(speciesSettlement.Pending)==SettlementStatus.Granted&&speciesBag.Added==FishCatalog.MeatCount(sized.MassKg),"reconstructed species reward preserves full meat quantity");
            Check(speciesSettlement.TrySettle(speciesSettlement.Pending)==SettlementStatus.AlreadyGranted,"weighted meat batch cannot be duplicated");
        }
        var water=new Water();Vec3 target;
        Check(CastTargeting.TryFind(water,new Vec3(0,2,0),new Vec3(0,-.25f,1),Vec3.Zero,25,.6f,out target)&&Math.Abs(target.Z-8)<.001f,"cast intersects aimed water surface");
        water.Blocked=true;Check(!CastTargeting.TryFind(water,new Vec3(0,2,0),new Vec3(0,-.25f,1),Vec3.Zero,25,.6f,out target),"cast cannot cross solid obstacle");
        water.Blocked=false;water.Depth=.2f;Check(!CastTargeting.TryFind(water,new Vec3(0,2,0),new Vec3(0,-.25f,1),Vec3.Zero,25,.6f,out target),"shallow water rejected");
        water.Depth=3;water.Unloaded=true;Check(!CastTargeting.TryFind(water,new Vec3(0,2,0),new Vec3(0,-.25f,1),Vec3.Zero,25,.6f,out target),"unloaded cast path rejected");
        water.Unloaded=false;water.Dry=true;Check(!CastTargeting.TryFind(water,new Vec3(0,2,0),new Vec3(0,-.25f,1),Vec3.Zero,25,.6f,out target),"dry cast rejected");
        water.Dry=false;Check(!CastTargeting.TryFind(water,new Vec3(0,2,0),new Vec3(0,-.25f,1),Vec3.Zero,5,.6f,out target),"out-of-range water rejected");
        Check(!CastTargeting.TryFind(water,new Vec3(0,2,0),Vec3.Up,Vec3.Zero,25,.6f,out target),"upward aim rejected");
        Console.WriteLine("RESULT PASS settlementAndTargetChecks="+checks);
    }
}
