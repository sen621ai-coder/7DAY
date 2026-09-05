#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot
$managed = Join-Path (Split-Path -Parent $modRoot) '7DaysToDie_Data/Managed'
Get-ChildItem -LiteralPath $managed -Filter '*.dll' | ForEach-Object {
    try { [void][Reflection.Assembly]::LoadFrom($_.FullName) } catch { }
}
$references = @((Join-Path $managed 'Assembly-CSharp.dll'), (Join-Path $managed 'UnityEngine.CoreModule.dll'))
$frameworkReferences = @(Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName)
Add-Type -ReferencedAssemblies ($references + $frameworkReferences) -TypeDefinition @'
using System;
using System.Linq;
using System.Xml.Linq;
public static class SuccessionRegression
{
    static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    static float Value(XElement node,string effect,string tags,float quality)
    {
        // Strip only requirements for legacy baseline: granting every conditional
        // passive produces a conservative comparison envelope for these metrics.
        var copy=new XElement(node);
        foreach(var r in copy.Descendants("requirement").ToArray()) r.Remove();
        foreach(var r in copy.Descendants("triggered_effect").ToArray()) r.Remove();
        var controller=MinEffectController.ParseXml(copy,null,MinEffectController.SourceParentType.ItemModifierClass);
        float b=100,p=1;
        controller.ModifyValue(null,(PassiveEffects)Enum.Parse(typeof(PassiveEffects),effect),ref b,ref p,quality,FastTags<TagGroup.Global>.Parse(tags));
        return b*p;
    }
    public static string Run(string currentXml,string legacyXml)
    {
        var current=XDocument.Parse(currentXml).Descendants("item_modifier").ToDictionary(x=>(string)x.Attribute("name"));
        var old=XDocument.Parse(legacyXml).Descendants("item_modifier").ToDictionary(x=>(string)x.Attribute("name"));
        // Family, comparison metric, attack tags, old equivalent, lower is better.
        string[][] cases={
            new[]{"ThreatLens","EntityDamage","ranged","modHeavyTipUnique","0"},
            new[]{"CoolingSink","RoundsPerMinute","","modReactorAmplifierUnique","0"},
            new[]{"KineticRecycler","StaminaLoss","secondary","modMeleeErgonomicGripUnique","1"},
            new[]{"RepairServo","ReloadSpeedMultiplier","","modGunDrumMagazineExtenderUnique","0"},
            new[]{"PhaseRangefinder","SpreadMultiplierAiming","","modGunBipodUnique","1"},
            new[]{"NearfieldReflex","ReloadSpeedMultiplier","","modGunDrumMagazineExtenderUnique","0"},
            new[]{"ClosedLoopFeed","MagazineSize","","modHugeSupplyUnique","0"},
            new[]{"PulseCapacitor","EntityDamage","","modHeavyTipUnique","0"},
            new[]{"StanceBreaker","EntityDamage","secondary","modHeavyTipUnique","0"},
            new[]{"GatekeeperHook","EntityDamage","","modHeavyTipUnique","0"},
            new[]{"EmergencyLiner","PhysicalDamageResist","","modArmorPlatingReinforced","0"},
            new[]{"InsulatedTreads","StaminaChangeOT","","modTerrainResistUnique","0"},
        };
        int count=0;
        foreach(var c in cases) {
            float previous=Value(old[c[3]],c[1],c[2],6);
            for(int tier=16;tier<=19;tier++) {
                var node=current["modPZAEC"+c[0]+"T"+tier];
                float first=Value(node,c[1],c[2],1);
                Check(c[4]=="1" ? first<previous : first>previous,c[0]+" T"+tier+" fails legacy/prior tier: "+first+" vs "+previous);
                for(int q=1;q<=6;q++) {
                    Check(Math.Abs(Value(node,c[1],c[2],q)-first)<.001f,c[0]+" scales down with item quality");
                    count++;
                }
                previous=first;
                for(int other=16;other<=19;other++) {
                    var otherNode=current["modPZAEC"+c[0]+"T"+other];
                    Check(FastTags<TagGroup.Global>.Parse((string)node.Attribute("modifier_tags")).Test_AnySet(FastTags<TagGroup.Global>.Parse((string)otherNode.Attribute("modifier_tags"))),"Same family tiers can stack");
                }
            }
        }
        return "PASS: "+count+" native component metric/quality checks; 12 families exceed comparison baseline, progress every tier, and reject same-family stacking. Conditional active procs excluded.";
    }
}
'@
$source = Join-Path $modRoot '99-AEC_T16_RuntimeFix/Config/item_modifiers.xml'
$dump = Join-Path $modRoot '.local-tests/UserData/Saves/Navezgane/AEC_Equipment_Verification_20260905/ConfigsDump/item_modifiers.xml'
[SuccessionRegression]::Run((Get-Content $source -Raw), (Get-Content $dump -Raw))
