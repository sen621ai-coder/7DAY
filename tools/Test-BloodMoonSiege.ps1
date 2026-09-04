#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Test-LegendaryNetworking.ps1')
Add-Type -ReferencedAssemblies ($references + $frameworkReferences) -TypeDefinition @'
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using HarmonyLib;
using AECT16RuntimeFix;
using UnityEngine;

public static class SiegeRegression
{
    static void Check(bool ok,string why) { if(!ok) throw new Exception(why); }
    public static string Rules()
    {
        foreach(var pair in new[]{new[]{11999,0},new[]{12000,16},new[]{13999,16},new[]{14000,17},
            new[]{15999,17},new[]{16000,18},new[]{17999,18},new[]{18000,19},new[]{999999,19}})
            Check(BloodMoonSiege.TierForGameStage(pair[0])==pair[1],"Wrong siege GS tier");
        int heavy=0,acid=0;
        for(int tier=15;tier<=20;tier++) for(int roll=0;roll<100;roll++) {
            string id=BloodMoonSiege.Variant(tier,roll);
            if(tier<16 || tier>19 || roll>=BloodMoonSiege.ReplacementChance) Check(id==null,"Out-of-scope selection");
            else { Check(BloodMoonSiege.Tier(id)==tier,"Wrong siege tier"); if(id.Contains("heavy")) heavy++; else acid++; }
        }
        Check(heavy==68 && acid==32,"25% ordinary replacement / approximate 2:1 composition changed");
        Check(BloodMoonSiege.HasRoom(7,3) && !BloodMoonSiege.HasRoom(8,3) && !BloodMoonSiege.HasRoom(3,4),"Squad cap broken");
        Check(BloodMoonSiege.InRange(Vector3.zero,new Vector3(8,0,0)) && BloodMoonSiege.InRange(Vector3.zero,new Vector3(0,0,52)),"Valid standoff range");
        Check(!BloodMoonSiege.InRange(Vector3.zero,new Vector3(7.99f,0,0)) && !BloodMoonSiege.InRange(Vector3.zero,new Vector3(52.01f,0,0)),"Standoff range ignored");
        Check(!BloodMoonSiege.InRange(Vector3.zero,new Vector3(float.NaN,0,0)),"NaN aim");
        Check(Type.GetType("EAIPZAECSiege,AEC.T16.RuntimeFix",true)==typeof(EAIPZAECSiege),"Native AI factory name cannot resolve");
        Check(Type.GetType("ItemActionPZAECSiegeVomit,AEC.T16.RuntimeFix",true)==typeof(ItemActionPZAECSiegeVomit),"Native item factory name cannot resolve");
        foreach(int cd in new[]{6,10}) {
            var ai=new EAIPZAECSiege();
            ai.SetData(DynamicProperties.ParseData("itemType=1;cooldown="+cd+";duration=7;minRange=8;maxRange=52"));
            Check(ai.itemActionType==1 && ai.baseCooldown==cd && ai.attackDuration==7 && ai.startAnimType==-1,"Native task parameter parsing failed");
        }
        var old=Block.list;
        try {
            Block.list=new Block[65536];
            var wall=(Block)RuntimeHelpers.GetUninitializedObject(typeof(Block)); wall.blockName="concreteBlock"; wall.MaxDamage=5000; Block.list[2000]=wall;
            Check(BloodMoonSiege.IsStructure(new BlockValue(2000)),"Concrete wall rejected");
            wall.blockName="landClaimBlock"; Check(!BloodMoonSiege.IsStructure(new BlockValue(2000)),"Claim selected");
            wall.blockName="bedroll"; Check(!BloodMoonSiege.IsStructure(new BlockValue(2000)),"Bedroll selected");
            wall.blockName="tree"; wall.bIsPlant=true; Check(!BloodMoonSiege.IsStructure(new BlockValue(2000)),"Plant selected");
            Check(!BloodMoonSiege.IsStructure(BlockValue.Air),"Air selected");
        } finally { Block.list=old; }
        return "PASS: siege GS 12000/14000/16000/18000, 600 replacement cases, raised caps, range/structure filters, assembly-qualified factories and native AI parameter parsing.";
    }
    public static string Projectiles(string xml)
    {
        int count=0;
        foreach(var node in XDocument.Parse(xml).Descendants("item").Where(n=>((string)n.Attribute("name")).StartsWith("ammoPZAECSiege_"))) {
            var actionNode=node.Elements("property").Single(n=>(string)n.Attribute("class")=="Action1");
            var properties=new DynamicProperties(); foreach(var property in actionNode.Elements("property")) properties.Parse(property,false);
            var item=(ItemClass)RuntimeHelpers.GetUninitializedObject(typeof(ItemClass));
            item.Properties=new DynamicProperties(); item.Effects=MinEffectController.ParseXml(node,null,MinEffectController.SourceParentType.ItemClass);
            var action=new ItemActionProjectile {item=item,ActionIndex=1}; action.ReadFrom(properties);
            bool heavy=((string)node.Attribute("name")).Contains("heavy");
            Check(action.FlyTime<0 && action.Gravity==-2 && action.LifeTime==5 && action.Velocity==40,"Ballistic parameters ignored");
            Check(action.damageBlock==(float)actionNode.Elements("property").Single(p=>(string)p.Attribute("name")=="DamageBlock").Attribute("value"),"Native projectile damage parse failed");
            Check(action.Explosion.BlockRadius==(heavy?1.2f:0f),"Blast radius ignored");
            Check(action.Explosion.BlockDamage==(heavy?new[]{350,500,700,900}[int.Parse(((string)node.Attribute("name")).Substring(((string)node.Attribute("name")).Length-2))-16]:0),"Blast damage ignored");
            count++;
        }
        Check(count==8,"Missing dedicated projectile definitions");
        return "PASS: eight projectile definitions parsed by the real game's ItemActionProjectile and ExplosionData.";
    }
    public static Action<NetPackageItemActionEffects,PooledBinaryWriter> Writer(DynamicMethod method, List<CodeInstruction> code)
    {
        Check(code[0].opcode==OpCodes.Ldarg_0 && code[1].opcode==OpCodes.Ldarg_1 &&
            code[2].operand is MethodInfo header && header.DeclaringType==typeof(NetPackage) && header.Name=="write","Native header layout changed");
        var il=method.GetILGenerator();
        // Omit only registry-dependent packet header; execute every original
        // payload instruction, including its conditional vector serialization.
        foreach(var op in code.Skip(3)) {
            foreach(var label in op.labels) il.MarkLabel(label);
            if(op.operand==null) il.Emit(op.opcode);
            else if(op.operand is MethodInfo m) il.Emit(op.opcode,m);
            else if(op.operand is FieldInfo f) il.Emit(op.opcode,f);
            else if(op.operand is Label l) il.Emit(op.opcode,l);
            else if(op.operand is LocalBuilder b) il.Emit(op.opcode,b);
            else throw new Exception("Unsupported native packet instruction: "+op);
        }
        return (Action<NetPackageItemActionEffects,PooledBinaryWriter>)method.CreateDelegate(typeof(Action<NetPackageItemActionEffects,PooledBinaryWriter>));
    }
    public static string WireAndIL(Action<NetPackageItemActionEffects,PooledBinaryWriter> write)
    {
        int count=0;
        foreach(var point in new[]{new Vector3(-1234,80,900),new Vector3(0,63,0),new Vector3(840,140,-400)}) {
            var original=new NetPackageItemActionEffects().Setup(42,0,1,(ItemActionFiringState)1,new Vector3(10,64,12),point,1);
            using(var stream=new MemoryStream()) {
                var w=new PooledBinaryWriter(); w.SetBaseStream(stream); write(original,w); w.Flush();
                stream.Position=0;
                var r=new PooledBinaryReader(); r.SetBaseStream(stream); var received=new NetPackageItemActionEffects(); received.read(r);
                Check(received.direction==point && received.startPos==original.startPos && received.userData==1 && received.entityId==42 && received.actionIdx==1,"Native packet lost fixed wall target");
                Check(stream.Position==stream.Length,"Action packet size mismatch");
            }
            count++;
        }
        using(var game=Mono.Cecil.ModuleDefinition.ReadModule(typeof(Quest).Assembly.Location))
        using(var mod=Mono.Cecil.ModuleDefinition.ReadModule(typeof(BloodMoonSiege).Assembly.Location)) {
            var collision=game.Types.Single(t=>t.Name=="ProjectileMoveScript").Methods.Single(m=>m.Name=="checkCollision");
            Check(collision.Body.Instructions.Any(i=>i.Operand is Mono.Cecil.FieldReference f && f.Name=="isEntityRemote"),"Remote projectile damage guard changed");
            foreach(string name in new[]{"Hit","ExplosionServer"}) Check(collision.Body.Instructions.Any(i=>i.Operand is Mono.Cecil.MethodReference m && m.Name==name),"Native impact path changed");
            var selector=mod.Types.Single(t=>t.Name=="BloodMoonSpawnFix").Methods.Single(m=>m.Name=="SelectForTarget");
            Check(selector.Body.Instructions.Count(i=>i.Operand is Mono.Cecil.MethodReference m && m.Name=="SelectReplacement")==2,"Dynamic/fallback blood moon path missing squad");
            Check(selector.Body.Instructions.Count(i=>i.Operand is Mono.Cecil.MethodReference m && m.DeclaringType.Name=="BloodMoonSiege" && m.Name=="TierForGameStage")==2,"Siege-specific GS ladder missing from dynamic/fallback paths");
            var ai=mod.Types.Single(t=>t.Name=="EAIPZAECSiege");
            Check(ai.Methods.Single(m=>m.Name=="Continue").Body.Instructions.Any(i=>i.Operand is Mono.Cecil.FieldReference f && f.Name=="recovering"),"Gunner walks into ditch on reload");
            var enabled=ai.Methods.Single(m=>m.Name=="Enabled");
            foreach(string name in new[]{"IsBloodMoon","isEventBloodMoon"}) Check(enabled.Body.Instructions.Any(i=>i.Operand is Mono.Cecil.FieldReference f && f.Name==name),"Siege active outside blood moon");
        }
        return "PASS: three native fire-payload round-trips (registry header excluded); server impact authority, Blood Moon scoping and both selector paths verified.";
    }
}
'@
[SiegeRegression]::Rules()
[SiegeRegression]::Projectiles((Get-Content (Join-Path $modRoot '99-AEC_T16_RuntimeFix/Config/items.xml') -Raw))
$siegeWriter=[Reflection.Emit.DynamicMethod]::new('SiegeNativePayload',[void],[Type[]]@([NetPackageItemActionEffects],[PooledBinaryWriter]),$true)
$siegeIL=[LegendaryNetworkingRegression]::ReadGameIL([HarmonyLib.AccessTools]::Method([NetPackageItemActionEffects],'write'),$siegeWriter.GetILGenerator())
[SiegeRegression]::WireAndIL([SiegeRegression]::Writer($siegeWriter,$siegeIL))

