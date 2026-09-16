using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Runtime.Serialization;
using YFAutomation;
public static class AutomationSmoke
{
 static int checks;
 static void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
 static ItemStack Stack(int type,int count,float wear=0,ushort quality=0)=>new ItemStack(new ItemValue{type=type,UseTimes=wear,Quality=quality},count);
 static int Move(ItemStack[] a,ItemStack[] b,int cap=100, int il=-1,int ol=-1)=>InventoryTransfer.Move(a,b,i=>i==il,i=>i==ol,v=>cap);
 public static int Main()
 {
  try {
   foreach(int count in new[]{1,16,17,100,1000}){
    var a=new[]{Stack(1,count)};var b=new[]{ItemStack.Empty};
    int moved=ConveyorTransfer.Move(a,b,i=>false,i=>false,16,16,v=>6000);
    Check(moved==Math.Min(count,16)&&a.Sum(s=>s.count)+b.Sum(s=>s.count)==count,"belt packet capacity and conservation");
    Check(ConveyorTransfer.Move(b,b,i=>false,i=>false,16,16,v=>6000)==0,"belt rejects self-transfer");
    var sink=new[]{ItemStack.Empty};Check(ConveyorTransfer.Move(b,sink,i=>false,i=>false,0,16,v=>6000)==0,"zero initial budget cannot forward arrivals");
    Check(ConveyorTransfer.Move(b,sink,i=>true,i=>false,16,16,v=>6000)==0,"locked belt does not move");
    Check(ConveyorTransfer.Move(b,sink,i=>false,i=>false,16,16,v=>6000,2)==0,"output filter blocks wrong item");
   }
   Check(TransferRules.SameChunk(-16,-1,-1,-16),"negative chunk coordinates");
   var invalidUnlockItem=(ItemClass)FormatterServices.GetUninitializedObject(typeof(ItemClass));
   invalidUnlockItem.Properties=new DynamicProperties();invalidUnlockItem.Properties.Values["UnlockedBy"]="";
   Check(invalidUnlockItem.UnlockedBy.Length==1,"native empty unlock produces one invalid entry");
   bool reproduced=false;try{invalidUnlockItem.UnlockedBy[0].GetName();}catch(NullReferenceException){reproduced=true;}
   Check(reproduced,"reproduces reported recipe unlock UI null reference");
   var fixedUnlockItem=(ItemClass)FormatterServices.GetUninitializedObject(typeof(ItemClass));fixedUnlockItem.Properties=new DynamicProperties();
   Check(fixedUnlockItem.UnlockedBy.Length==0,"omitted unlock yields no invalid UI entries");
   Check(!TransferRules.SameChunk(-1,0,0,0)&&!TransferRules.SameChunk(15,0,16,0),"boundary rejected");
   Check(!TransferRules.SameOwner(null,null)&&!TransferRules.SameOwner("a","b")&&TransferRules.SameOwner("a","a"),"owner checks");
   foreach(int count in new[]{1,15,16,17,100,1000}){
    var a=new[]{Stack(1,count)};var b=new[]{Stack(1,1),ItemStack.Empty};int total=count+1;
    for(int i=0;i<100;i++){
     int moved=Move(a,b,2000);Check(moved>=0&&moved<=16,"bounded transfer");
     Check(a.Sum(v=>v.count)+b.Sum(v=>v.count)==total,"conservation across repeated ticks");
     Check(b[0].count==1,"sample retained");
    }
    Check(a[0].IsEmpty()&&b[1].count==count,"drained once without duplicate");
   }
   var src=new[]{Stack(1,20)};var dst=new[]{Stack(2,1),ItemStack.Empty};Check(Move(src,dst)==0,"unmatched filter");
   dst[0]=Stack(1,1);Check(Move(src,dst,100,0)==0&&Move(src,dst,100,-1,1)==0,"slot locks respected");
   dst[1]=Stack(1,99);Check(Move(src,dst)==1&&dst[1].count==100&&src[0].count==19,"partial capacity");
   Check(Move(src,dst)==0&&src[0].count==19,"full output leaves source intact");
   Check(Move(src,new[]{ItemStack.Empty,ItemStack.Empty})==0,"no sample no movement");
   src=new[]{Stack(1,1,42,5)};dst=new[]{Stack(1,1),Stack(1,1,7,5),ItemStack.Empty};
   Check(Move(src,dst,1)==1&&dst[1].itemValue.UseTimes==7&&dst[2].itemValue.UseTimes==42&&dst[2].itemValue.Quality==5,"wear and quality survive without incompatible merge");
   var value=new ItemValue{type=1,Quality=5,UseTimes=13,Modifications=new[]{new ItemValue{type=2,Quality=3}}};
   src=new[]{new ItemStack(value,1)};dst=new[]{Stack(1,1),ItemStack.Empty};Move(src,dst,1);
   Check(dst[1].itemValue.Modifications[0].type==2&&dst[1].itemValue.Modifications[0].Quality==3,"installed mod preserved");
   Check(!ReferenceEquals(value,dst[1].itemValue)&&!ReferenceEquals(value.Modifications,dst[1].itemValue.Modifications),"deep copied values");
   Check(typeof(TileEntityComposite).GetMethod("UpdateTick",new[]{typeof(World)})!=null,"native tick hook exists");
   Check(typeof(Chunk).GetMethod("write",new[]{typeof(PooledBinaryWriter),typeof(bool)})!=null,"native chunk serialization hook exists");
   var chunk=(Chunk)FormatterServices.GetUninitializedObject(typeof(Chunk));
   object gate=ChunkTransferLock.For(chunk),held=null;Check(ReferenceEquals(gate,ChunkTransferLock.For(chunk)),"stable chunk gate");
   ChunkTransferLock.BeforeWrite(chunk,out held);Check(Monitor.IsEntered(gate),"serializer acquires transfer gate");ChunkTransferLock.AfterWrite(held);Check(!Monitor.IsEntered(gate),"serializer releases gate");
   src=new[]{Stack(1,1),Stack(2,25)};dst=new[]{ItemStack.Empty,ItemStack.Empty};
   Check(InventoryTransfer.MoveUnfiltered(src,dst,i=>i==0,i=>false,v=>100)==16,"unfiltered transfer bounded");
   Check(src[0].count==1&&src[1].count==9&&dst[0].count==16,"upstream sample retained and downstream slot zero usable");
   var original=new[]{Stack(1,10),Stack(1,7)};var copy=ProductionInventory.Clone(original);
   Check(ProductionInventory.Consume(copy,1,15,i=>i==1)==false,"locked ingredient unavailable");
   Check(original[0].count==10&&original[1].count==7,"failed detached transaction leaves source untouched");
   copy=ProductionInventory.Clone(original);Check(ProductionInventory.Consume(copy,1,15,i=>false)&&copy[1].count==2,"ingredients combined across slots");
   var outOriginal=new[]{Stack(2,1),Stack(2,99),ItemStack.Empty};var planned=ProductionInventory.Clone(outOriginal);
   Check(ProductionInventory.Produce(planned,Stack(2,5),i=>false,v=>100)&&planned[1].count==100&&planned[2].count==4,"batch output split across stacks");
   Check(outOriginal[1].count==99&&outOriginal[2].IsEmpty()&&planned[0].count==1,"output and sample isolated until commit");
   planned=ProductionInventory.Clone(outOriginal);Check(!ProductionInventory.Produce(planned,Stack(2,5),i=>i==2,v=>100),"locked output causes whole transaction rejection");
   Check(ProductionInventory.ScrapYield(100,1,1,false)==75,"native 75 percent equipment scrap yield");
   Check(ProductionInventory.ScrapYield(100,1,1,true)==100,"sandbox scrap100 yield");
   Check(ProductionInventory.ScrapYield(100,3,2,false)==49,"native integer weight ratio before scrap multiplier");
   Check(ProductionInventory.ScrapYield(1,5,1,false)==0,"underweight scrap not consumed");
   Check(WaterSystem.Free(0)==200&&WaterSystem.Free(199)==1&&WaterSystem.Free(200)==0&&WaterSystem.Free(201)==0,"pump tank upper bound");
   var ammoSlots=new[]{Stack(1,1),Stack(2,10),Stack(3,5)};
   Check(TurretFeed.FindRound(ammoSlots,i=>false,type=>type==2)==1,"turret accepts compatible ammunition only");
   Check(TurretFeed.FindRound(ammoSlots,i=>i==1,type=>type==2)==-1,"locked ammo is unavailable");
   Check(TurretFeed.FindRound(ammoSlots,i=>i==0,type=>type==1)==-1,"output sample cannot be fired");
   Check(TurretFeed.FindRound(ammoSlots,i=>false,type=>type==9)==-1,"wrong cartridge cannot feed turret");
   Check(ammoSlots.Sum(v=>v.count)==16,"ammo readiness query does not consume bullets");
   var magazine=new[]{Stack(2,1),Stack(2,80)};
   for(int shot=0;shot<80;shot++){
    int index=TurretFeed.FindRound(magazine,i=>i==0,type=>type==2);
    Check(TurretFeed.ConsumeRound(magazine,index)&&magazine.Sum(v=>v.count)==80-shot,"exactly one external round per shot");
   }
   Check(magazine[0].count==1&&magazine[1].IsEmpty()&&!TurretFeed.ConsumeRound(magazine,1),"empty magazine cannot fire or consume filter sample");
   bool nativeSuccess=true;ItemClass ammoClass=null;TurretFeed.AfterDecrement(null,ref ammoClass,ref nativeSuccess);
   Check(nativeSuccess,"successful native shot bypasses external consumption");
   var decrement=typeof(TileEntityPoweredRangedTrap).GetMethod("DecrementAmmo",new[]{typeof(ItemClass).MakeByRefType()});
   Check(decrement!=null&&decrement.ReturnType==typeof(bool),"native per-shot ammo hook signature");
   var state=new TEFeatureAutomationState{Job="forge:iron",Seconds=25.5f};
   using(var stream=new System.IO.MemoryStream()){
    var writer=new PooledBinaryWriter();writer.SetBaseStream(stream);state.Write(writer,TileEntity.StreamModeWrite.Persistency);writer.Flush();stream.Position=0;
    var reader=new PooledBinaryReader();reader.SetBaseStream(stream);var restored=new TEFeatureAutomationState();restored.Read(reader,TileEntity.StreamModeRead.Persistency);
    Check(restored.Job==state.Job&&restored.Seconds==25.5f,"native feature progress serialization round trip");
    stream.Position=0;restored.Job="trusted";restored.Seconds=3;restored.Read(reader,TileEntity.StreamModeRead.FromClient);
    Check(restored.Job=="trusted"&&restored.Seconds==3,"client cannot forge production progress");
   }
   InternalInventoryChecks();
   ConfigurationChecks();
   Console.WriteLine("PASS: "+checks+" inventory/production/configuration checks: conservation, filters, metadata, progress, settings persistence, stale edits and bounded wire data.");return 0;
  }catch(Exception ex){Console.WriteLine(ex);return 1;}
 }
 static void InternalInventoryChecks()
 {
  foreach(int count in new[]{1,16,17,100})foreach(int slot in new[]{0,17}){
   var machine=ItemStack.CreateArray(36);machine[slot]=Stack(2,count,12,3);machine[18]=Stack(1,4);
   var belt=new[]{ItemStack.Empty};
   int take=ConveyorTransfer.Move(machine,belt,i=>!MachineInventory.IsOutput(i),i=>false,16,16,v=>100);
   Check(take==4&&machine[slot].count==count&&machine[18].IsEmpty(),"belt extracts output without taking raw materials");
   belt=new[]{Stack(3,7,23,2)};
   int add=ConveyorTransfer.Move(belt,machine,i=>false,i=>!MachineInventory.IsInput(i),16,100,v=>100);
   Check(add==7&&machine.Take(18).Sum(v=>v.count)==count+7&&machine.Skip(18).All(v=>v.IsEmpty()),"belt inserts input without polluting outputs");
   Check(machine.First(v=>v.itemValue.type==3).itemValue.UseTimes==23,"machine endpoint preserves cargo durability");
  }
  var source=ItemStack.CreateArray(36);for(int i=0;i<18;i++)source[i]=Stack(1,100);
  var incoming=new[]{Stack(2,5)};
  Check(ConveyorTransfer.Move(incoming,source,i=>false,i=>!MachineInventory.IsInput(i),16,100,v=>100)==0&&incoming[0].count==5,"full input does not spill cargo into empty output partition");
  Check(!MachineInventory.IsInput(-1)&&!MachineInventory.IsOutput(36)&&MachineInventory.IsInput(17)&&MachineInventory.IsOutput(18),"partition boundaries are disjoint and bounded");
  var baseline=ItemStack.CreateArray(36);baseline[0]=Stack(1,3);baseline[18]=Stack(2,4);
  var consumed=ProductionInventory.Clone(baseline);Check(ProductionInventory.Consume(consumed,1,1,i=>!MachineInventory.IsInput(i)),"consume from input partition");
  var result=ProductionInventory.Clone(consumed);Check(ProductionInventory.Produce(result,Stack(2,5),i=>!MachineInventory.IsOutput(i),v=>100),"produce into detached post-consumption snapshot");
  Check(result[0].count==2&&result[18].count==9&&baseline[0].count==3&&baseline[18].count==4,"atomic internal production plan neither overwrites consumption nor mutates original");
  Check(!MachineSettingsStorage.ValidStorageMode("forged")&&MachineSettingsStorage.ValidStorageMode("internal")&&MachineSettingsStorage.ValidStorageMode("external")&&MachineSettingsStorage.ValidStorageMode(""),"inventory mode is a bounded server-validated enum");
  var feature=new TEFeatureMachineInventory();Check(feature.Legacy,"missing new feature preserves legacy mode");
  feature.Legacy=false;
  using(var stream=new System.IO.MemoryStream()){
   var writer=new PooledBinaryWriter();writer.SetBaseStream(stream);feature.Write(writer,TileEntity.StreamModeWrite.Persistency);writer.Flush();stream.Position=0;
   var reader=new PooledBinaryReader();reader.SetBaseStream(stream);var copy=new TEFeatureMachineInventory();copy.Read(reader,TileEntity.StreamModeRead.Persistency);
   Check(!copy.Legacy,"new machine mode round trips in native feature");stream.Position=0;copy.Legacy=true;copy.Read(reader,TileEntity.StreamModeRead.FromClient);Check(copy.Legacy,"client cannot change migration marker");
  }
  Check(ReflectionHelpers.GetTypeWithPrefix("XUiC_","YFAutomation.YFAutomationInventoryControls, YF.Automation")==typeof(XUiC_YFAutomationInventoryControls),"native XML resolves integrated inventory controls");
 }
 static void ConfigurationChecks()
 {
  foreach(int count in new[]{1,16,17,100})foreach(bool sample in new[]{false,true}){
   var a=new[]{Stack(1,8),Stack(2,count,12,3)};var b=new[]{sample?Stack(1,1):ItemStack.Empty,ItemStack.Empty};
   int moved=InventoryTransfer.Move(a,b,i=>false,i=>false,v=>100,2);
   Check(moved==Math.Min(count,16)&&a[0].count==8,"explicit filter overrides sample without moving another type");
   Check(b[0].count==(sample?1:0)&&a[1].count+b[1].count==count,"configured filter preserves reserved slot and item count");
   Check(b[1].itemValue.Quality==3&&b[1].itemValue.UseTimes==12,"configured filter preserves item metadata");
  }
  var src=new[]{Stack(2,30)};var dst=new[]{ItemStack.Empty,ItemStack.Empty};
  Check(InventoryTransfer.Move(src,dst,i=>true,i=>false,v=>100,2)==0,"configured filter respects input locks");
  Check(InventoryTransfer.Move(src,dst,i=>false,i=>i==1,v=>100,2)==0,"configured filter respects output locks");
  Check(InventoryTransfer.Move(src,dst,i=>false,i=>false,v=>100)==0,"unconfigured machine still needs sample");
  var original=new MachineSettings{Position="-1,100,2",Kind="yfAutoSorter",Owner="test",Source="-2,100,2",Target="-1,100,3",Product="resourceWood",Paused=true,Revision=7,StorageMode="internal"};
  var clone=original.Clone();clone.Product="resourceStone";
  Check(original.Product=="resourceWood","editing draft does not change active settings");
  Check(MachineConfiguration.ValidateRevision(original,clone,"a","a")==null,"current edit accepted");
  clone.Revision=6;Check(MachineConfiguration.ValidateRevision(original,clone,"a","a")!=null,"stale concurrent edit rejected");
  clone.Revision=7;Check(MachineConfiguration.ValidateRevision(original,clone,"b","a")!=null,"replaced or reloaded machine token rejected");
  Check(MachineConfiguration.ValidateRevision(original,clone,"","")!=null,"uninitialized token rejected");
  var path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"automation-settings-test-"+Guid.NewGuid().ToString("N")+".xml");
  try{
   Check(MachineSettingsStorage.Load(path).Machines.Count==0,"old world without config keeps defaults");
   MachineSettingsStorage.Save(path,new[]{original});var restored=MachineSettingsStorage.Load(path).Machines.Single();
   Check(restored.StorageMode=="internal"&&restored.Product==original.Product&&restored.Source==original.Source&&restored.Target==original.Target&&restored.Paused&&restored.Revision==7&&restored.Owner=="test","settings round trip preserves controls and identity");
   clone.Revision=8;MachineSettingsStorage.Save(path,new[]{clone});
   Check(MachineSettingsStorage.Load(path).Machines.Single().Revision==8&&MachineSettingsStorage.Load(path+".bak").Machines.Single().Revision==7,"atomic replacement retains previous backup");
   System.IO.File.WriteAllText(path,"broken");bool rejected=false;try{MachineSettingsStorage.Load(path);}catch{rejected=true;}
   Check(rejected,"corrupt settings fail closed instead of silently enabling machines");
   MachineSettingsStorage.Save(path,new[]{original,original});rejected=false;try{MachineSettingsStorage.Load(path);}catch{rejected=true;}
   Check(rejected,"duplicate persisted machine identity rejected");
  }finally{foreach(var suffix in new[]{"",".tmp",".bak"})if(System.IO.File.Exists(path+suffix))System.IO.File.Delete(path+suffix);}
  using(var stream=new System.IO.MemoryStream()){
   var writer=new PooledBinaryWriter();writer.SetBaseStream(stream);ConfigurationWire.Settings(writer,original);writer.Flush();stream.Position=0;
   var reader=new PooledBinaryReader();reader.SetBaseStream(stream);var received=ConfigurationWire.Settings(reader);
   Check(received.StorageMode=="internal"&&received.Product==original.Product&&received.Paused&&received.Revision==7&&received.Source==original.Source&&received.Target==original.Target&&stream.Position==stream.Length,"configuration wire round trip");
   Check(received.Owner==""&&received.Kind==""&&received.Position=="","request cannot assert machine owner or kind");
   stream.SetLength(0);stream.Position=0;writer.Write((ushort)161);writer.Flush();stream.Position=0;bool rejected=false;
   try{ConfigurationWire.Text(reader);}catch(System.IO.InvalidDataException){rejected=true;}Check(rejected,"oversize string rejected before reading allocation");
   stream.SetLength(0);stream.Position=0;writer.Write((ushort)3);writer.Write((byte)65);writer.Flush();stream.Position=0;rejected=false;
   try{ConfigurationWire.Text(reader);}catch(System.IO.EndOfStreamException){rejected=true;}Check(rejected,"truncated network settings rejected");
  }
  Check(ReflectionHelpers.GetTypeWithPrefix("XUiC_","YFAutomation.YFAutomationConfiguration, YF.Automation")==typeof(XUiC_YFAutomationConfiguration),"native XML resolves machine UI controller");
  Check(!MachineConfiguration.Supported("yfAutoBeltStraight")&&!MachineConfiguration.Supported("yfAutoWaterTank"),"configuration hooks exclude belts and tanks");
  Check(!MachineConfiguration.HasProduct("yfAutoWaterPump")&&!MachineConfiguration.HasBoxes("yfAutoAmmoFeed"),"utility UI hides unsupported controls");
 }
}
