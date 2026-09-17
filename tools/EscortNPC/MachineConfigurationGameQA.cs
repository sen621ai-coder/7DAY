using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using Platform;
using HarmonyLib;
using YFAutomation;

// Explicit opt-in, isolated-world-only native integration checks. Never runs in a player save.
public sealed class MachineConfigurationGameQA : IModApi
{
    static World world;static float deadline;static bool waiting;static string reportPath;
    static readonly List<string> report=new List<string>();
    static int checks;static PlatformUserIdentifierAbs localIdentity;
    public static bool Identity(ref PlatformUserIdentifierAbs __result){__result=localIdentity;return false;}
    public void InitMod(Mod mod)
    {
        if(!Environment.GetCommandLineArgs().Contains("-yfMachineConfigurationQA"))return;
        ModEvents.GameStartDone.RegisterHandler(Ready);ModEvents.GameUpdate.RegisterHandler(Update);
    }
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;report.Add("PASS "+message);}
    static void Ready(ref ModEvents.SGameStartDoneData data)
    {
        if(GamePrefs.GetString(EnumGamePrefs.GameName)!="AutomationConfigQA_Isolated")return;
        world=GameManager.Instance.World;reportPath=Path.Combine(GameIO.GetSaveGameDir(),"machine-configuration-qa.txt");
        GameManager.Instance.AddChunkObserver(new Vector3(8,160,8),false,4,4);deadline=Time.realtimeSinceStartup+180;waiting=true;
    }
    static void Update(ref ModEvents.SGameUpdateData data)
    {
        if(!waiting)return;
        if(!world.IsChunkAreaLoaded(new Vector3(8,160,8))&&Time.realtimeSinceStartup<deadline)return;
        waiting=false;
        try{Run();report.Add("FINISHED checks="+checks+" failures=0");}
        catch(Exception e){report.Add("FAIL "+e);}
        finally{File.WriteAllLines(reportPath,report);Application.Quit();}
    }
    static TileEntityComposite Place(string name,Vector3i p,PlatformUserIdentifierAbs owner)
    {
        // Repeatable fixture: setting an identical existing block does not fire native placement hooks.
        world.SetBlockRPC(new BlockValueRef(p),BlockValue.Air);
        world.SetBlockRPC(new BlockValueRef(p),Block.GetBlockValue(name));var t=world.GetTileEntity(p) as TileEntityComposite;
        Check(t!=null,"place "+name);t.SetOwner(owner);return t;
    }
    static void Run()
    {
        var wires=WireManager.Instance;
        if(wires.activeWires==null)wires.Init();
        var wire=wires.GetWireNodeFromPool();
        wire.SetWireCanHide(true);
        wire.SetStartPosition(new Vector3(8,160,8));wire.SetEndPosition(new Vector3(10,160,8));wire.BuildMesh();
        wire.SetVisible(false);
        Check(wire.GetGameObject().activeSelf,"ordinary native wire remains visible without wire tool");
        wires.ToggleAllWirePulse(false);
        Check(wire.GetGameObject().activeSelf&&!wires.ShowPulse,"putting wire tool away preserves wire visibility without pulse");
        wires.ReturnToPool(wire);
        Check(!wire.GetGameObject().activeSelf&&!wires.activeWires.Contains(wire),"removed wire stays hidden in native pool");
        var owner=PlatformUserIdentifierAbs.FromCombinedString("Steam_76561198000000001",false);
        world.SetBlockRPC(new BlockValueRef(6,160,8),BlockValue.Air);
        var existing=world.GetTileEntity(new Vector3i(8,160,8)) as TileEntityComposite;
        if(existing!=null){var saved=MachineSettingsStorage.Load(Path.Combine(GameIO.GetSaveGameDir(),"automation-machine-settings.xml")).Machines.FirstOrDefault(s=>s.Position=="8,160,8");if(saved!=null)Check(MachineConfiguration.Get(existing).Product==saved.Product,"server reload restores persisted machine configuration");}
        var player=(EntityPlayer)EntityFactory.CreateEntity(EntityClass.FromString("playerMale"),new Vector3(8,160,8));player.entityId=900001;player.MinEventContext.ItemValue=ItemValue.None;
        var pp=GameManager.Instance.persistentPlayers.CreatePlayerData(owner,owner,"AutomationQA",default(EPlayGroup));pp.EntityId=player.entityId;GameManager.Instance.persistentPlayers.MapPlayer(pp);
        var m=Place("yfAutoSmelter",new Vector3i(8,160,8),owner);
        var input=Place("yfAutoInput",new Vector3i(9,160,8),owner);
        var output=Place("yfAutoOutput",new Vector3i(8,160,9),owner);
        Check(MachineConfiguration.CanAccess(world,m,player),"owner configuration access");
        var options=MachineConfiguration.Boxes(world,m,true);Check(options.Contains(input),"adjacent input candidate");
        Check(MachineConfiguration.Boxes(world,m,false).Contains(output),"near output candidate");
        var config=MachineConfiguration.Get(m).Clone();Check(!config.Paused&&config.Product=="","old-world defaults");
        config.Source=MachineConfiguration.Key(input.ToWorldPos());config.Target=MachineConfiguration.Key(output.ToWorldPos());config.Product="yfAutoIngot_iron";
        string token=MachineConfiguration.Token(m);
        Check(MachineConfiguration.Apply(world,m,player,config,token)=="已保存","save configured product and boxes");
        Check(MachineConfiguration.Apply(world,m,player,config,token).Contains("其他玩家"),"stale concurrent edit rejected by server");
        config=MachineConfiguration.Get(m).Clone();
        Check(MachineConfiguration.Apply(world,m,player,config,"wrong").Contains("重新加载"),"wrong machine token rejected by server");
        player.position=new Vector3(100,160,100);Check(!MachineConfiguration.CanAccess(world,m,player),"distant actor rejected");player.position=new Vector3(8,160,8);
        var stranger=PlatformUserIdentifierAbs.FromCombinedString("Steam_76561198000000002",false);m.SetOwner(stranger);
        Check(!MachineConfiguration.CanAccess(world,m,player),"foreign owner rejected");m.SetOwner(owner);
        config.Source="100,160,100";Check(MachineConfiguration.Apply(world,m,player,config,token).Contains("所选箱子"),"forged target coordinates rejected");config=MachineConfiguration.Get(m).Clone();
        config.Product="resourceWood";Check(MachineConfiguration.Apply(world,m,player,config,token).Contains("不支持"),"unsupported product rejected");
        config=MachineConfiguration.Get(m).Clone();m.bUserAccessing=true;Check(MachineConfiguration.Apply(world,m,player,config,token).Contains("正在被使用"),"save respects native edit lock");m.bUserAccessing=false;
        var items=input.GetFeature<TEFeatureStorage>().items;items[0]=new ItemStack(ItemClass.GetItem("resourceScrapIron"),2);
        var outItems=output.GetFeature<TEFeatureStorage>().items;Check(outItems[0].IsEmpty(),"explicit production starts without output sample");
        string status="";for(int i=0;i<300;i++){status=Production.Step(m,input,output,null);if(status.StartsWith("完成"))break;}
        Check(status.StartsWith("完成")&&items[0].count==1&&outItems[0].IsEmpty(),"configured production consumes once and preserves reserved slot");
        Check(outItems.Skip(1).Sum(s=>s.count)==ItemClass.GetItem("resourceScrapIron").ItemClass.GetWeight(),"native material weight output");
        config=MachineConfiguration.Get(m).Clone();config.Paused=true;Check(MachineConfiguration.Apply(world,m,player,config,token)=="已保存","pause saved");
        Check(Production.Step(m,input,output,null).Contains("暂停")&&items[0].count==1,"paused machine does not consume");
        config=MachineConfiguration.Get(m).Clone();config.Paused=false;m.GetFeature<TEFeatureAutomationState>().Seconds=5;
        config.Product="yfAutoIngot_lead";Check(MachineConfiguration.Apply(world,m,player,config,token)=="已保存"&&m.GetFeature<TEFeatureAutomationState>().Seconds==0,"switching product resets progress without consuming inventory");
        var disk=MachineSettingsStorage.Load(Path.Combine(GameIO.GetSaveGameDir(),"automation-machine-settings.xml"));
        Check(disk.Machines.Single().Product=="yfAutoIngot_lead","world settings saved to disk");
        localIdentity=owner;new Harmony("yf.configuration.qa.identity").Patch(AccessTools.PropertyGetter(typeof(PlatformManager),"InternalLocalUserIdentifier"),prefix:new HarmonyMethod(typeof(MachineConfigurationGameQA),nameof(Identity)));
        var original=new MachineSettings{Product="resourceWood",Paused=true,Revision=7,StorageMode="internal"};
  using(var stream=new System.IO.MemoryStream()){
   var writer=new PooledBinaryWriter();writer.SetBaseStream(stream);var reader=new PooledBinaryReader();reader.SetBaseStream(stream);
   var packet=new NetPackageYFAutomationConfigRequest{At=new Vector3i(-2,100,7),Request=13,Save=true,Token="test-token",Value=original.Clone()};
   packet.write(writer);writer.Flush();Check(stream.Length==packet.GetLength(),"request advertised wire length");stream.Position=2;
   var received=new NetPackageYFAutomationConfigRequest();received.read(reader);
   Check(received.At==packet.At&&received.Save&&received.Token==packet.Token&&received.Request==13&&received.Value.Product==original.Product&&received.Value.StorageMode=="internal"&&stream.Position==stream.Length,"complete request packet round trip");
   stream.SetLength(0);stream.Position=0;
   var reply=new NetPackageYFAutomationConfigReply{At=packet.At,Request=13,Allowed=true,Message="已保存",Kind="yfAutoSorter",Token="test-token",Value=original.Clone()};
   reply.write(writer);writer.Flush();Check(stream.Length==reply.GetLength(),"reply advertised wire length includes UTF8 message");stream.Position=2;
   var response=new NetPackageYFAutomationConfigReply();response.read(reader);
   Check(response.Allowed&&response.Message==reply.Message&&response.Kind==reply.Kind&&response.Token==reply.Token&&response.Value.Revision==7&&stream.Position==stream.Length,"complete server snapshot packet round trip");
  }

        var commands=m.block.GetBlockActivationCommands(world,m.blockValue,m.ToWorldPos(),player);
        Check(commands.Any(c=>c.enabled&&c.text==MachineConfigurationUI.Command),"native configuration activation command enabled");
        foreach(var kind in new[]{"yfAutoKitchen","yfAutoForge","yfAutoRecycler","yfAutoFarm","yfAutoMiner","yfAutoSorter"})
            Check(MachineConfiguration.Products(kind).Count>0,"native supported product catalog: "+kind);
        InventoryChecks(m,player,owner);
        ForgeChecks(player,owner);
        world.SetBlockRPC(new BlockValueRef(m.ToWorldPos()),BlockValue.Air);
        Check(MachineSettingsStorage.Load(Path.Combine(GameIO.GetSaveGameDir(),"automation-machine-settings.xml")).Machines.Count==0,"removing machine deletes saved settings");
    }
    static void InventoryChecks(TileEntityComposite m,EntityPlayer player,PlatformUserIdentifierAbs owner)
    {
        Check(MachineInventory.Has(m)&&MachineInventory.UsesInternal(m),"new machine defaults to 36 native internal slots");
        var storage=m.GetFeature<TEFeatureStorage>();var progress=m.GetFeature<TEFeatureAutomationState>();
        var c=MachineConfiguration.Get(m).Clone();c.StorageMode="internal";c.Product="yfAutoIngot_iron";c.Source="";c.Target="";
        Check(MachineConfiguration.Apply(world,m,player,c,MachineConfiguration.Token(m))=="已保存","switch to internal mode without boxes");
        int iron=ItemClass.GetItem("resourceScrapIron").type,product=ItemClass.GetItem("yfAutoIngot_iron").type;
        storage.items[0]=new ItemStack(new ItemValue(iron),2);
        string status="";for(int i=0;i<300;i++){status=Production.Step(m,m,m,null);if(status.StartsWith("完成"))break;}
        Check(status.StartsWith("完成")&&storage.items[0].count==1,"same inventory commit consumes exactly once");
        int yield=ItemClass.GetForId(iron).GetWeight();
        Check(storage.items.Skip(18).Sum(v=>v.count)==yield&&storage.items[18].itemValue.type==product,"internal production uses output partition including its first slot");
        for(int i=18;i<36;i++)storage.items[i]=new ItemStack(ItemClass.GetItem("resourceWood"),ItemClass.GetItem("resourceWood").ItemClass.Stacknumber.Value);
        float before=progress.Seconds;status=Production.Step(m,m,m,null);
        Check(status.Contains("满")&&storage.items[0].count==1&&progress.Seconds==before,"full output does not consume input or advance progress");
        for(int i=0;i<36;i++)storage.items[i]=ItemStack.Empty;
        storage.items[18]=new ItemStack(new ItemValue(iron),2);
        Check(!Production.Step(m,m,m,null).StartsWith("生产中")&&storage.items[18].count==2,"output inventory cannot be consumed as ingredients");
        storage.items[0]=new ItemStack(new ItemValue(iron),2);storage.items[18]=new ItemStack(new ItemValue(product),7);
        progress.Job="migration-test";progress.Seconds=3;
        var all=m.modulesInternalOrder;
        foreach(bool legacy in new[]{false,true})using(var stream=new MemoryStream())
        {
            if(legacy)m.modulesInternalOrder=all.Where(f=>!(f is TEFeatureStorage)&&!(f is TEFeatureMachineInventory)).ToArray();
            var w=new PooledBinaryWriter();w.SetBaseStream(stream);
            try{m.write(w,TileEntity.StreamModeWrite.Persistency);w.Flush();}finally{m.modulesInternalOrder=all;}
            stream.Position=0;var reader=new PooledBinaryReader();reader.SetBaseStream(stream);
            var restored=new TileEntityComposite((Chunk)world.GetChunkFromWorldPos(m.ToWorldPos()),m.blockValue);restored.localChunkPos=m.localChunkPos;
            restored.read(reader,TileEntity.StreamModeRead.Persistency);
            Check(restored.Owner.Equals(owner)&&restored.GetFeature<TEFeatureAutomationState>().Seconds==3&&restored.GetFeature<TEFeatureAutomationState>().Job=="migration-test","native V18 owner and progress survive "+(legacy?"old feature layout":"new inventory layout"));
            Check(restored.GetFeature<TEFeatureMachineInventory>().Legacy==legacy,"native feature migration selects correct inventory default");
            var slots=restored.GetFeature<TEFeatureStorage>().items;
            Check(slots.Length==36&&(legacy?slots.All(v=>v.IsEmpty()):slots[0].count==2&&slots[18].count==7),"native inventory save/load or empty initialization");
        }
        LockManager.Instance.singleLocks.Add(player.entityId,new LockEntry(storage,0));
        try
        {
            m.bUserAccessing=true;c=MachineConfiguration.Get(m).Clone();
            Check(MachineConfiguration.Apply(world,m,player,c,MachineConfiguration.Token(m))=="已保存","own native storage lock permits configuration save");
            Check(!MachineInventoryUI.OwnsStorageLock(m,player.entityId+1),"another actor cannot claim inventory lock");
        }
        finally{LockManager.Instance.singleLocks.RemoveByKey(player.entityId);m.bUserAccessing=false;}
        var sorter=Place("yfAutoSorter",new Vector3i(11,160,8),owner);
        var ss=sorter.GetFeature<TEFeatureStorage>();ss.items[0]=new ItemStack(new ItemValue(iron),20);
        MachineInventory.PassThrough(sorter,"resourceScrapIron");
        Check(ss.items[0].count==4&&ss.items[18].count==16,"sorter transfers between internal partitions without duplication");
        MachineInventory.PassThrough(sorter,"resourceWood");
        Check(ss.items[0].count==4&&ss.items[18].count==16,"internal sorter obeys selected filter");
        // Native belt Step, native tiles and native powered port; deterministic power state fixture.
        var belt=Place("yfAutoBeltStraight",new Vector3i(11,160,7),owner);
        var bv=world.GetBlock(belt.ToWorldPos());
        for(byte r=0;r<24;r++){bv.rotation=r;if(bv.Block.SupportsRotation(r)&&ConveyorPath.Offset(bv,Vector3.forward)==new Vector3i(0,0,1))break;}
        world.SetBlockRPC(new BlockValueRef(belt.ToWorldPos()),bv);belt=(TileEntityComposite)world.GetTileEntity(new Vector3i(11,160,7));belt.SetOwner(owner);
        var pp=new Vector3i(12,160,7);world.SetBlockRPC(new BlockValueRef(pp),Block.GetBlockValue("yfAutoPowerPort"));
        var port=world.GetTileEntity(pp) as TileEntityPowered;
        if(port==null){var chunk=(Chunk)world.GetChunkFromWorldPos(pp);port=((BlockPowered)world.GetBlock(pp).Block).CreateTileEntity(chunk);port.localChunkPos=Chunk.ToLocalPosition(pp);chunk.AddTileEntity(port);}
        port.InitializePowerData();port.PowerItem.isPowered=true;
        Conveyors.Observe(belt,world);var step=AccessTools.Method(typeof(Conveyors),"Step");
        belt.GetFeature<TEFeatureStorage>().items[0]=new ItemStack(new ItemValue(iron),5);
        step.Invoke(null,new object[]{new[]{belt}});
        Check(ss.items.Take(18).Sum(v=>v.count)==9&&ss.items[18].count==16&&belt.GetFeature<TEFeatureStorage>().items[0].IsEmpty(),"belt end inserts into machine input only: input="+ss.items.Take(18).Sum(v=>v.count)+" output="+ss.items[18].count+" belt="+belt.GetFeature<TEFeatureStorage>().items[0].count+" status="+belt.GetFeature<TEFeatureAutomationState>().Job+" internal="+MachineInventory.UsesInternal(sorter)+" powered="+port.IsPowered+" exit="+ConveyorPath.Offset(world.GetBlock(belt.ToWorldPos()),Vector3.forward)+" owner="+MachineConfiguration.Owner(belt)+" destination="+sorter.ToWorldPos());
        // Reverse belt: its entry now faces the machine and output pickup must ignore raw inputs.
        for(byte r=0;r<24;r++){bv.rotation=r;if(bv.Block.SupportsRotation(r)&&ConveyorPath.Offset(bv,Vector3.forward)==new Vector3i(0,0,-1))break;}
        world.SetBlockRPC(new BlockValueRef(belt.ToWorldPos()),bv);belt=(TileEntityComposite)world.GetTileEntity(new Vector3i(11,160,7));belt.SetOwner(owner);
        step.Invoke(null,new object[]{new[]{belt}});
        Check(ss.items.Take(18).Sum(v=>v.count)==9&&ss.items[18].IsEmpty()&&belt.GetFeature<TEFeatureStorage>().items[0].count==16,"belt entry extracts only machine output");
        var savedCount=ss.items.Sum(v=>v.count);ss.items[18]=new ItemStack(new ItemValue(iron),3);sorter.bUserAccessing=true;
        belt.GetFeature<TEFeatureStorage>().items[0]=ItemStack.Empty;step.Invoke(null,new object[]{new[]{belt}});
        Check(ss.items[18].count==3&&belt.GetFeature<TEFeatureStorage>().items[0].IsEmpty(),"native open inventory blocks conveyor extraction");sorter.bUserAccessing=false;
        c=MachineConfiguration.Get(sorter).Clone();c.StorageMode="external";
        Check(MachineConfiguration.Apply(world,sorter,player,c,MachineConfiguration.Token(sorter))=="已保存"&&!MachineInventory.UsesInternal(sorter),"external mode preserves stored items and disconnects internal belt endpoints");
        step.Invoke(null,new object[]{new[]{belt}});Check(ss.items[18].count==3,"legacy mode belt does not remove internal cargo");
        world.SetBlockRPC(new BlockValueRef(sorter.ToWorldPos()),BlockValue.Air);
        world.SetBlockRPC(new BlockValueRef(belt.ToWorldPos()),BlockValue.Air);
        world.SetBlockRPC(new BlockValueRef(pp),BlockValue.Air);
    }

    static void ForgeChecks(EntityPlayer player,PlatformUserIdentifierAbs owner)
    {
        var forge=Place("yfAutoForge",new Vector3i(6,160,8),owner);var store=forge.GetFeature<TEFeatureStorage>();
        var config=MachineConfiguration.Get(forge).Clone();config.Product="resourceForgedSteel";config.StorageMode="internal";
        Check(MachineConfiguration.Apply(world,forge,player,config,MachineConfiguration.Token(forge))=="已保存","select steel for direct raw-material production");
        string empty=RecipePreview.Describe(world,forge,config,player);
        Check(empty.Contains("缺")&&empty.Contains("工具"),"steel preview explains missing ingredients and crucible");
        store.items[0]=new ItemStack(ItemClass.GetItem("resourceScrapIron"),500);
        store.items[1]=new ItemStack(ItemClass.GetItem("resourceClayLump"),100);
        store.items[2]=new ItemStack(ItemClass.GetItem("toolForgeCrucible"),1);
        var plan=RecipePlan.Select("resourceForgedSteel","yfAutoForge",store.items,i=>!MachineInventory.IsInput(i),player);
        Check(plan!=null&&plan.Ready,"native steel recipe accepts ordinary iron, clay and crucible");
        string ready=RecipePreview.Describe(world,forge,config,player);
        Check(ready.Contains("材料与工具已齐")&&ready.Contains("需要")&&ready.Contains("已有"),"server preview and production share the same adjusted recipe plan");
        Check(RecipePreview.Describe(world,forge,config,null).Contains("所有者上线"),"preview does not substitute another player's recipe modifiers");
        string status="";for(int i=0;i<5000;i++){status=Production.Step(forge,forge,forge,player);if(status.StartsWith("完成"))break;}
        int steel=ItemClass.GetItem("resourceForgedSteel").type;
        Check(status.StartsWith("完成")&&store.items.Skip(18).Any(v=>v.itemValue.type==steel&&v.count>0),"native steel finishes from raw materials without an intermediate machine");
        Check(store.items[2].count==1&&store.items.Take(18).Zip(plan.Input.Take(18),(a,b)=>a.count==b.count&&a.itemValue.type==b.itemValue.type).All(v=>v),"steel consumes exactly the displayed plan and retains its tool");
        for(int i=0;i<36;i++)store.items[i]=ItemStack.Empty;
        store.items[0]=new ItemStack(ItemClass.GetItem("yfAutoIngot_iron"),500);
        store.items[1]=new ItemStack(ItemClass.GetItem("yfAutoIngot_clay"),100);
        store.items[2]=new ItemStack(ItemClass.GetItem("toolForgeCrucible"),1);
        plan=RecipePlan.Select("resourceForgedSteel","yfAutoForge",store.items,i=>!MachineInventory.IsInput(i),player);
        Check(plan!=null&&plan.Ready,"existing refined-material pipeline remains usable");
        using(var stream=new MemoryStream())
        {
            var writer=new PooledBinaryWriter();writer.SetBaseStream(stream);var reader=new PooledBinaryReader();reader.SetBaseStream(stream);
            var request=new NetPackageYFAutomationRecipeRequest{At=forge.ToWorldPos(),Request=88,Draft=config};request.write(writer);writer.Flush();
            Check(stream.Length==request.GetLength(),"recipe preview request wire length");stream.Position=2;var requestCopy=new NetPackageYFAutomationRecipeRequest();requestCopy.read(reader);
            Check(requestCopy.Draft.Product==config.Product&&requestCopy.Request==88&&stream.Position==stream.Length,"recipe preview request round trip");
            stream.SetLength(0);stream.Position=0;var reply=new NetPackageYFAutomationRecipeReply{At=forge.ToWorldPos(),Request=88,Text=ready};reply.write(writer);writer.Flush();
            Check(stream.Length==reply.GetLength(),"recipe preview reply UTF8 wire length");stream.Position=2;var replyCopy=new NetPackageYFAutomationRecipeReply();replyCopy.read(reader);
            Check(replyCopy.Text==ready&&replyCopy.Request==88&&stream.Position==stream.Length,"recipe preview reply round trip");
        }
        world.SetBlockRPC(new BlockValueRef(forge.ToWorldPos()),BlockValue.Air);
    }

}

