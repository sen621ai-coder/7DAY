using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace PZAEC.Mecha
{
    public static class Boarding
    {
        public const byte BoardEvent=8;
        sealed class Show {public EntityVehicle Vehicle;public int Actor,Sequence;public float Started,Duration,NextSync;public bool Exit,Full,Opened,Closed,Ready,Deferred,Committed,AttachedSeen;}
        static readonly Dictionary<int,Show> shows=new Dictionary<int,Show>();
        static readonly Dictionary<int,int> completed=new Dictionary<int,int>();
        static int sequence;static bool suppressJump,committing;
        public static void Install(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(EntityVehicle),"EnterVehicle"),prefix:new HarmonyMethod(typeof(Boarding),nameof(EnterObserve)));
            // Delay the request before it sends a detach packet or changes player state.
            h.Patch(AccessTools.Method(typeof(Entity),"SendDetach"),prefix:new HarmonyMethod(typeof(Boarding),nameof(ExitObserve)));
        }
        static bool Safe(EntityVehicle v)
        {
            var rb=v.vehicleRB;if(rb==null||rb.velocity.sqrMagnitude>.16f||Vector3.Dot(rb.rotation*Vector3.up,Vector3.up)<.95f)return false;
            if(v.GetWheelsOnGround()==0&&!Weapons.Trace(v,v.position+Vector3.up*.2f,Vector3.down,.9f,out var ground))return false;
            var rig=Model.GetRig(v);if(rig==null)return false;
            var origin=v.position+Vector3.up*.3f;
            return !Weapons.Trace(v,origin,Vector3.up,3.6f,out var hit)&&!Locomotion.Get(v).HoverOn&&!Locomotion.Get(v).Boost;
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
            if(__instance.IsDead()||v.IsDead()||!Safe(v)){Finish(v,true);return true;}
            if(Active(v))return false;
            BeginDeferred(v,(EntityAlive)__instance,true);return false;
        }
        static void BeginDeferred(EntityVehicle v,EntityAlive actor,bool exit)
        {
            Start(v,actor.entityId,exit,Safe(v));shows[v.entityId].Deferred=true;
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
            Start(v,actor,mode==1,Safe(v));
        }
        public static float TransferAt(bool exit,bool full){return full?(exit?1.4f:1.6f):.35f;}
        static void Commit(Show s,EntityAlive actor)
        {
            if(!s.Deferred||s.Committed||actor==null)return;
            s.Committed=true;committing=true;
            try{
                if(s.Exit){if(actor.AttachedToEntity==s.Vehicle){MechaArmor.SafeDismount(actor,s.Vehicle);actor.SendDetach();}}
                else if(actor.AttachedToEntity==null&&s.Vehicle.GetAttached(0)==null)s.Vehicle.EnterVehicle(actor);
            }finally{committing=false;}
        }
        static void Start(EntityVehicle v,int actor,bool exit,bool full)
        {
            var s=new Show{Vehicle=v,Actor=actor,Exit=exit,Full=full,Duration=exit?2f:full?4f:.35f,Started=Time.time,Sequence=Weapons.Server?++sequence:0};
            shows[v.entityId]=s;if(Weapons.Server)Publish(s,false);
        }
        static void Publish(Show s,bool done)
        {Weapons.Broadcast(s.Vehicle.entityId,s.Sequence,BoardEvent,new Vector3(s.Actor,s.Exit?1:0,s.Full?1:0),Vector3.zero,Time.time-s.Started,done?0:s.Duration);}
        public static void ReceiveSnapshot(World world,int id,int seq,Vector3 a,Vector3 b,float age,float duration)
        {
            if(Weapons.Server||!Weapons.Finite(age)||!Weapons.Finite(duration)||age<0||duration<0||duration>4)return;
            var v=world.GetEntity(id) as EntityVehicle;if(!Weapons.IsMecha(v))return;
            int old;if(completed.TryGetValue(id,out old)&&seq<=old)return;
            Show s;if(shows.TryGetValue(id,out s)&&seq<s.Sequence)return;
            if(duration==0){completed[id]=seq;Finish(v,false);return;}
            if(s==null||s.Sequence!=seq){var predicted=s!=null&&s.Deferred&&s.Actor==(int)a.x&&s.Exit==(a.y>.5f)?s:null;s=predicted??new Show{Vehicle=v};s.Actor=(int)a.x;s.Exit=a.y>.5f;s.Full=a.z>.5f;s.Sequence=seq;shows[id]=s;}
            s.Started=Time.time-age;s.Duration=duration;
        }
        public static bool Active(EntityVehicle v){return v!=null&&shows.ContainsKey(v.entityId);}
        public static float Total(bool exit){return exit?2:4;}
        static float Ease(float t){t=Mathf.Clamp01(t);return t*t*(3-2*t);}
        public static float Kneel(EntityVehicle v)
        {Show s;if(v==null||!shows.TryGetValue(v.entityId,out s)||!s.Full)return 0;float t=Time.time-s.Started;
            return s.Exit?(t<.9f?Ease(t/.9f):t<1.4f?1:1-Ease((t-1.4f)/.6f)):t<.9f?Ease(t/.9f):t<2.5f?1:1-Ease((t-2.5f)/1.5f);}
        public static float Hatch(EntityVehicle v)
        {Show s;if(v==null||!shows.TryGetValue(v.entityId,out s)||!s.Full)return 0;float t=Time.time-s.Started;
            return s.Exit?(t<.6f?0:t<1.2f?Ease((t-.6f)/.6f):t<1.4f?1:1-Ease((t-1.4f)/.6f)):t<.9f?0:t<1.6f?Ease((t-.9f)/.7f):t<2.1f?1:1-Ease((t-2.1f)/.4f);}
        public static bool ApplyPose(Model.Rig rig,EntityVehicle v)
        {Show s;if(!shows.TryGetValue(v.entityId,out s))return false;
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
                    if(age>=TransferAt(s.Exit,s.Full))Commit(s,actor);
                }
                if(age>=s.Duration){finish.Add(s.Vehicle);continue;}
                if(s.Full&&age>=.9f&&!s.Opened){s.Opened=true;RobotAudio.OneShot(s.Vehicle,"hatch-open",.7f);}
                if(s.Full&&age>=2.1f&&!s.Closed){s.Closed=true;RobotAudio.OneShot(s.Vehicle,"hatch-close",.7f);}
                if(!s.Exit&&age>=s.Duration-.3f&&!s.Ready){s.Ready=true;RobotAudio.OneShot(s.Vehicle,"ready",.6f);}
                if(Weapons.Server&&Time.time>=s.NextSync){s.NextSync=Time.time+.2f;Publish(s,false);}
            }
            foreach(var v in finish){Show s;if(v!=null&&shows.TryGetValue(v.entityId,out s)&&s.Deferred&&!s.Committed&&!Weapons.Server)Weapons.SendLocalIntent(v,Weapons.BoardControl,new Vector3(2,0,0),Vector3.zero);Finish(v,true);}
        }
        static void Finish(EntityVehicle v,bool publish)
        {if(v==null)return;Show s;if(!shows.TryGetValue(v.entityId,out s))return;
            shows.Remove(v.entityId);completed[v.entityId]=s.Sequence;
            var rig=Model.GetRig(v);if(rig!=null)rig.ResetPose();
            if(publish&&Weapons.Server)Publish(s,true);}
        public static void Skip(EntityVehicle v){CompletePending(v);Finish(v,true);}
        static void CompletePending(EntityVehicle v){Show s;if(v!=null&&shows.TryGetValue(v.entityId,out s))Commit(s,GameManager.Instance.World.GetEntity(s.Actor) as EntityAlive);}
        public static void SkipLocal(EntityVehicle v){suppressJump=true;CompletePending(v);Finish(v,false);}
        public static bool FilterJump(bool held){return held&&!suppressJump;}
        public static bool Describe(EntityPlayerLocal player,out float progress,out bool dismount,out bool rider)
        {progress=0;dismount=rider=false;if(player==null)return false;foreach(var s in shows.Values)if(s.Actor==player.entityId){progress=Mathf.Clamp01((Time.time-s.Started)/s.Duration);dismount=s.Exit;rider=true;return true;}return false;}
        public static bool CameraRide(out Vector3 position,out Quaternion rotation,out float fov)
        {
            position=Vector3.zero;rotation=Quaternion.identity;fov=-1;
            var world=GameManager.Instance!=null?GameManager.Instance.World:null;var player=world!=null?world.GetPrimaryPlayer() as EntityPlayerLocal:null;
            if(player==null)return false;
            foreach(var s in shows.Values)
            {
                if(s.Actor!=player.entityId||(s.Exit&&player.AttachedToEntity!=s.Vehicle))continue;
                var rig=Model.GetRig(s.Vehicle);if(rig==null)return false;
                float age=Time.time-s.Started;var target=rig.Torso.position+Vector3.up*.5f;var eye=rig.Head.position+rig.Mount.forward*.18f;
                var outside=rig.Mount.TransformPoint(new Vector3(2.6f,2.4f,3.6f));float t=s.Exit?0:s.Full?Ease((age-1.6f)/.9f):1;
                position=Vector3.Lerp(outside,eye,t);rotation=Quaternion.Slerp(Quaternion.LookRotation(target-outside,Vector3.up),rig.Mount.rotation,t);fov=Mathf.Lerp(55,player.GetCameraFOV(),t);
                var from=target+Origin.position;var delta=position-target;
                if(Weapons.Trace(s.Vehicle,from,delta.normalized,delta.magnitude,out var obstruction)){position=eye;rotation=rig.Mount.rotation;}
                var travel=position-player.playerCamera.transform.position;
                if(travel.sqrMagnitude>.001f&&Weapons.Trace(s.Vehicle,player.playerCamera.transform.position+Origin.position,travel.normalized,travel.magnitude,out var block)){position=eye;rotation=rig.Mount.rotation;}
                return true;
            }
            return false;
        }
        public static bool CockpitCamera(EntityVehicle v){Show s;return v!=null&&shows.TryGetValue(v.entityId,out s)&&!s.Exit&&Time.time-s.Started>=2.3f;}
        public static void Clear(){shows.Clear();completed.Clear();suppressJump=committing=false;}
    }
}
