#Requires -Version 7.0
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
Add-Type -Path @("$PSScriptRoot/ModuleFixtures.cs","$root/ZZ-PZAEC_M1Abrams/Source/M1Rules.cs","$root/ZZ-PZAEC_M1Abrams/Source/M1ModuleRules.cs","$root/ZZ-PZAEC_M1Abrams/Source/M1Modules.cs")
$flags=[Reflection.BindingFlags]'NonPublic,Static'
$begin=[PZAEC.M1.Modules].GetMethod('BeginSync',$flags)
$end=[PZAEC.M1.Modules].GetMethod('EndSync',$flags)
$load=[PZAEC.M1.Modules].GetMethod('LoadItems',$flags)
$vehicle=[EntityVehicle]::new()
$incoming=[ItemValue]::new('vehicleM1AbramsPlaceable')
$incoming.modifications=@([ItemValue]::new('modPZAECM1Armor'))
$outer=[object[]]@(7,$null)
$begin.Invoke($null,$outer)
$save=[object[]]@($null)
[PZAEC.M1.Modules].GetMethod('BeginSaveRead',$flags).Invoke($null,$save)
$disk=[object[]]@(0,$null)
$begin.Invoke($null,$disk)
$vehicle.hasDriver=$true
$load.Invoke($null,[object[]]@($vehicle.vehicle,[ItemStack[]]@([ItemStack]::new($incoming))))
if($incoming.Q -ne 0){throw 'Disk load must not quarantine saved modules as a client edit'}
$end.Invoke($null,[object[]]@($vehicle,[ushort]4,$disk[1],$null))
if($vehicle.Syncs -ne 0){throw 'Disk load must not echo network sync before PostInit'}
[PZAEC.M1.Modules].GetMethod('EndSaveRead',$flags).Invoke($null,[object[]]@($save[0],$null))
$end.Invoke($null,[object[]]@($vehicle,[ushort]4,$outer[1],$null))
if($vehicle.Syncs -ne 1){throw 'Nested disk load must restore the outer network context'}
$network=[object[]]@(7,$null)
$begin.Invoke($null,$network)
$load.Invoke($null,[object[]]@($vehicle.vehicle,[ItemStack[]]@([ItemStack]::new($incoming))))
if($incoming.Q -ne 1){throw 'Occupied remote edits must still be quarantined after a disk load'}
$failure=[Exception]::new('test read failure')
$returned=$end.Invoke($null,[object[]]@($vehicle,[ushort]4,$network[1],$failure))
if($returned -ne $failure -or $vehicle.Syncs -ne 1){throw 'Failed network reads must retain their exception without sending a reply'}
Write-Output 'PASS saved vehicle modules retained; no disk-load network echo; nested network context restored'
