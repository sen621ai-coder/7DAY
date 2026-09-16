#Requires -Version 7.0
param([string]$Stage='E:/soft/7DTD-Modding/Sakura071-Staging/96-SakuraPreview')
$ErrorActionPreference='Stop'
if(Get-Process 7DaysToDie,7DaysToDieServer -ErrorAction SilentlyContinue){throw '正常退出游戏和独立服务器后再安装。'}
$destination=Join-Path (Split-Path -Parent $PSScriptRoot) '96-SakuraPreview'
foreach($file in @('Sakura.Preview.dll','ModInfo.xml','Config/entityclasses.xml','Config/items.xml','Config/loot.xml','Config/recipes.xml','Config/dialogs.xml','Config/quests.xml','Config/npc.xml','Config/XUi_InGame/xui.xml','Config/XUi_InGame/windows.xml','Config/Localization.csv','Resources/sakura-preview.unity3d','Resources/mint-guardian.unity3d')){
    if(-not(Test-Path (Join-Path $Stage $file))){throw "暂存包缺少 $file"}
}
$backup='E:/soft/7DTD-Modding/Backups/SakuraPreview-before-interactive-'+(Get-Date -Format yyyyMMdd-HHmmss)
Copy-Item -LiteralPath $destination -Destination $backup -Recurse
Copy-Item -Path (Join-Path $Stage '*') -Destination $destination -Recurse -Force
foreach($file in @('Sakura.Preview.dll','ModInfo.xml','Config/entityclasses.xml','Config/items.xml','Config/loot.xml','Config/recipes.xml','Config/dialogs.xml','Config/quests.xml','Config/npc.xml','Config/XUi_InGame/xui.xml','Config/XUi_InGame/windows.xml','Config/Localization.csv','Resources/sakura-preview.unity3d','Resources/mint-guardian.unity3d')){
    if((Get-FileHash (Join-Path $Stage $file)).Hash -ne (Get-FileHash (Join-Path $destination $file)).Hash){throw "安装校验失败: $file"}
}
"已安装小樱与 Mint 原生界面版；旧版本备份：$backup"


