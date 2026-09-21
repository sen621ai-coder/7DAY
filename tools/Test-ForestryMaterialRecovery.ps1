$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$source=Get-Content (Join-Path $root '99-AEC_T16_RuntimeFix/Source/AutoForestryActivity.cs') -Raw
# Execute the actual material initialization block with a strict null-copy stub.
# This reproduces the Material(source:null) exception in the supplied game log.
$start=$source.IndexOf('            if(logRenderers.Count>0)')
$end=$source.IndexOf('            if(blade==null', $start)
if($start -lt 0 -or $end -lt $start){throw 'Material initialization block not found'}
$body=$source.Substring($start,$end-$start)
$fixture=@'
using System;
using System.Collections.Generic;
namespace UnityEngine.Rendering { public enum BlendMode { SrcAlpha, OneMinusSrcAlpha } }
public class Material {
 public string name; public int renderQueue;
 public Material(){} public Material(Material source){if(source==null)throw new ArgumentNullException("source");}
 public void SetFloat(string n,float v){} public void SetInt(string n,int v){}
 public void SetOverrideTag(string n,string v){} public void EnableKeyword(string n){} public void DisableKeyword(string n){}
}
public class Renderer { public Material sharedMaterial; }
public static class AutoForestryModel { public static readonly Material Original=new Material(); public static Material GetTimberMaterial()=>Original; }
public class ForestryMaterialFixture {
 List<Renderer> logRenderers=new List<Renderer>(); Material solidLog;
 static void Destroy(Material m){}
 void Initialize(){ BODY }
 public static void Run(){
  var a=new ForestryMaterialFixture(); a.Initialize();
  for(int i=0;i<5;i++)a.logRenderers.Add(new Renderer());
  a.Initialize();
  foreach(var r in a.logRenderers)if(r.sharedMaterial!=AutoForestryModel.Original)throw new Exception("Null material not restored");
  var b=new ForestryMaterialFixture();
  b.logRenderers.Add(new Renderer{sharedMaterial=new Material()}); b.Initialize();
  if(b.solidLog!=AutoForestryModel.Original)throw new Exception("Unexpected material adopted");
  a.logRenderers[0].sharedMaterial=null; a.Initialize();
  if(a.logRenderers[0].sharedMaterial!=AutoForestryModel.Original)throw new Exception("Reinitialization failed");
 }
}
'@
Add-Type -TypeDefinition $fixture.Replace('BODY',$body)
[ForestryMaterialFixture]::Run()
Write-Output 'PASS: empty/null timber materials, five log renderers, canonical shared material, repeated initialization.'
