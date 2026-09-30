using System;
using PZAEC.Fishing.Contracts;

namespace PZAEC.Fishing.Networking
{
    /// <summary>Multiplex the host's local connection with a native remote transport.</summary>
    public sealed class FishingServerTransport : IFishingTransport
    {
        readonly IFishingTransport remote;
        string localConnection;
        Action<byte[]> localReceive;
        public FishingServerTransport(IFishingTransport remote)
        {this.remote=remote??throw new ArgumentNullException(nameof(remote));}
        public void SetLocalReceiver(string connectionId,Action<byte[]> receive)
        {
            if(string.IsNullOrWhiteSpace(connectionId) || receive==null)throw new ArgumentException("Local connection required");
            if(localReceive!=null)throw new InvalidOperationException("Host receiver already attached");
            localConnection=connectionId;localReceive=receive;
        }
        public void ClearLocalReceiver(){localReceive=null;localConnection=null;}
        public void SendToServer(byte[] payload){throw new InvalidOperationException("Authority cannot send client requests");}
        public void SendToPeer(string connectionId,byte[] payload)
        {if(connectionId==localConnection && localReceive!=null)localReceive((byte[])payload.Clone());else remote.SendToPeer(connectionId,payload);}
    }

    public sealed class FishingLoopback : IFishingTransport, IDisposable
    {
        readonly FishingNetwork authority;
        readonly FishingServerTransport serverTransport;
        readonly string connectionId="local-"+Guid.NewGuid().ToString("N");
        FishingNetwork client;
        bool attached;
        public FishingLoopback(FishingNetwork authority,FishingServerTransport serverTransport)
        {this.authority=authority??throw new ArgumentNullException(nameof(authority));this.serverTransport=serverTransport??throw new ArgumentNullException(nameof(serverTransport));}
        public void Attach(FishingNetwork localClient,string authenticatedPlayerId,int entityId)
        {
            if(attached)throw new InvalidOperationException("Loopback already attached");
            client=localClient??throw new ArgumentNullException(nameof(localClient));
            if(!authority.RegisterPeer(connectionId,authenticatedPlayerId,entityId))throw new InvalidOperationException("Host identity registration failed");
            try {serverTransport.SetLocalReceiver(connectionId,p=>client.ReceiveFromServer(p));attached=true;}
            catch {authority.DisconnectPeer(connectionId);client=null;throw;}
        }
        public void SendToServer(byte[] payload)
        {if(!attached)throw new InvalidOperationException("Loopback not attached");authority.ReceiveFromPeer(connectionId,(byte[])payload.Clone());}
        public void SendToPeer(string connection,byte[] payload){throw new InvalidOperationException("Loopback is a client transport");}
        public void Dispose()
        {
            if(!attached)return;
            try {authority.DisconnectPeer(connectionId);client.Stop();}
            finally {serverTransport.ClearLocalReceiver();client=null;attached=false;}
        }
    }
}
