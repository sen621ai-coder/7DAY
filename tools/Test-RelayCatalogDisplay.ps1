#Requires -Version 7.0
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Test-ModelTintFix.ps1')
Add-Type -ReferencedAssemblies ($references + $frameworkReferences) -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Linq;
using AECT16RuntimeFix;
public static class RelayCatalogRegression {
    static void Check(bool ok, string why) { if(!ok) throw new Exception(why); }
    public static string Run(string[] catalog) {
        var rows = catalog.Reverse().Concat(catalog.Take(12)).ToList();
        var original = rows.ToArray();
        var indices = Enumerable.Range(0, rows.Count).Select(i => i * 3 + 7).ToList();
        RelayCatalogDisplay.Reorder(rows, indices, rows.ToArray());
        for(int i=0; i<rows.Count; i++) {
            Check(rows[i] == original[(indices[i]-7)/3], "Stock index/item mismatch");
            if(i>0) Check(RelayCatalogDisplay.Tier(rows[i-1]) <= RelayCatalogDisplay.Tier(rows[i]), "Tier ordering");
        }
        Check(indices.Distinct().Count()==original.Length, "Lost/merged stock entries");
        var firstRows=rows.ToArray(); var firstIndices=indices.ToArray();
        RelayCatalogDisplay.Reorder(rows, indices, rows.ToArray());
        Check(rows.SequenceEqual(firstRows) && indices.SequenceEqual(firstIndices), "Unstable duplicate ordering");
        // A filtered subset retains original server stock indices across pages.
        rows=rows.Where((v,i)=>i%2==0).ToList();
        indices=firstIndices.Where((v,i)=>i%2==0).ToList();
        RelayCatalogDisplay.Reorder(rows, indices, rows.ToArray());
        for(int page=0;page*20<rows.Count;page++)
            for(int i=page*20;i<Math.Min(rows.Count,(page+1)*20);i++)
                Check(rows[i]==original[(indices[i]-7)/3], "Filtered pagination mapping");
        Check(RelayCatalogDisplay.Kind("itemAECDumdumContract_T09")==0,"Legacy hunt");
        Check(RelayCatalogDisplay.Kind("itemAECArcherMinionAssaultContract_T03")==1,"Assault");
        Check(RelayCatalogDisplay.Kind("itemAECDoomlordContract_100waveschallenge_minion03_T15")==2,"100 waves");
        Check(RelayCatalogDisplay.Kind("itemAECAllInOneHuntContract_T15")==3,"Combined");
        Check(RelayCatalogDisplay.CleanName("[FFD700][AEC][-] 100波次挑战 0[-][FFD700]★[-] | 手枪兵 [5★]")=="100波次挑战 0★ | 手枪兵","Difficulty marker lost");
        Check(!RelayCatalogDisplay.IsRelay(1) && !RelayCatalogDisplay.IsRelay(97) && RelayCatalogDisplay.IsRelay(91) && RelayCatalogDisplay.IsRelay(96),"Trader scope");
        Check(RelayCatalogDisplay.Tier("casinoCoin")==int.MaxValue,"Non-contract sorting");
        return "PASS: "+catalog.Length+" catalog entries; sorted/duplicate/filtered/page stock-index mapping; stable order; labels preserve challenge difficulty; relay-only scope.";
    }
}
'@
[xml]$original=Get-Content (Join-Path $modRoot '03-AEC-AIO_BOSS_EXTREME_EDITION/Config/traders.xml') -Raw
[xml]$extra=Get-Content (Join-Path $modRoot '99-AEC_T16_RuntimeFix/Config/traders.xml') -Raw
$names=@($original.SelectNodes('//trader_item_group[starts-with(@name,"aecContractsPool")]/item') | ForEach-Object {$_.name})
$names+=@($extra.SelectNodes('//item[@name]')|ForEach-Object {$_.name})
[RelayCatalogRegression]::Run([string[]]$names)
$native=[HarmonyLib.AccessTools]::Method([XUiC_TraderWindow],'FilterByName')
if(-not $native){throw 'Native filter entrypoint missing'}
foreach($field in @('currentInventory','currentIndexList','isSecretStash')){if(-not [HarmonyLib.AccessTools]::Field([XUiC_TraderWindow],$field)){throw "Native field missing: $field"}}
$binding=[HarmonyLib.AccessTools]::Method([XUiC_TraderItemEntry],'GetBindingValueInternal')
if(($binding.GetParameters().Name -join ',') -ne 'value,bindingName'){throw 'Native label signature changed'}
'PASS: native view hooks present. Live trader UI/purchases require in-game verification.'
