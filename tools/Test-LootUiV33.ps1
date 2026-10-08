#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
[xml]$effective = Get-Content (Join-Path (Split-Path $root) 'Data/Config/XUi_InGame/windows.xml') -Raw
foreach ($file in @('01-ProjectZ/Config/XUi_InGame/windows.xml', '98-AECxProjectZ_Tweaks/Config/XUi_InGame/windows.xml')) {
    [xml]$patch = Get-Content (Join-Path $root $file) -Raw
    foreach ($op in $patch.DocumentElement.ChildNodes) {
        if (!$op.xpath -or !$op.xpath.Contains("windowLooting")) { continue }
        $targets = @($effective.SelectNodes($op.xpath))
        if (!$targets.Count) { throw "Loot patch misses installed game XML: $file : $($op.xpath)" }
        foreach ($target in $targets) {
            switch ($op.Name) {
                'set' { $target.Value = $op.InnerText }
                'remove' { [void]$target.ParentNode.RemoveChild($target) }
                'append' { foreach ($child in $op.ChildNodes) { [void]$target.AppendChild($effective.ImportNode($child, $true)) } }
                default { throw "Unsupported operation $($op.Name)" }
            }
        }
    }
}
$window = $effective.SelectSingleNode("/windows/window[@name='windowLooting']")
$grid = $window.SelectSingleNode("rect[@name='content']/scrollview/grid[@name='queue']")
if (!$grid -or $grid.rows -ne '13' -or $grid.cols -ne '15') { throw '195-slot loot grid missing from native scrollview' }
if ($grid.cell_width -ne '54' -or $grid.cell_height -ne '36') { throw 'Compact slot dimensions lost' }
if ($grid.SelectNodes('backpack_item_stack').Count -ne 1 -or $grid.SelectNodes('item_stack').Count) { throw 'Incorrect slot templates' }
if ($window.SelectNodes("rect[@name='header']/rect[@controller='ContainerStandardControls']").Count -ne 1) { throw 'Duplicate storage controls' }
if ($window.width -notmatch '\* 54' -or $window.SelectSingleNode('rect/scrollview').width -notmatch '\* 54') { throw 'Scroll bounds differ from slot width' }
$bar = $window.SelectSingleNode('rect/defaultscrollbar')
if ($bar.pos -notmatch '\* 54' -or $bar.barheight -notmatch '\* 36' -or $bar.thumbheight -notmatch '\* 36') { throw 'Scrollbar dimensions differ from slots' }
'PASS: installed V3.3 loot patches all match; 195 compact slots, native scrollview, matching scrollbar and one control set.'
