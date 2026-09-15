#Requires -Version 7.0
param(
    [string]$Editor='E:/soft/Unity/2022.3.62f2/Editor/Unity.exe',
    [string]$Project='E:/soft/7DTD-Modding/SakuraEscort'
)
$ErrorActionPreference='Stop'
if(-not(Test-Path -LiteralPath $Editor)){throw "Unity editor not installed: $Editor"}
if(-not(Test-Path -LiteralPath "$Project/Assets/Character/Sakura.fbx")){throw 'Initialize-Project.ps1 must succeed first.'}
$log=Join-Path $Project 'Logs/asset-build.log'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $log) | Out-Null
$stamp=Join-Path $Project 'Build/Windows/verified.txt'
$started=[DateTime]::UtcNow
$arguments=@('-batchmode','-nographics','-quit','-projectPath',('"'+$Project+'"'),'-executeMethod','EscortAssetBuild.Build','-logFile',('"'+$log+'"'))
$process=Start-Process -FilePath $Editor -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
if($process.ExitCode -ne 0){throw "Unity exited with $($process.ExitCode). See $log"}
if(-not(Test-Path $stamp) -or (Get-Item $stamp).LastWriteTimeUtc -lt $started){throw "No fresh successful build stamp. See $log"}
Get-Content -LiteralPath $stamp
