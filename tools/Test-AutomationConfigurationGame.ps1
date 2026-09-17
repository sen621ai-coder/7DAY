#Requires -Version 7.0
param([string]$RuntimeDll,[int]$TimeoutSeconds=600,[string]$ModDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$game=Split-Path $root
$qaRoot=Join-Path $root '.local-tests/AutomationConfiguration/GameQA'
if(-not $RuntimeDll){$RuntimeDll=Join-Path $root '.local-tests/AutomationConfiguration/YF.Automation.dll'}
if(-not(Test-Path -LiteralPath $RuntimeDll)){throw 'Build the staged runtime first with Build-AutomationWorkshop.ps1 -Output.'}
$mod=Join-Path $qaRoot 'UserData/Mods/97-AutomationWorkshop'
$qaMod=Join-Path $qaRoot 'UserData/Mods/98-MachineConfigurationQA'
New-Item -ItemType Directory -Force $mod,$qaMod | Out-Null
foreach($item in @('Config','Resources','ModInfo.xml')){
    $sourceMod=if($ModDirectory){$ModDirectory}else{Join-Path $root '97-AutomationWorkshop'}
    Copy-Item -LiteralPath (Join-Path $sourceMod $item) -Destination $mod -Recurse -Force
}
Copy-Item -LiteralPath $RuntimeDll -Destination (Join-Path $mod 'YF.Automation.dll') -Force
& (Join-Path $PSScriptRoot 'Build-AutomationGameQA.ps1') -ConfigurationOnly -RuntimeDll $RuntimeDll -Output (Join-Path $qaMod 'Automation.GameQA.dll')
'<xml><Name value="YFMachineConfigurationQA"/><DisplayName value="Isolated machine configuration QA"/><Version value="1.0"/></xml>' | Set-Content -LiteralPath (Join-Path $qaMod 'ModInfo.xml')
[xml]$config=Get-Content -LiteralPath (Join-Path $game 'serverconfig.xml')
$values=@{
    GameWorld='Navezgane';GameName='AutomationConfigQA_Isolated';ServerName='Automation Isolated QA';
    ServerPort='27940';ServerVisibility='0';ServerPassword='AutomationQALocalOnly';
    TelnetEnabled='false';WebDashboardEnabled='false';TerminalWindowEnabled='false';EACEnabled='false';
    UserDataFolder=(Join-Path $qaRoot 'UserData');ServerMaxPlayerCount='1'
}
foreach($key in $values.Keys){
    $node=$config.SelectSingleNode("/ServerSettings/property[@name='$key']")
    if(-not $node){$node=$config.CreateElement('property');$node.SetAttribute('name',$key);$config.DocumentElement.AppendChild($node)|Out-Null}
    $node.SetAttribute('value',$values[$key])
}
$configPath=Join-Path $qaRoot 'serverconfig.xml';$config.Save($configPath)
$logPath=Join-Path $qaRoot ('game-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.log')
$report=Join-Path $qaRoot 'UserData/Saves/Navezgane/AutomationConfigQA_Isolated/machine-configuration-qa.txt'
$started=Get-Date
$process=Start-Process -FilePath (Join-Path $game '7DaysToDie.exe') -WorkingDirectory $game -WindowStyle Hidden -PassThru -ArgumentList @(
    '-batchmode','-nographics','-dedicated','-crossplatform=None','-serverplatforms=Steam,LAN',('-configfile="'+$configPath+'"'),
    ('-UserDataFolder="'+$values.UserDataFolder+'"'),'-yfMachineConfigurationQA','-logfile',('"'+$logPath+'"')
)
try{
    if(-not $process.WaitForExit($TimeoutSeconds*1000)){throw "Isolated QA timed out. Log: $logPath"}
    if(-not(Test-Path -LiteralPath $report) -or (Get-Item -LiteralPath $report).LastWriteTime -lt $started){throw "QA did not produce a fresh report. Log: $logPath"}
    $result=Get-Content -LiteralPath $report
    $result | Write-Output
    if(-not($result -match '^FINISHED checks=\d+ failures=0$')){throw "Native configuration QA failed. Report: $report"}
}finally{
    # This process object is the isolated instance created above, never an existing player session.
    if(-not $process.HasExited){Stop-Process -Id $process.Id}
}

