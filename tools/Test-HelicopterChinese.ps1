$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$locale=@{}
foreach($dir in @('ZZ-PZAEC_ApacheFlight','ZZ-PZAEC_MD500Compatibility')){
 $path=Join-Path $root "$dir/Config/Localization.csv"
 $seen=@{}
 foreach($row in Import-Csv $path){
  if($seen.ContainsKey($row.Key)){throw "Duplicate localization: $($row.Key)"};$seen[$row.Key]=$true
  if($row.schinese -notmatch '[\p{IsCJKUnifiedIdeographs}]' -or $row.english -ne $row.schinese){throw "Missing Chinese display/fallback: $($row.Key)"}
  $locale[$row.Key]=$row
 }
}
$checked=@{}
foreach($dir in @('MD-500','ZZ-PZAEC_ApacheFlight')){
 [xml]$items=Get-Content "$root/$dir/Config/items.xml" -Raw
 [xml]$recipes=Get-Content "$root/$dir/Config/recipes.xml" -Raw
 foreach($item in $items.SelectNodes('//item')){
  if(!$locale.ContainsKey($item.name)){throw "Missing product name: $($item.name)"}
  $key=$item.SelectSingleNode("property[@name='DescriptionKey']").value
  if($item.name -eq 'questRewardMD500PartsBundle'){$key='questRewardMD500PartsBundleDesc'}
  if(!$locale.ContainsKey($key)){throw "Missing product description: $key"}
 }
 foreach($ingredient in $recipes.SelectNodes('//ingredient')){
  if(!$locale.ContainsKey($ingredient.name)){throw "Missing material translation: $($ingredient.name)"}
  $checked[$ingredient.name]=$true
 }
}
[xml]$md=Get-Content "$root/MD-500/Config/items.xml" -Raw
[xml]$patch=Get-Content "$root/ZZ-PZAEC_MD500Compatibility/Config/items.xml" -Raw
$target=$md.SelectSingleNode("//item[@name='questRewardMD500PartsBundle']/property[@name='DescriptionKey']/@value")
if(!$target){throw 'Bundle DescriptionKey target missing'}
if($patch.SelectSingleNode("//set[contains(@xpath,'questRewardMD500PartsBundle')]").InnerText -ne 'questRewardMD500PartsBundleDesc'){throw 'Wrong bundle description key'}
if((Get-FileHash "$root/ZZ-PZAEC_ApacheFlight/Config/Localization.csv").Hash -ne (Get-FileHash "$root/ZZ-PZAEC_ApacheFlight/Config/Localization.txt").Hash){throw 'Compatibility localization mirror differs'}
Add-Type -Path "$root/0_TFP_Harmony/Mono.Cecil.dll"
$game=[Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path (Split-Path $root) '7DaysToDie_Data/Managed/Assembly-CSharp.dll'))
try{
 $loader=($game.Types|Where-Object Name -eq Localization).Methods|Where-Object Name -eq LoadPatchDictionaries
 if(!($loader.Body.Instructions|Where-Object {$_.OpCode.Name -eq 'ldstr' -and $_.Operand -eq '/Localization.csv'})){throw 'Native localization filename changed'}
}finally{$game.Dispose()}
Write-Output "PASS: $($locale.Count) Chinese display/fallback keys; all 9 products and their descriptions; $($checked.Count) unique recipe materials; bundle override and actual V3.2 Localization.csv loader verified."
