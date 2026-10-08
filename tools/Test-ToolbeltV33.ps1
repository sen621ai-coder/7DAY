#Requires -Version 7.0
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
[xml]$base=Get-Content (Join-Path (Split-Path $root) 'Data/Config/XUi_InGame/windows.xml') -Raw
[xml]$project=Get-Content (Join-Path $root '01-ProjectZ/Config/XUi_InGame/windows.xml') -Raw
[xml]$patch=Get-Content (Join-Path $root '99-AEC_T16_RuntimeFix/Config/XUi_InGame/windows.xml') -Raw
$effective=[xml]'<windows />'
$original=$project.SelectSingleNode("//window[@name='windowToolbelt']")
[void]$effective.DocumentElement.AppendChild($effective.ImportNode($original,$true))
foreach($op in $patch.configs.ChildNodes){
    if(!$op.xpath -or !$op.xpath.Contains('windowToolbelt')){continue}
    $targets=@($effective.SelectNodes($op.xpath))
    if($targets.Count -ne 1){throw "Expected one target: $($op.xpath)"}
    foreach($target in $targets){
        if($op.Name -eq 'remove'){[void]$target.ParentNode.RemoveChild($target)}
        elseif($op.Name -eq 'append'){foreach($child in $op.ChildNodes){[void]$target.AppendChild($effective.ImportNode($child,$true))}}
    }
}
function Canonical($node){
    $attrs=@($node.Attributes | Sort-Object Name | ForEach-Object { $_.Name+'='+$_.Value }) -join '|'
    $children=@($node.ChildNodes | Where-Object NodeType -eq Element | ForEach-Object {Canonical $_}) -join ''
    return '<'+$node.Name+' '+$attrs+'>'+$children+'</'+$node.Name+'>'
}
$path="/windows/window[@name='windowToolbelt']/rect/rect[@controller='Toolbelt']"
if((Canonical $effective.SelectSingleNode($path)) -cne (Canonical $base.SelectSingleNode($path))){throw 'Toolbelt differs from installed game controller structure'}
if($effective.SelectNodes('//@*') | Where-Object Value -Match 'secondrow'){throw 'Obsolete secondrow binding remains'}
if($effective.SelectSingleNode("//rect[@name='bars']").repeat_count -ne '{%beltCount}'){throw 'Missing native bar repetition'}
if($effective.SelectSingleNode("//rect[@visible='{cvar(`$ComboStatus:true;false;false)}']") -eq $null){throw 'ProjectZ combo HUD lost'}
'PASS ProjectZ toolbelt patch matches installed V3.3 structure; no secondrow; combo HUD preserved.'
