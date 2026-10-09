#Requires -Version 7.0
param([int]$TimeoutSeconds=480,[switch]$KeepRunning)
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot);$game=Split-Path $root
if(Get-Process 7DaysToDie,7DaysToDieServer -ErrorAction SilentlyContinue){throw 'Existing game or QA process is running; not touched.'}
$qa=Join-Path $root ('.local-tests/FlyingSword/run-'+[guid]::NewGuid().ToString('N'))
$data=Join-Path $qa 'UserData';$mods=Join-Path $data 'Mods';New-Item -ItemType Directory -Force $mods|Out-Null
# Override only our mod. All other installed mods are read in-place, preserving
# the actual AEC boss stats, runtime patches and weapon integration under test.
$target=Join-Path $mods 'ZZ-PZAEC_FlyingSword'
Copy-Item -LiteralPath (Join-Path $root 'ZZ-PZAEC_FlyingSword') -Destination $target -Recurse
& (Join-Path $root 'tools/Build-FlyingSword.ps1') -SourceRoot (Join-Path $target 'Source') -OutputPath (Join-Path $target 'PZAEC.FlyingSword.dll') -ExtraSources @((Join-Path $PSScriptRoot 'NativeQA.cs'),(Join-Path $PSScriptRoot 'PoseAudit.cs'),(Join-Path $PSScriptRoot 'CombatAudit.cs'))
[xml]$config=Get-Content -LiteralPath (Join-Path $game 'serverconfig.xml')
$values=@{GameWorld='Navezgane';GameName='JuqueQA';ServerName='Juque QA';ServerPort='27991';ServerVisibility='0';ServerPassword='JuqueIsolatedOnly';TelnetEnabled='false';WebDashboardEnabled='false';TerminalWindowEnabled='false';EACEnabled='false';UserDataFolder=$data;ServerMaxPlayerCount='2';GameDifficulty='2'}
foreach($key in $values.Keys){$node=$config.SelectSingleNode("/ServerSettings/property[@name='$key']");if(!$node){$node=$config.CreateElement('property');$node.SetAttribute('name',$key);$config.DocumentElement.AppendChild($node)|Out-Null};$node.SetAttribute('value',$values[$key])}
$cfg=Join-Path $qa 'serverconfig.xml';$config.Save($cfg);$log=Join-Path $qa 'game.log'
$args=@('-batchmode','-dedicated','-crossplatform=None','-serverplatforms=Steam,LAN',('-configfile="'+$cfg+'"'),('-UserDataFolder="'+$data+'"'),'-logfile',('"'+$log+'"'),'-juqueQA')
$process=Start-Process -FilePath (Join-Path $game '7DaysToDie.exe') -WorkingDirectory $game -WindowStyle Hidden -PassThru -ArgumentList $args
@{ProcessId=$process.Id;Log=$log;QaRoot=$qa}|ConvertTo-Json|Set-Content (Join-Path $root '.local-tests/FlyingSword/session.json')
Write-Output ('QA_ROOT='+$qa)
try {
 $deadline=(Get-Date).AddSeconds($TimeoutSeconds)
 while((Get-Date)-lt $deadline -and !$process.HasExited){Start-Sleep -Seconds 3;if(Test-Path $log){$content=Get-Content $log -Raw;if($content -match '\[JuqueQA\] COMPLETE|\[Juque\] Installation failed'){break}}}
 if(Test-Path $log){Select-String -Path $log -Pattern '\[Juque|Juque.*Exception|ERR.*Juque|Failed initializing.*FlyingSword'|Select-Object -Last 55|ForEach-Object Line}
 if(!(Test-Path $log) -or (Get-Content $log -Raw) -notmatch '\[JuqueQA\] COMPLETE failures=0'){throw 'Juque native QA did not pass; inspect session.json and game.log.'}
}finally{if(!$KeepRunning -and !$process.HasExited){Stop-Process -Id $process.Id;[void]$process.WaitForExit(10000)}}
