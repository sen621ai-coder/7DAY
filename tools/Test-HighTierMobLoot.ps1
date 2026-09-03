#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
# Reuse the metadata IL reader; this does not launch Unity or install live patches.
. (Join-Path $PSScriptRoot 'Test-ModelTintFix.ps1')
Add-Type -ReferencedAssemblies ($references + $frameworkReferences + @([ModelTintRegression].Assembly.Location | Where-Object { $_ })) -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using HarmonyLib;
using AECT16RuntimeFix;

public static class MobLootRegression
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static HashSet<ItemClass> qualityItems = new HashSet<ItemClass>();
    static int capturedType, capturedMin, capturedMax;
    static bool capturedDefault;
    static string[] capturedMods;
    static float capturedChance;
    public static bool HasQualityStub(ItemClass item) { return qualityItems.Contains(item); }
    public static ItemValue ConstructorStub(int type, int min, int max, bool defaults, string[] mods, float chance)
    {
        capturedType=type; capturedMin=min; capturedMax=max; capturedDefault=defaults; capturedMods=mods; capturedChance=chance;
        return null;
    }
    public delegate ItemValue Factory(int type, int min, int max, bool defaults, string[] mods, float chance);
    public static string CheckFactory(List<CodeInstruction> code, DynamicMethod method)
    {
        var il = method.GetILGenerator();
        var signature = new[] { typeof(int), typeof(int), typeof(int), typeof(bool), typeof(string[]), typeof(float) };
        var constructor = AccessTools.Constructor(typeof(ItemValue), signature);
        var hasQuality = AccessTools.PropertyGetter(typeof(ItemClass), "HasQuality");
        int replacements=0;
        foreach (var c in code)
        {
            foreach (var label in c.labels) il.MarkLabel(label);
            if (c.opcode == OpCodes.Newobj && Equals(c.operand,constructor))
            { il.Emit(OpCodes.Call, typeof(MobLootRegression).GetMethod("ConstructorStub")); replacements++; }
            else if (Equals(c.operand,hasQuality)) il.Emit(OpCodes.Call,typeof(MobLootRegression).GetMethod("HasQualityStub"));
            else if (c.operand == null) il.Emit(c.opcode);
            else if (c.operand is LocalBuilder local) il.Emit(c.opcode,local);
            else if (c.operand is Label label) il.Emit(c.opcode,label);
            else if (c.operand is Label[] labels) il.Emit(c.opcode,labels);
            else if (c.operand is MethodInfo mi) il.Emit(c.opcode,mi);
            else if (c.operand is ConstructorInfo ci) il.Emit(c.opcode,ci);
            else if (c.operand is FieldInfo fi) il.Emit(c.opcode,fi);
            else if (c.operand is Type type) il.Emit(c.opcode,type);
            else if (c.operand is string text) il.Emit(c.opcode,text);
            else if (c.operand is int n) il.Emit(c.opcode,n);
            else if (c.operand is sbyte sb) il.Emit(c.opcode,sb);
            else if (c.operand is byte b) il.Emit(c.opcode,b);
            else throw new Exception("Unsupported factory IL operand: " + c.operand.GetType());
        }
        Check(replacements==1,"Factory no longer constructs one native item");
        var run=(Factory)method.CreateDelegate(typeof(Factory));
        var saved=ItemClass.list;
        var ordinary=(ItemClass)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ItemClass));
        var advanced=(ItemClass)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ItemClass));
        var material=(ItemClass)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ItemClass));
        advanced.Properties=new DynamicProperties();
        advanced.Properties.Values["PZAECAdvancedLoot"]="true";
        qualityItems.Add(ordinary); qualityItems.Add(advanced);
        string[] mods={"test-mod"};
        try
        {
            ItemClass.list=new[] { (ItemClass)null, ordinary, advanced, material };
            foreach(int tier in new[] { 0,16,17,18,19 })
            {
                int state=HighTierMobLoot.EnterScope(tier==0?"zPackReg":"PZAECMobT"+tier+"_zPackReg");
                try
                {
                    foreach(int type in new[] { -1,0,1,2,3,999 })
                    {
                        run(type,1,4,true,mods,.75f);
                        bool scoped=tier>=16 && (type==1 || type==2);
                        Check(capturedMin==(scoped?type==2?tier-14:5:1),"Actual factory minimum incorrect");
                        Check(capturedMax==(scoped?type==2?tier-14:6:4),"Actual factory maximum incorrect");
                        Check(capturedType==type && capturedDefault && ReferenceEquals(capturedMods,mods) && capturedChance==.75f,"Native item/mod constructor arguments changed");
                    }
                }
                finally { HighTierMobLoot.ExitScope(state); }
            }
        }
        finally { ItemClass.list=saved; qualityItems.Clear(); }
        return "PASS: actual factory IL executed with Unity constructor stub: real item-marker lookup, scope, quality arguments, materials/invalid IDs and native mod arguments verified.";
    }
    public static string Run()
    {
        int cases = 0;
        foreach (int tier in new[] { -1, 0, 15, 16, 17, 18, 19, 20 })
        foreach (bool quality in new[] { false, true })
        foreach (bool advanced in new[] { false, true })
        for (int oldMin = 0; oldMin <= 6; oldMin++)
        for (int oldMax = oldMin; oldMax <= 6; oldMax++)
        {
            int min = oldMin, max = oldMax;
            HighTierMobLoot.QualityBounds(tier, quality, advanced, ref min, ref max);
            bool scoped = quality && tier >= 16 && tier <= 19;
            Check(min == (scoped ? advanced ? tier - 14 : 5 : oldMin), "Wrong minimum quality");
            Check(max == (scoped ? advanced ? tier - 14 : 6 : oldMax), "Wrong maximum quality");
            cases++;
        }
        string[] kinds = { "Reg", "Strong", "Boss", "Lab", "Nurse", "Soldier", "Thug", "Utility", "Plague" };
        foreach (int tier in new[] { 16, 17, 18, 19 })
        foreach (string kind in kinds)
            Check(HighTierMobLoot.TierForContainer("PZAECMobT" + tier + "_zPack" + kind) == tier, "Unrecognized bag");
        foreach (string name in new[] { null, "", "zPackReg", "AECDumdumBossLootT16", "PZAECBossLootBundleT16", "PZAECMobT15_zPackReg", "PZAECMobT20_zPackReg", "PZAECMobT19_zPackRegExtra", "PZAECMobT19_unknown" })
            Check(HighTierMobLoot.TierForContainer(name) == 0, "Unrelated loot entered scope");
        var scope = typeof(HighTierMobLoot).GetField("activeTier", BindingFlags.NonPublic | BindingFlags.Static);
        int previous = HighTierMobLoot.EnterScope("PZAECMobT19_zPackReg");
        Check(previous == 0 && (int)scope.GetValue(null) == 19, "Scope did not enter");
        int nested = HighTierMobLoot.EnterScope("zPackReg");
        Check((int)scope.GetValue(null) == 0, "Nested ordinary container inherited tier");
        HighTierMobLoot.ExitScope(nested);
        Check((int)scope.GetValue(null) == 19, "Nested scope did not restore");
        int questState;
        HighTierMobLoot.RewardPrefix(out questState);
        Check((int)scope.GetValue(null) == 0, "Quest reward inherited bag scope");
        HighTierMobLoot.SpawnFinalizer(questState);
        int threadTier = -1;
        var thread = new Thread(() => { threadTier = (int)scope.GetValue(null); });
        thread.Start(); thread.Join();
        Check(threadTier == 0, "Scope leaked to another thread");
        try { throw new InvalidOperationException("simulated loot failure"); }
        catch (InvalidOperationException) { }
        finally { HighTierMobLoot.SpawnFinalizer(previous); }
        Check((int)scope.GetValue(null) == 0, "Finalizer did not restore after failure");
        return "PASS: " + cases + " quality cases; 36 bag identities; unrelated loot, nested quest rewards, exception restoration and thread isolation.";
    }
    public static string CheckIL(List<CodeInstruction> original)
    {
        var patched = HighTierMobLoot.ItemFactoryTranspiler(original.Select(c => new CodeInstruction(c))).ToList();
        var signature = new[] { typeof(int), typeof(int), typeof(int), typeof(bool), typeof(string[]), typeof(float) };
        var constructor = AccessTools.Constructor(typeof(ItemValue), signature);
        var factory = AccessTools.Method(typeof(HighTierMobLoot), "CreateScopedItem", signature);
        int changes = 0;
        Check(original.Count == patched.Count, "Instruction count changed");
        for (int i = 0; i < original.Count; i++)
        {
            var a = original[i]; var b = patched[i];
            Check(a.labels.SequenceEqual(b.labels) && a.blocks.SequenceEqual(b.blocks), "Labels/regions changed");
            if (a.opcode == OpCodes.Newobj && Equals(a.operand, constructor))
            { Check(b.opcode == OpCodes.Call && Equals(b.operand, factory), "Wrong factory replacement"); changes++; }
            else Check(a.opcode == b.opcode && Equals(a.operand,b.operand), "Native selection/count/stats logic changed");
        }
        Check(changes == 2, "Wrong number of patched native constructors");
        foreach (int count in new[] { 0, 1, 3 })
        {
            bool rejected = false;
            try { HighTierMobLoot.ItemFactoryTranspiler(Enumerable.Range(0,count).Select(_ => new CodeInstruction(OpCodes.Newobj,constructor))).ToList(); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Unsupported game IL accepted");
        }
        return "PASS: real SpawnItem IL: only two constructors redirected; all native item selection, quantities, GS stats, durability, labels and failure guards preserved.";
    }
}
'@
[MobLootRegression]::Run()
$lootMethod = [HarmonyLib.AccessTools]::Method([LootContainer], 'SpawnItem')
$lootIL = [Reflection.Emit.DynamicMethod]::new('MobLootIL', [void], [Type[]]@()).GetILGenerator()
[MobLootRegression]::CheckIL([ModelTintRegression]::ReadGameIL($lootMethod, $lootIL))
$factoryMethod = [HarmonyLib.AccessTools]::Method([AECT16RuntimeFix.HighTierMobLoot], 'CreateScopedItem')
$factoryTest = [Reflection.Emit.DynamicMethod]::new('MobLootFactory', [ItemValue], [Type[]]@([int],[int],[int],[bool],[string[]],[single]))
[MobLootRegression]::CheckFactory([ModelTintRegression]::ReadGameIL($factoryMethod, $factoryTest.GetILGenerator()), $factoryTest)

