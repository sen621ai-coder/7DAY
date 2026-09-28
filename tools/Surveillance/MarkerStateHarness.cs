// Minimal world/network fixtures for compiling the actual state and wire implementation.
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using PZAEC.Surveillance;
namespace UnityEngine
{
 public struct Vector3 {public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}public float sqrMagnitude=>x*x+y*y+z*z;public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}
 public static class Time {public static float realtimeSinceStartup;}
 public static class Mathf {public static int Clamp(int v,int a,int b)=>Math.Max(a,Math.Min(b,v));}
}
public struct Vector3i {public int x,y,z;public Vector3i(int a,int b,int c){x=a;y=b;z=c;}}
public class Identity {public string CombinedString;}
public class PlayerData {public Identity PrimaryId;}
public class Entity {}
public class EntityPlayer:Entity {public UnityEngine.Vector3 position;public PlayerData PersistentPlayerData;public bool isAdmin;public bool IsDead()=>false;}
public class Block {public int requiredPower;public bool isMultiBlock;public MultiBlock multiBlockPos=new MultiBlock();public static Block GetBlockByName(string name,bool v)=>null;public string GetBlockName()=>"";public virtual TileEntity CreateTileEntity(object ignored)=>null;}
public class MultiBlock {public Vector3i[] pos=new Vector3i[0];}
public class BlockPZAEC_SurveillanceCamera:Block {}
public class BlockPZAEC_SurveillanceScreen:Block {}
public class BlockValue {public Block Block=new Block();}
public class TileEntity {public bool IsRemoving;public BlockValue blockValue=new BlockValue();public Vector3i ToWorldPos()=>new Vector3i();}
public class TileEntityPowered:TileEntity {public bool IsPowered;}
public class TileEntityPoweredBlock:TileEntityPowered {}
public class TileEntityPoweredTrigger:TileEntityPowered {public Identity GetOwner()=>null;}
public class TileList {public Dictionary<int,TileEntity> dict=new Dictionary<int,TileEntity>();}
public class Chunk {public bool IsLocked,NeedsDecoration;public TileList GetTileEntities()=>new TileList();}
public class ChunkCache {public long[] GetChunkKeysCopySync()=>new long[0];public object GetChunkSync(long k)=>null;}
public class World {public bool Remote;public Entity Player;public ChunkCache ChunkCache;public bool IsRemote()=>Remote;public TileEntity GetTileEntity(Vector3i p)=>null;public Entity GetEntity(int id)=>Player;}
public class GameManager {public static GameManager Instance=new GameManager();public World World;}
public static class GameIO {public static string GetSaveGameDir()=>"";}
public static class Log {public static void Out(string m){}public static void Error(string m){Console.WriteLine(m);}}
public class PooledBinaryWriter:BinaryWriter {public PooledBinaryWriter(Stream s):base(s) {}}
public class PooledBinaryReader:BinaryReader {public PooledBinaryReader(Stream s):base(s) {}}
public enum NetPackageDirection {ToClient,ToServer}
public class ClientInfo {public bool loginDone,bAttachedToEntity;public int entityId;public void SendPackage(NetPackage p){}}
public abstract class NetPackage
{
 public ClientInfo Sender;public abstract NetPackageDirection PackageDirection{get;}public abstract int GetLength();public virtual void write(PooledBinaryWriter w){}public abstract void read(PooledBinaryReader r);public abstract void ProcessPackage(World w,GameManager g);
}
public static class NetPackageManager {public static T GetPackage<T>() where T:new()=>new T();}
public class ConnectionManager {public static ConnectionManager Instance;public bool IsServer;public void SendPackage(NetPackage p){}}
namespace PZAEC.Surveillance {public static class SurveillanceMenu {public static void Result(Vector3i p,bool accepted,string message){}}}
public static class MarkerStateHarness
{
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 static void Field(string name,object value)=>typeof(SurveillanceState).GetField(name,BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,value);
 static void Call(string name)=>typeof(SurveillanceState).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
 static T RoundTrip<T>(T input) where T:NetPackage,new()
 {
  using(var stream=new MemoryStream())
  {
   var writer=new PooledBinaryWriter(stream);input.write(writer);writer.Flush();Check(stream.Length<=input.GetLength(),"Package size underreported");stream.Position=0;
   var output=new T();output.read(new PooledBinaryReader(stream));Check(stream.Position==stream.Length,"Wire length mismatch");return output;
  }
 }
 public static void Run(string path)
 {
  SurveillanceState.Stop();var world=new World();Field("world",world);Field("path",path);GameManager.Instance.World=world;
  var position=new Vector3i(0,0,0);var screen=SurveillanceState.Register(position,SurveillanceState.ScreenBlock,"owner");
  var second=SurveillanceState.Register(new Vector3i(2,0,0),SurveillanceState.ScreenBlock,"owner");
  var player=new EntityPlayer{position=new UnityEngine.Vector3(0,0,0),PersistentPlayerData=new PlayerData{PrimaryId=new Identity{CombinedString="owner"}}};world.Player=player;
  string message;Check(!screen.TargetMarkers,"Old/default screen enables markers");
  Check(SurveillanceState.SetTargetMarkers(position,screen.Id,true,0,player,out message),"Owner enable failed");
  Check(screen.TargetMarkers&&screen.Revision==1&&!second.TargetMarkers,"Toggle leaks to another screen");
  Check(!SurveillanceState.SetTargetMarkers(position,screen.Id,false,0,player,out message)&&screen.TargetMarkers,"Stale revision overwrites settings");
  Check(!SurveillanceState.SetTargetMarkers(position,Guid.NewGuid(),false,1,player,out message),"Replaced-screen identity accepted");
  player.PersistentPlayerData.PrimaryId.CombinedString="other";
  Check(!SurveillanceState.SetTargetMarkers(position,screen.Id,false,1,player,out message),"Non-owner changed settings");
  player.PersistentPlayerData.PrimaryId.CombinedString="owner";player.position=new UnityEngine.Vector3(100,0,0);
  Check(!SurveillanceState.SetTargetMarkers(position,screen.Id,false,1,player,out message),"Distant player changed settings");player.position=new UnityEngine.Vector3(0,0,0);
  Check(screen.Clone().TargetMarkers,"Snapshot clone lost option");Call("Save");screen.TargetMarkers=false;Call("Load");screen=SurveillanceState.At(position);
  Check(screen.TargetMarkers&&screen.Revision==1&&!SurveillanceState.At(second.Position).TargetMarkers,"Save/load lost screen option");
  var document=XDocument.Load(path);foreach(var e in document.Root.Elements("device"))e.Attribute("targetMarkers")?.Remove();document.Save(path);Call("Load");screen=SurveillanceState.At(position);
  Check(!screen.TargetMarkers,"Legacy save default is not off");
  screen.TargetMarkers=true;SurveillanceClient.Clear();
  var snapshot=RoundTrip(new NetPackagePZSurveillanceSnapshot().Setup(new SurveillanceSnapshot{Revision=10,Devices=new[]{screen.Clone(),SurveillanceState.At(second.Position).Clone()}}));
  var remote=new World{Remote=true};GameManager.Instance.World=remote;snapshot.ProcessPackage(remote,GameManager.Instance);
  Check(SurveillanceClient.At(position).TargetMarkers&&!SurveillanceClient.At(second.Position).TargetMarkers,"Snapshot round-trip leaked/lost option");
  GameManager.Instance.World=world;var packet=RoundTrip(new NetPackagePZSurveillanceMarkers().Setup(screen,false));
  packet.Sender=new ClientInfo{loginDone=true,bAttachedToEntity=true,entityId=1};packet.ProcessPackage(world,GameManager.Instance);
  Check(!SurveillanceState.At(position).TargetMarkers&&SurveillanceState.At(position).Revision==2,"Marker request round-trip failed");
  SurveillanceState.Stop();SurveillanceClient.Clear();
  Console.WriteLine("PASS marker defaults, per-screen isolation, permissions, distance, revision/identity validation, save/legacy load, snapshot and request round-trip");
 }
}
