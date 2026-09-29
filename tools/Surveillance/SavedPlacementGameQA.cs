using System;
using System.Linq;
using UnityEngine;
using HarmonyLib;
using PZAEC.Surveillance;

// Run only on a disposable copy of the reported player save.
public sealed class SurveillanceSavedPlacementQA : IModApi
{
    static World world;static EntityPlayer player;static int phase,index;static float due,deadline;
    static readonly Vector3i[] roots={new Vector3i(1183,86,1130),new Vector3i(1172,86,1125)};
    static Vector3i current;static BlockValue placed;
    public void InitMod(Mod mod)
    {
        if(!Environment.GetCommandLineArgs().Contains("-surveillancePlacementQA"))return;
        ModEvents.GameStartDone.RegisterHandler(Ready);ModEvents.GameUpdate.RegisterHandler(Tick);
    }
    static bool PlayerUpdate(EntityPlayer __instance){return __instance!=player;}
    static void Ready(ref ModEvents.SGameStartDoneData data)
    {
        if(GamePrefs.GetString(EnumGamePrefs.GameName)!="SurveillanceQA_Isolated")return;
        world=GameManager.Instance.World;
        player=(EntityPlayer)EntityFactory.CreateEntity(EntityClass.FromString("playerMale"),new Vector3(1180,90,1130));
        player.MinEventContext.ItemValue=ItemValue.None;
        new Harmony("pzaec.surveillance.savedplacementqa").Patch(AccessTools.Method(typeof(EntityPlayer),"OnUpdateEntity"),prefix:new HarmonyMethod(typeof(SurveillanceSavedPlacementQA),nameof(PlayerUpdate)));
        new Harmony("pzaec.surveillance.savedplacementtrace").Patch(AccessTools.Method(typeof(World),"AddFallingBlock"),prefix:new HarmonyMethod(typeof(SurveillanceSavedPlacementQA),nameof(Falling)));
        world.SpawnEntityInWorld(player);
        GameManager.Instance.AddChunkObserver(player.position,false,4,-1);
        phase=1;deadline=Time.realtimeSinceStartup+100;
    }
    static void Falling(Vector3i __0)
    {
        if(world.GetBlock(__0).Block.GetBlockName()==SurveillanceState.ScreenBlock)
            Log.Out("[SurveillancePlacementQA] FALL REQUEST "+__0+" value="+world.GetBlock(__0)+" stability="+world.GetStability(__0)+" stack="+Environment.StackTrace);
    }
    static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
    static Vector3i Cell(Vector3i offset)
    {var v=placed.Block.shape.GetRotation(placed)*(Vector3)offset;return current+new Vector3i(Mathf.RoundToInt(v.x),Mathf.RoundToInt(v.y),Mathf.RoundToInt(v.z));}
    static void Tick(ref ModEvents.SGameUpdateData data)
    {
        if(phase==0||Time.realtimeSinceStartup<due)return;
        try
        {
            if(phase==1)
            {
                for(int x=72;x<=74;x++)for(int z=69;z<=71;z++)
                {
                    var p=new Vector3i(x*16,86,z*16);
                    var c=world.GetChunkFromWorldPos(p) as Chunk;
                    if(c==null||c.StopStabilityCalculation){Check(Time.realtimeSinceStartup<deadline,"Saved chunk initialization timeout");return;}
                }
                current=roots[index<4?0:1];placed=Block.GetBlockValue(SurveillanceState.ScreenBlock);placed.rotation=(byte)(index<4?index:0);
                Check(placed.Block.isMultiBlock&&!placed.Block.isOversized,"Wrong structural mode");
                foreach(var d in Vector3i.AllDirections)
                    Log.Out("[SurveillancePlacementQA] SAVED SUPPORT root="+current+" rotation="+placed.rotation+" dir="+d+" value="+world.GetBlock(current+d)+" stability="+world.GetStability(current+d));
                Check(placed.Block.multiBlockPos.pos.All(o=>world.GetBlock(Cell(o)).isair),"Saved test footprint overlaps an existing block at rotation "+placed.rotation);
                placed.Block.PlaceBlock(world,new BlockPlacement.Result(default(BlockPlacement.EnumPlacement),current.ToVector3(),current,BlockFace.South,placed,PropTransform.identity),null);
                foreach(var o in placed.Block.multiBlockPos.pos)
                    Log.Out("[SurveillancePlacementQA] SAVED CELL "+Cell(o)+" stability="+world.GetStability(Cell(o)));
                phase=2;due=Time.realtimeSinceStartup+4;return;
            }
            int count=placed.Block.multiBlockPos.pos.Count(o=>world.GetBlock(Cell(o)).type==placed.type);
            Check(count==12,"Saved location collapsed: root="+current+" rotation="+placed.rotation+" occupied="+count+"/12");
            foreach(var old in new[]{new Vector3i(1160,86,1126),new Vector3i(1172,86,1126)})
            {
                var v=world.GetBlock(old);Check(v.type==placed.type,"Previously saved screen disappeared: "+old);
                foreach(var o in v.Block.multiBlockPos.pos)
                {
                    var d=v.Block.shape.GetRotation(v)*(Vector3)o;
                    var p=old+new Vector3i(Mathf.RoundToInt(d.x),Mathf.RoundToInt(d.y),Mathf.RoundToInt(d.z));
                    Check(world.GetBlock(p).type==placed.type,"Previously saved screen lost a cell: "+p);
                }
                Check(world.GetTileEntity(old) is TileEntityPoweredBlock,"Previously saved screen lost powered tile: "+old);
            }
            Log.Out("[SurveillancePlacementQA] SAVED PASS root="+current+" rotation="+placed.rotation+" occupied="+count+"/12");
            world.SetBlockRPC(current,BlockValue.Air);
            index++;if(index==5){phase=0;Log.Out("[SurveillancePlacementQA] PLACEMENT PASS saved locations: four ground orientations, original wall location, both existing screens retained");return;}
            phase=1;due=Time.realtimeSinceStartup+1;
        }
        catch(Exception e){phase=0;Log.Error("[SurveillancePlacementQA] PLACEMENT FAIL "+e);}
    }
}
