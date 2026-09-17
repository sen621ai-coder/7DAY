$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
# Execute the shipped patch against a small collector fixture. The production
# DLL is separately compiled against the real game, validating API signatures.
$fixture=@'
using System;
using System.Collections.Generic;
using System.Reflection;
namespace HarmonyLib {
 public class HarmonyMethod { public HarmonyMethod(Type t,string n){} }
 public class Harmony { public void Patch(object m,HarmonyMethod prefix=null,HarmonyMethod postfix=null){} }
 public static class AccessTools { public static object Method(Type t,string n)=>null; }
}
public struct Vector2i { public int x,y; public Vector2i(int a,int b){x=a;y=b;} }
public class ItemClass { public string Name="bundle"; public string GetItemName()=>Name; }
public class ItemValue { public int type=1; public ItemClass ItemClass=new ItemClass(); }
public class ItemStack { public int count; public ItemValue itemValue=new ItemValue(); public bool IsEmpty()=>count==0; }
public class Block { public string Name="AutoMinerIron"; public string GetBlockName()=>Name; }
public class BlockValue { public Block Block=new Block(); }
public class BlockCollector {
 public class OutputType { public string Name="IronBundle",OutputItem="bundle",OutputItemModded="bundle",Fuel="flower"; }
 public class FuelType {}
 public FuelType GetFuelType(string name)=>new FuelType();
 public int GetSandboxModifiedFuelNeeded(int count)=>count;
 public OutputType Output=new OutputType(); public OutputType GetOutputType(string n)=>Output;
}
public class TileEntityCollector {
 public class FillData { public int slot,fillTime,fillTimeLeft; }
 public BlockValue blockValue=new BlockValue(); public BlockCollector collector=new BlockCollector();
 public bool HasModCount; public ItemStack[] Items=new ItemStack[6];
 public int Chickens=3;
 public BlockCollector.OutputType[] SlotTypes;
 public BlockCollector.OutputType GetSlotOutputType(int i)=>SlotTypes==null?collector.Output:SlotTypes[i];
 public int getCurrentConvertCount(BlockCollector.OutputType t)=>t.Name=="honey"?(HasModCount?10:5):t.Name=="egg"?2*Chickens:t.Name=="feather"?5*Chickens:Chickens>0?1:0;
 public int FuelCount;
 public int fuelCost(BlockCollector.OutputType t,int count)=>10;
 public int getFuelCount(BlockCollector.FuelType type)=>FuelCount;
 public void SetModified(){}
 public Dictionary<string,FillData> fillDataLookup=new Dictionary<string,FillData>();
}
public class XUiC_ItemStack { public int OverrideStackCount; public int SlotNumber; }
public class XUiC_DewCollectorContainer { public T[] GetChildrenByType<T>()=>new T[0]; }
public static class CollectorFixture {
 static Type Patch=>typeof(AECT16RuntimeFix.CollectorBatchStorage);
 static object Call(string name,params object[] args)=>Patch.GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);
 static void Check(bool b,string message){if(!b)throw new Exception(message);}
 static int Find(TileEntityCollector te,BlockCollector.OutputType output=null){
  var slots=new List<int>();for(int i=0;i<te.Items.Length;i++)if(output==null||te.GetSlotOutputType(i).Name==output.Name)slots.Add(i);
  object[] a={te,slots,-1};Call("FreeSlot",a);return (int)a[2];}
 static int Produce(TileEntityCollector te,int batch,BlockCollector.OutputType output=null) {
  output=output??te.collector.Output;
  Call("Prepare",te,output);
  int slot=Find(te,output);if(slot<0)return 0;
  te.fillDataLookup[output.Name]=new TileEntityCollector.FillData{slot=slot};
  object[] limit={te,output,batch};Call("LimitBatch",limit);int produced=(int)limit[2];
  var product=new ItemStack{count=produced};product.itemValue.ItemClass.Name=output.OutputItem;product.itemValue.type=output.OutputItem.GetHashCode();
  object[] merge={te,output,product};Call("MergeBatch",merge);
  te.Items[slot]=(ItemStack)merge[2];te.fillDataLookup.Clear();return produced;
 }
 public static void Run() {
  foreach(bool mod in new[]{false,true})foreach(int batch in new[]{1,2,4}) {
   var te=new TileEntityCollector{HasModCount=mod};int total=0,steps=0;
   while(Find(te)>=0){total+=Produce(te,batch);Check(++steps<50,"production must stop when full");}
   int cap=mod?6:3;Check(total==cap*6,"total storage capacity");
   foreach(var s in te.Items)Check(s.count==cap,"per-slot cap or merge failed");
   Check(Produce(te,batch)==0,"full machine produced excess");
   te.Items[2].count--;Check(Produce(te,batch)==1,"partial slot refill must clamp batch");
  }
  var downgrade=new TileEntityCollector{HasModCount=true};while(Find(downgrade)>=0)Produce(downgrade,2);
  downgrade.HasModCount=false;Check(Produce(downgrade,1)==0,"downgrade must not produce into full slots");
  foreach(var s in downgrade.Items)Check(s.count==6,"downgrade deleted existing products");
  downgrade.Items[0].count=2;Check(Produce(downgrade,1)==1 && downgrade.Items[0].count==3,"downgrade refill");
  downgrade.fillDataLookup["IronBundle"]=new TileEntityCollector.FillData{slot=1};Call("Prepare",downgrade,downgrade.collector.Output);
  Check(downgrade.fillDataLookup.Count==0,"full pending slot was not cancelled");
  object[] size={downgrade,new Vector2i(3,3)};Call("Size",size);var v=(Vector2i)size[1];Check(v.x==3&&v.y==2,"layout");
  foreach(string name in new[]{"AutoMinerIron","AutoMinerLead","AutoMinerCoal","AutoMinerNitrate","AutoMinerClay","AutoMinerShale","AutoMinerBrass","yfAutoForestry"})
   Check(AECT16RuntimeFix.CollectorBatchStorage.Applies(name),"missing machine: "+name);
  Check(!AECT16RuntimeFix.CollectorBatchStorage.Applies("dewCollector"),"unrelated collector affected");
  foreach(int chickens in new[]{1,3,6})foreach(int slots in new[]{6,9}) {
   var coop=new TileEntityCollector{Chickens=chickens,Items=new ItemStack[slots],SlotTypes=new BlockCollector.OutputType[slots]};
   coop.blockValue.Block.Name="cntChickenCoop";
   var outputs=new[]{new BlockCollector.OutputType{Name="egg",OutputItem="foodEgg",OutputItemModded="foodEgg"},
    new BlockCollector.OutputType{Name="feather",OutputItem="resourceFeather",OutputItemModded="resourceFeather"},
    new BlockCollector.OutputType{Name="chicken",OutputItem="domesticatedChicken",OutputItemModded="domesticatedChicken"}};
   for(int j=0;j<slots;j++)coop.SlotTypes[j]=outputs[j/3];
   for(int j=0;j<slots/3;j++) {
    var output=outputs[j];int batch=coop.getCurrentConvertCount(output),total=0,steps=0;
    while(Find(coop,output)>=0){total+=Produce(coop,batch,output);Check(++steps<=6,"coop should hold two batches per slot");}
    Check(total==batch*6,"coop group total");
    for(int k=j*3;k<j*3+3;k++)Check(coop.Items[k].count==batch*2 && coop.Items[k].itemValue.ItemClass.Name==output.OutputItem,"coop cap or product crossed rows");
    coop.Items[j*3].count--;Check(Produce(coop,batch,output)==1,"coop partial refill");
   }
   object[] coopSize={coop,new Vector2i(3,slots/3)};Call("Size",coopSize);Check(((Vector2i)coopSize[1]).y==slots/3,"coop nesting box layout changed");
   coop.Chickens=1;foreach(var output in outputs)Produce(coop,coop.getCurrentConvertCount(output),output);
   if(chickens>1)Check(coop.Items[0].count==4*chickens,"removing chickens deleted stored products");
  }
  Console.WriteLine("PASS: coop 1/3/6 chickens; 6/9 slots; two batches per slot; separate output rows; refill; chicken removal preserves output.");
  var honey=new BlockCollector.OutputType{Name="honey",OutputItem="foodHoney",OutputItemModded="foodHoney"};
  var hive=new TileEntityCollector();hive.blockValue.Block.Name="cntApiary";
  var hivePatch=typeof(AECT16RuntimeFix.ApiaryProduction);
  foreach(bool brood in new[]{false,true})foreach(int fuel in new[]{0,9,10,20})foreach(ulong speed in new ulong[]{1,2}) {
   hive.HasModCount=brood;hive.FuelCount=fuel;
   object[] countArgs={hive,brood?2:1};hivePatch.GetMethod("Count",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,countArgs);
   Check((int)countArgs[1]==(brood?10:5),"apiary yield");
   object[] available={hive,honey,0};var native=(bool)hivePatch.GetMethod("Available",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,available);
   Check(!native&&(int)available[2]==(brood?10:5),"unfed apiary must still produce");
   object[] speedArgs={hive,honey,speed};hivePatch.GetMethod("Speed",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,speedArgs);
   Check((ulong)speedArgs[2]==speed*(fuel>=10?2UL:1UL),"apiary fed/foraging speed");
   var consume=(bool)hivePatch.GetMethod("Consume",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{hive,brood?10:5,honey});
   Check(consume==(fuel>=10),"foraging should not consume partial fuel");
  }
  hive.fillDataLookup["honey"]=new TileEntityCollector.FillData{fillTime=48000,fillTimeLeft=12000};
  var migrate=hivePatch.GetMethod("MigrateTimer",BindingFlags.NonPublic|BindingFlags.Static);
  migrate.Invoke(null,new object[]{hive,honey});migrate.Invoke(null,new object[]{hive,honey});
  Check(hive.fillDataLookup["honey"].fillTime==96000&&hive.fillDataLookup["honey"].fillTimeLeft==24000,"timer migration preserves progress and only runs once");
  Check(!AECT16RuntimeFix.ApiaryProduction.Applies(downgrade),"apiary patch leaked to miners");
  Console.WriteLine("PASS: apiary 5/10 yield; 0/9/10/20 fuel; extractor; fed/foraging switching; no partial fuel loss; idempotent timer migration; scope.");
  Console.WriteLine("PASS: six slots; 3/6 caps; batches 1/2/4; refill; full stop; downgrade preservation; pending slot; layout; scope.");
 }
}
'@
$source=Get-Content -Raw (Join-Path $root '99-AEC_T16_RuntimeFix/Source/CollectorBatchStorage.cs')
$apiary=Get-Content -Raw (Join-Path $root '99-AEC_T16_RuntimeFix/Source/ApiaryProduction.cs')
$source += "`n"+($apiary -replace '(?m)^using [^;]+;\r?\n','')
$fixture=$fixture -replace '(?m)^using [^;]+;\r?\n',''
Add-Type -TypeDefinition ("using System.Reflection;`n"+$source+"`n"+$fixture)
[CollectorFixture]::Run()
