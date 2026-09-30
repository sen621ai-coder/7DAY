#Requires -Version 7.0
[CmdletBinding()]
param([string]$GameRoot)
$ErrorActionPreference='Stop'
$modsRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if(!$GameRoot){$GameRoot=Split-Path $modsRoot}
$sessionPath=Join-Path $PSScriptRoot 'artifacts/native-session.json'
if(Test-Path -LiteralPath $sessionPath){
 $previous=Get-Content -LiteralPath $sessionPath -Raw|ConvertFrom-Json
 $existing=Get-Process -Id $previous.ProcessId -ErrorAction SilentlyContinue
 if($existing -and $existing.ProcessName -eq '7DaysToDie' -and [Math]::Abs(($existing.StartTime-[datetime]$previous.StartTime).TotalSeconds) -lt 1){throw 'A Fishing M0 probe is already running. Use Stop-NativeProbe.ps1 before starting another.'}
}
$qaRoot=Join-Path $PSScriptRoot ('artifacts/native-'+[guid]::NewGuid().ToString('N'))
$userData=Join-Path $qaRoot 'UserData';$qaMods=Join-Path $userData 'Mods';$probeMod=Join-Path $qaMods 'FishingM0Probe'
New-Item -ItemType Directory -Force $probeMod|Out-Null
Copy-Item -LiteralPath (Join-Path $modsRoot '0_TFP_Harmony') -Destination $qaMods -Recurse
& (Join-Path $PSScriptRoot 'Build.ps1') -GameRoot $GameRoot -ProbeOnly -OutputDirectory $probeMod -AssemblyName 'Fishing.M0Probe' -ExtraSource (Join-Path $PSScriptRoot 'NativeProbe.cs')
'<xml><Name value="FishingM0Probe"/><DisplayName value="Fishing M0 isolated probe"/><Version value="0.0.1"/></xml>'|Set-Content (Join-Path $probeMod 'ModInfo.xml')
[xml]$config=Get-Content -LiteralPath (Join-Path $GameRoot 'serverconfig.xml')
$settings=@{GameWorld='Navezgane';GameName='FishingM0_Isolated';ServerName='Fishing M0 QA';ServerPort='28047';ServerVisibility='0';ServerPassword='FishingM0LocalOnly';TelnetEnabled='false';WebDashboardEnabled='false';TerminalWindowEnabled='false';EACEnabled='false';UserDataFolder=$userData;ServerMaxPlayerCount='1';ServerMaxAllowedViewDistance='2';DynamicMeshEnabled='false'}
foreach($key in $settings.Keys){$node=$config.SelectSingleNode("/ServerSettings/property[@name='$key']");if(!$node){$node=$config.CreateElement('property');$node.SetAttribute('name',$key);$config.DocumentElement.AppendChild($node)|Out-Null};$node.SetAttribute('value',$settings[$key])}
$configPath=Join-Path $qaRoot 'serverconfig.xml';$config.Save($configPath);$log=Join-Path $qaRoot 'game.log'
# Existing forestry mods require native shader materials even on dedicated servers.
# Use a hidden graphics-capable batch process; a null graphics device cannot load this mod set.
$arguments=@('-batchmode','-dedicated','-screen-width','640','-screen-height','360','-crossplatform=None','-serverplatforms=LAN',('-configfile="'+$configPath+'"'),('-UserDataFolder="'+$userData+'"'),'-pzaecFishingM0','-logfile',('"'+$log+'"'))
$process=Start-Process -FilePath (Join-Path $GameRoot '7DaysToDie.exe') -WorkingDirectory $GameRoot -WindowStyle Hidden -PassThru -ArgumentList $arguments
$session=[pscustomobject]@{ProcessId=$process.Id;StartTime=$process.StartTime.ToString('o');QaRoot=$qaRoot;Log=$log}
$session|ConvertTo-Json|Set-Content (Join-Path $PSScriptRoot 'artifacts/native-session.json')
$session|ConvertTo-Json
# The caller polls in bounded intervals, then stops this exact disposable process.
