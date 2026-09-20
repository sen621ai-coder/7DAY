using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
namespace YFAutomation
{
    public sealed class AutomationMod : IModApi
    {
        public void InitMod(Mod mod)
        {
            var h=new Harmony("yf.automation.logistics");
            MachineConfigurationUI.Install(h);
            h.Patch(AccessTools.Method(typeof(TileEntityPoweredRangedTrap),"DecrementAmmo"),
                postfix:new HarmonyMethod(typeof(TurretFeed),nameof(TurretFeed.AfterDecrement)));
            h.Patch(AccessTools.Method(typeof(TileEntityComposite),nameof(TileEntityComposite.UpdateTick)),
                postfix:new HarmonyMethod(typeof(Logistics),nameof(Logistics.Observe)));
            h.Patch(AccessTools.Method(typeof(GameManager),"Update"),
                postfix:new HarmonyMethod(typeof(Logistics),nameof(Logistics.Tick)));
            h.Patch(AccessTools.Method(typeof(GameManager),"Update"),postfix:new HarmonyMethod(typeof(Conveyors),nameof(Conveyors.Tick)));
            h.Patch(AccessTools.Method(typeof(TileEntityComposite),"SetBlockEntityData"),postfix:new HarmonyMethod(typeof(ConveyorVisual),nameof(ConveyorVisual.Attach)));
            h.Patch(AccessTools.Method(typeof(TileEntityComposite),"SetBlockEntityData"),postfix:new HarmonyMethod(typeof(MachineDisplay),nameof(MachineDisplay.AttachInteraction)));
            h.Patch(AccessTools.Method(typeof(TEFeatureStorage),"GetActivationText"),postfix:new HarmonyMethod(typeof(ConveyorVisual),nameof(ConveyorVisual.ActivationText)));
            h.Patch(AccessTools.Method(typeof(TEFeatureSignable),"GetActivationText"),postfix:new HarmonyMethod(typeof(MachineDisplay),nameof(MachineDisplay.ActivationText)));
            h.Patch(AccessTools.Method(typeof(TEFeatureStorage),"GetActivationText"),postfix:new HarmonyMethod(typeof(MachineDisplay),nameof(MachineDisplay.ActivationText)));
            h.Patch(AccessTools.Method(typeof(Chunk),"write",new[]{typeof(PooledBinaryWriter),typeof(bool)}),
                prefix:new HarmonyMethod(typeof(ChunkTransferLock),nameof(ChunkTransferLock.BeforeWrite)),
                finalizer:new HarmonyMethod(typeof(ChunkTransferLock),nameof(ChunkTransferLock.AfterWrite)));
            Log.Out("[YFAutomation] 0.7.0 native machine inventory panels, direct conveyor endpoints, native interaction, logistics, production, irrigation and external turret magazines installed.");
        }
    }
    public static class Logistics
    {
        static World world;
        static float next;
        static readonly Dictionary<Vector3i,TileEntityComposite> machines=new Dictionary<Vector3i,TileEntityComposite>();
        internal static readonly Vector3i[] Sides={new Vector3i(1,0,0),new Vector3i(-1,0,0),new Vector3i(0,1,0),new Vector3i(0,-1,0),new Vector3i(0,0,1),new Vector3i(0,0,-1)};
        static Vector3i[] sides=>Sides;
        static int cursor;
        static readonly Dictionary<Vector3i,int> sourceCursors=new Dictionary<Vector3i,int>();
        static readonly HashSet<TileEntityComposite> faulted=new HashSet<TileEntityComposite>();
        public static void Observe(TileEntityComposite __instance,World __0)
        {
            // Harmony argument index avoids dependence on the native parameter's name.
            ObserveCore(__instance,__0);
            Conveyors.Observe(__instance,__0);
        }
        static void ObserveCore(TileEntityComposite te,World w)
        {
            if(w==null||w.IsRemote()||te==null||te.IsRemoving)return;
            if(world!=w){world=w;machines.Clear();faulted.Clear();sourceCursors.Clear();cursor=0;}
            if(te.block.GetBlockName()=="yfAutoSorter"||te.block.GetBlockName()=="yfAutoTransfer"||te.block.GetBlockName()=="yfAutoAmmoFeed"||te.block.GetBlockName()=="yfAutoWaterPump"||Production.IsMachine(te.block.GetBlockName()))machines[te.ToWorldPos()]=te;
        }
        public static void Tick()
        {
            if(ConnectionManager.Instance==null||!ConnectionManager.Instance.IsServer||GameManager.Instance?.IsPaused()==true||Time.realtimeSinceStartup<next)return;
            next=Time.realtimeSinceStartup+1;
            var w=GameManager.Instance?.World;if(w==null||w!=world)return;
            // Bound per-tick work; round-robin avoids starving large factories.
            var entries=machines.ToArray();if(entries.Length==0)return;
            for(int i=0;i<Math.Min(entries.Length,32);i++)
            {
                var entry=entries[(cursor+i)%entries.Length];
                if(w.GetTileEntity(entry.Key)!=entry.Value||entry.Value.IsRemoving){machines.Remove(entry.Key);continue;}
                if(faulted.Contains(entry.Value))continue;
                try{Run(entry.Value);}catch(Exception ex){faulted.Add(entry.Value);Log.Error("[YFAutomation] Paused "+entry.Key+": "+ex.Message);Status(entry.Value,"故障，请查看日志");}
            }
            cursor=(cursor+32)%entries.Length;
        }
        internal static Vector3i Add(Vector3i a,Vector3i b)=>new Vector3i(a.x+b.x,a.y+b.y,a.z+b.z);
        static string Owner(TileEntityComposite te)=>(te?.GetFeature<TEFeatureLockable>()?.GetOwner()??te?.Owner)?.CombinedString;
        internal static bool Busy(TileEntityComposite te)
        {
            if(te.bUserAccessing)return true;
            var storage=te.GetFeature<TEFeatureStorage>();var sign=te.GetFeature<TEFeatureSignable>();
            return storage!=null&&LockManager.Instance.IsLockedServer(storage,0) || sign!=null&&LockManager.Instance.IsLockedServer(sign,0);
        }
        internal static bool Available(TileEntityComposite te,string kind,string owner)=>te!=null&&!te.IsRemoving&&
            te.block.GetBlockName()==kind&&TransferRules.SameOwner(owner,Owner(te))&&!Busy(te);
        static void Status(TileEntityComposite te,string value)
        {
            var sign=te.GetFeature<TEFeatureSignable>();if(sign==null||Busy(te))return;
            if(sign.GetAuthoredText().Text!=value)sign.SetText(value,true,te.Owner);
        }
        static void Run(TileEntityComposite sorter)
        {
            if(Busy(sorter))return;
            var config=MachineConfiguration.Get(sorter);
            if(config.Paused){Status(sorter,"已暂停（机器配置）");return;}
            var at=sorter.ToWorldPos();string owner=Owner(sorter);
            if(string.IsNullOrEmpty(owner)){Status(sorter,"等待所有者");return;}
            bool powered=Powered(world,at);
            if(!powered){Status(sorter,"缺电：4格内需通电供电口");return;}
            if(sorter.block.GetBlockName()=="yfAutoWaterPump"){Status(sorter,WaterSystem.Pump(world,sorter));return;}
            if(sorter.block.GetBlockName()=="yfAutoAmmoFeed"){Status(sorter,TurretFeed.Run(sorter));return;}
            if(MachineInventory.UsesInternal(sorter))
            {
                var localChunk=world.GetChunkFromWorldPos(at.x,at.z) as Chunk;
                if(localChunk==null||localChunk.IsLocked)return;
                lock(ChunkTransferLock.For(localChunk))
                {
                    var player=GameManager.Instance.GetPersistentPlayerList()?.GetEntityPlayerFromUserId(sorter.GetFeature<TEFeatureLockable>()?.GetOwner()??sorter.Owner);
                    Status(sorter,Production.IsMachine(sorter.block.GetBlockName())?Production.Step(sorter,sorter,sorter,player):MachineInventory.PassThrough(sorter,config.Product));
                }
                return;
            }
            if(sorter.block.GetBlockName()=="yfAutoTransfer"){RunTransfer(sorter,owner);return;}
            bool production=Production.IsMachine(sorter.block.GetBlockName());
            var sources=sides.Select(offset=>world.GetTileEntity(Add(at,offset)) as TileEntityComposite)
                .Where(te=>Available(te,"yfAutoInput",owner)||production&&Available(te,"yfAutoOutput",owner)).ToArray();
            if(config.Source!="")sources=sources.Where(t=>MachineConfiguration.Key(t.ToWorldPos())==config.Source).ToArray();
            if(sources.Length==0){Status(sorter,config.Source!=""?"指定输入箱不可用/正在打开":"邻接同主人的输入箱");return;}
            int start;sourceCursors.TryGetValue(at,out start);start%=sources.Length;sourceCursors[at]=(start+1)%sources.Length;
            if(!production)sources=sources.Skip(start).Concat(sources.Take(start)).ToArray();
            var outputs=new List<TileEntityComposite>();bool boundary=false;
            // Short-range first release: output radius four horizontally, one vertically.
            for(int y=-1;y<=1;y++)for(int x=-4;x<=4;x++)for(int z=-4;z<=4;z++)
            {
                if(x*x+z*z>16)continue;
                var pos=Add(at,new Vector3i(x,y,z));
                var te=world.GetTileEntity(pos) as TileEntityComposite;
                if(!Available(te,"yfAutoOutput",owner))continue;
                if(!TransferRules.SameChunk(at.x,at.z,pos.x,pos.z)){boundary=true;continue;}
                outputs.Add(te);
            }
            outputs=outputs.OrderBy(te=>{var p=te.ToWorldPos();return Math.Abs(p.x-at.x)+Math.Abs(p.y-at.y)+Math.Abs(p.z-at.z);})
                .ThenBy(te=>te.ToWorldPos().x).ThenBy(te=>te.ToWorldPos().z).ThenBy(te=>te.ToWorldPos().y).ToList();
            if(config.Target!="")outputs=outputs.Where(t=>MachineConfiguration.Key(t.ToWorldPos())==config.Target).ToList();
            if(outputs.Count==0){Status(sorter,config.Target!=""?"指定输出箱不可用/正在打开":boundary?"输出箱跨区块，请移近":sorter.block.GetBlockName()=="yfAutoRecycler"?"4米内放输出箱，无需样品":"4米内放输出箱和样品");return;}
            if(production)
            {
                // Exactly one job gets time per machine tick, irrespective of box count.
                string report="等待物料/产品样品";
                var chunk=world.GetChunkFromWorldPos(at.x,at.z) as Chunk;
                if(chunk==null||chunk.IsLocked)return;
                lock(ChunkTransferLock.For(chunk))
                {
                    var player=GameManager.Instance.GetPersistentPlayerList()?.GetEntityPlayerFromUserId(sorter.GetFeature<TEFeatureLockable>()?.GetOwner()??sorter.Owner);
                    foreach(var source in sources)
                    {
                        var p=source.ToWorldPos();if(!TransferRules.SameChunk(at.x,at.z,p.x,p.z))continue;
                        foreach(var target in outputs)
                        {
                            if(source==target)continue;
                            report=Production.Step(sorter,source,target,player);
                            if(report.StartsWith("生产中")||report.StartsWith("完成")){Status(sorter,report);return;}
                        }
                    }
                }
                Status(sorter,report);return;
            }
            foreach(var source in sources)
            {
                var p=source.ToWorldPos();
                if(!TransferRules.SameChunk(at.x,at.z,p.x,p.z)){boundary=true;continue;}
                foreach(var target in outputs)
                {
                    int moved=Transfer(source,target,owner,config.Product);
                    if(moved>0){Status(sorter,"运行：已搬运 "+moved);return;}
                }
            }
            Status(sorter,boundary?"输入箱跨区块，请移近":"等待物料/空位/样品");
        }
        // Block-center distance, including vertical distance; build the offsets once.
        static readonly Vector3i[] powerOffsets=(from x in Enumerable.Range(-4,9)
            from y in Enumerable.Range(-4,9) from z in Enumerable.Range(-4,9)
            where x*x+y*y+z*z<=16
            orderby x*x+y*y+z*z
            select new Vector3i(x,y,z)).ToArray();
        internal static bool Powered(World w,Vector3i at)=>w!=null&&powerOffsets.Any(offset=>
            w.GetTileEntity(Add(at,offset)) is TileEntityPowered p&&p.block.GetBlockName()=="yfAutoPowerPort"&&p.IsPowered);
        internal static bool Locked(TEFeatureStorage storage,int slot)=>storage.HasSlotLocksSupport&&storage.SlotLocks!=null&&storage.SlotLocks[slot];
        static void RunTransfer(TileEntityComposite machine,string owner)
        {
            var at=machine.ToWorldPos();var chunk=world.GetChunkFromWorldPos(at.x,at.z) as Chunk;
            if(chunk==null||chunk.IsLocked)return;
            var config=MachineConfiguration.Get(machine);
            var boxes=sides.Select(o=>world.GetTileEntity(Add(at,o)) as TileEntityComposite).Where(t=>t!=null).ToArray();
            int moved=0;
            lock(ChunkTransferLock.For(chunk))
            {
                foreach(var source in boxes.Where(t=>Available(t,"yfAutoOutput",owner)&&(config.Source==""||MachineConfiguration.Key(t.ToWorldPos())==config.Source)))
                foreach(var target in boxes.Where(t=>Available(t,"yfAutoInput",owner)&&(config.Target==""||MachineConfiguration.Key(t.ToWorldPos())==config.Target)))
                {
                    var a=source.ToWorldPos();var b=target.ToWorldPos();
                    if(!TransferRules.SameChunk(at.x,at.z,a.x,a.z)||!TransferRules.SameChunk(at.x,at.z,b.x,b.z))continue;
                    var input=source.GetFeature<TEFeatureStorage>();var output=target.GetFeature<TEFeatureStorage>();
                    moved=InventoryTransfer.MoveUnfiltered(input.items,output.items,i=>i==0||Locked(input,i),i=>Locked(output,i),v=>v.ItemClass.Stacknumber.Value);
                    if(moved>0){source.SetChunkModified();target.SetChunkModified();source.SetModified();target.SetModified();Status(machine,"输送："+moved);return;}
                }
            }
            Status(machine,moved>0?"输送："+moved:"邻接输出箱→输入箱");
        }
        static int Transfer(TileEntityComposite source,TileEntityComposite target,string owner,string filter)
        {
            var a=source.ToWorldPos();var b=target.ToWorldPos();
            if(!TransferRules.SameChunk(a.x,a.z,b.x,b.z))return 0;
            var chunk=world.GetChunkFromWorldPos(a.x,a.z) as Chunk;if(chunk==null||chunk.IsLocked)return 0;
            // Both arrays belong to one native chunk. The same lock also guards
            // serialization (see Chunk.write patch) against half-applied copies.
            int moved=0;
            var gate=ChunkTransferLock.For(chunk);Monitor.Enter(gate);
            try
            {
                if(world.GetTileEntity(a)!=source||world.GetTileEntity(b)!=target||!Available(source,"yfAutoInput",owner)||!Available(target,"yfAutoOutput",owner))return 0;
                var input=source.GetFeature<TEFeatureStorage>();var output=target.GetFeature<TEFeatureStorage>();
                if(input==null||output==null||filter!=""&&ItemClass.GetItem(filter).type==0)return 0;
                moved=InventoryTransfer.Move(input.items,output.items,i=>Locked(input,i),i=>Locked(output,i),v=>v.ItemClass.Stacknumber.Value,filter==""?0:ItemClass.GetItem(filter).type);
                if(moved>0){source.SetChunkModified();target.SetChunkModified();}
                return moved;
            }
            finally
            {
                Monitor.Exit(gate);
                if(moved>0){source.SetModified();target.SetModified();}
            }
        }
    }
}

namespace YFAutomation
{
    // Separate from the game's recursive/ordering-sensitive ReaderWriterLockSlim.
    // Serializing a whole native chunk cannot observe a half-applied transfer.
    public static class ChunkTransferLock
    {
        static readonly ConditionalWeakTable<Chunk,object> gates=new ConditionalWeakTable<Chunk,object>();
        public static object For(Chunk chunk)=>gates.GetValue(chunk,k=>new object());
        public static void BeforeWrite(Chunk __instance,out object __state)
        {__state=For(__instance);Monitor.Enter(__state);}
        public static void AfterWrite(object __state){if(__state!=null)Monitor.Exit(__state);}
    }
}