[xml]$siegeEntities=Get-Content (Join-Path $modRoot '99-AEC_T16_RuntimeFix/Config/entityclasses.xml') -Raw
[xml]$siegeItems=Get-Content (Join-Path $modRoot '99-AEC_T16_RuntimeFix/Config/items.xml') -Raw
[xml]$siegeParents=Get-Content (Join-Path $modRoot '98-AECxProjectZ_Tweaks/Config/entityclasses.xml') -Raw
$siegeLabels=Import-Csv (Join-Path $modRoot '99-AEC_T16_RuntimeFix/Config/Localization.csv')
$squad=@($siegeEntities.SelectNodes('//entity_class[starts-with(@name,"PZAECSiege_")]'))
if($squad.Count -ne 8){throw 'Expected two roles in four tiers'}
foreach($entity in $squad) {
    $tier=[AECT16RuntimeFix.BloodMoonSiege]::Tier($entity.name)
    if(-not $siegeParents.SelectSingleNode("//entity_class[@name='$($entity.extends)']") -or $entity.extends -notmatch "Tier$tier"){throw 'Invalid exact-tier parent'}
    $handName=$entity.SelectSingleNode('property[@name="HandItem"]').value
    $hand=$siegeItems.SelectSingleNode("//item[@name='$handName']")
    $action=$hand.SelectSingleNode('property[@class="Action1"]')
    if($action.SelectSingleNode('property[@name="Class"]').value -ne 'PZAECSiegeVomit,AEC.T16.RuntimeFix'){throw 'Missing native aim override'}
    $ammo=$action.SelectSingleNode('property[@name="Magazine_items"]').value
    if(-not $siegeItems.SelectSingleNode("//item[@name='$ammo']")){throw 'Missing siege ammo'}
    $expectedWarning=if($entity.name -match '_heavy_'){3}else{2}
    if([int]$action.SelectSingleNode('property[@name="WarningDelay"]').value -ne $expectedWarning -or $action.SelectSingleNode('property[@name="WarningMax"]').value -ne '1'){throw 'Warning not deterministic'}
    $label=@($siegeLabels | Where-Object Key -eq $entity.name)
    if($label.Count -ne 1 -or $label[0].schinese -notmatch "T$tier"){throw 'Missing/duplicate Chinese siege name'}
    if($entity.SelectNodes('effect_group').Count -ne 0){throw 'Unexpected global stats/loot changes'}
}
'PASS: eight exact-tier squad entities, dedicated hands/ammo, deterministic warnings and Chinese names verified. No live gameplay or multiplayer test performed.'
