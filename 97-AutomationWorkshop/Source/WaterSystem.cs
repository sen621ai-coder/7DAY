using System;
using System.Linq;
namespace YFAutomation
{
    public static class WaterSystem
    {
        public const int Capacity=200;
        public static int Free(int stored)=>Math.Max(0,Capacity-Math.Max(0,stored));
        static string Owner(TileEntityComposite te)=>(te.GetFeature<TEFeatureLockable>()?.GetOwner()??te.Owner)?.CombinedString;
        static System.Collections.Generic.IEnumerable<TileEntityComposite> Tanks(World world,TileEntityComposite machine)
        {
            var at=machine.ToWorldPos();var owner=Owner(machine);
            for(int y=-1;y<=1;y++)for(int x=-4;x<=4;x++)for(int z=-4;z<=4;z++)
            {
                if(x*x+z*z>16)continue;var pos=new Vector3i(at.x+x,at.y+y,at.z+z);
                if(!TransferRules.SameChunk(at.x,at.z,pos.x,pos.z))continue;
                var tank=world.GetTileEntity(pos) as TileEntityComposite;
                if(Logistics.Available(tank,"yfAutoWaterTank",owner))yield return tank;
            }
        }
        public static string Pump(World world,TileEntityComposite machine)
        {
            if(Logistics.Busy(machine))return "库存正在使用，抽水暂停";
            var at=machine.ToWorldPos();
            if(!Logistics.Sides.Any(s=>world.IsWater(Logistics.Add(at,s))))return "进水口须紧贴水体";
            var chunk=world.GetChunkFromWorldPos(at.x,at.z) as Chunk;if(chunk==null||chunk.IsLocked)return "等待区块可写";
            lock(ChunkTransferLock.For(chunk))
            {
                int type=ItemClass.GetItem("yfAutoIrrigationWater").type;
                if(type==0)return "灌溉水配置缺失";
                foreach(var tank in MachineInventory.UsesInternal(machine)?new[]{machine}:Tanks(world,machine))
                {
                    if(Logistics.Busy(tank))continue;
                    var storage=tank.GetFeature<TEFeatureStorage>();
                    int stored=storage.items.Where(s=>s!=null&&!s.IsEmpty()&&s.itemValue.type==type).Sum(s=>s.count);
                    if(Free(stored)==0)continue;
                    var copy=ProductionInventory.Clone(storage.items);
                    // Transport helper handles slot zero normally; no filter sample in a tank.
                    var unit=new[]{new ItemStack(new ItemValue(type),1)};
                    if(InventoryTransfer.MoveUnfiltered(unit,copy,i=>false,i=>Logistics.Locked(storage,i)||tank==machine&&!MachineInventory.IsOutput(i),v=>v.ItemClass.Stacknumber.Value)==0)continue;
                    var state=machine.GetFeature<TEFeatureAutomationState>();
                    if(state.Job!="pump"){state.Job="pump";state.Seconds=0;}
                    state.Seconds++;machine.SetChunkModified();
                    if(state.Seconds<5)return "抽水中 "+(int)(state.Seconds*20)+"%";
                    if(Logistics.Busy(machine)||Logistics.Busy(tank))return "库存正在使用，抽水暂停";
                    Array.Copy(copy,storage.items,copy.Length);state.Seconds=0;
                    tank.SetChunkModified();tank.SetModified();return "储水："+(stored+1)+"/"+Capacity;
                }
            }
            return "4米内放同主储水箱/水箱已满";
        }
        public static void Irrigate(World world,TileEntityComposite machine,FieldMachines.Work work)
        {
            int type=ItemClass.GetItem("yfAutoIrrigationWater").type;if(type==0)return;
            if(MachineInventory.UsesInternal(machine))
            {
                var storage=machine.GetFeature<TEFeatureStorage>();
                var copy=ProductionInventory.Clone(work.Input);
                if(ProductionInventory.Consume(copy,type,1,i=>!MachineInventory.IsInput(i)||Logistics.Locked(storage,i)))
                {work.Input=copy;work.Duration=5;work.Key+=":irrigated";}
                return;
            }
            foreach(var tank in Tanks(world,machine))
            {
                var storage=tank.GetFeature<TEFeatureStorage>();var copy=ProductionInventory.Clone(storage.items);
                if(!ProductionInventory.Consume(copy,type,1,i=>Logistics.Locked(storage,i)))continue;
                var original=work.Complete;
                var originalReady=work.Ready;
                work.Duration=5;work.Key+=":irrigated";
                work.Ready=()=>world.GetTileEntity(tank.ToWorldPos())==tank&&!Logistics.Busy(tank)&&(originalReady==null||originalReady());
                work.Complete=()=>{original?.Invoke();Array.Copy(copy,storage.items,copy.Length);tank.SetChunkModified();tank.SetModified();};
                return;
            }
        }
    }
}
