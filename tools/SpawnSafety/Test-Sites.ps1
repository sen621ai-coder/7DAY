$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
$sites=Get-Content "$root/ZZZ-PZAEC_SpawnSafety/Source/Sites.cs" -Raw
$sites=$sites.Substring(0,$sites.IndexOf('    public sealed class Request'))+'}'
$rules=Get-Content "$root/ZZZ-PZAEC_SpawnSafety/Source/SiteRules.cs" -Raw
# Reuse only the managed vector/bounds fixture; native Unity physics is simulated explicitly.
$fixtureText=Get-Content "$PSScriptRoot/Test-Rules.ps1" -Raw
$start=$fixtureText.IndexOf('namespace UnityEngine {')
$vectors=$fixtureText.Substring($start,$fixtureText.IndexOf('public class World {')-$start)
$vectors=$vectors.Replace('public float x,y,z;','public static Vector3 up=>new Vector3(0,1,0);public static Vector3 down=>new Vector3(0,-1,0);public static Vector3 operator *(Vector3 v,float f)=>new Vector3(v.x*f,v.y*f,v.z*f);public float x,y,z;')
$fakes=@'
namespace UnityEngine {
 public struct Quaternion {public static Quaternion identity=>new Quaternion();}
 public enum QueryTriggerInteraction {Ignore}
 public struct Ray {public Vector3 origin,direction;public Ray(Vector3 o,Vector3 d){origin=o;direction=d;}}
 public static class Physics {public static bool Blocked;public static Vector3 Center;public static int Mask;public static bool CheckBox(Vector3 c,Vector3 e,Quaternion q,int mask,QueryTriggerInteraction t){Center=c;Mask=mask;return Blocked;}}
}
public static class Origin {public static UnityEngine.Vector3 position;}
public static class Constants {public const int cLayerTerrainCollision=16,cLayerTerrain=28;}
public struct Vector3i {public int x,y,z;public Vector3i(int x,int y,int z){this.x=x;this.y=y;this.z=z;}}
public class Shape {public bool Terrain;public bool IsTerrain()=>Terrain;}
public class Block {
 public bool IsCollideMovement=true;public Shape shape=new Shape();public UnityEngine.Vector3 Size=new UnityEngine.Vector3(1,1,1);
 public void GetCollisionAABB(BlockValue v,int x,int y,int z,float add,System.Collections.Generic.List<UnityEngine.Bounds> boxes){boxes.Add(new UnityEngine.Bounds(new UnityEngine.Vector3(x+.5f,y+.5f,z+.5f),Size));}
}
public class BlockModelTree:Block {}
public struct BlockValue {public Block Block;public bool isair=>Block==null;}
public class HitInfoDetails {public UnityEngine.Vector3 pos;public Vector3i blockPos;}
public class WorldRayHitInfo {public bool bHitValid=true;public HitInfoDetails hit=new HitInfoDetails();}
public static class Voxel {public static bool Hit=true;public static WorldRayHitInfo voxelRayHitInfo=new WorldRayHitInfo();public static bool Raycast(World w,UnityEngine.Ray r,float d,bool a,bool b)=>Hit;}
public class World {
 public bool Loaded=true;public int Reads;public int Highest;
 public System.Collections.Generic.Dictionary<Vector3i,BlockValue> Blocks=new System.Collections.Generic.Dictionary<Vector3i,BlockValue>();
 public bool IsChunkAreaLoaded(UnityEngine.Vector3 p)=>Loaded;
 public byte GetTerrainHeight(int x,int z)=>30;
 public byte GetHeight(int x,int z)=>(byte)Highest;
 public BlockValue GetBlock(Vector3i p){Reads++;return Blocks.TryGetValue(p,out var v)?v:new BlockValue();}
 public BlockValue GetBlock(int x,int y,int z)=>GetBlock(new Vector3i(x,y,z));
 public void Put(int x,int y,int z,Block b){Blocks[new Vector3i(x,y,z)]=new BlockValue{Block=b};Highest=System.Math.Max(Highest,y);}
}
public static class SitesTests {
 static int checks;static void Check(bool b,string s){checks++;if(!b)throw new System.Exception(s);}
 public static string Run(){
  var w=new World{Highest=30};var sites=new PZAEC.SpawnSafety.Sites(w);
  Check(!sites.StructuralCover(0,0,32,254),"open sky clear");Check(w.Reads==0,"no scanning hundreds of empty sky blocks");
  w.Put(0,40,0,new Block());sites=new PZAEC.SpawnSafety.Sites(w);Check(sites.StructuralCover(0,0,32,254),"roof detected below live height cap");int reads=w.Reads;
  Check(sites.StructuralCover(0,0,32,254)&&w.Reads==reads,"repeated column cached within same entity attempt");
  w.Blocks.Clear();w.Highest=30;sites=new PZAEC.SpawnSafety.Sites(w);Check(!sites.StructuralCover(0,0,32,254),"next entity sees removed roof");
  w.Put(0,40,0,new BlockModelTree());sites=new PZAEC.SpawnSafety.Sites(w);Check(!sites.StructuralCover(0,0,32,254),"tree does not block surface spawn");
  w.Loaded=false;sites=new PZAEC.SpawnSafety.Sites(w);Check(sites.StructuralCover(0,0,32,254),"unknown neighbour column is conservative");w.Loaded=true;w.Blocks.Clear();
  w.Put(0,30,0,new Block{shape=new Shape{Terrain=true}});sites=new PZAEC.SpawnSafety.Sites(w);
  var body=new UnityEngine.Bounds(new UnityEngine.Vector3(.5f,31,.5f),new UnityEngine.Vector3(.5f,1.6f,.5f));
  Check(sites.Clear(body),"terrain proxy cube not mistaken for density mesh");
  UnityEngine.Physics.Blocked=true;Check(!sites.Clear(body),"real terrain collision blocks placement");UnityEngine.Physics.Blocked=false;
  Origin.position=new UnityEngine.Vector3(100,0,200);Check(sites.Clear(body),"floating origin query");
  Check(UnityEngine.Physics.Center.x==-99.5f&&UnityEngine.Physics.Center.z==-199.5f,"physics receives scene coordinates");
  Check(UnityEngine.Physics.Mask==((1<<16)|(1<<28)),"terrain-only mask excludes unregistered enemy collider");
  w.Put(0,30,0,new Block());Check(!sites.Clear(body),"building collision blocks same volume");
  w.Blocks.Clear();w.Put(0,30,0,new Block{Size=new UnityEngine.Vector3(.1f,.1f,.1f)});Check(sites.Clear(new UnityEngine.Bounds(new UnityEngine.Vector3(.9f,31,.5f),new UnityEngine.Vector3(.1f,1.6f,.1f))),"small shape respects actual collision bounds");
  w.Put(0,30,0,new Block());Voxel.voxelRayHitInfo.hit.blockPos=new Vector3i(0,30,0);Voxel.voxelRayHitInfo.hit.pos=new UnityEngine.Vector3(.5f,31,.5f);float y;
  Check(sites.Floor(body.center,out y)&&y==31,"actual ray support height used");Voxel.Hit=false;Check(!sites.Floor(body.center,out y),"missing ray support rejected");
  return "PASS: "+checks+" actual Sites adapter checks with simulated native physics; live Unity geometry not exercised.";
 }
}
'@
# Source using directives must precede all namespaces.
$sites=$sites.Substring($sites.IndexOf('namespace PZAEC'))
Add-Type -TypeDefinition ("using System.Collections.Generic;`n"+$rules+$sites+$vectors+$fakes)
[SitesTests]::Run()
