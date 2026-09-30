#Requires -Version 7.0
param([string]$GameRoot)
$ErrorActionPreference='Stop'
$mods=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if(!$GameRoot){$GameRoot=Split-Path $mods}
$managed=Join-Path $GameRoot '7DaysToDie_Data/Managed'
Add-Type -Path (Join-Path $mods '0_TFP_Harmony/Mono.Cecil.dll')
$module=[Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $managed 'Assembly-CSharp.dll'))
$lines=[Collections.Generic.List[string]]::new()
function Check-FishingApi([bool]$ok,[string]$message){if(!$ok){throw "FAIL $message"};$lines.Add("PASS $message")}
function Find-FishingMethod([string]$type,[string]$method){($module.Types|Where-Object Name -eq $type).Methods|Where-Object Name -eq $method|Select-Object -First 1}
try{
 $lines.Add('Assembly '+$module.Assembly.FullName)
 $lines.Add('MVID '+$module.Mvid)
 $lines.Add('SHA256 '+(Get-FileHash (Join-Path $managed 'Assembly-CSharp.dll')).Hash)
 $constants=$module.Types|Where-Object FullName -eq 'Constants'
 foreach($field in $constants.Fields|Where-Object Name -match '^c(ReleaseType|VersionMajor|VersionMinor|VersionBuild)$'){$lines.Add('CONST '+$field.Name+'='+$field.Constant)}
 foreach($pair in @(@('World','GetWater'),@('WaterValue','GetMassPercent'),@('WaterValue','HasMass'),@('EntityPlayerLocal','MoveByInput'),@('PlayerMoveController','Update'),@('Inventory','Execute'),@('Bag','CanTakeItem'),@('Bag','AddItem'),@('ItemValue','SetMetadata'),@('NetPackage','ProcessPackage'))){Check-FishingApi ($null -ne (Find-FishingMethod $pair[0] $pair[1])) ($pair[0]+'::'+$pair[1])}
 $update=Find-FishingMethod 'PlayerMoveController' 'Update'
 $calls=@($update.Body.Instructions|ForEach-Object {[string]$_.Operand})
 Check-FishingApi ([bool]($calls -match 'Input::GetAxis')) 'native controller reads legacy mouse axes'
 Check-FishingApi ([bool]($calls -match 'EntityPlayerLocal::MoveByInput')) 'native controller calls MoveByInput'
 Check-FishingApi ([bool]($calls -match 'Inventory::Execute')) 'native attack routes through Inventory.Execute'
 $move=Find-FishingMethod 'EntityPlayerLocal' 'MoveByInput'
 $moveText=$move.Body.Instructions.ToString()
 $moveOps=($move.Body.Instructions|ForEach-Object { $_.ToString() }) -join "`n"
 Check-FishingApi ($moveOps.Contains('MovementInput::moveForward') -and $moveOps.Contains('MovementInput::moveStrafe')) 'MoveByInput reads native desired axes'
 Check-FishingApi ($moveOps.Contains('EffectManager::GetValue')) 'native movement retains effect checks'
 $localMove=Find-FishingMethod 'EntityPlayerLocal' 'Move'
 Check-FishingApi ([bool](($localMove.Body.Instructions|ForEach-Object { $_.ToString() }) -match 'Entity::Move\(')) 'local Move delegates to native entity movement (not direct Transform)'
 $latest=Get-ChildItem (Join-Path $env:APPDATA '7DaysToDie/logs') -Filter '*.txt'|Sort-Object LastWriteTime -Descending|Select-Object -First 1
 if($latest){$lines.Add('LOG '+$latest.FullName);Get-Content $latest.FullName -TotalCount 70|Where-Object {$_ -match '^Initialize engine version:| INF Version:'}|ForEach-Object {$lines.Add($_)}}
 foreach($editor in @('E:/soft/Unity/2022.3.62f2/Editor/Unity.exe','D:/unity/2022.3.48f1c1/Editor/Unity.exe','D:/unity/6000.0.23f1c1/Editor/Unity.exe')){$lines.Add('EDITOR '+$editor+' exists='+(Test-Path -LiteralPath $editor))}
 $output=Join-Path $PSScriptRoot 'artifacts/native-audit.txt'
 New-Item -ItemType Directory -Force (Split-Path $output)|Out-Null
 $lines|Set-Content $output -Encoding utf8
 $lines
}finally{$module.Dispose()}
