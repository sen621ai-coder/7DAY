#Requires -Version 7.0
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
$mod=Join-Path $root 'ZZZ-PZAEC_BasementLight'
Add-Type -TypeDefinition (Get-Content "$mod/Source/Photometry.cs" -Raw)
$minimum=[double]::MaxValue;$maximum=0.0
# Check full target floor, including corners, with 8-bit cookie quantization.
foreach($height in @(3.8,4.0,4.2,4.5,4.8,5.0)) {
 $low=[double]::MaxValue;$high=0.0
 for($x=-9;$x -le 9;$x+=0.5) {for($z=-9;$z -le 9;$z+=0.5) {
  $distance=[Math]::Sqrt($x*$x+$z*$z+$height*$height)
  $cookie=[Math]::Round(255*[PZAEC.BasementLight.Photometry]::Transmission($x,-$height,$z))/255
  $estimate=12*[PZAEC.BasementLight.Photometry]::Falloff($distance)*($height/$distance)*$cookie
  $low=[Math]::Min($low,$estimate);$high=[Math]::Max($high,$estimate)
 }}
 if($low -le 0.025 -or $high/$low -gt 2.5){throw "Photometry failed at height $height : $low to $high"}
 Write-Output "Analytic floor check, height $height : minimum=$low maximum=$high ratio=$($high/$low) (not a game render)"
}
if([PZAEC.BasementLight.Photometry]::Transmission(0,1,0) -ne 0){throw 'Upward leakage in cookie'}
$centre=[PZAEC.BasementLight.Photometry]::Transmission(0,-4.2,0)
$corner=[PZAEC.BasementLight.Photometry]::Transmission(9,-4.2,9)
if($corner -le $centre*10){throw 'Centre suppression missing'}
[xml]$blocks=Get-Content "$mod/Config/blocks.xml"
$b=$blocks.SelectSingleNode("//block[@name='pzaecBasementPanelLight']")
if($b.SelectSingleNode("property[@name='UnlockedBy']")){throw 'Free recipe must omit UnlockedBy, not define an empty unlock entry'}
if($b.SelectSingleNode("property[@name='Extends']").param1 -ne 'UnlockedBy'){throw 'Parent lamp unlock must be excluded from inheritance'}
if($b.SelectSingleNode("property[@name='RequiredPower']").value -ne '15'){throw 'Not 15W'}
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
} finally {$game.Dispose()}
Write-Output 'PASS: coverage model, power, one-block config, ingredients, localization and native hook.'
