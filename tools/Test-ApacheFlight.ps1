$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$mod=Join-Path $root 'ZZ-PZAEC_ApacheFlight'
$base=Join-Path (Split-Path $root) 'Data/Config'
$docs=@{}
foreach($file in Get-ChildItem "$mod/Config" -Filter '*.xml') {
 [xml]$doc=Get-Content $file.FullName -Raw
 $docs[$file.BaseName]=$doc
 [xml]$vanilla=Get-Content (Join-Path $base $file.Name) -Raw
 if($file.Name -eq 'items.xml'){
  [xml]$aecItems=Get-Content (Join-Path $root '07-AEC-Vehicles-NoMicrocraft/Config/items.xml') -Raw
  foreach($item in $aecItems.SelectNodes('//item[@name]')){
   if(!$vanilla.SelectSingleNode("/items/item[@name='$($item.name)']")){[void]$vanilla.DocumentElement.AppendChild($vanilla.ImportNode($item,$true))}
  }
 }
 foreach($op in $doc.DocumentElement.ChildNodes) {
  if($op.NodeType -ne 'Element'){continue}
  if($op.Name -ne 'append'){throw "Unexpected global mutation: $($file.Name)"}
  if($vanilla.SelectNodes($op.GetAttribute('xpath')).Count -ne 1){throw "Missing/ambiguous target in $($file.Name): $($op.xpath)"}
 }
 if($doc.OuterXml -match 'VehicleWeapon|CustomParticleLoader|ammoMissile|BigExplosion'){throw "Weapon dependency in $($file.Name)"}
}
$items=@{}
foreach($file in @((Join-Path $base 'items.xml'),(Join-Path $root '07-AEC-Vehicles-NoMicrocraft/Config/items.xml'),"$mod/Config/items.xml")) {
 [xml]$doc=Get-Content $file -Raw
 foreach($item in $doc.SelectNodes('//item[@name]')){
  $parent=$item.SelectSingleNode("property[@name='Extends']")
  if($parent -and !$items.ContainsKey($parent.value)){
   throw "Item $($item.name) extends $($parent.value) before that item is registered"
  }
  $items[$item.name]=$true
 }
}
$recipes=$docs.recipes.SelectNodes('//recipe')
if($recipes.Count -ne 6){throw 'Expected three aircraft recipes and three ammunition recipes'}
foreach($r in $recipes){
 if(!$items.ContainsKey($r.name)){throw "Unknown recipe output: $($r.name)"}
 $station=if($r.name -like 'pzApache*'){'workbench'}else{'aecVehicleFinalAssemblyBench'}
 if($r.craft_area -ne $station -or $r.always_unlocked -ne 'false' -or $r.tags -notmatch 'learnable'){throw "Recipe progression bypass: $($r.name)"}
 foreach($ingredient in $r.ingredient){if(!$items.ContainsKey($ingredient.name) -or [int]$ingredient.count -le 0){throw "Invalid ingredient: $($ingredient.OuterXml)"}}
 $unlock=$docs.progression.SelectSingleNode('//passive_effect')
 if($unlock.level -ne '100,100' -or $r.name -notin $unlock.tags.Split(',')){throw "Missing level-100 unlock: $($r.name)"}
}
[xml]$blocks=Get-Content (Join-Path $root '07-AEC-Vehicles-NoMicrocraft/Config/blocks.xml') -Raw
if(!$blocks.SelectSingleNode("//block[@name='aecVehicleFinalAssemblyBench']")){throw 'Assembly bench missing'}
$v=$docs.vehicles.SelectSingleNode('//vehicle')
if($v.name -ne 'vehicleApacheHelicopter'){throw 'Vehicle identity changed'}
$speeds=@($v.SelectSingleNode("property[@name='velocityMax_turbo']").value.Split(',') | ForEach-Object {[double]::Parse($_,[Globalization.CultureInfo]::InvariantCulture)})
if($speeds[0] -le 17.28 -or $speeds[2] -le 28.8 -or $speeds[2] -gt 36 -or $speeds[1] -gt 12 -or $speeds[3] -gt 12){throw 'Apache speed tier outside intended bounds'}
if($v.SelectNodes("property[starts-with(@class,'seat')]").Count -ne 2){throw 'Apache must retain two seats'}
if($v.SelectNodes("property[starts-with(@class,'force')]").Count -ne 6){throw 'Flight fallback structure changed'}
if(!$v.SelectSingleNode("property[@class='motor0']/property[@name='transform' and @value='Origin/TopPropellerJoint']")){throw 'Original rotor path lost'}
$apacheItem=$docs.items.SelectSingleNode("//item[@name='vehicleApacheHelicopterPlaceable']")
$apacheTags=@($apacheItem.SelectSingleNode("property[@name='Tags']").value.Split(','))
foreach($tag in @('vehicle','varmor','vengine','vfuel','vlight','vstorage','canHaveCosmetic')){if($tag -notin $apacheTags){throw "Apache vehicle mod tag missing: $tag"}}
foreach($tag in @('vseat','vplow')){if($tag -in $apacheTags){throw "Unsupported Apache vehicle mod tag: $tag"}}
if([int]$apacheItem.SelectSingleNode("effect_group/passive_effect[@name='ModSlots']").value -ne 4){throw 'Apache must retain four vehicle-mod slots'}
$headlight=$v.SelectSingleNode("property[@class='headlight']")
if(!$headlight -or !$headlight.SelectSingleNode("property[@name='class' and @value='Headlight']") -or !$headlight.SelectSingleNode("property[@name='transform' and @value='Origin/headlight/lightSource']") -or !$headlight.SelectSingleNode("property[@name='mod' and @value='light']")){throw 'Apache headlight binding/mod integration invalid'}
foreach($part in @(@('engine','speed'),@('fuelTank','fueltank'),@('storage','storage'))){if(!$v.SelectSingleNode("property[@class='$($part[0])']/property[@name='mod' and @value='$($part[1])']")){throw "Apache vehicle part mod binding invalid: $($part[0])"}}
if($docs.entityclasses.SelectSingleNode("//property[@name='Class']").value -ne 'EntityVGyroCopter'){throw 'Wrong vehicle physics class'}
if($docs.entityclasses.SelectSingleNode("//property[@name='LootList']").value -ne 'vehicleGyrocopter'){throw 'Storage compatibility changed'}
foreach($prop in $docs.items.SelectNodes("//item[starts-with(@name,'vehicleApache')]/property[@name='CustomIcon']")){if(!(Test-Path "$mod/UIAtlases/ItemIconAtlas/$($prop.value).png")){throw "Missing icon $($prop.value)"}}
foreach($doc in $docs.Values){foreach($attribute in $doc.SelectNodes('//@*')){
 if($attribute.Value -match '^#@modfolder:([^?]+)\?') {if(!(Test-Path (Join-Path $mod $Matches[1]))){throw "Missing resource $($attribute.Value)"}}
}}
$bundle="$mod/Resources/ApacheHelicopterPrefab.unity3d"
$source=Join-Path $root '.local-tests/apache-research/apache/AH-64-Apache-Helicopter/Resources/ApacheHelicopterPrefab.unity3d'
if((Get-Item $bundle).Length -ne 14971698){throw 'Unexpected model bundle size'}
if((Test-Path $source) -and (Get-FileHash $bundle).Hash -ne (Get-FileHash $source).Hash){throw 'Model differs from original'}
$localization=Import-Csv "$mod/Config/Localization.csv"
foreach($r in $recipes){if(!($localization | Where-Object Key -eq $r.name)){throw "Missing recipe localization $($r.name)"}}
Write-Output 'PASS: Apache XML targets, recipes, progression, speed/seat layout, vehicle-mod tags/parts, headlight binding, weapon isolation, localization and original model hash.'
