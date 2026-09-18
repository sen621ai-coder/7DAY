#Requires -Version 7.0
param([string]$Archive='C:/Users/yf/Downloads/real-time-bones-demo-phoenix-bird.zip',[string]$Project='E:/soft/7DTD-Modding/PhoenixBoss/Unity',[string]$Editor='E:/soft/unity20223/Editor/Unity.exe')
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force "$Project/Assets/Editor","$Project/Assets/Phoenix","$Project/Packages","$Project/ProjectSettings" | Out-Null
$source=Join-Path (Split-Path $Project) 'Source'
Expand-Archive -LiteralPath $Archive -DestinationPath $source -Force
Copy-Item -LiteralPath "$source/source/fly.fbx" -Destination "$Project/Assets/Phoenix/fly.fbx" -Force
Get-ChildItem -LiteralPath "$source/textures" -Filter '*.png' | Copy-Item -Destination "$Project/Assets/Phoenix" -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PhoenixBoss/PhoenixAssetBuild.cs') -Destination "$Project/Assets/Editor/PhoenixAssetBuild.cs" -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PhoenixBoss/PhoenixFeathers.shader') -Destination "$Project/Assets/Phoenix/PhoenixFeathers.shader" -Force
'm_EditorVersion: 2022.3.62f2' | Set-Content "$Project/ProjectSettings/ProjectVersion.txt"
'{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.animation":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.particlesystem":"1.0.0","com.unity.modules.imageconversion":"1.0.0"}}' | Set-Content "$Project/Packages/manifest.json"
$process=Start-Process -FilePath $Editor -ArgumentList @('-batchmode','-nographics','-quit','-projectPath',('"'+$Project+'"'),'-executeMethod','PhoenixAssetBuild.Build','-logFile',('"'+$Project+'/build.log"')) -WindowStyle Hidden -PassThru -Wait
if($process.ExitCode -ne 0){throw "Phoenix asset build failed: $Project/build.log"}
Get-Content "$Project/Build/verified.txt"
Write-Output "Bundle: $Project/Build/phoenix.unity3d"
