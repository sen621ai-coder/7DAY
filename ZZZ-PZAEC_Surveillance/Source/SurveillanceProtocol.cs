using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PZAEC.Surveillance
{
    public static class SurveillanceWire
    {
        public static void Text(PooledBinaryWriter w,string value,int max=96)
        {var b=Encoding.UTF8.GetBytes(value??"");if(b.Length>max)throw new InvalidDataException("Surveillance text too long");w.Write((byte)b.Length);w.Write(b);}
        public static string Text(PooledBinaryReader r,int max=96)
        {int n=r.ReadByte();if(n>max)throw new InvalidDataException("Surveillance text too long");var b=r.ReadBytes(n);if(b.Length!=n)throw new EndOfStreamException();return new UTF8Encoding(false,true).GetString(b);}
        public static void Position(PooledBinaryWriter w,Vector3i p){w.Write(p.x);w.Write(p.y);w.Write(p.z);}
        public static Vector3i Position(PooledBinaryReader r)=>new Vector3i(r.ReadInt32(),r.ReadInt32(),r.ReadInt32());
        public static Guid Guid(PooledBinaryReader r){var b=r.ReadBytes(16);if(b.Length!=16)throw new EndOfStreamException();return new Guid(b);}
    }

    public static class SurveillanceClient
    {
        static readonly Dictionary<Vector3i,SurveillanceDevice> devices=new Dictionary<Vector3i,SurveillanceDevice>();
        static World world;
        static long revision=-1;
        public static IEnumerable<SurveillanceDevice> Devices
        {
            get
            {
                var current=GameManager.Instance?.World;
                if(current!=null&&!current.IsRemote()&&SurveillanceState.World==current)return SurveillanceState.Devices;
                return devices.Values;
            }
        }
        public static SurveillanceDevice At(Vector3i p)
        {
            var current=GameManager.Instance?.World;if(current!=null&&!current.IsRemote()&&SurveillanceState.World==current)return SurveillanceState.At(p);
            SurveillanceDevice d;return devices.TryGetValue(p,out d)?d:null;
        }
        public static SurveillanceDevice ById(Guid id)=>Devices.FirstOrDefault(d=>d.Id==id);
        public static void Receive(SurveillanceSnapshot snapshot,World current)
        {
            if(current==null||snapshot==null)return;
            if(world!=current){Clear();world=current;}
            if(snapshot.Revision<=revision)return;revision=snapshot.Revision;devices.Clear();
            foreach(var d in snapshot.Devices)devices[d.Position]=d;
        }
        public static void Clear(){devices.Clear();world=null;revision=-1;}
    }

    public sealed class NetPackagePZSurveillanceSnapshot : NetPackage
    {
        SurveillanceSnapshot snapshot=new SurveillanceSnapshot();
        public NetPackagePZSurveillanceSnapshot Setup(SurveillanceSnapshot value){snapshot=value??new SurveillanceSnapshot();return this;}
        public override NetPackageDirection PackageDirection=>NetPackageDirection.ToClient;
        public override int GetLength()=>32+snapshot.Devices.Length*160;
        public override void write(PooledBinaryWriter w)
        {
            base.write(w);w.Write(snapshot.Revision);w.Write((ushort)snapshot.Devices.Length);
            foreach(var d in snapshot.Devices)
            {
                w.Write(d.Id.ToByteArray());w.Write((byte)d.Kind);SurveillanceWire.Position(w,d.Position);SurveillanceWire.Text(w,d.Label);
                w.Write(d.Loaded);w.Write(d.Powered);w.Write((byte)d.Selected);w.Write(d.Cycle);
                w.Write(d.Revision);
                for(int i=0;i<4;i++)w.Write(d.Channels[i].ToByteArray());
            }
        }
        public override void read(PooledBinaryReader r)
        {
            snapshot=new SurveillanceSnapshot{Revision=r.ReadInt64()};int count=r.ReadUInt16();
            if(snapshot.Revision<0||count>256)throw new InvalidDataException("Invalid surveillance snapshot");
            snapshot.Devices=new SurveillanceDevice[count];var ids=new HashSet<Guid>();
            for(int i=0;i<count;i++)
            {
                var d=new SurveillanceDevice{Id=SurveillanceWire.Guid(r),Kind=(SurveillanceDeviceKind)r.ReadByte(),Position=SurveillanceWire.Position(r),Label=SurveillanceWire.Text(r),Loaded=r.ReadBoolean(),Powered=r.ReadBoolean(),Selected=r.ReadByte(),Cycle=r.ReadBoolean(),Revision=r.ReadInt64()};
                if(d.Id==Guid.Empty||!ids.Add(d.Id)||!Enum.IsDefined(typeof(SurveillanceDeviceKind),d.Kind)||d.Selected>3||d.Revision<0)throw new InvalidDataException("Invalid surveillance device");
                for(int c=0;c<4;c++)d.Channels[c]=SurveillanceWire.Guid(r);snapshot.Devices[i]=d;
            }
        }
        public override void ProcessPackage(World world,GameManager callbacks){SurveillanceClient.Receive(snapshot,world);}
    }

    public sealed class NetPackagePZSurveillanceConfigure : NetPackage
    {
        public Vector3i Screen;
        public int ChannelIndex;
        public Guid Camera;
        public bool ModeOnly;
        public bool Cycle;
        public long ExpectedRevision;
        public NetPackagePZSurveillanceConfigure Setup(Vector3i screen,int channel,Guid camera,bool modeOnly,bool cycle,long expectedRevision)
        {Screen=screen;ChannelIndex=channel;Camera=camera;ModeOnly=modeOnly;Cycle=cycle;ExpectedRevision=expectedRevision;return this;}
        public override NetPackageDirection PackageDirection=>NetPackageDirection.ToServer;
        public override int GetLength()=>48;
        public override void write(PooledBinaryWriter w){base.write(w);SurveillanceWire.Position(w,Screen);w.Write((byte)ChannelIndex);w.Write(Camera.ToByteArray());w.Write(ModeOnly);w.Write(Cycle);w.Write(ExpectedRevision);}
        public override void read(PooledBinaryReader r){Screen=SurveillanceWire.Position(r);ChannelIndex=r.ReadByte();Camera=SurveillanceWire.Guid(r);ModeOnly=r.ReadBoolean();Cycle=r.ReadBoolean();ExpectedRevision=r.ReadInt64();if(ChannelIndex>3||ExpectedRevision<0)throw new InvalidDataException("Invalid surveillance configuration request");}
        public override void ProcessPackage(World world,GameManager callbacks)
        {
            if(world==null||world.IsRemote()||Sender==null||!Sender.loginDone||!Sender.bAttachedToEntity)return;
            var player=world.GetEntity(Sender.entityId) as EntityPlayer;string message;
            bool accepted;
            if(!SurveillanceState.AllowRequest(Sender.entityId)){accepted=false;message="操作过快，请稍后重试";}
            else if(ModeOnly)accepted=SurveillanceState.SetMode(Screen,ChannelIndex,Cycle,ExpectedRevision,player,out message);
            else accepted=SurveillanceState.Configure(Screen,ChannelIndex,Camera,ExpectedRevision,player,out message);
            Sender.SendPackage(NetPackageManager.GetPackage<NetPackagePZSurveillanceResult>().Setup(Screen,accepted,message));
        }
    }

    public sealed class NetPackagePZSurveillanceResult : NetPackage
    {
        Vector3i screen;
        bool accepted;
        string message="";
        public NetPackagePZSurveillanceResult Setup(Vector3i value,bool ok,string text){screen=value;accepted=ok;message=text??"";return this;}
        public override NetPackageDirection PackageDirection=>NetPackageDirection.ToClient;
        public override int GetLength()=>120;
        public override void write(PooledBinaryWriter w){base.write(w);SurveillanceWire.Position(w,screen);w.Write(accepted);SurveillanceWire.Text(w,message);}
        public override void read(PooledBinaryReader r){screen=SurveillanceWire.Position(r);accepted=r.ReadBoolean();message=SurveillanceWire.Text(r);}
        public override void ProcessPackage(World world,GameManager callbacks){SurveillanceMenu.Result(screen,accepted,message);}
    }
}
