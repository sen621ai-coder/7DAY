using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using HarmonyLib;

namespace YFAutomation.CargoDrones
{
    // Native adapter for the verified game build. Session tracking is installed
    // before access; a transaction requires a drained endpoint and writer fence.
    // End-to-end multiplayer and the complete crash matrix remain QA obligations.
    public sealed class CargoNativeValidationEndpoint : ICargoDurableEndpoint
    {
        static readonly Dictionary<TileEntity,Guid> fences=new Dictionary<TileEntity,Guid>();
        static bool installed;
        static bool recoveryQuarantine;
        static Guid quarantineWorld;
        static readonly HashSet<Guid> quarantineEndpoints=new HashSet<Guid>();
        public static void SetStartupQuarantine(Guid world,IEnumerable<Guid> endpoints)
        {lock(fences){quarantineWorld=world;quarantineEndpoints.Clear();foreach(var id in endpoints)quarantineEndpoints.Add(id);}}
        public static void ResetWorld(){lock(fences){fences.Clear();quarantineEndpoints.Clear();quarantineWorld=Guid.Empty;}recoveryQuarantine=false;}
        public static void SetRecoveryQuarantineForValidation(bool active){RequireQA();recoveryQuarantine=active;}
        readonly World world;
        readonly TileEntity tile;
        readonly RegionFileManager manager;
        readonly CargoEndpointMarker identity;
        readonly int thread;
        readonly bool startupRecovery;
        Guid held;
        CargoNativeQueuedSave save;
        bool saveRequested;
        public static bool IsFenced(TileEntity tile)
        {
            if(tile==null)return false;
            lock(fences)
            {
                if(fences.ContainsKey(tile))return true;
                if(quarantineEndpoints.Count==0)return false;
                if(CargoNativeQueuedSaves.IsVerificationClone(tile))return false;
                try{var marker=CargoNativeMarkers.Read(tile,quarantineWorld);return marker!=null&&quarantineEndpoints.Contains(marker.EndpointId);}
                catch{return true;} // Corrupt endpoint metadata cannot authorize a write during recovery.
            }
        }
        static void RequireQA()
        {
            if(!Environment.GetCommandLineArgs().Contains("-yfCargoDroneNativeQA")||GamePrefs.GetString(EnumGamePrefs.GameName)!="CargoDroneQA_Isolated")throw new InvalidOperationException("Native inventory adapter is isolated-QA only");
        }
        public static void InstallForValidation(Harmony harmony)
        {
            RequireQA();Install(harmony);
        }
        public static void Install(Harmony harmony)
        {
            if(installed)return;
            if(typeof(Chunk).Module.ModuleVersionId!=new Guid("229796d0-95ca-4662-b426-1a6f1f1596ed"))throw new NotSupportedException("Unverified native inventory build");
            CargoNativeQueuedSaves.InstallForValidation(harmony);
            foreach(string name in new[]{"UpdateTick","HandleUpdate","handleUpdateForOutputType"})
            foreach(var method in typeof(TileEntityCollector).GetMethods(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic).Where(m=>m.Name==name))
                harmony.Patch(method,prefix:new HarmonyMethod(typeof(CargoNativeValidationEndpoint),nameof(BeforeProduction)));
            harmony.Patch(AccessTools.Method(typeof(TileEntity),"CanLockOnServer"),prefix:new HarmonyMethod(typeof(CargoNativeValidationEndpoint),nameof(BeforeLock)));
            CargoNativeWriteGuards.Install(harmony);
            installed=true;
        }
        public static bool BeforeProduction(TileEntityCollector __instance){return !recoveryQuarantine&&!IsFenced(__instance);}
        public static bool BeforeLock(TileEntity __instance,ref bool __result)
        {if(!IsFenced(__instance))return true;__result=false;return false;}
        public CargoNativeValidationEndpoint(World world,TileEntity tile,Guid worldId,RegionFileManager manager,bool startupRecovery=false)
        {
            if(!installed||!CargoNativeAccessSessions.Installed||world==null||tile==null||manager==null)throw new ArgumentException("Invalid native endpoint or missing access protocol");
            if(startupRecovery&&!CargoRuntime.Recovering)throw new InvalidOperationException("Startup recovery authorization required");
            this.startupRecovery=startupRecovery;this.world=world;this.tile=tile;this.manager=manager;thread=Thread.CurrentThread.ManagedThreadId;
            identity=CargoNativeMarkers.Read(tile,worldId);if(identity==null)throw new InvalidOperationException("Endpoint has no placement identity");Validate();
        }
        void Validate(bool requireReady=true)
        {
            if(Thread.CurrentThread.ManagedThreadId!=thread||world.IsRemote()||GameManager.Instance?.World!=world||tile.IsRemoving||world.GetTileEntity(tile.ToWorldPos())!=tile)throw new InvalidOperationException("Native endpoint context changed");
            var chunk=tile.GetChunk();if(chunk==null||chunk.NeedsDecoration)throw new InvalidOperationException("Endpoint chunk unavailable");
            if(requireReady&&chunk.IsLocked)throw new CargoEndpointUnavailableException();
            var marker=CargoNativeMarkers.Read(tile,identity.WorldId);
            if(marker==null||marker.EndpointId!=identity.EndpointId||marker.Incarnation!=identity.Incarnation||marker.Owner!=identity.Owner)throw new InvalidOperationException("Endpoint identity changed");
        }
        CargoEndpointMarker Marker(){Validate();return CargoNativeMarkers.Read(tile,identity.WorldId);}
        public Guid LastTransaction{get{return Marker().LastTransaction;}}
        public CargoInventory Snapshot()
        {
            lock(ChunkTransferLock.For(tile.GetChunk()))
            {var marker=Marker();return CargoNativeMarkers.Snapshot(tile,marker,held==Guid.Empty&&!startupRecovery);}
        }
        public bool AcquireFence(Guid transaction,long expectedRevision)
        {
            Validate();if(transaction==Guid.Empty)throw new ArgumentException("Empty transaction");
            lock(ChunkTransferLock.For(tile.GetChunk()))lock(fences)
            {
                if(held!=Guid.Empty||fences.ContainsKey(tile)||!startupRecovery&&IsFenced(tile))return false;
                if(!CargoNativeAccessSessions.ReadyForCargo(tile))return false;
                // Native storage Write lazily creates an all-unlocked array.
                // Normalize that metadata before fencing, so serialization need
                // not perform a guarded setter while saving the transaction.
                var storage=(tile as TileEntityComposite)?.GetFeature<TEFeatureStorage>();
                if(storage!=null&&storage.HasSlotLocksSupport&&storage.SlotLocks==null)
                {if(startupRecovery)CargoNativeWriteGuards.NormalizeRecoveryLocks(storage);else storage.SlotLocks=new PackedBoolArray(storage.items.Length);}
                var snapshot=Snapshot();if(snapshot.Busy||snapshot.Revision!=expectedRevision)return false;
                fences.Add(tile,transaction);held=transaction;save=null;saveRequested=false;return true;
            }
        }
        void Own(Guid transaction)
        {Validate(false);lock(fences){Guid owner;if(held!=transaction||transaction==Guid.Empty||!fences.TryGetValue(tile,out owner)||owner!=transaction)throw new InvalidOperationException("Missing native transaction fence");}}
        public void Apply(CargoPlan plan,Guid transaction)
        {
            Own(transaction);CargoPlanIntegrity.Validate(plan);
            lock(ChunkTransferLock.For(tile.GetChunk()))
            {
                if(!plan.Matches(Snapshot()))throw new InvalidOperationException("Native before-image changed");
                var marker=Marker();var replacement=plan.EndpointAfter.Select(CargoNativeItems.Decode).ToArray();
                var after=new CargoEndpointMarker(marker.WorldId,marker.EndpointId,marker.Incarnation,marker.Owner,marker.BlockName,checked(marker.Revision+1),transaction);
                CargoNativeMarkers.Encode(after); // Validate before mutating inventory.
                CargoNativeWriteGuards.Replace(tile,replacement);
                CargoNativeMarkers.Write(tile,after);tile.SetChunkModified();
                CargoNativeWorld.Current?.Diagnostics.Write("inventory-applied",transaction,"endpoint="+identity.EndpointId+" moved="+plan.Moved+" revision="+after.Revision,0);
            }
        }
        public void RequestDurableSave(Guid transaction)
        {
            Own(transaction);if(saveRequested)throw new InvalidOperationException("Save already requested");
            saveRequested=true;
            CargoNativeWorld.Current?.Diagnostics.Write("save-request",transaction,"endpoint="+identity.EndpointId+" at="+tile.ToWorldPos(),0);
        }
        void TryCapture(Guid transaction)
        {
            // A native worker may lock a just-loaded chunk between acquiring the
            // inventory fence and requesting a save. Retain the fence and retry
            // on a later main-thread tick; never interpret that wait as a receipt.
            if(save!=null||tile.GetChunk().IsLocked)return;
            var marker=Marker();if(marker.LastTransaction!=transaction)throw new InvalidOperationException("Missing applied transaction marker");
            save=CargoNativeQueuedSaves.EnqueueForValidation(manager,tile,marker,Snapshot().Items);
        }
        public CargoSaveResult PollDurableSave(Guid transaction)
        {
            Own(transaction);if(!saveRequested)return CargoSaveResult.Uncertain;
            if(tile.GetChunk().IsLocked){CargoNativeWorld.Current?.Diagnostics.Write("save-wait",transaction,"endpoint="+identity.EndpointId+" reason=chunk-locked");return CargoSaveResult.Pending;}
            TryCapture(transaction);
            if(save!=null&&save.Status==CargoSaveResult.Uncertain)throw new InvalidOperationException("Native save confirmation failed: "+save.Failure);
            var result=save==null?CargoSaveResult.Pending:save.Status;
            CargoNativeWorld.Current?.Diagnostics.Write("save-status",transaction,"endpoint="+identity.EndpointId+" result="+result,10,result.ToString());
            return result;
        }
        public void ReleaseFence(Guid transaction)
        {
            Own(transaction);
            if(Marker().LastTransaction==transaction&&(save==null||save.Status!=CargoSaveResult.Durable))throw new InvalidOperationException("Cannot release applied inventory before durable save");
            lock(fences){fences.Remove(tile);held=Guid.Empty;}
        }
    }
}
