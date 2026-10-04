using System;
using System.Collections.Generic;
using UnityEngine;

namespace PZAEC.Mecha
{
    // Complete Form only. Input is a leased set of held buttons, never client damage.
    // The server owns charge time, swing contacts, shield energy and action snapshots.
    public static class Samurai
    {
        public const byte InputIdle=16, InputSword=17, InputGuard=18, InputBoth=19, Cancel=20, Snapshot=16;
        public const float HeavyCharge=.8f, AlertDelay=6f, NormalDuration=1.6f, HeavyDuration=1.9f;
        public static readonly Vector3 Grip=new Vector3(.78f,1.81f,.76f);
        public static readonly Vector3 BladeRoot=new Vector3(.73f,1.57f,.30f), BladeTip=new Vector3(.23f,.50f,-1.10f);
        public sealed class State
        {
            public EntityVehicle Vehicle;
            public int Actor=-1, Sequence, Serial, Received=-1, Combo;
            public int AttackSerial; public bool Blocked; public float BlockedAt=-100,StartCharge,RecoveryPoleAngle; public Vector3 RecoveryGrip,RecoveryDirection,RecoveryNormal=Vector3.right,RecoveryShift,RecoveryEuler;
            public bool SwordHeld, GuardHeld, SuppressSword, Charging, Guarding, Heavy, Swing, Queued, QueuedHeavy, BeamSpent;
            public float InputAt=-100, PressedAt, Started=-100, LastCombat=-100, LastHit=-100, BrokenUntil, Energy=100, LastSync=-100;
            public float BeamStarted=-1, LaserCharge, LaserWait, ReceivedAt=-100, Alert, GuardBlend, AimYaw, AimPitch, HeadYaw, HeadPitch;
            public float LastSound=-100,LastShieldSound=-100; public float LastSweep=-1; public Vector3 PreviousRoot,PreviousTip,PreviousPosition;
            public readonly HashSet<int> Hit=new HashSet<int>();
        }
        static readonly Dictionary<int,State> states=new Dictionary<int,State>();
        static byte input=255;static float nextInput;
        public static State Get(EntityVehicle v)
        { State s;if(!states.TryGetValue(v.entityId,out s)||s.Vehicle!=v){s=new State{Vehicle=v};states[v.entityId]=s;}return s; }
        public static bool Busy(EntityVehicle v){if(!Rules.Complete(v))return false;var s=Get(v);return s.Swing||s.Charging;}
        public static bool Braced(EntityVehicle v){return Rules.Complete(v)&&!Flight.AirPose(Locomotion.Get(v))&&(Get(v).GuardHeld||Get(v).SwordHeld||Busy(v));}
        public static bool GroundReady(EntityVehicle v)
        {var m=Locomotion.Get(v);return m.Grounded&&!Flight.AirPose(m)&&m.WingBlend<.1f&&!m.HoverOn&&!m.Boost&&m.Blend<.1f&&(v.vehicleRB==null||Vector3.ProjectOnPlane(v.vehicleRB.velocity,Vector3.up).magnitude<=4.2f);}
        static bool Operator(State s)
        {var p=s.Vehicle.GetAttached(0) as EntityAlive;return p!=null&&!p.IsDead()&&!s.Vehicle.IsDead()&&!Boarding.Active(s.Vehicle)&&s.Vehicle.vehicle.GetHealth()>0;}
        public static void Stop(EntityVehicle v)
        {
            if(!Rules.Complete(v))return;var s=Get(v);s.SwordHeld=s.GuardHeld=s.Charging=s.Guarding=s.Swing=s.Queued=false;
            s.Blocked=false;s.StartCharge=0;s.SuppressSword=true;s.BeamStarted=-1;s.LaserCharge=0;s.BeamSpent=false;s.LastSweep=-1;
        }
        public static void ReleaseLocal(EntityVehicle v)
        {if(Rules.Complete(v))Weapons.SendLocalIntent(v,Cancel,Vector3.zero,Vector3.zero);input=255;nextInput=0;}
        public static void LocalInput(EntityVehicle v,Ray ray,bool ready)
        {
            byte op=!ready?InputIdle:Input.GetKey(KeyCode.Mouse0)?(Input.GetKey(KeyCode.Mouse1)?InputBoth:InputSword):Input.GetKey(KeyCode.Mouse1)?InputGuard:InputIdle;
            if(!Weapons.Server){var s=Get(v);s.GuardHeld=op==InputGuard||op==InputBoth;s.SwordHeld=op==InputSword||op==InputBoth;}
            if(op!=input||Time.time>=nextInput){Weapons.SendLocalIntent(v,op,ray.direction,ray.origin);input=op;nextInput=Time.time+.1f;}
        }
        public static void Request(EntityVehicle v,int actor,int sequence,byte op,Vector3 direction,Vector3 origin,float now)
        {
            if(!Rules.Complete(v)||op<InputIdle||op>Cancel||v.GetAttached(0)==null||v.GetAttached(0).entityId!=actor)return;
            var s=Get(v);if(s.Actor==actor&&sequence<=s.Sequence)return;
            if(s.Actor!=actor){Stop(v);s.Actor=actor;}s.Sequence=sequence;
            if(op==Cancel){Stop(v);s.InputAt=-100;return;}
            if(!Operator(s)||!Weapons.Finite(direction.x)||!Weapons.Finite(direction.y)||!Weapons.Finite(direction.z)||!Weapons.Finite(origin.x)||!Weapons.Finite(origin.y)||!Weapons.Finite(origin.z)||(origin-v.position).sqrMagnitude>36||direction.sqrMagnitude<.001f)return;
            s.InputAt=now;SetAim(s,direction);
            bool sword=op==InputSword||op==InputBoth,guard=op==InputGuard||op==InputBoth;
            if(!sword)s.SuppressSword=false;
            if(guard&&s.Charging&&now-s.PressedAt<HeavyCharge){s.Charging=false;s.SuppressSword=true;}
            if(sword&&!s.SwordHeld&&!s.SuppressSword&&!guard&&GroundReady(v))
            {s.PressedAt=now;s.Charging=!s.Swing;s.LastCombat=now;}
            if(!sword&&s.SwordHeld&&!s.SuppressSword&&!guard&&GroundReady(v))
            {
                bool heavy=now-s.PressedAt>=HeavyCharge;
                if(s.Swing){s.Queued=true;s.QueuedHeavy=heavy;}
                else if(s.Charging)Start(s,heavy,now);
                s.Charging=false;
            }
            s.SwordHeld=sword;s.GuardHeld=guard;
            if(guard)s.LastCombat=now;
        }
        static void SetAim(State s,Vector3 direction)
        {
            var local=Quaternion.Inverse(Weapons.BodyRotation(s.Vehicle))*direction.normalized;
            s.AimYaw=Mathf.Clamp(Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg,-45,45);
            s.AimPitch=Mathf.Clamp(Mathf.Atan2(local.y,new Vector2(local.x,local.z).magnitude)*Mathf.Rad2Deg,-25,30);
        }
        public static void Start(State s,bool heavy,float now)
        {
            // Quantize once on the authority; the same value travels in the
            // snapshot flags so remote actors author the same release pose.
            s.StartCharge=Mathf.RoundToInt(Mathf.Clamp01(heavy?1:s.Charging?(now-s.PressedAt)/HeavyCharge:0)*255)/255f;
            s.AttackSerial++;s.Blocked=false;s.Swing=true;s.Heavy=heavy;s.Started=now;s.LastCombat=now;s.Guarding=false;s.Charging=false;s.LastSweep=-1;s.Hit.Clear();
            s.Combo=heavy?0:1-s.Combo;s.BeamStarted=-1;s.LaserCharge=0;
            Weapons.GetState(s.Vehicle).LastWeaponUse=now;
        }
        public static float Duration(State s){return s.Heavy?HeavyDuration:NormalDuration;}
        public static bool Arc(Vector3 local)
        {float yaw=Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg,pitch=Mathf.Atan2(local.y,new Vector2(local.x,local.z).magnitude)*Mathf.Rad2Deg;return Mathf.Abs(yaw)<=45&&pitch>=-25&&pitch<=30;}
        public static bool LaserReady(Weapons.State w,float now)
        {
            var s=Get(w.Vehicle);
            if(!w.TriggerHeld){s.BeamSpent=false;s.BeamStarted=-1;s.LaserCharge=0;return false;}
            if(s.Swing||s.Charging||now-Locomotion.Get(w.Vehicle).LandingAt<.45f||now<w.NextBeam||s.BeamSpent)
            {s.BeamStarted=-1;s.LaserCharge=0;return false;}
            s.LastCombat=now;SetAim(s,w.AimDirection);
            if(s.BeamStarted<0)s.BeamStarted=now;
            s.LaserCharge=Mathf.Clamp01((now-s.BeamStarted)/.6f);
            return s.LaserCharge>=1;
        }
        public static void LaserFired(EntityVehicle v)
        {var s=Get(v);s.BeamSpent=true;s.BeamStarted=-1;s.LaserCharge=0;}
        public static Vector3 Muzzle(Model.Rig rig,EntityVehicle v)
        {return rig!=null&&rig.Head!=null?rig.Head.TransformPoint(new Vector3(0,.10f,.20f))+Origin.position:v.position+Weapons.BodyRotation(v)*new Vector3(0,2.71f,.58f);}
        public static bool ShieldObstructs(Model.Rig rig,Vector3 origin,Vector3 direction)
        {
            if(rig==null||rig.HandL==null)return false;
            var o=rig.HandL.InverseTransformPoint(origin-Origin.position);var d=rig.HandL.InverseTransformDirection(direction);
            if(Mathf.Abs(d.x)<.0001f)return false;float t=(-.09f-o.x)/d.x;if(t<0||t>2)return false;
            var p=o+d*t;return p.y>-.85f&&p.y<1.10f&&p.z>-.56f&&p.z<.49f;
        }
        public static float ShieldFactor(EntityVehicle v,DamageSource source,int damage,bool explosion)
        {
            if(!Weapons.Server||!Rules.Complete(v)||source==null||damage<=0)return 1;
            var s=Get(v);s.LastCombat=s.LastHit=Time.time;
            if(!s.Guarding||!Operator(s)||Time.time-s.InputAt>Rules.HoldTimeout||s.Energy<=0||s.Swing||s.Charging||!GroundReady(v))return 1;
            var incoming=Vector3.ProjectOnPlane(-source.getDirection(),Vector3.up);
            if(incoming.sqrMagnitude<.0001f||Vector3.Dot(incoming.normalized,Weapons.BodyRotation(v)*Vector3.forward)<Mathf.Cos(50*Mathf.Deg2Rad))return 1;
            float cost=Mathf.Clamp(damage/3000f,4,40),available=Mathf.Min(1,s.Energy/cost);
            s.Energy=Mathf.Max(0,s.Energy-cost);
            if(Time.time-s.LastShieldSound>.15f){s.LastShieldSound=Time.time;RobotAudio.OneShot(v,"shield",.4f);}
            if(s.Energy<=0){s.BrokenUntil=Time.time+2.5f;s.Guarding=false;}
            return 1-(explosion?.35f:.70f)*available;
        }
        public static void Tick(World world,float dt)
        {
            var gone=new List<int>();
            foreach(var pair in states)
            {
                var s=pair.Value;var v=s.Vehicle;if(v==null||world.GetEntity(pair.Key)!=v){gone.Add(pair.Key);continue;}
                if(!Weapons.Server){if(Time.time-s.ReceivedAt>1){Stop(v);s.LastCombat=-100;}continue;}
                float now=Time.time;
                if(!Operator(s)||now-s.InputAt>Rules.HoldTimeout)Stop(v);
                if(!GroundReady(v)){s.Charging=s.Swing=s.Queued=false;s.LastSweep=-1;}
                if(s.Blocked&&now-s.BlockedAt>=.38f){s.Swing=s.Blocked=s.Queued=false;s.SuppressSword=true;}
                if(s.Swing&&!s.Blocked&&now-s.Started>=Duration(s))
                {s.Swing=false;if(s.SwordHeld&&!s.GuardHeld)s.Charging=true;if(s.Queued&&!s.GuardHeld){bool heavy=s.QueuedHeavy;s.Queued=false;Start(s,heavy,now);}else s.Queued=false;}
                s.Guarding=s.GuardHeld&&!s.Swing&&!s.Charging&&GroundReady(v)&&now>=s.BrokenUntil&&s.Energy>0&&Operator(s);
                var w=Weapons.GetState(v);
                if(!w.TriggerHeld){s.BeamSpent=false;s.BeamStarted=-1;s.LaserCharge=0;}
                if(w.MissileAiming||w.TriggerHeld)s.LastCombat=now;
                s.LaserWait=Mathf.Max(0,w.NextBeam-now);
                if(!s.GuardHeld&&s.LaserCharge==0&&now-s.LastHit>2&&now>=s.BrokenUntil)s.Energy=Mathf.Min(100,s.Energy+18*dt);
                if(now-s.LastSync>=.1f){s.LastSync=now;Broadcast(s,now);}
            }
            foreach(int id in gone)states.Remove(id);
        }
        static void Broadcast(State s,float now)
        {
            int flags=(s.Guarding?1:0)|(s.Charging?2:0)|(s.Heavy?4:0)|(s.Swing?8:0)|(s.Combo==1?16:0)|(now<s.BrokenUntil?32:0)|(s.Blocked?64:0)|(Mathf.RoundToInt(Mathf.Clamp01(s.StartCharge)*255)<<8);
            Weapons.Broadcast(s.Vehicle.entityId,++s.Serial,Snapshot,new Vector3(s.Energy,Mathf.Max(0,AlertDelay-(now-s.LastCombat)),s.Swing?now-s.Started:s.Charging?now-s.PressedAt:0),new Vector3(s.AimYaw,s.AimPitch,s.LaserCharge),flags,s.LaserWait);
        }
        public static void Receive(EntityVehicle v,int serial,Vector3 a,Vector3 b,float flags,float wait)
        {
            if(Weapons.Server||!Rules.Complete(v))return;var s=Get(v);if(serial<=s.Received)return;s.Received=serial;s.ReceivedAt=Time.time;
            if(a.x<s.Energy&&Time.time-s.LastShieldSound>.15f){s.LastShieldSound=Time.time;RobotAudio.OneShot(v,"shield",.4f);}
            int f=(int)flags;s.Energy=Mathf.Clamp(a.x,0,100);s.LastCombat=Time.time-AlertDelay+Mathf.Clamp(a.y,0,AlertDelay);s.Started=s.PressedAt=Time.time-Mathf.Clamp(a.z,0,5);
            s.Guarding=(f&1)!=0;s.GuardHeld=s.Guarding;s.Charging=(f&2)!=0;s.Heavy=(f&4)!=0;s.Swing=(f&8)!=0;s.Combo=(f&16)!=0?1:0;s.StartCharge=((f>>8)&255)/255f;s.BrokenUntil=(f&32)!=0?Time.time+.2f:0;
            if((f&64)!=0&&!s.Blocked)Interrupt(v,Time.time);else if((f&64)==0)s.Blocked=false;
            s.AimYaw=Mathf.Clamp(b.x,-45,45);s.AimPitch=Mathf.Clamp(b.y,-25,30);s.LaserCharge=Mathf.Clamp01(b.z);s.LaserWait=Mathf.Clamp(wait,0,3);
        }
        static void ShieldArm(Model.Rig r,float blend)
        {
            if(blend<=0)return;
            var shoulder=r.ShoulderL;var elbow=r.ElbowL;var hand=r.HandL;
            float a=Vector3.Distance(shoulder.position,elbow.position),b=Vector3.Distance(elbow.position,hand.position);
            var bodyOffset=r.Torso.position-r.Mount.TransformPoint(r.TorsoBasePosition);
            var target=Vector3.Lerp(hand.position,r.Mount.TransformPoint(new Vector3(-.34f,2.04f,.72f))+bodyOffset,blend);
            var delta=target-shoulder.position;float d=Mathf.Clamp(delta.magnitude,Mathf.Abs(a-b)+.005f,a+b-.005f);
            var axis=delta.normalized;var pole=Vector3.ProjectOnPlane(-r.Mount.right,axis).normalized;
            float along=(a*a-b*b+d*d)/(2*d);var knee=shoulder.position+axis*along+pole*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
            shoulder.rotation=Quaternion.FromToRotation(elbow.position-shoulder.position,knee-shoulder.position)*shoulder.rotation;
            elbow.rotation=Quaternion.FromToRotation(hand.position-elbow.position,shoulder.position+axis*d-elbow.position)*elbow.rotation;
        }
        static void Rotate(Model.Rig r,Transform joint,Vector3 euler)
        {joint.localRotation=r.RestRot[joint]*Quaternion.Euler(euler);}
        public static void BoardPose(Model.Rig r,float kneel)
        {
            Rotate(r,r.ShoulderR,new Vector3(0,0,12*kneel));
            var blade=Vector3.Lerp(new Vector3(.60f,-.67f,.35f),new Vector3(.85f,.05f,.15f),kneel).normalized;
            r.HandR.rotation=Quaternion.FromToRotation(r.HandR.TransformDirection(BladeTip-Grip),r.Mount.TransformDirection(blade))*r.HandR.rotation;
            Rotate(r,r.HandL,new Vector3(0,-10,0));
        }
        public static void Pose(EntityVehicle v,Model.Rig r,float dt,float now,bool applySword=true)
        {
            if(!Rules.Complete(v))return;var s=Get(v);var move=Locomotion.Get(v);
            if(s.Swing&&now-s.Started>=Duration(s)*SwordMotion.WindEnd&&Mathf.Abs(s.Started-s.LastSound)>.2f){s.LastSound=s.Started;RobotAudio.OneShot(v,"sword",s.Heavy?.65f:.45f);}
            float want=now-s.LastCombat<AlertDelay?1:0;
            s.Alert=Mathf.MoveTowards(s.Alert,want,dt/(want>s.Alert?.5f:1f));
            s.GuardBlend=Mathf.MoveTowards(s.GuardBlend,s.Guarding?1:0,dt/.22f);
            float alert=s.Alert,guard=s.GuardBlend,boost=Rules.Complete(v)&&move.WingBlend>.01f?move.WingBlend:move.Blend;
            if(Flight.AirPose(move)){guard=0;s.GuardBlend=0;}
            // Low guard exposes the chest. Raised shield is a distinct, active action.
            var left=Vector3.Lerp(new Vector3(0,0,-5),new Vector3(-12,-8,-12),alert);
            left=Vector3.Lerp(left,new Vector3(-48,0,-12),guard);
            float elbowL=Mathf.Lerp(0,-10,alert)+guard*50;
            left=Vector3.Lerp(left,new Vector3(15,-10,-8),boost);
            Rotate(r,r.ShoulderL,left);Rotate(r,r.ElbowL,new Vector3(elbowL,0,0));
            ShieldArm(r,guard);
            Rotate(r,r.HandL,new Vector3(0,-10,0));
            var shieldNormal=r.Mount.TransformDirection(Vector3.Slerp(Vector3.left,new Vector3(-.2f,0,1).normalized,guard));
            var raised=Quaternion.LookRotation(shieldNormal,r.Mount.TransformDirection(new Vector3(-.4f,.92f,0)))*Quaternion.Inverse(Quaternion.LookRotation(Vector3.left,Vector3.up));
            r.HandL.rotation=Quaternion.Slerp(r.HandL.rotation,raised,guard);
            s.HeadYaw=Mathf.MoveTowards(s.HeadYaw,alert*s.AimYaw,dt*100);s.HeadPitch=Mathf.MoveTowards(s.HeadPitch,alert*s.AimPitch,dt*80);
            r.Head.rotation=r.Mount.rotation*Quaternion.Euler(-s.HeadPitch,s.HeadYaw,0);
            if(applySword)SwordMotion.Pose(v,r,s,now);
        }
        // Distance to the swept physical blade. Each target is charged only once per swing.
        public static float SegmentDistance(Vector3 p,Vector3 a,Vector3 b)
        {var d=b-a;float t=d.sqrMagnitude>.000001f?Mathf.Clamp01(Vector3.Dot(p-a,d)/d.sqrMagnitude):0;return Vector3.Distance(p,a+t*d);}
        public static bool SweptContact(Vector3 p,Vector3 a,Vector3 b,Vector3 c,Vector3 d,float radius)
        {int steps=Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(Vector3.Distance(a,c),Vector3.Distance(b,d))/.08f),1,64);for(int i=0;i<=steps;i++)if(SegmentDistance(p,Vector3.Lerp(a,c,i/(float)steps),Vector3.Lerp(b,d,i/(float)steps))<=radius)return true;return false;}
        static int contactSerial;
        public static void Interrupt(EntityVehicle v,float now)
        {var s=Get(v);s.RecoveryPoleAngle=SwordMotion.PoleAt(s,Locomotion.Get(v),now);var r=Model.GetRig(v);if(r!=null){s.RecoveryGrip=r.Torso.InverseTransformPoint(r.HandR.position)+r.TorsoBasePosition;s.RecoveryDirection=r.Torso.InverseTransformDirection(SwordMotion.Tip(r)-r.HandR.position).normalized;s.RecoveryNormal=r.Torso.InverseTransformDirection(r.HandR.TransformDirection(r.SwordRestNormal)).normalized;s.RecoveryShift=r.Torso.localPosition-r.ActionBaseTorsoPosition;var e=(Quaternion.Inverse(r.ActionBaseTorsoRotation)*r.Torso.localRotation).eulerAngles;s.RecoveryEuler=new Vector3(Mathf.DeltaAngle(0,e.x),Mathf.DeltaAngle(0,e.y),Mathf.DeltaAngle(0,e.z));}s.Blocked=true;s.BlockedAt=now;s.Charging=s.Queued=false;s.SuppressSword=true;}
        public static void Contacts(World world,EntityVehicle v,Model.Rig r,float now)
        {
            if(!Weapons.Server||!Rules.Complete(v))return;var s=Get(v);if(!s.Swing||s.Blocked||!Operator(s)||!GroundReady(v)){s.LastSweep=-1;return;}
            float from=s.LastSweep<0?s.Started:s.LastSweep,gap=now-from;
            if(gap>.4001f||(s.LastSweep>=0&&(v.position-s.PreviousPosition).sqrMagnitude>4)){Interrupt(v,now);s.LastSweep=now;return;}
            if(r.ContactJoints==null||r.ContactJoints.Length!=r.RestRot.Count){r.ContactJoints=new Transform[r.RestRot.Count];r.RestRot.Keys.CopyTo(r.ContactJoints,0);r.ContactRotations=new Quaternion[r.ContactJoints.Length];r.ContactPositions=new Vector3[r.ContactJoints.Length];}
            var joints=r.ContactJoints;var rotations=r.ContactRotations;var positions=r.ContactPositions;bool hadBase=r.ActionBaseReady;
            for(int i=0;i<joints.Length;i++){rotations[i]=joints[i].localRotation;positions[i]=joints[i].localPosition;}
            try{
                int steps=Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(0,gap)*60),1,24);
                SwordMotion.Pose(v,r,s,from);var previousRoot=SwordMotion.Root(r);var previousTip=SwordMotion.Tip(r);
                for(int sample=1;sample<=steps;sample++){
                    float time=Mathf.Lerp(from,now,sample/(float)steps),phase=(time-s.Started)/Duration(s);SwordMotion.Pose(v,r,s,time);var root=SwordMotion.Root(r);var tip=SwordMotion.Tip(r);
                    if(SwordMotion.DamagePhase(phase)){
                        Vector3 contact;if(SwordMotion.Environment(v,root,tip,out contact)||SwordMotion.Environment(v,r.HandR.position,r.HandR.TransformPoint(r.HiltAnchor),out contact)||SwordMotion.Environment(v,previousTip,tip,out contact)||SwordMotion.Environment(v,previousRoot,root,out contact)){
                            Interrupt(v,now);Weapons.Broadcast(v.entityId,++contactSerial,CombatFeedback.SwordContact,contact+Origin.position,(previousTip-tip).normalized,0,-(Mathf.Max(1,s.AttackSerial)+(s.Heavy?.5f:0)));break;
                        }
                        foreach(var e in world.Entities.list){var target=e as EntityAlive;if(!Weapons.Hostile(target)||s.Hit.Contains(target.entityId)||(target.position-v.position).sqrMagnitude>36)continue;
                            var center=target.GetPosition()+Vector3.up*.8f;if(!SweptContact(center,previousRoot+Origin.position,previousTip+Origin.position,root+Origin.position,tip+Origin.position,.5f))continue;
                            var origin=v.position+Vector3.up*1.5f;var delta=center-origin;
                            if(Weapons.Trace(v,origin,delta.normalized,Mathf.Max(.01f,delta.magnitude-.3f),out var hit)&&ItemActionAttack.FindHitEntity(hit)!=target)continue;
                            s.Hit.Add(target.entityId);int damage=s.Heavy?135000:90000,packets=Weapons.DamagePackets(damage),before=target.Health;
                            var source=new DamageSourceEntity(EnumDamageSource.External,EnumDamageTypes.Bashing,s.Actor,delta.normalized){AttackingItem=ItemClass.GetItem(Rules.BeamAmmo,false),canHitSpecialBodyParts=false,DismemberChance=0};source.SetIgnoreConsecutiveDamages(false);
                            for(int i=0;i<packets&&!target.IsDead();i++)target.DamageEntity(source,damage/packets,false,0);
                            int actual=Mathf.Max(0,before-target.Health);if(actual>0)Weapons.Broadcast(v.entityId,++contactSerial,CombatFeedback.SwordContact,center,-delta.normalized,actual,s.AttackSerial+(s.Heavy?.5f:0));
                        }
                    }
                    previousRoot=root;previousTip=tip;
                }
                s.PreviousRoot=previousRoot+Origin.position;s.PreviousTip=previousTip+Origin.position;s.PreviousPosition=v.position;s.LastSweep=now;
            }finally{for(int i=0;i<joints.Length;i++){joints[i].localRotation=rotations[i];joints[i].localPosition=positions[i];}r.ActionBaseReady=hadBase;}
        }
        public static void Clear(){states.Clear();contactSerial=0;input=255;nextInput=0;}
    }
}
