#Requires -Version 7.0
$ErrorActionPreference='Stop'
$modsRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$gameRoot=Split-Path $modsRoot
$target=Join-Path $env:APPDATA '7DaysToDie/Saves/Navezgane/FishingTest'
if(Test-Path -LiteralPath $target){throw 'FishingTest already exists; will not overwrite it.'}
if(Get-Process -Name 7DaysToDie,7DaysToDieServer -ErrorAction SilentlyContinue){throw 'Close the game before creating the test save.'}
$stage=Join-Path $PSScriptRoot ('artifacts/seed-'+[guid]::NewGuid().ToString('N'))
$userData=Join-Path $stage 'UserData';$bootstrap=Join-Path $userData 'Mods/FishingTestSeed'
New-Item -ItemType Directory -Force $bootstrap|Out-Null
& (Join-Path $PSScriptRoot 'Build.ps1') -CoreOnly -AssemblyName Fishing.TestSeed -OutputDirectory $bootstrap -ExtraSource (Join-Path $PSScriptRoot 'SeedTestSave.cs')
'<xml><Name value="FishingTestSeed"/><DisplayName value="Fishing test save bootstrap"/><Version value="0.1.0"/></xml>'|Set-Content (Join-Path $bootstrap 'ModInfo.xml')
[xml]$config=Get-Content -LiteralPath (Join-Path $gameRoot 'serverconfig.xml')
$settings=@{GameWorld='Navezgane';GameName='FishingTest';ServerName='Fishing test save setup';ServerPort='28049';ServerVisibility='0';ServerPassword='FishingTestSetupOnly';TelnetEnabled='false';WebDashboardEnabled='false';TerminalWindowEnabled='false';EACEnabled='false';UserDataFolder=$userData;ServerMaxPlayerCount='1';ServerMaxAllowedViewDistance='2';DynamicMeshEnabled='false'}
foreach($key in $settings.Keys){$node=$config.SelectSingleNode("/ServerSettings/property[@name='$key']");if(!$node){$node=$config.CreateElement('property');$node.SetAttribute('name',$key);$config.DocumentElement.AppendChild($node)|Out-Null};$node.SetAttribute('value',$settings[$key])}
$configPath=Join-Path $stage 'serverconfig.xml';$config.Save($configPath);$log=Join-Path $stage 'game.log'
$arguments=@('-batchmode','-dedicated','-screen-width','640','-screen-height','360','-crossplatform=None','-serverplatforms=LAN',('-configfile="'+$configPath+'"'),('-UserDataFolder="'+$userData+'"'),'-pzaecCreateFishingTest','-logfile',('"'+$log+'"'))
$process=Start-Process -FilePath (Join-Path $gameRoot '7DaysToDie.exe') -WorkingDirectory $gameRoot -WindowStyle Hidden -PassThru -ArgumentList $arguments
$state=[pscustomobject]@{ProcessId=$process.Id;StartTime=$process.StartTime.ToString('o');Stage=$stage;Log=$log;Source=(Join-Path $userData 'Saves/Navezgane/FishingTest');Target=$target}
$state|ConvertTo-Json|Set-Content (Join-Path $PSScriptRoot 'artifacts/seed-session.json')
$state|ConvertTo-Json
