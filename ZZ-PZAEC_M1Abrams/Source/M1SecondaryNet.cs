using UnityEngine;
namespace PZAEC.M1
{
    public sealed class NetPackageM1SecondaryIntent:NetPackage
    {
        public byte Version=SecondaryRules.Protocol,Flags,Select;public int Vehicle,Serial;public Vector3 Origin,Direction;
        public override NetPackageDirection PackageDirection=>NetPackageDirection.ToServer;
        public override void read(PooledBinaryReader r){Version=r.ReadByte();Vehicle=r.ReadInt32();Serial=r.ReadInt32();Flags=r.ReadByte();Select=r.ReadByte();Origin=NetPackageM1Intent.Read(r);Direction=NetPackageM1Intent.Read(r);}
        public override void write(PooledBinaryWriter w){base.write(w);w.Write(Version);w.Write(Vehicle);w.Write(Serial);w.Write(Flags);w.Write(Select);NetPackageM1Intent.Write(w,Origin);NetPackageM1Intent.Write(w,Direction);}
        public override void ProcessPackage(World w,GameManager g){if(w!=null&&Weapons.Server&&Sender!=null&&Sender.loginDone&&Sender.bAttachedToEntity)Secondary.Request(w,Sender.entityId,this);}
    }
    public sealed class NetPackageM1SecondaryEvent:NetPackage
    {
        public byte Version=SecondaryRules.Protocol,Kind;public int Vehicle,Epoch,Serial;public float Time;public Vector3 A,B;public readonly float[] F=new float[16];public readonly int[] I=new int[8];
        public override NetPackageDirection PackageDirection=>NetPackageDirection.ToClient;
        public override void read(PooledBinaryReader r){Version=r.ReadByte();Kind=r.ReadByte();Vehicle=r.ReadInt32();Epoch=r.ReadInt32();Serial=r.ReadInt32();Time=r.ReadSingle();A=NetPackageM1Intent.Read(r);B=NetPackageM1Intent.Read(r);for(int i=0;i<F.Length;i++)F[i]=r.ReadSingle();for(int i=0;i<I.Length;i++)I[i]=r.ReadInt32();}
        public override void write(PooledBinaryWriter w){base.write(w);w.Write(Version);w.Write(Kind);w.Write(Vehicle);w.Write(Epoch);w.Write(Serial);w.Write(Time);NetPackageM1Intent.Write(w,A);NetPackageM1Intent.Write(w,B);foreach(float f in F)w.Write(f);foreach(int n in I)w.Write(n);}
        public override void ProcessPackage(World w,GameManager g){if(w!=null&&!Weapons.Server&&Version==SecondaryRules.Protocol)SecondaryPresentation.Receive(w,this);}
    }
}
