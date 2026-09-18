$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$source=Get-Content "$root/99-AEC_T16_RuntimeFix/Source/ApacheWeapons.cs" -Raw
$start=$source.IndexOf('        public static byte ResolveAim(')
$end=$source.IndexOf('        private static bool ValidSight(',$start)
$method=$source.Substring($start,$end-$start)
$stub=@'
using System;
using UnityEngine;
namespace UnityEngine {
 public struct Vector3 {
  public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}
  public float sqrMagnitude=>x*x+y*y+z*z;public Vector3 normalized=>this*(1f/(float)Math.Sqrt(sqrMagnitude));public void Normalize(){this=normalized;}
  public static Vector3 operator+(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
  public static Vector3 operator-(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
  public static Vector3 operator*(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);
 }
}
namespace AECT16RuntimeFix {
 public static class AimFixture {
  public class State {public object Vehicle;}
  public class Hit {public Point hit=new Point();} public class Point {public Vector3 pos;}
  public static object currentWorld=new object();
  static int traceCalls;static bool targetHit,blocked;static Vector3 target;
  static Vector3 CannonPivot(State s)=>new Vector3(0,0,0);
  static bool InArc(object v,Vector3 d)=>ApacheWeaponRules.InArc(d.x,d.y,d.z);
  static bool Trace(object v,Vector3 p,Vector3 d,float r,out Hit hit){traceCalls++;hit=new Hit();hit.hit.pos=target;return traceCalls==1?targetHit:blocked;}
'@
$tests=@'
  static int checks;static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
  static byte Run(bool hasTarget,bool obstruction,Vector3 point,out Vector3 aim,out Vector3 muzzle){traceCalls=0;targetHit=hasTarget;blocked=obstruction;target=point;return ResolveAim(new State(),new Vector3(0,2,-5),new Vector3(0,0,1),out aim,out muzzle);}
  public static void Tests(){
   Vector3 d,m;
   Check(Run(true,false,new Vector3(0,0,20),out d,out m)==0,"camera target converges from offset sight");
   Check(Math.Abs(d.y)<.0001&&Math.Abs(m.z-ApacheWeaponRules.MuzzleOffset)<.0001,"damage starts at muzzle on target line, not camera parallel line");
   Check(Run(true,true,new Vector3(0,0,20),out d,out m)==2,"wall between pivot and muzzle prevents firing through it");
   Check(Run(true,false,new Vector3(0,0,.5f),out d,out m)==2,"target behind muzzle is blocked");
   Check(Run(true,false,new Vector3(0,20,2),out d,out m)==1,"corrected barrel direction still obeys mechanical arc");
   Check(Run(false,false,new Vector3(),out d,out m)==0&&d.y>0,"sky aim uses finite sight range and camera offset");
   Check(ResolveAim(new State(),new Vector3(),new Vector3(float.NaN,0,1),out d,out m)==1,"invalid direction rejected before raycast");
   currentWorld=null;Check(Run(false,false,new Vector3(),out d,out m)==1,"missing world rejects aim");
   Console.WriteLine("PASS: "+checks+" actual ResolveAim geometry/obstruction checks with mocked world raycasts; not an in-game physics test.");
  }
 }
}
'@
$rules=Get-Content "$root/99-AEC_T16_RuntimeFix/Source/ApacheWeaponRules.cs" -Raw
Add-Type -TypeDefinition ($stub+$method+$tests+$rules.Replace('using System;',''))
[AECT16RuntimeFix.AimFixture]::Tests()
