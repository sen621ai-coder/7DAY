#Requires -Version 7.0
[CmdletBinding()]
param([string]$GameRoot=(Split-Path (Split-Path (Split-Path $PSScriptRoot))))
$ErrorActionPreference='Stop'
$modRoot=Split-Path (Split-Path $PSScriptRoot)
$running=@(Get-Process 7DaysToDie -ErrorAction SilentlyContinue)
if($running.Count){throw 'A 7 Days to Die process is already running; native smoke was not started.'}
$qaBase=Join-Path $modRoot '.local-tests/Surveillance/NativeSmoke'
$qaRoot=Join-Path $qaBase ('run-'+[guid]::NewGuid().ToString('N'))
$userData=Join-Path $qaRoot 'UserData';$mods=Join-Path $userData 'Mods'
New-Item -ItemType Directory -Force $mods|Out-Null
Copy-Item -LiteralPath (Join-Path $modRoot '0_TFP_Harmony') -Destination $mods -Recurse -Force
Copy-Item -LiteralPath (Join-Path $modRoot 'ZZZ-PZAEC_Surveillance') -Destination $mods -Recurse -Force
[xml]$config=Get-Content -LiteralPath (Join-Path $GameRoot 'serverconfig.xml')
$values=@{GameWorld='Navezgane';GameName='SurveillanceQA_Isolated';ServerName='Surveillance Native Smoke';ServerPort='27991';ServerVisibility='0';ServerPassword='SurveillanceQALocalOnly';TelnetEnabled='false';WebDashboardEnabled='false';TerminalWindowEnabled='false';EACEnabled='false';UserDataFolder=$userData;ServerMaxPlayerCount='1'}
foreach($key in $values.Keys){$node=$config.SelectSingleNode("/ServerSettings/property[@name='$key']");if(!$node){$node=$config.CreateElement('property');$node.SetAttribute('name',$key);$config.DocumentElement.AppendChild($node)|Out-Null};$node.SetAttribute('value',$values[$key])}
$configPath=Join-Path $qaRoot 'serverconfig.xml';$config.Save($configPath);$log=Join-Path $qaRoot 'game.log'
$args=@('-batchmode','-dedicated','-crossplatform=None','-serverplatforms=LAN',('-configfile="'+$configPath+'"'),('-UserDataFolder="'+$userData+'"'),'-logfile',('"'+$log+'"'))
$process=Start-Process -FilePath (Join-Path $GameRoot '7DaysToDie.exe') -WorkingDirectory $GameRoot -WindowStyle Hidden -PassThru -ArgumentList $args
$deadline=(Get-Date).AddMinutes(4);$passed=$false;$failure=$null;$initialized=$false;$blocksLoaded=$false;$footprint=$false;$worldAttached=$false
try{
  while((Get-Date)-lt$deadline -and !$process.HasExited){
    Start-Sleep -Seconds 2
    if(Test-Path -LiteralPath $log){
      $text=Get-Content -LiteralPath $log -Raw
      if($text -match '\[Surveillance\] v1\.0\.0 wireless cameras'){$initialized=$true}
      if($text -match 'INF Loaded \(local\): blocks in'){$blocksLoaded=$true}
      if($text -match '\[Surveillance\] 4x3 footprint verified: -2, 0, 0;'){$footprint=$true}
      if($text -match '\[Surveillance\] Wireless device registry attached; block/tile audit passed'){$worldAttached=$true}
      if($initialized -and $blocksLoaded -and $footprint -and $worldAttached){$passed=$true;break}
      if($text -match '(?m)^.*\sERR\s.*(PZAEC_Surveillance|PZAEC\.Surveillance)|XML loader.*(error|failed)|Exception.*PZAEC\.Surveillance'){ $failure=$Matches[0]; break }
    }
    $process.Refresh()
  }
}finally{
  $process.Refresh();if(!$process.HasExited){Stop-Process -Id $process.Id -Force;Wait-Process -Id $process.Id -Timeout 30 -ErrorAction SilentlyContinue}
}
if(!$passed){if(!$failure){$failure='Timed out before surveillance initialization'};throw "$failure; log=$log"}
[pscustomobject]@{Result='PASS';Log=$log;QaRoot=$qaRoot;ProcessId=$process.Id}
