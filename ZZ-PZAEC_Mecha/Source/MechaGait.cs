using System;
using System.Collections.Generic;
using UnityEngine;
namespace PZAEC.Mecha
{
    public static class Gait
    {
        sealed class Leg { public Vector3 Home, Foot, From, To, Normal=Vector3.up; public float Age,Duration; public bool Swing; }
        sealed class Walker { public EntityVehicle Vehicle; public Leg[] Legs={new Leg(),new Leg()}; public Vector3 LastPosition,Velocity;public float LastYaw,Lean,RecoilAt=-100,LastLanding=-100,LastJump=-100,Travel,PendingLanding=-100;public int Next;public bool WasAir; }
        static readonly Dictionary<int,Walker> walkers=new Dictionary<int,Walker>();
        public static void Recoil(int id) { Walker w;if(walkers.TryGetValue(id,out w))w.RecoilAt=Time.time; }
        public static bool Ground(EntityVehicle v,Vector3 at,out Vector3 p)
        { if(Weapons.Trace(v,at+Vector3.up*.65f,Vector3.down,1.8f,out var hit)){p=hit.hit.pos+Vector3.up*.02f;return true;}p=at;return false; }
        static Vector3 GroundNormal(EntityVehicle v,Vector3 p)
        { Vector3 a,b;if(!Ground(v,p+Vector3.right*.12f,out a)||!Ground(v,p+Vector3.forward*.12f,out b))return Vector3.up;
          var n=Vector3.Cross(b-p,a-p).normalized;return n.y>.6f?n:Vector3.up; }
        // All lengths measured in world metres; never mix pre-fit GLB units and rig units.
        public static void Solve(Model.Rig rig,int side,Vector3 sole,Vector3 normal,float poleAngle=0)
        {
            var hip=side==0?rig.HipL:rig.HipR;var knee=side==0?rig.KneeL:rig.KneeR;var ankle=side==0?rig.AnkleL:rig.AnkleR;var foot=side==0?rig.FootL:rig.FootR;
            var soleRotation=Quaternion.FromToRotation(rig.Mount.up,normal)*rig.Mount.rotation;
            var offset=Quaternion.Inverse(ankle.rotation)*(foot.position-ankle.position);
            var target=sole-soleRotation*offset;var delta=target-hip.position;
            float a=rig.LegUpper,b=rig.LegLower,dist=Mathf.Clamp(delta.magnitude,Mathf.Abs(a-b)+.01f,a+b-.005f);
            var axis=delta.sqrMagnitude>.00001f?delta.normalized:Vector3.down;
            var pole=Vector3.ProjectOnPlane(rig.Mount.forward,axis).normalized;
            if(pole.sqrMagnitude<.01f)pole=rig.Mount.up;
            pole=Quaternion.AngleAxis(poleAngle,axis)*pole;
            float along=(a*a-b*b+dist*dist)/(2*dist),height=Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
            var desiredKnee=hip.position+axis*along+pole*height;
            hip.rotation=Quaternion.FromToRotation(knee.position-hip.position,desiredKnee-hip.position)*hip.rotation;
            knee.rotation=Quaternion.FromToRotation(ankle.position-knee.position,hip.position+axis*dist-knee.position)*knee.rotation;
            ankle.rotation=soleRotation;
        }
        public static void Update(World world,EntityVehicle v,Model.Rig rig,float dt)
        {
            if(rig==null||rig.FootL==null||rig.FootR==null||dt<=0)return;
            rig.ResetPose();
            Walker w;if(!walkers.TryGetValue(v.entityId,out w)||w.Vehicle!=v)
            {
                // A renderer created halfway through an action does not replay
                // the actor's old jump / landing timestamps.
                var initial=Locomotion.Get(v);
                w=new Walker{Vehicle=v,LastPosition=v.position,LastYaw=Weapons.BodyRotation(v).eulerAngles.y,LastLanding=initial.LandingAt,LastJump=initial.JumpAt};
                for(int i=0;i<2;i++){var foot=i==0?rig.FootL:rig.FootR;w.Legs[i].Home=rig.Mount.InverseTransformPoint(foot.position);w.Legs[i].Foot=foot.position+Origin.position;}
                walkers[v.entityId]=w;
            }
            var displacement=v.position-w.LastPosition;float yaw=Weapons.BodyRotation(v).eulerAngles.y;
            float turn=Mathf.DeltaAngle(w.LastYaw,yaw)/Mathf.Max(dt,.001f);w.LastYaw=yaw;w.LastPosition=v.position;
            if(displacement.sqrMagnitude>16){foreach(var leg in w.Legs){leg.Foot=rig.Mount.TransformPoint(leg.Home)+Origin.position;leg.Swing=false;}displacement=Vector3.zero;}
            var velocity=Vector3.ProjectOnPlane(displacement/Mathf.Max(dt,.001f),Vector3.up);
            w.Velocity=Vector3.Lerp(w.Velocity,velocity,Mathf.Min(1,dt*12));float speed=w.Velocity.magnitude;
            var state=Locomotion.Get(v);bool airborne=!state.Grounded&&!state.HoverOn;
            state.VisualForward=Vector3.Dot(w.Velocity,rig.Mount.forward);state.VisualTurn=turn;
            if(Rules.Complete(v)&&v.isEntityRemote&&Flight.AirPose(state)&&Time.time-state.LastHeightAt>=.2f){state.LastHeightAt=Time.time;float clearance;state.FlightHeight=Flight.Clearance(v,out clearance)?clearance:-1;}
            float mountY=0;
            if(GroundSupport.Find(v)==null&&state.Grounded&&!state.HoverOn&&Ground(v,v.position,out var support))mountY=Mathf.Clamp(support.y-v.position.y-.05f,-.4f,.25f);
            rig.Mount.localPosition=new Vector3(0,Mathf.MoveTowards(rig.Mount.localPosition.y,mountY,dt*2),0);
            bool show=Boarding.ApplyPose(rig,v);float activity=0;
            if(!show)
            {
                float along=Vector3.Dot(w.Velocity,rig.Mount.forward);w.Lean=Mathf.MoveTowards(w.Lean,along*.9f,dt*18);
                float land=Mathf.Clamp01(1-(Time.time-state.LandingAt)/.5f);
                float drop=state.Charge*.22f+Mathf.Sin(land*Mathf.PI)*.2f+(speed>.2f&&!Flight.AirPose(state)?.20f:0);
                rig.Torso.localPosition=rig.TorsoBasePosition+Vector3.down*drop+Vector3.up*(Mathf.Sin(Time.time*1.4f)*.006f);
                rig.Torso.localRotation=rig.RestRot[rig.Torso]*Quaternion.Euler(state.Blend*12+w.Lean,0,-Mathf.Clamp(turn*.04f,-4,4));
                w.Travel+=speed*dt;float swing=Mathf.Sin(w.Travel*5)*Mathf.Clamp(speed*2,0,9)*(1-state.Blend);
                rig.ShoulderL.localRotation=rig.RestRot[rig.ShoulderL]*Quaternion.Euler(swing,0,0);
                rig.ShoulderR.localRotation=rig.RestRot[rig.ShoulderR]*Quaternion.Euler(-swing,0,0);
                float recoil=Mathf.Clamp01(1-(Time.time-w.RecoilAt)/.22f);
                rig.ElbowR.localRotation=rig.RestRot[rig.ElbowR]*Quaternion.Euler(-recoil*8,0,0);
            }
            if(Rules.Complete(v))SwordMotion.CaptureBase(rig);
            if(!show){Samurai.Pose(v,rig,dt,Time.time,false);Flight.Pose(v,rig,dt);}

            bool supportPose=!show&&!state.HoverOn&&(!airborne||Traversal.Active(v))&&(!Flight.AirPose(state)||state.Grounded&&(state.FlightMode==Flight.Phase.Landing||state.VerticalInput<0))&&Traversal.Pose(v,rig);
            if(supportPose){var ground=GroundSupport.Find(v);var traversal=Traversal.Get(v);
                if(v.isEntityRemote&&traversal.Current!=null){for(int i=0;i<2;i++){w.Legs[i].Foot=traversal.FrameFeet[i];w.Legs[i].Normal=Vector3.up;}activity=1;}
                else if(ground!=null){for(int i=0;i<2;i++){w.Legs[i].Foot=ground.Feet[i].Position;w.Legs[i].Normal=ground.Feet[i].Normal;w.Legs[i].Swing=ground.Feet[i].Swing;}activity=(ground.Feet[0].Swing||ground.Feet[1].Swing)?1:0;}}
            for(int i=0;!supportPose&&i<2;i++)
            {
                var leg=w.Legs[i];var home=rig.Mount.TransformPoint(leg.Home)+Origin.position;
                if(state.Blend>.05f||airborne||state.WingBlend>.05f)
                {
                    leg.Swing=false;float tuck=Rules.Complete(v)&&(state.FlightMode==Flight.Phase.Landing||state.VerticalInput<0)&&state.FlightHeight>=0?Mathf.Clamp01((state.FlightHeight-.5f)/3):1;
                    if(Rules.Complete(v)&&!Flight.AirPose(state)&&state.Grounded)tuck=0;
                    leg.Foot=home+(rig.Mount.up*.32f-rig.Mount.forward*.22f)*tuck;
                    Solve(rig,i,leg.Foot-Origin.position,rig.Mount.up);continue;
                }
                if(w.WasAir){leg.Foot=home;Ground(v,home,out leg.Foot);leg.Swing=false;}
                if(show)
                {
                    // Feet remain on the ground while the torso lowers and the hatch opens.
                    var poseTarget=Boarding.FootTarget(v,rig,i,home);
                    if(Ground(v,poseTarget,out var onGround)){float lift=Mathf.Max(0,poseTarget.y-home.y);poseTarget=onGround+Vector3.up*lift;leg.Normal=GroundNormal(v,onGround);}
                    leg.Foot=poseTarget;leg.Swing=false;
                }
                else if(leg.Swing)
                {
                    leg.Age+=dt;float t=Mathf.Clamp01(leg.Age/leg.Duration);float ease=t*t*(3-2*t);
                    leg.Foot=Vector3.Lerp(leg.From,leg.To,ease)+Vector3.up*(Mathf.Sin(t*Mathf.PI)*.2f);
                    activity=1;
                    if(t>=1){leg.Swing=false;leg.Foot=leg.To;leg.Normal=GroundNormal(v,leg.Foot);w.Next=1-i;RobotAudio.ContactEvent(v,i==0?"step-left":"step-right",RobotAudio.NextPresentationSerial(),leg.Foot,.85f);}
                }
                else if(!w.Legs[1-i].Swing)
                {
                    float duration=Mathf.Clamp(.34f-speed*.05f,.14f,.34f);
                    var predicted=home+Vector3.ClampMagnitude(w.Velocity*(duration+.08f),.95f);
                    var error=Vector3.ProjectOnPlane(predicted-leg.Foot,Vector3.up).magnitude;
                    bool moving=speed>.08f||Mathf.Abs(turn)>3;
                    if((moving&&error>.14f&&i==w.Next)||error>.6f||(!moving&&error>.12f))
                    {
                        if(Ground(v,predicted,out var target)&&Mathf.Abs(target.y-home.y)<.5f)
                        {leg.Swing=true;leg.From=leg.Foot;leg.To=target;leg.Age=0;leg.Duration=duration;}
                    }
                }
                Solve(rig,i,leg.Foot-Origin.position,leg.Normal);
            }
            w.WasAir=airborne||state.Blend>.05f;
            if(state.LandingAt>w.LastLanding){w.LastLanding=state.LandingAt;w.PendingLanding=Time.time+(state.LandingEventExpected?.8f:.25f);}
            // Wait longer for an authoritative heavy event, but never leave a
            // rejected/lost event completely silent. Fallback is light contact
            // only: it cannot apply damage or create an explosion.
            if(w.PendingLanding>=0&&Time.time>=w.PendingLanding)
            {if(!MechaFX.LandingRecently(v.entityId,.8f))RobotAudio.LandCue(v,v.position,.25f);w.PendingLanding=-100;}
            if(state.JumpAt>w.LastJump){w.LastJump=state.JumpAt;if(!state.HoverOn)RobotAudio.Event(v,"jump",RobotAudio.NextPresentationSerial(),.6f);}
            if(Rules.Complete(v)){SwordMotion.CacheFeet(rig,w.Legs[0].Foot-Origin.position,w.Legs[1].Foot-Origin.position,w.Legs[0].Normal,w.Legs[1].Normal);SwordMotion.SafePose(v,rig);}
            Traversal.EquipmentPose(v,rig);
            CombatFeedback.Blade(v,rig);RobotAudio.Update(v,activity,show);
            RobotPresentation.Update(v,rig,state.Blend,Boarding.Hatch(v));
        }
        public static void Forget(EntityVehicle v){RobotAudio.StopChannels(v);walkers.Remove(v.entityId);}
        public static void Clear(){foreach(var w in walkers.Values)RobotAudio.StopChannels(w.Vehicle);walkers.Clear();}
    }
}
