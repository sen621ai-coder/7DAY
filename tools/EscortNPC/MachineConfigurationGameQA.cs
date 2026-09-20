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
        wire.SetVisible(true);
        Check(wire.GetGameObject().activeSelf,"native wire appears while the wire tool is held");
        wire.SetVisible(false);
        Check(!wire.GetGameObject().activeSelf,"native wire hides when the wire tool is put away");
        wires.ToggleAllWirePulse(false);
        Check(!wire.GetGameObject().activeSelf&&!wires.ShowPulse,"putting wire tool away hides wire without pulse");
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
        WorkbenchChecks(player,owner);
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

    static void WorkbenchChecks(EntityPlayer player,PlatformUserIdentifierAbs owner)
    {
        const string kind="yfAutoWorkbench";
        var table=Place(kind,new Vector3i(6,160,8),owner);var storage=table.GetFeature<TEFeatureStorage>();
        Check(Production.IsMachine(kind)&&MachineDisplay.IsMachine(kind)&&MachineInventory.UsesInternal(table),"automatic workbench supports production, interaction and internal conveyors");
        var products=MachineConfiguration.Products(kind);
        foreach(var product in new[]{"resourceGunPowder","ammo9mmBulletBall","ammo762mmBulletBall","ammoShotgunShell"})
        {
            Check(products.Contains(product),"automatic workbench lists "+product);
            var config=MachineConfiguration.Get(table).Clone();config.Product=product;config.StorageMode="internal";
            Check(MachineConfiguration.Apply(world,table,player,config,MachineConfiguration.Token(table))=="已保存","configure automatic workbench "+product);
            var recipe=CraftingManager.GetRecipes(product).First(r=>RecipeMachines.Supports(kind,r)&&r.IsUnlocked(player));
            FillRecipe(storage,recipe,player);
            var plan=RecipePlan.Select(product,kind,storage.items,i=>!MachineInventory.IsInput(i),player);
            Check(plan!=null&&plan.Ready,"actual ingredient plan is ready for "+product);
            Check(RecipePreview.Describe(world,table,config,player).Contains("材料与工具已齐"),"server preview is available for "+product);
            var before=ProductionInventory.Clone(storage.items);
            Check(Production.Step(table,table,table,null).Contains("所有者上线")&&SameItems(before,storage.items),"offline owner cannot craft "+product);
            float modifier=recipe.tags.Test_AnySet(XUiM_Recipes.SandboxIgnoreTag)?1:XUiM_Recipes.CraftingOutputModifier;
            int expected=Math.Max(1,(int)(EffectManager.GetValue(PassiveEffects.CraftingOutputCount,null,recipe.count,player,recipe,recipe.tags)*modifier));
            string status="";for(int i=0;i<10000;i++){status=Production.Step(table,table,table,player);if(status.StartsWith("完成"))break;}
            Check(status.StartsWith("完成")&&storage.items.Skip(18).Sum(s=>s.count)==expected&&storage.items.Skip(18).Where(s=>!s.IsEmpty()).All(s=>s.itemValue.ItemClass.GetItemName()==product),"native production yields the exact recipe output for "+product);
            Check(SameItems(storage.items.Take(18),plan.Input.Take(18)),"native production consumes exactly the previewed materials for "+product);
        }
        Check(!RecipeMachines.Supports(kind,CraftingManager.GetRecipes("resourceGunPowder").First(r=>r.craftingArea=="chemistryStation")),"automatic workbench rejects chemistry-only gunpowder recipe");
        Check(!RecipeMachines.Supports(kind,CraftingManager.GetRecipes("resourceForgedSteel").First(r=>r.craftingArea=="forge")),"automatic workbench rejects forge-only recipe");
        var quality=CraftingManager.GetAllRecipes().First(r=>r.craftingArea=="workbench"&&r.GetOutputItemClass().HasQuality);
        Check(!RecipeMachines.Supports(kind,quality)&&!products.Contains(quality.GetOutputItemClass().GetItemName()),"quality equipment cannot be produced with a default item quality");
        Check(!RecipeMachines.Supports("yfAutoForge",CraftingManager.GetRecipes("ammo9mmBulletBall").First(r=>r.craftingArea=="workbench")),"forge does not inherit workbench ammo recipes");
        var c=MachineConfiguration.Get(table).Clone();c.Product="ammo9mmBulletBall";c.StorageMode="internal";
        Check(MachineConfiguration.Apply(world,table,player,c,MachineConfiguration.Token(table))=="已保存","select ammo for interruption tests");
        var ammo=CraftingManager.GetRecipes(c.Product).First(r=>RecipeMachines.Supports(kind,r)&&r.IsUnlocked(player));FillRecipe(storage,ammo,player);
        for(int i=18;i<36;i++)storage.items[i]=new ItemStack(ItemClass.GetItem("resourceWood"),ItemClass.GetItem("resourceWood").ItemClass.Stacknumber.Value);
        var snapshot=ProductionInventory.Clone(storage.items);var state=table.GetFeature<TEFeatureAutomationState>();float progress=state.Seconds;
        Check(Production.Step(table,table,table,player).Contains("满")&&SameItems(snapshot,storage.items)&&state.Seconds==progress,"full ammo output pauses without consuming ingredients or progress");
        FillRecipe(storage,ammo,player);storage.SlotLocks=new PackedBoolArray(36);storage.SlotLocks[0]=true;snapshot=ProductionInventory.Clone(storage.items);
        Check(Production.Step(table,table,table,player).Contains("缺材料")&&SameItems(snapshot,storage.items),"locked ingredients cannot be consumed by automatic workbench");storage.SlotLocks[0]=false;
        c=MachineConfiguration.Get(table).Clone();c.Paused=true;MachineConfiguration.Apply(world,table,player,c,MachineConfiguration.Token(table));
        Check(Production.Step(table,table,table,player).Contains("暂停")&&SameItems(snapshot,storage.items),"paused automatic workbench preserves materials");
        c=MachineConfiguration.Get(table).Clone();c.Paused=false;MachineConfiguration.Apply(world,table,player,c,MachineConfiguration.Token(table));
        var locked=CraftingManager.GetAllRecipes().First(r=>RecipeMachines.Supports(kind,r)&&!r.IsUnlocked(player)&&CraftingManager.GetRecipes(r.GetOutputItemClass().GetItemName()).Where(v=>RecipeMachines.Supports(kind,v)).All(v=>!v.IsUnlocked(player)));
        c=MachineConfiguration.Get(table).Clone();c.Product=locked.GetOutputItemClass().GetItemName();MachineConfiguration.Apply(world,table,player,c,MachineConfiguration.Token(table));FillRecipe(storage,locked,player);snapshot=ProductionInventory.Clone(storage.items);
        Check(RecipePreview.Describe(world,table,c,player).Contains("尚未解锁")&&!Production.Step(table,table,table,player).StartsWith("完成")&&SameItems(snapshot,storage.items),"locked recipe cannot be automated even with all ingredients");
        c=MachineConfiguration.Get(table).Clone();c.Product="ammo9mmBulletBall";MachineConfiguration.Apply(world,table,player,c,MachineConfiguration.Token(table));storage.items=ItemStack.CreateArray(36);
        var belt=Place("yfAutoBeltStraight",new Vector3i(6,160,7),owner);var bv=world.GetBlock(belt.ToWorldPos());
        for(byte r=0;r<24;r++){bv.rotation=r;if(bv.Block.SupportsRotation(r)&&ConveyorPath.Offset(bv,Vector3.forward)==new Vector3i(0,0,1))break;}
        world.SetBlockRPC(new BlockValueRef(belt.ToWorldPos()),bv);belt=(TileEntityComposite)world.GetTileEntity(new Vector3i(6,160,7));belt.SetOwner(owner);
        var at=new Vector3i(7,160,7);world.SetBlockRPC(new BlockValueRef(at),Block.GetBlockValue("yfAutoPowerPort"));var port=world.GetTileEntity(at) as TileEntityPowered;
        if(port==null){var chunk=(Chunk)world.GetChunkFromWorldPos(at);port=((BlockPowered)world.GetBlock(at).Block).CreateTileEntity(chunk);port.localChunkPos=Chunk.ToLocalPosition(at);chunk.AddTileEntity(port);}port.InitializePowerData();port.PowerItem.isPowered=true;
        Conveyors.Observe(belt,world);var step=AccessTools.Method(typeof(Conveyors),"Step");belt.GetFeature<TEFeatureStorage>().items[0]=new ItemStack(ItemClass.GetItem("resourceBulletCasing"),5);step.Invoke(null,new object[]{new[]{belt}});
        Check(storage.items.Take(18).Sum(s=>s.count)==5&&storage.items.Skip(18).All(s=>s.IsEmpty()),"conveyor inserts ammunition ingredients into workbench input only");
        storage.items[18]=new ItemStack(ItemClass.GetItem("ammo9mmBulletBall"),7);
        for(byte r=0;r<24;r++){bv.rotation=r;if(bv.Block.SupportsRotation(r)&&ConveyorPath.Offset(bv,Vector3.forward)==new Vector3i(0,0,-1))break;}
        world.SetBlockRPC(new BlockValueRef(belt.ToWorldPos()),bv);belt=(TileEntityComposite)world.GetTileEntity(new Vector3i(6,160,7));belt.SetOwner(owner);step.Invoke(null,new object[]{new[]{belt}});
        Check(storage.items[18].IsEmpty()&&storage.items.Take(18).Sum(s=>s.count)==5&&belt.GetFeature<TEFeatureStorage>().items[0].count==7,"conveyor extracts finished ammo without taking ingredients");
        state.Job="workbench:resume";state.Seconds=1;storage.items[18]=new ItemStack(ItemClass.GetItem("ammo9mmBulletBall"),2);
        using(var stream=new MemoryStream()){
            var writer=new PooledBinaryWriter();writer.SetBaseStream(stream);table.write(writer,TileEntity.StreamModeWrite.Persistency);writer.Flush();stream.Position=0;
            var reader=new PooledBinaryReader();reader.SetBaseStream(stream);var restored=new TileEntityComposite((Chunk)world.GetChunkFromWorldPos(table.ToWorldPos()),table.blockValue);restored.localChunkPos=table.localChunkPos;restored.read(reader,TileEntity.StreamModeRead.Persistency);
            Check(restored.GetFeature<TEFeatureAutomationState>().Job==state.Job&&restored.GetFeature<TEFeatureAutomationState>().Seconds==1&&SameItems(restored.GetFeature<TEFeatureStorage>().items,storage.items),"automatic workbench native save/load preserves inventory and progress");
        }
        Check(MachineSettingsStorage.Load(Path.Combine(GameIO.GetSaveGameDir(),"automation-machine-settings.xml")).Machines.Any(s=>s.Kind==kind&&s.Product=="ammo9mmBulletBall"),"automatic workbench recipe configuration persists");
        world.SetBlockRPC(new BlockValueRef(table.ToWorldPos()),BlockValue.Air);world.SetBlockRPC(new BlockValueRef(belt.ToWorldPos()),BlockValue.Air);world.SetBlockRPC(new BlockValueRef(at),BlockValue.Air);
    }
    static bool SameItems(IEnumerable<ItemStack> a,IEnumerable<ItemStack> b)=>a.Zip(b,(x,y)=>x.count==y.count&&x.itemValue.Equals(y.itemValue)).All(v=>v);
    static void FillRecipe(TEFeatureStorage store,Recipe recipe,EntityPlayer player)
    {
        store.items=ItemStack.CreateArray(36);int slot=0;recipe.craftingTier=recipe.GetCraftingTier(player);
        foreach(var item in recipe.GetIngredientsSummedUp()){
            int remaining=RecipePlan.Required(recipe,item,player);
            while(remaining>0){Check(slot<18,"test recipe ingredients fit input partition");int count=Math.Min(remaining,item.itemValue.ItemClass.Stacknumber.Value);store.items[slot++]=new ItemStack(item.itemValue.Clone(),count);remaining-=count;}
        }
        if(recipe.craftingToolType>0)store.items[slot]=new ItemStack(new ItemValue(recipe.craftingToolType),1);
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
            stream.SetLength(0);stream.Position=0;var reply=new NetPackageYFAutomationRecipeReply{At=forge.ToWorldPos(),Request=88,Text=ready,Materials=plan.Materials};reply.write(writer);writer.Flush();
            Check(stream.Length==reply.GetLength(),"recipe preview reply UTF8 wire length");stream.Position=2;var replyCopy=new NetPackageYFAutomationRecipeReply();replyCopy.read(reader);
            Check(replyCopy.Text==ready&&replyCopy.Request==88&&stream.Position==stream.Length,"recipe preview reply round trip");
            Check(replyCopy.Materials.Count==plan.Materials.Count&&replyCopy.Materials.Zip(plan.Materials,(a,b)=>a.Name==b.Name&&a.Need==b.Need&&a.Have==b.Have&&a.Tool==b.Tool).All(v=>v),"native material icon/count data round trip");
        }
        var ironValue=ItemClass.GetItem("resourceScrapIron");
        store.items=ItemStack.CreateArray(36);store.SlotLocks=new PackedBoolArray(36);store.SlotLocks[0]=true;
        var inputPartition=new MachinePartitionInventory(store,0);var outputPartition=new MachinePartitionInventory(store,18);
        Check(inputPartition.AddItem(new ItemStack(ironValue.Clone(),4))&&store.items[0].IsEmpty()&&store.items[1].count==4&&store.items.Skip(18).All(s=>s.IsEmpty()),"deposit respects locked input and never enters output");
        Check(outputPartition.AddItem(new ItemStack(ironValue.Clone(),7))&&store.items[18].count==7&&store.items[1].count==4,"explicit output deposit stays in its own partition");
        var more=new ItemStack(ironValue.Clone(),3);inputPartition.TryStackItem(0,more);
        Check(more.count==0&&store.items[1].count==7&&store.items[18].count==7,"partial stack transfer conserves counts and preserves output");
        var mask=XUiC_YFAutomationStorageWindow.Mask(store,0);
        Check(mask[0]&&!mask[1]&&Enumerable.Range(18,18).All(i=>mask[i]),"input sort/take mask excludes all output slots and locked input");
        var sorted=StackSortUtil.CombineAndSortStacks(ProductionInventory.Clone(store.items),0,mask);
        Check(sorted[18].count==7&&sorted.Take(18).Sum(s=>s.count)==7,"native masked sorting preserves output and input quantities");
        for(int i=0;i<18;i++)store.items[i]=new ItemStack(ironValue.Clone(),ironValue.ItemClass.Stacknumber.Value);
        Check(!inputPartition.AddItem(new ItemStack(ironValue.Clone(),1))&&store.items[19].IsEmpty(),"full input cannot spill deposits into empty output slots");
        world.SetBlockRPC(new BlockValueRef(forge.ToWorldPos()),BlockValue.Air);
    }

}
