$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
$source=Get-Content "$root/ZZZ-PZAEC_WallStudBarrier/Source/StudNavigation.cs" -Raw
$source=$source.Substring(0,$source.IndexOf('    public static class Navigation'))+"}`n"
$fixture=@'
namespace HarmonyLib {} namespace GamePath {}
namespace UnityEngine {
 public struct Vector3 {public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);}
 public static class Mathf {public static int FloorToInt(float f)=>(int)System.Math.Floor(f);}
}
namespace Pathfinding {
 public class Path {}
 public class ABPath:Path {public GraphNode startNode;}
 public class GraphNode {public UnityEngine.Vector3 position;public bool Walkable=true;}
 public struct Connection {public GraphNode node;}
 public interface ITraversalProvider {bool CanTraverse(Path p,GraphNode n,int d);bool CanTraverseConnection(Path p,Connection c);uint GetTraversalCost(Path p,GraphNode n);}
}
public struct BlockValue {public bool IsStud;}
public class ChunkCache {
 public System.Collections.Generic.HashSet<string> Studs=new System.Collections.Generic.HashSet<string>();
 public BlockValue GetBlock(int x,int y,int z)=>new BlockValue{IsStud=Studs.Contains(x+","+y+","+z)};
 public void Put(int x,int y,int z)=>Studs.Add(x+","+y+","+z);
}
namespace PZAEC.WallStudBarrier {public static class Barrier {public static bool Stud(BlockValue b)=>b.IsStud;}}
public class NativeTraversal:Pathfinding.ITraversalProvider {
 public bool CanTraverse(Pathfinding.Path p,Pathfinding.GraphNode n,int d)=>n.Walkable;
 public bool CanTraverseConnection(Pathfinding.Path p,Pathfinding.Connection c)=>c.node.Walkable;
 public uint GetTraversalCost(Pathfinding.Path p,Pathfinding.GraphNode n)=>123;
}
public static class RouteTests {
 static int count;static void Check(bool b,string label){count++;if(!b)throw new System.Exception(label);}
 static Pathfinding.GraphNode Node(float x,float y,float z)=>new Pathfinding.GraphNode{position=new UnityEngine.Vector3(x,y,z)};
 static System.Collections.Generic.List<int> Search(Pathfinding.ITraversalProvider provider){
  var p=new Pathfinding.Path();var queue=new System.Collections.Generic.Queue<int>();var parent=new System.Collections.Generic.Dictionary<int,int>();
  int start=3*13,goal=3*13+12;queue.Enqueue(start);parent[start]=-1;
  while(queue.Count>0){int current=queue.Dequeue();if(current==goal){var result=new System.Collections.Generic.List<int>();for(int c=current;c!=-1;c=parent[c])result.Add(c);return result;}
   int x=current%13,z=current/13;foreach(int delta in new[]{1,-1,13,-13}){int next=current+delta,nx=next%13,nz=next/13;if(next<0||next>=91||System.Math.Abs(nx-x)+System.Math.Abs(nz-z)!=1||parent.ContainsKey(next))continue;
    var node=Node(nx+.5f,0,nz+.5f);if(!provider.CanTraverse(p,node,-1)||!provider.CanTraverseConnection(p,new Pathfinding.Connection{node=node}))continue;parent[next]=current;queue.Enqueue(next);}
  }return null;
 }
 public static void Run(){
  var original=new NativeTraversal();var cache=new ChunkCache();for(int z=1;z<=5;z++){cache.Put(6,0,z);cache.Put(6,1,z);}
  var provider=new PZAEC.WallStudBarrier.StudTraversal(original,cache,new UnityEngine.Vector3(0,0,0),.3f,1.8f);
  var before=Search(original);Check(before.Count==13,"baseline chooses direct breakable route");
  var after=Search(provider);Check(after!=null&&after.Count>before.Count,"alternate route is selected during search, not after an attack");
  foreach(int cell in after)Check(!(cell%13==6&&cell/13>=1&&cell/13<=5),"detour never enters stud cells");
  bool side=false;foreach(int cell in after)if(cell%13==6&&(cell/13==0||cell/13==6))side=true;Check(side,"route uses actual side opening");
  for(int z=0;z<7;z++){cache.Put(6,0,z);cache.Put(6,1,z);}
  provider=new PZAEC.WallStudBarrier.StudTraversal(original,cache,new UnityEngine.Vector3(0,0,0),.3f,1.8f);Check(Search(provider)==null,"closed barrier is never treated as break-through route");
  cache.Studs.Clear();provider=new PZAEC.WallStudBarrier.StudTraversal(original,cache,new UnityEngine.Vector3(0,0,0),.3f,1.8f);Check(Search(provider).Count==13,"next search observes removed studs");
  cache.Put(100,1,-100);provider=new PZAEC.WallStudBarrier.StudTraversal(original,cache,new UnityEngine.Vector3(100,0,-100),.3f,1.8f);var path=new Pathfinding.Path();
  Check(!provider.CanTraverse(path,Node(.5f,0,.5f),-1),"floating origin and head-height stud");
  var low=new PZAEC.WallStudBarrier.StudTraversal(original,cache,new UnityEngine.Vector3(100,0,-100),.3f,.8f);Check(low.CanTraverse(path,Node(.5f,0,.5f),-1),"short entity may use genuine clearance underneath");
  var blocked=Node(9.5f,0,9.5f);blocked.Walkable=false;Check(!provider.CanTraverse(path,blocked,0),"native impassable stays impassable");Check(provider.GetTraversalCost(path,blocked)==123,"other terrain costs preserved");
  Check(PZAEC.WallStudBarrier.RouteRules.Occupied(new UnityEngine.Vector3(99.8f,0,-99.5f),.3f,1.8f,(x,y,z)=>cache.GetBlock(x,y,z).IsStud),"body radius rejects corner squeeze");
  var startNode=Node(.5f,0,.5f);var touching=new Pathfinding.ABPath{startNode=startNode};
  Check(provider.CanTraverse(touching,startNode,-1),"touching stud start node may escape");
  Check(!provider.CanTraverse(new Pathfinding.Path(),startNode,-1),"same node remains blocked for a different search");
  Check(!provider.CanTraverse(touching,Node(.55f,0,.5f),-1),"start exemption does not spill into neighbouring nodes");
  startNode.Walkable=false;Check(!provider.CanTraverse(touching,startNode,-1),"start exemption preserves native unwalkability");
  System.Console.WriteLine("PASS "+count+" navigation/detour checks using actual traversal wrapper and body occupancy code.");
 }
}
'@
Add-Type -TypeDefinition ($source+"`n"+$fixture)
[RouteTests]::Run()
Add-Type -Path "$root/0_TFP_Harmony/Mono.Cecil.dll"
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path (Split-Path $root) '7DaysToDie_Data/Managed/Assembly-CSharp.dll'))
$type=$a.MainModule.Types|Where-Object Name -eq 'ASPPathFinder'
$calculate=$type.Methods|Where-Object Name -eq 'Calculate'
$calls=@($calculate.Body.Instructions|Where-Object {($_.Operand -as [string]) -eq 'System.Void AstarPath::StartPath(Pathfinding.Path,System.Boolean)'})
if($calls.Count -ne 1){throw 'Native path queue injection point changed'}
if(!(($type.Methods|Where-Object Name -eq 'IsLineClear').Parameters.Count -eq 3)){throw 'Native smoothing hook changed'}
$ctor=($a.MainModule.Types|Where-Object Name -eq 'PathFinder').Fields|Where-Object Name -eq 'pathInfo'
if(!$ctor){throw 'Path ownership field changed'}
$a.Dispose()
Write-Output 'PASS native queue injection point, smoothing signature and path ownership field.'
