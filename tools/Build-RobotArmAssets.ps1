#Requires -Version 7.0
param([string]$Editor='E:/soft/unity20223/Editor/Unity.exe',[string]$Project='E:/soft/7DTD-Modding/AutomationAssets')
$ErrorActionPreference='Stop'
if(-not(Test-Path -LiteralPath $Editor)){throw "Unity editor missing: $Editor"}
if(-not(Test-Path -LiteralPath "$Project/ProjectSettings/ProjectVersion.txt")){throw 'Initialize the automation Unity project with Build-ConveyorAssets.ps1 first.'}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'EscortNPC/RobotArmAssetBuild.cs') -Destination "$Project/Assets/Editor/RobotArmAssetBuild.cs" -Force
$started=[DateTime]::UtcNow
$process=Start-Process -FilePath $Editor -WindowStyle Hidden -Wait -PassThru -ArgumentList @('-batchmode','-quit','-projectPath',('"'+$Project+'"'),'-executeMethod','RobotArmAssetBuild.Preview','-logFile',('"'+$Project+'/Logs/arms.log"'))
$stamp="$Project/Build/RobotArms/verified.txt"
if($process.ExitCode -ne 0 -or -not(Test-Path -LiteralPath $stamp) -or (Get-Item -LiteralPath $stamp).LastWriteTimeUtc -lt $started){throw "Robot arm build failed: $Project/Logs/arms.log"}
Get-Content -LiteralPath $stamp
