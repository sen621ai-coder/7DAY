using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;
using YFAutomation;

public sealed class InteractionGameQA : IModApi
{
 const string Folder="E:/soft/7DTD-Modding/AutomationGameQA";
 static World world;static int phase;static float deadline;static readonly List<string> report=new List<string>();
 static PlatformUserIdentifierAbs localUser;
 // Dedicated servers have no local UI identity. Supply only that identity to
 // exercise the unmodified native command/lock checks as the machine owner.
 public static bool LocalIdentity(ref PlatformUserIdentifierAbs __result){__result=localUser;return false;}
 public void InitMod(Mod mod){if(!Environment.GetCommandLineArgs().Contains("-yfInteractionQA"))return;ModEvents.GameStartDone.RegisterHandler(Ready);ModEvents.GameUpdate.RegisterHandler(Update);}
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Ready(ref ModEvents.SGameStartDoneData data){if(GamePrefs.GetString(EnumGamePrefs.GameName)!="AutomationQA_Isolated")return;world=GameManager.Instance.World;GameManager.Instance.AddChunkObserver(new Vector3(8,120,8),false,4,4);phase=1;deadline=Time.realtimeSinceStartup+180;}
 static Transform Prefab(BlockValue v)=>(Transform)AccessTools.Method(typeof(BlockShapeModelEntity),"getPrefab").Invoke(v.Block.shape,null);
 static void Update(ref ModEvents.SGameUpdateData data){if(phase!=1)return;
  if(!world.IsChunkAreaLoaded(new Vector3(8,120,8))&&Time.realtimeSinceStartup<deadline)return;phase=0;
  var created=new List<GameObject>();var edits=new List<Tuple<Vector3i,BlockValue>>();
  try{
   var native=Prefab(Block.GetBlockValue("cntSteelWritableCrate"));report.Add("Native crate collider tags/layers: "+string.Join(",",native.GetComponentsInChildren<Collider>(true).Select(c=>c.tag+"/"+c.gameObject.layer+"/"+LayerMask.LayerToName(c.gameObject.layer))));
   var own=PlatformUserIdentifierAbs.FromCombinedString(File.ReadAllText(Folder+"/owner.txt").Trim(),false);
   localUser=own;new Harmony("yf.interaction.qa.local-user").Patch(AccessTools.PropertyGetter(typeof(Platform.PlatformManager),"InternalLocalUserIdentifier"),prefix:new HarmonyMethod(typeof(InteractionGameQA),nameof(LocalIdentity)));
   var player=(EntityPlayer)EntityFactory.CreateEntity(EntityClass.FromString("playerMale"),new Vector3(8,121,8));
   string[] names={"Sorter","Kitchen","Smelter","Forge","Recycler","Farm","Miner","Transfer","WaterPump","WaterTank","AmmoFeed","BeltStraight","BeltLeft","BeltRight","BeltUp","BeltDown"};
   int index=0;
   foreach(string kind in names){
    var pos=new Vector3i(2+(index%4)*3,120,2+(index/4)*3);index++;
    var value=Block.GetBlockValue("yfAuto"+kind);edits.Add(Tuple.Create(pos,world.GetBlock(pos)));world.SetBlockRPC(new BlockValueRef(pos),value);
    var chunk=(Chunk)world.GetChunkFromWorldPos(pos);var bed=chunk.GetBlockEntity(pos);Check(bed!=null,"No block entity stub: "+kind);
    var tile=world.GetTileEntity(pos) as TileEntityComposite;Check(tile!=null,"No tile: "+kind);tile.SetOwner(own);tile.GetFeature<TEFeatureLockable>()?.SetLocked(false);
    var go=UnityEngine.Object.Instantiate(Prefab(value)).gameObject;created.Add(go);go.SetActive(true);go.transform.position=new Vector3(pos.x+.5f,pos.y,pos.z+.5f)-Origin.position;
    bed.transform=go.transform;bed.bHasTransform=true;
    var collider=go.GetComponentsInChildren<Collider>().First();Physics.SyncTransforms();
    var center=collider.bounds.center+Origin.position;
    var ray=new Ray(center-Vector3.forward*2,Vector3.forward);
    bool before=Voxel.Raycast(world,ray,4,-555528221,1,0)&&Voxel.voxelRayHitInfo.hit.blockValue.type==value.type;
    Check(!before,"Regression fixture unexpectedly recognized unbound model: "+kind);
    // Invoke the real native tile hook, including the production Harmony postfix.
    tile.SetBlockEntityData(bed);Physics.SyncTransforms();
    foreach(var c in go.GetComponentsInChildren<Collider>())Check(c.CompareTag("T_Block")&&c.gameObject.layer==16&&RootTransformRefParent.FindRoot(c.transform)==go.transform,"Missing native collision identity "+kind);
    foreach(var dir in new[]{Vector3.forward,Vector3.back,Vector3.left,Vector3.right}){
     var origin=center-dir*2;
     Check(Voxel.Raycast(world,new Ray(origin,dir),4,-555528221,1,0),"Native selection ray missed "+kind);
     Check(Voxel.voxelRayHitInfo.hit.blockValue.type==value.type&&Voxel.voxelRayHitInfo.hit.blockPos==pos,"Wrong native block identity "+kind+": "+Voxel.voxelRayHitInfo.hit.blockPos);
    }
    var commands=value.Block.GetBlockActivationCommands(world,value,pos,player);
    Check(commands!=null&&commands.Any(c=>c.enabled&&c.text.EndsWith(tile.GetFeature<TEFeatureStorage>()!=null?":Search":":edit",StringComparison.OrdinalIgnoreCase)),"No enabled native use/edit command "+kind+": "+string.Join(",",commands.Select(c=>c.text)));
    // The graphical Block wrapper obtains key-binding markup from a local UI.
    // Supply that label here, then exercise the same native tile/feature prompt path.
    var text=tile.GetActivationText(world,pos,value,player,"E",value.Block.GetLocalizedBlockName());Check(!string.IsNullOrEmpty(text),"No native use prompt "+kind);
    report.Add("PASS "+kind+": unbound model fails; native tile hook restores four-side Voxel selection, correct block ID, prompt and enabled commands ["+string.Join(",",commands.Where(c=>c.enabled).Select(c=>c.text))+"]");
    bed.transform=null;bed.bHasTransform=false;go.SetActive(false);
   }
   report.Add("FINISHED failures=0");
  }catch(Exception e){report.Add("FAIL "+e);}
  finally{foreach(var e in edits){var chunk=(Chunk)world.GetChunkFromWorldPos(e.Item1);var bed=chunk?.GetBlockEntity(e.Item1);if(bed!=null){bed.transform=null;bed.bHasTransform=false;}world.SetBlockRPC(new BlockValueRef(e.Item1),e.Item2);}foreach(var go in created)UnityEngine.Object.Destroy(go);File.WriteAllLines(Folder+"/interaction-report.txt",report);Application.Quit();}
 }
}
