param([string]$Types='Vehicle,EntityVehicle,XUiM_Vehicle,XUiC_ItemStack,ItemClassModifier,ItemValue',[string]$Methods='')
Add-Type -Path "$PSScriptRoot/../../0_TFP_Harmony/Mono.Cecil.dll"
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly("$PSScriptRoot/../../../7DaysToDie_Data/Managed/Assembly-CSharp.dll")
foreach($t in $a.MainModule.Types|Where-Object {$_.Name -in $Types.Split(',')}){
$t.FullName
if(!$Methods){$t.Fields|ForEach-Object {"FIELD $_"};$t.Methods|ForEach-Object {"METHOD $_"}}
else {foreach($m in $t.Methods|Where-Object {$_.Name -match $Methods}){"METHOD $m";$m.Body.Instructions|ForEach-Object ToString}}
}
$a.Dispose()
