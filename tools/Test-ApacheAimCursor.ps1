$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$cursor=Get-Content "$root/99-AEC_T16_RuntimeFix/Source/ApacheAimCursor.cs" -Raw
$weapons=Get-Content "$root/99-AEC_T16_RuntimeFix/Source/ApacheWeapons.cs" -Raw
$start=$cursor.IndexOf('        public static Vector2 Advance(')
$end=$cursor.IndexOf('        public static void Update(',$start)
if($start-lt 0-or $end-lt 0){throw 'Floating sight method missing'}
$method=$cursor.Substring($start,$end-$start)
$fixture=@'
using System;
using UnityEngine;
namespace UnityEngine {
 public struct Vector2 {public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}}
 public static class Mathf {public static float Clamp(float n,float a,float b)=>Math.Max(a,Math.Min(b,n));}
}
namespace AECT16RuntimeFix {
 public static class CursorFixture {
'@
$tests=@'
  static int checks;static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
  public static void Run(){
   var p=Advance(new UnityEngine.Vector2(.5f,.64f),1,-1);
   Check(Math.Abs(p.x-.514f)<.0001&&Math.Abs(p.y-.626f)<.0001,"mouse delta moves floating sight in both axes");
   p=Advance(p,100,100);Check(p.x==.84f&&p.y==.84f,"upper and side bounds keep sight on screen");
   p=Advance(p,-100,-100);Check(p.x==.16f&&p.y==.36f,"lower bound keeps sight above vehicle HUD");
   Console.WriteLine("PASS: "+checks+" floating sight movement and bounds checks.");
  }
 }
}
'@
Add-Type -TypeDefinition ($fixture+$method+$tests)
[AECT16RuntimeFix.CursorFixture]::Run()
if($weapons -notmatch 'return ApacheAimCursor\.Ray\(player\)'){throw 'Weapon sight ray does not use floating sight'}
if($weapons -notmatch 'ApacheAimCursor\.Update\(v,seat\)'){throw 'Local input does not update floating sight'}
Write-Output 'PASS: pilot, gunner, mark and fire requests share ApacheWeapons.SightRay.'
