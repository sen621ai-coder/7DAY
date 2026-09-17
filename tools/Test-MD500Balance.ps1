$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$culture = [Globalization.CultureInfo]::InvariantCulture
$patchDir = Join-Path $root 'ZZ-PZAEC_MD500Compatibility'
[xml]$metadata = Get-Content (Join-Path $patchDir 'ModInfo.xml') -Raw
if ($metadata.xml.Name.value -ne 'ZZPZAECMD500Compatibility') { throw 'Wrong mod identity' }
[xml]$aec = Get-Content (Join-Path $root '07-AEC-Vehicles-NoMicrocraft/Config/vehicles.xml') -Raw
$baseline = $aec.SelectSingleNode("//vehicle[@name='AECArmoredGyroVehicle']/property[@name='velocityMax_turbo']")
$expected = @($baseline.value.Split(',') | ForEach-Object { [double]::Parse($_.Trim(), $culture) * 1.3 })
[xml]$speed = Get-Content (Join-Path $patchDir 'Config/vehicles.xml') -Raw
[xml]$health = Get-Content (Join-Path $patchDir 'Config/items.xml') -Raw
foreach ($doc in @($speed, $health)) {
    if ($doc.SelectNodes('/configs/conditional/if').Count -ne 1 -or
        $doc.SelectSingleNode('/configs/conditional/if').cond -ne "mod_loaded('MD-500')") {
        throw 'Patches must be conditional on the independent MD-500 mod'
    }
    if ($doc.SelectNodes('//*[@xpath]').Count -ne 1) { throw 'Unexpected extra changes' }
}
$speedSet = $speed.SelectSingleNode('//set')
$actual = @($speedSet.InnerText.Split(',') | ForEach-Object { [double]::Parse($_.Trim(), $culture) })
if ($actual.Count -ne 4) { throw 'Speed tuple must contain four values' }
for ($i = 0; $i -lt 4; $i++) {
    if ([Math]::Abs($actual[$i] - $expected[$i]) -gt .00001) { throw "Speed component $i is not AEC baseline +30%" }
}
if ($speedSet.xpath -ne "/vehicles/vehicle[@name='vehicleMD500']/property[@name='velocityMax_turbo']/@value") { throw 'Speed patch scope changed' }
$healthSet = $health.SelectSingleNode('//set')
if ($healthSet.InnerText -ne '1000000' -or
    $healthSet.xpath -ne "/items/item[@name='vehicleMD500placeable']/effect_group/passive_effect[@name='DegradationMax']/@value") { throw 'Health or health scope mismatch' }

# Apply the exact XPath patches to in-memory copies of the installed author files.
# No redistribution of third-party assets; skip this part if MD-500 is not installed.
foreach ($file in @('vehicles.xml','items.xml')) {
    $authorPath = Join-Path $root "MD-500/Config/$file"
    if (!(Test-Path $authorPath)) { Write-Output "SKIP author XPath test: $file not installed"; continue }
    [xml]$author = Get-Content $authorPath -Raw
    $merged = [xml]::new()
    $rootName = if ($file -eq 'vehicles.xml') { 'vehicles' } else { 'items' }
    [void]$merged.AppendChild($merged.CreateElement($rootName))
    foreach ($node in $author.SelectNodes('/configs/append/*')) {
        [void]$merged.DocumentElement.AppendChild($merged.ImportNode($node, $true))
    }
    $operation = if ($file -eq 'vehicles.xml') { $speedSet } else { $healthSet }
    $targets = $merged.SelectNodes($operation.xpath)
    if ($targets.Count -ne 1) { throw "Expected exactly one author patch target in $file" }
    $targets[0].Value = $operation.InnerText
    if ($merged.SelectSingleNode($operation.xpath).Value -ne $operation.InnerText) { throw 'Patch did not apply' }
}
Write-Output 'PASS: MD-500 maximum durability 1,000,000; all four horizontal speed limits exactly AEC Armored Gyro x1.30; optional dependency and exact scope verified; author files unchanged.'
