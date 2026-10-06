using UnityEngine;

namespace PZAEC.Mecha
{
    public sealed class NetPackagePZAECMechaIntent : NetPackage
    {
        public int Vehicle, Sequence; public byte Op;
        public Vector3 Direction, Origin;
        public override NetPackageDirection PackageDirection { get { return NetPackageDirection.ToServer; } }
        public NetPackagePZAECMechaIntent Setup(int vehicle, byte op, Vector3 direction, Vector3 origin, int sequence)
        { Vehicle = vehicle; Op = op; Direction = direction; Origin = origin; Sequence = sequence; return this; }
        
#if MECHA_LEGACY_PACKAGE_LENGTH
        public override int
#else
        public int
#endif
        GetLength() { return 33; }
        public override void read(PooledBinaryReader r)
        {
            Vehicle = r.ReadInt32(); Op = r.ReadByte();
            Direction = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            Origin = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            Sequence = r.ReadInt32();
        }
        public override void write(PooledBinaryWriter w)
        {
            base.write(w); w.Write(Vehicle); w.Write(Op);
            w.Write(Direction.x); w.Write(Direction.y); w.Write(Direction.z);
            w.Write(Origin.x); w.Write(Origin.y); w.Write(Origin.z);
            w.Write(Sequence);
        }
        public override void ProcessPackage(World world, GameManager callbacks)
        {
            if (world != null && Weapons.Server && Sender != null && (Sender.bAttachedToEntity || Op==Weapons.Repair || Op==Weapons.RepairStop || Op==Weapons.BoardControl || Op==Weapons.SkipBoard))
                Weapons.Request(world, Sender.entityId, Vehicle, Op, Direction, Origin, Sequence);
        }
    }

    public sealed class NetPackagePZAECMechaEvent : NetPackage
    {
        public int Vehicle, Id; public byte Kind; public Vector3 A, B; public float Value, C;
        public override NetPackageDirection PackageDirection { get { return NetPackageDirection.ToClient; } }
        public NetPackagePZAECMechaEvent Setup(int vehicle, int id, byte kind, Vector3 a, Vector3 b, float value, float c)
        { Vehicle = vehicle; Id = id; Kind = kind; A = a; B = b; Value = value; C = c; return this; }
        
#if MECHA_LEGACY_PACKAGE_LENGTH
        public override int
#else
        public int
#endif
        GetLength() { return 41; }
        public override void read(PooledBinaryReader r)
        {
            Vehicle = r.ReadInt32(); Id = r.ReadInt32(); Kind = r.ReadByte();
            A = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            B = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            Value = r.ReadSingle(); C = r.ReadSingle();
        }
        public override void write(PooledBinaryWriter w)
        {
            base.write(w); w.Write(Vehicle); w.Write(Id); w.Write(Kind);
            w.Write(A.x); w.Write(A.y); w.Write(A.z);
            w.Write(B.x); w.Write(B.y); w.Write(B.z);
            w.Write(Value); w.Write(C);
        }
        public override void ProcessPackage(World world, GameManager callbacks)
        {
            if (world != null && !Weapons.Server) MechaFX.Receive(world, Vehicle, Id, Kind, A, B, Value, C);
        }
    }
}
