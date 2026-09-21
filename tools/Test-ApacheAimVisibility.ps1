$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$source=Get-Content "$root/99-AEC_T16_RuntimeFix/Source/ApacheAimVisibility.cs" -Raw
$source=$source.Replace('using System.Collections.Generic;','').Replace('using UnityEngine;','')
$stub=@'
using System;
using System.Collections.Generic;
using UnityEngine;
namespace UnityEngine {
 public class Renderer {public bool enabled;public Renderer(bool value){enabled=value;}}
 public class Light {public bool enabled;public Light(bool value){enabled=value;}}
 public class Transform {
  public Renderer[] Renderers=new Renderer[0];public Light[] Lights=new Light[0];
  public T[] GetComponentsInChildren<T>(bool inactive){
   if(typeof(T)==typeof(Renderer))return (T[])(object)Renderers;
   if(typeof(T)==typeof(Light))return (T[])(object)Lights;
   return new T[0];
  }
 }
}
public class EntityVehicle {public int entityId;public Transform ModelTransform;}
namespace AECT16RuntimeFix {
 public static class ApacheWeapons {
  public sealed class State {public Transform Mesh;}
  static readonly Dictionary<EntityVehicle,State> states=new Dictionary<EntityVehicle,State>();
  public static bool IsApache(EntityVehicle v)=>v!=null;
  public static State GetState(EntityVehicle v){if(!states.TryGetValue(v,out var s)){s=new State{Mesh=v.ModelTransform};states[v]=s;}return s;}
 }
 public static class AimVisibilityFixture {
  static int checks;static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
  static EntityVehicle Vehicle(int id,Renderer[] renderers,Light[] lights)=>new EntityVehicle{entityId=id,ModelTransform=new Transform{Renderers=renderers,Lights=lights}};
  public static void Run(){
   var visible=new Renderer(true);var nativeHidden=new Renderer(false);var lamp=new Light(true);
   var first=Vehicle(1,new[]{visible,nativeHidden},new[]{lamp});
   ApacheAimVisibility.Update(first,true);
   Check(!visible.enabled&&!nativeHidden.enabled&&!lamp.enabled,"aiming hides local renderers and lights");
   ApacheAimVisibility.Update(first,false);
   Check(visible.enabled&&!nativeHidden.enabled&&lamp.enabled,"release restores each original visibility state");
   var secondRenderer=new Renderer(true);var second=Vehicle(2,new[]{secondRenderer},new Light[0]);
   ApacheAimVisibility.Update(first,true);ApacheAimVisibility.Update(second,true);
   Check(visible.enabled&&!secondRenderer.enabled,"vehicle switch restores old aircraft before hiding new one");
   ApacheAimVisibility.Clear();Check(secondRenderer.enabled,"world cleanup restores hidden aircraft");
   Console.WriteLine("PASS: "+checks+" aim visibility hide/restore/state-preservation checks.");
  }
 }
}
'@
Add-Type -TypeDefinition ($stub+$source)
[AECT16RuntimeFix.AimVisibilityFixture]::Run()
