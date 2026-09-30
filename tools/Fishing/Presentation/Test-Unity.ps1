#Requires -Version 7.0
param([string]$Editor='D:/unity/2022.3.48f1c1/Editor/Unity.exe',[switch]$PreviewOnly)
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path (Split-Path $PSScriptRoot))
$project=Join-Path $PSScriptRoot 'artifacts/UnityProject'
if(!(Test-Path "$project/Build/fishing-presentation.unity3d")){throw 'Run Build-Assets.ps1 first.'}
if(!(Test-Path "$project/Build/verified.txt") -or (Get-Item "$project/Build/verified.txt").LastWriteTimeUtc -lt (Get-Item "$project/Build/fishing-presentation.unity3d").LastWriteTimeUtc){throw 'The newest asset bundle has not passed asset validation. Run Build-Assets.ps1 successfully first.'}
New-Item -ItemType Directory -Force -Path "$project/Assets/Editor/Verification/Contracts","$project/Assets/Editor/Verification/Presentation" | Out-Null
Copy-Item "$root/ZZZ-PZAEC_Fishing/Source/Contracts/*.cs" "$project/Assets/Editor/Verification/Contracts" -Force
Copy-Item "$root/ZZZ-PZAEC_Fishing/Source/Presentation/*.cs" "$project/Assets/Editor/Verification/Presentation" -Force
Copy-Item "$PSScriptRoot/UnityVerification.cs" "$project/Assets/Editor/Verification/UnityVerification.cs" -Force
# Bundle comes from the same successful asset build; test directory mimics the mod resource layout.
New-Item -ItemType Directory -Force -Path "$project/Build/TestMod/Resources" | Out-Null
Copy-Item "$project/Build/fishing-presentation.unity3d" "$project/Build/TestMod/Resources/fishing-presentation.unity3d" -Force
$started=[DateTime]::UtcNow
$method=if($PreviewOnly){'FishingUnityVerification.ExportMotion'}else{'FishingUnityVerification.Run'}
$logName=if($PreviewOnly){'motion.log'}else{'verification.log'}
$argsList=@('-batchmode','-quit','-projectPath',('"'+$project+'"'),'-executeMethod',$method,'-logFile',('"'+$project+'/Logs/'+$logName+'"'))
$proc=Start-Process -FilePath $Editor -ArgumentList $argsList -WindowStyle Hidden -PassThru
$proc.WaitForExit()
$stamp=if($PreviewOnly){"$project/Build/motion-verified.txt"}else{"$project/Build/presentation-verified.txt"}
if($proc.ExitCode -ne 0 -or !(Test-Path $stamp) -or (Get-Item $stamp).LastWriteTimeUtc -lt $started){throw "Presentation verification failed; see $project/Logs/verification.log"}
Get-Content $stamp
