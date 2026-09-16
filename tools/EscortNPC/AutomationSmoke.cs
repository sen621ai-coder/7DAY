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
   Check(TransferRules.SameChunk(-16,-1,-1,-16),"negative chunk coordinates");
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
   Console.WriteLine("PASS: "+checks+" inventory/production checks: conservation, capacity, filters, sample, metadata, detached commits, progress serialization and client authority.");return 0;
  }catch(Exception ex){Console.WriteLine(ex);return 1;}
 }
}
