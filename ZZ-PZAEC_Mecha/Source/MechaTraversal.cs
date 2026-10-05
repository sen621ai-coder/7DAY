using System;
using System.Collections.Generic;
using UnityEngine;

namespace PZAEC.Mecha
{
    public static class Traversal
    {
        public enum Stage : byte { Idle, Prepare, FrontStep, Transfer, RearStep, Settle, Exit }
        public enum Kind : byte { Up, Down, Gap }
        public sealed class Plan
        {
            public int Id,Actor,Front;public Kind Type;public Quaternion Rotation;
            public Vector3 Root,End;public Vector3[] Start=new Vector3[2],StartNormal={Vector3.up,Vector3.up};
            public GroundSupport.Pad[] Land=new GroundSupport.Pad[2];
            public float[] Lift={Rules.TraverseToeClearance,Rules.TraverseToeClearance};
            public float Duration,Height,Width;public bool Major=true,Adaptive;public GroundSupport.Profile Shape;
            public Stage Phase(float age)
            {float t=age/Duration;return t<.12f?Stage.Prepare:t<.37f?Stage.FrontStep:t<.60f?Stage.Transfer:t<.85f?Stage.RearStep:t<1?Stage.Settle:Stage.Exit;}
            static float Ease(float t){t=Mathf.Clamp01(t);return t*t*(3-2*t);}
            Vector3 WeightShift(float t)
            {
                float side=Front==0?1:-1;
                float lateral=t<.37f?side*.16f*Ease(t/.12f):t<.60f?Mathf.Lerp(side*.16f,-side*.16f,Ease((t-.37f)/.23f)):-side*.16f*(1-Ease((t-.60f)/.25f));
                return Rotation*Vector3.right*lateral-Vector3.up*(.02f*Mathf.Min(Ease(t/.12f),1-Ease((t-.85f)/.15f)));
            }
            public float PoleAngle(float age,int side)
            {
                if(side==Front)return 0;
                float t=age/Duration;
                bool early=Type!=Kind.Up;
                float blend=Ease((t-(early?.12f:.22f))/(early?.12f:.10f))*(1-Ease((t-.72f)/.13f));
                return (side==0?1:-1)*(early?75:65)*blend;
            }
            public void Frame(float age,out Vector3 root,out Vector3 left,out Vector3 right)
            {
                float t=Mathf.Clamp01(age/Duration),front=Ease((t-.12f)/.25f),follow=Ease((t-.37f)/.48f);
                float pre=Type==Kind.Down?.95f:Type==Kind.Gap?.65f:.20f;
                root=Vector3.Lerp(Root,End,pre*front+(1-pre)*follow);
                root.y=Type==Kind.Down?Mathf.Lerp(Root.y,End.y,front):Mathf.Lerp(Root.y,End.y,follow);
                if(Type==Kind.Gap)root.y-=Mathf.Lerp(.22f,.55f,Mathf.Clamp01((Width-.40f)/.35f))*front*(1-follow);
                root+=WeightShift(t);
                var feet=new Vector3[2]; // Render/network callers only; fixed simulation uses FrameInto below.
                FrameFeet(t,feet);FitRoot(age,feet,ref root);left=feet[0];right=feet[1];
            }
            public void FrameInto(float age,Vector3[] feet,out Vector3 root)
            {
                float t=Mathf.Clamp01(age/Duration),front=Ease((t-.12f)/.25f),follow=Ease((t-.37f)/.48f),pre=Type==Kind.Down?.95f:Type==Kind.Gap?.65f:.20f;
                root=Vector3.Lerp(Root,End,pre*front+(1-pre)*follow);
                root.y=Type==Kind.Down?Mathf.Lerp(Root.y,End.y,front):Mathf.Lerp(Root.y,End.y,follow);
                if(Type==Kind.Gap)root.y-=Mathf.Lerp(.22f,.55f,Mathf.Clamp01((Width-.40f)/.35f))*front*(1-follow);
                root+=WeightShift(t);
                FrameFeet(t,feet);
                FitRoot(age,feet,ref root);
            }
            void FitRoot(float age,Vector3[] feet,ref Vector3 root){if(!Adaptive||Shape==null)return;float height;FootPlanner.Pelvis(Shape,Rotation,root,feet[0],feet[1],FootNormal(age,0),FootNormal(age,1),root.y,out height);root.y=height;}
            public Vector3 FootNormal(float age,int side)
            {float t=age/Duration;float blend=Mathf.SmoothStep(0,1,Mathf.Clamp01((t-(side==Front?.12f:.37f))/(side==Front?.25f:.48f)));return Vector3.Slerp(StartNormal[side],Land[side].Normal,blend).normalized;}
            void FrameFeet(float t,Vector3[] feet)
            {
                feet[Front]=Swing(Start[Front],Land[Front].Point,Mathf.Clamp01((t-.12f)/.25f),Type==Kind.Gap?Mathf.Max(.15f,Lift[Front]):Lift[Front]);
                feet[1-Front]=Swing(Start[1-Front],Land[1-Front].Point,Mathf.Clamp01((t-.37f)/.48f),Lift[1-Front]);
            }
        }
        public sealed class State
        {
            public EntityVehicle Vehicle;public Plan Current,Candidate;public Stage Phase;
            public float EquipmentBlend;
            public float Age,SearchAt=-100,FeedbackUntil,LastPacket=-100,LastSend=-100,PendingAt=-100;
            public int NextId,Actor=-1,ReceivedId,ReceivedTick,Tick;public bool Pressed,Pending,QueuedToggle,QueuedJump,Remote,Accepted,HadForward;
            public Stage DeliveredPhase;
            public string Reason="",Feedback="";public float BrakeDistance,SmallDistance;
            public Vector3[] FrameFeet=new Vector3[2];public Vector3 TargetRoot,TargetVelocity;
        }
        static readonly Dictionary<int,State> states=new Dictionary<int,State>();
        public static State Get(EntityVehicle v)
        {State s;if(!states.TryGetValue(v.entityId,out s)||s.Vehicle!=v){s=new State{Vehicle=v};states[v.entityId]=s;}return s;}
        public static bool Active(EntityVehicle v){State s;return v!=null&&states.TryGetValue(v.entityId,out s)&&s.Vehicle==v&&s.Current!=null;}
        public static bool Major(EntityVehicle v){State s;var ground=GroundSupport.Find(v);return ground!=null&&ground.Recovering||v!=null&&states.TryGetValue(v.entityId,out s)&&s.Current!=null&&s.Current.Major;}
        public static bool Charging(EntityVehicle v){return Samurai.Busy(v)||(Rules.Complete(v)&&Samurai.Get(v).LaserCharge>0)||Locomotion.Get(v).Charge>0;}
        public static void Press(EntityVehicle v){var s=Get(v);s.Pressed=true;}
        public static Vector3 Swing(Vector3 a,Vector3 b,float t,float extra)
        {
            t=Mathf.Clamp01(t);if(t<=0)return a;if(t>=1)return b;
            float rise=Mathf.Max(a.y,b.y)+extra;
            // Lift before crossing the lip, descend only after the entire sole clears it.
            float x=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.24f,.76f,t));
            var p=Vector3.Lerp(a,b,x);
            p.y=t<.24f?Mathf.Lerp(a.y,rise,Mathf.SmoothStep(0,1,t/.24f)):t>.76f?Mathf.Lerp(rise,b.y,Mathf.SmoothStep(0,1,(t-.76f)/.24f)):rise;
            return p;
        }
        static string pathFailure;
        public static string PathFailure {get{return pathFailure;}}
        public static bool FootPath(EntityVehicle v,GroundSupport.Profile shape,Vector3 root,Quaternion yaw,int side,Vector3 foot,Vector3 previous,float poleAngle=0,Vector3? surfaceNormal=null)
        {
            var normal=surfaceNormal??Vector3.up;var soleRotation=Quaternion.FromToRotation(Vector3.up,normal)*yaw;
            if(!shape.Reach(root,yaw,side,foot,normal)){pathFailure="reach root="+root+" foot="+foot;return false;}
            if(!GroundSupport.BoxClear(v,previous+normal*.15f,foot+normal*.15f,new Vector3(.20f,.125f,.29f),soleRotation)){pathFailure="foot volume root="+root+" foot="+foot+" hit="+GroundSupport.Blocked;return false;}
            // Reconstruct the same two-bone pole as render IK, then sweep both leg volumes.
            var hip=root+yaw*shape.Hip[side];var ankle=foot-soleRotation*shape.AnkleOffset[side];var d=ankle-hip;float length=d.magnitude;
            if(length<.001f)return false;var axis=d/length;var pole=Vector3.ProjectOnPlane(yaw*Vector3.forward,axis).normalized;
            if(pole.sqrMagnitude<.01f)pole=yaw*Vector3.up;
            pole=Quaternion.AngleAxis(poleAngle,axis)*pole;
            float along=(shape.Upper*shape.Upper-shape.Lower*shape.Lower+length*length)/(2*length);
            var knee=hip+axis*along+pole*Mathf.Sqrt(Mathf.Max(0,shape.Upper*shape.Upper-along*along));
            if(!LimbClear(v,hip,knee)||!LimbClear(v,knee,ankle)){pathFailure="leg volume hip="+hip+" knee="+knee+" ankle="+ankle+" hit="+GroundSupport.Blocked;return false;}return true;
        }
        static bool LimbClear(EntityVehicle v,Vector3 a,Vector3 b)
        {
            // Narrow boxes along each actual segment; no large hull around the empty space between legs.
            var delta=b-a;var rotation=Quaternion.FromToRotation(Vector3.forward,delta.normalized);
            return GroundSupport.BoxClear(v,(a+b)*.5f,(a+b)*.5f,new Vector3(.065f,.065f,delta.magnitude*.5f),rotation);
        }
        static bool EquipmentClear(EntityVehicle v,GroundSupport.Profile shape,Plan p,float age,Vector3 previous,Vector3 root)
        {
            if(!shape.Equipment)return true;float t=age/p.Duration;float blend=Mathf.Clamp01(t/.12f);
            // Folded shield/arm envelopes extend beyond the central torso hull.
            for(int side=0;side<2;side++){
                var center=p.Rotation*(side==0?new Vector3(-.72f,2,.50f):new Vector3(.85f,2.30f,.60f));
                var half=side==0?new Vector3(.40f,.70f,.50f):new Vector3(.32f,.45f,.45f);
                if(!GroundSupport.BoxClear(v,previous+center,root+center,half,p.Rotation))return false;
            }
            for(int side=0;side<2;side++){
                var fold=Quaternion.Euler(85*blend,(side==0?15:-15)*blend,(side==0?-12:12)*blend);var box=shape.WingBounds[side];
                var center=p.Rotation*(shape.WingRoot[side]+fold*box.center);
                if(!GroundSupport.BoxClear(v,previous+center,root+center,box.extents,p.Rotation*fold))return false;
            }
            // The raised blade extends above the torso: include it in headroom/path checks.
            if(t>=.12f&&t<=.85f){
                var grip=p.Rotation*new Vector3(.65f,2.11f,1.01f);var direction=p.Rotation*new Vector3(.05f,.90f,.40f).normalized;
                var center=grip+direction*1.20f;
                if(!GroundSupport.BoxClear(v,previous+center,root+center,new Vector3(.22f,.22f,1.25f),Quaternion.LookRotation(direction)))return false;
            }
            return true;
        }
        public static bool ValidatePath(EntityVehicle v,GroundSupport.State support,Plan p,out string reason)
        {
            p.Shape=support.Shape;reason="";var feet=new Vector3[2];var previous=(Vector3[])p.Start.Clone();var last=p.Root;
            // Also sample by time so curved toe trajectories never skip a thin obstruction.
            int count=Mathf.Max(100,Mathf.CeilToInt((Vector3.Distance(p.Root,p.End)+Mathf.Abs(p.Height)+2)/.05f));
            for(int i=0;i<=count;i++){
                Vector3 root;p.FrameInto(p.Duration*i/count,feet,out root);
                if(!GroundSupport.HullClear(v,support.Shape,last,root,p.Rotation)){reason="上方或机体路径受阻";return false;}
                if(!EquipmentClear(v,support.Shape,p,p.Duration*i/count,last,root)){reason="翼组或长剑路径受阻";return false;}
                for(int side=0;side<2;side++)if(!FootPath(v,support.Shape,root,p.Rotation,side,feet[side],previous[side],p.PoleAngle(p.Duration*i/count,side),p.FootNormal(p.Duration*i/count,side))){reason="腿部路径受阻或落点超出腿长 t="+(i/(float)count)+" side="+side+" "+pathFailure;return false;}
                last=root;previous[0]=feet[0];previous[1]=feet[1];
            }
            return true;
        }
        public static bool ValidateSegment(EntityVehicle v,GroundSupport.State support,Plan p,float from,float to,out string reason)
        {
            reason="";var s=Get(v);var old=new Vector3[2];Vector3 previous;p.FrameInto(from,old,out previous);
            int count=Mathf.Max(1,Mathf.CeilToInt((to-from)/.01f));
            for(int i=1;i<=count;i++){
                float age=Mathf.Lerp(from,to,i/(float)count);Vector3 root;p.FrameInto(age,s.FrameFeet,out root);
                if(!GroundSupport.HullClear(v,support.Shape,previous,root,p.Rotation)){reason="路径出现新障碍";return false;}
                if(!EquipmentClear(v,support.Shape,p,age,previous,root)){reason="装备路径出现新障碍";return false;}
                for(int side=0;side<2;side++)if(!FootPath(v,support.Shape,root,p.Rotation,side,s.FrameFeet[side],old[side],p.PoleAngle(age,side),p.FootNormal(age,side))){reason="腿部路径变化";return false;}
                previous=root;old[0]=s.FrameFeet[0];old[1]=s.FrameFeet[1];
            }
            var phase=p.Phase(to);int bearing=phase<Stage.Transfer?1-p.Front:p.Front;
            var point=phase<Stage.Transfer?p.Start[bearing]:p.Land[bearing].Point;GroundSupport.Pad pad;
            if(!GroundSupport.PadAt(v,point,p.Rotation,.45f,.45f,out pad)||Vector3.Distance(point,pad.Point)>.05f){reason="支撑地面已改变";return false;}
            return true;
        }
        public static Plan Search(EntityVehicle v,GroundSupport.State support,int front,out string reason)
        {
            reason="没有可跨越的目标";if(support==null||!support.Grounded||!support.Feet[0].Planted||!support.Feet[1].Planted)return null;
            var rb=v.vehicleRB;var root=rb.position+Origin.position;var yaw=Quaternion.Euler(0,rb.rotation.eulerAngles.y,0);var forward=yaw*Vector3.forward;
            float floor=(support.Feet[0].Position.y+support.Feet[1].Position.y)*.5f;
            float homeZ=(support.Shape.Home[0].z+support.Shape.Home[1].z)*.5f;
            float edge=-1,landing=-1,height=0,gapStart=-1,width=0,previousY=floor;
            Kind kind=Kind.Up;
            for(float distance=homeZ+.05f;distance<=homeZ+1.9f;distance+=.025f){
                var at=root+forward*distance;at.y=floor;Vector3 point;
                bool pointFound=GroundSupport.PointAt(v,at,Rules.ActiveStepHeight+.50f,Rules.ActiveStepHeight+.15f,out point);
                if(!pointFound){if(edge<0&&gapStart<0)gapStart=distance-.0125f;continue;}
                float y=point.y+Rules.SoleClearance;
                if(edge<0&&gapStart<0){
                    if(Mathf.Abs(y-previousY)<=Rules.AutoStepHeight+.02f){previousY=y;continue;}
                    height=y-floor;if(Mathf.Abs(height)>Rules.ActiveStepHeight+.02f){reason=height>0?"台阶过高":"落差过大";return null;}
                    edge=distance-.0125f;kind=height>0?Kind.Up:Kind.Down;
                }
                if(gapStart>=0&&edge<0){
                    // Refine both rims; report perpendicular net width, not the longer diagonal travel distance.
                    float lo=gapStart-.025f,hi=gapStart+.025f;
                    for(int k=0;k<6;k++){float mid=(lo+hi)*.5f;var q=root+forward*mid;q.y=floor;Vector3 found;if(GroundSupport.PointAt(v,q,.2f,.35f,out found)&&Mathf.Abs(found.y+Rules.SoleClearance-floor)<.08f)lo=mid;else hi=mid;}
                    float near=(lo+hi)*.5f;lo=distance-.025f;hi=distance;
                    for(int k=0;k<6;k++){float mid=(lo+hi)*.5f;var q=root+forward*mid;q.y=floor;Vector3 found;if(GroundSupport.PointAt(v,q,.2f,.35f,out found)&&Mathf.Abs(found.y+Rules.SoleClearance-floor)<.08f)hi=mid;else lo=mid;}
                    float far=(lo+hi)*.5f;
                    var a=root+forward*near;a.y=floor;var b=root+forward*far;b.y=floor;
                    width=GroundSupport.GapWidth(v,a,b);
                    if(width>Rules.ActiveGapWidth+.025f){reason="沟太宽";return null;}
                    kind=Kind.Gap;edge=gapStart;height=y-floor;
                    if(Mathf.Abs(height)>Rules.AutoStepHeight+.02f){kind=height>0?Kind.Up:Kind.Down;if(Mathf.Abs(height)>Rules.ActiveStepHeight+.02f){reason="缺少可靠落脚点";return null;}}
                }
                at.y=floor+height;GroundSupport.Pad pad;
                if(!GroundSupport.PadAt(v,at,yaw,Mathf.Abs(height)+.20f,.13f,out pad))continue;
                landing=distance;break;
            }
            width=Mathf.Min(width,Rules.ActiveGapWidth);
            if(landing<0){if(gapStart>=0)reason="缺少可靠落脚点";return SearchContour(v,support,front,ref reason);}
            var p=new Plan{Front=front,Type=kind,Root=root,Rotation=yaw,Height=height,Width=width,Duration=Rules.Complete(v)?Rules.CompleteTraverseSeconds:Rules.PrototypeTraverseSeconds};
            p.Start[0]=support.Feet[0].Position;p.Start[1]=support.Feet[1].Position;p.StartNormal[0]=support.Feet[0].Normal;p.StartNormal[1]=support.Feet[1].Normal;
            // Finish with the original stance spread, on pads wholly inside the top/opposite bank.
            float distanceRoot=landing-homeZ;
            for(float extension=0;extension<=.70f;extension+=.05f){
                p.Adaptive=extension>.35f;
                p.End=root+forward*(distanceRoot+extension);p.End.y=floor+height+support.Shape.NeutralY;
                bool pads=true;
                for(int side=0;side<2;side++){
                    var at=p.End+yaw*support.Shape.Home[side];at.y=floor+height;
                    if(!GroundSupport.PadAt(v,at,yaw,Mathf.Abs(height)+.20f,.13f,out p.Land[side])){pads=false;reason="顶部太窄或缺少双脚落点";break;}
                }
                if(pads){
                    p.End.y=float.PositiveInfinity;
                    for(int side=0;side<2;side++)p.End.y=Mathf.Min(p.End.y,p.Land[side].Point.y+support.Shape.NeutralY+support.Shape.AnkleOffset[side].y-(Quaternion.FromToRotation(Vector3.up,p.Land[side].Normal)*yaw*support.Shape.AnkleOffset[side]).y);
                    if(ValidatePath(v,support,p,out reason))return p;
                }
            }
            return SearchContour(v,support,front,ref reason);
        }
        static Plan SearchContour(EntityVehicle v,GroundSupport.State support,int front,ref string reason)
        {
            // A crater may change height smoothly at every ray sample. Compare
            // usable whole soles to the planted stance, rather than requiring a wall edge.
            if(!support.Cautious&&support.StopReason=="")return null;
            var rb=v.vehicleRB;var yaw=Quaternion.Euler(0,rb.rotation.eulerAngles.y,0);var root=rb.position+Origin.position;var forward=yaw*Vector3.forward;
            float floor=(support.Feet[0].Position.y+support.Feet[1].Position.y)*.5f;
            for(float distance=.20f;distance<=1.05f;distance+=.05f){
                var p=new Plan{Front=front,Root=root,Rotation=yaw,Adaptive=true,Duration=Rules.Complete(v)?Rules.CompleteTraverseSeconds:Rules.PrototypeTraverseSeconds};
                p.End=root+forward*distance;bool found=true;
                for(int i=0;i<2;i++){
                    p.Start[i]=support.Feet[i].Position;p.StartNormal[i]=support.Feet[i].Normal;
                    var at=p.End+yaw*support.Shape.Home[i];at.y=floor;
                    if(!GroundSupport.PadAt(v,at,yaw,Rules.ActiveStepHeight+.15f,Rules.ActiveStepHeight+.15f,out p.Land[i])){found=false;break;}
                    if(Mathf.Abs(p.Land[i].Point.y-p.Start[i].y)>Rules.ActiveStepHeight+.001f){found=false;break;}
                }
                if(!found)continue;p.Height=(p.Land[0].Point.y+p.Land[1].Point.y)*.5f-floor;
                if(Mathf.Abs(p.Height)<.08f)continue;p.Type=p.Height>=0?Kind.Up:Kind.Down;
                for(int i=0;i<2;i++)p.Lift[i]=FootPlanner.RequiredLift(support,p.Start[i],p.Land[i].Point);
                float height;if(!FootPlanner.Pelvis(support,p.End,p.Land[0].Point,p.Land[1].Point,p.Land[0].Normal,p.Land[1].Normal,FootPlanner.Preferred(support,p.Land[0].Point,p.Land[1].Point,p.Land[0].Normal,p.Land[1].Normal),out height))continue;
                p.End.y=height;string why;if(ValidatePath(v,support,p,out why))return p;reason=why;
            }
            return null;
        }
        static void Feedback(State s,string reason){s.Feedback=reason.StartsWith("腿部路径受阻")?"腿部路径受阻或落点超出腿长":reason;s.FeedbackUntil=Time.time+2.5f;s.Reason=reason;}
        public static string Prompt(EntityVehicle v)
        {
            var s=Get(v);if(s.Current!=null)return "越障 · "+StageName(s.Phase);
            if(Time.time<s.FeedbackUntil)return s.Feedback;
            var ground=GroundSupport.Find(v);if(ground!=null&&ground.StopReason!="")return ground.StopReason;
            if(s.Candidate==null&&s.BrakeDistance>0)return "前方缺少可靠落脚点 · 左 Alt 尝试主动迈步";
            if(s.Candidate==null)return "";
            string key=Rules.Key(v,"pzMechaTraverseKey",KeyCode.LeftAlt)==KeyCode.LeftAlt?"左 Alt":Rules.Key(v,"pzMechaTraverseKey",KeyCode.LeftAlt).ToString();
            return "["+key+"] "+(s.Candidate.Type==Kind.Up?"跨上台阶":s.Candidate.Type==Kind.Down?"走下台阶":"跨过短沟");
        }
        static string StageName(Stage phase){return phase==Stage.Prepare?"制动承重":phase==Stage.FrontStep?"抬前脚":phase==Stage.Transfer?"转移重心":phase==Stage.RearStep?"后脚跟进":phase==Stage.Settle?"双脚落稳":"退出";}
        public static float LimitSpeed(EntityVehicle v,GroundSupport.State support,float requested,float dt)
        {
            var s=Get(v);if(support==null||!support.Grounded)return requested;
            float slope=Vector3.Angle(Vector3.up,support.Normal);
            if(slope>Rules.NormalWalkSlope)requested*=Mathf.Lerp(1,.35f,Mathf.InverseLerp(Rules.NormalWalkSlope,Rules.MaxWalkSlope,slope));
            var rb=v.vehicleRB;float along=Vector3.Dot(rb.velocity,rb.rotation*Vector3.forward);
            if(Time.time-s.SearchAt>=.1f){
                s.SearchAt=Time.time;s.Candidate=Search(v,support,support.Next,out s.Reason);s.BrakeDistance=0;s.SmallDistance=0;
                float blocked;bool rough;FootPlanner.Preview(support,requested,out blocked,out rough);
                support.Cautious=rough;if(blocked>0&&(s.BrakeDistance==0||blocked<s.BrakeDistance))s.BrakeDistance=blocked;
            }
            if(requested>=0&&s.Candidate!=null&&(!s.Candidate.Adaptive||Mathf.Abs(s.Candidate.Height)>Rules.AutoStepHeight+.02f)||s.Pending||support.Recovering)return 0;
            if(s.BrakeDistance>0){float distance=Mathf.Max(0,s.BrakeDistance-.60f-Mathf.Abs(along)*.12f);float safe=Mathf.Sqrt(8*distance);requested=Mathf.Clamp(requested,-safe,safe);}
            if(support.Cautious)requested=Mathf.Clamp(requested,-Rules.RoughWalkSpeed,Rules.RoughWalkSpeed);
            if(s.SmallDistance>0)requested=Mathf.Clamp(requested,-1.5f,1.5f);
            return requested;
        }
        public static bool Begin(EntityVehicle v,GroundSupport.State support,Plan plan,bool network=true)
        {
            var s=Get(v);if(plan==null||support==null||s.Current!=null||Charging(v)||Boarding.Active(v)||Flight.AirPose(Locomotion.Get(v))||Locomotion.Get(v).HoverOn)return false;
            string reason;if(!ValidatePath(v,support,plan,out reason)){Feedback(s,reason);return false;}
            plan.Id=++s.NextId;plan.Actor=v.GetAttached(0)!=null?v.GetAttached(0).entityId:-1;
            s.Actor=plan.Actor;s.Current=plan;s.Age=0;s.Phase=Stage.Prepare;s.Pending=false;s.Accepted=Weapons.Server||!network;s.Remote=false;
            s.TargetRoot=plan.Root;s.TargetVelocity=Vector3.zero;var move=Locomotion.Get(v);move.AirSince=-1;move.AirPeakDownSpeed=0;move.LandingPendingUntil=-100;move.LandingEventExpected=false;RobotAudio.Event(v,"entry-brace",RobotAudio.NextPresentationSerial(),.45f);
            if(network)TraversalNet.Start(v,s,support);return true;
        }
        public static bool Step(EntityVehicle v,GroundSupport.State support,Locomotion.MoveState motion,float throttle,bool powered,float dt)
        {
            var s=Get(v);int actor=v.GetAttached(0)!=null?v.GetAttached(0).entityId:-1;
            if(s.Actor>=0&&actor!=s.Actor){Cancel(v,"驾驶员已更换");s.Pressed=s.Pending=false;s.QueuedToggle=s.QueuedJump=false;}
            if(s.Pressed){
                s.Pressed=false;
                if(s.Current!=null){}
                else if(!powered||support==null||!support.Grounded)Feedback(s,"需要落稳并启动机体");
                else if(throttle<-.05f)Feedback(s,"主动越障仅支持前向");
                else if(Charging(v)||Boarding.Active(v)||Flight.AirPose(motion)||motion.HoverOn)Feedback(s,"当前动作不能开始越障");
                else{s.Pending=true;s.PendingAt=Time.time;s.HadForward=throttle>.10f;}
            }
            if(s.Pending){
                if(!powered||Time.time-s.PendingAt>4){s.Pending=false;Feedback(s,"越障请求已取消");}
                else if(Vector3.ProjectOnPlane(v.vehicleRB.velocity,Vector3.up).magnitude<=Rules.TraverseSafeSpeed&&Mathf.Abs(v.vehicleRB.velocity.y)<.4f&&support!=null&&v.vehicleRB.position.y+Origin.position.y>=Mathf.Min(support.Feet[0].Position.y,support.Feet[1].Position.y)+support.Shape.NeutralY-.025f){
                    var plan=Search(v,support,support.Next,out s.Reason);s.Pending=false;
                    if(plan==null)Feedback(s,s.Reason);else Begin(v,support,plan);
                }
            }
            if(s.Current==null)return false;
            if(!powered||v.IsDead()||Boarding.Active(v)){Cancel(v,"动力或驾驶中断");return false;}
            if(s.Phase<=Stage.FrontStep&&(throttle<-.05f||(s.HadForward&&throttle<.05f)||motion.Toggle||motion.Jump)){
                Cancel(v,"越障准备已取消");return false;
            }
            if(s.Phase>=Stage.Transfer){s.QueuedToggle^=motion.Toggle;s.QueuedJump=motion.Jump;motion.Toggle=motion.Jump=false;}
            if(!s.Accepted&&Time.time-s.LastPacket>1){Cancel(v,"越障验证超时");return false;}
            if(!s.Accepted){
                // Wait for validation on two planted soles, preserving the physical brace.
                if(support==null||!support.Grounded){Cancel(v,"等待验证时失去支撑");return false;}
                s.Age=0;s.Phase=Stage.Prepare;
                var rb=v.vehicleRB;var delta=s.TargetRoot-(rb.position+Origin.position);
                rb.AddForce(Vector3.ClampMagnitude(Vector3.ProjectOnPlane(delta*400-rb.velocity*40,Vector3.up),80)*rb.mass,ForceMode.Force);
                GroundSupport.Apply(support,dt,s.TargetRoot);return true;
            }
            return Advance(v,support,s,dt);
        }
        public static bool Advance(EntityVehicle v,GroundSupport.State support,State s,float dt)
        {
            var p=s.Current;float next=Mathf.Min(p.Duration,s.Age+dt);var phase=p.Phase(next);
            Vector3 desired;p.FrameInto(next,s.FrameFeet,out desired);
            var rb=v.vehicleRB;var actual=rb.position+Origin.position;
            if(Vector3.Distance(actual,s.TargetRoot)>.35f){Cancel(v,"机体偏离安全路径");return false;}
            if(!GroundSupport.HullClear(v,support.Shape,actual,desired,p.Rotation)){Cancel(v,"路径出现新障碍");return false;}
            if(!EquipmentClear(v,support.Shape,p,next,actual,desired)){Cancel(v,"装备路径出现新障碍");return false;}
            for(int side=0;side<2;side++){
                var f=support.Feet[side];bool swing=side==p.Front?phase==Stage.FrontStep:phase==Stage.Transfer||phase==Stage.RearStep;
                if(!FootPath(v,support.Shape,desired,p.Rotation,side,s.FrameFeet[side],f.Position,p.PoleAngle(next,side),p.FootNormal(next,side))){Cancel(v,"腿部路径变化");return false;}
                if(f.Planted&&!GroundSupport.Recheck(v,f)){Cancel(v,"支撑地面已改变");return false;}
                if(swing){f.Planted=false;f.Swing=true;f.Position=s.FrameFeet[side];f.Normal=p.FootNormal(next,side);}
                bool land=side==p.Front?s.Phase==Stage.FrontStep&&phase>=Stage.Transfer:s.Phase==Stage.RearStep&&phase>=Stage.Settle;
                if(land){GroundSupport.Pad pad;
                    if(!GroundSupport.PadAt(v,p.Land[side].Point,p.Rotation,.08f,.08f,out pad)||Vector3.Distance(pad.Point,p.Land[side].Point)>.05f){Cancel(v,"落脚点已改变");return false;}
                    GroundSupport.Plant(support,side,pad);
                }
            }
            // Commit the load transfer only after the leading sole has been verified and planted.
            support.Grounded=support.Feet[0].Planted||support.Feet[1].Planted;
            if(!support.Grounded){Cancel(v,"失去支撑");return false;}
            var velocity=(desired-s.TargetRoot)/Mathf.Max(.001f,dt);
            var acceleration=(velocity-s.TargetVelocity)/Mathf.Max(.001f,dt)+(s.TargetRoot-actual)*400+(s.TargetVelocity-rb.velocity)*40-Physics.gravity+rb.velocity*rb.drag;
            rb.AddForce(Vector3.ClampMagnitude(acceleration,180)*rb.mass,ForceMode.Force);
            var tilt=Vector3.Cross(rb.rotation*Vector3.up,Vector3.up);
            float yaw=Mathf.DeltaAngle(rb.rotation.eulerAngles.y,p.Rotation.eulerAngles.y)*Mathf.Deg2Rad;
            Locomotion.AngularAcceleration(rb,Vector3.ClampMagnitude(tilt*10-rb.angularVelocity*3+Vector3.up*yaw*8,5));
            s.TargetVelocity=velocity;support.TargetY=desired.y;
            s.Age=next;s.TargetRoot=desired;
            bool edge=phase!=s.Phase;s.Phase=phase;
            if(edge&&phase==Stage.Transfer)RobotAudio.Event(v,"stand-lock",RobotAudio.NextPresentationSerial(),.32f);
            TraversalNet.Publish(v,s,edge);
            if(phase==Stage.Exit){
                if(Vector3.Distance(actual,p.End)>.05f){s.Phase=Stage.Settle;return true;}
                s.Current=null;s.Candidate=null;s.Phase=Stage.Idle;s.SearchAt=-100;
                TraversalNet.Publish(v,s,true);
                motionFinish(v,s);return false;
            }
            return true;
        }
        static void motionFinish(EntityVehicle v,State s){var m=Locomotion.Get(v);m.Toggle|=s.QueuedToggle;m.Jump|=s.QueuedJump;s.QueuedToggle=s.QueuedJump=false;m.AirSince=-1;m.AirPeakDownSpeed=0;m.LandingPendingUntil=-100;m.LandingEventExpected=false;m.Grounded=true;}
        public static void Cancel(EntityVehicle v,string reason)
        {
            var s=Get(v);if(s.Current==null)return;
            s.Current=null;s.Phase=Stage.Exit;s.Pending=false;s.TargetVelocity=Vector3.zero;Feedback(s,reason);
            var support=GroundSupport.Find(v);if(support!=null){
                // Stop at current physical position. A swinging foot may settle only onto a pad directly beneath it.
                foreach(var f in support.Feet)if(f.Swing){GroundSupport.Pad p;f.Swing=false;if(GroundSupport.PadAt(v,f.Position,v.vehicleRB.rotation,.08f,.20f,out p)&&Vector3.Distance(f.Position,p.Point)<.20f){f.Position=p.Point;f.Contact=p;f.Normal=p.Normal;f.Planted=true;}}
            }
            s.QueuedToggle=s.QueuedJump=false;TraversalNet.Publish(v,s,true);
        }
        public static void Tick(World world,float dt)
        {
            foreach(var s in states.Values){
                if(world.GetEntity(s.Vehicle.entityId)!=s.Vehicle)continue;
                if(s.Remote&&s.Current!=null){
                    int actor=s.Vehicle.GetAttached(0)!=null?s.Vehicle.GetAttached(0).entityId:-1;
                    if(Time.time-s.LastPacket>1||actor!=s.Actor||s.Vehicle.IsDead()){s.Current=null;s.Phase=Stage.Exit;continue;}
                    s.Age=Mathf.Min(s.Current.Duration,s.Age+dt);s.Phase=s.Current.Phase(s.Age);
                    s.Current.FrameInto(s.Age,s.FrameFeet,out s.TargetRoot);
                }
            }
        }
        public static bool Pose(EntityVehicle v,Model.Rig rig)
        {
            var s=Get(v);var support=GroundSupport.Find(v);
            if(s.Remote&&s.Current!=null){for(int i=0;i<2;i++)Gait.Solve(rig,i,s.FrameFeet[i]-Origin.position,s.Current.FootNormal(s.Age,i),s.Current.PoleAngle(s.Age,i));return true;}
            if(GroundNet.Pose(v,rig))return true;
            if(v.isEntityRemote||support==null||!support.Initialized)return false;
            for(int i=0;i<2;i++)Gait.Solve(rig,i,support.Feet[i].Position-Origin.position,support.Feet[i].Normal,s.Current!=null?s.Current.PoleAngle(s.Age,i):0);
            return true;
        }
        public static void EquipmentPose(EntityVehicle v,Model.Rig r)
        {
            if(!Rules.Complete(v))return;var s=Get(v);var ground=GroundSupport.Find(v);bool recovery=ground!=null&&ground.Recovering;bool active=Major(v)||recovery;float swordBlend=0;
            if(!active&&(Flight.AirPose(Locomotion.Get(v))||Boarding.Active(v))){s.EquipmentBlend=0;return;}
            if(recovery){s.EquipmentBlend=Mathf.MoveTowards(s.EquipmentBlend,1,Time.deltaTime/.12f);swordBlend=s.EquipmentBlend;}
            else if(active){float t=s.Age/s.Current.Duration;s.EquipmentBlend=Mathf.Clamp01(t/.12f);swordBlend=Mathf.Min(s.EquipmentBlend,Mathf.Clamp01((1-t)/.15f));}
            else if(s.EquipmentBlend>0){
                float proposed=Mathf.MoveTowards(s.EquipmentBlend,0,Time.deltaTime/.35f);var shape=GroundSupport.Find(v);bool clear=shape!=null;
                if(clear){var root=v.vehicleRB.position+Origin.position;var yaw=v.vehicleRB.rotation;
                    for(int i=0;i<2;i++){var fold=Quaternion.Euler(85*proposed,(i==0?15:-15)*proposed,(i==0?-12:12)*proposed);var box=shape.Shape.WingBounds[i];var center=root+yaw*(shape.Shape.WingRoot[i]+fold*box.center);clear&=GroundSupport.BoxClear(v,center,center,box.extents,yaw*fold);}
                }if(clear)s.EquipmentBlend=proposed;
            }
            float blend=s.EquipmentBlend;if(blend<=0&&!active)return;
            r.WingL.localRotation=r.RestRot[r.WingL]*Quaternion.Euler(85*blend,15*blend,-12*blend);
            r.WingR.localRotation=r.RestRot[r.WingR]*Quaternion.Euler(85*blend,-15*blend,12*blend);
            if(!active)return;
            r.Torso.localPosition=r.TorsoBasePosition;r.Torso.localRotation=r.RestRot[r.Torso];
            r.ShoulderL.localRotation=Quaternion.Slerp(r.ShoulderL.localRotation,r.RestRot[r.ShoulderL]*Quaternion.Euler(-18,0,-12),swordBlend);
            r.ElbowL.localRotation=Quaternion.Slerp(r.ElbowL.localRotation,r.RestRot[r.ElbowL]*Quaternion.Euler(-55,0,0),swordBlend);
            var shoulder=r.ShoulderR.localRotation;var elbow=r.ElbowR.localRotation;var wrist=r.HandR.localRotation;
            SwordMotion.Arm(r,r.Torso.TransformPoint(new Vector3(.65f,.65f,.75f)),r.Torso.TransformDirection(Vector3.right),55);
            SwordMotion.Blade(r,r.Torso.TransformDirection(new Vector3(.05f,.90f,.40f).normalized),r.Torso.TransformDirection(Vector3.right));
            r.ShoulderR.localRotation=Quaternion.Slerp(shoulder,r.ShoulderR.localRotation,swordBlend);r.ElbowR.localRotation=Quaternion.Slerp(elbow,r.ElbowR.localRotation,swordBlend);r.HandR.localRotation=Quaternion.Slerp(wrist,r.HandR.localRotation,swordBlend);
            Pose(v,r);
        }
        public static string Diagnostics(EntityVehicle v){var s=Get(v);return "action="+(s.Current!=null?s.Current.Id:0)+" phase="+s.Phase+" age="+s.Age+" reason="+s.Reason+" accepted="+s.Accepted+" remote="+s.Remote+" target="+s.TargetRoot+" brakeDistance="+s.BrakeDistance+" padFailure="+GroundSupport.PadFailure;}
        public static void Forget(EntityVehicle v){states.Remove(v.entityId);TraversalNet.Forget(v);}
        public static void Clear(){states.Clear();TraversalNet.Clear();}
    }
}
