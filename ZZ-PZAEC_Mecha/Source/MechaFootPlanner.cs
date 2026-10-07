using UnityEngine;

namespace PZAEC.Mecha
{
    // Ordinary steps and recovery share the same world feet / constrained pelvis
    // as the render IK. No force, teleport, remote ownership or landing damage here.
    public static class FootPlanner
    {
        static readonly float[] forwardOffsets={0,-.12f,-.24f,.12f,.24f,.36f};
        static readonly float[] justiceForwardOffsets={0,-.12f,-.24f,.12f,.24f,.36f,.48f,.60f,.72f};
        static readonly float[] sideOffsets={0,-.10f,.10f,-.20f,.20f};
        static readonly float[] justicePreviewForward={.12f,-.12f,.24f,.36f,.48f,.60f,.72f,.84f,.96f};
        static readonly float[] previewForward={.12f,-.12f,.24f,.36f,.48f,.60f};
        public static string Failure="";
        public static float SelectedLift=Rules.TraverseToeClearance;
        static bool Fail(string why){Failure=why;return false;}
        public static bool HeightRange(GroundSupport.Profile p,Vector3 root,Quaternion yaw,int side,Vector3 foot,Vector3 normal,out float low,out float high)
        {
            var ankle=foot-Quaternion.FromToRotation(Vector3.up,normal)*yaw*p.AnkleOffset[side];var hip=yaw*p.Hip[side];
            float dx=root.x+hip.x-ankle.x,dz=root.z+hip.z-ankle.z;
            float radius=(p.Upper+p.Lower)*.95f-Rules.SupportHeightMargin;
            float vertical=radius*radius-dx*dx-dz*dz;
            low=ankle.y-hip.y+.20f;high=vertical>0?ankle.y-hip.y+Mathf.Sqrt(vertical):low-.01f;
            return high>=low;
        }
        public static bool Pelvis(GroundSupport.State s,Vector3 root,Vector3 left,Vector3 right,Vector3 normalL,Vector3 normalR,float preferred,out float height)
        {return Pelvis(s.Shape,s.Vehicle.vehicleRB.rotation,root,left,right,normalL,normalR,preferred,out height);}
        public static bool Pelvis(GroundSupport.Profile shape,Quaternion yaw,Vector3 root,Vector3 left,Vector3 right,Vector3 normalL,Vector3 normalR,float preferred,out float height)
        {
            float lo0,hi0,lo1,hi1;
            bool a=HeightRange(shape,root,yaw,0,left,normalL,out lo0,out hi0),b=HeightRange(shape,root,yaw,1,right,normalR,out lo1,out hi1);
            float low=Mathf.Max(lo0,lo1),high=Mathf.Min(hi0,hi1);height=Mathf.Clamp(preferred,low,Mathf.Max(low,high));
            return a&&b&&low<=high;
        }
        public static float Preferred(GroundSupport.State s,Vector3 left,Vector3 right,Vector3 normalL,Vector3 normalR)
        {
            var yaw=s.Vehicle.vehicleRB.rotation;
            float a=left.y+s.Shape.NeutralY+s.Shape.AnkleOffset[0].y-(Quaternion.FromToRotation(Vector3.up,normalL)*yaw*s.Shape.AnkleOffset[0]).y;
            float b=right.y+s.Shape.NeutralY+s.Shape.AnkleOffset[1].y-(Quaternion.FromToRotation(Vector3.up,normalR)*yaw*s.Shape.AnkleOffset[1]).y;
            // Keep stance adjustment continuous when braking. The feasible interval
            // always takes precedence over the preferred standing/crouching height.
            float activity=Mathf.Clamp01(Mathf.Max(Vector3.ProjectOnPlane(s.Vehicle.vehicleRB.velocity,Vector3.up).magnitude/.8f,Mathf.Abs(s.Vehicle.vehicleRB.angularVelocity.y)/.3f));
            return Mathf.Min(a,b)-.15f*activity;
        }
        public static bool TargetHeight(GroundSupport.State s,Vector3 root,out float height)
        {
            var a=s.Feet[0];var b=s.Feet[1];
            return Pelvis(s,root,a.Position,b.Position,a.Normal,b.Normal,Preferred(s,a.Position,b.Position,a.Normal,b.Normal),out height);
        }
        public static Vector3 ReachableSole(GroundSupport.State s,int side,Vector3 sole,Vector3 normal)
        {
            var rb=s.Vehicle.vehicleRB;var rotation=Quaternion.FromToRotation(Vector3.up,normal)*rb.rotation;
            var hip=rb.position+Origin.position+rb.rotation*s.Shape.Hip[side];var offset=rotation*s.Shape.AnkleOffset[side];
            var ankle=sole-offset;float radius=(s.Shape.Upper+s.Shape.Lower)*.93f;
            return hip+Vector3.ClampMagnitude(ankle-hip,radius)+offset;
        }
        public static bool Validate(GroundSupport.State s,int side,GroundSupport.Pad target,float duration,Vector3 velocity,bool recovering)
        {
            var v=s.Vehicle;var rb=v.vehicleRB;var start=rb.position+Origin.position;var f=s.Feet[side];var other=s.Feet[1-side];
            var last=f.Position;var previousRoot=start;float lift=RequiredLift(s,last,target.Point);int count=Mathf.Max(8,Mathf.CeilToInt(Vector3.Distance(last,target.Point)/.05f));
            for(int k=0;k<=count;k++){
                float t=k/(float)count;var foot=Traversal.Swing(f.Position,target.Point,t,lift);
                var normal=Vector3.Slerp(f.Normal,target.Normal,t).normalized;var root=start+velocity*(duration*t);float height;
                var left=side==0?foot:other.Position;var right=side==1?foot:other.Position;
                var nl=side==0?normal:other.Normal;var nr=side==1?normal:other.Normal;
                if(!Pelvis(s,root,left,right,nl,nr,Preferred(s,left,right,nl,nr),out height))return Fail("first pelvis t="+t);
                root.y=height;
                if(!GroundSupport.HullClear(v,s.Shape,previousRoot,root,rb.rotation)||!Traversal.FootPath(v,s.Shape,root,rb.rotation,side,foot,last,0,normal)||!Traversal.FootPath(v,s.Shape,root,rb.rotation,1-side,other.Position,other.Position,0,other.Normal))return Fail("first path t="+t+" "+Traversal.PathFailure);
                previousRoot=root;last=foot;
            }
            // A following foot must have a reachable route before committing the
            // moving pelvis. Recovery is stationary and already retains one pad.
            if(!recovering){
                var end=start+velocity*duration;var home=end+rb.rotation*s.Shape.Home[1-side]+velocity*(duration+.035f);home.y=target.Point.y;GroundSupport.Pad follow;
                bool reachable=false;
                foreach(float dz in (s.Shape.Justice?justiceForwardOffsets:forwardOffsets))foreach(float dx in sideOffsets){
                    if(reachable)continue;
                    var at=home+rb.rotation*new Vector3(dx,0,dz);
                    if(!GroundSupport.PadAt(v,at,rb.rotation,.7f,.7f,out follow)||Mathf.Abs(Vector3.Dot(follow.Point-other.Position,other.Normal))/Mathf.Max(.01f,other.Normal.y)>Rules.AutoStepHeight+.02f)continue;
                    if(Following(s,side,target,follow,end,other,duration,velocity,count)){reachable=true;break;}
                }
                if(!reachable)return Fail("following complete path");
            }
            SelectedLift=lift;return true;
        }
        static bool Following(GroundSupport.State s,int side,GroundSupport.Pad target,GroundSupport.Pad follow,Vector3 end,GroundSupport.Foot other,float duration,Vector3 velocity,int count)
        {
                var v=s.Vehicle;var rb=v.vehicleRB;float lift=RequiredLift(s,other.Position,follow.Point);
                var lastOther=other.Position;var lastBody=end;float initial;
                if(!Pelvis(s,end,side==0?target.Point:other.Position,side==1?target.Point:other.Position,side==0?target.Normal:other.Normal,side==1?target.Normal:other.Normal,Preferred(s,target.Point,other.Position,target.Normal,other.Normal),out initial))return false;lastBody.y=initial;
                for(int k=0;k<=count;k++){
                    float t=k/(float)count;var moving=Traversal.Swing(other.Position,follow.Point,t,lift);var n=Vector3.Slerp(other.Normal,follow.Normal,t).normalized;
                    var body=end+velocity*(duration*t);float y;
                    var l=side==0?target.Point:moving;var r=side==1?target.Point:moving;var nl=side==0?target.Normal:n;var nr=side==1?target.Normal:n;
                    if(!Pelvis(s,body,l,r,nl,nr,Preferred(s,l,r,nl,nr),out y))return Fail("following pelvis t="+t);body.y=y;
                    if(!GroundSupport.HullClear(v,s.Shape,lastBody,body,rb.rotation)||!Traversal.FootPath(v,s.Shape,body,rb.rotation,1-side,moving,lastOther,0,n)||!Traversal.FootPath(v,s.Shape,body,rb.rotation,side,target.Point,target.Point,0,target.Normal))return Fail("following path t="+t+" "+Traversal.PathFailure);
                    lastOther=moving;lastBody=body;
                }
            return true;
        }
        public static float RequiredLift(GroundSupport.State s,Vector3 from,Vector3 to)
        {
            float peak=Mathf.Max(from.y,to.y);var yaw=s.Vehicle.vehicleRB.rotation;int count=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(from,to)/.05f));
            for(int k=0;k<=count;k++){
                var at=Vector3.Lerp(from,to,k/(float)count);at.y=peak;
                for(int corner=0;corner<5;corner++){
                    var sample=at;if(corner>0)sample+=yaw*new Vector3((corner<=2?-1:1)*Rules.SoleWidth(s.Vehicle)*.5f,0,(corner%2==0?-1:1)*Rules.SoleDepth(s.Vehicle)*.5f);
                    Vector3 point;if(GroundSupport.PointAt(s.Vehicle,sample,.8f,.8f,out point))peak=Mathf.Max(peak,point.y+Rules.SoleClearance);
                }
            }
            return peak-Mathf.Max(from.y,to.y)+Rules.TraverseToeClearance;
        }
        public static bool Select(GroundSupport.State s,int side,Vector3 desired,float duration,Vector3 velocity,bool recovering,out GroundSupport.Pad best)
        {
            best=new GroundSupport.Pad();var rb=s.Vehicle.vehicleRB;var f=s.Feet[side];float score=float.PositiveInfinity,bestLift=Rules.TraverseToeClearance;
            foreach(float dz in (s.Shape.Justice?justiceForwardOffsets:forwardOffsets))foreach(float dx in sideOffsets){
                var at=desired+rb.rotation*new Vector3(dx,0,dz);at.y=f.Position.y;GroundSupport.Pad pad;
                if(!GroundSupport.PadAt(s.Vehicle,at,rb.rotation,.8f,.8f,out pad)||Mathf.Abs(Vector3.Dot(pad.Point-f.Position,f.Normal))/Mathf.Max(.01f,f.Normal.y)>Rules.AutoStepHeight+.02f)continue;
                float value=Vector3.ProjectOnPlane(pad.Point-desired,Vector3.up).sqrMagnitude+pad.Residual*.5f;
                if(value>=score||!Validate(s,side,pad,duration,velocity,recovering))continue;
                score=value;best=pad;bestLift=SelectedLift;
            }
            SelectedLift=bestLift;
            return !float.IsPositiveInfinity(score);
        }
        public static void Release(GroundSupport.State s,int side)
        {
            if(!s.Recovering&&s.Grounded&&s.Clock-s.RecoverySoundAt>.35f){s.RecoverySoundAt=s.Clock;RobotAudio.Event(s.Vehicle,"entry-brace",RobotAudio.NextPresentationSerial(),.28f);}
            var f=s.Feet[side];f.Planted=f.Swing=false;f.Position=ReachableSole(s,side,f.Position,f.Normal);f.Recovery=true;f.Retry=0;
            s.Recovering=s.Grounded;s.StopReason="正在重新落稳";
        }
        public static void Recovery(GroundSupport.State s,float dt,bool enabled)
        {
            if(!enabled||!s.Grounded)return;
            for(int side=0;side<2;side++){
                var f=s.Feet[side];if(f.Planted||f.Swing||!s.Feet[1-side].Planted)continue;
                s.Recovering=true;f.Recovery=true;f.Retry-=dt;if(f.Retry>0)continue;f.Retry=.10f;
                f.Position=ReachableSole(s,side,f.Position,f.Normal);
                var home=s.Vehicle.vehicleRB.position+Origin.position+s.Vehicle.vehicleRB.rotation*s.Shape.Home[side];
                GroundSupport.Pad target;if(!Select(s,side,home,Rules.RecoveryStepSeconds,Vector3.zero,true,out target)){s.StopReason="缺少安全补脚位置";continue;}
                f.From=f.Position;f.To=target.Point;f.FromNormal=f.Normal;f.ToNormal=target.Normal;f.Age=0;f.Duration=Rules.RecoveryStepSeconds;f.Lift=SelectedLift;f.Swing=true;
            }
        }
        public static void Preview(GroundSupport.State s,float requested,out float blocked,out bool rough)
        {
            blocked=0;rough=false;var rb=s.Vehicle.vehicleRB;var root=rb.position+Origin.position;var yaw=Quaternion.Euler(0,rb.rotation.eulerAngles.y,0);
            var forward=yaw*Vector3.forward*(requested<0?-1:1);float speed=Vector3.ProjectOnPlane(rb.velocity,Vector3.up).magnitude;
            // Include the search cache delay and the first full sole in the braking distance.
            float horizon=Mathf.Max(.9f,speed*speed/8+speed*.12f+.75f);
            for(int side=0;side<2;side++){
                var reference=s.Feet[side].Contact.Surface!=null?s.Feet[side].Contact.Point:s.Feet[side].Position;var normal=s.Feet[side].Contact.Surface!=null?s.Feet[side].Contact.Normal:s.Feet[side].Normal;
                for(float distance=.15f;distance<=horizon;distance+=.15f){
                    var at=root+yaw*s.Shape.Home[side]+forward*distance;
                    at.y=reference.y-(normal.x*(at.x-reference.x)+normal.z*(at.z-reference.z))/Mathf.Max(.01f,normal.y);
                    GroundSupport.Pad pad;
                    bool found=GroundSupport.PadAt(s.Vehicle,at,yaw,.65f,.65f,out pad);
                    if(!found){
                        // A single unusable sole center is not a wall. Check nearby
                        // full soles; execution still validates both complete leg paths.
                        foreach(float dz in (s.Shape.Justice?justicePreviewForward:previewForward))foreach(float dx in sideOffsets){
                            var candidate=at+yaw*new Vector3(dx,0,dz);GroundSupport.Pad alternate;
                            if(!found&&GroundSupport.PadAt(s.Vehicle,candidate,yaw,.65f,.65f,out alternate)){pad=alternate;found=true;rough=true;}
                        }
                    }
                    if(!found||Mathf.Abs(pad.Point.y-at.y)>Rules.AutoStepHeight+.02f){if(blocked==0||distance<blocked)blocked=distance;Failure=found?"前方高差超出自动迈步范围":GroundSupport.PadFailure;break;}
                    if(Mathf.Abs(pad.Point.y-at.y)>.018f||Vector3.Angle(normal,pad.Normal)>3||pad.Residual>.012f)rough=true;
                    reference=pad.Point;normal=pad.Normal;
                }
            }
        }
    }
}
