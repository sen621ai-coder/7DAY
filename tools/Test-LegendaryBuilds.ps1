#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Test-LegendaryDefense.ps1')

# Use the installed game's effect parser/calculator and item wire format.
# This does not start Unity or claim to test animation, UI or multiplayer live.
Add-Type -ReferencedAssemblies ($references + $frameworkReferences) -TypeDefinition @'
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

public static class BuildRegression
{
    static void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
    static FastTags<TagGroup.Global> Tags(string value) { return FastTags<TagGroup.Global>.Parse(value); }
    static float Value(MinEffectController effects,string effect,string tags,float initial=100,float quality=1)
    {
        float b=initial,p=1;
        effects.ModifyValue(null,(PassiveEffects)Enum.Parse(typeof(PassiveEffects),effect),ref b,ref p,quality,Tags(tags));
        return b*p;
    }
    static void Near(float actual,float expected,string why) { Check(Math.Abs(actual-expected)<.001f,why+": "+actual+" != "+expected); }
    public static bool Fits(string install,string blocked,string candidate)
    { return Tags(install).Test_AnySet(Tags(candidate)) && !Tags(blocked).Test_AnySet(Tags(candidate)); }

    public static string Effects(string xml)
    {
        var mods=XDocument.Parse(xml).Descendants("item_modifier")
            .Where(n => System.Text.RegularExpressions.Regex.IsMatch((string)n.Attribute("name") ?? "",
                @"^modPZAEC(Precision|Breaker|Barrage|Skirmisher)R[2-5]$"))
            .ToArray(); int cases=0;
        Check(mods.Length==16,"Expected 16 cores");
        foreach(var node in mods)
        {
            string id=(string)node.Attribute("name"); int rank=id[id.Length-1]-'0',i=rank-2;
            var effects=MinEffectController.ParseXml(node,null,MinEffectController.SourceParentType.ItemModifierClass);
            Check(!effects.IsOwnerTiered() && !effects.HasEvents(),"Core leaked rank dependence or event state");
            Check(effects.EffectGroups.Sum(g=>g.PassiveEffects.Count)==node.Descendants("passive_effect").Count(),"Effect silently ignored by native parser");
            foreach(float quality in new[]{0,1,2,3,4,5,6,19})
            {
                if(id.Contains("Precision")) {
                    Near(Value(effects,"HeadshotDamageModifier","head",100,quality),100*(1+new[]{.3f,.45f,.6f,.8f}[i]),"Head bonus");
                    Near(Value(effects,"HeadshotDamageModifier","torso",100,quality),100,"Head bonus applied to body");
                    Near(Value(effects,"SpreadMultiplierAiming","",100,quality),100*(1-new[]{.1f,.15f,.2f,.25f}[i]),"Aim spread");
                    Near(Value(effects,"RoundsPerMinute","",100,quality),85,"Fire-rate trade-off");
                    Near(Value(effects,"SpreadMultiplierHip","",100,quality),120,"Hip trade-off");
                } else if(id.Contains("Breaker")) {
                    Near(Value(effects,"EntityDamage","secondary",100,quality),100*(1+new[]{.35f,.5f,.7f,.9f}[i]),"Power hit");
                    Near(Value(effects,"EntityDamage","primary",100,quality),90,"Normal-hit trade-off");
                    Near(Value(effects,"EntityDamage","",100,quality),100,"Unscoped damage changed");
                    Near(Value(effects,"StaminaLoss","secondary",100,quality),115,"Power stamina trade-off");
                    Near(Value(effects,"StaminaLoss","primary",100,quality),100,"Normal stamina changed");
                } else if(id.Contains("Barrage")) {
                    Near(Value(effects,"RoundsPerMinute","",100,quality),100*(1+new[]{.15f,.2f,.25f,.3f}[i]),"Fire-rate bonus");
                    Near(Value(effects,"MagazineSize","",100,quality),100*(1+new[]{.1f,.2f,.3f,.4f}[i]),"Magazine bonus");
                    Near(Value(effects,"ReloadSpeedMultiplier","",100,quality),90,"Reload trade-off");
                    Near(Value(effects,"SpreadMultiplierHip","",100,quality),115,"Hip trade-off");
                } else if(id.Contains("Skirmisher")) {
                    Near(Value(effects,"StaminaMax","",100,quality),100+new[]{30,45,60,80}[i],"Stamina bonus");
                    Near(Value(effects,"RunSpeed","",100,quality),100*(1+new[]{.04f,.06f,.08f,.1f}[i]),"Run bonus");
                    Near(Value(effects,"PhysicalDamageResist","",50,quality),48,"Armor trade-off");
                } else throw new Exception("Unexpected core");
                Near(Value(effects,"BlockDamage","",100,quality),100,"Unrelated building damage changed"); cases++;
            }
            foreach(var other in mods)
                Check(Tags((string)node.Attribute("modifier_tags")).Test_AnySet(Tags((string)other.Attribute("modifier_tags"))),"Core types/ranks can stack on one item");
        }
        using(var game=Mono.Cecil.ModuleDefinition.ReadModule(typeof(ItemValue).Assembly.Location)) {
            foreach(var pair in new[]{new[]{"XUiM_AssembleItem","AddPartToItem"},new[]{"XUiC_ItemPartStack","CanSwap"}}) {
                var method=game.Types.Single(t=>t.Name==pair[0]).Methods.Single(m=>m.Name==pair[1]);
                foreach(string field in new[]{"ModifierTags","InstallableTags","DisallowedTags","MaxModsAllowed"})
                    Check(method.Body.Instructions.Any(op=>op.Operand is Mono.Cecil.FieldReference f && f.Name==field),"Native assembly gate changed: "+field);
            }
        }
        return "PASS: native effect parser/calculator; "+cases+" fixed-rank quality cases, attack-tag isolation, trade-offs and 256 mutual-exclusion pairs; both native installation gates verified.";
    }

