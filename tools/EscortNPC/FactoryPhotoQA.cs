using System;
using System.IO;
using System.Linq;
using UnityEngine;
using YFAutomation;

// Explicitly opt-in, isolated-save-only scene builder. Never included in the shipped Mod.
public sealed class FactoryPhotoQA : IModApi
{
 const string Folder="E:/soft/7DTD-Modding/AutomationGameQA";
 static World world;static int phase;static float deadline;static PlatformUserIdentifierAbs owner;
 static TileEntityComposite smelter,forge,result;static EntityPlayerLocal player;
 public void InitMod(Mod mod){if(!Environment.GetCommandLineArgs().Contains("-yfFactoryPhoto"))return;ModEvents.GameStartDone.RegisterHandler(Ready);ModEvents.GameUpdate.RegisterHandler(Update);}
 static void Ready(ref ModEvents.SGameStartDoneData data){if(GamePrefs.GetString(EnumGamePrefs.GameName)!="AutomationQA_Isolated")return;world=GameManager.Instance.World;GameManager.Instance.AddChunkObserver(new Vector3(8,76,8),false,4,4);phase=1;deadline=Time.realtimeSinceStartup+180;}
 static ItemStack Stack(string name,int count)=>new ItemStack(ItemClass.GetItem(name),count);
 static TileEntityComposite Place(string name,int x,int z,Vector3 forward){
  var v=Block.GetBlockValue(name);if(v.type==0)throw new Exception("Unknown "+name);
  if(ConveyorPath.IsBelt(name)){bool ok=false;for(byte r=0;r<24;r++){v.rotation=r;if(v.Block.SupportsRotation(r)&&Vector3.Dot(v.Block.shape.GetRotation(v)*Vector3.forward,forward)>.99f){ok=true;break;}}if(!ok)throw new Exception("Rotation "+name);}
  var pos=new Vector3i(x,75,z);world.SetBlockRPC(new BlockValueRef(pos),v);var t=world.GetTileEntity(pos) as TileEntityComposite;if(t!=null){t.SetOwner(owner);t.GetFeature<TEFeatureLockable>()?.SetLocked(false);t.SetModified();}return t;
 }
 static void Fill(TileEntityComposite tile,params ItemStack[] items){var s=tile.GetFeature<TEFeatureStorage>();for(int i=0;i<s.items.Length;i++)s.items[i]=i<items.Length?items[i]:ItemStack.Empty;tile.SetModified();}
 static void Port(int x,int z,PowerGenerator generator){Place("yfAutoPowerPort",x,z,Vector3.forward);var p=(TileEntityPowered)world.GetTileEntity(new Vector3i(x,75,z));p.InitializePowerData();PowerManager.Instance.SetParent(p.PowerItem,generator);p.CreateWireDataFromPowerItem();p.SetModified();}
 static void Build(){
  owner=PlatformUserIdentifierAbs.FromCombinedString(File.ReadAllText(Folder+"/owner.txt").Trim(),false);
  var concrete=Block.GetBlockValue("concreteShapes:cube");for(int x=1;x<=14;x++)for(int z=1;z<=14;z++){world.SetBlockRPC(new BlockValueRef(x,74,z),concrete);for(int y=75;y<82;y++)world.SetBlockRPC(new BlockValueRef(x,y,z),BlockValue.Air);}
  // Split clearing/placement over frames so native removed tile cleanup cannot erase new tiles.
 }
 static void Machines(){
  var input=Place("yfAutoInput",3,3,Vector3.forward);Fill(input,Stack("resourceScrapIron",1000));
  smelter=Place("yfAutoSmelter",3,4,Vector3.forward);var outbox=Place("yfAutoOutput",3,5,Vector3.forward);Fill(outbox,Stack("yfAutoIngot_iron",1));
  Place("yfAutoBeltStraight",3,6,Vector3.forward);Place("yfAutoBeltRight",3,7,Vector3.forward);
  for(int x=4;x<=7;x++)Place("yfAutoBeltStraight",x,7,Vector3.right);
  Place("yfAutoBeltLeft",8,7,Vector3.right);Place("yfAutoBeltStraight",8,8,Vector3.forward);
  var forgeInput=Place("yfAutoInput",8,9,Vector3.forward);Fill(forgeInput,Stack("yfAutoIngot_clay",1000));
  forge=Place("yfAutoForge",9,9,Vector3.forward);result=Place("yfAutoOutput",10,9,Vector3.forward);Fill(result,Stack("resourceForgedIron",1));
  Place("yfAutomationWorkbench",11,3,Vector3.forward);Place("generatorbank",11,11,Vector3.forward);
  var te=(TileEntityPowered)world.GetTileEntity(new Vector3i(11,75,11));te.InitializePowerData();var gen=(PowerGenerator)te.PowerItem;gen.SetSlots(Enumerable.Range(0,6).Select(i=>Stack("smallEngine",1)).ToArray());gen.CurrentFuel=gen.MaxFuel;gen.IsOn=true;
  Port(2,4,gen);Port(6,8,gen);Port(9,10,gen);te.CreateWireDataFromPowerItem();te.SetModified();
  world.SetTime(12000);GameManager.Instance.SaveWorld();File.WriteAllText(Folder+"/factory-photo-report.txt","Built: iron input -> smelter -> ingot buffer -> 8 belts with 2 corners -> clay/input buffer -> forge -> finished iron chest. All within one chunk.\n");
 }
 static void Update(ref ModEvents.SGameUpdateData data){if(phase==0)return;
  try{
   player=world.GetPrimaryPlayer();if(player==null)return;
   if(phase==1){if(!world.IsChunkAreaLoaded(new Vector3(8,76,8))){if(Time.realtimeSinceStartup>deadline)throw new Exception("chunk load timeout");return;}Build();phase=2;deadline=Time.realtimeSinceStartup+3;return;}
   if(phase==2){if(Time.realtimeSinceStartup<deadline)return;Machines();phase=3;deadline=Time.realtimeSinceStartup+120;}
   if(phase==3){
    // Keep the camera above the isolated platform while the actual factory ticks normally.
    player.SetPosition(new Vector3(8,83,-3),true);player.SetRotation(new Vector3(40,0,0));
    if(Time.realtimeSinceStartup<deadline)return;
    int count=result.GetFeature<TEFeatureStorage>().items.Skip(1).Sum(s=>s.count);
    File.AppendAllText(Folder+"/factory-photo-report.txt","Finished iron (excluding filter sample): "+count+"\nSmelter: "+smelter.GetFeature<TEFeatureSignable>().GetAuthoredText().Text+"\nForge: "+forge.GetFeature<TEFeatureSignable>().GetAuthoredText().Text+"\n");
    GameManager.Instance.SaveWorld();phase=4;
   }
   if(phase==4){player.SetPosition(new Vector3(8,83,-3),true);player.SetRotation(new Vector3(40,0,0));}
  }catch(Exception e){phase=0;File.AppendAllText(Folder+"/factory-photo-report.txt",e.ToString());Log.Error("FactoryPhotoQA "+e);}
 }
}
