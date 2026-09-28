#Requires -Version 7.0
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
[xml]$config=Get-Content -LiteralPath (Join-Path $root 'ZZ-PZAEC_M1Abrams/Config/vehicles.xml')
$vehicles=@($config.SelectNodes('/configs/append/vehicle'))
if($vehicles.Count -ne 4){throw 'Expected four M1 tiers'}
foreach($v in $vehicles){
 $parts=@($v.SelectNodes('property[@class="storage"]'))
 if($parts.Count -ne 1 -or $parts[0].SelectSingleNode('property[@name="class"]').value -ne 'Storage'){throw "Missing native storage part: $($v.name)"}
 if($parts[0].SelectNodes('property[starts-with(@name,"mod")]').Count){throw 'Cargo must not require an optional storage mod'}
}
Write-Output 'PASS all four M1 tiers have unconditional native Storage parts.'
