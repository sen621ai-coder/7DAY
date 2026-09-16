#Requires -Version 7.0
param([string]$RuntimeDll)
$ErrorActionPreference='Stop'
$modRoot=Join-Path (Split-Path $PSScriptRoot) '97-AutomationWorkshop'
if(-not $RuntimeDll){$RuntimeDll=Join-Path (Split-Path $PSScriptRoot) '.local-tests/AutomationConfiguration/YF.Automation.dll'}
if(Get-Process 7DaysToDie,7DaysToDieServer -ErrorAction SilentlyContinue){throw 'Game/server is running; close it before replacing the runtime.'}
if(-not(Test-Path -LiteralPath $RuntimeDll)){throw 'Validated runtime is missing.'}
foreach($relative in @('ModInfo.xml','Config/blocks.xml','Config/items.xml','Config/recipes.xml','Config/Localization.csv','Config/XUi_InGame/xui.xml','Config/XUi_InGame/windows.xml')){
 if(-not(Test-Path -LiteralPath (Join-Path $modRoot $relative))){throw "Missing mod file: $relative"}
}
$installed=Join-Path $modRoot 'YF.Automation.dll'
if(Test-Path -LiteralPath $installed){
 $backupDir=Join-Path (Split-Path $PSScriptRoot) '.local-tests/AutomationBackups'
 New-Item -ItemType Directory -Force -Path $backupDir|Out-Null
 Copy-Item -LiteralPath $installed -Destination (Join-Path $backupDir ((Get-Date -Format 'yyyyMMdd-HHmmss')+'-YF.Automation.dll'))
}
Copy-Item -LiteralPath $RuntimeDll -Destination $installed -Force
if((Get-FileHash -LiteralPath $RuntimeDll).Hash -ne (Get-FileHash -LiteralPath $installed).Hash){throw 'Installed runtime hash mismatch.'}
Write-Output "Installed and verified: $installed"