    public static string Wire(string xml)
    {
        var mods=XDocument.Parse(xml).Descendants("item_modifier").ToArray();
        var old=ItemClass.list; int count=0;
        try {
            ItemClass.list=new ItemClass[Block.ItemsStartHere+100];
            int hostId=Block.ItemsStartHere+1;
            ItemClass.list[hostId]=(ItemClass)RuntimeHelpers.GetUninitializedObject(typeof(ItemClass));
            for(int i=0;i<mods.Length;i++) {
                int modId=Block.ItemsStartHere+2+i;
                ItemClass.list[modId]=(ItemClassModifier)RuntimeHelpers.GetUninitializedObject(typeof(ItemClassModifier));
                foreach(ushort quality in new ushort[]{1,5,6}) {
                    var value=new ItemValue {type=hostId,Quality=quality,Modifications=new[]{new ItemValue{type=modId}},CosmeticMods=Array.Empty<ItemValue>()};
                    using(var stream=new MemoryStream()) {
                        var writer=new BinaryWriter(stream); value.Write(writer); writer.Flush(); stream.Position=0;
                        var read=new ItemValue(); read.Read(new BinaryReader(stream));
                        Check(read.type==hostId && read.Quality==quality && read.Modifications.Length==1 && read.Modifications[0].type==modId,"Core identity/rank lost in native serialization");
                        Check(read.Modifications[0].ItemClass is ItemClassModifier && stream.Position==stream.Length,"Modifier payload misaligned");
                    }
                    count++;
                }
            }
        } finally { ItemClass.list=old; }
        return "PASS: "+count+" native ItemValue save/network round-trips preserve fixed core identity on Q1/Q5/Q6 equipment.";
    }
}
'@

[xml]$buildMods=Get-Content (Join-Path $configRoot 'item_modifiers.xml') -Raw
[BuildRegression]::Effects($buildMods.OuterXml)
[BuildRegression]::Wire($buildMods.OuterXml)

