$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$source=Get-Content (Join-Path $root '99-AEC_T16_RuntimeFix/Source/ApacheWeapons.cs') -Raw
$start=$source.IndexOf('    public sealed class NetPackagePZApacheIntent')
if($start-lt 0){throw 'Packet classes missing'}
$packets='using UnityEngine; namespace AECT16RuntimeFix {'+"`n"+$source.Substring($start)
$fixture=@'
namespace UnityEngine { public struct Vector3 {public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}} }
public enum NetPackageDirection { ToServer,ToClient }
public class World{}
public class GameManager{}
public class PacketSender {public bool loginDone,bAttachedToEntity;public int entityId;}
public class PooledBinaryReader:System.IO.BinaryReader {public PooledBinaryReader(System.IO.Stream s):base(s){} }
public class PooledBinaryWriter:System.IO.BinaryWriter {public PooledBinaryWriter(System.IO.Stream s):base(s){} }
public abstract class NetPackage {
 public PacketSender Sender;
 public virtual NetPackageDirection PackageDirection {get{return NetPackageDirection.ToServer;}}
 public abstract int GetLength();public abstract void read(PooledBinaryReader r);
 public virtual void write(PooledBinaryWriter w){}public abstract void ProcessPackage(World w,GameManager g);
}
namespace AECT16RuntimeFix {
 public static class ApacheWeapons {public static bool Server;public static int Calls,Actor,Vehicle;public static void Request(World w,int actor,int vehicle,byte op,UnityEngine.Vector3 d,UnityEngine.Vector3 origin,int sequence){Calls++;Actor=actor;Vehicle=vehicle;} }
 public static class ApacheWeaponVisuals {public static int Calls;public static void Receive(World w,int v,int id,byte kind,UnityEngine.Vector3 a,UnityEngine.Vector3 b,float h){Calls++;} }
}
public static class ApachePacketTests {
 static int checks;static void Check(bool b,string message){checks++;if(!b)throw new System.Exception(message);}
 public static void Run(){
  var packet=new AECT16RuntimeFix.NetPackagePZApacheIntent().Setup(123,1,new UnityEngine.Vector3(.1f,-.2f,.9f),new UnityEngine.Vector3(10,20,30),42);
  var stream=new System.IO.MemoryStream();var writer=new PooledBinaryWriter(stream);packet.write(writer);writer.Flush();
  Check(stream.Length==packet.GetLength(),"intent payload size");stream.Position=0;
  var copy=new AECT16RuntimeFix.NetPackagePZApacheIntent();copy.read(new PooledBinaryReader(stream));
  Check(copy.Vehicle==123&&copy.Op==1&&copy.Direction.y==-.2f&&copy.Origin.y==20&&copy.Sequence==42,"intent roundtrip");
  Check(copy.PackageDirection==NetPackageDirection.ToServer,"intent server only");
  var world=new World();AECT16RuntimeFix.ApacheWeapons.Server=true;
  copy.ProcessPackage(world,null);Check(AECT16RuntimeFix.ApacheWeapons.Calls==0,"missing sender rejected");
  copy.Sender=new PacketSender{entityId=77,loginDone=false,bAttachedToEntity=true};
  copy.ProcessPackage(world,null);Check(AECT16RuntimeFix.ApacheWeapons.Calls==0,"unauthenticated sender rejected");
  copy.Sender.loginDone=true;copy.Sender.bAttachedToEntity=false;
  copy.ProcessPackage(world,null);Check(AECT16RuntimeFix.ApacheWeapons.Calls==0,"unattached sender rejected");
  copy.Sender.bAttachedToEntity=true;copy.ProcessPackage(null,null);Check(AECT16RuntimeFix.ApacheWeapons.Calls==0,"missing world rejected");
  AECT16RuntimeFix.ApacheWeapons.Server=false;copy.ProcessPackage(world,null);Check(AECT16RuntimeFix.ApacheWeapons.Calls==0,"client cannot execute damage intent");
  AECT16RuntimeFix.ApacheWeapons.Server=true;copy.ProcessPackage(world,null);
  Check(AECT16RuntimeFix.ApacheWeapons.Calls==1&&AECT16RuntimeFix.ApacheWeapons.Actor==77&&AECT16RuntimeFix.ApacheWeapons.Vehicle==123,"actor comes from connection identity");
  var effect=new AECT16RuntimeFix.NetPackagePZApacheEvent().Setup(123,456,2,new UnityEngine.Vector3(1,2,3),new UnityEngine.Vector3(4,5,6),78);
  stream=new System.IO.MemoryStream();writer=new PooledBinaryWriter(stream);effect.write(writer);writer.Flush();
  Check(stream.Length==effect.GetLength(),"visual packet payload size");stream.Position=0;
  var effectCopy=new AECT16RuntimeFix.NetPackagePZApacheEvent();effectCopy.read(new PooledBinaryReader(stream));
  Check(effectCopy.Vehicle==123&&effectCopy.Id==456&&effectCopy.Kind==2&&effectCopy.A.z==3&&effectCopy.B.x==4&&effectCopy.Heat==78,"visual packet roundtrip");
  Check(effectCopy.PackageDirection==NetPackageDirection.ToClient,"effects client only");
  effectCopy.ProcessPackage(world,null);Check(AECT16RuntimeFix.ApacheWeaponVisuals.Calls==0,"visual replay never accepted on server");
  AECT16RuntimeFix.ApacheWeapons.Server=false;effectCopy.ProcessPackage(world,null);Check(AECT16RuntimeFix.ApacheWeaponVisuals.Calls==1,"client plays visual event");
    var status=new AECT16RuntimeFix.NetPackagePZApacheEvent().Setup(123,3,4,new UnityEngine.Vector3(8,90,.5f),new UnityEngine.Vector3(2,0,0),67);
  stream=new System.IO.MemoryStream();writer=new PooledBinaryWriter(stream);status.write(writer);writer.Flush();stream.Position=0;
  effectCopy.read(new PooledBinaryReader(stream));Check(effectCopy.Kind==4&&effectCopy.Id==3&&effectCopy.A.y==90&&effectCopy.B.x==2,"explicit overheat/storage flags, ammo and recovery roundtrip");
  System.Console.WriteLine("PASS: "+checks+" actual packet serialization, direction and connection-identity assertions (mock transport, not multiplayer playtest).");
 }
}
'@
Add-Type -TypeDefinition ($packets+"`n"+$fixture)
[ApachePacketTests]::Run()
