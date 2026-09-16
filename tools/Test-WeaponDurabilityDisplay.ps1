#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
# Reuse only the XML merger, without executing the other audit's body.
$tokens=$null; $errors=$null
$ast=[System.Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Test-EndgameRewardBalance.ps1'),[ref]$tokens,[ref]$errors)
$fn=$ast.Find({param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Apply-Config'},$true)
. ([scriptblock]::Create($fn.Extent.Text))
[xml]$items=Get-Content (Join-Path $root '../Data/Config/items.xml') -Raw
[xml]$ui=Get-Content (Join-Path $root '../Data/Config/ui_display.xml') -Raw
foreach($dir in Get-ChildItem $root -Directory | Sort-Object Name){
    foreach($pair in @(@('items.xml',$items),@('ui_display.xml',$ui))){
        $path=Join-Path $dir.FullName ('Config/'+$pair[0])
        if(Test-Path $path){Apply-Config $pair[1] ([xml](Get-Content $path -Raw))}
    }
}
$weapons=@($items.SelectNodes("/items/item[property[@name='Tags' and contains(@value,'PZAECAdvancedWeapon')]]"))
if($weapons.Count -ne 28){throw "Expected 28 weapons, found $($weapons.Count)"}
$managed=Join-Path $root '../7DaysToDie_Data/Managed'
Get-ChildItem $managed -Filter '*.dll' | ForEach-Object {try{[void][Reflection.Assembly]::LoadFrom($_.FullName)}catch{}}
[void][Reflection.Assembly]::LoadFrom((Join-Path $root '0_TFP_Harmony/0Harmony.dll'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $root '99-AEC_T16_RuntimeFix/AEC.T16.RuntimeFix.dll'))
foreach($weapon in $weapons){
    if(-not [AECT16RuntimeFix.EquipmentStatDisplay]::ShowsWeaponDurability($weapon.name)){throw "Missing runtime scope: $($weapon.name)"}
    if($weapon.SelectSingleNode("property[@name='ShowQuality']").value -ne 'true'){throw "Hidden bar: $($weapon.name)"}
    $row=$ui.SelectSingleNode("/ui_display_info/item_display/item_display_info[@display_type='AECDisplay_$($weapon.name)']/display_entry[@name='aecBase_DegradationMax']")
    if($row.title_key -ne 'aecDisplay_WeaponDurabilityRemaining'){throw "Wrong durability title: $($weapon.name)"}
}
foreach($case in @(@(5000,0,'5000 / 5000 (100%)'),@(5000,2500,'2500 / 5000 (50%)'),@(5000,5000,'0 / 5000 (0%)'),@(5000,6000,'0 / 5000 (0%)'),@(0,0,'0 / 0 (0%)'),@(3750,1875,'1875 / 3750 (50%)'))){
    $actual=[AECT16RuntimeFix.EquipmentStatDisplay]::FormatDurability($case[0],$case[1])
    if($actual -ne $case[2]){throw "Format mismatch: $actual"}
}
foreach($name in @('gunMGT3M60','gunPZAECStormReservoirT15','armorPZAECHarrierOutfitT19','modPZAECClosedLoopFeedT19')){
    if([AECT16RuntimeFix.EquipmentStatDisplay]::ShowsWeaponDurability($name)){throw "Scope leak: $name"}
}
$labels=Import-Csv (Join-Path $root '99-AEC_T16_RuntimeFix/Config/Localization.csv')
$label=$labels | Where-Object Key -eq 'aecDisplay_WeaponDurabilityRemaining'
if($label.schinese -ne '剩余耐久／最大耐久'){throw 'Missing Chinese durability label'}
'PASS: 28 weapon bars and labels; compiled formatter full/half/broken/over-worn/zero/reduced maximum; legacy scope excluded.'
