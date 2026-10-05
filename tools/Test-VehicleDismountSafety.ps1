#Requires -Version 7.0
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$scratch=Join-Path $root '.local-tests/dismount-safety'
[IO.Directory]::CreateDirectory($scratch)|Out-Null
$harmony=Join-Path $root '0_TFP_Harmony/0Harmony.dll'
. "$PSScriptRoot/Build-RuntimeFixRoslyn.ps1" -OutputPath "$scratch/AEC.T16.RuntimeFix.dll"
$managed=Join-Path (Split-Path $root) '7DaysToDie_Data/Managed'
$refs=@('mscorlib.dll','System.dll','System.Core.dll')|ForEach-Object {Join-Path $managed $_}
[RuntimeFixCompiler]::Build(@("$root/99-AEC_T16_RuntimeFix/Source/VehicleDismountSafety.cs","$PSScriptRoot/VehicleDismountSafetyFixture.cs"),($refs+@($harmony)),"$scratch/Fixture.dll")
$runner=Join-Path $scratch 'Run.ps1'
Set-Content -LiteralPath $runner -Encoding utf8 -Value @'
param($HarmonyPath,$FixturePath)
$ErrorActionPreference='Stop'
[void][Reflection.Assembly]::LoadFrom($HarmonyPath)
[void][Reflection.Assembly]::LoadFrom($FixturePath)
[DismountTests]::Run()
'@
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $runner $harmony "$scratch/Fixture.dll"
if($LASTEXITCODE -ne 0){throw 'Dismount fixture failed'}
Add-Type -Path "$root/0_TFP_Harmony/Mono.Cecil.dll"
$game=[Mono.Cecil.AssemblyDefinition]::ReadAssembly("$managed/Assembly-CSharp.dll")
try {
 $vehicle=$game.MainModule.Types|Where-Object Name -eq EntityVehicle
 $detach=$vehicle.Methods|Where-Object Name -eq DetachEntity
 $ops=@($detach.Body.Instructions)
 $find=@($ops|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'FindAttachSlot'})
 $enable=@($ops|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'CharacterControllerAbstract' -and $_.Operand.Name -eq 'Enable'})
 $remove=@($ops|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'Entity' -and $_.Operand.Name -eq 'DetachEntity'})
 if($find.Count -ne 1 -or $enable.Count -ne 1 -or $remove.Count -ne 1 -or $find[0].Offset -ge $enable[0].Offset -or $enable[0].Offset -ge $remove[0].Offset){throw 'Native detach order changed'}
 $entity=$game.MainModule.Types|Where-Object Name -eq Entity
 $baseDetach=$entity.Methods|Where-Object Name -eq Detach
 $clear=@($baseDetach.Body.Instructions|Where-Object {$_.OpCode.Name -eq 'stfld' -and $_.Operand.Name -eq 'AttachedToEntity'})
 $call=@($baseDetach.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'DetachEntity'})
 if($clear.Count -ne 1 -or $call.Count -ne 1 -or $clear[0].Offset -ge $call[0].Offset){throw 'Native attachment clear order changed'}
 $collision=$vehicle.Methods|Where-Object Name -eq OnCollisionForward
 if(($collision.Parameters.ParameterType.Name -join ',') -ne 'Transform,Collision,Boolean'){throw 'Collision callback signature changed'}
 $damage=@($collision.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'DamageEntity'})
 if($damage.Count -ne 1){throw 'Native collision damage route changed'}
 Write-Output 'PASS: installed game detach ordering, controller activation and collision damage route verified.'
} finally {$game.Dispose()}
