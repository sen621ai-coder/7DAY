using System;
using PZAEC.Fishing.Contracts;
using PZAEC.Fishing.Content;
using PZAEC.Fishing.Controls;
using PZAEC.Fishing.Presentation;
using PZAEC.Fishing.Simulation;
using UnityEngine;
using FishingEvent = PZAEC.Fishing.Contracts.FishingEvent;
using FishingEventKind = PZAEC.Fishing.Contracts.FishingEventKind;
using FishingPhase = PZAEC.Fishing.Contracts.FishingPhase;
using FailureReason = PZAEC.Fishing.Contracts.FailureReason;

namespace PZAEC.Fishing.Runtime
{
    // Local authoritative host only. No client prediction or remote inventory mutation.
    public sealed class LocalFishingRuntime : IDisposable
    {
        readonly string directory;
        readonly FishingContent content=new FishingContent();
        readonly FishingConfig config;
        readonly FishConfig baseFish;
        readonly NativeBindings bindings;
        readonly NativeControlLease lease=new NativeControlLease();
        readonly NativeInputHooks hooks;
        readonly NativeHeldRod heldRod;
        readonly NativeArmPose arms=new NativeArmPose();
        readonly FloatReadout floatReadout=new FloatReadout();
        bool Pole => config.Line.FixedLengthMeters>0;
        public void RestoreArms()=>arms.Restore();
        World world;
        EntityPlayerLocal player;
        NativeWaterQuery water;
        CatchSettlement settlement;
        SessionDriver session;
        FishingPresentation presentation;
        Vec3 forward,right,lastPosition;
        int health,rodSlot,startFrame;
        float retryAt,messageUntil;
        string message="";
        bool faulted;
        FishingPhase loggedPhase;
        float diagnosticAt;
        Guid diagnosticSession;
        double biteStartedAt;
        public LocalFishingRuntime(string directory)
        {
            this.directory=directory;config=content.Load(directory);baseFish=FishCatalog.Profile(config.Fish,FishingContract.FishDefinition);
            heldRod=new NativeHeldRod {ModDirectory=directory};
            string error;if(!content.Validate(config,out error))throw new InvalidOperationException(error);
            bindings=new NativeBindings(config.Controls);
            hooks=new NativeInputHooks(lease){Preparing=Prepare,Sampled=Sample,LostInput=Cancel};
        }
        bool Equipped => player!=null&&player.inventory?.holdingItem?.GetItemName()==FishingContract.RodItem;
        public void Tick()
        {
            var next=GameManager.Instance?.World;
            var local=next!=null&&!next.IsRemote()&&!GameManager.IsDedicatedServer?next.GetPrimaryPlayer():null;
            if(world!=next||player!=local) {
                ClearSession(FailureReason.WorldClosed);heldRod.Dispose();world=next;player=local;water=null;settlement=null;faulted=false;
                if(player!=null) {
                    string id=player.PersistentPlayerData?.PrimaryId?.CombinedString;
                    if(string.IsNullOrEmpty(id)) {player=null;return;}
                    water=new NativeWaterQuery(world);
                    settlement=new CatchSettlement(content,new NativeCatchRecord(player,id),new NativeCatchInventory(player),id);
                }
            }
            if(player==null||faulted)return;
            if(!Equipped)heldRod.Dispose();
            if(session!=null) {
                var reason=Eligibility();if(reason!=FailureReason.None)Cancel(reason);
                else if(!lease.CanReadInput())Cancel(FailureReason.MenuOpened);
            }
            if(!player.IsDead()&&Time.realtimeSinceStartup>=retryAt&&settlement.HasPending) {
                retryAt=Time.realtimeSinceStartup+1;
                var result=settlement.TrySettle(settlement.Pending);
                if(result==SettlementStatus.Granted)Message("鱼获已放入背包。");
                else if(result==SettlementStatus.InventoryFull)Message("背包已满：鱼获已保留，请腾出一个空位。");
                else if(result==SettlementStatus.Invalid) {faulted=true;Message("待领取鱼获与配置不匹配，请检查钓鱼配置。");}
            }
        }
        void Prepare(PlayerMoveController controller)
        {
            arms.Restore();
            if(player==null||controller.entityPlayerLocal!=player||faulted)return;
            if(session!=null) {
                var reason=Eligibility();if(reason!=FailureReason.None)Cancel(reason);
                return;
            }
            if(!Equipped||!bindings.CastPressed||player.playerCamera==null||Time.timeScale==0)return;
            heldRod.Update(player,config,false);
            if(!heldRod.HandScene.HasValue){Message("钓竿正在装备，请稍候再抛投。");return;}
            lease.Acquire(player);
            if(!lease.CanReadInput()){lease.Release();return;}
            if(settlement.HasPending){lease.Release();Message("请先腾出背包空位领取鱼获。");return;}
            try{Begin();}finally{if(session==null)lease.Release();}
        }
        void Begin()
        {
            var ray=player.GetLookRay(); // Native ray origin is already absolute; do not add Origin twice.
            Vec3 origin=NativeCoordinates.FromUnity(ray.origin),direction=NativeCoordinates.FromUnity(ray.direction).Normalized;
            Vec3 target;
            if(!CastTargeting.TryFind(water,origin,direction,NativeCoordinates.FromUnity(player.GetPosition()),config.Session.MaxCastMeters,config.Session.MinDepthMeters,out target)){Message("请瞄准近处足够深、没有遮挡的水面。");return;}
            var look=direction;look.Y=0;forward=look.Normalized;
            if(forward.LengthSquared<.5f){Message("请朝水面前方抛投。");return;}
            right=new Vec3(forward.Z,0,-forward.X);lastPosition=NativeCoordinates.FromUnity(player.GetPosition());
            health=player.Health;rodSlot=player.inventory.holdingItemIdx;
            presentation=new FishingPresentation(directory,options:new PresentationOptions {RightHandScene=()=>heldRod.HandScene},diagnosticLog:s=>Log.Out("[PZAEC.Fishing] "+s));
            session=new SessionDriver(new ContractFishingSimulation(),new FishingControlsAdapter(),presentation);
            var id=Guid.NewGuid();
            config.Fish=FishCatalog.Select(baseFish,BitConverter.ToUInt32(id.ToByteArray(),0));
            try {
                session.Begin(new SessionStart {SessionId=id,PlayerPersistentId=player.PersistentPlayerData.PrimaryId.CombinedString,
                    PlayerEntityId=player.entityId,Seed=BitConverter.ToUInt32(id.ToByteArray(),0),CastTarget=target,
                    FishDefinitionId=config.Fish.Id,Authority=AuthorityMode.Standalone},config,Environment(0),water);
                if(!session.Active){ClearSession(FailureReason.InvalidWater);Message("这里无法抛投，请换个位置。");return;}
                if(!settlement.OpenSession(id)){ClearSession(FailureReason.CancelledByPlayer);Message("鱼获结算尚未准备好，暂时无法抛投。");return;}
                // All validation and module Begin succeeded before consuming one bait on the main thread.
                if(!NativeCatchInventory.ConsumeBait(player)){ClearSession(FailureReason.CancelledByPlayer);Message("需要鱼饵：蚯蚓，可用腐肉制作。");return;}
                session.Event+=OnEvent;startFrame=Time.frameCount;
                diagnosticSession=id;loggedPhase=session.Current.Phase;diagnosticAt=0;
                Log.Out("[PZAEC.Fishing] Session "+id.ToString("N")+" accepted target="+target+" depth="+water.SampleColumn(target,1,16).DepthMeters.ToString("F2")+"m bait=1");
                Message("已抛竿，观察漂相。");
            } catch {ClearSession(FailureReason.ModuleError);throw;}
        }
        FailureReason Eligibility()
        {
            if(player==null||world==null||world.IsRemote())return FailureReason.Disconnected;
            if(player.IsDead())return FailureReason.Dead;
            if(player.IsSwimming())return FailureReason.Swimming;
            if(player.AttachedToEntity!=null)return FailureReason.Vehicle;
            if(Time.timeScale==0)return FailureReason.MenuOpened;
            if(!Equipped||player.inventory.holdingItemIdx!=rodSlot)return FailureReason.ItemChanged;
            if(player.Health<health)return FailureReason.Damaged;
            health=player.Health;return FailureReason.None;
        }
        EnvironmentFrame Environment(float dt)
        {
            var position=NativeCoordinates.FromUnity(player.GetPosition());
            float pitch=session!=null?session.Current.Rod.PitchRadians:.35f;
            float yaw=session!=null?session.Current.Rod.YawRadians:0;
            var pose=new RodPose {Forward=forward,Right=right,PitchRadians=pitch,YawRadians=yaw};
            var root=heldRod.Root(pose,config.Rod.LengthMeters);
            var result=new EnvironmentFrame {CanFish=true,IsGrounded=player.onGround,PlayerEntityId=player.entityId,
                PlayerPosition=position,PlayerVelocity=dt>0?(position-lastPosition)/dt:Vec3.Zero,ViewForward=forward,ViewRight=right,
                Rod=new RodPose {Root=root,Forward=forward,Right=right,PitchRadians=.35f},
                HasPlayerStamina=true,PlayerStamina01=Mathf.Clamp01(player.Stamina/Math.Max(1,player.GetMaxStamina())),PlayerStaminaMaximum=Math.Max(1,player.GetMaxStamina()),
                Water=water.SampleColumn(session!=null?session.Current.FloatPosition:position,4,16)};
            lastPosition=position;return result;
        }
        void Sample(RawInputFrame frame)
        {
            if(session==null)return;
            frame=bindings.Apply(frame);
            if(Pole){frame.ReelHeld=false;frame.DragAdjustDelta=0;}
            if(startFrame==Time.frameCount) {frame.CastPressed=false;frame.StrikePressed=false;frame.MouseBackDelta=frame.MouseRightDelta=0;}
            lease.FreeLook=frame.FreeLookHeld;
            if(frame.StrikePressed)Log.Out("[PZAEC.Fishing] Strike session="+diagnosticSession.ToString("N")+" phase="+session.Current.Phase+" pitch="+session.Current.Rod.PitchRadians.ToString("F3")+" mouseBack="+frame.MouseBackDelta.ToString("F3"));
            float previousYaw=session.Current.Rod.YawRadians;
            session.Advance(Time.deltaTime,frame,Environment(Time.deltaTime));lease.Request=session.Movement;
            NativeFishingEffort.Spend(player,session.FrameStaminaCost);
            lease.RodYawDeltaRadians=frame.FreeLookHeld||frame.RecenterHeld?0:session.Current.Rod.YawRadians-previousYaw;
            floatReadout.Observe(session.Current,config.Float);
            Diagnostic();
            if(!session.Active) {
                var failure=session.Current.Failure;
                if(failure!=FailureReason.None)Message("钓鱼结束："+Reason(failure));
                ClearSession(failure);
            }
        }
        void OnEvent(FishingEvent e)
        {
            Log.Out("[PZAEC.Fishing] Event session="+e.SessionId.ToString("N")+" tick="+e.Tick+" kind="+e.Kind+" reason="+e.Reason);
            if(Pole&&e.Kind==FishingEventKind.Hooked)Message("中鱼了！后拉鼠标抬竿、左右侧压，开始遛鱼。");
            if(e.Kind!=FishingEventKind.Landed||session==null||e.SessionId!=session.Current.SessionId)return;
            if(!settlement.RecordLanding(session.Current,config.Fish.Id))throw new InvalidOperationException("Landing rejected by settlement guard");
            Message(FishCatalog.Name(config.Fish.Id)+"已上岸（"+session.Current.FishMassKg.ToString("F2")+" kg），获得 "+FishCatalog.MeatCount(session.Current.FishMassKg)+" 份鱼肉。");retryAt=0;
        }
        public void Render()
        {
            if(player==null||faulted)return;
            if(session!=null&&Pole) {
                var reference=player.playerCamera!=null?player.playerCamera.transform.forward:NativeCoordinates.ToUnity(forward);
                arms.Apply(player,reference,NativeCoordinates.ToUnity(PresentationMath.RodAim(session.Current.Rod)));
            } else arms.Restore();
            if(Equipped)heldRod.Update(player,config,session!=null);else heldRod.Dispose();
            if(session==null)return;
            session.Render(NativeCoordinates.FromUnity(Origin.position));
            if(presentation.LastError!=null)throw new InvalidOperationException(presentation.LastError);
        }
        public void Fail(Exception error)
        {faulted=true;ClearSession(FailureReason.ModuleError);Message("钓鱼发生错误，已释放操作；详情见游戏日志。");Log.Error("[PZAEC.Fishing] "+error);}
        void Cancel(FailureReason reason){if(session!=null)Message("钓鱼结束："+Reason(reason));ClearSession(reason);}
        void ClearSession(FailureReason reason)
        {
            arms.Dispose();
            floatReadout.Reset();
            var old=session;session=null;presentation=null;
            if(old!=null)Log.Out("[PZAEC.Fishing] End session="+old.Current.SessionId.ToString("N")+" phase="+old.Current.Phase+" failure="+old.Current.Failure+" cleanup="+reason+" tick="+old.Current.Tick);
            try{if(old!=null){old.Event-=OnEvent;try{old.Cancel(reason);}finally{old.Dispose();}}}
            finally {
                lease.Release();
            }
        }
        void Message(string value){message=value;messageUntil=Time.realtimeSinceStartup+6;}
        static string Reason(FailureReason reason)
        {
            switch(reason) {
                case FailureReason.Overload:return "鱼线承受不住拉力，断线了。";
                case FailureReason.SlackLine:return "松线太久，鱼脱钩了。";
                case FailureReason.EarlyStrike:return "空竿，请重新抛竿。";
                case FailureReason.LateStrike:return "错过了提竿时机。";
                case FailureReason.InvalidInput:return "未能完成提竿，请重新抛竿。";
                case FailureReason.ItemChanged:return "已切换装备。";
                case FailureReason.Damaged:return "受到伤害。";
                case FailureReason.MenuOpened:return "打开菜单或失去焦点。";
                case FailureReason.CancelledByPlayer:return "已取消。";
                default:return reason.ToString();
            }
        }
        public void Draw()
        {
            if(player==null||(!Equipped&&Time.realtimeSinceStartup>messageUntil))return;
            string heading="台钓 · 左键抛竿",stats="瞄准近处水面 · 需要蚯蚓";
            string help="Alt 观察 · Ctrl 挪鼠标 · Esc 取消";
            if(session!=null) {
                var s=session.Current;
                switch(s.Phase) {
                    case FishingPhase.Casting:heading="正在抛竿";stats="等浮漂落水";break;
                    case FishingPhase.Settling:heading="浮漂落水";stats="等漂站稳";break;
                    case FishingPhase.Waiting:
                    case FishingPhase.Nibbling:
                    case FishingPhase.BiteWindow:heading="台钓 · 看漂";stats="观察漂相 · 自行判断提竿";break;
                    case FishingPhase.Hooked:
                    case FishingPhase.Fighting:heading="中鱼 · 遛鱼";stats="后拉顶住 · 前推休息";help="左右侧压 · Ctrl 挪鼠标 · Esc 取消";break;
                    case FishingPhase.Landing:heading="鱼已疲劳";stats="抬竿引到近岸";break;
                    default:heading="等待吃饵";stats="看漂等口";break;
                }
            }
            FishingHud.Draw(heading,stats,help,session==null&&Time.realtimeSinceStartup<messageUntil?message:"",Pole&&session!=null?(FloatReadoutState?)floatReadout.Current:null,
                session!=null&&(session.Current.Phase==FishingPhase.Hooked||session.Current.Phase==FishingPhase.Fighting||session.Current.Phase==FishingPhase.Landing)?(FightReadout?)new FightReadout {Tension=session.Current.LineTensionNewtons,BreakForce=config.Line.BreakForceNewtons,Stamina=session.Current.FishStamina01,LandStamina=config.Fish.LandingStamina01,
                    PlayerEffort=session.PlayerEffort01,PlayerStamina=Mathf.Clamp01(player.Stamina/Math.Max(1,player.GetMaxStamina())),ShowEffort=config.Controls.EffortEnabled}:null);
            if(session!=null&&player.playerCamera!=null&&(session.Current.Phase==FishingPhase.Hooked||session.Current.Phase==FishingPhase.Fighting||session.Current.Phase==FishingPhase.Landing))
                FishingHud.DrawFishBearing(player.playerCamera.WorldToViewportPoint(NativeCoordinates.ToScene(session.Current.FishPosition)));
        }
        void Diagnostic()
        {
            var s=session.Current;
            if(s.Phase==loggedPhase&&Time.realtimeSinceStartup<diagnosticAt)return;
            if(s.Phase==FishingPhase.BiteWindow&&loggedPhase!=s.Phase)biteStartedAt=s.TimeSeconds;
            loggedPhase=s.Phase;diagnosticAt=Time.realtimeSinceStartup+5;
            Log.Out("[PZAEC.Fishing] State session="+s.SessionId.ToString("N")+" phase="+s.Phase+" failure="+s.Failure+" tension="+s.LineTensionNewtons.ToString("F1")+"N stamina="+s.FishStamina01.ToString("F2")+" line="+s.LineLengthMeters.ToString("F2")+"m hook="+s.HookQuality01.ToString("F2"));
        }
        public void Dispose(){try{ClearSession(FailureReason.WorldClosed);}finally{heldRod.Dispose();hooks.Dispose();}}
    }
    [DefaultExecutionOrder(10000)]
    public sealed class FishingRuntimeView : MonoBehaviour
    {
        public LocalFishingRuntime Runtime;
        void Update(){Runtime?.RestoreArms();}
        void LateUpdate(){try{Runtime?.Render();}catch(Exception error){Runtime?.Fail(error);}}
        void OnGUI(){Runtime?.Draw();}
    }
}
