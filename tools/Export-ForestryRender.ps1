$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$files=@('AutoForestryModel.cs','AutoForestryMachinery.cs','AutoForestryWoodworking.cs','AutoForestryRailStock.cs','ForestryMotion.cs','ForestryResources.cs','ForestryPreflight.cs')
$code=Get-Content (Join-Path $PSScriptRoot 'Forestry/RenderShim.cs') -Raw
# Include the actual light-fixture geometry, but exclude Unity's particle/LOD systems.
$presentation=Get-Content (Join-Path $root '99-AEC_T16_RuntimeFix/Source/AutoForestryPresentation.cs') -Raw
$start=$presentation.IndexOf('            var lamp=')
$end=$presentation.IndexOf('            var light=')
$code+="`nnamespace AECT16RuntimeFix { public static class AutoForestryPresentation { public static void Build(UnityEngine.GameObject root,string assets,UnityEngine.Material[] materials,UnityEngine.Material metal,UnityEngine.Material dark){"+$presentation.Substring($start,$end-$start).Replace('new Vector3','new UnityEngine.Vector3')+'}}}'
$output=Join-Path $root 'tools/Forestry/Renders'
New-Item -ItemType Directory -Force $output | Out-Null
$adapter=Join-Path $output 'RenderAdapter.cs'
Set-Content -LiteralPath $adapter -Value $code -Encoding utf8
$sources=@($adapter)+@($files | ForEach-Object {Join-Path $root "99-AEC_T16_RuntimeFix/Source/$_"})
Add-Type -Path $sources
[ForestryRenderExport]::Run((Join-Path $root '98-AECxProjectZ_Tweaks/Resources/Forestry'),(Join-Path $output 'scene.meshbin'))
