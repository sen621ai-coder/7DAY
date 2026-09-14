#Requires -Version 7.0
# Run Test-FourInOne.py first to build independently merged fixtures.
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot
$managed = Join-Path (Split-Path -Parent $modRoot) '7DaysToDie_Data/Managed'
Get-ChildItem -LiteralPath $managed -Filter '*.dll' | ForEach-Object {
    try { [void][Reflection.Assembly]::LoadFrom($_.FullName) } catch { }
}
$references = @((Join-Path $managed 'Assembly-CSharp.dll'), (Join-Path $managed 'UnityEngine.CoreModule.dll'))
$framework = @(Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName)
Add-Type -ReferencedAssemblies ($references + $framework) -TypeDefinition @'
using System;
using System.Linq;
using System.Xml.Linq;
public static class FourInOneNativeCheck
{
    public static string Run(string path)
    {
        int count = 0, passives = 0, triggers = 0;
        foreach (var node in XDocument.Load(path).Root.Elements("item_modifier"))
        {
            var effects = MinEffectController.ParseXml(node, null, MinEffectController.SourceParentType.ItemModifierClass);
            int expected = node.Descendants("passive_effect").Count();
            if (effects.EffectGroups.Count != node.Elements("effect_group").Count() ||
                effects.EffectGroups.Sum(g => g.PassiveEffects.Count) != expected ||
                effects.EffectGroups.Sum(g => g.TriggeredEffects.Sum(e => e.Value.Count)) != node.Descendants("triggered_effect").Count())
                throw new Exception("Lost native effects: " + (string)node.Attribute("name"));
            passives += expected;
            triggers += node.Descendants("triggered_effect").Count();
            count++;
        }
        return "PASS: native effect parser accepted " + count + " modifiers, " + passives + " passive effects and " + triggers + " triggers: " + path;
    }
}
'@
[FourInOneNativeCheck]::Run((Join-Path $modRoot '.local-tests/four-in-one/ingredients.xml'))
[FourInOneNativeCheck]::Run((Join-Path $modRoot '.local-tests/four-in-one/item_modifiers.xml'))