function Assert-MobLoot([bool]$condition, [string]$message) { if (-not $condition) { throw $message } }
function Apply-EntityPatch([xml]$targetDoc, [xml]$patchDoc) {
    foreach ($op in $patchDoc.DocumentElement.ChildNodes) {
        if ($op.NodeType -ne 'Element') { continue }
        foreach ($target in @($targetDoc.SelectNodes($op.xpath))) {
            switch ($op.Name) {
                'set' { $target.InnerText = $op.InnerText }
                'remove' { [void]$target.ParentNode.RemoveChild($target) }
                'append' { foreach ($child in $op.ChildNodes) { if ($child.NodeType -eq 'Element') { [void]$target.AppendChild($targetDoc.ImportNode($child, $true)) } } }
                'insertBefore' { foreach ($child in $op.ChildNodes) { if ($child.NodeType -eq 'Element') { [void]$target.ParentNode.InsertBefore($targetDoc.ImportNode($child, $true), $target) } } }
                'insertAfter' { $anchor=$target; foreach ($child in $op.ChildNodes) { if ($child.NodeType -eq 'Element') { $anchor=$target.ParentNode.InsertAfter($targetDoc.ImportNode($child, $true), $anchor) } } }
                default { throw "Unsupported patch operation: $($op.Name)" }
            }
        }
    }
}
function Get-EntityProperty([hashtable]$map, [string]$name, [string]$property) {
    $seen = @{}
    while ($map.ContainsKey($name) -and -not $seen.ContainsKey($name)) {
        $seen[$name] = $true; $node = $map[$name]
        $properties = @($node.SelectNodes("property[@name='$property']"))
        if ($properties.Count) { return [string]$properties[-1].value }
        $name = [string]$node.extends
    }
    return ''
}
[xml]$mergedEntities = Get-Content -LiteralPath (Join-Path $modRoot '../Data/Config/entityclasses.xml') -Raw
foreach ($directory in Get-ChildItem -LiteralPath $modRoot -Directory | Sort-Object Name) {
    $entityPath = Join-Path $directory.FullName 'Config/entityclasses.xml'
    if (Test-Path -LiteralPath $entityPath) { Apply-EntityPatch $mergedEntities ([xml](Get-Content -LiteralPath $entityPath -Raw)) }
}
$entityMap = @{}
foreach ($node in $mergedEntities.SelectNodes('/entity_classes/entity_class')) { $entityMap[$node.name] = $node }
[xml]$lootConfig = Get-Content -LiteralPath (Join-Path $modRoot '98-AECxProjectZ_Tweaks/Config/loot.xml') -Raw
[xml]$baseLoot = Get-Content -LiteralPath (Join-Path $modRoot '../Data/Config/loot.xml') -Raw
$suffixes = @{16='Tier16Transcendent';17='Tier17Ascendant';18='Tier18Eternal';19='Tier19Apocalyptic'}
$mappedCount = 0
foreach ($tier in 16..19) {
    $ordinary = @($entityMap.Keys | Where-Object { $_.EndsWith($suffixes[$tier]) })
    Assert-MobLoot ($ordinary.Count -eq 35) "Missing ordinary T$tier family"
    foreach ($name in $ordinary) {
        $source = $name.Replace($suffixes[$tier], 'Tier15Mythic')
        $before = Get-EntityProperty $entityMap $source 'LootDropEntityClass'
        $after = Get-EntityProperty $entityMap $name 'LootDropEntityClass'
        Assert-MobLoot ($after -eq ($before -replace 'EntityLootContainer', "PZAECMobT${tier}_EntityLootContainer")) "Wrong bag family/weights: $name"
        Assert-MobLoot ((Get-EntityProperty $entityMap $name 'LootDropProb') -eq (Get-EntityProperty $entityMap $source 'LootDropProb')) "Drop rate changed: $name"
        $mappedCount++
    }
}
$scopedEntities = @($entityMap.Values | Where-Object { $_.SelectNodes("property[@name='LootDropEntityClass' and starts-with(@value,'PZAECMobT')]").Count -gt 0 })
Assert-MobLoot ($scopedEntities.Count -eq 140) 'Other enemies acquired scoped drops'
$bags = @($entityMap.Values | Where-Object { $_.name.StartsWith('PZAECMobT') })
Assert-MobLoot ($bags.Count -eq 36) 'Wrong scoped bag count'
foreach ($bag in $bags) {
    $tier = [int]$bag.name.Substring(9,2)
    $lootName = Get-EntityProperty $entityMap $bag.name 'LootList'
    Assert-MobLoot ([AECT16RuntimeFix.HighTierMobLoot]::TierForContainer($lootName) -eq $tier) 'Unrecognized bag LootList'
    $nodes = @($lootConfig.SelectNodes("//lootcontainer[@name='$lootName']"))
    Assert-MobLoot ($nodes.Count -eq 1) "Missing/duplicate loot list: $lootName"
    $oldName = Get-EntityProperty $entityMap $bag.extends 'LootList'
    $copy = $nodes[0].CloneNode($true); $copy.SetAttribute('name',$oldName)
    Assert-MobLoot ($copy.OuterXml -eq $baseLoot.SelectSingleNode("/lootcontainers/lootcontainer[@name='$oldName']").OuterXml) "Contents/probabilities changed: $lootName"
}
# Apply classification selectors to real item definitions; no runtime item stats are modified.
[xml]$itemConfig = Get-Content -LiteralPath (Join-Path $modRoot '98-AECxProjectZ_Tweaks/Config/items.xml') -Raw
[xml]$projectItems = Get-Content -LiteralPath (Join-Path $modRoot '01-ProjectZ/Config/items.xml') -Raw
[xml]$itemDefinitions = '<items />'
foreach ($node in $projectItems.SelectNodes('//item[@name]')) { [void]$itemDefinitions.DocumentElement.AppendChild($itemDefinitions.ImportNode($node,$true)) }
$markers = @($itemConfig.SelectNodes("/configs/append[property[@name='PZAECAdvancedLoot']]"))
Assert-MobLoot ($markers.Count -eq 1) 'Missing advanced item classifier'
$marked = @($itemDefinitions.SelectNodes($markers[0].xpath) | ForEach-Object { $_.name })
foreach ($id in @('gunRifleT3SniperRifleRareCrusher','GausLegTesla','gunRifleT5SniperRifleGaus','gunRifleT4SniperRifleImp','CombistickLegSurgeon')) {
    Assert-MobLoot ($marked -contains $id) "Special item not classified: $id"
}
foreach ($id in @('gunRifleT3SniperRifle','meleeWpnClubT3SteelClub','resourceLegendaryParts','resourceAECMutationSampleT5')) {
    Assert-MobLoot ($marked -notcontains $id) "Ordinary item misclassified: $id"
}
[xml]$projectLoot = Get-Content -LiteralPath (Join-Path $modRoot '01-ProjectZ/Config/loot.xml') -Raw
foreach ($entry in $projectLoot.SelectNodes("//lootgroup[@name='groupImp_Weapon' or @name='groupUnique_Weapon' or @name='groupLegend_Weapon' or @name='groupLegend_MeleeWeapon']/item[@name]")) {
    Assert-MobLoot ($marked -contains $entry.name) "Special weapon omitted: $($entry.name)"
}
$localized = @(Import-Csv -LiteralPath (Join-Path $modRoot '98-AECxProjectZ_Tweaks/Config/Localization.csv') | Where-Object { $_.Key.StartsWith('PZAECMobT') })
Assert-MobLoot ($localized.Count -eq 36 -and @($localized | Group-Object Key | Where-Object Count -gt 1).Count -eq 0) 'Missing/duplicate bag names'
foreach ($row in $localized) { Assert-MobLoot (-not [string]::IsNullOrWhiteSpace($row.schinese)) 'Missing Chinese bag name' }
Write-Output "PASS: $mappedCount ordinary zombie mappings; 36 inherited bags; drop rates, mixed weights, original loot contents and special-item classification verified."
