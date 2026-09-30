#Requires -Version 7.0
[CmdletBinding()]
param([string]$Editor='D:/unity/2022.3.48f1c1/Editor/Unity.exe')
$ErrorActionPreference='Stop'
$project=Join-Path $PSScriptRoot 'artifacts/UnityProject'
$modRoot=Join-Path (Split-Path (Split-Path (Split-Path $PSScriptRoot))) 'ZZZ-PZAEC_Fishing'
if(!(Test-Path -LiteralPath $Editor)){throw "Unity editor missing: $Editor"}
New-Item -ItemType Directory -Force -Path "$project/Assets/Editor","$project/Packages","$project/ProjectSettings","$project/Logs" | Out-Null
if(!(Test-Path "$project/ProjectSettings/ProjectVersion.txt")){'m_EditorVersion: 2022.3.48f1c1' | Set-Content "$project/ProjectSettings/ProjectVersion.txt"}
# Seed existing asset metadata so a clean machine does not regenerate all GUIDs.
New-Item -ItemType Directory -Force "$project/Assets/Fishing" | Out-Null
Copy-Item "$modRoot/ArtSource/Generated/*" "$project/Assets/Fishing" -Recurse -Force
'{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.audio":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.animation":"1.0.0","com.unity.modules.physics":"1.0.0"}}' | Set-Content "$project/Packages/manifest.json"
Copy-Item -LiteralPath "$PSScriptRoot/FishingAssetBuild.cs" -Destination "$project/Assets/Editor/FishingAssetBuild.cs" -Force
Copy-Item -LiteralPath "$PSScriptRoot/FishingRealismAssets.cs" -Destination "$project/Assets/Editor/FishingRealismAssets.cs" -Force
$started=[DateTime]::UtcNow
$argsList=@('-batchmode','-quit','-projectPath',('"'+$project+'"'),'-executeMethod','FishingAssetBuild.Build','-logFile',('"'+$project+'/Logs/build.log"'))
$proc=Start-Process -FilePath $Editor -ArgumentList $argsList -WindowStyle Hidden -PassThru
$proc.WaitForExit()
$stamp="$project/Build/verified.txt"
if($proc.ExitCode -ne 0 -or !(Test-Path $stamp) -or (Get-Item $stamp).LastWriteTimeUtc -lt $started){throw "Fishing asset build failed; see $project/Logs/build.log"}
New-Item -ItemType Directory -Force -Path "$modRoot/Resources","$modRoot/ArtSource/Generated" | Out-Null
Copy-Item "$project/Build/fishing-presentation.unity3d" "$modRoot/Resources/fishing-presentation.unity3d" -Force
# Keep native mesh/material/prefab sources reproducible and separate from installed resources.
Copy-Item "$project/Assets/Fishing/*" "$modRoot/ArtSource/Generated" -Recurse -Force
Get-Content $stamp
Write-Output "Previews and validation: $project/Build"
