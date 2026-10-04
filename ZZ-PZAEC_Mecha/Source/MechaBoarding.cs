using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace PZAEC.Mecha
{
    public static class Boarding
    {
        public const byte BoardEvent=8;
        sealed class Show {public EntityVehicle Vehicle;public int Actor,Sequence,AudioSequence,AudioMask;public float Started,Duration,NextSync;public bool Exit,Full,Deferred,Committed,AttachedSeen;}
        static readonly Dictionary<int,Show> shows=new Dictionary<int,Show>();
        static readonly Dictionary<int,int> completed=new Dictionary<int,int>();
        public static string Notice;static float noticeUntil;
        public static string CurrentNotice {get{return Time.time<noticeUntil?Notice:null;}}
        static int cameraCockpit=-1;
        static Entity exitActor;static Vector3 exitPoint;
        static int sequence;static bool suppressJump,committing;
        public static void Install(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(EntityVehicle),"EnterVehicle"),prefix:new HarmonyMethod(typeof(Boarding),nameof(EnterObserve)));
            h.Patch(AccessTools.Method(typeof(Entity),"FindValidExitPosition"),prefix:new HarmonyMethod(typeof(Boarding),nameof(ExitPosition)));
            // Delay the request before it sends a detach packet or changes player state.
            h.Patch(AccessTools.Method(typeof(Entity),"SendDetach"),prefix:new HarmonyMethod(typeof(Boarding),nameof(ExitObserve)));
        }
        static bool ExitPosition(Entity __instance,ref AttachedToEntitySlotExit __result)
        {if(committing&&__instance==exitActor){__result=new AttachedToEntitySlotExit{position=exitPoint,rotation=__instance.rotation};return false;}
            var v=__instance.AttachedToEntity as EntityVehicle;Show s;
            if(v!=null&&Rules.Complete(v)&&shows.TryGetValue(v.entityId,out s)&&s.Exit&&s.Actor==__instance.entityId&&Time.time-s.Started>=Transfer(s)&&Ceremony.FindExit(v,__instance,out var point)){__result=new AttachedToEntitySlotExit{position=point,rotation=__instance.rotation};return false;}return true;}
        static float Transfer(Show s){return Rules.Complete(s.Vehicle)?(s.Full?(s.Exit?Ceremony.ExitTransfer:Ceremony.EnterTransfer):.35f):TransferAt(s.Exit,s.Full);}
        static bool Safe(EntityVehicle v)
        {
            var rb=v.vehicleRB;if(rb==null||rb.velocity.sqrMagnitude>.16f||Vector3.Dot(rb.rotation*Vector3.up,Vector3.up)<.95f)return false;
            if(v.GetWheelsOnGround()==0&&!(Rules.Complete(v)&&Flight.HullSupported(rb))&&!Weapons.Trace(v,v.position+Vector3.up*.2f,Vector3.down,.9f,out var ground))return false;
            var rig=Model.GetRig(v);if(rig==null)return false;
            var origin=v.position+Vector3.up*.3f;
            return !Weapons.Trace(v,origin,Vector3.up,3.6f,out var hit)&&!Locomotion.Get(v).HoverOn&&!Locomotion.Get(v).Boost&&!Flight.AirPose(Locomotion.Get(v))&&Locomotion.Get(v).WingBlend<.1f;
        }
        static bool EnterObserve(EntityVehicle __instance,EntityAlive _entity)
        {
            if(committing||!Rules.BoardingEnabled||!Weapons.IsMecha(__instance)||!(_entity is EntityPlayerLocal)||_entity.IsDead())return true;
            if(Active(__instance))return false;
            if(_entity.AttachedToEntity!=null||__instance.GetAttached(0)!=null)return true;
            BeginDeferred(__instance,_entity,false);return false;
        }
        static bool ExitObserve(Entity __instance)
        {
            var v=__instance.AttachedToEntity as EntityVehicle;
            if(committing||!(__instance is EntityPlayerLocal)||!Weapons.IsMecha(v))return true;
            if(__instance.IsDead()||v.IsDead()||(Rules.Complete(v)?!Ceremony.Stable(v)||Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift):!Safe(v))){Finish(v,true);return true;}
            if(Active(v))return false;
            BeginDeferred(v,(EntityAlive)__instance,true);return false;
        }
        static void BeginDeferred(EntityVehicle v,EntityAlive actor,bool exit)
        {
            Start(v,actor.entityId,exit,Rules.Complete(v)?Ceremony.Space(v,actor):Safe(v));shows[v.entityId].Deferred=true;
            if(!Weapons.Server)Weapons.SendLocalIntent(v,Weapons.BoardControl,new Vector3(exit?1:0,0,0),Vector3.zero);
        }
        public static bool Owned(EntityVehicle v,int actor)
        {Show s;return v!=null&&shows.TryGetValue(v.entityId,out s)&&s.Actor==actor;}
        public static void Request(World world,EntityVehicle v,int actor,int mode)
        {
            var p=world.GetEntity(actor) as EntityPlayer;if(p==null||p.IsDead()||v.IsDead())return;
            if(mode==2){if(Owned(v,actor))Finish(v,true);return;}
            if(mode!=0&&mode!=1)return;
            if(Active(v))return;
            if(mode==0&&(p.AttachedToEntity!=null||v.GetAttached(0)!=null||(p.position-v.position).sqrMagnitude>36))return;
            if(mode==1&&v.GetAttached(0)!=p)return;
            Start(v,actor,mode==1,Rules.Complete(v)?Ceremony.Space(v,p):Safe(v));
        }
        public static float TransferAt(bool exit,bool full){return full?(exit?1.4f:1.6f):.35f;}
        static void Commit(Show s,EntityAlive actor)
        {
            if(!s.Deferred||s.Committed||actor==null)return;
            if(s.Exit&&Rules.Complete(s.Vehicle)&&!Ceremony.FindExit(s.Vehicle,actor,out exitPoint)){Notice="舱外没有安全落脚点，请移动机甲后重试（Shift + 下机可紧急离舱）";noticeUntil=Time.time+4;if(!Weapons.Server)Weapons.SendLocalIntent(s.Vehicle,Weapons.BoardControl,new Vector3(2,0,0),Vector3.zero);Finish(s.Vehicle,true);return;}
            exitActor=s.Exit&&Rules.Complete(s.Vehicle)?actor:null;
            s.Committed=true;committing=true;
            try{
                if(s.Exit){if(actor.AttachedToEntity==s.Vehicle){MechaArmor.SafeDismount(actor,s.Vehicle);actor.SendDetach();}}
                else if(actor.AttachedToEntity==null&&s.Vehicle.GetAttached(0)==null)s.Vehicle.EnterVehicle(actor);
            }finally{committing=false;exitActor=null;}
        }
        static void Start(EntityVehicle v,int actor,bool exit,bool full)
        {
            var s=new Show{Vehicle=v,Actor=actor,Exit=exit,Full=full,Duration=Rules.Complete(v)?(full?(exit?Ceremony.ExitSeconds:Ceremony.EnterSeconds):.5f):exit?2f:full?4f:.35f,Started=Time.time,Sequence=Weapons.Server?++sequence:0,AudioSequence=RobotAudio.NextPresentationSerial()};
            shows[v.entityId]=s;if(Rules.Complete(v))Samurai.Stop(v);Sounds(s,0,false);if(Weapons.Server)Publish(s,false);
        }
        static void Publish(Show s,bool done)
        {Weapons.Broadcast(s.Vehicle.entityId,s.Sequence,BoardEvent,new Vector3(s.Actor,s.Exit?1:0,s.Full?1:0),Vector3.zero,Time.time-s.Started,done?0:s.Duration);}
        public static void ReceiveSnapshot(World world,int id,int seq,Vector3 a,Vector3 b,float age,float duration)
        {
            if(Weapons.Server||!Weapons.Finite(age)||!Weapons.Finite(duration)||age<0||duration<0||duration>8)return;
            var v=world.GetEntity(id) as EntityVehicle;if(!Weapons.IsMecha(v))return;
            int old;if(completed.TryGetValue(id,out old)&&seq<=old)return;
            Show s;if(shows.TryGetValue(id,out s)&&seq<s.Sequence)return;
            if(duration==0){if(s!=null&&s.Sequence==seq&&age>=s.Duration-.02f)Sounds(s,s.Duration,false);completed[id]=seq;Finish(v,false);return;}
            bool fresh=false;
            if(s==null||s.Sequence!=seq){var predicted=s!=null&&s.Deferred&&s.Actor==(int)a.x&&s.Exit==(a.y>.5f)?s:null;s=predicted??new Show{Vehicle=v,AudioSequence=RobotAudio.NextPresentationSerial()};fresh=predicted==null;s.Actor=(int)a.x;s.Exit=a.y>.5f;s.Full=a.z>.5f;s.Sequence=seq;shows[id]=s;}
            s.Started=Time.time-age;s.Duration=duration;
            // A late observer begins at the current physical pose. Its first
            // snapshot consumes past cues silently rather than replaying them.
            if(fresh)Sounds(s,age,age>.3f);
        }
        static void Cue(Show s,float age,float at,int bit,string cue,float volume,bool silent,int foot=-1)
        {
            int flag=1<<bit;if(age<at||(s.AudioMask&flag)!=0)return;s.AudioMask|=flag;if(silent)return;
            if(foot>=0){var rig=Model.GetRig(s.Vehicle);var joint=rig!=null?(foot==0?rig.FootL:rig.FootR):null;RobotAudio.ContactEvent(s.Vehicle,cue,s.AudioSequence,joint!=null?joint.position+Origin.position:s.Vehicle.position,volume);}
            else RobotAudio.Event(s.Vehicle,cue,s.AudioSequence,volume);
        }
        static void Sounds(Show s,float age,bool silent)
        {
            bool complete=Rules.Complete(s.Vehicle);
            Cue(s,age,s.Full&&complete?.45f:0,0,"entry-brace",.48f,silent);
            if(s.Full)
            {
                if(complete){Cue(s,age,s.Exit?.75f:1.05f,1,"step-left",.72f,silent,0);Cue(s,age,s.Exit?1.05f:1.5f,2,"step-right",.78f,silent,1);}
                Cue(s,age,complete?Ceremony.KneelEnd(s.Exit):.9f,3,"kneel-lock",.62f,silent);
                Cue(s,age,complete?Ceremony.OpenAt(s.Exit)+(s.Exit?.7f:.75f):s.Exit?1.2f:1.6f,4,"hatch-open",.52f,silent);
                Cue(s,age,complete?Ceremony.CloseAt(s.Exit)+.65f:s.Exit?2:2.5f,5,"hatch-close",.68f,silent);
                Cue(s,age,complete?(s.Exit?5.75f:6.6f):s.Duration-.1f,6,"stand-lock",.7f,silent);
            }
            if(!s.Exit)Cue(s,age,s.Duration-.08f,7,"ready",.46f,silent);
        }
        public static bool Active(EntityVehicle v){return v!=null&&shows.ContainsKey(v.entityId);}
        public static bool SwordTarget(EntityVehicle v,out Vector3 grip,out Vector3 direction,out Vector3 normal)
        {grip=direction=normal=Vector3.zero;Show s;if(v==null||!Rules.Complete(v)||!shows.TryGetValue(v.entityId,out s))return false;Ceremony.SwordTarget(s.Exit,s.Full?Time.time-s.Started:0,out grip,out direction,out normal);return true;}
        public static Vector3 FootTarget(EntityVehicle v,Model.Rig rig,int side,Vector3 home)
        {Show s;return v!=null&&Rules.Complete(v)&&shows.TryGetValue(v.entityId,out s)&&s.Full?Ceremony.FootTarget(rig,s.Exit,Time.time-s.Started,side,home):home+(side==0?-rig.Mount.forward*.85f:rig.Mount.forward*.3f)*Kneel(v);}
        public static float Total(bool exit){return exit?2:4;}
        static float Ease(float t){t=Mathf.Clamp01(t);return t*t*(3-2*t);}
        public static float Kneel(EntityVehicle v)
        {Show s;if(v==null||!shows.TryGetValue(v.entityId,out s)||!s.Full)return 0;float t=Time.time-s.Started;
            if(Rules.Complete(v))return Ceremony.Kneel(s.Exit,t);
            return s.Exit?(t<.9f?Ease(t/.9f):t<1.4f?1:1-Ease((t-1.4f)/.6f)):t<.9f?Ease(t/.9f):t<2.5f?1:1-Ease((t-2.5f)/1.5f);}
        public static float Hatch(EntityVehicle v)
        {Show s;if(v==null||!shows.TryGetValue(v.entityId,out s))return 0;float t=Time.time-s.Started;
            if(Rules.Complete(v))return Ceremony.Hatch(s.Exit,s.Full,t);if(!s.Full)return 0;
            return s.Exit?(t<.6f?0:t<1.2f?Ease((t-.6f)/.6f):t<1.4f?1:1-Ease((t-1.4f)/.6f)):t<.9f?0:t<1.6f?Ease((t-.9f)/.7f):t<2.1f?1:1-Ease((t-2.1f)/.4f);}
        public static bool ApplyPose(Model.Rig rig,EntityVehicle v)
        {Show s;if(!shows.TryGetValue(v.entityId,out s))return false;
            if(Rules.Complete(v)){var actor=GameManager.Instance.World.GetEntity(s.Actor);Ceremony.Pose(v,rig,s.Exit,s.Full,Time.time-s.Started,actor!=null?actor.position:v.position+Weapons.BodyRotation(v)*Vector3.forward);return true;}
            float k=Kneel(v);rig.Torso.localPosition=rig.TorsoBasePosition+new Vector3(0,-k*.9f,0);
            rig.Torso.localRotation=rig.RestRot[rig.Torso]*Quaternion.Euler(k*8,0,0);rig.Head.localRotation=rig.RestRot[rig.Head]*Quaternion.Euler(k*12,0,0);
            rig.ShoulderL.localRotation=rig.RestRot[rig.ShoulderL]*Quaternion.Euler(0,0,-k*8);rig.ShoulderR.localRotation=rig.RestRot[rig.ShoulderR]*Quaternion.Euler(0,0,k*8);
            return true;}
        public static void Update(World world)
        {
            if(!Input.GetKey(KeyCode.Space))suppressJump=false;
            var finish=new List<EntityVehicle>();
            foreach(var s in new List<Show>(shows.Values))
            {
                var actor=world.GetEntity(s.Actor) as EntityAlive;
                if(s.Vehicle==null||world.GetEntity(s.Vehicle.entityId)!=s.Vehicle||actor==null||actor.IsDead()||s.Vehicle.IsDead()){finish.Add(s.Vehicle);continue;}
                float age=Time.time-s.Started;
                bool attached=actor.AttachedToEntity==s.Vehicle;
                if(attached)s.AttachedSeen=true;
                if(!s.Exit&&((s.AttachedSeen&&!attached)||(!attached&&((actor.position-s.Vehicle.position).sqrMagnitude>36||actor.AttachedToEntity!=null)))){finish.Add(s.Vehicle);continue;}
                if(s.Deferred&&!s.Committed){
                    if(!s.Exit&&!Weapons.UIReady(actor as EntityPlayerLocal)){finish.Add(s.Vehicle);continue;}
                    if(Input.GetKeyDown(KeyCode.Space)){suppressJump=true;Commit(s,actor);if(!Weapons.Server)Weapons.SendLocalIntent(s.Vehicle,Weapons.SkipBoard,Vector3.zero,Vector3.zero);finish.Add(s.Vehicle);continue;}
                    if(age>=Transfer(s))Commit(s,actor);
                }
                if(!Active(s.Vehicle))continue;
                if(Rules.Complete(s.Vehicle)&&s.Exit&&s.Full&&age>=Ceremony.ExitHold&&Ceremony.NearDoor(s.Vehicle,actor)){s.Started=Time.time-Ceremony.ExitHold;age=Ceremony.ExitHold;}
                // Consume all crossed stages before finishing, including a
                // last frame that jumps beyond the ceremony's duration.
                Sounds(s,age,false);
                if(age>=s.Duration){finish.Add(s.Vehicle);continue;}
                if(Weapons.Server&&Time.time>=s.NextSync){s.NextSync=Time.time+.2f;Publish(s,false);}
            }
            foreach(var v in finish){Show s;if(v!=null&&shows.TryGetValue(v.entityId,out s)&&s.Deferred&&!s.Committed&&!Weapons.Server)Weapons.SendLocalIntent(v,Weapons.BoardControl,new Vector3(2,0,0),Vector3.zero);Finish(v,true);}
        }
        static void Finish(EntityVehicle v,bool publish)
        {if(v==null)return;Show s;if(!shows.TryGetValue(v.entityId,out s))return;
            shows.Remove(v.entityId);completed[v.entityId]=s.Sequence;
            RobotAudio.StopChannels(v);
            var rig=Model.GetRig(v);if(rig!=null)rig.ResetPose();
            if(publish&&Weapons.Server)Publish(s,true);}
        public static void Skip(EntityVehicle v){CompletePending(v);Finish(v,true);}
        static void CompletePending(EntityVehicle v){Show s;if(v!=null&&shows.TryGetValue(v.entityId,out s))Commit(s,GameManager.Instance.World.GetEntity(s.Actor) as EntityAlive);}
        public static void SkipLocal(EntityVehicle v){suppressJump=true;CompletePending(v);Finish(v,false);}
        public static bool FilterJump(bool held){return held&&!suppressJump;}
        public static bool Describe(EntityPlayerLocal player,out float progress,out bool dismount,out bool rider)
        {progress=0;dismount=rider=false;if(player==null)return false;foreach(var s in shows.Values)if(s.Actor==player.entityId){progress=Mathf.Clamp01((Time.time-s.Started)/s.Duration);dismount=s.Exit;rider=!s.Exit||player.AttachedToEntity==s.Vehicle;return true;}return false;}
        public static bool CameraRide(out Vector3 position,out Quaternion rotation,out float fov)
        {
            cameraCockpit=-1;position=Vector3.zero;rotation=Quaternion.identity;fov=-1;
            var world=GameManager.Instance!=null?GameManager.Instance.World:null;var player=world!=null?world.GetPrimaryPlayer() as EntityPlayerLocal:null;
            if(player==null)return false;
            foreach(var s in shows.Values)
            {
                if(s.Actor!=player.entityId||(s.Exit&&player.AttachedToEntity!=s.Vehicle))continue;
                var rig=Model.GetRig(s.Vehicle);if(rig==null)return false;
                float age=Time.time-s.Started;if(Rules.Complete(s.Vehicle)&&s.Full&&!s.Exit&&age<.6f)return false;var target=rig.Torso.position+Vector3.up*.5f;var eye=rig.Head.position+rig.Mount.forward*.18f;
                var outside=rig.Mount.TransformPoint(new Vector3(Rules.Complete(s.Vehicle)?-2.6f:2.6f,2.4f,3.6f));float t=s.Exit?0:s.Full?Rules.Complete(s.Vehicle)?Ease((age-Ceremony.EnterTransfer)/.9f):Ease((age-1.6f)/.9f):1;
                position=Vector3.Lerp(outside,eye,t);rotation=Quaternion.Slerp(Quaternion.LookRotation(target-outside,Vector3.up),rig.Mount.rotation,t);fov=Mathf.Lerp(55,player.GetCameraFOV(),t);
                var from=target+Origin.position;var delta=position-target;
                if(Weapons.Trace(s.Vehicle,from,delta.normalized,delta.magnitude,out var obstruction)){if(player.AttachedToEntity!=s.Vehicle)return false;cameraCockpit=s.Vehicle.entityId;position=eye;rotation=rig.Mount.rotation;}
                var travel=position-player.playerCamera.transform.position;
                if(travel.sqrMagnitude>.001f&&Weapons.Trace(s.Vehicle,player.playerCamera.transform.position+Origin.position,travel.normalized,travel.magnitude,out var block)){if(player.AttachedToEntity!=s.Vehicle)return false;cameraCockpit=s.Vehicle.entityId;position=eye;rotation=rig.Mount.rotation;}
                return true;
            }
            return false;
        }
        public static bool CockpitCamera(EntityVehicle v){Show s;return v!=null&&shows.TryGetValue(v.entityId,out s)&&(cameraCockpit==v.entityId||!s.Exit&&Time.time-s.Started>=(Rules.Complete(v)?Ceremony.EnterTransfer+.65f:2.3f));}
        public static void Clear(){foreach(var s in shows.Values)RobotAudio.StopChannels(s.Vehicle);shows.Clear();completed.Clear();suppressJump=committing=false;exitActor=null;cameraCockpit=-1;Notice=null;noticeUntil=0;}
    }
}
