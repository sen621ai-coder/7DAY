$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
$source=Get-Content "$root/ZZZ-PZAEC_WallStudBarrier/Source/WallStudBarrier.cs" -Raw
$source=$source.Substring(0,$source.IndexOf('    public sealed class ModApi'))+"}`n"
$fixture=@'
namespace HarmonyLib {}
namespace UnityEngine {
 public struct Vector3 {public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}public float sqrMagnitude=>x*x+y*y+z*z;public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}
 public static class Mathf {public static int FloorToInt(float f)=>(int)System.Math.Floor(f);public static float Clamp(float f,float a,float b)=>System.Math.Max(a,System.Math.Min(b,f));}
}
public struct Vector3i {public int x,y,z;public Vector3i(int a,int b,int c){x=a;y=b;z=c;}public override string ToString()=>x+","+y+","+z;}
public class Entity {public int entityClass;public UnityEngine.Vector3 position;public float physicsHeight=1;}
public class EntityAlive:Entity {public WorldBase world;public EntityMoveHelper moveHelper;}
public class EntityPlayer:EntityAlive {}
public class EntityClass {public bool bIsEnemyEntity;public static System.Collections.Generic.Dictionary<int,EntityClass> list=new System.Collections.Generic.Dictionary<int,EntityClass>();}
public class Block {public string Shape;public string GetAutoShapeShapeName()=>Shape;}
public struct BlockValue {public Block Block;}
public class BlockValueRef {public Vector3i BlockPosition;}
public struct HitInfoDetails {public Vector3i blockPos;}
public class WorldRayHitInfo {public bool bHitValid;public HitInfoDetails hit;}
public class EntityMoveHelper {public WorldRayHitInfo HitInfo;}
public class WorldBase {
 public EntityAlive Attacker;public System.Collections.Generic.Dictionary<string,BlockValue> Blocks=new System.Collections.Generic.Dictionary<string,BlockValue>();
 public BlockValue GetBlock(int x,int y,int z)=>GetBlock(new Vector3i(x,y,z));
 public BlockValue GetBlock(Vector3i p)=>Blocks.TryGetValue(p.ToString(),out var v)?v:new BlockValue();
 public Entity GetEntity(int id)=>id==1?Attacker:null;
 public void Put(int x,int y,int z,string shape){Blocks[new Vector3i(x,y,z).ToString()]=new BlockValue{Block=new Block{Shape=shape}};}
}
public static class BarrierTests {
 static int count;static void Check(bool v,string s){count++;if(!v)throw new System.Exception(s);}
 public static void Run(){
  EntityClass.list[1]=new EntityClass{bIsEnemyEntity=true};EntityClass.list[2]=new EntityClass();
  foreach(int axis in new[]{0,1,2})foreach(int sign in new[]{-1,1}){
   var world=new WorldBase();var target=new Vector3i(0,0,0);int x=axis==0?sign:0,y=axis==1?sign:0,z=axis==2?sign:0;world.Put(x,y,z,"wallStud");
   var enemy=new EntityAlive{entityClass=1,world=world,position=new UnityEngine.Vector3(.5f+3*x,3*y,.5f+3*z)};world.Attacker=enemy;
   Check(PZAEC.WallStudBarrier.Barrier.Protected(world,enemy,target),"six-direction barrier "+axis+" "+sign);
   enemy.moveHelper=new EntityMoveHelper{HitInfo=new WorldRayHitInfo{bHitValid=true,hit=new HitInfoDetails{blockPos=target}}};
   bool result=true;PZAEC.WallStudBarrier.Barrier.CanBreakPostfix(enemy,ref result);Check(!result,"AI does not select shielded wall");
   Check(!PZAEC.WallStudBarrier.Barrier.BreakPrefix(enemy),"existing break task is stopped");result=true;Check(!PZAEC.WallStudBarrier.Barrier.AttackPrefix(enemy,ref result)&&!result,"attack animation admission blocked");
   result=true;PZAEC.WallStudBarrier.Barrier.FindDestroyPostfix(enemy,new UnityEngine.Vector3(.5f,.5f,.5f),ref result);Check(!result,"destroy-area target rejected");
   int damage=99;Check(!PZAEC.WallStudBarrier.Barrier.DamagePrefix(world,new BlockValueRef{BlockPosition=target},1,ref damage)&&damage==0,"fallback does not damage or drop loot");
   world.Blocks.Clear();Check(PZAEC.WallStudBarrier.Barrier.BreakPrefix(enemy),"removing stud immediately restores attacks");
   world.Put(x,y,z,"wallStudBroken");Check(!PZAEC.WallStudBarrier.Barrier.Protected(world,enemy,target),"broken shape not protected");
   world.Put(x,y,z,"wallStud");enemy.entityClass=2;Check(!PZAEC.WallStudBarrier.Barrier.Protected(world,enemy,target),"friendly entity excluded");
   var player=new EntityPlayer{entityClass=1,world=world,position=enemy.position};Check(!PZAEC.WallStudBarrier.Barrier.Protected(world,player,target),"player may dismantle");
  }
  var w=new WorldBase();w.Put(0,0,0,"wallStud");var e=new EntityAlive{entityClass=1,world=w,position=new UnityEngine.Vector3(-2,0,.5f)};w.Attacker=e;
  Check(PZAEC.WallStudBarrier.Barrier.Protected(w,e,new Vector3i(0,0,0)),"direct stud damage rejected");
  Check(!PZAEC.WallStudBarrier.Barrier.Protected(w,e,new Vector3i(-3,0,0)),"wall on attacker side remains vulnerable");
  Check(!PZAEC.WallStudBarrier.Barrier.Protected(w,e,new Vector3i(0,0,5)),"uncovered side remains vulnerable");
  int d=0;Check(PZAEC.WallStudBarrier.Barrier.DamagePrefix(w,new BlockValueRef{BlockPosition=new Vector3i(0,0,0)},-1,ref d),"unknown/environment source unchanged");
  Check(!PZAEC.WallStudBarrier.Barrier.Crosses(new UnityEngine.Vector3(0,0,0),new UnityEngine.Vector3(0,0,0),(x,y,z)=>false),"zero-length traversal terminates");
  Check(PZAEC.WallStudBarrier.Barrier.Crosses(new UnityEngine.Vector3(-2.5f,-2.5f,-2.5f),new UnityEngine.Vector3(.5f,.5f,.5f),(x,y,z)=>x==-1&&y==-1&&z==-1),"negative coordinate diagonal traversal");
  System.Console.WriteLine("PASS "+count+" actual-source barrier behavior checks.");
 }
}
'@
Add-Type -TypeDefinition ($source+"`n"+$fixture)
[BarrierTests]::Run()
Add-Type -Path "$root/0_TFP_Harmony/Mono.Cecil.dll"
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path (Split-Path $root) '7DaysToDie_Data/Managed/Assembly-CSharp.dll'))
foreach($pair in @(@('EAIBreakBlock','CanExecute',0),@('EAIBreakBlock','AttackBlock',0),@('EntityAlive','Attack',1),@('EntityMoveHelper','FindDestroyPos',3),@('Block','DamageBlock',8))){
 $type=$a.MainModule.Types|Where-Object Name -eq $pair[0]
 $method=@($type.Methods|Where-Object {$_.Name -eq $pair[1] -and $_.Parameters.Count -eq $pair[2]})
 if($method.Count -ne 1){throw "Native hook changed: $($pair[0]).$($pair[1])"}
}
foreach($pair in @(@('EAIBase','theEntity'),@('EntityMoveHelper','entity'))){if(!(($a.MainModule.Types|Where-Object Name -eq $pair[0]).Fields|Where-Object Name -eq $pair[1])){throw 'Native injected field changed'}}
$shape=($a.MainModule.Types|Where-Object Name -eq 'Block').Methods|Where-Object Name -eq 'GetAutoShapeShapeName'
if(!($shape.Body.Instructions|Where-Object {($_.Operand -as [string]) -match 'autoShapeShapeName'})){throw 'Shape identity implementation changed'}
$a.Dispose()
Write-Output 'PASS installed native hook signatures, inherited AI fields and shape identity.'
