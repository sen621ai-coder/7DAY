#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$bundleRoot = Split-Path -Parent $PSScriptRoot
$bundleManaged = Join-Path (Split-Path -Parent $bundleRoot) '7DaysToDie_Data/Managed'
Get-ChildItem -LiteralPath $bundleManaged -Filter '*.dll' | ForEach-Object {
    try { [void][Reflection.Assembly]::LoadFrom($_.FullName) } catch { }
}
[xml]$bundleXml = Get-Content -Raw (Join-Path $bundleRoot '99-AEC_T16_RuntimeFix/Config/items.xml')
$bundleNodes = @($bundleXml.SelectNodes('//item[starts-with(@name,"itemPZAECTestBundle")]'))
foreach ($bundleNode in $bundleNodes) {
    $bundleProperties = [DynamicProperties]::new()
    foreach ($bundleProperty in $bundleNode.SelectNodes('property[@class="Action0"]/property')) {
        $bundleProperties.Values.Add([string]$bundleProperty.name, [string]$bundleProperty.value)
    }
    $bundleAction = [ItemActionOpenBundle]::new()
    [ItemActionOpenBundle].GetMethod('ReadFrom').Invoke($bundleAction, [object[]]@($bundleProperties))
    $bundleNames = [ItemActionOpenBundle].GetField('CreateItem').GetValue($bundleAction)
    $bundleCounts = [ItemActionOpenBundle].GetField('CreateItemCount').GetValue($bundleAction)
    $bundleConsume = [ItemActionOpenBundle].GetField('Consume').GetValue($bundleAction)
    if ($bundleNames.Length -eq 0 -or $bundleNames.Length -ne $bundleCounts.Length -or !$bundleConsume) {
        throw "Native bundle parser rejected $($bundleNode.name)"
    }
}
Write-Output "PASS: all $($bundleNodes.Count) test bundles parsed by native ItemActionOpenBundle.ReadFrom."
