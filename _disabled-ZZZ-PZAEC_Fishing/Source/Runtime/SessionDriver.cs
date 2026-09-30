using System;
using System.Collections.Generic;
using PZAEC.Fishing.Contracts;

namespace PZAEC.Fishing.Runtime
{
    public sealed class SeededRandom : IRandomSource
    {
        uint state;
        public SeededRandom(uint seed) { state = seed == 0 ? 0x9e3779b9u : seed; }
        public uint NextUInt() { uint x=state; x^=x<<13; x^=x>>17; x^=x<<5; return state=x; }
        public float Next01() => (NextUInt() >> 8) * (1f / 16777216f);
    }

    public sealed class InputAccumulator
    {
        RawInputFrame pending;
        long lastSequence = -1;
        public void Clear() { pending=default(RawInputFrame); lastSequence=-1; }
        public bool Push(RawInputFrame value)
        {
            if (value.Sequence<=lastSequence || !Scalar.IsFinite(value.MouseRightDelta) || !Scalar.IsFinite(value.MouseBackDelta)
                || !Scalar.IsFinite(value.MoveRight) || !Scalar.IsFinite(value.MoveForward) || !Scalar.IsFinite(value.DragAdjustDelta)
                || !Scalar.IsFinite(value.DurationSeconds) || value.DurationSeconds<0 || double.IsNaN(value.SampleTimeSeconds) || double.IsInfinity(value.SampleTimeSeconds)) return false;
            if (!Scalar.IsFinite(pending.MouseRightDelta+value.MouseRightDelta) || !Scalar.IsFinite(pending.MouseBackDelta+value.MouseBackDelta) || !Scalar.IsFinite(pending.DragAdjustDelta+value.DragAdjustDelta)) return false;
            var old=pending;
            pending=value; lastSequence=value.Sequence;
            pending.MouseRightDelta+=old.MouseRightDelta; pending.MouseBackDelta+=old.MouseBackDelta;
            pending.DragAdjustDelta+=old.DragAdjustDelta;
            pending.CastPressed|=old.CastPressed; pending.StrikePressed|=old.StrikePressed; pending.CancelPressed|=old.CancelPressed;
            return true;
        }
        public RawInputFrame Consume(int remainingTicks,float dt)
        {
            if(remainingTicks<1)throw new ArgumentOutOfRangeException(nameof(remainingTicks));
            var value=pending;
            value.DurationSeconds=dt;
            value.MouseRightDelta/=remainingTicks; value.MouseBackDelta/=remainingTicks; value.DragAdjustDelta/=remainingTicks;
            pending.MouseRightDelta-=value.MouseRightDelta; pending.MouseBackDelta-=value.MouseBackDelta; pending.DragAdjustDelta-=value.DragAdjustDelta;
            pending.CastPressed=pending.StrikePressed=pending.CancelPressed=false;
            return value;
        }
    }

