using System;
using PZAEC.Fishing.Contracts;

namespace PZAEC.Fishing.Runtime
{
    public sealed class NativeCatchRecord : ICatchRecord
    {
        const string Prefix="pzaecFishingCatchV1";
        readonly EntityPlayerLocal player;
        readonly string playerId;
        public NativeCatchRecord(EntityPlayerLocal player,string playerId){this.player=player;this.playerId=playerId;}
        float Get(string key)=>player.Buffs.GetCustomVar(Prefix+key);
        void Set(string key,float value)=>player.Buffs.SetCustomVar(Prefix+key,value);
        public bool Completed => Get("State")==2;
        public CatchResult Result {
            get {
                float state=Get("State");if(state==0)return default(CatchResult);
                if(state!=1&&state!=2)throw new InvalidOperationException("Invalid saved fishing catch state");
                var bytes=new byte[16];ulong tick=0;
                for(int i=0;i<8;i++) {
                    float value=Get("Id"+i);if(!Scalar.IsFinite(value)||value<0||value>65535||value!=(int)value)throw new InvalidOperationException("Invalid catch id");
                    ushort part=(ushort)value;bytes[i*2]=(byte)part;bytes[i*2+1]=(byte)(part>>8);
                }
                for(int i=0;i<4;i++) {
                    float value=Get("Tick"+i);if(!Scalar.IsFinite(value)||value<0||value>65535||value!=(int)value)throw new InvalidOperationException("Invalid catch tick");
                    tick|=(ulong)(ushort)value<<(16*i);
                }
                var id=new Guid(bytes);
                if(id==Guid.Empty||tick>long.MaxValue)throw new InvalidOperationException("Invalid saved catch");
                return new CatchResult {SessionId=id,SettlementId=id,PlayerPersistentId=playerId,FishDefinitionId=FishingContract.FishDefinition,
                    MassKg=Get("Mass"),TerminalTick=(long)tick};
            }
        }
        public void Write(CatchResult result,bool completed)
        {
            var bytes=result.SessionId.ToByteArray();
            for(int i=0;i<8;i++)Set("Id"+i,bytes[i*2]|bytes[i*2+1]<<8);
            for(int i=0;i<4;i++)Set("Tick"+i,((ulong)result.TerminalTick>>(16*i))&65535);
            Set("Mass",result.MassKg);Set("State",completed?2:1);
        }
    }
    public sealed class NativeCatchInventory : ICatchInventory
    {
        readonly EntityPlayerLocal player;
        public NativeCatchInventory(EntityPlayerLocal player){this.player=player;}
        public bool TryAdd(RewardSpec reward,Guid settlementId)
        {
            if(reward.Count!=1||player.bag==null) return false;
            var value=ItemClass.GetItem(reward.ItemId).Clone();
            if(value.type<=0)throw new InvalidOperationException("Fishing reward item missing");
            value.SetMetadata("pzaecFishingMassKg",reward.FishMassKg);
            value.SetMetadata("pzaecFishingSettlement",settlementId.ToString("N"));
            // Native AddItem merges catches despite different metadata. Place one fish in an
            // empty slot through SetSlots so its identity/mass cannot be overwritten by stacking.
            var slots=player.bag.GetSlots();
            for(int i=0;i<slots.Length;i++) {
                if(slots[i]!=null&&slots[i].count>0)continue;
                var updated=(ItemStack[])slots.Clone();updated[i]=new ItemStack(value,1);
                player.bag.SetSlots(updated);return true;
            }
            return false;
        }
        public static bool ConsumeBait(EntityPlayerLocal player)
        {
            var bait=ItemClass.GetItem(FishingContract.BaitItem);
            if(bait.type<=0)return false;
            if(player.bag!=null&&player.bag.DecItem(bait,1)==1)return true;
            return player.inventory!=null&&player.inventory.DecItem(bait,1)==1;
        }
    }
}