function Assert-Build([bool]$condition,[string]$message) { if(-not $condition){throw $message} }
function Apply-BuildPatch([xml]$document,[xml]$patch) {
    foreach($op in $patch.DocumentElement.ChildNodes) {
        if($op.NodeType -ne 'Element'){continue}
        foreach($target in @($document.SelectNodes($op.GetAttribute('xpath')))) {
            switch($op.LocalName) {
                'set' {$target.InnerText=$op.InnerText}
                'setattribute' {$target.SetAttribute($op.GetAttribute('name'),$op.InnerText)}
                'remove' {[void]$target.ParentNode.RemoveChild($target)}
                'append' {foreach($child in $op.ChildNodes){if($child.NodeType -eq 'Element'){[void]$target.AppendChild($document.ImportNode($child,$true))}}}
                'insertAfter' {$anchor=$target; foreach($child in $op.ChildNodes){if($child.NodeType -eq 'Element'){$anchor=$target.ParentNode.InsertAfter($document.ImportNode($child,$true),$anchor)}}}
                default {throw "Unsupported build test patch: $($op.LocalName)"}
            }
        }
    }
}
[xml]$buildMergedItems=Get-Content (Join-Path $modRoot '../Data/Config/items.xml') -Raw
foreach($directory in Get-ChildItem -LiteralPath $modRoot -Directory | Sort-Object Name) {
    $file=Join-Path $directory.FullName 'Config/items.xml'
    if(Test-Path -LiteralPath $file){Apply-BuildPatch $buildMergedItems ([xml](Get-Content $file -Raw))}
}
$buildItemMap=@{}; foreach($item in $buildMergedItems.SelectNodes('/items/item')){$buildItemMap[$item.name]=$item}
function Get-BuildProperty([string]$id,[string]$property) {
    $seen=@{}
    while($buildItemMap.ContainsKey($id) -and -not $seen.ContainsKey($id)) {
        $seen[$id]=$true; $item=$buildItemMap[$id]; $prop=$item.SelectNodes("property[@name='$property']")
        if($prop.Count){return [string]$prop[$prop.Count-1].value}
        $id=[string]$item.SelectSingleNode("property[@name='Extends']").value
    }
    return ''
}
$corePattern='^modPZAEC(Precision|Breaker|Barrage|Skirmisher)R[2-5]$'
$coreNodes=@($buildMods.SelectNodes('//item_modifier') | Where-Object name -match $corePattern)
$coreRecipes=@($recipes.SelectNodes('//recipe') | Where-Object name -match $corePattern)
Assert-Build ($coreRecipes.Count -eq 28) 'Missing direct or upgrade core recipes'
Assert-Build ($items.SelectNodes('//item[starts-with(@name,"PZAECBuildParts")]').Count -eq 4) 'Wrong component count'
foreach($core in $coreNodes) {
    $rank=[int]$core.name.Substring($core.name.Length-1); $tier=$rank+14; $id=$core.name
    Assert-Build ($rank -in 2..5 -and $core.SelectSingleNode("property[@name='MaxModsAllowed']").value -eq '1') 'Wrong fixed rank/stack limit'
    Assert-Build ($core.SelectNodes('effect_group/triggered_effect | item_property_overrides').Count -eq 0) 'Unexpected stateful effect/weapon mutation'
    $recipe=$recipes.SelectSingleNode("//recipe[@name='$id']")
    Assert-Build ($recipe.count -eq '1' -and $recipe.craft_area -eq 'workbench' -and $recipe.always_unlocked -eq 'true' -and $recipe.use_ingredient_modifier -eq 'false' -and $recipe.craft_time -eq '60') 'Unsafe/missing deterministic crafting'
    $costs=@{("PZAECBuildPartsR$rank")=6;resourceLegendaryParts=@(5,10,20,35)[$rank-2];resourceForgedSteel=@(20,30,40,50)[$rank-2];resourceElectricParts=20}
    Assert-Build ($recipe.SelectNodes('ingredient').Count -eq 4) 'Unexpected ingredient count'
    foreach($entry in $costs.GetEnumerator()) {
        Assert-Build ($buildItemMap.ContainsKey($entry.Key) -and [int]$recipe.SelectSingleNode("ingredient[@name='$($entry.Key)']").count -eq $entry.Value) "Wrong/missing cost $id / $($entry.Key)"
    }
    foreach($key in @($id,"${id}Desc")) {Assert-Build ($localization.ContainsKey($key) -and $localization[$key].schinese -match "$rank" -and $localization[$key].english.Length -gt 0) "Missing core text $key"}
    Assert-Build ($localization["${id}Desc"].schinese -match '代价') 'Trade-off omitted from description'
    Assert-Build (-not [BuildRegression]::Fits($core.installable_tags,$core.blocked_tags,'noMods,gun,armorChest,perkPummelPete')) 'noMods bypass'
    Assert-Build (-not [BuildRegression]::Fits($core.installable_tags,$core.blocked_tags,'launcher,perkDemolitionsExpert')) 'Explosive launcher accepted'
    if($id -match 'Skirmisher') {
        Assert-Build ($core.installable_tags -eq 'armorChest') 'Mobility must occupy a single armor slot'
        Assert-Build (-not [BuildRegression]::Fits($core.installable_tags,$core.blocked_tags,'armorFeet,armor')) 'Boot stacking enabled'
    }
}
foreach($rank in 2..5) {
    $tier=$rank+14; $material="PZAECBuildPartsR$rank"
    Assert-Build ((Get-BuildProperty $material 'Tags') -eq 'PZAECBuildMaterial' -and (Get-BuildProperty $material 'Weight') -eq '0') 'Resource inherited harvest/scrap exploits'
    Assert-Build ((Get-BuildProperty $material 'SellableToTrader') -eq 'false') 'Components can be sold'
    $entries=$quests.SelectNodes("//reward[@id='$material']")
    Assert-Build ($entries.Count -eq 2) 'Components added outside two completion quests'
    foreach($entry in $entries) {
        $expected=if($entry.ParentNode.id -eq "PZAECChallengeT$tier"){3}elseif($entry.ParentNode.id -eq "PZAECDefenseT$tier"){5}else{0}
        Assert-Build ($expected -gt 0 -and [int]$entry.value -eq $expected -and $entry.type -eq 'Item' -and -not $entry.HasAttribute('ischosen') -and -not $entry.HasAttribute('stage')) 'Component tier/count/award stage wrong'
    }
}
# Resolve actual installed legendary weapon tags, not assumptions from their names.
$legendGunCount=0; $legendMeleeCount=0; $chestCount=0
[xml]$buildProjectLoot=Get-Content (Join-Path $modRoot '01-ProjectZ/Config/loot.xml') -Raw
$buildLegendPool=@($buildProjectLoot.SelectNodes('//lootgroup[@name="groupLegend_Weapon" or @name="groupLegend_MeleeWeapon"]/item[@name]') | ForEach-Object name)
foreach($id in $buildItemMap.Keys) {
    $tags=Get-BuildProperty $id 'Tags'
    if($tags -match '(^|,)LegendWeapon(,|$)' -or $buildLegendPool -contains $id) {
        if($tags -match '(^|,)gun(,|$)' -and $tags -notmatch 'launcher|perkDemolitionsExpert|noMods') {
            foreach($kind in @('Precision','Barrage')) {
                $core=$buildMods.SelectSingleNode("//item_modifier[@name='modPZAEC${kind}R5']")
                Assert-Build ([BuildRegression]::Fits($core.installable_tags,$core.blocked_tags,$tags)) "Legendary firearm incompatible $id"
            }
            $legendGunCount++
        }
        if($tags -match '(^|,)melee(,|$)' -and $tags -notmatch '(^|,)(noMods|tool|motorTool)(,|$)') {
            $core=$buildMods.SelectSingleNode("//item_modifier[@name='modPZAECBreakerR5']")
            Assert-Build ([BuildRegression]::Fits($core.installable_tags,$core.blocked_tags,$tags)) "Legendary melee incompatible $id"
            $legendMeleeCount++
        }
    }
    if($tags -match '(^|,)armorChest(,|$)' -and $tags -notmatch 'noMods'){$chestCount++}
}
Assert-Build ($legendGunCount -gt 10 -and $legendMeleeCount -gt 5 -and $chestCount -gt 5) 'Compatibility test missed installed endgame equipment'
"PASS: 16 recipes, 4 component tiers, 8 completion-only rewards; $legendGunCount legendary firearms, $legendMeleeCount legendary melee weapons and $chestCount chest items checked against merged modpack."
