using System;
using PZAEC.Fishing.Contracts;

namespace PZAEC.Fishing.Runtime
{
    // The native implementation stores these fields in the same player save as the inventory.
    public interface ICatchRecord
    {
        CatchResult Result { get; }
        bool Completed { get; }
        void Write(CatchResult result,bool completed);
    }
    public interface ICatchInventory
    {
        // Exactly one item. False means no mutation; exceptions must not trigger blind retry.
        bool TryAdd(RewardSpec reward,Guid settlementId);
    }
    public sealed class CatchSettlement : ICatchSettlement
    {
        readonly IFishingContent content;
        readonly ICatchRecord record;
        readonly ICatchInventory inventory;
        readonly string playerId;
        bool faulted;
        Guid activeSession;
        public CatchSettlement(IFishingContent content,ICatchRecord record,ICatchInventory inventory,string playerId)
        {this.content=content;this.record=record;this.inventory=inventory;this.playerId=playerId;}
        public bool HasPending => record.Result.SessionId!=Guid.Empty&&!record.Completed;
        public CatchResult Pending => record.Result;
        public bool OpenSession(Guid id)
        {if(faulted||HasPending||id==Guid.Empty||id==record.Result.SessionId)return false;activeSession=id;return true;}
        // Called only with the host's actual terminal snapshot, never a client or visual event payload.
        public bool RecordLanding(FishingSnapshot state,string fishId)
        {
            if(faulted||state.Phase!=FishingPhase.Resolved||state.SessionId==Guid.Empty||
                (state.Authority!=AuthorityMode.Standalone&&state.Authority!=AuthorityMode.Server))return false;
            if(record.Result.SessionId==state.SessionId)return true;
            if(HasPending||activeSession!=state.SessionId)return false;
            var result=new CatchResult {SessionId=state.SessionId,SettlementId=state.SessionId,PlayerPersistentId=playerId,
                FishDefinitionId=fishId,MassKg=state.FishMassKg,TerminalTick=state.Tick};
            RewardSpec reward;if(!content.TryGetReward(result,out reward)||reward.Count!=1)return false;
            record.Write(result,false);activeSession=Guid.Empty;return true;
        }
        public SettlementStatus TrySettle(CatchResult result)
        {
            var accepted=record.Result;
            if(faulted)return SettlementStatus.Deferred;
            if(result.SessionId==Guid.Empty||result.SessionId!=accepted.SessionId||result.SettlementId!=accepted.SettlementId||
                result.PlayerPersistentId!=playerId||result.FishDefinitionId!=accepted.FishDefinitionId||
                result.MassKg!=accepted.MassKg||result.TerminalTick!=accepted.TerminalTick)return SettlementStatus.Invalid;
            if(record.Completed)return SettlementStatus.AlreadyGranted;
            RewardSpec reward;if(!content.TryGetReward(accepted,out reward)||reward.Count!=1)return SettlementStatus.Invalid;
            try {
                if(!inventory.TryAdd(reward,result.SettlementId))return SettlementStatus.InventoryFull;
                record.Write(accepted,true);return SettlementStatus.Granted;
            } catch {faulted=true;throw;}
        }
    }
}
