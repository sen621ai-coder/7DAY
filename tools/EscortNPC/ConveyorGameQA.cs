using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using YFAutomation;
using HarmonyLib;
public sealed class ConveyorGameQA : IModApi
{
 const string Folder="E:/soft/7DTD-Modding/AutomationGameQA";
 static int phase,failures;static float deadline;static World world;static EntityPlayer player;static PowerGenerator generator;
 static TileEntityComposite source,sink;static List<TileEntityComposite> line=new List<TileEntityComposite>();static List<string> report=new List<string>();static PlatformUserIdentifierAbs owner;static int y=75;
 public void InitMod(Mod mod){if(!Environment.GetCommandLineArgs().Contains("-yfConveyorQA"))return;new Harmony("yf.automation.qa.bot").Patch(AccessTools.Method(typeof(EntityPlayer),"OnUpdateEntity"),prefix:new HarmonyMethod(typeof(ConveyorGameQA),nameof(BotUpdate)));ModEvents.GameStartDone.RegisterHandler(Ready);ModEvents.GameUpdate.RegisterHandler(Update);}
 // This headless sentinel has no client UI/connection. Only skip its personal
 // character simulation; native world, power and tile ticks remain untouched.
 public static bool BotUpdate(EntityPlayer __instance)=>!ReferenceEquals(__instance,player);
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Test(string name,Action a){try{a();report.Add("PASS "+name);}catch(Exception e){failures++;report.Add("FAIL "+name+": "+e);}File.WriteAllLines(Folder+"/conveyor-report.txt",report);}
 static ItemStack Stack(string n,int count)=>new ItemStack(ItemClass.GetItem(n),count);
 static TEFeatureStorage S(TileEntityComposite t)=>t.GetFeature<TEFeatureStorage>();
 static void Ready(ref ModEvents.SGameStartDoneData data){if(GamePrefs.GetString(EnumGamePrefs.GameName)!="AutomationQA_Isolated")return;world=GameManager.Instance.World;GameManager.Instance.AddChunkObserver(new Vector3(8,76,9),false,4,4);phase=1;deadline=Time.realtimeSinceStartup+180;}
 static TileEntityComposite Place(string name,int x,int height,int z,Vector3 forward){
  var p=new Vector3i(x,height,z);var v=Block.GetBlockValue(name);Check(v.type!=0,"unknown block "+name);
  if(ConveyorPath.IsBelt(name)){bool found=false;for(byte r=0;r<24;r++){v.rotation=r;if(v.Block.SupportsRotation(r)&&Vector3.Dot(v.Block.shape.GetRotation(v)*Vector3.forward,forward)>.99f){found=true;break;}}Check(found,"native rotation missing");}
  if(world.GetBlock(p).type!=v.type||world.GetBlock(p).rotation!=v.rotation)world.SetBlockRPC(new BlockValueRef(p),v);
  var t=world.GetTileEntity(p) as TileEntityComposite;if(t!=null){var store=t.GetFeature<TEFeatureStorage>();if(store!=null)for(int i=0;i<store.items.Length;i++)store.items[i]=ItemStack.Empty;t.SetOwner(owner);t.GetFeature<TEFeatureLockable>()?.SetLocked(false);t.SetModified();if(ConveyorPath.IsBelt(name))line.Add(t);}return t;
 }
 static int Total()=>S(source).items.Sum(s=>s.count)+S(sink).items.Sum(s=>s.count)+line.Sum(t=>S(t).items.Sum(s=>s.count));
 static void Setup(){
  owner=PlatformUserIdentifierAbs.FromCombinedString(File.ReadAllText(Folder+"/owner.txt").Trim(),false);
  line.Clear();source=Place("yfAutoInput",8,y,6,Vector3.forward);sink=Place("yfAutoOutput",13,y,9,Vector3.forward);
  Place("yfAutoBeltStraight",8,y,7,Vector3.forward);Place("yfAutoBeltStraight",8,y,8,Vector3.forward);Place("yfAutoBeltRight",8,y,9,Vector3.forward);
  Place("yfAutoBeltStraight",9,y,9,Vector3.right);Place("yfAutoBeltUp",10,y,9,Vector3.right);Place("yfAutoBeltDown",11,y+1,9,Vector3.right);Place("yfAutoBeltStraight",12,y,9,Vector3.right);
  // Side support for the elevated ramp, outside the transport path.
  var concrete=Block.GetBlockValue("concreteShapes:cube");for(int h=y;h<=y+1;h++)world.SetBlockRPC(new BlockValueRef(11,h,10),concrete);
  var p=new Vector3i(7,y,7);var v=Block.GetBlockValue("yfAutoPowerPort");world.SetBlockRPC(new BlockValueRef(p),v);var port=world.GetTileEntity(p) as TileEntityPowered;
  if(port==null){var chunk=(Chunk)world.GetChunkFromWorldPos(p);port=((BlockPowered)v.Block).CreateTileEntity(chunk);port.localChunkPos=Chunk.ToLocalPosition(p);chunk.AddTileEntity(port);}port.InitializePowerData();
  var gt=(TileEntityPowered)world.GetTileEntity(new Vector3i(11,y,3));gt.InitializePowerData();generator=(PowerGenerator)gt.PowerItem;generator.CurrentFuel=generator.MaxFuel;generator.IsOn=true;PowerManager.Instance.SetParent(port.PowerItem,generator);port.SetModified();gt.CreateWireDataFromPowerItem();gt.SetModified();
  S(source).items[0]=Stack("resourceScrapIron",100);S(sink).items[0]=Stack("resourceScrapIron",1);source.SetModified();sink.SetModified();
  foreach(var b in line)Check(S(b).items.Length==1,"belt buffer must have one slot");
  player=(EntityPlayer)EntityFactory.CreateEntity(EntityClass.FromString("playerMale"),new Vector3(8,y+1,3));player.MinEventContext.ItemValue=ItemValue.None;world.SpawnEntityInWorld(player);
 }
 static void Update(ref ModEvents.SGameUpdateData data){if(phase==0)return;
  if(phase==1){if(!world.IsChunkAreaLoaded(new Vector3(8,76,9))&&Time.realtimeSinceStartup<deadline)return;
   Test("eleven machine models: game bundle, collision, native status and saved features",()=>{
    var bundle=AssetBundle.GetAllLoadedAssetBundles().First(b=>b.GetAllAssetNames().Any(n=>n.EndsWith("machineminer.prefab")));
    var own=PlatformUserIdentifierAbs.FromCombinedString(File.ReadAllText(Folder+"/owner.txt").Trim(),false);
    int index=0;
    foreach(string kind in new[]{"Sorter","Kitchen","Smelter","Forge","Recycler","Farm","Miner","Transfer","WaterPump","WaterTank","AmmoFeed"}){
     var prefab=bundle.LoadAsset<GameObject>("Assets/Machines/Machine"+kind+".prefab");Check(prefab!=null,"missing machine "+kind);var go=UnityEngine.Object.Instantiate(prefab);
     try{
      var collider=go.GetComponent<BoxCollider>();Check(collider!=null&&go.GetComponentsInChildren<Collider>().Length==1,"machine selection collider "+kind);
      go.transform.position=new Vector3(200+index++*2,100,200);Physics.SyncTransforms();RaycastHit hit;Check(collider.Raycast(new Ray(go.transform.position+new Vector3(0,.5f,-2),Vector3.forward),out hit,3),"front interaction ray missed "+kind);
      foreach(var r in go.GetComponentsInChildren<Renderer>())Check(r.sharedMaterial!=null&&r.sharedMaterial.shader!=null,"missing material "+kind);
      var value=Block.GetBlockValue("yfAuto"+kind);var chunk=new Chunk(200,200);chunk.SetBlockRaw(1,100,1,value);var tile=new TileEntityComposite(chunk,value);tile.localChunkPos=new Vector3i(1,100,1);tile.SetOwner(own);tile.bDisableModifiedCheck=true;
      var sign=tile.GetFeature<TEFeatureSignable>();Check(sign!=null,"old sign feature removed "+kind);sign.SetText("测试运行状态",false,own);Check(MachineDisplay.WithStatus(tile,"使用").Contains("测试运行状态"),"status not visible "+kind);
      foreach(var mode in new[]{TileEntity.StreamModeWrite.Persistency,TileEntity.StreamModeWrite.ToClient})using(var stream=new MemoryStream()){
       var writer=new PooledBinaryWriter();writer.SetBaseStream(stream);tile.write(writer,mode);writer.Flush();stream.Position=0;var clone=new TileEntityComposite(chunk,value);clone.localChunkPos=tile.localChunkPos;var reader=new PooledBinaryReader();reader.SetBaseStream(stream);clone.read(reader,mode==TileEntity.StreamModeWrite.Persistency?TileEntity.StreamModeRead.Persistency:TileEntity.StreamModeRead.FromServer);Check(MachineDisplay.WithStatus(clone,"使用").Contains("测试运行状态"),"status roundtrip "+kind);
       Check((clone.GetFeature<TEFeatureStorage>()!=null)==(tile.GetFeature<TEFeatureStorage>()!=null),"storage feature changed "+kind);
      }
     }finally{UnityEngine.Object.Destroy(go);}
    }
   });
   Test("all 32 workbench recipes use the native crafting queue",()=>{
    var value=Block.GetBlockValue("yfAutomationWorkbench");var recipes=CraftingManager.GetRecipes().Where(r=>r.craftingArea=="yfAutomationWorkbench").ToArray();Check(recipes.Length==32,"workbench recipe count");
    foreach(var recipe in recipes){var chunk=new Chunk(200,200);chunk.SetBlockRaw(1,100,1,value);var station=new TileEntityWorkstation(chunk);station.localChunkPos=new Vector3i(1,100,1);station.bDisableModifiedCheck=true;for(int i=0;i<station.Queue.Length;i++)station.Queue[i]=new RecipeQueueItem();station.Queue[station.Queue.Length-1]=new RecipeQueueItem{Recipe=recipe,Multiplier=1,CraftingTimeLeft=0,OneItemCraftTime=1,IsCrafting=true,StartingEntityId=-1};station.HandleRecipeQueue(2);Check(station.Output.Any(s=>!s.IsEmpty()&&s.itemValue.type==recipe.itemValueType&&s.count==recipe.count),"recipe failed: "+recipe.GetName());}
   });
   if(Environment.GetCommandLineArgs().Contains("-yfConveyorQAReadback")){Test("bind persisted conveyor scene",()=>{
    source=(TileEntityComposite)world.GetTileEntity(new Vector3i(8,y,6));sink=(TileEntityComposite)world.GetTileEntity(new Vector3i(13,y,9));
    foreach(var p in new[]{new Vector3i(8,y,7),new Vector3i(8,y,8),new Vector3i(8,y,9),new Vector3i(9,y,9),new Vector3i(10,y,9),new Vector3i(11,y+1,9),new Vector3i(12,y,9)}){var t=(TileEntityComposite)world.GetTileEntity(p);Check(t!=null&&ConveyorPath.IsBelt(t.block.GetBlockName()),"persisted belt missing");line.Add(t);}
    Check(Total()==1018,"persisted cargo total wrong: "+Total());player=(EntityPlayer)EntityFactory.CreateEntity(EntityClass.FromString("playerMale"),new Vector3(8,y+1,3));player.MinEventContext.ItemValue=ItemValue.None;world.SpawnEntityInWorld(player);
   });phase=8;deadline=Time.realtimeSinceStartup+18;return;}
   Test("construct powered seven-segment corner/ramp demonstration",Setup);if(failures>0){Finish();return;}phase=7;deadline=Time.realtimeSinceStartup+3;return;}
  if(Time.realtimeSinceStartup<deadline)return;
  if(phase==7){Test("parcels travel segment by segment, not instant teleport",()=>Check(S(sink).items.Skip(1).Sum(s=>s.count)==0&&line.Any(t=>S(t).items[0].count>0),"no visible in-flight interval"));phase=2;deadline=Time.realtimeSinceStartup+25;return;}
  if(phase==8){Test("restart keeps inventories, wiring and resumes production",()=>{Check(Total()==1018,"restart lost/duplicated items");Check(S(sink).items.Skip(1).Sum(s=>s.count)>17,"line did not resume after restart");});Finish();return;}
  if(phase==2){
   Test("native belt route: right turn, rotated headings, up/down, conservation",()=>{Check(S(sink).items.Skip(1).Sum(s=>s.count)==100,"delivery incomplete: source="+S(source).items.Sum(s=>s.count)+" same="+ReferenceEquals(source,world.GetTileEntity(source.ToWorldPos()))+" block="+world.GetBlock(source.ToWorldPos()).Block.GetBlockName()+" sink="+S(sink).items.Sum(s=>s.count)+" states="+string.Join(";",line.Select(t=>t.GetFeature<TEFeatureAutomationState>().Job+":"+S(t).items.Sum(s=>s.count))));Check(Total()==101,"lost or duplicated items");Check(S(sink).items[0].count==1,"sample consumed");});
   if(failures>0){Finish();return;}
   Test("native belt inventory and animation state serialization",()=>{foreach(var t in line){S(t).items[0]=Stack("resourceWood",3);var state=t.GetFeature<TEFeatureAutomationState>();state.Job="运输中";state.Seconds=7;
    foreach(var mode in new[]{TileEntity.StreamModeWrite.Persistency,TileEntity.StreamModeWrite.ToClient})using(var stream=new MemoryStream()){var writer=new PooledBinaryWriter();writer.SetBaseStream(stream);t.write(writer,mode);writer.Flush();stream.Position=0;var clone=new TileEntityComposite((Chunk)world.GetChunkFromWorldPos(t.ToWorldPos()),world.GetBlock(t.ToWorldPos()));clone.localChunkPos=Chunk.ToLocalPosition(t.ToWorldPos());var reader=new PooledBinaryReader();reader.SetBaseStream(stream);clone.read(reader,mode==TileEntity.StreamModeWrite.Persistency?TileEntity.StreamModeRead.Persistency:TileEntity.StreamModeRead.FromServer);Check(clone.GetFeature<TEFeatureStorage>().items[0].count==3&&clone.GetFeature<TEFeatureAutomationState>().Seconds==7,"payload/progress reload failed: "+mode);}S(t).items[0]=ItemStack.Empty;t.SetModified();}});
   Test("five loaded game prefabs and live cargo display binding",()=>{
    var bundle=AssetBundle.GetAllLoadedAssetBundles().First(b=>b.GetAllAssetNames().Any(n=>n.EndsWith("conveyorstraight.prefab")));
    foreach(string kind in new[]{"Straight","Left","Right","Up","Down"}){
     var prefab=bundle.LoadAsset<GameObject>("Assets/Conveyor/Conveyor"+kind+".prefab");Check(prefab!=null,"missing game model");var go=UnityEngine.Object.Instantiate(prefab);
     try{var cargo=go.transform.Find("Cargo");Check(cargo!=null&&go.GetComponentsInChildren<Collider>().Length>0,"missing cargo/collider");var t=line[0];S(t).items[0]=Stack("resourceWood",1);var visual=go.AddComponent<ConveyorVisual>();visual.Bind(t,go.transform);go.SendMessage("Update");Check(cargo.gameObject.activeSelf,"cargo display not activated");S(t).items[0]=ItemStack.Empty;go.SendMessage("Update");Check(!cargo.gameObject.activeSelf,"empty belt leaves phantom cargo");}finally{UnityEngine.Object.Destroy(go);}
    }
   });
   S(source).items[0]=Stack("resourceScrapIron",17);for(int i=1;i<S(sink).items.Length;i++)S(sink).items[i]=Stack("resourceWood",ItemClass.GetItem("resourceWood").ItemClass.Stacknumber.Value);source.SetModified();sink.SetModified();phase=3;deadline=Time.realtimeSinceStartup+15;return;
  }
  if(phase==3){
   Test("full output jams without deleting parcels",()=>{Check(line.Sum(t=>S(t).items.Sum(s=>s.count))+S(source).items.Sum(s=>s.count)==17,"blocked line lost cargo");Check(line.Sum(t=>S(t).items.Sum(s=>s.count))>0,"no in-flight cargo");});
   generator.IsOn=false;for(int i=1;i<S(sink).items.Length;i++)S(sink).items[i]=ItemStack.Empty;sink.SetModified();phase=4;deadline=Time.realtimeSinceStartup+8;return;
  }
  if(phase==4){Test("power cut pauses line with all inventory retained",()=>{Check(S(sink).items.Skip(1).Sum(s=>s.count)==0,"unpowered output moved");Check(line.Sum(t=>S(t).items.Sum(s=>s.count))+S(source).items.Sum(s=>s.count)==17,"unpowered cargo lost");});generator.IsOn=true;phase=5;deadline=Time.realtimeSinceStartup+18;return;}
  if(phase==5){Test("restored power resumes jammed parcels exactly once",()=>Check(S(sink).items.Skip(1).Sum(s=>s.count)==17&&Total()==18,"resume lost/duplicated cargo"));S(source).items[0]=Stack("resourceScrapIron",1000);source.SetModified();Finish();}
 }
 static void Finish(){phase=0;if(player!=null){world.RemoveEntity(player.entityId,EnumRemoveEntityReason.Despawned);player=null;}GameManager.Instance.SaveWorld();report.Add("FINISHED failures="+failures);File.WriteAllLines(Folder+"/conveyor-report.txt",report);Application.Quit();}
}
