#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Test-ModelTintFix.ps1')
Add-Type -ReferencedAssemblies ($references + $frameworkReferences + @([ModelTintRegression].Assembly.Location)) -TypeDefinition @'
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using AECT16RuntimeFix;
using HarmonyLib;

public static class TacticalScrapRegression
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    public static string CheckRolls()
    {
        var saved = ItemClass.list;
        try
        {
            string[] names = { "armorPZAECHarrierHelmetT16", "gunPZAECBastionShotgunT19", "armorAthleticHelmet" };
            ItemClass.list = new ItemClass[4];
            for (int i=0; i<names.Length; i++)
            {
                var item = (ItemClass)RuntimeHelpers.GetUninitializedObject(typeof(ItemClass));
                item.pName=names[i]; item.pId=i+1;
                ItemClass.list[i+1]=item;
            }
            var value = new ItemValue(); value.type=1;
            var source = new ItemStack(value, 4);
            var recipe = new Recipe(); recipe.IsScrap=true; recipe.count=5;
            recipe.ingredients.Add(source);
            int calls=0; string component;
            int result=TacticalScrapRewards.Roll(recipe, () => (++calls % 2)==1, out component);
            Check(result==2 && calls==4 && component=="PZAECBuildPartsR2", "Armor per-item independent rolls");
            value.type=2; value.Quality=6; value.UseTimes=9999;
            calls=0;
            result=TacticalScrapRewards.Roll(recipe, () => (++calls % 2)==1, out component);
            Check(result==4 && calls==4 && component=="PZAECBuildPartsR5", "Weapon payout, tier or quality/durability dependence");
            Check(recipe.count==5 && source.count==4 && ReferenceEquals(source.itemValue,value), "Native outputs or consumed items changed");
            Check(TacticalScrapRewards.Roll(recipe, () => false, out component)==0, "Failed rolls awarded components");
            Check(TacticalScrapRewards.Roll(recipe, () => true, out component)==8, "Successful batch payout incorrect");
            recipe.IsScrap=false;
            calls=0;
            Check(TacticalScrapRewards.Roll(recipe, () => { calls++; return true; }, out component)==0 && calls==0, "Crafting/upgrade rolled a bonus");
            recipe.IsScrap=true; value.type=3;
            Check(TacticalScrapRewards.Roll(recipe, () => { calls++; return true; }, out component)==0 && calls==0, "Vanilla gear rolled a bonus");
            value.type=2;
            // Exercise the installed game's serialization, not a parallel model.
            Recipe restored;
            using(var stream=new MemoryStream())
            {
                using(var writer=new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) recipe.Write(writer);
                stream.Position=0;
                using(var reader=new BinaryReader(stream, System.Text.Encoding.UTF8, true)) restored=Recipe.Read(reader);
            }
            Check(restored.IsScrap && restored.ingredients[0].count==4, "Save/load lost scrap source/count");
            Check(TacticalScrapRewards.Roll(restored, () => true, out component)==8 && component=="PZAECBuildPartsR5", "Restored queue reward changed");
            return "PASS: independent batch rolls, 1/2 payouts, exact rank, failed rolls, unchanged originals, non-scrap exclusion and native recipe save/load.";
        }
        finally { ItemClass.list=saved; }
    }
    public static string CheckIL(MethodInfo method, List<CodeInstruction> original, bool queued)
    {
        var patched=(queued ? TacticalScrapRewards.QueueTranspiler(original) : TacticalScrapRewards.UITranspiler(original)).ToList();
        var helper=AccessTools.Method(typeof(TacticalScrapRewards),queued?"CompleteQueued":"CompleteUI");
        var counter=AccessTools.Field(queued?typeof(RecipeQueueItem):typeof(XUiC_RecipeStack),queued?"Multiplier":"recipeCount");
        int hooks=0;
        var stripped=new List<CodeInstruction>();
        for(int i=0;i<patched.Count;i++)
        {
            var code=patched[i]; stripped.Add(code);
            if(code.opcode!=OpCodes.Stfld || !Equals(code.operand,counter)) continue;
            Check(patched[++i].opcode==OpCodes.Ldarg_0,"Missing queue owner");
            if(queued) Check(patched[++i].opcode==OpCodes.Ldloc_0,"Missing current queue unit");
            Check(patched[++i].opcode==OpCodes.Call && Equals(patched[i].operand,helper),"Wrong completion callback");
            CheckCommittedSlice(patched, i, queued);
            hooks++;
        }
        Check(hooks==(queued?1:4),"Missing normal/full-output completion path");
        Check(stripped.Count==original.Count && stripped.Zip(original,(a,b)=>ReferenceEquals(a,b)).All(x=>x),"Native instructions, labels or retry branches changed");
        bool rejected=false;
        try { (queued?TacticalScrapRewards.QueueTranspiler(new CodeInstruction[0]):TacticalScrapRewards.UITranspiler(new CodeInstruction[0])).ToList(); }
        catch(InvalidOperationException) { rejected=true; }
        Check(rejected,"Unsupported game version silently accepted");
        return "PASS: "+method.DeclaringType.Name+" "+hooks+" completion paths; emitted completion slices executed; original control flow retained; unsupported IL rejected.";
    }
    static void CheckCommittedSlice(List<CodeInstruction> patched, int callIndex, bool queued)
    {
        // Execute each actual patched completion slice, replacing only the reward
        // delivery call so the offline host never calls Unity inventory APIs.
        var type=queued?typeof(RecipeQueueItem):typeof(XUiC_RecipeStack);
        var counter=AccessTools.Field(type,queued?"Multiplier":"recipeCount");
        var args=queued?new[]{typeof(TileEntityWorkstation),type,typeof(bool)}:new[]{type,typeof(bool)};
        var run=new DynamicMethod("CommitBranch",typeof(void),args,typeof(TacticalScrapRegression),true);
        var il=run.GetILGenerator(); var done=il.DefineLabel();
        if(queued) { il.DeclareLocal(type); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stloc_0); }
        il.Emit(queued?OpCodes.Ldarg_2:OpCodes.Ldarg_1); il.Emit(OpCodes.Brfalse,done);
        for(int j=callIndex-(queued?9:7);j<callIndex;j++)
        {
            var c=patched[j];
            if(c.operand is FieldInfo field) il.Emit(c.opcode,field);
            else if(c.operand==null) il.Emit(c.opcode);
            else throw new Exception("Unexpected completion slice operand");
        }
        il.Emit(OpCodes.Call,typeof(TacticalScrapRegression).GetMethod(queued?"RecordQueued":"RecordCommitted"));
        il.MarkLabel(done); il.Emit(OpCodes.Ret);
        var instance=RuntimeHelpers.GetUninitializedObject(type);
        counter.SetValue(instance,queued?(object)(short)1:1);
        commits=0;
        run.Invoke(null,queued?new[]{null,instance,(object)false}:new[]{instance,(object)false});
        Check(commits==0 && Convert.ToInt32(counter.GetValue(instance))==1,"Blocked output awarded a bonus");
        run.Invoke(null,queued?new[]{null,instance,(object)true}:new[]{instance,(object)true});
        Check(commits==1 && Convert.ToInt32(counter.GetValue(instance))==0,"Bonus not tied to committed decrement");
    }
    static int commits;
    public static void RecordQueued(TileEntityWorkstation station, RecipeQueueItem queue) { RecordCommitted(queue); }
    public static void RecordCommitted(object queue)
    {
        var field=AccessTools.Field(queue.GetType(),queue is RecipeQueueItem?"Multiplier":"recipeCount");
        Check(Convert.ToInt32(field.GetValue(queue))==0,"Bonus before completion"); commits++;
    }
}
'@
[TacticalScrapRegression]::CheckRolls()
foreach($type in @([XUiC_RecipeStack],[TileEntityWorkstation])){
    $queued=$type -eq [TileEntityWorkstation]
    $method=[HarmonyLib.AccessTools]::Method($type,$(if($queued){'HandleRecipeQueue'}else{'Update'}))
    $dm=[Reflection.Emit.DynamicMethod]::new('ScrapIL',[void],[Type[]]@())
    $il=[ModelTintRegression]::ReadGameIL($method,$dm.GetILGenerator())
    [TacticalScrapRegression]::CheckIL($method,$il,$queued)
    if($queued){
        $combined=[Collections.Generic.List[HarmonyLib.CodeInstruction]]::new()
        foreach($code in [AECT16RuntimeFix.FusionTierUpgrade]::QueueTranspiler($il)){ $combined.Add($code) }
        [TacticalScrapRegression]::CheckIL($method,$combined,$queued)
        'PASS: workstation scrap hooks compose with existing fusion-upgrade output hooks.'
    }
}
[xml]$items=Get-Content (Join-Path $modRoot '99-AEC_T16_RuntimeFix/Config/items.xml') -Raw
$gear=@($items.SelectNodes('//item[@name]') | Where-Object { $_.name -match '^(armor|gun|melee)PZAEC.*T1[6-9]$' })
if($gear.Count -ne 92){ throw 'Review eligible gear roster' }
foreach($item in $gear){
    $component=''; $amount=0
    if(-not [AECT16RuntimeFix.TacticalScrapRewards]::Classify($item.name,[ref]$component,[ref]$amount)){throw "Missing gear: $($item.name)"}
    $rank=[int]$item.name.Substring($item.name.Length-2)-14
    if($component -ne "PZAECBuildPartsR$rank" -or $amount -ne $(if($item.name.StartsWith('armor')){1}else{2})){throw "Wrong reward: $($item.name)"}
}
foreach($name in @('armorAthleticHelmet','meleeWpnSledgeT3SteelSledgehammer','gunPZAECEmberPistolT15','gunPZAECEmberPistolT20','gunPZAECEmberPistolT16Extra','modPZAECClosedLoopFeedT16','itemPZAECTestBundleCommonSupplies1','meleeToolRepairT5NailgunLegend')){
    $component=''; $amount=0
    if([AECT16RuntimeFix.TacticalScrapRewards]::Classify($name,[ref]$component,[ref]$amount)){throw "Unexpected eligible item: $name"}
}
'PASS: all 64 armor and 28 weapons match current XML; vanilla gear, other tiers, attachments and test bundles excluded.'
