using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace PZAEC.FlyingSword
{
    public static class SwordRuntime
    {
        public sealed class Session {public EntityPlayer Player;public int Sequence=-1,ChargeSlot=-1;public string ChargeId;public float ChargeAt=-1,NextShot,NextRefill,LastAttack=-100,NextSync,Seen;}
        static readonly Dictionary<int,Session> sessions=new Dictionary<int,Session>();
        static readonly Dictionary<int,int> received=new Dictionary<int,int>();
        static World world;static int sequence,eventSequence;static float nextSweep;
        public static bool Server=>ConnectionManager.Instance!=null&&ConnectionManager.Instance.IsServer;
        public static Session State(EntityPlayer p){Session s;if(!sessions.TryGetValue(p.entityId,out s)||s.Player!=p){s=new Session{Player=p,Seen=Time.time};sessions[p.entityId]=s;}return s;}
        public static void Install(Harmony h){h.Patch(AccessTools.Method(typeof(GameManager),"Update"),postfix:new HarmonyMethod(typeof(SwordRuntime),nameof(Tick)));h.Patch(AccessTools.Method(typeof(EntityVehicle),"FixedUpdateForces"),prefix:new HarmonyMethod(typeof(SwordRuntime),nameof(Forces)));
            h.Patch(AccessTools.Method(typeof(EntityPlayer),"Kill"),prefix:new HarmonyMethod(typeof(SwordRuntime),nameof(RecallPlayer)));
            h.Patch(AccessTools.Method(typeof(EntityPlayer),"OnEntityUnload"),prefix:new HarmonyMethod(typeof(SwordRuntime),nameof(RecallPlayer)));
            h.Patch(AccessTools.Method(typeof(GameManager),"SaveAndCleanupWorld"),prefix:new HarmonyMethod(typeof(SwordRuntime),nameof(RecallAll)));
        }
        static void RecallPlayer(EntityPlayer __instance){if(!Server||__instance.world==null)return;foreach(var e in __instance.world.Entities.list.ToArray())if(e is EntityJuque v&&Owned(__instance,v))Return(__instance,v);}
        static void RecallAll(){if(!Server||world==null)return;foreach(var p in world.Entities.list.OfType<EntityPlayer>().ToArray())RecallPlayer(p);}
        static bool Forces(EntityVehicle __instance){var v=__instance as EntityJuque;if(v==null)return true;v.StepFlight(Time.fixedDeltaTime);return false;}
        public static void Send(EntityPlayerLocal p,SwordOp op,int target=0,Vector3? a=null,Vector3? b=null)
        {if(p==null)return;var ray=p.GetLookRay();var origin=a??ray.origin;var dir=b??ray.direction;int seq=++sequence;if(Server)Request(p.world,p.entityId,op,target,seq,origin,dir);else {if(op==SwordOp.Charge)State(p).ChargeAt=Time.time;else if(op==SwordOp.Cancel||op==SwordOp.Release||op==SwordOp.Deploy)State(p).ChargeAt=-1;ConnectionManager.Instance.SendToServer(NetPackageManager.GetPackage<NetPackagePZAECJuqueIntent>().Setup(op,target,seq,origin,dir));}}
        public static void Request(World w,int actor,SwordOp op,int target,int seq,Vector3 origin,Vector3 direction)
        {
            var p=w.GetEntity(actor) as EntityPlayer;if(p==null||p.IsDead()||!SwordRules.Finite(origin)||!SwordRules.Finite(direction))return;
            var s=State(p);if(seq<=s.Sequence)return;s.Sequence=seq;
            if(op==SwordOp.Flight){var v=w.GetEntity(target) as EntityJuque;if(v==null||v.GetAttached(0)!=p||!Owned(p,v))return;if(Mathf.Abs(origin.x)>1||Mathf.Abs(origin.y)>1||Mathf.Abs(origin.z)>1||direction.x<0||direction.x>1)return;v.Throttle=origin.x;v.Turn=origin.y;v.Vertical=origin.z;v.Boost=direction.x>.5f;v.InputAt=Time.time;return;}
            if(op==SwordOp.Cancel){if(s.ChargeAt>=0)Emit(5,p,p.inventory.holdingItemIdx,0,p.inventory.holdingItemItemValue,p.position,Vector3.zero,0);s.ChargeAt=-1;return;}
            var held=p.inventory.holdingItemItemValue;
            if(op==SwordOp.Deploy){if(p.AttachedToEntity!=null||!SwordRules.IsSword(held)||SwordRules.Deployed(held)!=0)return;if(s.ChargeAt>=0)Emit(5,p,p.inventory.holdingItemIdx,0,held,p.position,Vector3.zero,0);s.ChargeAt=-1;Deploy(w,p,held);return;}
            if(op==SwordOp.Board){var v=w.GetEntity(target) as EntityJuque;if(v==null||!Owned(p,v)||p.AttachedToEntity!=null||v.GetAttached(0)!=null||(v.position-p.position).sqrMagnitude>16)return;p.Buffs.SetCustomVar(SwordRules.FallKey,0);v.vehicle.SetFuelLevel(1800);v.IsEngineRunning=true;v.EnterVehicle(p);s.ChargeAt=-1;return;}
            if(op==SwordOp.Return){var v=w.GetEntity(target) as EntityJuque;if(v==null||!Owned(p,v)||v.GetAttached(0)!=null&&v.GetAttached(0)!=p)return;if(p.AttachedToEntity!=v&&(v.position-p.position).sqrMagnitude>25)return;Return(p,v);return;}
            if(op==SwordOp.Refill){if(Time.time<s.NextRefill)return;s.NextRefill=Time.time+.5f;Refill(p);return;}
            if(!SwordRules.IsSword(held)||SwordRules.Deployed(held)!=0||p.AttachedToEntity!=null||held.MaxUseTimes>0&&held.UseTimes>=held.MaxUseTimes)return;
            if(op==SwordOp.Charge){if(Time.time<s.NextShot||s.ChargeAt>=0)return;s.ChargeAt=Time.time;s.ChargeSlot=p.inventory.holdingItemIdx;s.ChargeId=SwordRules.EnsureId(held);Emit(3,p,s.ChargeSlot,0,held,p.position,direction,0);return;}
            if(op==SwordOp.Release){
                float start=s.ChargeAt;s.ChargeAt=-1;Emit(5,p,s.ChargeSlot,0,held,p.position,Vector3.zero,0);
                if(start<0||Time.time<s.NextShot||s.ChargeSlot!=p.inventory.holdingItemIdx||s.ChargeId!=SwordRules.Id(held)||(origin-p.getHeadPosition()).sqrMagnitude>9||direction.sqrMagnitude<.9f||direction.sqrMagnitude>1.1f||Vector3.Dot(p.GetLookVector(),direction.normalized)<.5f)return;
                float c=SwordRules.Charge(Time.time-start),cost=SwordRules.Cost(c),energy=SwordRules.Energy(held);if(energy<cost)return;
                held.SetMetadata(SwordRules.EnergyKey,energy-cost);held.UseTimes+=1;s.LastAttack=Time.time;s.NextShot=Time.time+(c>0?1.5f:.5f);Notify(p,s.ChargeSlot);
                Emit(7,p,s.ChargeSlot,0,held,p.position,direction.normalized,c);SwordCombat.Schedule(p,held,direction.normalized,c);
            }
        }
        static void Deploy(World w,EntityPlayer p,ItemValue item)
        {
            var forward=Vector3.ProjectOnPlane(p.GetLookVector(),Vector3.up).normalized;if(forward.sqrMagnitude<.1f)forward=Vector3.forward;
            Vector3 start=p.position+forward*2+Vector3.up*1.5f;
            if(!Voxel.Raycast(w,new Ray(start,Vector3.down),4,-538750997,8,0))return;
            var ground=Voxel.voxelRayHitInfo.hit.pos;var local=ground-Origin.position+Vector3.up*.25f;
            if(Physics.CheckBox(local+Vector3.up, new Vector3(.4f,.85f,1.55f),Quaternion.LookRotation(forward),-538750997,QueryTriggerInteraction.Ignore))return;
            int slot=p.inventory.holdingItemIdx;var id=SwordRules.EnsureId(item);
            foreach(var e in w.Entities.list)if(e is EntityJuque duplicate&&duplicate.SwordId==id)return;
            var v=EntityFactory.CreateEntity(EntityClass.FromString(SwordRules.VehicleName),ground+Vector3.up*.25f) as EntityJuque;if(v==null)return;
            v.SwordId=id;v.OwnerActor=p.entityId;v.OwnerSlot=slot;v.Energy=SwordRules.Energy(item);v.SetRotation(new Vector3(0,Quaternion.LookRotation(forward).eulerAngles.y,0));
            if(p.PersistentPlayerData!=null)v.SetOwner(p.PersistentPlayerData.PrimaryId);
            w.SpawnEntityInWorld(v);SwordInventory.Reserve(p,slot,item);item.SetMetadata(SwordRules.DeployedKey,v.entityId);item.SetMetadata(SwordRules.EnergyKey,v.Energy);Notify(p,slot);Emit(0,p,slot,v.entityId,item,v.position,Vector3.zero,0);
        }
        public static bool Owned(EntityPlayer p,EntityJuque v){if(p==null||v==null||v.OwnerSlot<0||v.OwnerSlot>=p.inventory.SlotCount)return false;return SwordRules.Id(p.inventory.GetItem(v.OwnerSlot).itemValue)==v.SwordId&&SwordRules.Deployed(p.inventory.GetItem(v.OwnerSlot).itemValue)==v.entityId&&(v.GetOwner()==null||p.PersistentPlayerData!=null&&v.IsOwner(p.PersistentPlayerData.PrimaryId));}
        public static void Return(EntityPlayer p,EntityJuque v)
        {
            if(!Owned(p,v))return;var item=p.inventory.GetItem(v.OwnerSlot).itemValue;bool rider=p.AttachedToEntity==v;
            var from=v.position;var direction=v.transform.forward;
            if(rider){if(p is EntityPlayerLocal local)local.inventory.ReleaseAll(local.playerInput);SwordSafety.Grant(p);try{SwordSafety.Detaching=true;p.SendDetach();}finally{SwordSafety.Detaching=false;}}
            item.SetMetadata(SwordRules.EnergyKey,v.Energy);item.SetMetadata(SwordRules.DeployedKey,0);SwordInventory.RestoreLock(p,v.OwnerSlot,item);State(p).ChargeAt=-1;Notify(p,v.OwnerSlot);
            Emit(2,p,v.OwnerSlot,v.entityId,item,from,direction,rider?1:0);
            p.world.RemoveEntity(v.entityId,EnumRemoveEntityReason.Despawned);
        }
        public static void Notify(EntityPlayer p,int slot){p.inventory.GetItem(slot).itemValue.NotifyChanged();p.inventory.OnSlotChanged(slot);}
        static void Refill(EntityPlayer p)
        {
            int slot=-1;for(int i=0;i<p.inventory.SlotCount;i++){var iv=p.inventory.GetItem(i).itemValue;if(SwordRules.IsSword(iv)&&SwordRules.Energy(iv)<SwordRules.Capacity[SwordRules.Tier(iv)]){slot=i;if(i==p.inventory.holdingItemIdx||SwordRules.Deployed(iv)>0)break;}}
            if(slot<0)return;int crystal=ItemClass.GetItem(SwordRules.Crystal,false).type,source=-1;bool bag=false;ItemStackGrid grid=p.inventory.ItemGrid;
            for(int i=0;i<grid.Length;i++)if(grid.GetItem(i).itemValue.type==crystal&&grid.GetItem(i).count>0){source=i;break;}
            if(source<0){bag=true;grid=p.bag.ItemGrid;for(int i=0;i<grid.Length;i++)if(grid.GetItem(i).itemValue.type==crystal&&grid.GetItem(i).count>0){source=i;break;}}
            if(source<0)return;grid.ChangeCount(source,-1);int remaining=grid.GetItem(source).count;
            var item=p.inventory.GetItem(slot).itemValue;item.SetMetadata(SwordRules.EnergyKey,Mathf.Min(SwordRules.Capacity[SwordRules.Tier(item)],SwordRules.Energy(item)+250));var vehicle=p.world.GetEntity(SwordRules.Deployed(item)) as EntityJuque;if(vehicle!=null&&Owned(p,vehicle))vehicle.Energy=SwordRules.Energy(item);Notify(p,slot);Emit(4,p,slot,bag?-source-1:source,item,Vector3.zero,new Vector3(remaining,0,0),0);
        }
        public static void Emit(byte kind,EntityPlayer p,int slot,int entity,ItemValue item,Vector3 a,Vector3 b,float value)
        {
            var ev=NetPackageManager.GetPackage<NetPackagePZAECJuqueEvent>().Setup(kind,p.entityId,slot,entity,SwordRules.Id(item),SwordRules.Energy(item),a,b,value,++eventSequence);
            ConnectionManager.Instance.SendPackage(ev,false,-1,-1,-1,null,512);Receive(p.world,ev);
        }
        public static void Receive(World w,NetPackagePZAECJuqueEvent e)
        {
            int previous;if(received.TryGetValue(e.Actor,out previous)&&e.Sequence<=previous)return;received[e.Actor]=e.Sequence;
            var p=w.GetEntity(e.Actor) as EntityPlayer;if(p==null)return;
            if(e.Kind==6&&!Server){int index=-e.Slot-1;if(index>=0&&index<p.bag.ItemGrid.Length){var stored=p.bag.ItemGrid.GetItem(index).itemValue;if(SwordRules.IsSword(stored)&&(string.IsNullOrEmpty(SwordRules.Id(stored))||SwordRules.Id(stored)==e.SwordId)){stored.SetMetadata(SwordRules.IdKey,e.SwordId);stored.SetMetadata(SwordRules.EnergyKey,e.Energy);stored.NotifyChanged();}}return;}
            if(e.Kind==4&&!Server){var grid=e.EntityId<0?p.bag.ItemGrid:p.inventory.ItemGrid;int index=e.EntityId<0?-e.EntityId-1:e.EntityId;if(index>=0&&index<grid.Length&&grid.GetItem(index).itemValue.type==ItemClass.GetItem(SwordRules.Crystal,false).type)grid.ChangeCount(index,(int)e.B.x-grid.GetItem(index).count);}
            var flying=e.Kind==0?w.GetEntity(e.EntityId) as EntityJuque:null;if(flying!=null)flying.Energy=e.Energy;
            if(!Server&&e.Slot>=0&&e.Slot<p.inventory.SlotCount){var item=p.inventory.GetItem(e.Slot).itemValue;if(SwordRules.IsSword(item)&&(string.IsNullOrEmpty(SwordRules.Id(item))||SwordRules.Id(item)==e.SwordId)){
                item.SetMetadata(SwordRules.IdKey,e.SwordId);item.SetMetadata(SwordRules.EnergyKey,e.Energy);if(e.Kind==0||e.Kind==2){int old=SwordRules.Deployed(item);if(old==0&&e.EntityId>0&&e.Kind==0)SwordInventory.Reserve(p,e.Slot,item);item.SetMetadata(SwordRules.DeployedKey,e.Kind==2?0:e.EntityId);if(old>0&&SwordRules.Deployed(item)==0)SwordInventory.RestoreLock(p,e.Slot,item);}Notify(p,e.Slot);
            }}
            if(e.Kind==2&&e.Value>0){if(p is EntityPlayerLocal local)local.inventory.ReleaseAll(local.playerInput);SwordSafety.Grant(p);if(p.AttachedToEntity is EntityJuque){try{SwordSafety.Detaching=true;p.Detach();}finally{SwordSafety.Detaching=false;}}}
            if(w.GetPrimaryPlayer()!=null)SwordFX.Receive(w,e);
        }
        static void Tick()
        {
            var w=GameManager.Instance!=null?GameManager.Instance.World:null;
            if(w!=world){world=w;sessions.Clear();received.Clear();SwordCombat.Clear();SwordFX.Clear();nextSweep=0;}
            if(w==null)return;SwordControls.Tick(w);SwordFX.Tick(w);if(!Server)return;
            SwordCombat.Tick(w,Time.deltaTime);
            if(Time.time<nextSweep)return;nextSweep=Time.time+.25f;
            foreach(var e in w.Entities.list.ToArray()){
                var p=e as EntityPlayer;if(p==null)continue;var s=State(p);
                if(p.IsDead()){s.ChargeAt=-1;continue;}
                if(s.ChargeAt>=0&&(p.inventory.holdingItemIdx!=s.ChargeSlot||SwordRules.Id(p.inventory.holdingItemItemValue)!=s.ChargeId||SwordRules.Deployed(p.inventory.holdingItemItemValue)>0))s.ChargeAt=-1;
                for(int slot=0;slot<p.inventory.SlotCount;slot++){
                    var iv=p.inventory.GetItem(slot).itemValue;if(!SwordRules.IsSword(iv))continue;SwordRules.EnsureId(iv);float energy=SwordRules.Energy(iv);int deployed=SwordRules.Deployed(iv);
                    if(deployed>0){var v=w.GetEntity(deployed) as EntityJuque;if(v!=null&&Owned(p,v)){v.OwnerActor=p.entityId;bool riding=v.GetAttached(0)!=null;if(riding)energy=Mathf.Max(0,energy-.25f*(v.Boost?2.5f:1));else if(p.onGround&&Time.time-s.LastAttack>=10)energy=Mathf.Min(SwordRules.Capacity[SwordRules.Tier(iv)],energy+.5f);v.Energy=energy;}
                        else if(Time.time-s.Seen>8){iv.SetMetadata(SwordRules.DeployedKey,0);SwordInventory.RestoreLock(p,slot,iv);deployed=0;}
                    }else if(p.onGround&&Time.time-s.LastAttack>=10&&s.ChargeAt<0)energy=Mathf.Min(SwordRules.Capacity[SwordRules.Tier(iv)],energy+.5f);
                    iv.SetMetadata(SwordRules.EnergyKey,energy);
                    if(Time.time>=s.NextSync){Notify(p,slot);Emit(0,p,slot,deployed,iv,p.position,Vector3.zero,0);}
                }
                if(p.onGround&&Time.time-s.LastAttack>=10&&s.ChargeAt<0)for(int slot=0;slot<p.bag.ItemGrid.Length;slot++){
                    var iv=p.bag.ItemGrid.GetItem(slot).itemValue;if(!SwordRules.IsSword(iv)||SwordRules.Deployed(iv)>0)continue;SwordRules.EnsureId(iv);iv.SetMetadata(SwordRules.EnergyKey,Mathf.Min(SwordRules.Capacity[SwordRules.Tier(iv)],SwordRules.Energy(iv)+.5f));
                    if(Time.time>=s.NextSync){iv.NotifyChanged();Emit(6,p,-slot-1,0,iv,p.position,Vector3.zero,0);}
                }
                if(Time.time>=s.NextSync)s.NextSync=Time.time+1;
            }
            // An orphaned scene representation never becomes a second collectible sword.
            foreach(var e in w.Entities.list.ToArray())if(e is EntityJuque v){var p=w.GetEntity(v.OwnerActor) as EntityPlayer;if(p!=null&&!Owned(p,v)){if(v.GetAttached(0)!=null)v.GetAttached(0).Detach();w.RemoveEntity(v.entityId,EnumRemoveEntityReason.Despawned);}}
        }
    }
}
