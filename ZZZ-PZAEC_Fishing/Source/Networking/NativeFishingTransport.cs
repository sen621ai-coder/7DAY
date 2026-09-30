using System;
using System.Collections.Generic;
using System.IO;
using PZAEC.Fishing.Contracts;

namespace PZAEC.Fishing.Networking
{
    /// <summary>
    /// A explicitly binds one instance per world, invokes Tick and disconnect events, then Dispose.
    /// This module installs no hooks or ModApi and does not start gameplay by being loaded.
    /// </summary>
    public sealed class NativeFishingTransport : IFishingTransport, IDisposable
    {
        internal static NativeFishingTransport Active;
        readonly Dictionary<ClientInfo,string> connections=new Dictionary<ClientInfo,string>();
        readonly Dictionary<string,ClientInfo> clients=new Dictionary<string,ClientInfo>(StringComparer.Ordinal);
        readonly Func<ClientInfo,string> persistentIdentity;
        readonly World world;
        FishingNetwork network;
        public NativeFishingTransport(World world,Func<ClientInfo,string> persistentIdentity)
        {this.world=world??throw new ArgumentNullException(nameof(world));this.persistentIdentity=persistentIdentity??throw new ArgumentNullException(nameof(persistentIdentity));}
        public void Bind(FishingNetwork value)
        {
            if(Active!=null && Active!=this)throw new InvalidOperationException("Fishing transport already bound");
            network=value??throw new ArgumentNullException(nameof(value));Active=this;
        }
        // Call on authenticated player attachment. identity callback must agree with A's save ID.
        public bool Attach(ClientInfo client)
        {
            if(network==null || world.IsRemote() || client==null || !client.loginDone || !client.bAttachedToEntity)return false;
            string key;if(connections.TryGetValue(client,out key))return true;
            string player=persistentIdentity(client);
            if(string.IsNullOrWhiteSpace(player))return false;
            key=Guid.NewGuid().ToString("N");
            if(!network.RegisterPeer(key,player,client.entityId))return false;
            connections.Add(client,key);clients.Add(key,client);return true;
        }
        public void Detach(ClientInfo client)
        {
            string key;if(client==null || !connections.TryGetValue(client,out key))return;
            connections.Remove(client);clients.Remove(key);network?.DisconnectPeer(key);
        }
        internal void ReceiveClient(World actualWorld,ClientInfo sender,byte[] bytes)
        {
            if(actualWorld!=world || world.IsRemote() || sender==null || !sender.loginDone || !sender.bAttachedToEntity)return;
            string key;
            // Unknown senders cannot auto-register by sending packets. A attaches on login.
            if(connections.TryGetValue(sender,out key))network?.ReceiveFromPeer(key,bytes);
        }
        internal void ReceiveServer(World actualWorld,byte[] bytes)
        {if(actualWorld==world && world.IsRemote())network?.ReceiveFromServer(bytes);}
        public void SendToServer(byte[] payload)
        {
            if(!world.IsRemote())throw new InvalidOperationException("Use explicit loopback for host player");
            ConnectionManager.Instance.SendToServer(NetPackagePzaecFishingRequest.Create(payload));
        }
        public void SendToPeer(string connectionId,byte[] payload)
        {
            ClientInfo client;
            if(!world.IsRemote() && clients.TryGetValue(connectionId,out client) && client.loginDone && client.bAttachedToEntity)
                client.SendPackage(NetPackagePzaecFishingResponse.Create(payload));
        }
        public void Dispose()
        {
            try{network?.Stop();}
            finally{connections.Clear();clients.Clear();network=null;if(Active==this)Active=null;}
        }
    }

    // Separate directions are enforced again in ProcessPackage, independent of core codec checks.
    public sealed class NetPackagePzaecFishingRequest : NetPackage
    {
        byte[] bytes=new byte[0];
        public override bool ReliableDelivery {get {return true;}}
        public override bool AllowedBeforeAuth {get {return false;}}
        public override NetPackageDirection PackageDirection {get {return NetPackageDirection.ToServer;}}
        public static NetPackagePzaecFishingRequest Create(byte[] value)
        {var p=NetPackageManager.GetPackage<NetPackagePzaecFishingRequest>();p.bytes=NativeFishingPacket.Copy(value);return p;}
        public override int GetLength(){return bytes.Length+4;}
        public override void write(PooledBinaryWriter writer){base.write(writer);NativeFishingPacket.Write(writer,bytes);}
        public override void read(PooledBinaryReader reader){bytes=NativeFishingPacket.Read(reader);}
        public override void ProcessPackage(World world,GameManager callbacks)
        {if(world!=null && !world.IsRemote())NativeFishingTransport.Active?.ReceiveClient(world,Sender,bytes);}
    }
    public sealed class NetPackagePzaecFishingResponse : NetPackage
    {
        byte[] bytes=new byte[0];
        public override bool ReliableDelivery {get {return true;}}
        public override bool AllowedBeforeAuth {get {return false;}}
        public override NetPackageDirection PackageDirection {get {return NetPackageDirection.ToClient;}}
        public static NetPackagePzaecFishingResponse Create(byte[] value)
        {var p=NetPackageManager.GetPackage<NetPackagePzaecFishingResponse>();p.bytes=NativeFishingPacket.Copy(value);return p;}
        public override int GetLength(){return bytes.Length+4;}
        public override void write(PooledBinaryWriter writer){base.write(writer);NativeFishingPacket.Write(writer,bytes);}
        public override void read(PooledBinaryReader reader){bytes=NativeFishingPacket.Read(reader);}
        public override void ProcessPackage(World world,GameManager callbacks)
        {if(world!=null && world.IsRemote())NativeFishingTransport.Active?.ReceiveServer(world,bytes);}
    }
    static class NativeFishingPacket
    {
        internal static byte[] Copy(byte[] value)
        {if(value==null || value.Length>FishingWire.MaxBytes)throw new InvalidDataException("Fishing payload size");return (byte[])value.Clone();}
        internal static void Write(PooledBinaryWriter w,byte[] value){var writer=(BinaryWriter)w;writer.Write((ushort)value.Length);writer.Write(value,0,value.Length);}
        internal static byte[] Read(PooledBinaryReader r)
        {int n=r.ReadUInt16();if(n>FishingWire.MaxBytes)throw new InvalidDataException("Fishing payload size");var b=r.ReadBytes(n);if(b.Length!=n)throw new EndOfStreamException();return b;}
    }
}
