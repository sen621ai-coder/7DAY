#Requires -Version 7.0
param([string]$ModApiSource)
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
if(!$ModApiSource){$ModApiSource=Join-Path $root 'ZZZ-PZAEC_Surveillance/Source/ModApi.cs'}
$source=Get-Content $ModApiSource -Raw
$start=$source.IndexOf('public static void BeforePoolDestroy(')
$end=$source.IndexOf('static void Start(', $start)
$method=$source.Substring($start,$end-$start)
# Execute the real cleanup prefix against an object-tree double. Native graphics and
# world occupancy are covered separately by PlacementGameQA when a game run is available.
$prefix='public static class CleanupUnderTest {'+$method+'}'
Add-Type -TypeDefinition ($prefix+@'
public sealed class Material {public bool Alive=true;}
public sealed class Renderer {public Material[] sharedMaterials;}
public sealed class GameObject {
 public bool Active=true;
 public readonly System.Collections.Generic.List<object> Components=new System.Collections.Generic.List<object>();
 public readonly System.Collections.Generic.List<GameObject> Children=new System.Collections.Generic.List<GameObject>();
 public T GetComponent<T>() where T:class {foreach(var c in Components)if(c is T)return (T)c;return null;}
 public T[] GetComponentsInChildren<T>(bool includeInactive) {
  var result=new System.Collections.Generic.List<T>();
  if(includeInactive||Active){foreach(var c in Components)if(c is T)result.Add((T)c);foreach(var child in Children)result.AddRange(child.GetComponentsInChildren<T>(includeInactive));}
  return result.ToArray();
 }
}
public sealed class ScreenView {
 public bool Retiring;public GameObject Owner;
 public T[] GetComponentsInChildren<T>(bool inactive)=>Owner.GetComponentsInChildren<T>(inactive);
}
public static class PreviewLifetimeTests {
 static void Check(bool value,string message){if(!value)throw new System.Exception(message);}
 public static void Run(){
  foreach(bool wrapped in new[]{false,true})foreach(bool active in new[]{false,true}){
   var shared=new Material();var screen=new GameObject{Active=active};
   var view=new ScreenView{Owner=screen};screen.Components.Add(view);
   var body=new GameObject();body.Components.Add(new Renderer{sharedMaterials=new[]{shared}});screen.Children.Add(body);
   var root=screen;Material unrelated=null;
   if(wrapped){root=new GameObject();var inner=new GameObject();root.Children.Add(inner);inner.Children.Add(screen);unrelated=new Material();root.Components.Add(new Renderer{sharedMaterials=new[]{unrelated}});}
   var placed=new Renderer{sharedMaterials=new[]{shared}};
   CleanupUnderTest.BeforePoolDestroy(root);
   // Native GameObjectPool cleanup destroys every remaining runtime material.
   foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))foreach(var material in renderer.sharedMaterials)material.Alive=false;
   Check(placed.sharedMaterials[0].Alive,"Placed screen material destroyed by preview cleanup");
   Check(view.Retiring,"Retirement marker missing");
   if(wrapped)Check(!unrelated.Alive,"Unrelated wrapper renderers were modified");
  }
  System.Console.WriteLine("PASS production cleanup prefix: direct/wrapped, active/inactive previews preserve placed screen materials; unrelated cleanup unchanged");
 }
}
'@)
[PreviewLifetimeTests]::Run()
