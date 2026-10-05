using System;
using System.Collections.Generic;
using UnityEngine;

namespace PZAEC.Mecha
{
    // Ordinary walking/recovery feet have their own message. Motion remains
    // flags/charge/vertical input; remote peers never apply bearing forces.
    public sealed class GroundWire
    {
        public int Vehicle,Actor,Sequence;public byte Flags;public float Yaw;
        public Vector3 Root;public Vector3[] Feet=new Vector3[2],Normals={Vector3.up,Vector3.up};
        public const int Bytes=77;
        static void Vector(PooledBinaryWriter w,Vector3 v){w.Write(v.x);w.Write(v.y);w.Write(v.z);}
        static Vector3 Vector(PooledBinaryReader r){return new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());}
        public void Write(PooledBinaryWriter w){w.Write(Vehicle);w.Write(Actor);w.Write(Sequence);w.Write(Flags);w.Write(Yaw);Vector(w,Root);for(int i=0;i<2;i++){Vector(w,Feet[i]);Vector(w,Normals[i]);}}
        public void Read(PooledBinaryReader r){Vehicle=r.ReadInt32();Actor=r.ReadInt32();Sequence=r.ReadInt32();Flags=r.ReadByte();Yaw=r.ReadSingle();Root=Vector(r);for(int i=0;i<2;i++){Feet[i]=Vector(r);Normals[i]=Vector(r);}}
        static bool Finite(Vector3 v){return Weapons.Finite(v.x)&&Weapons.Finite(v.y)&&Weapons.Finite(v.z);}
        public bool Valid(){if(Sequence<=0||Flags>31||!Weapons.Finite(Yaw)||!Finite(Root)||((Flags&1)!=0)!=((Flags&6)!=0)||((Flags&8)!=0&&(Flags&1)==0))return false;
            for(int i=0;i<2;i++)if(!Finite(Feet[i])||!Finite(Normals[i])||Mathf.Abs(Normals[i].magnitude-1)>.02f||Normals[i].y<.707f||(Feet[i]-Root).sqrMagnitude>6)return false;return true;}
    }
    public sealed class NetPackagePZAECMechaGroundIntent:NetPackage
    {
        public GroundWire Data=new GroundWire();public override NetPackageDirection PackageDirection{get{return NetPackageDirection.ToServer;}}
        public NetPackagePZAECMechaGroundIntent Setup(GroundWire d){Data=d;return this;}public override int GetLength(){return GroundWire.Bytes;}
        public override void read(PooledBinaryReader r){Data=new GroundWire();Data.Read(r);}public override void write(PooledBinaryWriter w){base.write(w);Data.Write(w);}
        public override void ProcessPackage(World world,GameManager callbacks){if(Weapons.Server&&world!=null&&Sender!=null&&Sender.entityId==Data.Actor)GroundNet.ServerReceive(world,Sender.entityId,Data);}
    }
    public sealed class NetPackagePZAECMechaGroundEvent:NetPackage
    {
        public GroundWire Data=new GroundWire();public override NetPackageDirection PackageDirection{get{return NetPackageDirection.ToClient;}}
        public NetPackagePZAECMechaGroundEvent Setup(GroundWire d){Data=d;return this;}public override int GetLength(){return GroundWire.Bytes;}
        public override void read(PooledBinaryReader r){Data=new GroundWire();Data.Read(r);}public override void write(PooledBinaryWriter w){base.write(w);Data.Write(w);}
        public override void ProcessPackage(World world,GameManager callbacks){if(!Weapons.Server&&world!=null)GroundNet.ClientReceive(world,Data);}
    }
    public static class GroundNet
    {
        sealed class Peer{public int Sent,Actor=-1,Received;public byte SentFlags=255;public float SendAt=-100,Seen=-100;public GroundWire Last;public Vector3[] From=new Vector3[2];}
        static readonly Dictionary<int,Peer> peers=new Dictionary<int,Peer>();
        static Peer Get(EntityVehicle v){Peer p;if(!peers.TryGetValue(v.entityId,out p)){p=new Peer();peers[v.entityId]=p;}return p;}
        static int Actor(EntityVehicle v){var driver=v.GetAttached(0);return driver!=null?driver.entityId:-1;}
        public static bool Fresh(EntityVehicle v){Peer p;return v!=null&&peers.TryGetValue(v.entityId,out p)&&p.Last!=null&&Time.time-p.Seen<.75f&&p.Actor==Actor(v);}
        public static void Publish(GroundSupport.State s)
        {
            if(s==null||s.Vehicle.isEntityRemote||!s.Initialized||Traversal.Active(s.Vehicle))return;var v=s.Vehicle;var p=Get(v);
            byte flags=(byte)((s.Grounded?1:0)|(s.Feet[0].Planted?2:0)|(s.Feet[1].Planted?4:0)|(s.Recovering?8:0)|(s.Cautious?16:0));
            int actor=Actor(v);if(actor!=p.Actor){p.Actor=actor;p.SentFlags=255;}
            if(flags==p.SentFlags&&Time.time-p.SendAt<.1f)return;p.SentFlags=flags;p.SendAt=Time.time;
            var d=new GroundWire{Vehicle=v.entityId,Actor=actor,Sequence=++p.Sent,Flags=flags,Yaw=v.vehicleRB.rotation.eulerAngles.y,Root=v.vehicleRB.position+Origin.position};
            for(int i=0;i<2;i++){d.Feet[i]=s.Feet[i].Position;d.Normals[i]=s.Feet[i].Normal;}
            if(Weapons.Server){Broadcast(d);return;}if(actor>=0&&ConnectionManager.Instance!=null)ConnectionManager.Instance.SendToServer(NetPackageManager.GetPackage<NetPackagePZAECMechaGroundIntent>().Setup(d));
        }
        static void Broadcast(GroundWire d){if(ConnectionManager.Instance!=null)ConnectionManager.Instance.SendPackage(NetPackageManager.GetPackage<NetPackagePZAECMechaGroundEvent>().Setup(d),false,-1,-1,d.Vehicle,null,512);}
        public static bool ServerReceive(World world,int actor,GroundWire d)
        {
            if(!d.Valid()||d.Actor!=actor||actor<0)return false;var v=world.GetEntity(d.Vehicle) as EntityVehicle;
            if(!Weapons.IsMecha(v)||Actor(v)!=actor||v.IsDead()||Traversal.Active(v)||Flight.AirPose(Locomotion.Get(v))||Locomotion.Get(v).HoverOn||Vector3.Distance(v.vehicleRB.position+Origin.position,d.Root)>1.5f)return false;
            var peer=Get(v);if(peer.Actor==actor&&d.Sequence<=peer.Received)return false;var support=GroundSupport.Get(v);if(support==null)return false;var yaw=Quaternion.Euler(0,d.Yaw,0);
            if(!GroundSupport.HullClear(v,support.Shape,d.Root,d.Root,yaw))return false;
            for(int i=0;i<2;i++){
                if(!support.Shape.Reach(d.Root,yaw,i,d.Feet[i],d.Normals[i]))return false;
                if((d.Flags&(2<<i))!=0){GroundSupport.Pad pad;if(!GroundSupport.PadAt(v,d.Feet[i],yaw,.08f,.08f,out pad)||Vector3.Distance(pad.Point,d.Feet[i])>.05f||Vector3.Angle(pad.Normal,d.Normals[i])>5)return false;}
                if(!Traversal.FootPath(v,support.Shape,d.Root,yaw,i,d.Feet[i],d.Feet[i],0,d.Normals[i]))return false;
            }
            Accept(v,peer,d);Broadcast(d);return true;
        }
        public static bool ClientReceive(World world,GroundWire d)
        {
            if(!d.Valid())return false;var v=world.GetEntity(d.Vehicle) as EntityVehicle;if(!Weapons.IsMecha(v)||Actor(v)!=d.Actor||v.IsDead()||Flight.AirPose(Locomotion.Get(v))||Locomotion.Get(v).HoverOn)return false;
            var p=Get(v);if(p.Actor==d.Actor&&d.Sequence<=p.Received)return false;
            if(!v.isEntityRemote)return true;Accept(v,p,d);return true;
        }
        static void Accept(EntityVehicle v,Peer p,GroundWire d)
        {
            bool same=p.Last!=null&&p.Actor==d.Actor&&Time.time-p.Seen<.75f;var support=GroundSupport.Get(v);if(support==null)return;
            for(int i=0;i<2;i++)p.From[i]=same?Vector3.Lerp(p.From[i],p.Last.Feet[i],Mathf.Clamp01((Time.time-p.Seen)/.1f)):d.Feet[i];
            if(same){if((d.Flags&8)!=0&&(p.Last.Flags&8)==0)RobotAudio.Event(v,"entry-brace",d.Sequence,.28f);
                if((d.Flags&8)==0&&(p.Last.Flags&8)!=0)RobotAudio.Event(v,"stand-lock",d.Sequence,.32f);
                for(int i=0;i<2;i++)if((d.Flags&(2<<i))!=0&&(p.Last.Flags&(2<<i))==0)RobotAudio.ContactEvent(v,i==0?"step-left":"step-right",d.Sequence,d.Feet[i],.85f);}
            p.Actor=d.Actor;p.Received=d.Sequence;p.Last=d;p.Seen=Time.time;support.Actor=d.Actor;support.Initialized=true;support.Grounded=(d.Flags&1)!=0;support.Recovering=(d.Flags&8)!=0;support.Cautious=(d.Flags&16)!=0;
            for(int i=0;i<2;i++){support.Feet[i].Position=d.Feet[i];support.Feet[i].Normal=d.Normals[i];support.Feet[i].Planted=(d.Flags&(2<<i))!=0;support.Feet[i].Swing=!support.Feet[i].Planted;}
            if(support.Recovering){var m=Locomotion.Get(v);m.AirSince=-1;m.LandingEventExpected=false;m.LandingPendingUntil=-100;}
        }
        public static bool Pose(EntityVehicle v,Model.Rig rig){if(!v.isEntityRemote||!Fresh(v))return false;var p=Get(v);float t=Mathf.Clamp01((Time.time-p.Seen)/.1f);for(int i=0;i<2;i++)Gait.Solve(rig,i,((p.Last.Flags&(2<<i))!=0?p.Last.Feet[i]:Vector3.Lerp(p.From[i],p.Last.Feet[i],t))-Origin.position,p.Last.Normals[i],0);return true;}
        public static void Tick(World world){foreach(var pair in peers){var p=pair.Value;if(p.Last==null)continue;var v=world.GetEntity(pair.Key) as EntityVehicle;if(v==null||Actor(v)!=p.Actor||v.IsDead()||Flight.AirPose(Locomotion.Get(v))||Locomotion.Get(v).HoverOn||Time.time-p.Seen>=.75f){p.Last=null;if(v!=null&&v.isEntityRemote){var s=GroundSupport.Find(v);if(s!=null){s.Recovering=s.Cautious=s.QueuedToggle=false;s.Initialized=false;}}}}}
        public static void Forget(EntityVehicle v){peers.Remove(v.entityId);}public static void Clear(){peers.Clear();}
    }
}
