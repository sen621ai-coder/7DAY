#Requires -Version 7.0
param([string]$Editor='E:/soft/unity20223/Editor/Unity.exe', [string]$Project='E:/soft/7DTD-Modding/AutomationAssets', [switch]$Preview)
$ErrorActionPreference='Stop'
if(-not(Test-Path -LiteralPath $Editor)){throw "Unity editor missing: $Editor"}
New-Item -ItemType Directory -Force "$Project/Assets/Editor","$Project/ProjectSettings","$Project/Packages","$Project/Logs" | Out-Null
if(-not(Test-Path "$Project/ProjectSettings/ProjectVersion.txt")){
    'm_EditorVersion: 2022.3.62f2' | Set-Content "$Project/ProjectSettings/ProjectVersion.txt"
}
if(-not(Test-Path "$Project/Packages/manifest.json")){
    '{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.physics":"1.0.0"}}' | Set-Content "$Project/Packages/manifest.json"
}
Copy-Item (Join-Path $PSScriptRoot 'EscortNPC/MachineAssetBuild.cs') "$Project/Assets/Editor/MachineAssetBuild.cs" -Force
$method=if($Preview){'MachineAssetBuild.Preview'}else{'MachineAssetBuild.Build'}
$arguments=@('-batchmode','-quit','-projectPath',('"'+$Project+'"'),'-executeMethod',$method,'-logFile',('"'+$Project+'/Logs/machines-build.log"'))
if(-not $Preview){$arguments+='-nographics'}
$started=[DateTime]::UtcNow
$process=Start-Process $Editor -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
$stamp="$Project/Build/Windows/machines-verified.txt"
if($process.ExitCode -ne 0 -or -not(Test-Path $stamp) -or (Get-Item $stamp).LastWriteTimeUtc -lt $started){throw "Machine asset build failed; see $Project/Logs/machines-build.log"}
Get-Content $stamp
Write-Output "Bundle: $Project/Build/Windows/automation-machines.unity3d"
