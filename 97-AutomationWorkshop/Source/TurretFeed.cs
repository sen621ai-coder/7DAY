using System;
using System.Linq;
namespace YFAutomation
{
    // External magazine: rounds remain in native chest storage until actually fired.
    // Never transfer rounds into PowerRangedTrap.Stacks (saved separately in power.dat).
    public static class TurretFeed
    {
        public static int FindRound(ItemStack[] items,Func<int,bool> locked,Func<int,bool> accepted)
        {
            for(int i=0;i<items.Length;i++)
                if(!locked(i)&&items[i]!=null&&!items[i].IsEmpty()&&accepted(items[i].itemValue.type))return i;
            return -1;
        }
        public static bool ConsumeRound(ItemStack[] items,int slot)
        {
            if(slot<0||slot>=items.Length||items[slot]==null||items[slot].IsEmpty())return false;
            var value=items[slot].Clone();value.count--;
            items[slot]=value.count==0?ItemStack.Empty:value;return true;
        }
        public static void AfterDecrement(TileEntityPoweredRangedTrap __instance,ref ItemClass __0,ref bool __result)
        {
            if(__result||ConnectionManager.Instance==null||!ConnectionManager.Instance.IsServer)return;
            ItemClass ammo;
            if(TryRound(__instance,true,out ammo)){__0=ammo;__result=true;}
        }
        public static string Run(TileEntityComposite feeder)
        {
            var world=GameManager.Instance.World;var pos=feeder.ToWorldPos();int ready=0;
            foreach(var side in Logistics.Sides)
            {
                var turret=world.GetTileEntity(Logistics.Add(pos,side)) as TileEntityPoweredRangedTrap;
                ItemClass ammo;
                if(turret==null||!TryRound(turret,false,out ammo))continue;
                // The powered feeder explicitly enables automatic defense; targeting is unchanged.
                if(!turret.IsLocked){turret.IsLocked=true;turret.SetModified();}ready++;
            }
            return ready>0?"外接供弹就绪："+ready:"邻接炮塔和匹配弹药箱";
        }
        static bool TryRound(TileEntityPoweredRangedTrap turret,bool consume,out ItemClass ammo)
        {
            ammo=null;var world=GameManager.Instance?.World;
            if(world==null||world.IsRemote()||turret.IsRemoving||!turret.IsPowered||turret.bUserAccessing||LockManager.Instance.IsLockedServer(turret,0))return false;
            var pos=turret.ToWorldPos();var owner=turret.GetOwner()?.CombinedString;
            if(string.IsNullOrEmpty(owner)||turret.AmmoItems==null)return false;
            var chunk=world.GetChunkFromWorldPos(pos.x,pos.z) as Chunk;if(chunk==null||chunk.IsLocked)return false;
            lock(ChunkTransferLock.For(chunk))
            {
                foreach(var side in Logistics.Sides)
                {
                    var fpos=Logistics.Add(pos,side);
                    var feeder=world.GetTileEntity(fpos) as TileEntityComposite;
                    if(!Logistics.Available(feeder,"yfAutoAmmoFeed",owner)||MachineConfiguration.Paused(feeder)||!Logistics.Powered(world,fpos)||!TransferRules.SameChunk(pos.x,pos.z,fpos.x,fpos.z))continue;
                    bool internalStorage=MachineInventory.UsesInternal(feeder);
                    foreach(var inputSide in internalStorage?new[]{Vector3i.zero}:Logistics.Sides)
                    {
                        var inputPos=Logistics.Add(fpos,inputSide);var box=world.GetTileEntity(inputPos) as TileEntityComposite;
                        if(!TransferRules.SameChunk(pos.x,pos.z,inputPos.x,inputPos.z)||
                            !(internalStorage&&box==feeder||Logistics.Available(box,"yfAutoInput",owner)||Logistics.Available(box,"yfAutoOutput",owner)))continue;
                        var storage=box.GetFeature<TEFeatureStorage>();if(storage==null)continue;
                        int slot=FindRound(storage.items,i=>Logistics.Locked(storage,i)||internalStorage&&!MachineInventory.IsInput(i)||i==0&&box.block.GetBlockName()=="yfAutoOutput",
                            type=>turret.AmmoItems.Any(v=>v!=null&&v.Id==type));
                        if(slot<0)continue;
                        ammo=storage.items[slot].itemValue.ItemClass;
                        if(consume)
                        {
                            if(!ConsumeRound(storage.items,slot)){ammo=null;return false;}
                            box.SetChunkModified();box.SetModified();
                        }
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