    // Pure orchestration. Game adapters schedule one Advance per controller update.
    // It NEVER moves a native player, creates objects, or issues inventory rewards.
    public sealed class SessionDriver : IEventSink, IDisposable
    {
        readonly IFishingSimulation simulation;
        readonly IFishingControls controls;
        readonly IFishingPresentation presentation;
        readonly InputAccumulator input=new InputAccumulator();
        readonly Queue<FishingEvent> events=new Queue<FishingEvent>();
        Guid sessionId;
        long lastEventSequence;
        float accumulator;
        int maxTicks;
        EnvironmentFrame lastSimulatedEnvironment;
        FishingSnapshot previous,current;
        public bool Active {get;private set;}
        public MovementRequest Movement {get;private set;} = MovementRequest.None;
        public FishingSnapshot Current => current;
        public event Action<FishingEvent> Event;
        public SessionDriver(IFishingSimulation simulation,IFishingControls controls,IFishingPresentation presentation)
        {this.simulation=simulation??throw new ArgumentNullException(nameof(simulation));this.controls=controls??throw new ArgumentNullException(nameof(controls));this.presentation=presentation??throw new ArgumentNullException(nameof(presentation));}
        public void Begin(SessionStart start,FishingConfig config,EnvironmentFrame environment,IWaterQuery water)
        {
            if(Active)throw new InvalidOperationException("Session already active");
            if(start.SessionId==Guid.Empty || config==null || config.Session==null || config.Controls==null || config.Rod==null || config.Version!=FishingContract.Version || water==null || !environment.CanFish)throw new ArgumentException("Invalid session start");
            input.Clear(); events.Clear(); lastEventSequence=0; accumulator=0; sessionId=start.SessionId;
            maxTicks=Math.Max(1,Math.Min(16,config.Session.MaxCatchUpTicks));
            lastSimulatedEnvironment=environment;
            try
            {
                controls.Reset(config.Controls,config.Rod);
                simulation.Begin(start,config,environment,new SeededRandom(start.Seed),water,this);
                current=previous=simulation.Current;
                presentation.Begin(start,config); Active=true; FlushEvents();
                if(current.IsTerminal) Release();
            }
            catch { Release(); throw; }
        }
        public int Advance(float dt,RawInputFrame frame,EnvironmentFrame environment)
        {
            if(!Active)return 0;
            if(!environment.CanFish){Cancel(environment.UnavailableReason);return 0;}
            if(!frame.InputAllowed){Cancel(FailureReason.MenuOpened);return 0;}
            if(!Scalar.IsFinite(dt)||dt<0||!input.Push(frame)){Cancel(FailureReason.InvalidInput);return 0;}
            if(frame.CancelPressed){Cancel(FailureReason.CancelledByPlayer);return 0;}
            // Drop excessive wall time rather than executing an unbounded backlog.
            accumulator=Math.Min(accumulator+dt,FishingContract.FixedStepSeconds*maxTicks);
            int steps=Math.Min(maxTicks,(int)((accumulator+0.0000001f)/FishingContract.FixedStepSeconds));
            int executed=0;
            try
            {
                var fromEnvironment=lastSimulatedEnvironment;
                var sampledEnvironment=environment;
                for(int i=0;i<steps && Active;i++)
                {
                    previous=current;
                    environment=InterpolateEnvironment(fromEnvironment,sampledEnvironment,(i+1f)/steps);
                    environment.Tick=current.Tick+1;
                    environment.TimeSeconds=current.TimeSeconds+FishingContract.FixedStepSeconds;
                    var intent=controls.Step(FishingContract.FixedStepSeconds,input.Consume(steps-i,FishingContract.FixedStepSeconds),current,environment);
                    if(intent.Cancel){Cancel(FailureReason.CancelledByPlayer);break;}
                    // B owns angular limits and the physical tip; A supplies the observed root/basis.
                    current=simulation.Step(FishingContract.FixedStepSeconds,intent,environment,this);
                    executed++;
                    lastSimulatedEnvironment=environment;
                    Movement=intent.Movement;
                    accumulator=Math.Max(0,accumulator-FishingContract.FixedStepSeconds);
                    FlushEvents();
                    if(current.IsTerminal) Release();
                }
                return executed;
            }
            catch { Cancel(FailureReason.ModuleError); throw; }
        }
        static EnvironmentFrame InterpolateEnvironment(EnvironmentFrame a,EnvironmentFrame b,float t)
        {
            var result=b;result.PlayerPosition=Vec3.Lerp(a.PlayerPosition,b.PlayerPosition,t);
            result.PlayerVelocity=Vec3.Lerp(a.PlayerVelocity,b.PlayerVelocity,t);
            result.Rod.Root=Vec3.Lerp(a.Rod.Root,b.Rod.Root,t);
            result.ViewForward=Vec3.Lerp(a.ViewForward,b.ViewForward,t).Normalized;
            result.ViewRight=Vec3.Lerp(a.ViewRight,b.ViewRight,t).Normalized;
            result.Rod.Forward=Vec3.Lerp(a.Rod.Forward,b.Rod.Forward,t).Normalized;
            result.Rod.Right=Vec3.Lerp(a.Rod.Right,b.Rod.Right,t).Normalized;
            return result;
        }
        public static RodPose BuildStraightRod(RodPose reference,ControlIntent intent,float length)
        {
            reference.PitchRadians=intent.RodPitchRadians;reference.YawRadians=intent.RodYawRadians;
            var forward=reference.Forward.Normalized;var right=reference.Right.Normalized;
            var horizontal=forward*(float)Math.Cos(intent.RodYawRadians)+right*(float)Math.Sin(intent.RodYawRadians);
            var direction=horizontal*(float)Math.Cos(intent.RodPitchRadians)+Vec3.Up*(float)Math.Sin(intent.RodPitchRadians);
            reference.Tip=reference.Root+direction.Normalized*length;
            return reference;
        }
        public void Render(Vec3 renderOrigin,bool dedicated=false,bool debug=false)
        {
            if(!Active || dedicated)return;
            try {presentation.Render(new RenderFrame {Previous=previous,Current=current,Alpha=accumulator/FishingContract.FixedStepSeconds,RenderOrigin=renderOrigin,IsLocalPlayer=true,ShowDebug=debug});}
            catch {Cancel(FailureReason.ModuleError);throw;}
        }
        public void Emit(FishingEvent value)
        {
            if(value.SessionId!=sessionId || value.Sequence<=lastEventSequence)return;
            if(events.Count>=256)throw new InvalidOperationException("Event overflow");
            lastEventSequence=value.Sequence;events.Enqueue(value);
        }
        void FlushEvents(){while(events.Count>0){var value=events.Dequeue();presentation.OnEvent(value);Event?.Invoke(value);}}
        public void Cancel(FailureReason reason)
        {
            if(!Active){Release();return;}
            try{current=simulation.Cancel(reason,this);FlushEvents();}
            finally{Release();}
        }
        void Release()
        {
            Active=false;Movement=MovementRequest.None;input.Clear();events.Clear();accumulator=0;
            try{controls.Release();}finally{presentation.Clear();}
        }
        public void Dispose(){try{Cancel(FailureReason.WorldClosed);}finally{presentation.Dispose();}}
    }
}
