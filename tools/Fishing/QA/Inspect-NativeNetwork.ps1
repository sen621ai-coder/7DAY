#Requires -Version 7.0
$ErrorActionPreference='Stop'
$mods=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$assembly=Join-Path (Split-Path $mods) '7DaysToDie_Data/Managed/Assembly-CSharp.dll'
Add-Type -Path (Join-Path $mods '0_TFP_Harmony/Mono.Cecil.dll')
$module=[Mono.Cecil.ModuleDefinition]::ReadModule($assembly)
$lines=[Collections.Generic.List[string]]::new()
function Check-Native([bool]$value,[string]$label){if(!$value){throw "FAIL $label"};$lines.Add("PASS $label")}
try {
    $lines.Add('Assembly '+$module.Assembly.FullName)
    $lines.Add('MVID '+$module.Mvid)
    $lines.Add('SHA256 '+(Get-FileHash $assembly).Hash)
    $net=$module.Types | Where-Object Name -eq 'NetPackage'
    foreach($name in @('get_PackageDirection','get_ReliableDelivery','get_AllowedBeforeAuth')) {
        $method=$net.Methods | Where-Object Name -eq $name
        Check-Native ($null -ne $method -and $method.IsVirtual) ('NetPackage.'+$name+' overridable')
    }
    $reliable=$net.Methods | Where-Object Name -eq 'get_ReliableDelivery'
    Check-Native ($reliable.Body.Instructions[0].OpCode.Name -eq 'ldc.i4.1') 'game default delivery is reliable; F explicitly keeps it enabled'
    $auth=$net.Methods | Where-Object Name -eq 'get_AllowedBeforeAuth'
    Check-Native ($auth.Body.Instructions[0].OpCode.Name -eq 'ldc.i4.0') 'game disallows pre-auth package; F explicitly keeps it disabled'
    Check-Native ([bool]($net.Methods | Where-Object Name -eq 'get_Sender')) 'native Sender available outside payload'
    $client=$module.Types | Where-Object Name -eq 'ClientInfo'
    foreach($name in @('entityId','loginDone','bAttachedToEntity','PlatformId','CrossplatformId')) {
        Check-Native ([bool]($client.Fields | Where-Object Name -eq $name)) ('ClientInfo.'+$name+' available')
    }
    Check-Native ([bool]($client.Methods | Where-Object Name -eq 'SendPackage')) 'targeted ClientInfo.SendPackage available'
    $connection=$module.Types | Where-Object Name -eq 'ConnectionManager'
    Check-Native ([bool]($connection.Methods | Where-Object Name -eq 'SendToServer')) 'client SendToServer available'
    Check-Native ([bool]($connection.Fields | Where-Object Name -eq 'OnClientDisconnected')) 'A can bind native disconnect lifecycle'
    $output=Join-Path $PSScriptRoot 'artifacts/native-network-api.txt'
    New-Item -ItemType Directory -Force (Split-Path $output) | Out-Null
    $lines | Set-Content -LiteralPath $output -Encoding utf8
    $lines
} finally {$module.Dispose()}
