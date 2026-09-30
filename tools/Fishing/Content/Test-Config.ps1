#Requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$modsRoot=Split-Path (Split-Path (Split-Path $PSScriptRoot))
$fishingRoot=Join-Path $modsRoot 'ZZZ-PZAEC_Fishing'
$gameConfig=Join-Path (Split-Path $modsRoot) 'Data/Config'
$artifactPath=Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Path $artifactPath -Force | Out-Null
$modDirs=@(Get-ChildItem $modsRoot -Directory | Where-Object { (Test-Path (Join-Path $_.FullName 'ModInfo.xml')) -or $_.Name -eq 'ZZZ-PZAEC_Fishing' } | Sort-Object Name)
$modNames=@($modDirs.Name)
foreach($mod in $modDirs){
    $infoPath=Join-Path $mod.FullName 'ModInfo.xml'
    if(Test-Path $infoPath){$info=[xml][IO.File]::ReadAllText($infoPath); $node=$info.SelectSingleNode('//Name'); if($node){$modNames+= $node.GetAttribute('value')}}
}
$misses=[Collections.Generic.List[string]]::new()
$operationCount=0
function Apply-Patch([xml]$document, [System.Xml.XmlElement]$operation, [string]$source){
    if($operation.LocalName -eq 'conditional'){
        foreach($branch in $operation.ChildNodes){
            if($branch.NodeType -ne 'Element'){continue}
            if($branch.LocalName -ne 'if' -or $branch.GetAttribute('cond') -notmatch "^mod_loaded\('([^']+)'\)$"){throw "Unsupported conditional: $source"}
            if($modNames -contains $Matches[1]){foreach($child in $branch.ChildNodes){if($child.NodeType -eq 'Element'){Apply-Patch $document $child $source}}}
        }
        return
    }
    $xpath=$operation.GetAttribute('xpath')
    if(!$xpath){throw "Missing XPath in $source"}
    $targets=@($document.SelectNodes($xpath))
    if(!$targets.Count){
        if($source.Contains('ZZZ-PZAEC_Fishing')){throw "Fishing patch target not found: $xpath"}
        $misses.Add("${source}: $xpath")
    }
    foreach($target in $targets){
        switch -CaseSensitive ($operation.LocalName){
            'set' {$target.InnerText=$operation.InnerText}
            'setattribute' {$target.SetAttribute($operation.GetAttribute('name'),$operation.InnerText)}
            'remove' {if($target -is [Xml.XmlAttribute]){$target.OwnerElement.RemoveAttributeNode($target)|Out-Null}else{$target.ParentNode.RemoveChild($target)|Out-Null}}
            'append' {foreach($child in $operation.ChildNodes){if($child.NodeType -eq 'Element'){$target.AppendChild($document.ImportNode($child,$true))|Out-Null}}}
            'insertBefore' {foreach($child in $operation.ChildNodes){if($child.NodeType -eq 'Element'){$target.ParentNode.InsertBefore($document.ImportNode($child,$true),$target)|Out-Null}}}
            'insertAfter' {$anchor=$target; foreach($child in $operation.ChildNodes){if($child.NodeType -eq 'Element'){$anchor=$target.ParentNode.InsertAfter($document.ImportNode($child,$true),$anchor)}}}
            default {throw "Unsupported patch operation $($operation.LocalName) in $source"}
        }
    }
    $script:operationCount++
}
$merged=@{}
foreach($file in 'items','recipes','materials','buffs','blocks'){
    $document=[xml][IO.File]::ReadAllText((Join-Path $gameConfig "$file.xml"))
    foreach($mod in $modDirs){
        $path=Join-Path $mod.FullName "Config/$file.xml"
        if(!(Test-Path $path)){continue}
        $patch=[xml][IO.File]::ReadAllText($path)
        if($patch.DocumentElement.Name -notin 'configs','config'){throw "Unsupported patch root in $path"}
        foreach($op in $patch.DocumentElement.ChildNodes){if($op.NodeType -eq 'Element'){Apply-Patch $document $op "$($mod.Name)/Config/$file.xml"}}
    }
    $merged[$file]=$document
}
function Need([bool]$condition,[string]$message){if(!$condition){throw $message}}
$definitions=[xml][IO.File]::ReadAllText((Join-Path $fishingRoot 'Config/items.xml'))
$recipes=[xml][IO.File]::ReadAllText((Join-Path $fishingRoot 'Config/recipes.xml'))
$loc=Import-Csv (Join-Path $fishingRoot 'Config/Localization.csv')
$expected=@('pzaecFishingRodBasic','pzaecFishingBaitWorm','pzaecFishingFishCarp','pzaecFishingMealGrilled')
foreach($id in $expected){
    $nodes=@($merged.items.SelectNodes("/items/item[@name='$id']"))
    Need ($nodes.Count -eq 1) "Missing or duplicate item $id"
    $item=$nodes[0]
    foreach($key in @($id,($id+'Desc'))){
        $rows=@($loc | Where-Object Key -eq $key)
        Need ($rows.Count -eq 1 -and $rows[0].English -and $rows[0].Schinese) "Missing bilingual localization $key"
    }
    $material=$item.SelectSingleNode("property[@name='Material']").GetAttribute('value')
    Need ($null -ne $merged.materials.SelectSingleNode("/materials/material[@id='$material']")) "Unknown material $material"
    $icon=$item.SelectSingleNode("property[@name='CustomIcon']").GetAttribute('value')
    Need ($null -ne $merged.items.SelectSingleNode("/items/item[@name='$icon']")) "Unknown native icon item $icon"
    Need ($item.SelectSingleNode("property[@name='SellableToTrader']").GetAttribute('value') -eq 'false') "Unexpected trader money loop $id"
    if($id -ne 'pzaecFishingMealGrilled'){Need ($null -eq $item.SelectSingleNode("property[@class='Action0']")) "Unexpected native attack/eat action $id"}
    foreach($buff in $item.SelectNodes('.//*[@buff]')){
        $name=$buff.GetAttribute('buff')
        Need ($null -ne $merged.buffs.SelectSingleNode("/buffs/buff[@name='$name']")) "Unknown food buff $name"
    }
}
foreach($recipe in $recipes.SelectNodes('/configs/append/recipe')){
    $id=$recipe.GetAttribute('name')
    $nodes=@($merged.recipes.SelectNodes("/recipes/recipe[@name='$id']"))
    Need ($nodes.Count -eq 1) "Recipe missing or duplicated $id"
    $actual=$nodes[0]
    Need (!$actual.GetAttribute('tags').Contains('learnable')) "Unexpected locked starter recipe $id"
    Need ([int]$actual.GetAttribute('count') -gt 0) "Invalid recipe output $id"
    foreach($ingredient in $actual.SelectNodes('ingredient')){
        $name=$ingredient.GetAttribute('name')
        Need ($null -ne $merged.items.SelectSingleNode("/items/item[@name='$name']")) "Unknown ingredient $name"
        Need ([int]$ingredient.GetAttribute('count') -gt 0) "Invalid ingredient count $name"
    }
    if($actual.HasAttribute('craft_tool')){
        $name=$actual.GetAttribute('craft_tool'); Need ($null -ne $merged.items.SelectSingleNode("/items/item[@name='$name']")) "Unknown cooking tool $name"
    }
    if($actual.HasAttribute('craft_area')){
        $name=$actual.GetAttribute('craft_area'); Need ($null -ne $merged.blocks.SelectSingleNode("/blocks/block[@name='$name']")) "Unknown crafting station $name"
    }
}
Need ($null -eq $merged.recipes.SelectSingleNode("/recipes/recipe[@name='pzaecFishingFishCarp']")) 'Whole fish must come from authoritative catches, not crafting.'
$meal=$merged.items.SelectSingleNode("/items/item[@name='pzaecFishingMealGrilled']")
Need ($meal.SelectSingleNode("property[@class='Action0']/property[@name='Class']").GetAttribute('value') -eq 'Eat') 'Meal is not edible.'
$report=[ordered]@{
    scope='Offline XPath patch simulation; not the native game XML loader or live gameplay.'
    mods=$modDirs.Name; operations=$operationCount; itemCount=$expected.Count; recipeCount=3
    preexistingUnmatchedPatchTargets=$misses.Count
    verified='Unique definitions; ingredient/material/buff/crafting references; bilingual localization; no native attack/eat on rod/bait/catch; no trader loop.'
    unverified='Native resource atlas/prefab load; crafting UI; consumed food effects; active fishing; loaded user-directory mods; actual native load order.'
}
$report | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $artifactPath 'config-check.json') -Encoding utf8
$misses | Set-Content (Join-Path $artifactPath 'preexisting-unmatched-targets.txt') -Encoding utf8
Write-Output "PASS: 4 items / 3 recipes; $operationCount patch operations simulated across $($modDirs.Count) workspace mods."
Write-Output "NOTE: $($misses.Count) pre-existing unmatched patch targets logged; native game validation remains separate."
