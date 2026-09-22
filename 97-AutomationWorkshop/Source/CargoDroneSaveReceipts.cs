using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using HarmonyLib;

namespace YFAutomation.CargoDrones
{
    // Candidate G03 primitive, opt-in only. A receipt proves one exact native
    // snapshot write; it does not supply endpoint fencing or restart recovery.
    public sealed class CargoNativeSaveReceipt
    {
        internal readonly string Directory;
        internal readonly int X,Z;
        internal readonly byte[] Hash;
        int status;
        public CargoSaveResult Status{get{return (CargoSaveResult)Volatile.Read(ref status);}}
        public string Failure{get;private set;}
        internal CargoNativeSaveReceipt(string directory,int x,int z,byte[] hash){Directory=directory;X=x;Z=z;Hash=hash;}
        internal void Fail(string error){Failure=error;Volatile.Write(ref status,(int)CargoSaveResult.Uncertain);}
        internal void Complete(){if(Status==CargoSaveResult.Pending)Volatile.Write(ref status,(int)CargoSaveResult.Durable);}
    }
    public static class CargoNativeSaveReceipts
    {
        static readonly ConditionalWeakTable<RegionFileChunkSnapshot,CargoNativeSaveReceipt> watched=new ConditionalWeakTable<RegionFileChunkSnapshot,CargoNativeSaveReceipt>();
        sealed class WriteContext{public CargoNativeSaveReceipt Receipt;public bool Proof;public WriteContext Previous;}
        [ThreadStatic] static WriteContext context;
        static bool installed;
        static byte[] Digest(RegionFileChunkSnapshot snapshot)
        {
            if(snapshot==null||snapshot.stream==null||snapshot.stream.Length<8||snapshot.stream.Length>64L*1024*1024)throw new InvalidDataException("Invalid native snapshot");
            using(var sha=SHA256.Create())return sha.ComputeHash(snapshot.stream.ToArray());
        }
        public static void InstallForValidation(Harmony harmony)
        {
            if(installed)return;
            if(typeof(Chunk).Module.ModuleVersionId!=new Guid("229796d0-95ca-4662-b426-1a6f1f1596ed"))throw new NotSupportedException("Native save receipt API has not been validated on this assembly");
            harmony.Patch(AccessTools.Method(typeof(RegionFileChunkSnapshot),"Write"),prefix:new HarmonyMethod(typeof(CargoNativeSaveReceipts),nameof(BeforeSnapshot)),finalizer:new HarmonyMethod(typeof(CargoNativeSaveReceipts),nameof(AfterSnapshot)));
            harmony.Patch(AccessTools.Method(typeof(RegionFileRaw),"WriteData"),prefix:new HarmonyMethod(typeof(CargoNativeSaveReceipts),nameof(BeforeRegion)),postfix:new HarmonyMethod(typeof(CargoNativeSaveReceipts),nameof(AfterRegion)),finalizer:new HarmonyMethod(typeof(CargoNativeSaveReceipts),nameof(ReleaseRegion)));
            harmony.Patch(AccessTools.Method(typeof(RegionFileV2),"WriteData"),prefix:new HarmonyMethod(typeof(CargoNativeSaveReceipts),nameof(BeforeRegion)),postfix:new HarmonyMethod(typeof(CargoNativeSaveReceipts),nameof(AfterRegion)),finalizer:new HarmonyMethod(typeof(CargoNativeSaveReceipts),nameof(ReleaseRegion)));
            installed=true;
        }
        public static CargoNativeSaveReceipt Watch(RegionFileChunkSnapshot snapshot,string directory,int x,int z)
        {
            if(!installed)throw new InvalidOperationException("Native receipt hooks not installed");
            string root=Path.GetFullPath(GameIO.GetSaveGameDir()).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
            string target=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!target.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Receipt directory must be within current save");
            var receipt=new CargoNativeSaveReceipt(target,x,z,Digest(snapshot));
            lock(watched)
            {
                CargoNativeSaveReceipt previous;
                if(watched.TryGetValue(snapshot,out previous))
                {if(previous.Status==CargoSaveResult.Pending)throw new InvalidOperationException("Snapshot already has an unresolved receipt");watched.Remove(snapshot);}
                watched.Add(snapshot,receipt);
            }
            return receipt;
        }
        public static void BeforeSnapshot(RegionFileChunkSnapshot __instance,string __1,int __2,int __3,out object __state)
        {
            __state=null;CargoNativeSaveReceipt receipt;if(!watched.TryGetValue(__instance,out receipt))return;
            if(receipt.Status!=CargoSaveResult.Pending)return;
            var next=new WriteContext{Receipt=receipt,Previous=context};__state=next;context=next;
            try
            {
                string directory=Path.GetFullPath(__1).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
                if(next.Previous!=null||directory!=receipt.Directory||__2!=receipt.X||__3!=receipt.Z||!Digest(__instance).SequenceEqual(receipt.Hash))
                    receipt.Fail("Snapshot identity, bytes or destination changed before native write");
            }
            catch(Exception error){receipt.Fail(error.Message);}
        }
        public static void AfterSnapshot(Exception __exception,object __state)
        {
            var current=__state as WriteContext;if(current==null)return;
            try
            {
                if(__exception!=null)current.Receipt.Fail(__exception.Message);
                else if(!current.Proof)current.Receipt.Fail("No verified native region/header flush");
                else current.Receipt.Complete();
            }
            finally{context=current.Previous;}
        }
        public static void BeforeRegion(RegionFile __instance,out object __state)
        {
            __state=null;if(context==null||context.Receipt.Status!=CargoSaveResult.Pending)return;
            // Extend the native region monitor through header flush and readback.
            // This is on the native save worker during normal asynchronous saves.
            Monitor.Enter(__instance);__state=__instance;
        }
        public static void AfterRegion(RegionFile __instance,int __0,int __1,int __2,byte[] __4,bool __5,object __state)
        {
            if(__state==null)return;var current=context;var receipt=current.Receipt;
            try
            {
                string file=Path.GetFullPath(__instance.fullFilePath);
                if(current.Proof||!__5||__0!=receipt.X||__1!=receipt.Z||__2<1||__4==null||__2>__4.Length||!file.StartsWith(receipt.Directory,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Unexpected region write or header not saved");
                using(var handle=new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.Read)){handle.Flush(true);}
                // Reopen through a new native access object so verification reads
                // the persisted region header, not the writer's cached offsets.
                RegionFileAccessAbstract fresh=__instance is RegionFileRaw?(RegionFileAccessAbstract)new RegionFileAccessRaw():new RegionFileAccessSectorBased();
                try
                {
                    using(var source=fresh.GetInputStream(receipt.Directory,__0,__1,"7rg"))
                    using(var copy=new MemoryStream())
                    {
                        if(source==null)throw new InvalidDataException("Persisted native region header has no snapshot");
                        source.CopyTo(copy);
                        if(copy.Length!=__2||!copy.ToArray().SequenceEqual(__4.Take(__2)))throw new InvalidDataException("Native region readback differs from compressed snapshot");
                    }
                }
                finally{fresh.Close();}
                current.Proof=true;
            }
            catch(Exception error){receipt.Fail(error.Message);}
        }
        public static void ReleaseRegion(Exception __exception,object __state)
        {
            if(__state==null)return;
            try{if(__exception!=null&&context!=null)context.Receipt.Fail(__exception.Message);}
            finally{Monitor.Exit(__state);}
        }
    }
}
