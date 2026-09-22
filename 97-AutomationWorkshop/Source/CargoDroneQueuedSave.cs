using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;

namespace YFAutomation.CargoDrones
{
    public sealed class CargoNativeQueuedSave
    {
        internal readonly CargoEndpointMarker Marker;
        internal readonly Vector3i Position;
        internal readonly CargoItem[] Expected;
        internal readonly string Directory;
        internal CargoNativeSaveReceipt Receipt;
        internal string Error;
        internal CargoNativeQueuedSave(TileEntity endpoint,CargoEndpointMarker marker,CargoItem[] expected,string directory)
        {Position=endpoint.localChunkPos;Marker=marker;Expected=(CargoItem[])expected.Clone();Directory=directory;}
        public bool SnapshotCaptured{get{return Volatile.Read(ref Receipt)!=null;}}
        public CargoSaveResult Status{get{if(Volatile.Read(ref Error)!=null)return CargoSaveResult.Uncertain;var receipt=Volatile.Read(ref Receipt);return receipt==null?CargoSaveResult.Pending:receipt.Status;}}
        public string Failure{get{return Volatile.Read(ref Error)??Volatile.Read(ref Receipt)?.Failure;}}
    }
    // Bridges fenced endpoint transactions to the native asynchronous save queue.
    public static class CargoNativeQueuedSaves
    {
        static readonly ConditionalWeakTable<Chunk,CargoNativeQueuedSave> pending=new ConditionalWeakTable<Chunk,CargoNativeQueuedSave>();
        static bool installed;
        [ThreadStatic] static Chunk verificationClone;
        // A detached snapshot decoder cannot mutate a live endpoint. Scope this
        // exemption to the freshly allocated clone, not to every tile on a thread.
        internal static bool IsVerificationClone(TileEntity tile)
        {return verificationClone!=null&&tile.GetChunk()==verificationClone;}
        sealed class Capture{public Chunk Chunk;public CargoNativeQueuedSave Request;public object Gate;}
        public static void InstallForValidation(Harmony harmony)
        {
            if(installed)return;CargoNativeSaveReceipts.InstallForValidation(harmony);
            harmony.Patch(AccessTools.Method(typeof(RegionFileChunkSnapshot),"Update"),prefix:new HarmonyMethod(typeof(CargoNativeQueuedSaves),nameof(BeforeCapture)),finalizer:new HarmonyMethod(typeof(CargoNativeQueuedSaves),nameof(AfterCapture)));
            installed=true;
        }
        public static CargoNativeQueuedSave EnqueueForValidation(RegionFileManager manager,TileEntity endpoint,CargoEndpointMarker marker,CargoItem[] expected)
        {
            if(!installed||manager==null||endpoint==null||marker==null||expected==null||expected.Length>4096||endpoint.GetChunk()==null)throw new ArgumentException("Invalid queued save request");
            var chunk=endpoint.GetChunk();var request=new CargoNativeQueuedSave(endpoint,marker,expected,manager.saveDirectory);
            lock(pending){pending.Add(chunk,request);}
            try
            {
                manager.SaveChunkSnapshot(chunk,true);
                if(!request.SnapshotCaptured&&request.Status==CargoSaveResult.Pending)request.Error="Native queue did not synchronously capture the requested endpoint";
            }
            catch(Exception error){Volatile.Write(ref request.Error,error.Message);}
            finally{lock(pending){pending.Remove(chunk);}}
            return request;
        }
        public static void BeforeCapture(Chunk __0,out object __state)
        {
            __state=null;CargoNativeQueuedSave request;lock(pending){if(!pending.TryGetValue(__0,out request))return;}
            var capture=new Capture{Chunk=__0,Request=request,Gate=ChunkTransferLock.For(__0)};
            Monitor.Enter(capture.Gate);__state=capture;
        }
        public static void AfterCapture(RegionFileChunkSnapshot __instance,Exception __exception,object __state)
        {
            var capture=__state as Capture;if(capture==null)return;var request=capture.Request;
            try
            {
                if(__exception!=null)throw new InvalidDataException("Native snapshot failed",__exception);
                if(__instance.stream==null||__instance.stream.Length<8||__instance.stream.Length>64L*1024*1024)throw new InvalidDataException("Missing native snapshot body");
                // Native Update logs some serialization failures instead of throwing.
                // Decode the exact stream and verify endpoint identity AND inventory;
                // a live-memory check cannot establish what was actually captured.
                var clone=new Chunk(capture.Chunk.X,capture.Chunk.Z);
                using(var memory=new MemoryStream(__instance.stream.ToArray(),false))
                using(var reader=MemoryPools.poolBinaryReader.AllocSync(false))
                {
                    reader.SetBaseStream(memory);
                    if(!reader.ReadBytes(4).SequenceEqual(new byte[]{116,116,99,0}))throw new InvalidDataException("Invalid native snapshot header");
                    uint version=reader.ReadUInt32();var previous=verificationClone;
                    try{verificationClone=clone;clone.load(reader,version);}
                    finally{verificationClone=previous;}
                    if(memory.Position!=memory.Length)throw new InvalidDataException("Native snapshot contains trailing data");
                }
                var tile=clone.GetTileEntity(request.Position);var marker=CargoNativeMarkers.Read(tile,request.Marker.WorldId);var expected=request.Marker;
                if(marker==null||marker.EndpointId!=expected.EndpointId||marker.Incarnation!=expected.Incarnation||marker.Owner!=expected.Owner||marker.BlockName!=expected.BlockName||marker.Revision!=expected.Revision||marker.LastTransaction!=expected.LastTransaction)
                    throw new InvalidDataException("Captured endpoint marker does not match requested commit");
                var collector=tile as TileEntityCollector;var composite=tile as TileEntityComposite;
                var values=collector!=null?collector.Items:composite?.GetFeature<TEFeatureStorage>()?.items;
                if(values==null||!CargoPlanner.Equal(values.Select(CargoNativeItems.Encode).ToArray(),request.Expected))throw new InvalidDataException("Captured endpoint inventory does not match requested commit");
                Volatile.Write(ref request.Receipt,CargoNativeSaveReceipts.Watch(__instance,request.Directory,clone.X,clone.Z));
            }
            catch(Exception error){Volatile.Write(ref request.Error,error.Message);}
            finally{Monitor.Exit(capture.Gate);}
        }
    }
}
