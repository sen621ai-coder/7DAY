using UnityEngine;
namespace PZAEC.FlyingSword
{
    public enum SwordOp:byte {Deploy,Board,Return,Charge,Release,Cancel,Flight,Refill}
    public sealed class NetPackagePZAECJuqueIntent : NetPackage
    {
        public SwordOp Op;public int Target,Sequence;public Vector3 Origin,Direction;
        public override NetPackageDirection PackageDirection=>NetPackageDirection.ToServer;
        public NetPackagePZAECJuqueIntent Setup(SwordOp op,int target,int sequence,Vector3 origin,Vector3 direction){Op=op;Target=target;Sequence=sequence;Origin=origin;Direction=direction;return this;}
        public override void write(PooledBinaryWriter w){base.write(w);w.Write((byte)Op);w.Write(Target);w.Write(Sequence);WriteVector(w,Origin);WriteVector(w,Direction);}
        public override void read(PooledBinaryReader r){Op=(SwordOp)r.ReadByte();Target=r.ReadInt32();Sequence=r.ReadInt32();Origin=ReadVector(r);Direction=ReadVector(r);}
        public static void WriteVector(PooledBinaryWriter w,Vector3 v){w.Write(v.x);w.Write(v.y);w.Write(v.z);}
        public static Vector3 ReadVector(PooledBinaryReader r){return new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());}
        public override void ProcessPackage(World w,GameManager g){if(w!=null&&SwordRuntime.Server&&Sender!=null&&Sender.loginDone)SwordRuntime.Request(w,Sender.entityId,Op,Target,Sequence,Origin,Direction);}
    }
    public sealed class NetPackagePZAECJuqueEvent : NetPackage
    {
        public byte Kind;public int Actor,Slot,EntityId,Sequence;public string SwordId;public float Energy,Value;public Vector3 A,B;
        public override NetPackageDirection PackageDirection=>NetPackageDirection.ToClient;
        public NetPackagePZAECJuqueEvent Setup(byte kind,int actor,int slot,int entity,string id,float energy,Vector3 a,Vector3 b,float value,int seq){Kind=kind;Actor=actor;Slot=slot;EntityId=entity;SwordId=id??"";Energy=energy;A=a;B=b;Value=value;Sequence=seq;return this;}
        public override void write(PooledBinaryWriter w){base.write(w);w.Write(Kind);w.Write(Actor);w.Write(Slot);w.Write(EntityId);w.Write(Sequence);w.Write(SwordId);w.Write(Energy);w.Write(Value);NetPackagePZAECJuqueIntent.WriteVector(w,A);NetPackagePZAECJuqueIntent.WriteVector(w,B);}
        public override void read(PooledBinaryReader r){Kind=r.ReadByte();Actor=r.ReadInt32();Slot=r.ReadInt32();EntityId=r.ReadInt32();Sequence=r.ReadInt32();SwordId=r.ReadString();Energy=r.ReadSingle();Value=r.ReadSingle();A=NetPackagePZAECJuqueIntent.ReadVector(r);B=NetPackagePZAECJuqueIntent.ReadVector(r);}
        public override void ProcessPackage(World w,GameManager g){if(w!=null&&!SwordRuntime.Server)SwordRuntime.Receive(w,this);}
    }
}
