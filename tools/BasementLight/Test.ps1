#Requires -Version 7.0
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
$mod=Join-Path $root 'ZZZ-PZAEC_BasementLight'
Add-Type -TypeDefinition (Get-Content "$mod/Source/Photometry.cs" -Raw)
$minimum=[double]::MaxValue;$maximum=0.0
# Check full target floor, including corners, with 8-bit cookie quantization.
foreach($height in @(4.8,5.0,5.2,5.5,5.8,6.0)) {
 $low=[double]::MaxValue;$high=0.0
 for($x=-6;$x -le 6;$x+=0.5) {for($z=-6;$z -le 6;$z+=0.5) {
  $distance=[Math]::Sqrt($x*$x+$z*$z+$height*$height)
  $cookie=[Math]::Round(255*[PZAEC.BasementLight.Photometry]::Transmission($x,-$height,$z))/255
  $estimate=[PZAEC.BasementLight.Photometry]::Intensity*[PZAEC.BasementLight.Photometry]::Falloff($distance)*($height/$distance)*$cookie
  $low=[Math]::Min($low,$estimate);$high=[Math]::Max($high,$estimate)
 }}
 if($low -le 0.002 -or $high/$low -gt 3.5 -or $high -gt .02){throw "Photometry failed at height $height : $low to $high"}
 Write-Output "Analytic floor check, height $height : minimum=$low maximum=$high ratio=$($high/$low) (not a game render)"
}
if([PZAEC.BasementLight.Photometry]::Transmission(0,1,0) -ne 0){throw 'Upward leakage in cookie'}
$centre=[PZAEC.BasementLight.Photometry]::Transmission(0,-5.4,0)
$corner=[PZAEC.BasementLight.Photometry]::Transmission(6,-5.4,6)
if($corner -le $centre*3){throw 'Centre suppression missing'}
if([PZAEC.BasementLight.Photometry]::Intensity -gt [float]1.45){throw 'Unsafe fallback intensity if cookie is ignored by renderer'}
$previous=0.0
for($angle=0;$angle -le 180;$angle+=.25){
 $radians=$angle*[Math]::PI/180
 $t=[PZAEC.BasementLight.Photometry]::Transmission([Math]::Cos($radians),-[Math]::Sin($radians),0)
 if($t -lt 0 -or $t -gt .65 -or [Math]::Abs($t-$previous) -gt .02){throw 'Angular cutoff or excessive grazing output'}
 $rotated=[PZAEC.BasementLight.Photometry]::Transmission(0,-[Math]::Sin($radians),[Math]::Cos($radians))
 if([Math]::Abs($rotated-$t) -gt .000001){throw 'Azimuth seam in radial distribution'}
 $previous=$t
}
[xml]$blocks=Get-Content "$mod/Config/blocks.xml"
$b=$blocks.SelectSingleNode("//block[@name='pzaecBasementPanelLight']")
if($b.SelectSingleNode("property[@name='UnlockedBy']")){throw 'Free recipe must omit UnlockedBy, not define an empty unlock entry'}
if($b.SelectSingleNode("property[@name='Extends']").param1 -ne 'UnlockedBy'){throw 'Parent lamp unlock must be excluded from inheritance'}
if($b.SelectSingleNode("property[@name='RequiredPower']").value -ne '15'){throw 'Not 15W'}
if($b.SelectSingleNode("property[@name='Model']").value -ne 'pzaecBasementPanelRuntime.prefab'){throw 'Panel shares the vanilla lamp model pool'}
if($b.SelectSingleNode("property[@name='WireOffset']").value -ne '0,0.836,0'){throw 'Missing dedicated-server wire offset fallback'}
if($b.SelectSingleNode("property[@name='MultiBlockDim']")){throw 'Footprint must be one voxel'}
[xml]$recipes=Get-Content "$mod/Config/recipes.xml"
if($recipes.SelectSingleNode('//recipe').tags -match 'learnable'){throw 'Free lamp recipe must not require learning'}
[xml]$items=Get-Content (Join-Path (Split-Path $root) 'Data/Config/items.xml')
foreach($ingredient in $recipes.SelectNodes('//ingredient')){
 if(-not $items.SelectSingleNode("/items/item[@name='$($ingredient.name)']")){throw "Missing ingredient $($ingredient.name)"}
}
$localization=Import-Csv "$mod/Config/Localization.csv"
if($localization.Count -ne 2 -or -not $localization[1].schinese){throw 'Localization malformed'}
Add-Type -Path "$root/0_TFP_Harmony/Mono.Cecil.dll"
$game=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path (Split-Path $root) '7DaysToDie_Data/Managed/Assembly-CSharp.dll'))
try {
 $powered=$game.MainModule.Types|Where-Object Name -eq BlockPoweredLight
 $hook=$powered.Methods|Where-Object Name -eq OnBlockEntityTransformAfterActivated
 if(($hook.Parameters.Name -join ',') -ne '_world,_blockPos,_blockValue,_ebcd'){throw 'Hook signature changed'}
 $update=$powered.Methods|Where-Object Name -eq updateLightState
 if(-not ($update.Body.Instructions.Operand -match 'get_IsToggled')){throw 'Native toggle contract changed'}
 $shape=$game.MainModule.Types|Where-Object Name -eq BlockShapeModelEntity
 foreach($name in @('CloneModel','PoolLoadCallback')){
  $method=$shape.Methods|Where-Object Name -eq $name
  if(-not ($method.Body.Instructions.Operand -match 'BlockShapeModelEntity::getPrefab')){throw "Common preview/world prefab entry changed: $name"}
 }
 $utils=$game.MainModule.Types|Where-Object Name -eq GameUtils
 $hit=$utils.Methods|Where-Object Name -eq FindMasterBlockForEntityModelBlock
 if(-not ($hit.Body.Instructions.Operand -match 'RootTransformRefParent::FindRoot')){throw 'Native block hit ownership contract changed'}
 $tile=$game.MainModule.Types|Where-Object Name -eq TileEntityPowered
 $bind=$tile.Methods|Where-Object Name -eq set_BlockTransform
 if(-not ($bind.Body.Instructions.Operand -contains 'WireOffset')){throw 'Native wire anchor contract changed'}
} finally {$game.Dispose()}
Write-Output 'PASS: coverage model, power, one-block config, ingredients, localization and native hook.'


if([PZAEC.BasementLight.Photometry]::FillIntensity -gt [float]0.41){throw 'Bounce light too strong'}
if([PZAEC.BasementLight.Photometry]::FillTransmission(0,-1,0) -ne 0){throw 'Bounce floods floor'}
if([PZAEC.BasementLight.Photometry]::FillTransmission(1,0,0) -le 0 -or [PZAEC.BasementLight.Photometry]::FillTransmission(0,1,0) -le 0){throw 'Missing wall/ceiling fill'}
$lastFill=0.0
for($a=-90;$a -le 90;$a+=.25){
 $r=$a*[Math]::PI/180
 $f=[PZAEC.BasementLight.Photometry]::FillTransmission([Math]::Cos($r),[Math]::Sin($r),0)
 if($f -lt 0 -or $f -gt .65 -or [Math]::Abs($f-$lastFill) -gt .02){throw 'Invalid bounce transition'}
 $lastFill=$f
}

