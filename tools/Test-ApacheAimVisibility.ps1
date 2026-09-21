$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$source=Get-Content "$root/99-AEC_T16_RuntimeFix/Source/ApacheAimVisibility.cs" -Raw
$source=$source.Replace('using System.Collections.Generic;','').Replace('using UnityEngine;','')
$stub=@'
using System;
using System.Collections.Generic;
using UnityEngine;
namespace UnityEngine {
 public class Renderer {public bool enabled,forceRenderingOff;public Renderer(bool value){enabled=value;}}
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
public class Entity {public Transform transform;}
public class EntityVehicle {public int entityId;public bool Apache=true;public Transform ModelTransform;public Entity[] Occupants=new Entity[2];public Entity GetAttached(int seat)=>Occupants[seat];}
namespace AECT16RuntimeFix {
 public static class ApacheWeapons {
  public sealed class State {public Transform Mesh;}
  static readonly Dictionary<EntityVehicle,State> states=new Dictionary<EntityVehicle,State>();
  public static bool IsApache(EntityVehicle v)=>v!=null&&v.Apache;
  public static State GetState(EntityVehicle v){if(!states.TryGetValue(v,out var s)){s=new State{Mesh=v.ModelTransform};states[v]=s;}return s;}
 }
 public static class AimVisibilityFixture {
  static int checks;static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
  static EntityVehicle Vehicle(int id,Renderer[] renderers,Light[] lights)=>new EntityVehicle{entityId=id,ModelTransform=new Transform{Renderers=renderers,Lights=lights}};
  public static void Run(){
   var visible=new Renderer(true);var nativeHidden=new Renderer(false);var lamp=new Light(true);
   var first=Vehicle(1,new[]{visible,nativeHidden},new[]{lamp});
   ApacheAimVisibility.Update(first,true);
   Check(visible.forceRenderingOff&&nativeHidden.forceRenderingOff&&!lamp.enabled,"aiming suppresses local renderers and lights");
   Check(visible.enabled&&!nativeHidden.enabled,"native renderer enabled state stays intact");
   ApacheAimVisibility.Update(first,false);
   Check(!visible.forceRenderingOff&&!nativeHidden.forceRenderingOff&&visible.enabled&&!nativeHidden.enabled&&lamp.enabled,"release restores each original visibility state");
   var secondRenderer=new Renderer(true);var second=Vehicle(2,new[]{secondRenderer},new Light[0]);
   ApacheAimVisibility.Update(first,true);ApacheAimVisibility.Update(second,true);
   Check(!visible.forceRenderingOff&&secondRenderer.forceRenderingOff,"vehicle switch restores old aircraft before hiding new one");
   ApacheAimVisibility.Clear();Check(!secondRenderer.forceRenderingOff,"world cleanup restores hidden aircraft");
   var pilotBody=new Renderer(true);var gunnerBody=new Renderer(true);var equipment=new Renderer(true);
   var alreadySuppressed=new Renderer(true){forceRenderingOff=true};var helmetLamp=new Light(true);
   var pilot=new Entity{transform=new Transform{Renderers=new[]{pilotBody,alreadySuppressed},Lights=new[]{helmetLamp}}};
   var gunner=new Entity{transform=new Transform{Renderers=new[]{gunnerBody,equipment}}};
   var bystander=new Renderer(true);
   first.Occupants[0]=pilot;first.Occupants[1]=gunner;
   ApacheAimVisibility.Update(first,true);
   Check(pilotBody.forceRenderingOff&&gunnerBody.forceRenderingOff&&equipment.forceRenderingOff&&!helmetLamp.enabled,"independent pilot gunner and equipment trees hidden");
   Check(!bystander.forceRenderingOff,"unattached players are unaffected");
   pilotBody.enabled=false;ApacheAimVisibility.Update(first,true);pilotBody.enabled=true;
   Check(pilotBody.forceRenderingOff,"native animation toggles cannot reveal occupant");
   var replacement=new Renderer(true);gunner.transform.Renderers=new[]{gunnerBody,replacement};
   ApacheAimVisibility.Update(first,true);
   Check(!equipment.forceRenderingOff&&replacement.forceRenderingOff,"equipment change restores old item and hides new item");
   first.Occupants[0]=null;ApacheAimVisibility.Update(first,true);
   Check(!pilotBody.forceRenderingOff&&alreadySuppressed.forceRenderingOff&&helmetLamp.enabled,"dismount restores passenger immediately while other seat keeps aiming");
   Check(gunnerBody.forceRenderingOff&&visible.forceRenderingOff,"remaining occupant and aircraft stay hidden");
   first.Occupants[0]=gunner;first.Occupants[1]=pilot;ApacheAimVisibility.Update(first,true);
   Check(pilotBody.forceRenderingOff&&gunnerBody.forceRenderingOff,"seat swap remains hidden");
   gunnerBody.enabled=false;ApacheAimVisibility.Update(first,false);
   Check(!gunnerBody.forceRenderingOff&&!gunnerBody.enabled&&!pilotBody.forceRenderingOff,"release preserves animation state changed during aim");
   Check(alreadySuppressed.forceRenderingOff,"original force-rendering suppression restored exactly");
   var noMesh=Vehicle(3,new Renderer[0],new Light[0]);noMesh.ModelTransform=null;noMesh.Occupants[0]=pilot;
   ApacheAimVisibility.Update(noMesh,true);Check(pilotBody.forceRenderingOff,"missing aircraft mesh does not skip occupants");
   ApacheAimVisibility.Clear();Check(!pilotBody.forceRenderingOff&&helmetLamp.enabled,"world cleanup restores occupants and equipment lights");
   first.Apache=false;ApacheAimVisibility.Update(first,true);
   Check(!pilotBody.forceRenderingOff&&!visible.forceRenderingOff,"MD500 and other vehicles never enter Apache aim suppression");
   first.Apache=true;ApacheAimVisibility.Update(first,true);ApacheAimVisibility.Update(null,false);
   Check(!pilotBody.forceRenderingOff&&!replacement.forceRenderingOff,"leaving vehicle restores every tracked object");
   Console.WriteLine("PASS: "+checks+" aim visibility hide/restore/state-preservation checks.");
  }
 }
}
'@
Add-Type -TypeDefinition ($stub+$source)
[AECT16RuntimeFix.AimVisibilityFixture]::Run()
