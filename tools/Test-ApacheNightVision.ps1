$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$source=Get-Content "$root/99-AEC_T16_RuntimeFix/Source/ApacheNightVision.cs" -Raw
$source=$source -replace '^using UnityEngine;\r?\n',''
$source=$source -replace '\bMathf\.','UnityEngine.Mathf.'
$stub=@'
using System;
using System.Collections.Generic;
namespace UnityEngine {
 public static class Mathf {public static float Abs(float n)=>Math.Abs(n);public static float Clamp01(float n)=>Math.Max(0,Math.Min(1,n));public static float Max(float a,float b)=>Math.Max(a,b);}
}
public class ScreenEffects {
 public sealed class ScreenEffect {public string Name;public float Intensity,TargetIntensity,FadeTime;}
 public static ScreenEffects Instance;
 public List<ScreenEffect> activeEffects=new List<ScreenEffect>();
 public ScreenEffect Find(string name,List<ScreenEffect> list)=>list.Find(x=>x!=null&&x.Name==name);
 public void SetScreenEffect(string name,float target,float fade){var e=Find(name,activeEffects);if(e==null){if(target<=0)return;e=new ScreenEffect{Name=name};activeEffects.Add(e);}e.TargetIntensity=target;e.FadeTime=fade;}
}
'@
$tests=@'
namespace AECT16RuntimeFix {
 public static class NightVisionFixture {
  static int checks;static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
  static ScreenEffects Effects(float? target=null,float fade=.25f){var s=new ScreenEffects();if(target.HasValue)s.activeEffects.Add(new ScreenEffects.ScreenEffect{Name="NightVision",Intensity=target.Value,TargetIntensity=target.Value,FadeTime=fade});ScreenEffects.Instance=s;return s;}
  static ScreenEffects.ScreenEffect Current(ScreenEffects s)=>s.Find("NightVision",s.activeEffects);
  public static void Run(){
   var fresh=Effects();ApacheNightVision.Update(true);Check(ApacheNightVision.Active&&Current(fresh).TargetIntensity==1,"aim enables native night vision");
   ApacheNightVision.Update(false);Check(!ApacheNightVision.Active&&Current(fresh).TargetIntensity==0,"release restores initially-off night vision");
   var native=Effects(.65f,.4f);ApacheNightVision.Update(true);Check(Current(native).TargetIntensity==1,"Apache temporarily overrides existing intensity");
   ApacheNightVision.Clear();Check(Current(native).TargetIntensity==.65f&&Current(native).FadeTime==.4f,"cleanup restores native target and fade");
   var changed=Effects();ApacheNightVision.Update(true);Current(changed).TargetIntensity=0;Current(changed).FadeTime=.3f;ApacheNightVision.Update(true);ApacheNightVision.Update(false);
   Check(Current(changed).TargetIntensity==0&&Current(changed).FadeTime==.3f,"native off choice made while aiming is preserved");
   var old=Effects();ApacheNightVision.Update(true);var replacement=Effects(.4f,.2f);ApacheNightVision.Update(true);
   Check(Current(old).TargetIntensity==0&&Current(replacement).TargetIntensity==1,"screen-effect owner change restores old owner and enables new owner");
   ApacheNightVision.Clear();Check(Current(replacement).TargetIntensity==.4f,"new owner state restores on cleanup");
   Effects();ApacheNightVision.Update(true);ScreenEffects.Instance=null;ApacheNightVision.Update(true);Check(!ApacheNightVision.Active,"missing screen effects safely clears state");
   Console.WriteLine("PASS: "+checks+" Apache automatic night-vision activation and state-restoration checks.");
  }
 }
}
'@
Add-Type -TypeDefinition ($stub+$source+$tests)
[AECT16RuntimeFix.NightVisionFixture]::Run()
