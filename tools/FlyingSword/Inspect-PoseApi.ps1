#Requires -Version 7.0
$root=Split-Path (Split-Path $PSScriptRoot)
Add-Type -Path (Join-Path $root '0_TFP_Harmony/Mono.Cecil.dll')
$asm=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path (Split-Path $root) '7DaysToDie_Data/Managed/Assembly-CSharp.dll'))
foreach($type in $asm.MainModule.Types){foreach($m in $type.Methods){if(!$m.HasBody){continue};foreach($i in $m.Body.Instructions){if(($i.OpCode.Code -eq 'Ldstr' -and $i.Operand -eq 'YLook') -or "$($i.Operand)" -like '*::weaponCarryHash'){Write-Output ('AIMPITCH '+$m.FullName);$m.Body.Instructions|ForEach-Object ToString;break}}}}
foreach($type in $asm.MainModule.Types){
 if($type.Name -match 'Avatar|EModel|SDCSUtils|^Entity$|^EntityAlive$|^EntityPlayerLocal$|^EntityPlayer$'){
  foreach($method in $type.Methods){
   if($method.Name -in @('SetVehicleAnimation','SetInRightHand','SetAiming','SwitchModelAndView','GenerateMeshes','CreateVizTP','CreateVizUI','get_baseRigPrefab','get_TPAnimController','setupRig','GetLookVector','GetLookRay','updateSpineRotation','SetHeadAngles')){
    Write-Output $method.FullName
    if($method.HasBody){$method.Body.Instructions|ForEach-Object ToString}
   }
  }
 }
}
