using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;

namespace YFAutomation.CargoDrones
{
    // Native loot-session bridge, installed before world/player access on both
    // client and server. Actual multi-client close ordering still needs live QA.
    public static class CargoNativeAccessSessions
    {
        sealed class Registration{public TileEntity Tile;public Guid Endpoint,Incarnation;public CargoAccessSession Session;public ClientInfo Client;}
        sealed class ClientSession{public Guid Token;public long Sequence;public bool Closed;}
        static readonly Dictionary<Vector3i,Registration> registered=new Dictionary<Vector3i,Registration>();
        static readonly Dictionary<int,ClientInfo> capable=new Dictionary<int,ClientInfo>();
        static readonly Dictionary<Vector3i,ClientSession> clientSessions=new Dictionary<Vector3i,ClientSession>();
        static World currentWorld;
        static bool installed;
        public static bool Installed=>installed;
        public static void ResetWorld(){currentWorld=null;registered.Clear();capable.Clear();clientSessions.Clear();nextHello=0;}
        static float nextHello;
        [ThreadStatic] static NetPackageTileEntity authorizedRead;
        public static void InstallForValidation(Harmony harmony)
        {
            if(!Environment.GetCommandLineArgs().Contains("-yfCargoDroneNativeQA")||GamePrefs.GetString(EnumGamePrefs.GameName)!="CargoDroneQA_Isolated")throw new InvalidOperationException("Native access protocol is isolated-QA only");
            Install(harmony);
        }
        public static void Install(Harmony harmony)
        {
            if(installed)return;
            if(GameManager.Instance?.World!=null&&GameManager.Instance.World.Players.Count!=0)throw new InvalidOperationException("Install session tracking before any client accesses native inventories");
            if(typeof(TileEntity).Module.ModuleVersionId!=new Guid("229796d0-95ca-4662-b426-1a6f1f1596ed"))throw new NotSupportedException("Unverified native access protocol build");
            harmony.Patch(AccessTools.Method(typeof(ConnectionManager),"SendToServer"),prefix:new HarmonyMethod(typeof(CargoNativeAccessSessions),nameof(BeforeSend)));
            harmony.Patch(AccessTools.Method(typeof(NetPackageTileEntity),"ProcessPackage"),prefix:new HarmonyMethod(typeof(CargoNativeAccessSessions),nameof(BeforeRead)));
            harmony.Patch(AccessTools.Method(typeof(LockManager),"UnlockRequestLocal"),postfix:new HarmonyMethod(typeof(CargoNativeAccessSessions),nameof(AfterLocalClose)));
            harmony.Patch(AccessTools.Method(typeof(TileEntity),"CanLockOnServer"),prefix:new HarmonyMethod(typeof(CargoNativeAccessSessions),nameof(CanOpenTile)));
            harmony.Patch(AccessTools.Method(typeof(TEFeatureAbs),"CanLockOnServer"),prefix:new HarmonyMethod(typeof(CargoNativeAccessSessions),nameof(CanOpenFeature)));
            harmony.Patch(AccessTools.Method(typeof(TileEntity),"OnLockedServer"),postfix:new HarmonyMethod(typeof(CargoNativeAccessSessions),nameof(OpenedTile)));
            harmony.Patch(AccessTools.Method(typeof(TEFeatureStorage),"OnLockedServer"),postfix:new HarmonyMethod(typeof(CargoNativeAccessSessions),nameof(OpenedStorage)));
            harmony.Patch(AccessTools.Method(typeof(TEFeatureAbs),"OnLockedServer"),postfix:new HarmonyMethod(typeof(CargoNativeAccessSessions),nameof(OpenedFeature)));
            ModEvents.GameUpdate.RegisterHandler(UpdateClient);
            installed=true;
        }
        static void UpdateClient(ref ModEvents.SGameUpdateData data)
        {if(!(ConnectionManager.Instance?.IsClient??false)||UnityEngine.Time.realtimeSinceStartup<nextHello)return;nextHello=UnityEngine.Time.realtimeSinceStartup+2;AdvertiseClient();}
        static void WorldContext(World world)
        {if(world==currentWorld)return;currentWorld=world;registered.Clear();capable.Clear();clientSessions.Clear();}
        public static void AdvertiseClient()
        {
            if(!installed||GameManager.Instance?.World==null||!(ConnectionManager.Instance?.IsClient??false))return;
            WorldContext(GameManager.Instance.World);ConnectionManager.Instance.SendToServer(NetPackageManager.GetPackage<NetPackageCargoAccessHello>());
        }
        internal static void Hello(ClientInfo sender)
        {if(!installed||sender==null||!sender.loginDone||!sender.bAttachedToEntity||!(ConnectionManager.Instance?.IsServer??false))return;WorldContext(GameManager.Instance.World);capable[sender.entityId]=sender;}
        public static void Register(TileEntity tile,Guid worldId)
        {
            if(!installed||tile==null||GameManager.Instance?.World==null||GameManager.Instance.World.IsRemote())throw new InvalidOperationException("Server session tracking required");
            WorldContext(GameManager.Instance.World);
            var marker=CargoNativeMarkers.Read(tile,worldId);if(marker==null)throw new InvalidOperationException("Endpoint identity required");if(NativeLocked(tile))throw new CargoNativeAccessPendingException();
            Registration existing;
            if(registered.TryGetValue(tile.ToWorldPos(),out existing))
            {
                if(existing.Session!=null&&!existing.Session.Drained)throw new CargoNativeAccessPendingException();
                if(existing.Tile!=tile)
                {
                    // A native chunk reload creates new TileEntity objects. Only
                    // the same persisted instance may inherit a fully drained
                    // registration; replacement devices never inherit access.
                    if(GameManager.Instance.World.GetTileEntity(tile.ToWorldPos())!=tile||NativeLocked(existing.Tile)||CargoNativeValidationEndpoint.IsFenced(existing.Tile))throw new CargoNativeAccessPendingException();
                    if(existing.Endpoint!=marker.EndpointId||existing.Incarnation!=marker.Incarnation)
                    {
                        // A newly placed instance can replace a fully drained
                        // registration, but never inherits its connection ticket.
                        existing.Endpoint=marker.EndpointId;existing.Incarnation=marker.Incarnation;existing.Session=null;existing.Client=null;
                    }
                    existing.Tile=tile;
                }
                // Access is tracked before binding, so a previously used vanilla
                // crate can acquire its persistent identity only after its tail drains.
                if(existing.Incarnation!=marker.Incarnation){existing.Endpoint=marker.EndpointId;existing.Incarnation=marker.Incarnation;existing.Session=null;}
                return;
            }
            registered.Add(tile.ToWorldPos(),new Registration{Tile=tile,Endpoint=marker.EndpointId,Incarnation=marker.Incarnation});
        }
        static Registration TrackNativeAccess(TileEntity tile)
        {
            var storage=(tile as TileEntityComposite)?.GetFeature<TEFeatureStorage>();
            if(tile==null||!(tile is TileEntityCollector)&&!(storage?.bPlayerStorage??false))return null;
            WorldContext(GameManager.Instance.World);Registration r;
            if(registered.TryGetValue(tile.ToWorldPos(),out r)&&r.Tile==tile)return r;
            if(r!=null)r.Session?.Disconnect();
            r=new Registration{Tile=tile,Endpoint=Guid.NewGuid(),Incarnation=Guid.NewGuid()};registered[tile.ToWorldPos()]=r;return r;
        }
        static bool NativeLocked(TileEntity tile)
        {
            var storage=(tile as TileEntityComposite)?.GetFeature<TEFeatureStorage>();
            return tile.bUserAccessing||LockManager.Instance.IsLockedServer(storage!=null?(ILockTarget)storage:tile,0);
        }
        public static bool ReadyForCargo(TileEntity tile)
        {
            Registration r;if(!installed||!registered.TryGetValue(tile.ToWorldPos(),out r))return false;
            return r.Tile==tile&&!tile.IsRemoving&&!NativeLocked(tile)&&(r.Session==null||r.Session.CanHandOff(false,r.Incarnation));
        }
        static bool CanOpen(TileEntity tile,int actor)
        {
            var r=TrackNativeAccess(tile);if(r==null)return true;
            if(r.Tile!=tile||CargoNativeValidationEndpoint.IsFenced(tile)||r.Session!=null&&!r.Session.Drained)return false;
            if(GameManager.Instance.World.GetEntity(actor) is EntityPlayerLocal)return true;
            ClientInfo client;return capable.TryGetValue(actor,out client)&&client.loginDone&&client.bAttachedToEntity;
        }
        public static bool CanOpenTile(TileEntity __instance,int __0,ref bool __result){if(CanOpen(__instance,__0))return true;__result=false;return false;}
        public static bool CanOpenFeature(TEFeatureAbs __instance,int __0,ref bool __result){if(CanOpen(__instance.Parent,__0))return true;__result=false;return false;}
        public static void OpenedTile(TileEntity __instance,bool __0,int __1){if(__0)Opened(__instance,__1);}
        public static void OpenedStorage(TEFeatureStorage __instance,bool __0,int __1){if(__0)Opened(__instance.Parent,__1);}
        public static void OpenedFeature(TEFeatureAbs __instance,bool __0,int __1){if(__0)Opened(__instance.Parent,__1);}
        static void Opened(TileEntity tile,int actor)
        {
            Registration r;if(tile==null||!registered.TryGetValue(tile.ToWorldPos(),out r)||GameManager.Instance.World.GetEntity(actor) is EntityPlayerLocal)return;
            ClientInfo client;if(!capable.TryGetValue(actor,out client))throw new InvalidOperationException("Native access granted without cargo protocol handshake");
            if(r.Session!=null&&!r.Session.Drained)
            {if(ReferenceEquals(r.Client,client))return;throw new InvalidOperationException("Prior native access session still active");}
            r.Client=client;Guid connection=Guid.NewGuid(),token=Guid.NewGuid();
            r.Session=new CargoAccessSession(connection,token,r.Endpoint,r.Incarnation,bytes=>ApplyNative(r,bytes));
            var ticket=NetPackageManager.GetPackage<NetPackageCargoAccessStart>();ticket.At=tile.ToWorldPos();ticket.Token=token;client.SendPackage(ticket);
        }
        internal static void Start(Vector3i at,Guid token)
        {if(!installed||GameManager.Instance?.World==null||!GameManager.Instance.World.IsRemote()||token==Guid.Empty)return;WorldContext(GameManager.Instance.World);clientSessions[at]=new ClientSession{Token=token};}
        public static bool BeforeSend(ConnectionManager __instance,NetPackage __0,bool __1)
        {
            var native=__0 as NetPackageTileEntity;ClientSession session;
            if(native==null||!clientSessions.TryGetValue(native.teWorldPos,out session))return true;
            if(session.Closed)throw new InvalidOperationException("Native inventory write after cargo access close");
            byte[] bytes;using(var memory=new MemoryStream())using(var writer=MemoryPools.poolBinaryWriter.AllocSync(false))
            {writer.SetBaseStream(memory);native.write(writer);writer.Flush();bytes=memory.ToArray();}
            if(bytes.Length> CargoRules.MaxRecordBytes)throw new InvalidDataException("Native inventory envelope exceeds limit");
            long next=checked(session.Sequence+1);
            var wrapped=NetPackageManager.GetPackage<NetPackageCargoAccessData>();wrapped.At=native.teWorldPos;wrapped.Token=session.Token;wrapped.Sequence=next;wrapped.Payload=bytes;
            __instance.SendToServer(wrapped,__1);session.Sequence=next;NetPackageManager.FreePackage(native);return false;
        }
        public static void AfterLocalClose()
        {
            if(!(ConnectionManager.Instance?.IsClient??false))return;
            foreach(var pair in clientSessions.ToArray())if(!pair.Value.Closed)
            {var close=NetPackageManager.GetPackage<NetPackageCargoAccessClose>();close.At=pair.Key;close.Token=pair.Value.Token;close.Sequence=pair.Value.Sequence;ConnectionManager.Instance.SendToServer(close);pair.Value.Closed=true;}
        }
        public static bool BeforeRead(NetPackageTileEntity __instance)
        {
            if(ReferenceEquals(authorizedRead,__instance)){authorizedRead=null;return true;}
            Registration r;if(!(ConnectionManager.Instance?.IsServer??false)||!registered.TryGetValue(__instance.teWorldPos,out r))return true;
            // Registered native access is granted only to clients that negotiated
            // envelopes. An unwrapped update is never silently treated as current.
            if(ReferenceEquals(r.Client,__instance.Sender))r.Session?.Disconnect();return false;
        }
        internal static void Receive(ClientInfo sender,Vector3i at,Guid token,long sequence,byte[] payload,bool close)
        {
            Registration r;if(!installed||sender==null||!sender.loginDone||!(ConnectionManager.Instance?.IsServer??false)||!registered.TryGetValue(at,out r)||!ReferenceEquals(r.Client,sender)||r.Session==null)return;
            if(r.Tile.IsRemoving||GameManager.Instance.World.GetTileEntity(at)!=r.Tile){r.Session.Disconnect();return;}
            if(close)r.Session.Close(r.Session.Connection,token,sequence);else r.Session.Receive(r.Session.Connection,token,sequence,payload);
        }
        static void ApplyNative(Registration registration,byte[] bytes)
        {
            if(CargoNativeValidationEndpoint.IsFenced(registration.Tile))throw new InvalidOperationException("Player update reached an inventory transaction fence");
            var native=NetPackageManager.GetPackage<NetPackageTileEntity>();
            try
            {
                using(var memory=new MemoryStream(bytes,false))using(var reader=MemoryPools.poolBinaryReader.AllocSync(false))
                {reader.SetBaseStream(memory);if(reader.ReadUInt16()!=native.PackageId)throw new InvalidDataException("Envelope is not a native tile inventory packet");native.read(reader);if(memory.Position!=memory.Length)throw new InvalidDataException("Trailing native inventory envelope data");}
                if(native.teWorldPos!=registration.Tile.ToWorldPos()||native.teBlockId!=registration.Tile.blockValue.type)throw new InvalidDataException("Inventory envelope targets another endpoint");
                native.Sender=registration.Client;authorizedRead=native;native.ProcessPackage(GameManager.Instance.World,GameManager.Instance);
            }
            finally{authorizedRead=null;NetPackageManager.FreePackage(native);}
        }
    }
    public sealed class CargoNativeAccessPendingException : InvalidOperationException
    {public CargoNativeAccessPendingException():base("Previous native access has not drained") {}}
    public sealed class NetPackageCargoAccessHello : NetPackage
    {
        bool valid;
        public override NetPackageDirection PackageDirection=>NetPackageDirection.ToServer;
        public override int GetLength()=>6;
        public override void write(PooledBinaryWriter w){base.write(w);w.Write(1);}
        public override void read(PooledBinaryReader r){valid=r.ReadInt32()==1;}
        public override void ProcessPackage(World world,GameManager callbacks){if(valid)CargoNativeAccessSessions.Hello(Sender);}
    }
    public sealed class NetPackageCargoAccessStart : NetPackage
    {
        public Vector3i At;public Guid Token;
        public override NetPackageDirection PackageDirection=>NetPackageDirection.ToClient;
        public override int GetLength()=>30;
        public override void write(PooledBinaryWriter w){base.write(w);w.Write(At.x);w.Write(At.y);w.Write(At.z);w.Write(Token.ToByteArray());}
        public override void read(PooledBinaryReader r){At=new Vector3i(r.ReadInt32(),r.ReadInt32(),r.ReadInt32());Token=new Guid(r.ReadBytes(16));}
        public override void ProcessPackage(World world,GameManager callbacks){CargoNativeAccessSessions.Start(At,Token);}
    }
    public sealed class NetPackageCargoAccessData : NetPackage
    {
        public Vector3i At;public Guid Token;public long Sequence;public byte[] Payload=new byte[0];
        public override NetPackageDirection PackageDirection=>NetPackageDirection.ToServer;
        public override int GetLength()=>42+Payload.Length;
        public override void write(PooledBinaryWriter w){base.write(w);w.Write(At.x);w.Write(At.y);w.Write(At.z);w.Write(Token.ToByteArray());w.Write(Sequence);w.Write(Payload.Length);w.Write(Payload);}
        public override void read(PooledBinaryReader r){At=new Vector3i(r.ReadInt32(),r.ReadInt32(),r.ReadInt32());Token=new Guid(r.ReadBytes(16));Sequence=r.ReadInt64();int n=r.ReadInt32();if(n<1||n>CargoRules.MaxRecordBytes)throw new InvalidDataException("Invalid access envelope length");Payload=r.ReadBytes(n);if(Payload.Length!=n)throw new EndOfStreamException();}
        public override void ProcessPackage(World world,GameManager callbacks){CargoNativeAccessSessions.Receive(Sender,At,Token,Sequence,Payload,false);}
    }
    public sealed class NetPackageCargoAccessClose : NetPackage
    {
        public Vector3i At;public Guid Token;public long Sequence;
        public override NetPackageDirection PackageDirection=>NetPackageDirection.ToServer;
        public override int GetLength()=>38;
        public override void write(PooledBinaryWriter w){base.write(w);w.Write(At.x);w.Write(At.y);w.Write(At.z);w.Write(Token.ToByteArray());w.Write(Sequence);}
        public override void read(PooledBinaryReader r){At=new Vector3i(r.ReadInt32(),r.ReadInt32(),r.ReadInt32());Token=new Guid(r.ReadBytes(16));Sequence=r.ReadInt64();}
        public override void ProcessPackage(World world,GameManager callbacks){CargoNativeAccessSessions.Receive(Sender,At,Token,Sequence,null,true);}
    }
}
