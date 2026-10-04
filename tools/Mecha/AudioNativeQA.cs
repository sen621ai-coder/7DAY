using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using PZAEC.Mecha;

// Compiled only into the isolated QA DLL. No InitMod / game launch of its own.
public static class MechaAudioQA
{
    static readonly List<string> results=new List<string>();
    public static int Failures {get;private set;}
    public static string[] Results(){return results.ToArray();}
    static void Check(string name,bool ok){results.Add((ok?"PASS ":"FAIL ")+name);if(!ok)Failures++;}
    static FieldInfo Field(object o,string name){return AccessTools.Field(o.GetType(),name);}
    static object Get(object o,string name){return Field(o,name).GetValue(o);}
    static void Set(object o,string name,object value){Field(o,name).SetValue(o,value);}
    static IDictionary Map(Type type,string name){return (IDictionary)AccessTools.Field(type,name).GetValue(null);}
    static object Voice(EntityVehicle v){return AccessTools.Method(typeof(RobotAudio),"Get").Invoke(null,new object[]{v});}
    static bool Audible(ref bool __result){__result=true;return false;}
    static bool NoBroadcast(){return false;}
    static bool Operator(ref bool __result){__result=true;return false;}
    static bool GroundReady(ref bool __result){__result=true;return false;}
    static bool Client(ref bool __result){__result=false;return false;}
    sealed class Fields
    {
        readonly object target;readonly Dictionary<FieldInfo,object> values=new Dictionary<FieldInfo,object>();
        readonly int[] hits;
        public Fields(object value)
        {
            target=value;
            foreach(var f in value.GetType().GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
            {var v=f.GetValue(value);values[f]=v is Array?((Array)v).Clone():v;}
            var hit=value as Samurai.State;if(hit!=null)hits=hit.Hit.ToArray();
        }
        public void Restore(){foreach(var p in values)if(!p.Key.IsInitOnly)p.Key.SetValue(target,p.Value);var s=target as Samurai.State;if(s!=null&&hits!=null){s.Hit.Clear();foreach(int id in hits)s.Hit.Add(id);}}
    }
    static Dictionary<object,object> SaveMap(IDictionary map){var saved=new Dictionary<object,object>();foreach(DictionaryEntry e in map)saved.Add(e.Key,e.Value);return saved;}
    static void RestoreMap(IDictionary map,Dictionary<object,object> saved){map.Clear();foreach(var p in saved)map.Add(p.Key,p.Value);}
    sealed class Pose
    {
        readonly Transform[] joints;readonly Vector3[] positions,scales;readonly Quaternion[] rotations;
        public Pose(Model.Rig rig){joints=rig.Mount.GetComponentsInChildren<Transform>(true);positions=joints.Select(t=>t.localPosition).ToArray();rotations=joints.Select(t=>t.localRotation).ToArray();scales=joints.Select(t=>t.localScale).ToArray();}
        public void Restore(){for(int i=0;i<joints.Length;i++)if(joints[i]!=null){joints[i].localPosition=positions[i];joints[i].localRotation=rotations[i];joints[i].localScale=scales[i];}}
    }
    static void ResetGround(Locomotion.MoveState move){move.Grounded=true;move.HoverOn=move.Boost=false;move.FlightMode=Flight.Phase.Ground;move.Blend=move.WingBlend=move.VisualForward=0;}
    static int Count(EntityVehicle v,string cue){return RobotAudio.RecentCues().Count(c=>c.Vehicle==v.entityId&&c.Cue==cue);}
    static bool Quiet(AudioSource a){return a.volume==0&&!a.isPlaying;}
    public static void Run(World world,EntityVehicle v)
    {
        string variant=Rules.Complete(v)?"complete":"prototype";var rig=Model.GetRig(v);var move=Locomotion.Get(v);var combat=Rules.Complete(v)?Samurai.Get(v):null;
        var pose=new Pose(rig);var moveSaved=new Fields(move);var combatSaved=combat!=null?new Fields(combat):null;var weapon=Weapons.GetState(v);var weaponSaved=new Fields(weapon);
        var shows=Map(typeof(Boarding),"shows");var showsSaved=SaveMap(shows);var samurai=Map(typeof(Samurai),"states");var samuraiSaved=SaveMap(samurai);
        var completed=Map(typeof(Boarding),"completed");var completedSaved=SaveMap(completed);var gates=Map(typeof(RobotAudio),"gates");var gatesSaved=SaveMap(gates);
        var boardingScalars=typeof(Boarding).GetFields(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).Where(f=>!f.IsInitOnly&&!f.IsLiteral&&(f.FieldType.IsValueType||f.FieldType==typeof(string))).ToDictionary(f=>f,f=>f.GetValue(null));
        var samuraiScalars=typeof(Samurai).GetFields(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).Where(f=>!f.IsInitOnly&&!f.IsLiteral&&(f.FieldType.IsValueType||f.FieldType==typeof(string))).ToDictionary(f=>f,f=>f.GetValue(null));
        bool driver=v.hasDriver,engine=v.IsEngineRunning;float fuelModifier=EntityVehicle.VehicleFuelUsageModifier;
        var voice=Voice(v);var voiceSaved=new Fields(voice);var sourceNames=new[]{"Servo","Boost","Charge","Shot","Weapon","Touch"};var sources=sourceNames.Select(n=>(AudioSource)Get(voice,n)).ToArray();
        var volumes=sources.Select(a=>a.volume).ToArray();var pitches=sources.Select(a=>a.pitch).ToArray();var positions=sources.Select(a=>a.transform.position).ToArray();var playing=sources.Select(a=>a.isPlaying).ToArray();
        var harmony=new Harmony("mecha.audio.native.qa."+v.entityId);EntityVehicle other=null,replacement=null;EntityAlive pilot=null;
        var slots=AccessTools.Field(typeof(Entity),"attachedEntities");var originalSlots=slots.GetValue(v);
        try
        {
            var nativeEngine=v.vehicle.vehicleParts.OfType<VPEngine>().First();
            int blocked=EngineSilence.Blocked;
            AccessTools.Method(typeof(VPEngine),"playSound").Invoke(nativeEngine,new object[]{"Vehicles/Suv/suv_start"});
            var loopArgs=new object[]{"Vehicles/Suv/suv_idle",null};
            AccessTools.Method(typeof(VPEngine),"playSoundLoop").Invoke(nativeEngine,loopArgs);
            AccessTools.Method(typeof(VPEngine),"playAccelDecelSound").Invoke(nativeEngine,new object[]{"Vehicles/Suv/suv_accel"});
            AccessTools.Method(typeof(VPEngine),"updateEngineSounds").Invoke(nativeEngine,new object[]{1f});
            Check(variant+" native engine start/loop/accel/update entry points blocked delta="+(EngineSilence.Blocked-blocked),EngineSilence.Blocked==blocked+4&&loopArgs[1]==null);
            Check(variant+" native engine stop still clears acceleration state",nativeEngine.accelDecelSoundName==null);
            var inherited=v.GetComponentsInChildren<AudioSource>(true).Where(a=>!a.transform.name.StartsWith("Mecha")).ToArray();
            Check(variant+" inherited vehicle prefab sources silent",inherited.All(a=>!a.enabled&&a.mute&&a.clip==null));
            harmony.Patch(AccessTools.PropertyGetter(typeof(RobotAudio),"Audible"),prefix:new HarmonyMethod(typeof(MechaAudioQA),nameof(Audible)));
            harmony.Patch(AccessTools.Method(typeof(Weapons),"Broadcast"),prefix:new HarmonyMethod(typeof(MechaAudioQA),nameof(NoBroadcast)));
            shows.Clear();samurai.Clear();if(Rules.Complete(v))samurai.Add(v.entityId,combat);
            var folder=Path.Combine(Model.Path,"Audio");var names=Directory.GetFiles(folder,variant+"-*.wav");int loaded=0;
            foreach(var path in names){var clip=(AudioClip)AccessTools.Method(typeof(RobotAudio),"Clip").Invoke(null,new object[]{Path.GetFileNameWithoutExtension(path)});if(clip.channels==1&&clip.frequency==22050&&clip.samples>0)loaded++;}
            Check(variant+" native PCM loads all available designed assets count="+loaded,loaded==names.Length&&loaded==(Rules.Complete(v)?27:21));
            Check(variant+" six mechanical / weapon / touch / loop sources are distinct",sources.Distinct().Count()==6&&sources.All(a=>a!=null));
            Check(variant+" motion loops use variant texture assets",sources[0].clip.name=="Mecha_"+variant+"-servo"&&sources[1].clip.name=="Mecha_"+variant+"-thruster");
            Check(variant+" voice has no idle / reactor bed",!((GameObject)Get(voice,"Root")).GetComponentsInChildren<AudioSource>().Any(a=>a.clip!=null&&a.clip.name.Contains("idle")));
            int serial=RobotAudio.NextPresentationSerial(),before=RobotAudio.PlayedCueCount;
            RobotAudio.Event(v,"hatch-close",serial,.4f);RobotAudio.Event(v,"hatch-close",serial,.4f);RobotAudio.Event(v,"hatch-open",serial,.4f);
            Check(variant+" replay event suppressed, distinct cue same serial independent",RobotAudio.PlayedCueCount==before+2);
            var point=v.position+new Vector3(.3f,.2f,.1f);before=RobotAudio.PlayedCueCount;
            RobotAudio.ContactEvent(v,"step-left",serial,point,.4f);RobotAudio.ContactEvent(v,"step-left",serial,point,.4f);
            Check(variant+" replay contact suppressed and positioned at world-Origin",RobotAudio.PlayedCueCount==before+1&&Vector3.Distance(sources[5].transform.position,point-Origin.position)<.0001f);
            Check(variant+" audited cue reflects actual loaded variant clip",RobotAudio.RecentCues().Last().Clip==variant+"-step-left");
            other=EntityFactory.CreateEntity(EntityClass.FromString(Rules.VehicleName),v.position+Vector3.right*20) as EntityVehicle;world.SpawnEntityInWorld(other);
            before=RobotAudio.PlayedCueCount;RobotAudio.Event(other,"hatch-close",serial,.4f);
            Check(variant+" equal serial / cue on another vehicle remains independent",RobotAudio.PlayedCueCount==before+1);
            ResetGround(move);rig.ResetPose();Set(voice,"PowerKnown",false);v.hasDriver=true;v.IsEngineRunning=false;EntityVehicle.VehicleFuelUsageModifier=0;
            gates.Remove(v.entityId+"/power-on");gates.Remove(v.entityId+"/power-off");before=RobotAudio.PlayedCueCount;
            RobotAudio.Update(v,0,false);v.IsEngineRunning=true;RobotAudio.Update(v,0,false);RobotAudio.Update(v,0,false);v.IsEngineRunning=false;RobotAudio.Update(v,0,false);RobotAudio.Update(v,0,false);
            var powerCues=Enumerable.Reverse(RobotAudio.RecentCues()).Take(2).ToArray();
            Check(variant+" power false-true-false edges each cue exactly once",RobotAudio.PlayedCueCount==before+2&&powerCues.Count(c=>c.Vehicle==v.entityId&&c.Cue=="power-on")==1&&powerCues.Count(c=>c.Vehicle==v.entityId&&c.Cue=="power-off")==1);
            v.IsEngineRunning=true;RobotAudio.Update(v,0,false);RobotAudio.StopChannels(v);RobotAudio.Update(v,0,false);
            Check(variant+" powered stationary pilot has silent motion loops",(float)Get(voice,"ServoTarget")==0&&(float)Get(voice,"BoostTarget")==0&&Quiet(sources[0])&&Quiet(sources[1]));
            move.FlightMode=Flight.Phase.Cruise;RobotAudio.Update(v,1,false);RobotAudio.StopChannels(v);
            Check(variant+" cancellation immediately stops motion / charge loops",sources.Take(3).All(Quiet));ResetGround(move);
            if(Rules.Complete(v)){combat.BeamSpent=false;combat.LaserCharge=.75f;RobotAudio.Update(v,0,false);Check("complete charge uses own playing source / positive target",(float)Get(voice,"ChargeTarget")>0&&sources[2].isPlaying&&sources[2].clip.name=="Mecha_complete-laser-charge");RobotAudio.StopCharge(v);Check("complete explicit weapon cancel stops charge immediately",Quiet(sources[2])&&(float)Get(voice,"ChargeTarget")==0);combat.LaserCharge=0;}
            Set(voice,"LastLandAt",Time.time-10);before=RobotAudio.PlayedCueCount;RobotAudio.LandCue(v,point,.7f,RobotAudio.NextPresentationSerial());RobotAudio.LandCue(v,point,.25f,-1);
            Check(variant+" authoritative and fallback landing sound deduplicate across serial sources",RobotAudio.PlayedCueCount==before+1);
            var oldVoice=Voice(other);var oldRoot=(GameObject)Get(oldVoice,"Root");replacement=EntityFactory.CreateEntity(EntityClass.FromString(Rules.VehicleName),other.position) as EntityVehicle;replacement.entityId=other.entityId;
            var replaced=Voice(replacement);var newRoot=(GameObject)Get(replaced,"Root");Check(variant+" same id entity replacement immediately disables old sound root",!oldRoot.activeSelf&&newRoot.activeSelf);
            RobotAudio.Cleanup(world);Check(variant+" orphan replaced voice cleaned without active sound root",!Map(typeof(RobotAudio),"voices").Contains(other.entityId)&&!newRoot.activeSelf);
            pilot=EntityFactory.CreateEntity(EntityClass.FromString("playerMale"),v.position+Vector3.forward) as EntityAlive;world.SpawnEntityInWorld(pilot);pilot.AttachedToEntity=v;slots.SetValue(v,new Entity[]{pilot});
            BoardTrial(world,v,shows,pilot);
            if(Rules.Complete(v)){harmony.Patch(AccessTools.Method(typeof(Samurai),"Operator"),prefix:new HarmonyMethod(typeof(MechaAudioQA),nameof(Operator)));harmony.Patch(AccessTools.Method(typeof(Samurai),"GroundReady"),prefix:new HarmonyMethod(typeof(MechaAudioQA),nameof(GroundReady)));SwordTrial(world,v,combat);CancelTrial(v,combat,voice);}
        }
        catch(Exception ex){Check(variant+" sound probe exception "+ex,false);}
        finally
        {
            RobotAudio.StopChannels(v);harmony.UnpatchSelf();slots.SetValue(v,originalSlots);if(pilot!=null){pilot.AttachedToEntity=null;world.RemoveEntity(pilot.entityId,EnumRemoveEntityReason.Despawned);}if(other!=null)world.RemoveEntity(other.entityId,EnumRemoveEntityReason.Despawned);if(replacement!=null)UnityEngine.Object.Destroy(replacement.gameObject);
            RestoreMap(shows,showsSaved);RestoreMap(completed,completedSaved);RestoreMap(samurai,samuraiSaved);RestoreMap(gates,gatesSaved);foreach(var scalar in boardingScalars)scalar.Key.SetValue(null,scalar.Value);foreach(var scalar in samuraiScalars)scalar.Key.SetValue(null,scalar.Value);moveSaved.Restore();if(combatSaved!=null)combatSaved.Restore();weaponSaved.Restore();pose.Restore();voiceSaved.Restore();v.hasDriver=driver;v.IsEngineRunning=engine;EntityVehicle.VehicleFuelUsageModifier=fuelModifier;
            for(int i=0;i<sources.Length;i++)if(sources[i]!=null){sources[i].Stop();sources[i].volume=volumes[i];sources[i].pitch=pitches[i];sources[i].transform.position=positions[i];if(playing[i]&&sources[i].loop&&sources[i].clip!=null)sources[i].Play();}
            results.Add("AUDIO LIMITATION "+variant+": native PCM / source / phase trigger paths verified; server probe does not judge perceived timbre or client mixer loudness.");
        }
    }
    static void BoardTrial(World world,EntityVehicle v,IDictionary shows,EntityAlive pilot)
    {
        var start=AccessTools.Method(typeof(Boarding),"Start");string variant=Rules.Complete(v)?"complete":"prototype";
        foreach(bool exit in new[]{false,true}){
            start.Invoke(null,new object[]{v,pilot.entityId,exit,true});var phaseShow=shows[v.entityId];int serial=(int)Get(phaseShow,"AudioSequence");
            var sounds=AccessTools.Method(typeof(Boarding),"Sounds");
            float open=Rules.Complete(v)?Ceremony.OpenAt(exit):exit?.6f:.9f;
            float opened=Rules.Complete(v)?open+(exit?.7f:.75f):exit?1.2f:1.6f;
            float close=Rules.Complete(v)?Ceremony.CloseAt(exit):exit?1.4f:2.1f;
            float closed=Rules.Complete(v)?close+.65f:exit?2:2.5f;
            foreach(var phase in new[]{Tuple.Create(open,"hatch-open"),Tuple.Create(opened,"hatch-open-stop"),Tuple.Create(close,"hatch-close"),Tuple.Create(closed,"hatch-close-lock")}){
                sounds.Invoke(null,new object[]{phaseShow,phase.Item1-.001f,false});
                bool absent=!RobotAudio.RecentCues().Any(c=>c.Serial==serial&&c.Cue==phase.Item2);
                sounds.Invoke(null,new object[]{phaseShow,phase.Item1,false});sounds.Invoke(null,new object[]{phaseShow,phase.Item1,false});
                Check(variant+" exact hatch boundary exit="+exit+" cue="+phase.Item2,absent&&RobotAudio.RecentCues().Count(c=>c.Serial==serial&&c.Cue==phase.Item2)==1);
            }
            shows.Remove(v.entityId);
        }
        for(int run=0;run<2;run++){
            int before=RobotAudio.PlayedCueCount;start.Invoke(null,new object[]{v,pilot.entityId,false,true});var show=shows[v.entityId];Set(show,"NextSync",Time.time+100);
            float duration=(float)Get(show,"Duration");Set(show,"Started",Time.time-(duration-.09f));Boarding.Update(world);int crossed=RobotAudio.PlayedCueCount;Boarding.Update(world);
            Check(variant+" boarding crossed phases emit once per show run="+run,RobotAudio.PlayedCueCount==crossed&&crossed>=before+4);
            Set(show,"Started",Time.time-(duration+.1f));Boarding.Update(world);Check(variant+" boarding final slow frame emits ready and removes show run="+run,!shows.Contains(v.entityId)&&RobotAudio.RecentCues().Any(c=>c.Vehicle==v.entityId&&c.Serial==(int)Get(show,"AudioSequence")&&c.Cue=="ready"));
        }
    }
    static void SwordTrial(World world,EntityVehicle v,Samurai.State s)
    {
        foreach(bool heavy in new[]{false,true}){
            Samurai.Stop(v);s.InputAt=Time.time;s.LastSync=Time.time;Samurai.Start(s,heavy,Time.time);int serial=s.SoundSequence;string swing=heavy?"sword-heavy":"sword-swing";
            s.Started=Time.time-Samurai.Duration(s)*(SwordMotion.CutEnd+.10f);s.InputAt=Time.time;Samurai.Tick(world,.4f);Samurai.Tick(world,.4f);
            var cues=RobotAudio.RecentCues().Where(c=>c.Vehicle==v.entityId&&c.Serial==serial).ToArray();
            Check("complete sword crossed prepare / release / brake once heavy="+heavy,cues.Count(c=>c.Cue=="sword-prepare")==1&&cues.Count(c=>c.Cue==swing)==1&&cues.Count(c=>c.Cue=="sword-brake")==1);
            s.Started=Time.time-Samurai.Duration(s)-.1f;s.InputAt=Time.time;Samurai.Tick(world,.4f);Check("complete sword final slow frame ends swing without replay heavy="+heavy,!s.Swing&&RobotAudio.RecentCues().Count(c=>c.Vehicle==v.entityId&&c.Serial==serial)==3);
        }
    }
    static Vector3 Ack(Samurai.State s,int token){int actor=s.LocalPresentationActor;return new Vector3(token,actor&65535,(uint)actor>>16);}
    static void CancelTrial(EntityVehicle v,Samurai.State s,object voice)
    {
        var client=new Harmony("mecha.audio.cancel.native.qa."+v.entityId);
        try
        {
            client.Patch(AccessTools.PropertyGetter(typeof(Weapons),"Server"),prefix:new HarmonyMethod(typeof(MechaAudioQA),nameof(Client)));
            client.Patch(AccessTools.Method(typeof(Weapons),"SendLocalIntent"),prefix:new HarmonyMethod(typeof(MechaAudioQA),nameof(NoBroadcast)));
            Samurai.Stop(v);s.Received=-1;s.ReceivedAttack=-1;s.SoundAttack=-1;s.SoundMask=0;Set(s,"LocalPresentationSuppressed",false);Set(s,"CancelledAttack",-1);
            var charge=(AudioSource)Get(voice,"Charge");var a=new Vector3(100,Samurai.AlertDelay,.01f);var b=new Vector3(0,0,.75f);int oldFlags=50<<16;
            Samurai.Receive(v,9001,a,b,oldFlags,0);RobotAudio.Update(v,0,false);
            Check("real laser-only snapshot starts charge without impossible sword overlap",!s.Swing&&charge.isPlaying&&(float)Get(voice,"ChargeTarget")>0);
            Samurai.ReleaseLocal(v);int token=s.PendingCancelToken;
            Check("local release immediately stops real laser and waits for cancel acknowledgement",token>0&&s.LaserCharge==0&&Quiet(charge)&&s.LocalPresentationSuppressed);
            int before=RobotAudio.PlayedCueCount;a.z=.60f;Samurai.Receive(v,9002,a,b,oldFlags,0);RobotAudio.Update(v,0,false);
            Check("delayed laser-only snapshot cannot revive charge in menu",s.Received==9002&&s.LaserCharge==0&&Quiet(charge));
            Samurai.LocalInput(v,new Ray(v.position+Vector3.up*2,Vector3.forward),true);Samurai.Receive(v,9003,a,b,oldFlags,0);RobotAudio.Update(v,0,false);
            Check("restored input still rejects old laser-only snapshot before acknowledgement",!s.LocalPresentationSuppressed&&s.LaserCharge==0&&Quiet(charge)&&RobotAudio.PlayedCueCount==before);
            Samurai.ReceiveCancelAck(v,9004,Ack(s,token+1));
            Check("unrelated cancellation acknowledgement cannot release barrier",s.PendingCancelToken==token);
            Samurai.ReceiveCancelAck(v,9005,Ack(s,token));
            Samurai.Receive(v,9004,a,b,oldFlags,0);RobotAudio.Update(v,0,false);
            Check("matching acknowledgement consumes snapshot ordering barrier",s.PendingCancelToken==0&&s.Received==9005&&s.LaserCharge==0&&Quiet(charge));
            Samurai.Receive(v,9006,a,b,oldFlags,0);RobotAudio.Update(v,0,false);
            Check("fresh laser can charge with unchanged sword attack identity",!s.Swing&&s.AttackSerial==50&&s.LaserCharge>.7f&&charge.isPlaying);
            b.z=0;a.z=.01f;Samurai.Receive(v,9007,a,b,8|(51<<16),0);
            Check("independent sword action remains available",s.Swing&&s.AttackSerial==51);
            Samurai.ReleaseLocal(v);int prior=s.PendingCancelToken;Samurai.LocalInput(v,new Ray(v.position,Vector3.forward),true);Samurai.ReleaseLocal(v);token=s.PendingCancelToken;
            Samurai.ReceiveCancelAck(v,9008,Ack(s,prior));
            Check("older acknowledgement cannot release a newer cancellation",s.PendingCancelToken==token&&token!=prior);
            Samurai.LocalInput(v,new Ray(v.position,Vector3.forward),true);Samurai.Receive(v,9010,a,b,8|(52<<16),0);
            Samurai.ReceiveCancelAck(v,9009,Ack(s,token));Samurai.Receive(v,9011,a,b,8|(52<<16),0);
            Check("post-cancel new sword snapshot reordered ahead of ack recovers normally",s.PendingCancelToken==0&&s.Swing&&s.AttackSerial==52);

        }
        finally{client.UnpatchSelf();Samurai.Stop(v);}
    }
}
