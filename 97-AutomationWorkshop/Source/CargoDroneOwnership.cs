using System;
using System.IO;
using System.Text;
using HarmonyLib;

namespace YFAutomation.CargoDrones
{
    // Placement/removal rotate persistent instance identities. The legacy owner
    // label is diagnostic metadata, not a binding or inventory permission.
    public static class CargoCollectorOwnership
    {
        static World identityWorld;
        static Guid identity;
        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(BlockCollector),"OnBlockAdded"),
                postfix:new HarmonyMethod(typeof(CargoCollectorOwnership),nameof(Placed)));
            harmony.Patch(AccessTools.Method(typeof(BlockCollector),"OnBlockRemoved"),
                prefix:new HarmonyMethod(typeof(CargoCollectorOwnership),nameof(Removed)));
            harmony.Patch(AccessTools.Method(typeof(BlockCompositeTileEntity),"OnBlockAdded"),
                postfix:new HarmonyMethod(typeof(CargoCollectorOwnership),nameof(StoragePlaced)));
            harmony.Patch(AccessTools.Method(typeof(BlockCompositeTileEntity),"OnBlockRemoved"),
                prefix:new HarmonyMethod(typeof(CargoCollectorOwnership),nameof(StorageRemoved)));
        }
        public static string StorageOwner(TileEntityComposite tile)
        {
            if(tile==null||tile.IsRemoving||!(tile.GetFeature<TEFeatureStorage>()?.bPlayerStorage??false))return null;
            return (tile.GetFeature<TEFeatureLockable>()?.GetOwner()??tile.Owner)?.CombinedString;
        }
        public static void StoragePlaced(WorldBase __0,Chunk __1,Vector3i __2,BlockValue __3,PlatformUserIdentifierAbs __4)
        {
            var world=__0 as World;
            if(world==null||world.IsRemote()||__3.ischild)return;
            var tile=world.GetTileEntity(__2) as TileEntityComposite;if(tile==null)return;
            try
            {
                lock(ChunkTransferLock.For(tile.GetChunk()))
                {
                    // Clear positional metadata even when the replacement is not a player container.
                    CargoNativeMarkers.Remove(tile);
                    if(!(tile.GetFeature<TEFeatureStorage>()?.bPlayerStorage??false))return;
                    var owner=StorageOwner(tile)??"shared";
                    CargoNativeMarkers.Write(tile,new CargoEndpointMarker(WorldIdentity(world),Guid.NewGuid(),Guid.NewGuid(),
                        owner,__3.Block.GetBlockName(),0,Guid.Empty));
                }
            }
            catch(Exception error){Log.Error("[CargoDrone] Could not record storage placement at "+__2+": "+error.Message);}
        }
        public static void StorageRemoved(WorldBase __0,Chunk __1,Vector3i __2,BlockValue __3)
        {
            var world=__0 as World;if(world==null||world.IsRemote()||__3.ischild)return;
            var tile=world.GetTileEntity(__2) as TileEntityComposite;if(tile==null)return;
            lock(ChunkTransferLock.For(tile.GetChunk()))CargoNativeMarkers.Remove(tile);
        }
        public static Guid WorldIdentity(World world)
        {
            if(world==null||world.IsRemote())throw new InvalidOperationException("Server world required");
            if(identityWorld==world&&identity!=Guid.Empty)return identity;
            string path=Path.Combine(GameIO.GetSaveGameDir(),"cargo-world-id.txt");
            Guid loaded;
            if(File.Exists(path))
            {
                if(!Guid.TryParseExact(File.ReadAllText(path).Trim(),"D",out loaded)||loaded==Guid.Empty)
                    throw new InvalidDataException("Cargo world identity is corrupt; refusing to replace it");
            }
            else
            {
                loaded=Guid.NewGuid();
                // CreateNew prevents accidental replacement of another world's identity.
                using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {var bytes=Encoding.ASCII.GetBytes(loaded.ToString("D"));file.Write(bytes,0,bytes.Length);file.Flush(true);}
            }
            identityWorld=world;identity=loaded;return loaded;
        }
        public static void Placed(WorldBase __0,Chunk __1,Vector3i __2,BlockValue __3,PlatformUserIdentifierAbs __4)
        {
            var world=__0 as World;
            if(world==null||world.IsRemote()||__3.ischild||!CargoRules.IsSource(__3.Block.GetBlockName()))return;
            var tile=world.GetTileEntity(__2) as TileEntityCollector;
            if(tile==null)return;
            try
            {
                lock(ChunkTransferLock.For(tile.GetChunk()))
                {
                    CargoNativeMarkers.Remove(tile);
                    CargoNativeMarkers.Write(tile,new CargoEndpointMarker(WorldIdentity(world),Guid.NewGuid(),Guid.NewGuid(),
                        __4?.CombinedString??"shared",__3.Block.GetBlockName(),0,Guid.Empty));
                }
            }
            catch(Exception error){Log.Error("[CargoDrone] Could not record collector placement at "+__2+": "+error.Message);}
        }
        public static void Removed(WorldBase __0,Chunk __1,Vector3i __2,BlockValue __3)
        {
            var world=__0 as World;
            if(world==null||world.IsRemote()||__3.ischild||!CargoRules.IsSource(__3.Block.GetBlockName()))return;
            var tile=world.GetTileEntity(__2) as TileEntityCollector;
            if(tile==null)return;
            lock(ChunkTransferLock.For(tile.GetChunk()))CargoNativeMarkers.Remove(tile);
        }
    }
}
