$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
$rules=Get-Content "$root/ZZZ-PZAEC_SpawnSafety/Source/SiteRules.cs" -Raw
$sites=Get-Content "$root/ZZZ-PZAEC_SpawnSafety/Source/Sites.cs" -Raw
$runtime='namespace PZAEC.SpawnSafety {'+$sites.Substring($sites.IndexOf('    public sealed class Request'))
$fixtures=@'
namespace UnityEngine {
 public struct Vector3 {
  public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
  public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
  public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
 }
 public struct Bounds {
  public Vector3 center,size;public Bounds(Vector3 c,Vector3 s){center=c;size=s;}
  public Vector3 extents=>new Vector3(size.x/2,size.y/2,size.z/2);
  public Vector3 min=>center-extents;public Vector3 max=>center+extents;
  public bool Intersects(Bounds b)=>min.x<=b.max.x&&max.x>=b.min.x&&min.y<=b.max.y&&max.y>=b.min.y&&min.z<=b.max.z&&max.z>=b.min.z;
 }
 public static class Mathf {public static int FloorToInt(float f)=>(int)System.Math.Floor(f);}
 public static class Object {public static int Destroyed;public static void Destroy(object o){Destroyed++;}}
}
public class World {public Fixture Site=new Fixture();public bool Remote,Native=true;public bool IsRemote()=>Remote;public bool IsChunkAreaLoaded(UnityEngine.Vector3 p)=>Site.Loaded(p);public float GetTerrainHeight(int x,int z)=>Site.Terrain(x,z);public bool CanMobsSpawnAtPos(UnityEngine.Vector3 p)=>Native;}
public class GameManager {public static GameManager Instance=new GameManager();public World World=new World();}
public class Prefab {public bool Active=true;public void SetActive(bool b){Active=b;}}
public class Entity {public int entityClass=1;public UnityEngine.Vector3 position;public float physicsColliderRadius=.3f,physicsHeight=1.8f;public UnityEngine.Bounds boundingBox;public Prefab gameObject=new Prefab();public void SetPosition(UnityEngine.Vector3 p,bool force){position=p;}}
public class EntityAlive:Entity {}
public class EntityPlayer:EntityAlive {}
public class EntityFlying:EntityAlive {}
public class EntityVulture:EntityFlying {}
public class EntityClass {public bool bIsEnemyEntity=true;public float SizeScale=1;public static System.Collections.Generic.Dictionary<int,EntityClass> list=new System.Collections.Generic.Dictionary<int,EntityClass>{{1,new EntityClass()},{2,new EntityClass{bIsEnemyEntity=false}}};}
public class Fixture:PZAEC.SpawnSafety.ISiteWorld {
 public bool Available=true,Support=true,InvalidEdge;public float Ground=30,TerrainY=30,Slope,GapX=1000;
 public System.Collections.Generic.List<UnityEngine.Bounds> Obstacles=new System.Collections.Generic.List<UnityEngine.Bounds>();
 public System.Collections.Generic.HashSet<string> Cover=new System.Collections.Generic.HashSet<string>();
 public UnityEngine.Bounds LastBody;public int Queries;
 public bool Loaded(UnityEngine.Vector3 p)=>Available&&p.x<GapX;
 public float Terrain(int x,int z)=>TerrainY;
 public bool Floor(UnityEngine.Vector3 p,out float y){y=InvalidEdge&&p.x!=0?float.NaN:Ground+p.x*Slope;return Support&&p.x<GapX;}
 public bool Clear(UnityEngine.Bounds b){LastBody=b;Queries++;foreach(var o in Obstacles)if(o.Intersects(b))return false;return true;}
 public bool StructuralCover(int x,int z,int low,int high)=>Cover.Contains(x+","+z);
}
namespace PZAEC.SpawnSafety {
 public class Sites:ISiteWorld {
  Fixture f;public Sites(World w){f=w.Site;}public bool Loaded(UnityEngine.Vector3 p)=>f.Loaded(p);public float Terrain(int x,int z)=>f.Terrain(x,z);public bool Floor(UnityEngine.Vector3 p,out float y)=>f.Floor(p,out y);public bool Clear(UnityEngine.Bounds b)=>f.Clear(b);public bool StructuralCover(int x,int z,int low,int high)=>f.StructuralCover(x,z,low,high);
 }
 public static class Diagnostics {public static int AcceptedCount,RejectedCount;public static void Accepted(World w,Entity e,string s,UnityEngine.Vector3 p,UnityEngine.Vector3 q,int a,bool b){AcceptedCount++;}public static void Rejected(string s,int id,UnityEngine.Vector3 p,string r){RejectedCount++;}}
}
public static class SpawnRulesTests {
 static int checks;static void Check(bool b,string s){checks++;if(!b)throw new System.Exception(s);}
 static UnityEngine.Vector3 P(float x=0,float y=31,float z=0)=>new UnityEngine.Vector3(x,y,z);
 static void Expect(Fixture f,UnityEngine.Vector3 p,bool surface,bool expected,string reason,float radius=.3f,float height=1.8f){UnityEngine.Vector3 feet;string result;bool ok=PZAEC.SpawnSafety.SiteRules.Validate(f,p,radius,height,surface,out feet,out result);Check(ok==expected,reason+" outcome "+result);if(!expected)Check(result==reason,reason+" got "+result);}
 static EntityAlive Enemy()=>new EntityAlive{position=P()};
 public static string Run(){
  Expect(new Fixture(),P(),true,true,"flat");
  Expect(new Fixture{Available=false},P(),true,false,"unloaded");
  Expect(new Fixture{Support=false},P(),true,false,"no-support");
  Expect(new Fixture(),P(y:float.NaN),true,false,"invalid-coordinates");
  Expect(new Fixture(),P(),true,false,"invalid-coordinates",float.PositiveInfinity);
  Expect(new Fixture(),P(),true,false,"invalid-coordinates",height:25);
  Expect(new Fixture(),P(y:40),true,false,"no-support");
  Expect(new Fixture{Ground=32},P(),true,false,"no-support");
  Expect(new Fixture{TerrainY=40},P(),true,false,"below-terrain");
  Expect(new Fixture{TerrainY=40},P(),false,true,"legitimate underground");
  Expect(new Fixture{Ground=40},P(y:41),true,false,"elevated-platform");
  Expect(new Fixture{Ground=40},P(y:41),false,true,"underground profile has no roof raising");
  var roof=new Fixture();roof.Cover.Add("0,0");Expect(roof,P(),true,false,"covered-ground");Expect(roof,P(),false,true,"underground ceiling allowed with clearance");
  roof=new Fixture();roof.Cover.Add("1,0");Expect(roof,P(),true,true,"single neighbouring column is not counted twice");
  roof.Cover.Add("-1,0");roof.Cover.Add("0,1");Expect(roof,P(),true,false,"covered-ground");
  var wall=new Fixture();wall.Obstacles.Add(new UnityEngine.Bounds(P(y:31),new UnityEngine.Vector3(1,2,1)));Expect(wall,P(),true,false,"body-overlap");
  var low=new Fixture();low.Obstacles.Add(new UnityEngine.Bounds(P(y:33),new UnityEngine.Vector3(4,.2f,4)));Expect(low,P(),false,true,"ordinary fits");Expect(low,P(),false,false,"body-overlap",height:4);
  var narrow=new Fixture();narrow.Obstacles.Add(new UnityEngine.Bounds(P(x:1,y:31),new UnityEngine.Vector3(.2f,2,4)));Expect(narrow,P(),false,true,"small fits");Expect(narrow,P(),false,false,"body-overlap",radius:1);
  Expect(new Fixture{Slope=4},P(),true,false,"unstable-support");
  Expect(new Fixture{InvalidEdge=true},P(),true,false,"unstable-support");
  var slope=new Fixture{Slope=.5f};Expect(slope,P(),true,true,"gentle slope");Check(slope.LastBody.min.y>30.1,"feet above highest footprint support");
  Expect(new Fixture{GapX=.1f},P(),true,false,"unloaded");
  Expect(new Fixture{Ground=253,TerrainY=253},P(y:253.5f),true,false,"outside-world");
  Expect(new Fixture{Ground=0,TerrainY=0},P(y:1),true,false,"outside-world");
  var world=GameManager.Instance.World;world.Site=new Fixture();
  int calls=0,destroyed=UnityEngine.Object.Destroyed;
  world.Site.Obstacles.Add(new UnityEngine.Bounds(P(y:31),new UnityEngine.Vector3(1,2,1)));
  var request=new PZAEC.SpawnSafety.Request{Source="test",Surface=true,Next=()=>{calls++;return P(3);}};
  var enemy=Enemy();Check(PZAEC.SpawnSafety.Safety.Accept(enemy,request),"retry finds valid neighbouring ground");Check(calls==1&&enemy.position.x==3,"relocated before registration");Check(UnityEngine.Object.Destroyed==destroyed,"accepted prefab retained");
  world.Site.Available=false;calls=0;request=new PZAEC.SpawnSafety.Request{Surface=true,Next=()=>{calls++;return P();}};
  Check(!PZAEC.SpawnSafety.Safety.Accept(Enemy(),request),"all failed");Check(calls==11&&request.Remaining==0,"strict 12-candidate shared budget");Check(UnityEngine.Object.Destroyed==destroyed+1,"only unregistered prefab cleaned");
  request=new PZAEC.SpawnSafety.Request{Surface=true};calls=0;Check(!PZAEC.SpawnSafety.Safety.Accept(Enemy(),request),"exact-point source fails without invented search");
  Check(PZAEC.SpawnSafety.Safety.Accept(new EntityVulture(),request),"flying exempt");Check(PZAEC.SpawnSafety.Safety.Accept(new EntityPlayer(),request),"player exempt");Check(PZAEC.SpawnSafety.Safety.Accept(new EntityAlive{entityClass=2},request),"friendly exempt");
  Check(PZAEC.SpawnSafety.Safety.Accept(new EntityFlying(),request),"non-vulture flying enemy exempt");
  Check(PZAEC.SpawnSafety.Safety.Accept(Enemy(),new PZAEC.SpawnSafety.Request{Bypass=true}),"explicit air/manual exempt");
  world.Remote=true;Check(PZAEC.SpawnSafety.Safety.Accept(Enemy(),request),"client not authoritative");world.Remote=false;world.Site=new Fixture();
  request=new PZAEC.SpawnSafety.Request{Surface=true,Allowed=p=>false};Check(!PZAEC.SpawnSafety.Safety.Accept(Enemy(),request),"trader constraint checked");
  world.Site.Obstacles.Add(new UnityEngine.Bounds(P(y:31),new UnityEngine.Vector3(1,2,1)));world.Native=false;request=new PZAEC.SpawnSafety.Request{Surface=true,Next=()=>P(3)};Check(PZAEC.SpawnSafety.Safety.Accept(Enemy(),request),"native-selector accepted location not subjected to a different water/Y test");world.Native=true;
  world.Site=new Fixture{Available=false};Check(PZAEC.SpawnSafety.Safety.IsSurface(world,P()),"unloaded anchor is not underground permission");world.Site.Available=true;
  Check(PZAEC.SpawnSafety.Safety.IsSurface(world,P(y:0)),"uninitialized anchor is conservative");Check(PZAEC.SpawnSafety.Safety.IsSurface(world,P(y:float.NaN)),"invalid anchor is conservative");
  world.Site.TerrainY=40;Check(!PZAEC.SpawnSafety.Safety.IsSurface(world,P()),"known loaded underground anchor retained");
  world.Site=new Fixture();enemy=Enemy();enemy.physicsHeight=0;enemy.physicsColliderRadius=0;EntityClass.list[1].SizeScale=2;Check(PZAEC.SpawnSafety.Safety.Accept(enemy,new PZAEC.SpawnSafety.Request{Surface=true}),"uninitialized physics fallback");Check(world.Site.LastBody.size.y>4.5f&&world.Site.LastBody.size.x>1.5f,"fallback uses conservative scaled body");
  EntityClass.list[1].SizeScale=1;enemy=Enemy();enemy.boundingBox=new UnityEngine.Bounds(P(),new UnityEngine.Vector3(3,5,3));Check(PZAEC.SpawnSafety.Safety.Accept(enemy,new PZAEC.SpawnSafety.Request{Surface=true}),"scaled capsule accepted");Check(world.Site.LastBody.size.y>4.9f&&world.Site.LastBody.size.x==3,"real scaled bounds retained");
  return "PASS: "+checks+" spawn rules/runtime checks (synthetic world; actual Unity collider mesh needs live validation).";
 }
}
'@
Add-Type -TypeDefinition ($rules+$runtime+$fixtures)
[SpawnRulesTests]::Run()


