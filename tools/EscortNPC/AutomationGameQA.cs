using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using YFAutomation;
public class AutomationGameQA : IModApi
{
 static readonly List<string> report=new List<string>(); static string folder; static int failures;
 public void InitMod(Mod mod){
  if(!Environment.GetCommandLineArgs().Contains("-yfAutomationQA"))return;
  folder="E:/soft/7DTD-Modding/AutomationGameQA";
  ModEvents.GameStartDone.RegisterHandler(Ready);
  ModEvents.GameUpdate.RegisterHandler(Update);
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Case(string name,Action test){try{test();report.Add("PASS "+name);}catch(Exception e){failures++;report.Add("FAIL "+name+": "+e);}File.WriteAllLines(folder+"/report.txt",report);}
 static TileEntityComposite Make(Chunk chunk,string name,int x){var v=Block.GetBlockValue(name);Check(v.type!=0,"block not loaded: "+name);chunk.SetBlockRaw(x,100,1,v);var te=new TileEntityComposite(chunk,v);te.localChunkPos=new Vector3i(x,100,1);te.bDisableModifiedCheck=true;return te;}
 static ItemStack Stack(string name,int count){var v=ItemClass.GetItem(name);Check(v.type!=0,"item not loaded: "+name);return new ItemStack(v,count);}
 static void Ready(ref ModEvents.SGameStartDoneData data){
  if(GamePrefs.GetString(EnumGamePrefs.GameName)!="AutomationQA_Isolated")return;
  report.Add("Runtime: "+Application.unityVersion+" / isolated world / full installed mod pack");
  if(Environment.GetCommandLineArgs().Contains("-yfAutomationQAReadback")){GameManager.Instance.AddChunkObserver(new Vector3(8,100,28),false,4,4);phase=3;deadline=Time.realtimeSinceStartup+180;return;}
  Case("all automation unlock labels",()=>{
   foreach(var b in Block.list.Where(b=>b!=null&&b.GetBlockName().StartsWith("yfAuto")))foreach(var u in b.UnlockedBy)Check(u.GetName()!=null,b.GetBlockName());
   foreach(var n in new[]{"yfAutoFrame","yfAutoMotor","yfAutoController","yfAutoPump","yfAutoCutter","yfAutoTransport","yfAutoIngot_iron","yfAutoIrrigationWater"})foreach(var u in ItemClass.GetItem(n).ItemClass.UnlockedBy)Check(u.GetName()!=null,n);
  });
  Case("workbench native queue for every automation recipe",()=>{
   var value=Block.GetBlockValue("yfAutomationWorkbench");var block=(BlockWorkstation)value.Block;
   Check(block.WorkstationData.WorkstationWindow=="workstation_yfAutomationWorkbench","wrong window group");
   var recipes=CraftingManager.GetRecipes().Where(r=>r.craftingArea=="yfAutomationWorkbench").ToArray();Check(recipes.Length==32,"expected 32 station recipes, got "+recipes.Length);
   foreach(var recipe in recipes){
    var chunk=new Chunk(200,200);chunk.SetBlockRaw(1,100,1,value);var station=new TileEntityWorkstation(chunk);station.bDisableModifiedCheck=true;station.localChunkPos=new Vector3i(1,100,1);
    for(int i=0;i<station.Queue.Length;i++)station.Queue[i]=new RecipeQueueItem();
    station.Queue[station.Queue.Length-1]=new RecipeQueueItem{Recipe=recipe,Multiplier=1,CraftingTimeLeft=0,OneItemCraftTime=1,IsCrafting=true,StartingEntityId=-1};
    station.HandleRecipeQueue(2);Check(station.Output.Any(s=>!s.IsEmpty()&&s.itemValue.type==recipe.itemValueType&&s.count==recipe.count),"native workstation failed recipe: "+recipe.GetName());
   }
  });
  foreach(string name in new[]{"yfAutoInput","yfAutoOutput","yfAutoSorter","yfAutoTransfer","yfAutoKitchen","yfAutoForge","yfAutoSmelter","yfAutoRecycler","yfAutoFarm","yfAutoMiner","yfAutoWaterPump","yfAutoWaterTank","yfAutoAmmoFeed"}){string n=name;Case(n+" native tile init and save/load",()=>{
   var chunk=new Chunk(200,200);var te=Make(chunk,n,1);var state=te.GetFeature<TEFeatureAutomationState>();if(state!=null){state.Job="QA:job";state.Seconds=12.5f;}
   var storage=te.GetFeature<TEFeatureStorage>();if(storage!=null){Check(storage.items.Length>=2,"storage size");storage.items[1]=Stack("resourceScrapIron",17);}
   using(var stream=new MemoryStream()){
    var writer=new PooledBinaryWriter();writer.SetBaseStream(stream);te.write(writer,TileEntity.StreamModeWrite.Persistency);writer.Flush();stream.Position=0;
    var clone=Make(chunk,n,2);var reader=new PooledBinaryReader();reader.SetBaseStream(stream);clone.read(reader,TileEntity.StreamModeRead.Persistency);
    Check(stream.Position==stream.Length,"serialization has unread bytes");
    if(state!=null)Check(clone.GetFeature<TEFeatureAutomationState>().Seconds==12.5f,"progress lost");
    if(storage!=null)Check(clone.GetFeature<TEFeatureStorage>().items[1].count==17,"inventory lost");
   }
  });}
  Case("native smelting production complete and full output pause",()=>{
   var chunk=new Chunk(200,200);var machine=Make(chunk,"yfAutoSmelter",1);var source=Make(chunk,"yfAutoInput",2);var target=Make(chunk,"yfAutoOutput",3);
   var input=source.GetFeature<TEFeatureStorage>();var output=target.GetFeature<TEFeatureStorage>();
   input.items[0]=Stack("resourceScrapIron",2);output.items[0]=Stack("yfAutoIngot_iron",1);
   string status="";for(int i=0;i<120;i++){status=Production.Step(machine,source,target,null);if(status.StartsWith("完成"))break;}
   Check(status.StartsWith("完成"),"no production: "+status);Check(input.items[0].count==1,"wrong input count");
   Check(output.items.Skip(1).Sum(s=>s.count)==ItemClass.GetItem("resourceScrapIron").ItemClass.GetWeight(),"wrong native smelt weight");
   for(int i=1;i<output.items.Length;i++)output.items[i]=Stack("resourceWood",6000);
   float before=machine.GetFeature<TEFeatureAutomationState>().Seconds;status=Production.Step(machine,source,target,null);
   Check(status.Contains("满")&&input.items[0].count==1&&machine.GetFeature<TEFeatureAutomationState>().Seconds==before,"full output consumed resources/progress");
  });
  EntityPlayer player=null;
  Case("create native player for recipe effect evaluation",()=>{player=EntityFactory.CreateEntity(EntityClass.FromString("playerMale"),new Vector3(0,100,0)) as EntityPlayer;Check(player!=null,"native player creation failed");player.MinEventContext.ItemValue=ItemValue.None;});
  foreach(string kind in new[]{"yfAutoKitchen","yfAutoForge"}){string k=kind;Case(k+" actual recipe completion",()=>{
   Check(player!=null,"no test player");bool progression=XUiM_Recipes.CraftingProgression;
   try{
    XUiM_Recipes.CraftingProgression=false;
    var r=CraftingManager.GetRecipes(k=="yfAutoKitchen"?"foodBoiledMeat":"resourceForgedIron").First(x=>x.craftingArea==(k=="yfAutoKitchen"?"campfire":"forge"));
    var chunk=new Chunk(200,200);var machine=Make(chunk,k,1);var source=Make(chunk,"yfAutoInput",2);var target=Make(chunk,"yfAutoOutput",3);
    var input=source.GetFeature<TEFeatureStorage>();var output=target.GetFeature<TEFeatureStorage>();int slot=0;
    foreach(var ingredient in r.GetIngredientsSummedUp()){
     string n=ingredient.itemValue.ItemClass.GetItemName();if(n.StartsWith("unit_"))n="yfAutoIngot_"+n.Substring(5);
     input.items[slot++]=Stack(n,10000);
    }
    if(r.craftingToolType>0)input.items[slot++]=new ItemStack(new ItemValue(r.craftingToolType),1);
    output.items[0]=new ItemStack(new ItemValue(r.itemValueType),1);string status="";
    for(int tick=0;tick<5000;tick++){status=Production.Step(machine,source,target,player);if(status.StartsWith("完成"))break;}
    Check(status.StartsWith("完成"),"recipe did not finish: "+status);Check(output.items.Skip(1).Any(s=>!s.IsEmpty()&&s.itemValue.type==r.itemValueType),"missing output");
    Check(output.items[0].count==1,"consumed output sample");
   }finally{XUiM_Recipes.CraftingProgression=progression;}
  });}
  Case("native equipment recycler protects loaded/quality6 gear",()=>{
   Check(player!=null,"no test player");var chunk=new Chunk(200,200);var machine=Make(chunk,"yfAutoRecycler",1);var source=Make(chunk,"yfAutoInput",2);var target=Make(chunk,"yfAutoOutput",3);
   var input=source.GetFeature<TEFeatureStorage>();var output=target.GetFeature<TEFeatureStorage>();
   var gear=ItemClass.GetItem("meleeToolRepairT1ClawHammer");Check(gear.type!=0,"gear missing");gear.Quality=1;
   var scrap=CraftingManager.GetScrapableRecipe(gear,1);Check(scrap!=null,"no scrap recipe");output.items[0]=new ItemStack(new ItemValue(scrap.itemValueType),1);
   input.items[0]=new ItemStack(gear,1);input.items[0].itemValue.Quality=6;
   Check(!Production.Step(machine,source,target,player).StartsWith("生产中")&&input.items[0].count==1,"quality6 consumed");
   input.items[0].itemValue.Quality=1;input.items[0].itemValue.Meta=5;
   Check(!Production.Step(machine,source,target,player).StartsWith("生产中")&&input.items[0].count==1,"loaded equipment consumed");
   input.items[0].itemValue.Meta=0;string status="";for(int tick=0;tick<5000;tick++){status=Production.Step(machine,source,target,player);if(status.StartsWith("完成"))break;}
   Check(status.StartsWith("完成")&&input.items[0].IsEmpty()&&output.items.Skip(1).Sum(s=>s.count)>0,"recycling failed: "+status);
  });
  GameManager.Instance.AddChunkObserver(new Vector3(8,100,28),false,4,4);
  phase=1; deadline=Time.realtimeSinceStartup+180;
 }
 static int phase; static float deadline; static int demoY; static EntityPlayer qaPlayer;
 static void Update(ref ModEvents.SGameUpdateData data){
  if(phase==0)return;
  var w=GameManager.Instance.World;
  if(phase==3){
   if(!w.IsChunkAreaLoaded(new Vector3(8,100,28))&&Time.realtimeSinceStartup<deadline)return;
   Case("inspect saved generator",()=>{
    int y=int.Parse(File.ReadAllLines(folder+"/DEMO.txt")[1].Split(' ')[3])-1;
    var te=(TileEntityPowered)w.GetTileEntity(new Vector3i(11,y,3));te.InitializePowerData();var generator=(PowerGenerator)te.PowerItem;
    report.Add("Generator: on="+generator.IsOn+" fuel="+generator.CurrentFuel+"/"+generator.MaxFuel+" maxOutput="+generator.MaxOutput+" currentPower="+generator.CurrentPower+" fuelRate="+generator.OutputPerFuel);
    if(Environment.GetCommandLineArgs().Contains("-yfAutomationQARepairPower")){generator.CurrentFuel=generator.MaxFuel;generator.IsOn=true;te.SetModified();}
    // Native PowerManager intentionally does not tick a world with zero players.
    // Spawn a temporary native test player, then remove it before saving.
    qaPlayer=(EntityPlayer)EntityFactory.CreateEntity(EntityClass.FromString("playerMale"),new Vector3(8,y+1,3));qaPlayer.MinEventContext.ItemValue=ItemValue.None;w.SpawnEntityInWorld(qaPlayer);
   });
   phase=4;deadline=Time.realtimeSinceStartup+15;return;
  }
  if(phase==4){
   if(Time.realtimeSinceStartup<deadline)return;
   Case("reload saved demo blocks, inventories, ownership and power wiring",()=>{
    var lines=File.ReadAllLines(folder+"/DEMO.txt");
    int y=int.Parse(lines[1].Split(' ')[3])-1;
    Check(w.GetBlock(new Vector3i(2,y,2)).Block.GetBlockName()=="yfAutomationWorkbench","saved workstation missing");
    foreach(var line in lines.Skip(3)){
     var p=line.Split(' ');string kind=p[0].TrimEnd(':');var pos=new Vector3i(int.Parse(p[1]),int.Parse(p[2]),int.Parse(p[3]));
     var te=w.GetTileEntity(pos) as TileEntityComposite;Check(te!=null&&te.block.GetBlockName()==kind,"saved machine missing: "+kind);Check(te.Owner!=null,"owner lost");
     var target=w.GetTileEntity(new Vector3i(pos.x,pos.y,pos.z+1)) as TileEntityComposite;Check(target!=null&&!target.GetFeature<TEFeatureStorage>().items[0].IsEmpty(),"sample lost: "+kind);
     var port=w.GetTileEntity(new Vector3i(pos.x+1,pos.y,pos.z)) as TileEntityPowered;Check(port?.PowerItem?.Parent!=null,"power wiring lost: "+kind);
     Check(port.IsPowered,"saved demo port not powered: "+kind);
     if(kind=="yfAutoSorter"||kind=="yfAutoSmelter")Check(target.GetFeature<TEFeatureStorage>().items.Skip(1).Any(s=>!s.IsEmpty()),"no actual powered output: "+kind);
    }
   });if(qaPlayer!=null){w.RemoveEntity(qaPlayer.entityId,EnumRemoveEntityReason.Despawned);qaPlayer=null;}GameManager.Instance.SaveWorld();Finish();return;
  }
  if(phase==1){
   if(!w.IsChunkAreaLoaded(new Vector3(8,100,28))){if(Time.realtimeSinceStartup<deadline)return;Case("load demonstration chunks",()=>{throw new Exception("chunk loading timeout");});Finish();return;}
   phase=2;Case("build retained visible demonstration",BuildDemo);deadline=Time.realtimeSinceStartup+15;
  }else if(Time.realtimeSinceStartup>=deadline){Case("save demonstration world",()=>GameManager.Instance.SaveWorld());Finish();}
 }
 static void Finish(){phase=0;report.Add("FINISHED failures="+failures);File.WriteAllLines(folder+"/report.txt",report);Log.Out("[AutomationGameQA] FINISHED failures="+failures);Application.Quit();}
 static void BuildDemo(){
  var w=GameManager.Instance.World;
  var owner=PlatformUserIdentifierAbs.FromCombinedString(File.ReadAllText(folder+"/owner.txt").Trim(),false);Check(owner!=null,"missing demo owner");
  int top=0;for(int x=1;x<=14;x++)for(int z=1;z<=60;z++)top=Math.Max(top,w.GetHeight(x,z));demoY=top+1;
  Check(demoY<200,"unsafe platform height");
  var baseBlock=Block.GetBlockValue("concreteShapes:cube");if(baseBlock.type==0)baseBlock=Block.GetBlockValue("concreteShapes");Check(baseBlock.type!=0,"foundation block missing");
  for(int x=1;x<=14;x++)for(int z=1;z<=60;z++){
   int ground=w.GetTerrainHeight(x,z);
   for(int y=ground;y<demoY;y++)w.SetBlockRPC(new BlockValueRef(x,y,z),baseBlock);
   for(int y=demoY;y<demoY+4;y++)w.SetBlockRPC(new BlockValueRef(x,y,z),BlockValue.Air);
  }
  Action<string,int,int> place=(n,x,z)=>{var p=new Vector3i(x,demoY,z);var v=Block.GetBlockValue(n);Check(v.type!=0,"unknown demo block "+n);w.SetBlockRPC(new BlockValueRef(p),v);var c=w.GetTileEntity(p) as TileEntityComposite;if(c!=null){c.SetOwner(owner);c.GetFeature<TEFeatureLockable>()?.SetLocked(false);c.SetModified();}};
  place("yfAutomationWorkbench",2,2);place("generatorbank",11,3);
  var generator=w.GetTileEntity(new Vector3i(11,demoY,3)) as TileEntityPowered;Check(generator?.PowerItem is PowerGenerator,"generator tile missing");
  generator.InitializePowerData();var power=(PowerGenerator)generator.PowerItem;power.SetSlots(Enumerable.Range(0,6).Select(i=>Stack("smallEngine",1)).ToArray());power.CurrentFuel=power.MaxFuel;power.IsOn=true;generator.SetModified();
  string[] kinds={"yfAutoSorter","yfAutoSmelter","yfAutoKitchen","yfAutoForge","yfAutoRecycler","yfAutoTransfer","yfAutoFarm","yfAutoMiner","yfAutoWaterPump","yfAutoWaterTank","yfAutoAmmoFeed"};
  var notes=new List<string>{"AutomationQA_Isolated demo", "Console: teleport 8 "+(demoY+1)+" 3", "All machines are unlocked. Kitchen/forge need owner online and recipe unlocks. Farm/miner/pump/turret rows are setup exhibits; add crops/ore/water/turret as appropriate."};
  int row=7;
  foreach(var kind in kinds){if(row%16>12)row+=16-row%16;
   place(kind,4,row);place("yfAutoInput",3,row);place("yfAutoOutput",4,row+1);place("yfAutoPowerPort",5,row);
   var port=w.GetTileEntity(new Vector3i(5,demoY,row)) as TileEntityPowered;if(port==null){var pos=new Vector3i(5,demoY,row);var chunk=(Chunk)w.GetChunkFromWorldPos(pos);port=((BlockPowered)w.GetBlock(pos).Block).CreateTileEntity(chunk);port.localChunkPos=Chunk.ToLocalPosition(pos);chunk.AddTileEntity(port);}port.InitializePowerData();Check(port.PowerItem!=null,"power port item missing");PowerManager.Instance.SetParent(port.PowerItem,power);port.CreateWireDataFromPowerItem();port.SetModified();
   var input=((TileEntityComposite)w.GetTileEntity(new Vector3i(3,demoY,row))).GetFeature<TEFeatureStorage>();
   var output=((TileEntityComposite)w.GetTileEntity(new Vector3i(4,demoY,row+1))).GetFeature<TEFeatureStorage>();
   string material="resourceScrapIron",sample="resourceScrapIron";
   if(kind=="yfAutoSmelter")sample="yfAutoIngot_iron";
   if(kind=="yfAutoKitchen"){material="foodRawMeat";sample="foodBoiledMeat";input.items[1]=Stack("drinkJarBoiledWater",100);input.items[2]=Stack("toolCookingPot",1);}
   if(kind=="yfAutoForge"){material="yfAutoIngot_iron";sample="resourceForgedIron";input.items[1]=Stack("yfAutoIngot_clay",1000);}
   if(kind=="yfAutoRecycler"){material="meleeToolRepairT1ClawHammer";sample="resourceScrapIron";}
   input.items[0]=Stack(material,kind=="yfAutoRecycler"?1:1000);output.items[0]=Stack(sample,1);input.SetModified();output.SetModified();
   notes.Add(kind+": 4 "+demoY+" "+row);row+=4;
  }
  generator.CreateWireDataFromPowerItem();generator.SetModified();File.WriteAllLines(folder+"/DEMO.txt",notes);
 }
}
