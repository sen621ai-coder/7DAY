#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$skyRoot = Split-Path -Parent $PSScriptRoot
$skyManaged = Join-Path (Split-Path -Parent $skyRoot) '7DaysToDie_Data/Managed'
Get-ChildItem -LiteralPath $skyManaged -Filter '*.dll' | ForEach-Object {
    try { [void][Reflection.Assembly]::LoadFrom($_.FullName) } catch { }
}
$skyReferences = @((Join-Path $skyManaged 'Assembly-CSharp.dll'), (Join-Path $skyManaged 'UnityEngine.CoreModule.dll'))
$skyFramework = @(Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName)
Add-Type -ReferencedAssemblies ($skyReferences + $skyFramework) -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

public static class SkyguardAmmoAudit
{
    static int checks;
    static void Check(bool ok, string label) { checks++; if (!ok) throw new Exception(label); }
    static ItemClass Register(XElement node, int id, string tags)
    {
        var item = new ItemClass();
        item.SetId(id);
        item.SetName((string)node.Attribute("name"));
        item.ItemTags = FastTags<TagGroup.Global>.Parse(tags);
        item.Effects = MinEffectController.ParseXml(node, null, MinEffectController.SourceParentType.ItemClass);
        ItemClass.list[id] = item;
        foreach (string field in new[] {"nameToItem", "nameToItemCaseInsensitive"})
            ((Dictionary<string,ItemClass>)typeof(ItemClass).GetField(field, BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).GetValue(null))[item.GetItemName()] = item;
        return item;
    }
    public static string Run(string blocksPath, string itemsPath, string nativeItemsPath)
    {
        checks = 0;
        var blocks = XDocument.Load(blocksPath);
        var items = XDocument.Load(itemsPath);
        var native = XDocument.Load(nativeItemsPath);
        ItemClass.list = new ItemClass[65536];
        var ordinaryNode = native.Root.Elements("item").Single(x => (string)x.Attribute("name") == "ammo762mmBulletBall");
        var ordinary = Register(ordinaryNode, 30000, "ammo,ammo762mm");
        var wrong = Register(new XElement("item", new XAttribute("name", "skyguardAuditWrongAmmo")), 30001, "ammo,ammo9mm");
        var special = new List<ItemClass>();
        for (int tier=16; tier<=19; tier++)
        {
            string name = "ammoPZAECSkyguardInterceptorT" + tier;
            var node = items.Descendants("item").Single(x => (string)x.Attribute("name") == name);
            string tags = (string)node.Elements("property").Single(x => (string)x.Attribute("name") == "Tags").Attribute("value");
            special.Add(Register(node, 30100+tier, tags));
        }
        for (int tier=16; tier<=19; tier++)
        {
            var block = blocks.Descendants("block").Single(x => (string)x.Attribute("name") == "PZAECSkyguardArrayT"+tier);
            var props = block.Elements("property").Where(x => x.Attribute("name") != null).ToDictionary(x => (string)x.Attribute("name"), x => (string)x.Attribute("value"));
            var allowed = new List<ItemClass>();
            XUiC_RequiredItemStack.ParseItemClassesFromString(allowed, props["AmmoItem"]);
            Check(allowed.Contains(ordinary), "ordinary 7.62 not accepted T"+tier);
            Check(!allowed.Contains(wrong), "wrong caliber accepted T"+tier);
            foreach (var round in special) Check(allowed.Contains(round) == (round == special[tier-16]), "cross-tier special acceptance T"+tier);
            int baseDamage = int.Parse(props["EntityDamage"]);
            Check(baseDamage == new[] {450,650,900,1250}[tier-16], "wrong turret scaling T"+tier);
            var controller = (AutoTurretFireController)RuntimeHelpers.GetUninitializedObject(typeof(AutoTurretFireController));
            typeof(AutoTurretFireController).GetField("entityDamage", BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).SetValue(controller, baseDamage);
            var damage = typeof(AutoTurretFireController).GetMethod("GetDamageEntity", BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            var ordinaryValue = new ItemValue(30000, false);
            var specialValue = new ItemValue(30100+tier, false);
            float standardHit = (float)damage.Invoke(controller, new object[] { ordinaryValue });
            float specialHit = (float)damage.Invoke(controller, new object[] { specialValue });
            Check(Math.Abs(standardHit-baseDamage)<.01f, "ordinary damage mismatch " + standardHit);
            Check(Math.Abs(specialHit-baseDamage*2)<.01f, "special damage mismatch " + specialHit);
            var ammoNode = items.Descendants("item").Single(x => (string)x.Attribute("name") == special[tier-16].GetItemName());
            float displayed = float.Parse((string)ammoNode.Descendants("display_value").Single(x => (string)x.Attribute("name") == "dSkyguardDamage").Attribute("value"));
            Check(Math.Abs(displayed-specialHit)<.01f, "displayed turret damage differs from native result");
            float blockBase=1, blockPercent=1;
            special[tier-16].Effects.ModifyValue(null, PassiveEffects.BlockDamage, ref blockBase, ref blockPercent, 1, FastTags<TagGroup.Global>.none, 1);
            Check(Math.Abs(blockBase*blockPercent)<.01f, "special ammunition damages blocks");
            Console.WriteLine("T"+tier+": native turret standard="+standardHit+", interceptor="+specialHit);
        }
        var rifleAmmo = new List<ItemClass>();
        XUiC_RequiredItemStack.ParseItemClassesFromString(rifleAmmo, "tags(ammo762mm)");
        Check(special.All(x => !rifleAmmo.Contains(x)), "turret-only ammunition leaks into rifles");
        return "PASS: Skyguard native ammo selection and damage, checks="+checks;
    }
}
'@
[SkyguardAmmoAudit]::Run(
    (Join-Path $skyRoot '99-AEC_T16_RuntimeFix/Config/blocks.xml'),
    (Join-Path $skyRoot '99-AEC_T16_RuntimeFix/Config/items.xml'),
    (Join-Path $skyRoot '.local-tests/UserData/Saves/Navezgane/AEC_Equipment_Verification_20260905/ConfigsDump/items.xml'))
