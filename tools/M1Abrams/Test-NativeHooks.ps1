$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
Add-Type -Path "$root/0_TFP_Harmony/Mono.Cecil.dll"
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path (Split-Path $root) '7DaysToDie_Data/Managed/Assembly-CSharp.dll'))
$script:n=0
function Check($ok,$label){$script:n++;if(!$ok){throw $label}}
function Method($type,$name){$t=$a.MainModule.Types|Where-Object Name -eq $type;@($t.Methods|Where-Object Name -eq $name)}
$damage=Method EntityVehicle ApplyDamage
Check ($damage.Count -eq 1) 'ApplyDamage hook unambiguous'
Check (@($damage[0].Body.Instructions|Where-Object {$_.OpCode.Code -eq 'Ldc_I4' -and $_.Operand -eq 99999}).Count -eq 1) 'Exactly one vanilla destruction sentinel'
foreach($pair in @(@('EntityVehicle','ProcessDamageResponseLocal'),@('Equipment','GetTotalPhysicalArmorRating'),@('Vehicle','GetPlayerDamagePercent'),@('Vehicle','SetItemValue'),@('Vehicle','RepairParts'),@('XUiM_Vehicle','RepairVehicle'),@('ItemActionEntryCraft','OnActivated'))){Check ((Method $pair[0] $pair[1]).Count -eq 1) ($pair -join '.')}
$m=Method Vehicle SetItemValue
Check (@($m[0].Body.Instructions|Where-Object {($_.Operand -as [string]) -match 'ItemValue::get_UseTimes'}).Count -eq 1) 'Health reads item float degradation'
Check (@($m[0].Body.Instructions|Where-Object {($_.Operand -as [string]) -match 'Stat::set_BaseMax'}).Count -eq 1) 'Vehicle initializes max health from item'
$m=Method EntityVehicle ReadSyncData
Check (@($m[0].Body.Instructions|Where-Object {($_.Operand -as [string]) -match 'Vehicle::LoadItems'}).Count -eq 1) 'Network loads native item state'
$m=Method EntityVehicle Read
Check ($m.Count -eq 1) 'Disk read hook unambiguous'
$readCall=@($m[0].Body.Instructions|Where-Object {($_.Operand -as [string]) -match 'EntityVehicle::ReadSyncData'})
Check ($readCall.Count -eq 1 -and $readCall[0].Previous.OpCode.Code -eq 'Ldc_I4_0') 'Installed native disk reader passes actor zero; disk scope must not be inferred from negative actor IDs'
$m=Method Equipment CalcDamage
Check (@($m[0].Body.Instructions|Where-Object {($_.Operand -as [string]) -match 'GetTotalPhysicalArmorRating'}).Count -eq 1) 'Armor interception is on native damage path'
$m=Method Equipment '.cctor'
Check (@($m[0].Body.Instructions|Where-Object {$_.Operand -eq 'coredamageresist'}).Count -eq 1) 'Defender armor tags match native initialization'
$m=Method EntityVehicle PhysicsFixedUpdate
$steering=@($m[0].Body.Instructions|Where-Object {($_.Operand -as [string]) -match 'EntityVehicle::UpdateWheelsSteering'})
$forces=@($m[0].Body.Instructions|Where-Object {($_.Operand -as [string]) -match 'EntityVehicle::FixedUpdateForces'})
Check ($steering.Count -eq 1 -and $forces.Count -eq 1 -and $steering[0].Offset -lt $forces[0].Offset) 'Chassis postfix follows native wheel steering in physics step'
Check ((Method EntityVehicle FixedUpdateForces).Count -eq 1) 'Chassis physics hook unambiguous'
foreach($pair in @(@('Vehicle','CalcEffects'),@('Vehicle','CalcMods'),@('Vehicle','OnModsChanged'),@('Vehicle','LoadItems'),@('ItemValue','CalcModSlotCount'),@('ItemValue','Read'),@('XUiC_ItemPartStack','CanSwap'),@('XUiC_ItemPartStack','CanRemove'),@('XUiC_ItemCosmeticStack','CanSwap'))){Check ((Method $pair[0] $pair[1]).Count -eq 1) ('Module hook unambiguous: '+($pair -join '.'))}
$m=Method VPEngine Update
Check (@($m[0].Body.Instructions|Where-Object {($_.Operand -as [string]) -match 'Vehicle::EffectFuelUsePer'}).Count -eq 1) 'Native engine applies fuel multiplier exactly once'
$m=Method EntityVehicle PhysicsFixedUpdate
foreach($field in @('EffectMotorTorquePer','EffectVelocityMaxPer')){Check (@($m[0].Body.Instructions|Where-Object {($_.Operand -as [string]) -match ('Vehicle::'+$field)}).Count -eq 1) ('Native physics reads '+$field)}
$m=Method Vehicle CalcEffects
foreach($field in @('EffectMotorTorquePer','EffectVelocityMaxPer','EffectFuelUsePer')){Check (@($m[0].Body.Instructions|Where-Object {$_.OpCode.Code -eq 'Stfld' -and ($_.Operand -as [string]) -match ('Vehicle::'+$field)}).Count -eq 1) ('Native effects reset '+$field+' before module multiplication')}
foreach($pair in @(@('Vehicle','GetUpdatedItemValue'),@('EntityVehicle','Write'))){Check ((Method $pair[0] $pair[1]).Count -eq 1) ('Secondary persistence hook unambiguous: '+($pair -join '.'))}
$m=Method EntityVehicle PhysicsFixedUpdate
Check (@($m[0].Body.Instructions|Where-Object {$_.OpCode.Code -eq 'Ldsfld' -and ($_.Operand -as [string]) -match 'EntityVehicle::isTurnTowardsLook'}).Count -eq 1) 'Secondary aim/steering separation has one native look steering read'
$m=Method Vehicle GetUpdatedItemValue
Check (@($m[0].Body.Instructions|Where-Object {$_.OpCode.Code -eq 'Ret'}).Count -eq 1) 'Native pickup returns updated vehicle item'
$a.Dispose()
Write-Output "PASS $script:n installed-game hook and large-health compatibility checks"
