using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
namespace PZAEC.Mecha
{
    public static class RobotAudio
    {
        sealed class Voice
        {
            public EntityVehicle Vehicle; public GameObject Root;public AudioSource Servo,Boost,Charge,Shot,Weapon,Touch;
            public Model.Rig Rig;public Transform[] Joints;public Quaternion[] Rotations;public Vector3[] Positions;
            public float ServoTarget,BoostTarget,ChargeTarget,JointActivity,LastLandAt=-100;public bool PowerKnown,WasPowered;
        }
        public sealed class CueAudit {public int Vehicle,Serial;public string Cue,Clip;public float At,Volume;public bool Contact;public Vector3 Point;}
        struct CueKey:IEquatable<CueKey>
        {public int Vehicle,Serial;public string Cue;public bool Equals(CueKey o){return Vehicle==o.Vehicle&&Serial==o.Serial&&Cue==o.Cue;}public override bool Equals(object o){return o is CueKey&&Equals((CueKey)o);}public override int GetHashCode(){return unchecked((Vehicle*397^Serial)*397^Cue.GetHashCode());}}
        static readonly Dictionary<int,Voice> voices=new Dictionary<int,Voice>();
        static readonly Dictionary<string,AudioClip> clips=new Dictionary<string,AudioClip>();
        static readonly HashSet<CueKey> seen=new HashSet<CueKey>();static readonly Queue<CueKey> order=new Queue<CueKey>();
        static readonly Dictionary<string,float> gates=new Dictionary<string,float>();static readonly List<CueAudit> recent=new List<CueAudit>(64);
        public static int PlayedCueCount {get;private set;}public static int SuppressedCueCount {get;private set;}
        static int presentationSerial=0x40000000;
        public static int NextPresentationSerial(){if(presentationSerial==int.MaxValue)presentationSerial=0x40000000;return ++presentationSerial;}
        public static CueAudit[] RecentCues(){return recent.ToArray();}
        public static string ClipName(EntityVehicle v,string cue){if(cue=="sword")cue="sword-swing";return (Rules.Complete(v)?"complete-":"prototype-")+cue;}
        static AudioClip Clip(string name)
        {
            AudioClip c;if(clips.TryGetValue(name,out c))return c;
            using(var r=new BinaryReader(File.OpenRead(Path.Combine(Model.Path,"Audio",name+".wav"))))
            { if(new string(r.ReadChars(4))!="RIFF")throw new InvalidDataException(name);r.ReadInt32();if(new string(r.ReadChars(4))!="WAVE")throw new InvalidDataException(name);
              int rate=0;short channels=0,bits=0,format=0;byte[] bytes=null;
              while(r.BaseStream.Position+8<=r.BaseStream.Length){string id=new string(r.ReadChars(4));int size=r.ReadInt32();long end=r.BaseStream.Position+size;
                if(size<0||end>r.BaseStream.Length)throw new InvalidDataException(name);
                if(id=="fmt "){format=r.ReadInt16();channels=r.ReadInt16();rate=r.ReadInt32();r.ReadInt32();r.ReadInt16();bits=r.ReadInt16();}
                else if(id=="data")bytes=r.ReadBytes(size);
                r.BaseStream.Position=end+(size&1);}
              if(format!=1||channels!=1||bits!=16||rate<=0||bytes==null)throw new InvalidDataException("Expected mono PCM16: "+name);
              var samples=new float[bytes.Length/2];for(int i=0;i<samples.Length;i++)samples[i]=BitConverter.ToInt16(bytes,i*2)/32768f;
              c=AudioClip.Create("Mecha_"+name,samples.Length,1,rate,false);c.SetData(samples,0);clips[name]=c;return c; }
        }
        static AudioSource Source(GameObject root,string clip,bool loop)
        {var a=root.AddComponent<AudioSource>();a.playOnAwake=false;a.volume=0;a.loop=loop;a.spatialBlend=1;a.minDistance=3;a.maxDistance=45;a.rolloffMode=AudioRolloffMode.Logarithmic;a.dopplerLevel=0;if(clip!=null)a.clip=Clip(clip);return a;}
        static Voice Get(EntityVehicle v)
        {Voice a;if(voices.TryGetValue(v.entityId,out a)&&a.Vehicle==v&&a.Root!=null)return a;
            var root=new GameObject("MechaMechanicalAudio");root.transform.SetParent(v.transform,false);root.transform.localPosition=Vector3.up*1.5f;
            if(a!=null&&a.Root!=null){a.Root.SetActive(false);UnityEngine.Object.Destroy(a.Root);}
            a=new Voice{Vehicle=v,Root=root};a.Servo=Source(root,ClipName(v,"servo"),true);a.Boost=Source(root,ClipName(v,"thruster"),true);a.Charge=Source(root,Rules.Complete(v)?ClipName(v,"laser-charge"):null,true);a.Shot=Source(root,null,false);a.Weapon=Source(root,null,false);var contact=new GameObject("MechaContactAudio");contact.transform.SetParent(root.transform,false);a.Touch=Source(contact,null,false);voices[v.entityId]=a;return a;}
        static bool Audible {get {return GameManager.Instance!=null&&GameManager.Instance.World!=null&&GameManager.Instance.World.GetPrimaryPlayer()!=null;}}
        public static void OneShot(EntityVehicle v,string name,float volume)
        {Event(v,name,-1,volume);}
        public static void Contact(EntityVehicle v,string name,Vector3 point,float volume){ContactEvent(v,name,-1,point,volume);}
        // Event serials deduplicate replayed snapshots without silencing different
        // stages of the same action. Negative serials use only a short retrigger gate.
        public static void Event(EntityVehicle v,string cue,int serial=-1,float volume=1f)
        {Emit(v,cue,serial,Vector3.zero,false,volume);}
        public static void ContactEvent(EntityVehicle v,string cue,int serial,Vector3 point,float volume=1f)
        {
            if(v==null)return;
            bool foot=cue=="step-left"||cue=="step-right";
            if((foot||cue=="land")&&Skim.Active(v))return;
            if(foot&&Flight.AirPose(Locomotion.Get(v)))return;
            Emit(v,cue,serial,point,true,volume);
        }
        public static void LandCue(EntityVehicle v,Vector3 point,float strength,int serial=-1)
        {
            if(v==null||!Audible)return;var voice=Get(v);
            // Physics event and delayed visual fallback can describe one landing.
            if(Time.time-voice.LastLandAt<.7f){SuppressedCueCount++;return;}voice.LastLandAt=Time.time;
            ContactEvent(v,"land",serial,point,Mathf.Lerp(.45f,.90f,Mathf.Clamp01(strength)));
        }
        public static void StopCharge(EntityVehicle v)
        {Voice a;if(v==null||!voices.TryGetValue(v.entityId,out a))return;a.ChargeTarget=0;a.Charge.volume=0;a.Charge.Stop();}
        public static void StopChannels(EntityVehicle v)
        {
            Voice a;if(v==null||!voices.TryGetValue(v.entityId,out a))return;
            a.ServoTarget=a.BoostTarget=a.ChargeTarget=a.JointActivity=0;
            foreach(var source in new[]{a.Servo,a.Boost,a.Charge}){source.volume=0;source.Stop();}
            a.Joints=null; // A cancelled ceremony may snap to rest; that is not a new actuator movement.
        }
        static void Emit(EntityVehicle v,string cue,int serial,Vector3 point,bool contact,float volume)
        {
            if(v==null||!Audible||string.IsNullOrEmpty(cue)||volume<=0)return;if(cue=="sword")cue="sword-swing";
            if(serial>=0){var key=new CueKey{Vehicle=v.entityId,Serial=serial,Cue=cue};if(!seen.Add(key)){SuppressedCueCount++;return;}order.Enqueue(key);while(order.Count>512)seen.Remove(order.Dequeue());}
            else{string key=v.entityId+"/"+cue;float at;if(gates.TryGetValue(key,out at)&&Time.time-at<.055f){SuppressedCueCount++;return;}gates[key]=Time.time;}
            var voice=Get(v);bool weapon=cue=="head-laser"||cue=="palm-laser"||cue=="missile-release"||cue=="shield";
            var source=contact?voice.Touch:weapon?voice.Weapon:voice.Shot;if(contact)source.transform.position=point-Origin.position;
            string asset=ClipName(v,cue);PlayShot(source,asset,Mathf.Clamp01(volume));PlayedCueCount++;
            if(recent.Count==64)recent.RemoveAt(0);recent.Add(new CueAudit{Vehicle=v.entityId,Serial=serial,Cue=cue,Clip=asset,At=Time.time,Volume=Mathf.Clamp01(volume),Contact=contact,Point=point});
        }
        static void PlayShot(AudioSource a,string name,float volume){a.volume=1;a.PlayOneShot(Clip(name),volume);}
        static void Loop(AudioSource a,float target)
        {a.volume=Mathf.MoveTowards(a.volume,target,Time.deltaTime*(target<=0?8:2));if(target>0&&!a.isPlaying)a.Play();if(target<=0&&a.volume<=.001f){a.volume=0;a.Stop();}}
        // Sample the final local pose, after gait / sword / flight have applied.
        // The idle torso breathing (about .008 m/s) is below the motion floor.
        static float JointMotion(EntityVehicle v,Voice voice)
        {
            var rig=Model.GetRig(v);if(rig==null)return 0;
            bool first=voice.Rig!=rig||voice.Joints==null;
            if(first)
            {
                voice.Rig=rig;
                voice.Joints=new[]{rig.Torso,rig.Head,rig.ShoulderL,rig.ShoulderR,rig.ElbowL,rig.ElbowR,rig.HipL,rig.HipR,rig.KneeL,rig.KneeR,rig.FootL,rig.FootR,rig.WingL,rig.WingR,rig.ChestL,rig.ChestR,rig.ChestDoor};
                voice.Rotations=new Quaternion[voice.Joints.Length];voice.Positions=new Vector3[voice.Joints.Length];
            }
            float angular=0,linear=0,dt=Mathf.Max(.001f,Time.deltaTime);
            for(int i=0;i<voice.Joints.Length;i++)
            {
                var joint=voice.Joints[i];if(joint==null)continue;
                if(!first){angular=Mathf.Max(angular,Quaternion.Angle(voice.Rotations[i],joint.localRotation)/dt);linear=Mathf.Max(linear,Vector3.Distance(voice.Positions[i],joint.localPosition)/dt);}
                voice.Rotations[i]=joint.localRotation;voice.Positions[i]=joint.localPosition;
            }
            return Mathf.Clamp01(Mathf.Max((angular-6)/90,(linear-.025f)/.35f));
        }
        public static void Update(EntityVehicle v,float moving,bool ceremony)
        {if(!Audible)return;var a=Get(v);var s=Locomotion.Get(v);bool powered=Locomotion.Powered(v);
            if(a.PowerKnown&&a.WasPowered!=powered)Event(v,powered?"power-on":"power-off",-1,powered?.55f:.40f);a.PowerKnown=true;a.WasPowered=powered;
            a.JointActivity=JointMotion(v,a);a.ServoTarget=(powered||ceremony)?Mathf.Max(Mathf.Clamp01(moving),a.JointActivity)*.35f:0;
            float speed=Mathf.Abs(s.VisualForward);if(v.vehicleRB!=null)speed=Mathf.Max(speed,Vector3.ProjectOnPlane(v.vehicleRB.velocity,Vector3.up).magnitude);
            a.BoostTarget=powered?(Flight.Active(s)?(s.Boost?.85f:.45f):s.HoverOn?.35f:Skim.Active(v)?.55f:s.Boost&&speed>.25f?.55f:0):0;
            var combat=Rules.Complete(v)?Samurai.Get(v):null;
            a.ChargeTarget=powered&&!ceremony&&combat!=null&&!combat.BeamSpent?Mathf.Clamp01(combat.LaserCharge)*.40f:0;
            a.Charge.pitch=.9f+(combat!=null?Mathf.Clamp01(combat.LaserCharge)*.35f:0);
            Loop(a.Servo,a.ServoTarget);Loop(a.Boost,a.BoostTarget);Loop(a.Charge,a.ChargeTarget);}
        public static string Diagnostics(EntityVehicle v)
        {
            Voice a;if(v==null||!voices.TryGetValue(v.entityId,out a))return "mechanical audio not created";
            return "servoTarget="+a.ServoTarget+" servoPlaying="+a.Servo.isPlaying+" servoVolume="+a.Servo.volume+" jointActivity="+a.JointActivity+" boostTarget="+a.BoostTarget+" boostPlaying="+a.Boost.isPlaying+" boostVolume="+a.Boost.volume+" chargeTarget="+a.ChargeTarget+" chargePlaying="+a.Charge.isPlaying+" cues="+PlayedCueCount+" replaySuppressed="+SuppressedCueCount;
        }
        public static void Cleanup(World world)
        {var ids=new List<int>();foreach(var p in voices)if(p.Value.Vehicle==null||world.GetEntity(p.Key)!=p.Value.Vehicle){if(p.Value.Root!=null){p.Value.Root.SetActive(false);UnityEngine.Object.Destroy(p.Value.Root);}ids.Add(p.Key);}foreach(int id in ids)voices.Remove(id);RobotPresentation.Cleanup(world);}
        public static void Clear()
        {foreach(var a in voices.Values)if(a.Root!=null){a.Root.SetActive(false);UnityEngine.Object.Destroy(a.Root);}voices.Clear();foreach(var c in clips.Values)UnityEngine.Object.Destroy(c);clips.Clear();seen.Clear();order.Clear();gates.Clear();recent.Clear();PlayedCueCount=SuppressedCueCount=0;RobotPresentation.Clear();}
    }
    public static class RobotPresentation
    {
        sealed class Parts {public EntityVehicle Vehicle;public Transform Root,Left,Right,Emitter,Eye;public Transform[] Jets=new Transform[2];public Light Lamp;}
        static readonly Dictionary<int,Parts> parts=new Dictionary<int,Parts>();
        static Material plate,glow;
        static Transform Cube(Transform parent,string name,Vector3 position,Vector3 scale,Material material)
        {var o=GameObject.CreatePrimitive(PrimitiveType.Cube);o.name=name;var c=o.GetComponent<Collider>();c.enabled=false;UnityEngine.Object.Destroy(c);o.transform.SetParent(parent,false);o.transform.localPosition=position;o.transform.localScale=scale;o.GetComponent<Renderer>().sharedMaterial=material;return o.transform;}
        public static void Update(EntityVehicle v,Model.Rig rig,float boost,float hatch)
        {
            if(rig.Justice!=null)return;
            Parts p;if(!parts.TryGetValue(v.entityId,out p)||p.Root==null||p.Vehicle!=v)
            {
                if(plate==null){plate=new Material(Shader.Find("Standard")){color=new Color(.08f,.1f,.11f)};plate.SetFloat("_Metallic",.8f);glow=new Material(Shader.Find("Sprites/Default")){color=new Color(.15f,.7f,1f,.65f)};}
                p=new Parts{Vehicle=v,Root=new GameObject("MechaCockpit").transform};p.Root.SetParent(rig.Torso,false);p.Root.localPosition=Rules.Complete(v)?new Vector3(0,.63f,.56f):new Vector3(0,.65f,.73f);
                if(!Rules.Complete(v)){p.Left=Cube(p.Root,"HatchL",new Vector3(-.16f,0,0),new Vector3(.31f,.48f,.07f),plate);
                p.Right=Cube(p.Root,"HatchR",new Vector3(.16f,0,0),new Vector3(.31f,.48f,.07f),plate);
                }else{
                    p.Root.localPosition=new Vector3(0,2.27f,.70f)-rig.TorsoBasePosition;
                    // The source chest stays intact. Only two original small
                    // armour panels open over recessed conforming backing.
                    Cube(p.Root,"CockpitLightL",new Vector3(-.045f,-.01f,.025f),new Vector3(.004f,.05f,.004f),glow);
                    Cube(p.Root,"CockpitLightR",new Vector3(.132f,-.01f,.05f),new Vector3(.004f,.05f,.004f),glow);
                }
                p.Lamp=p.Root.gameObject.AddComponent<Light>();p.Lamp.color=new Color(.2f,.8f,1);p.Lamp.range=Rules.Complete(v)?.35f:2;p.Lamp.intensity=0;p.Lamp.shadows=LightShadows.None;
                for(int i=0;i<2;i++)p.Jets[i]=Cube(rig.Backpack,"MechaThruster",new Vector3(i==0?-.35f:.35f,-.15f,-.12f),new Vector3(.12f,.02f,.12f),glow);
                if(Rules.Complete(v)){
                    p.Emitter=Cube(rig.Head,"MechaForeheadEmitter",new Vector3(0,.10f,.20f),new Vector3(.045f,.04f,.018f),glow);
                    p.Eye=Cube(rig.Head,"MechaEyeSensor",new Vector3(0,-.015f,.215f),new Vector3(.115f,.013f,.01f),glow);
                }
                parts[v.entityId]=p;
                Model.CacheFirstPersonDisplay(rig);
            }
            if(Rules.Complete(v)){
                p.Root.gameObject.SetActive(hatch>.001f);
                if(rig.ChestL!=null&&rig.ChestR!=null&&rig.ChestDoor!=null){
                    float door=Mathf.SmoothStep(0,1,Mathf.Clamp01((hatch-.15f)/.85f));
                    rig.ChestL.localPosition=rig.RestPos[rig.ChestL]+new Vector3(-.012f,0,.012f)*door;
                    rig.ChestR.localPosition=rig.RestPos[rig.ChestR]+new Vector3(.012f,0,.012f)*door;
                    rig.ChestL.localRotation=rig.RestRot[rig.ChestL]*Quaternion.Euler(0,-12*door,0);
                    rig.ChestR.localRotation=rig.RestRot[rig.ChestR]*Quaternion.Euler(0,12*door,0);
                    rig.ChestDoor.localPosition=rig.RestPos[rig.ChestDoor];
                    rig.ChestDoor.localRotation=rig.RestRot[rig.ChestDoor];
                }
            }else{
                p.Left.localPosition=new Vector3(-.16f-hatch*.32f,0,0);p.Right.localPosition=new Vector3(.16f+hatch*.32f,0,0);
                p.Left.localRotation=Quaternion.Euler(0,-hatch*55,0);p.Right.localRotation=Quaternion.Euler(0,hatch*55,0);
            }
            p.Lamp.intensity=hatch*(Rules.Complete(v)?.25f:1.4f);
            if(p.Emitter!=null){var s=Samurai.Get(v);var block=new MaterialPropertyBlock();block.SetColor("_Color",new Color(.2f,.85f,1f,.2f+.8f*s.LaserCharge));p.Emitter.GetComponent<Renderer>().SetPropertyBlock(block);block.SetColor("_Color",new Color(.2f,.85f,1f,.2f+.5f*s.Alert+.3f*s.LaserCharge));p.Eye.GetComponent<Renderer>().SetPropertyBlock(block);}
            var move=Locomotion.Get(v);
            float thrust=Locomotion.Powered(v)?(Flight.Active(move)?(move.Boost?1:move.VerticalInput>0?.85f:.55f):move.FlightMode==Flight.Phase.PowerLost?0:Skim.Active(v)?Mathf.Max(.55f,boost):boost):0;
            foreach(var jet in p.Jets){jet.gameObject.SetActive(thrust>.01f);jet.localScale=new Vector3(.12f,.15f+thrust*.5f,.12f);}
        }
        public static void Cleanup(World world){var remove=new List<int>();foreach(var p in parts)if(p.Value.Vehicle==null||world.GetEntity(p.Key)!=p.Value.Vehicle){Destroy(p.Value);remove.Add(p.Key);}foreach(int id in remove)parts.Remove(id);}
        static void Destroy(Parts p){if(p.Emitter!=null)UnityEngine.Object.Destroy(p.Emitter.gameObject);if(p.Eye!=null)UnityEngine.Object.Destroy(p.Eye.gameObject);if(p.Root!=null)UnityEngine.Object.Destroy(p.Root.gameObject);foreach(var j in p.Jets)if(j!=null)UnityEngine.Object.Destroy(j.gameObject);}
        public static void Clear(){foreach(var p in parts.Values)Destroy(p);parts.Clear();if(plate!=null)UnityEngine.Object.Destroy(plate);if(glow!=null)UnityEngine.Object.Destroy(glow);plate=glow=null;}
    }
}
