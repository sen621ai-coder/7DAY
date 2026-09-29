using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;
using PZAEC.Surveillance;

// This fixture writes only to Start-NativeSmoke's disposable, isolated QA world.
public sealed class SurveillancePlacementQA : IModApi
{
    static World world;static int phase;static float due,deadline;
    static EntityPlayer observerPlayer;
    static readonly List<Vector3i> positions=new List<Vector3i>();
    static readonly List<BlockValue> placedValues=new List<BlockValue>();
    static readonly List<Vector3i> floorPositions=new List<Vector3i>();
    static readonly Vector3i unsupportedPosition=new Vector3i(52,120,40);
    static readonly List<GameObject> models=new List<GameObject>();
    static readonly List<GameObject> previews=new List<GameObject>();
    static readonly List<Vector3i> detached=new List<Vector3i>();
    public void InitMod(Mod mod)
    {
        if(!Environment.GetCommandLineArgs().Contains("-surveillancePlacementQA"))return;
        ModEvents.GameStartDone.RegisterHandler(Ready);ModEvents.GameUpdate.RegisterHandler(Tick);
    }
    static void Ready(ref ModEvents.SGameStartDoneData data)
    {
        world=GameManager.Instance.World;
        if(GamePrefs.GetString(EnumGamePrefs.GameName)!="SurveillanceQA_Isolated")return;
        // Native lighting/stability initialization processes Players.Count * 2
        // chunks per tick. A chunk observer alone leaves every chunk disabled.
        observerPlayer=(EntityPlayer)EntityFactory.CreateEntity(EntityClass.FromString("playerMale"),new Vector3(28,120,24));
        observerPlayer.MinEventContext.ItemValue=ItemValue.None;
        new Harmony("pzaec.surveillance.placementqa").Patch(AccessTools.Method(typeof(EntityPlayer),"OnUpdateEntity"),prefix:new HarmonyMethod(typeof(SurveillancePlacementQA),nameof(UpdateObserver)));
        world.SpawnEntityInWorld(observerPlayer);
        GameManager.Instance.AddChunkObserver(new Vector3(28,120,24),false,6,-1);
        phase=1;deadline=Time.realtimeSinceStartup+100;
    }
    static bool UpdateObserver(EntityPlayer __instance){return __instance!=observerPlayer;}
    static Vector3i Rotate(Quaternion q,Vector3i v)
    {var p=q*new Vector3(v.x,v.y,v.z);return new Vector3i(Mathf.RoundToInt(p.x),Mathf.RoundToInt(p.y),Mathf.RoundToInt(p.z));}
    static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
    static void Verify(string stage,bool requirePower=false)
    {
        foreach(var p in positions)
        {
            var v=world.GetBlock(p);Check(v.Block.GetBlockName()==SurveillanceState.ScreenBlock&&!v.ischild,stage+": root vanished "+p+" now="+v);
            var q=v.Block.shape.GetRotation(v);int count=0;
            foreach(var offset in v.Block.multiBlockPos.pos)
            {
                var cell=p+Rotate(q,offset);var child=world.GetBlock(cell);
                Check(child.type==v.type,stage+": occupied cell vanished "+cell);
                if(child.ischild)Check(child.Block.multiBlockPos.GetParentPos(cell,child)==p,stage+": child parent mismatch");
                count++;
            }
            Check(count==12,stage+": footprint count "+count);
            if(requirePower)Check(world.GetTileEntity(p) is TileEntityPoweredBlock,stage+": powered tile missing "+p);
        }
        Log.Out("[SurveillancePlacementQA] PASS "+stage+": "+positions.Count+" screens, all "+(positions.Count*12)+
            " occupied cells remain (wall="+(positions.Count-floorPositions.Count)+", floor="+floorPositions.Count+"); powered tiles required="+requirePower);
    }
    static void ObserveUnsupported()
    {
        var value=Block.GetBlockValue(SurveillanceState.ScreenBlock);
        var rotation=value.Block.shape.GetRotation(value);
        int occupied=0;
        foreach(var offset in value.Block.multiBlockPos.pos)
        {
            var cell=unsupportedPosition+Rotate(rotation,offset);
            if(world.GetBlock(cell).type==value.type)occupied++;
        }
        Check(occupied==0,"Negative control did not fall: occupied="+occupied+"/12; physics validation is invalid");
        Log.Out("[SurveillancePlacementQA] PASS unsupported screen fell (0/12)");
    }
    static void Tick(ref ModEvents.SGameUpdateData data)
    {
        if(phase==0||Time.realtimeSinceStartup<due)return;
        try
        {
            if(phase==1)
            {
                if(!world.IsChunkAreaLoaded(new Vector3(8,120,8)))
                {Check(Time.realtimeSinceStartup<deadline,"Chunk load timeout");return;}
                // Loaded block data is not sufficient: newly loaded chunks suppress
                // all structural calculations until StabilityInitializer finishes.
                for(int x=0;x<=3;x++)for(int z=0;z<=2;z++)
                {
                    var chunk=world.GetChunkFromWorldPos(new Vector3i(x*16,120,z*16)) as Chunk;
                    if(chunk==null||chunk.StopStabilityCalculation)
                    {Check(Time.realtimeSinceStartup<deadline,"Chunk stability initialization timeout "+x+","+z);return;}
                }
                Log.Out("[SurveillancePlacementQA] PHYSICS active="+GameManager.bPhysicsActive+" stability="+GameStats.GetBool(EnumGameStats.ChunkStabilityEnabled)+" timeScale="+Time.timeScale);
                var solid=Block.GetBlockValue("concreteShapes:cube");
                Check(!solid.isair,"Concrete support block is missing");
                for(byte rotation=0;rotation<4;rotation++)for(int supportMode=0;supportMode<2;supportMode++)
                {
                    var pos=new Vector3i(4+rotation%2*12+supportMode*24,120,4+rotation/2*12);
                    var value=Block.GetBlockValue(SurveillanceState.ScreenBlock);value.rotation=rotation;var q=value.Block.shape.GetRotation(value);
                    Check(value.Block.isMultiBlock&&!value.Block.isOversized,"Wrong structural mode");
                    if(supportMode==0)
                    {
                        // Full supporting wall down to bedrock, with no floor under the screen.
                        for(int x=-2;x<=1;x++)for(int y=-119;y<=2;y++)world.SetBlockRPC(pos+Rotate(q,new Vector3i(x,y,-1)),solid);
                    }
                    else
                    {
                        // A freestanding monitor placed on a roof has solid floor beneath
                        // its bottom four cells, but no wall behind it. This is the geometry
                        // from the player's screenshots and was absent from earlier smoke runs.
                        int bottom=value.Block.multiBlockPos.pos.Min(offset=>offset.y);
                        foreach(var offset in value.Block.multiBlockPos.pos.Where(offset=>offset.y==bottom))
                        {
                            var foot=pos+Rotate(q,offset)+Vector3i.down;
                            for(int y=1;y<=foot.y;y++)world.SetBlockRPC(new Vector3i(foot.x,y,foot.z),solid);
                        }
                        floorPositions.Add(pos);
                    }
                    foreach(var offset in value.Block.multiBlockPos.pos)world.SetBlockRPC(pos+Rotate(q,offset),BlockValue.Air);
                    positions.Add(pos);
                    placedValues.Add(value);
                    foreach(var offset in value.Block.multiBlockPos.pos.Where(o=>supportMode==0||o.y==0))
                    {
                        var support=pos+Rotate(q,offset+(supportMode==0?new Vector3i(0,0,-1):Vector3i.down));
                        Check(world.GetBlock(support).type==solid.type,"Support was not actually built: "+support);
                        Check(world.GetStability(support)>0,"Support has no structural stability: "+support);
                    }
                }
                // Negative control: a screen with neither a rear wall nor a floor must
                // still fall. Keep it well away from the supported fixtures.
                // Clear a full air halo, not just below and behind the footprint:
                // Navezgane terrain at this coordinate may otherwise support a side.
                for(int x=unsupportedPosition.x-3;x<=unsupportedPosition.x+2;x++)
                    for(int y=unsupportedPosition.y-2;y<=unsupportedPosition.y+3;y++)
                        for(int z=unsupportedPosition.z-1;z<=unsupportedPosition.z+1;z++)
                            world.SetBlockRPC(new Vector3i(x,y,z),BlockValue.Air);
                Log.Out("[SurveillancePlacementQA] Scaffolding complete; queued stability checks="+world.ChunkCache.stabilityCalcMainThread.queueStabilityAvail.Count);
                phase=6;due=Time.realtimeSinceStartup+2;deadline=Time.realtimeSinceStartup+30;return;
            }
            if(phase==6)
            {
                // Native BlockPlacedAt drops new checks once its 200-entry queue
                // is full. Drain scaffold construction before testing placements.
                var calculator=world.ChunkCache.stabilityCalcMainThread;
                if(calculator.queueStabilityAvail.Count!=0||calculator.queueStabilityEmpty.Count!=0||world.fallingBlockSet.Count!=0)
                {Check(Time.realtimeSinceStartup<deadline,"Scaffold physics did not settle");return;}
                Log.Out("[SurveillancePlacementQA] Scaffold physics drained; placing test screens");
                for(int i=0;i<positions.Count;i++)
                {
                    var pos=positions[i];var value=placedValues[i];
                    var result=new BlockPlacement.Result(default(BlockPlacement.EnumPlacement),pos.ToVector3(),pos,
                        BlockFace.South,value,PropTransform.identity);
                    value.Block.PlaceBlock(world,result,null);
                }
                var unsupported=Block.GetBlockValue(SurveillanceState.ScreenBlock);
                unsupported.Block.PlaceBlock(world,new BlockPlacement.Result(default(BlockPlacement.EnumPlacement),
                    unsupportedPosition.ToVector3(),unsupportedPosition,BlockFace.South,unsupported,PropTransform.identity),null);
                foreach(var offset in unsupported.Block.multiBlockPos.pos)
                {
                    var cell=unsupportedPosition+Rotate(unsupported.Block.shape.GetRotation(unsupported),offset);
                    Check(world.GetBlock(cell).type==unsupported.type,"Unsupported control was never placed at "+cell);
                }
                phase=2;due=Time.realtimeSinceStartup+3;return;
            }
            if(phase==2)
            {
                Verify("after native placement and stability delay");
                foreach(var pos in positions)
                {
                    var v=world.GetBlock(pos);var shape=(BlockShapeModelEntity)v.Block.shape;
                    var holder=new GameObject("QA preview holder");previews.Add(holder);
                    var preview=shape.CloneModel(v,holder.transform);preview.gameObject.SetActive(true);
                    var go=shape.CloneModel(v,null).gameObject;models.Add(go);go.SetActive(true);
                    go.transform.SetPositionAndRotation(new Vector3(pos.x+.5f,pos.y,pos.z+.5f)-Origin.position,shape.GetRotation(v));
                    var bed=((Chunk)world.GetChunkFromWorldPos(pos)).GetBlockEntity(pos);Check(bed!=null,"No model stub");bed.transform=go.transform;bed.bHasTransform=true;
                    v.Block.OnBlockEntityTransformAfterActivated(world,pos,v,bed);
                }
                // The original tool's StartHolding path invokes this exact native wire-pulse method.
                WireManager.Instance.ToggleAllWirePulse(true);
                foreach(var preview in previews)new GameObjectPool().DestroyObject(preview);
                previews.Clear();phase=3;due=Time.realtimeSinceStartup+3;return;
            }
            if(phase==3)
            {
                Verify("after preview disposal and wire-tool pulse");
                foreach(var go in models)
                {
                    Check(go!=null&&go.activeInHierarchy&&go.GetComponent<BoxCollider>().enabled,"Placed model/collider lost on preview disposal");
                    foreach(var renderer in go.GetComponentsInChildren<Renderer>(true))
                        Check(renderer.sharedMaterial!=null,"Placed renderer material destroyed with wrapped preview: "+renderer.name);
                }
                // Vanilla powered lights also create their tile lazily through the wire tool.
                // Check unconnected occupancy first; only then exercise that native path.
                foreach(var p in positions){var tile=new ItemActionConnectPower().GetPoweredBlock(p) as TileEntityPoweredBlock;Check(tile!=null,"Native wire tool did not create powered tile");tile.IsToggled=false;tile.IsToggled=true;}
                WireManager.Instance.ToggleAllWirePulse(false);
                phase=4;due=Time.realtimeSinceStartup+8;return;
            }
            if(phase==4)
            {
                Verify("after power-toggle and stability settling",true);
                ObserveUnsupported();
                // A legitimate loss of the supporting wall/floor must still collapse.
                foreach(var pos in new[]{positions[0],floorPositions.Last()})
                {
                    var value=world.GetBlock(pos);var q=value.Block.shape.GetRotation(value);bool floor=floorPositions.Contains(pos);
                    foreach(var offset in value.Block.multiBlockPos.pos.Where(o=>!floor||o.y==0))
                        world.SetBlockRPC(pos+Rotate(q,offset+(floor?Vector3i.down:new Vector3i(0,0,-1))),BlockValue.Air);
                    detached.Add(pos);
                }
                phase=5;due=Time.realtimeSinceStartup+6;return;
            }
            foreach(var pos in detached)Check(world.GetBlock(pos).isair,"Unsupported detached screen did not fall: "+pos);
            phase=0;Log.Out("[SurveillancePlacementQA] PLACEMENT PASS: 96 supported cells survived switching paths; floating and detached screens fell");
        }
        catch(Exception e){phase=0;Log.Error("[SurveillancePlacementQA] PLACEMENT FAIL "+e);}
    }
}
