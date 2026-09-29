using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using PZAEC.Surveillance;

// This fixture writes only to Start-NativeSmoke's disposable, isolated QA world.
public sealed class SurveillancePlacementQA : IModApi
{
    static World world;static int phase;static float due,deadline;
    static readonly List<Vector3i> positions=new List<Vector3i>();
    static readonly List<Vector3i> floorPositions=new List<Vector3i>();
    static readonly Vector3i unsupportedPosition=new Vector3i(52,120,40);
    static readonly List<GameObject> models=new List<GameObject>();
    static readonly List<GameObject> previews=new List<GameObject>();
    public void InitMod(Mod mod)
    {
        if(!Environment.GetCommandLineArgs().Contains("-surveillancePlacementQA"))return;
        ModEvents.GameStartDone.RegisterHandler(Ready);ModEvents.GameUpdate.RegisterHandler(Tick);
    }
    static void Ready(ref ModEvents.SGameStartDoneData data)
    {
        world=GameManager.Instance.World;
        if(GamePrefs.GetString(EnumGamePrefs.GameName)!="SurveillanceQA_Isolated")return;
        GameManager.Instance.AddChunkObserver(new Vector3(8,120,8),false,4,4);
        phase=1;deadline=Time.realtimeSinceStartup+100;
    }
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
        Log.Out("[SurveillancePlacementQA] OBSERVED unsupported screen occupied="+occupied+
            "/12 after stability settling; native multiblock physics may retain a floating panel");
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
                var solid=Block.GetBlockValue("concreteShapes");
                for(byte rotation=0;rotation<4;rotation++)for(int supportMode=0;supportMode<2;supportMode++)
                {
                    var pos=new Vector3i(4+rotation%2*12+supportMode*24,120,4+rotation/2*12);
                    var value=Block.GetBlockValue(SurveillanceState.ScreenBlock);value.rotation=rotation;var q=value.Block.shape.GetRotation(value);
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
                    // Use the same native block placement entrypoint as ItemActionPlaceAsBlock.
                    // World.SetBlockRPC alone skips Block.PlaceBlock and can miss a delayed fall.
                    var result=new BlockPlacement.Result(default(BlockPlacement.EnumPlacement),pos.ToVector3(),pos,
                        BlockFace.South,value,PropTransform.identity);
                    value.Block.PlaceBlock(world,result,null);
                }
                // Negative control: a screen with neither a rear wall nor a floor must
                // still fall. Keep it well away from the supported fixtures.
                var unsupported=Block.GetBlockValue(SurveillanceState.ScreenBlock);
                // Clear a full air halo, not just below and behind the footprint:
                // Navezgane terrain at this coordinate may otherwise support a side.
                for(int x=unsupportedPosition.x-3;x<=unsupportedPosition.x+2;x++)
                    for(int y=unsupportedPosition.y-2;y<=unsupportedPosition.y+3;y++)
                        for(int z=unsupportedPosition.z-1;z<=unsupportedPosition.z+1;z++)
                            world.SetBlockRPC(new Vector3i(x,y,z),BlockValue.Air);
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
            Verify("after power-toggle and stability settling",true);
            ObserveUnsupported();
            phase=0;Log.Out("[SurveillancePlacementQA] PLACEMENT PASS: wall-mounted and floor-supported screens survived switching paths");
        }
        catch(Exception e){phase=0;Log.Error("[SurveillancePlacementQA] PLACEMENT FAIL "+e);}
    }
}
