$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
$source=Get-Content "$root/ZZ-PZAEC_M1Abrams/Source/M1Weapons.cs" -Raw
$start=$source.IndexOf('    public sealed class NetPackageM1Intent')
$packets='using System; using UnityEngine; namespace PZAEC.M1 {'+$source.Substring($start)
$fixture=@'
namespace UnityEngine {public struct Vector3 {public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}}}
public enum NetPackageDirection{ToServer,ToClient}
public class World{}public class GameManager{}
public class SenderInfo{public bool loginDone,bAttachedToEntity;public int entityId;}
public class PooledBinaryReader:System.IO.BinaryReader{public PooledBinaryReader(System.IO.Stream s):base(s){}}
public class PooledBinaryWriter:System.IO.BinaryWriter{public PooledBinaryWriter(System.IO.Stream s):base(s){}}
public abstract class NetPackage{public SenderInfo Sender;public abstract NetPackageDirection PackageDirection{get;}public abstract int GetLength();public abstract void read(PooledBinaryReader r);public virtual void write(PooledBinaryWriter w){}public abstract void ProcessPackage(World w,GameManager g);}
public static class NetPackageManager{public static T GetPackage<T>() where T:new()=>new T();}
namespace PZAEC.M1 {
 public static class Weapons{public static bool Server;public static int Calls,Actor;public static void Request(World w,int actor,int v,byte op,int seq,UnityEngine.Vector3 o,UnityEngine.Vector3 d){Calls++;Actor=actor;}}
 public static class Presentation{public static int Calls;public static void Receive(World w,NetPackageM1Event p){Calls++;}}
}
public static class M1PacketTests{
 static int n;static void Check(bool x,string s){n++;if(!x)throw new System.Exception(s);}
 public static void Run(){
  var a=new PZAEC.M1.NetPackageM1Intent().Setup(123,1,42,new UnityEngine.Vector3(4,5,6),new UnityEngine.Vector3(0,0,1));
  var stream=new System.IO.MemoryStream();a.write(new PooledBinaryWriter(stream));Check(stream.Length==33&&a.GetLength()==33,"intent wire size");stream.Position=0;var b=new PZAEC.M1.NetPackageM1Intent();b.read(new PooledBinaryReader(stream));Check(b.Vehicle==123&&b.Op==1&&b.Sequence==42&&b.Origin.y==5&&b.Direction.z==1,"intent roundtrip");
  PZAEC.M1.Weapons.Server=true;var w=new World();b.ProcessPackage(w,null);Check(PZAEC.M1.Weapons.Calls==0,"anonymous denied");b.Sender=new SenderInfo{entityId=8};b.ProcessPackage(w,null);Check(PZAEC.M1.Weapons.Calls==0,"not logged in denied");b.Sender.loginDone=true;b.ProcessPackage(w,null);Check(PZAEC.M1.Weapons.Calls==0,"unattached sender denied");b.Sender.bAttachedToEntity=true;b.ProcessPackage(w,null);Check(PZAEC.M1.Weapons.Calls==1&&PZAEC.M1.Weapons.Actor==8,"authenticated sender identity used");PZAEC.M1.Weapons.Server=false;b.ProcessPackage(w,null);Check(PZAEC.M1.Weapons.Calls==1,"client cannot process fire intent");
  var e=PZAEC.M1.NetPackageM1Event.Make(123,99,66,22,1,12.5f,new UnityEngine.Vector3(1,2,3),new UnityEngine.Vector3(4,5,6),.4f,50);
  stream=new System.IO.MemoryStream();e.write(new PooledBinaryWriter(stream));Check(stream.Length==53&&e.GetLength()==53,"event wire size");stream.Position=0;var f=new PZAEC.M1.NetPackageM1Event();f.read(new PooledBinaryReader(stream));Check(f.Vehicle==123&&f.Epoch==99&&f.Sequence==66&&f.Shot==22&&f.Time==12.5f&&f.B.z==6&&f.X==.4f&&f.Y==50,"event roundtrip");
  PZAEC.M1.Weapons.Server=true;f.ProcessPackage(w,null);Check(PZAEC.M1.Presentation.Calls==0,"server rejects cosmetic event injection");PZAEC.M1.Weapons.Server=false;f.ProcessPackage(w,null);Check(PZAEC.M1.Presentation.Calls==1,"client accepts server presentation");
  Check(a.PackageDirection==NetPackageDirection.ToServer&&e.PackageDirection==NetPackageDirection.ToClient,"wire direction restrictions");
  Console.WriteLine("PASS "+n+" actual M1 packet serialization and routing checks");
 }
}
'@
Add-Type -TypeDefinition ($packets+$fixture)
[M1PacketTests]::Run()
