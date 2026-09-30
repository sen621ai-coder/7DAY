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
            this.directory=directory;config=content.Load(directory);
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
                Message(Pole?"已抛投。浮漂有口时点击左键提竿；中鱼后后拉鼠标遛鱼。":"已抛投。观察浮漂，咬实后左键配合向后移动鼠标抬竿。");
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
            session.Advance(Time.deltaTime,frame,Environment(Time.deltaTime));lease.Request=session.Movement;
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
            if(Pole&&e.Kind==FishingEventKind.Nibble)Message("鱼有口，现在点击左键提竿。");
            if(Pole&&e.Kind==FishingEventKind.Hooked)Message("中鱼了！后拉鼠标抬竿、左右侧压，开始遛鱼。");
            if(e.Kind!=FishingEventKind.Landed||session==null||e.SessionId!=session.Current.SessionId)return;
            if(!settlement.RecordLanding(session.Current,config.Fish.Id))throw new InvalidOperationException("Landing rejected by settlement guard");
            Message("鱼已上岸，正在收取鱼获。");retryAt=0;
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
                case FailureReason.EarlyStrike:return "提竿过早或力度不足。";
                case FailureReason.LateStrike:return "错过了提竿时机。";
                case FailureReason.InvalidInput:return "提竿动作未达到要求，或输入状态异常。咬实时点击左键，并向后拉鼠标。";
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
            string heading="台钓 · 准备抛投",stats="瞄准 6.5 米内足够深的水面，左键抛投。\n每次消耗 1 份蚯蚓。";
            string help="中鱼：左键配合鼠标后拉\n右键收线 · - / = 调泄力\n左 Alt 观察 · 左 Ctrl 挪鼠标 · Esc 取消";
            if(Pole)help="浮漂有口时左键提竿 · 中鱼后后拉鼠标遛鱼\n鼠标左右侧压 · WASD 走位 · 前推鼠标放低竿\n左 Alt 观察 · 左 Ctrl 挪鼠标 · Esc 取消";
            if(session!=null) {
                var s=session.Current;
                switch(s.Phase) {
                    case FishingPhase.Casting:heading="钓鱼 · 正在抛投";break;
                    case FishingPhase.Settling:heading="钓鱼 · 浮漂落水";break;
                    case FishingPhase.Nibbling:heading=Pole?"鱼有口！点击左键提竿":"钓鱼 · 鱼在试饵，先别提竿";break;
                    case FishingPhase.BiteWindow:heading=Pole?"鱼已咬实！点击左键提竿":"鱼已咬实！左键 + 鼠标后拉";break;
                    case FishingPhase.Hooked:heading="中鱼！开始遛鱼";break;
                    case FishingPhase.Fighting:heading="钓鱼 · 遛鱼";break;
                    case FishingPhase.Landing:heading="钓鱼 · 收到近岸即可上鱼";break;
                    default:heading="钓鱼 · 等待咬钩";break;
                }
                stats="拉力 "+s.LineTensionNewtons.ToString("F0")+" N    泄力 "+(s.Drag01*100).ToString("F0")+"%\n线长 "+s.LineLengthMeters.ToString("F1")+" m    鱼剩余体力 "+(s.FishStamina01*100).ToString("F0")+"%";
                if(Pole)stats="拉力 "+s.LineTensionNewtons.ToString("F0")+" N    固定线长 "+s.LineLengthMeters.ToString("F1")+" m\n鱼剩余体力 "+(s.FishStamina01*100).ToString("F0")+"% · 疲劳后抬竿引到近岸";
                if(s.Phase==FishingPhase.BiteWindow)stats="提竿剩余 "+System.Math.Max(0,config.Hook.BiteWindowSeconds-(s.TimeSeconds-biteStartedAt)).ToString("F1")+" 秒\n点击左键并向后移动鼠标；竿举得太高时先稍放低。";
                if(Pole&&s.Phase==FishingPhase.BiteWindow)stats="提竿剩余 "+Math.Max(0,config.Hook.BiteWindowSeconds-(s.TimeSeconds-biteStartedAt)).ToString("F1")+" 秒\n点击左键提竿刺鱼，中鱼后再后拉鼠标遛鱼。";
                if(Pole&&s.Phase==FishingPhase.Nibbling)stats="鱼正在吃饵，现在点击左键即可提竿。\n中鱼后后拉鼠标抬竿，左右侧压遛鱼。";
            }
            FishingHud.Draw(heading,stats,help,Time.realtimeSinceStartup<messageUntil?message:"",Pole&&session!=null?(FloatReadoutState?)floatReadout.Current:null);
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
