using System;
using System.Linq;
namespace YFAutomation
{
    public static class FieldMachines
    {
        public sealed class Work
        {
            public ItemStack Product;
            public ItemStack[] Input;
            public string Key;
            public float Duration;
            public Action Complete;
            public Func<bool> Ready;
        }
        // Restrict field work to the placing player's active land claim and this chunk.
        // This is deliberately stricter than allies' access to shared chests.
        public static Work Find(World world,TileEntityComposite machine,ItemStack[] input,Func<int,bool> locked,int outputType,EntityPlayer player)
        {
            if(player==null)return null;
            var data=GameManager.Instance.GetPersistentPlayerList().GetPlayerDataFromEntityID(player.entityId);
            var at=machine.ToWorldPos();
            bool farm=machine.block.GetBlockName()=="yfAutoFarm";
            for(int y=farm?-1:-8;y<=(farm?1:-1);y++)for(int x=-3;x<=3;x++)for(int z=-3;z<=3;z++)
            {
                var pos=new Vector3i(at.x+x,at.y+y,at.z+z);
                if(!TransferRules.SameChunk(at.x,at.z,pos.x,pos.z)||world.GetLandClaimOwner(pos,data)!=EnumLandClaimOwner.Self)continue;
                var value=world.GetBlock(pos);var block=value.Block;string name=block.GetBlockName();
                if(farm)
                {
                    if(!name.EndsWith("3HarvestPlayer"))continue;
                    var seed=ItemClass.GetItem(name.Substring(0,name.Length-"3HarvestPlayer".Length)+"1");
                    if(seed.type==0)continue;
                    var planted=Block.GetBlockValue(seed.ItemClass.GetItemName());
                    if(!(planted.Block is BlockPlantGrowing growing)||!growing.CanGrowOn(world,new Vector3i(pos.x,pos.y-1,pos.z),planted))continue;
                    System.Collections.Generic.List<Block.SItemDropProb> drops;
                    if(!block.itemsToDrop.TryGetValue(EnumDropEvent.Harvest,out drops))continue;
                    var drop=drops.FirstOrDefault(d=>d.tag=="cropHarvest"&&d.prob>=1&&d.minCount>0&&ItemClass.GetItem(d.name).type==outputType);
                    if(string.IsNullOrEmpty(drop.name))continue;
                    var trial=ProductionInventory.Clone(input);
                    if(!ProductionInventory.Consume(trial,seed.type,1,locked))continue;
                    var work=new Work{Input=trial,Product=new ItemStack(new ItemValue(outputType),drop.minCount),Duration=10,
                        Key="farm:"+pos+":"+name,Complete=()=>{
                            if(world.GetBlock(pos).type!=value.type)throw new InvalidOperationException("作物在收割前已改变");
                            world.SetBlockRPC(new BlockValueRef(pos),planted);
                            if(world.GetBlock(pos).type!=planted.type)throw new InvalidOperationException("补种未完成，未扣种子或发放产物");
                        }};
                    WaterSystem.Irrigate(world,machine,work);return work;
                }
                else
                {
                    // Mineral point extraction: verifies an actual ore block, does not dig terrain.
                    string resource=null;
                    switch(name){case "terrOreIron":resource="resourceScrapIron";break;case "terrOreLead":resource="resourceScrapLead";break;case "terrOreCoal":resource="resourceCoal";break;case "terrOrePotassiumNitrate":resource="resourcePotassiumNitratePowder";break;case "terrOreOilDeposit":resource="resourceOilShale";break;}
                    if(resource==null||ItemClass.GetItem(resource).type!=outputType)continue;
                    var trial=ProductionInventory.Clone(input);
                    if(!ProductionInventory.Consume(trial,ItemClass.GetItem("yfAutoDrillCharge").type,1,locked))continue;
                    return new Work{Input=trial,Product=new ItemStack(new ItemValue(outputType),20),Duration=60,Key="mine:"+pos+":"+name};
                }
            }
            return null;
        }
    }
}
