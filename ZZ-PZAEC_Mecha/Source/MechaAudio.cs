using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
namespace PZAEC.Mecha
{
    public static class RobotAudio
    {
        sealed class Voice { public EntityVehicle Vehicle; public GameObject Root;public AudioSource Idle,Servo,Boost,Shot,Touch; }
        static readonly Dictionary<int,Voice> voices=new Dictionary<int,Voice>();
        static readonly Dictionary<string,AudioClip> clips=new Dictionary<string,AudioClip>();
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
            a=new Voice{Vehicle=v,Root=root};a.Idle=Source(root,"reactor-idle",true);a.Servo=Source(root,"servo",true);a.Boost=Source(root,"boost",true);a.Shot=Source(root,null,false);var contact=new GameObject("MechaContactAudio");contact.transform.SetParent(root.transform,false);a.Touch=Source(contact,null,false);voices[v.entityId]=a;return a;}
        static bool Audible {get {return GameManager.Instance!=null&&GameManager.Instance.World!=null&&GameManager.Instance.World.GetPrimaryPlayer()!=null;}}
        public static void OneShot(EntityVehicle v,string name,float volume)
        {if(v!=null&&Audible)PlayShot(Get(v).Shot,name,volume);}
        public static void Contact(EntityVehicle v,string name,Vector3 point,float volume){if(v==null||!Audible)return;var source=Get(v).Touch;source.transform.position=point-Origin.position;PlayShot(source,name,volume);}
        static void PlayShot(AudioSource a,string name,float volume){a.volume=1;a.PlayOneShot(Clip(name),volume);}
        static void Loop(AudioSource a,float target)
        {a.volume=Mathf.MoveTowards(a.volume,target,Time.deltaTime*2);if(target>0&&!a.isPlaying)a.Play();if(target<=0&&a.volume<=.001f)a.Stop();}
        public static void Update(EntityVehicle v,float moving,bool ceremony)
        {if(!Audible)return;var a=Get(v);var s=Locomotion.Get(v);bool powered=Locomotion.Powered(v);
            Loop(a.Idle,powered?.10f:0);Loop(a.Servo,(powered||ceremony)?Mathf.Max(moving,ceremony?.7f:0)*.35f:0);Loop(a.Boost,powered?(Flight.Active(s)?(s.Boost?.85f:.55f):s.Blend*.6f):0);}
        public static void Cleanup(World world)
        {var ids=new List<int>();foreach(var p in voices)if(p.Value.Vehicle==null||world.GetEntity(p.Key)!=p.Value.Vehicle){if(p.Value.Root!=null)UnityEngine.Object.Destroy(p.Value.Root);ids.Add(p.Key);}foreach(int id in ids)voices.Remove(id);RobotPresentation.Cleanup(world);}
        public static void Clear()
        {foreach(var a in voices.Values)if(a.Root!=null)UnityEngine.Object.Destroy(a.Root);voices.Clear();foreach(var c in clips.Values)UnityEngine.Object.Destroy(c);clips.Clear();RobotPresentation.Clear();}
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
            Parts p;if(!parts.TryGetValue(v.entityId,out p)||p.Root==null||p.Vehicle!=v)
            {
                if(plate==null){plate=new Material(Shader.Find("Standard")){color=new Color(.08f,.1f,.11f)};plate.SetFloat("_Metallic",.8f);glow=new Material(Shader.Find("Sprites/Default")){color=new Color(.15f,.7f,1f,.65f)};}
                p=new Parts{Vehicle=v,Root=new GameObject("MechaCockpit").transform};p.Root.SetParent(rig.Torso,false);p.Root.localPosition=Rules.Complete(v)?new Vector3(0,.63f,.56f):new Vector3(0,.65f,.73f);
                if(!Rules.Complete(v)){p.Left=Cube(p.Root,"HatchL",new Vector3(-.16f,0,0),new Vector3(.31f,.48f,.07f),plate);
                p.Right=Cube(p.Root,"HatchR",new Vector3(.16f,0,0),new Vector3(.31f,.48f,.07f),plate);
                }else{
                    p.Root.localPosition=new Vector3(0,2.17f,.43f)-rig.TorsoBasePosition;
                    Cube(p.Root,"CockpitBack",new Vector3(0,0,-.08f),new Vector3(.38f,.40f,.035f),plate);
                    Cube(p.Root,"Seat",new Vector3(0,-.13f,-.025f),new Vector3(.20f,.10f,.12f),plate);
                    Cube(p.Root,"CockpitLightL",new Vector3(-.19f,0,0),new Vector3(.018f,.35f,.018f),glow);
                    Cube(p.Root,"CockpitLightR",new Vector3(.19f,0,0),new Vector3(.018f,.35f,.018f),glow);
                }
                p.Lamp=p.Root.gameObject.AddComponent<Light>();p.Lamp.color=new Color(.2f,.8f,1);p.Lamp.range=2;p.Lamp.intensity=0;p.Lamp.shadows=LightShadows.None;
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
                    float side=Mathf.Clamp01(hatch*1.5f),door=Mathf.Clamp01((hatch-.25f)/.75f);
                    rig.ChestL.localPosition=rig.RestPos[rig.ChestL]+new Vector3(-.16f,0,.04f)*side;
                    rig.ChestR.localPosition=rig.RestPos[rig.ChestR]+new Vector3(.16f,0,.04f)*side;
                    rig.ChestL.localRotation=rig.RestRot[rig.ChestL]*Quaternion.Euler(0,-45*side,0);
                    rig.ChestR.localRotation=rig.RestRot[rig.ChestR]*Quaternion.Euler(0,45*side,0);
                    rig.ChestDoor.localRotation=rig.RestRot[rig.ChestDoor]*Quaternion.Euler(80*door,0,0);
                }
            }else{
                p.Left.localPosition=new Vector3(-.16f-hatch*.32f,0,0);p.Right.localPosition=new Vector3(.16f+hatch*.32f,0,0);
                p.Left.localRotation=Quaternion.Euler(0,-hatch*55,0);p.Right.localRotation=Quaternion.Euler(0,hatch*55,0);
            }
            p.Lamp.intensity=hatch*1.4f;
            if(p.Emitter!=null){var s=Samurai.Get(v);var block=new MaterialPropertyBlock();block.SetColor("_Color",new Color(.2f,.85f,1f,.2f+.8f*s.LaserCharge));p.Emitter.GetComponent<Renderer>().SetPropertyBlock(block);block.SetColor("_Color",new Color(.2f,.85f,1f,.2f+.5f*s.Alert+.3f*s.LaserCharge));p.Eye.GetComponent<Renderer>().SetPropertyBlock(block);}
            var move=Locomotion.Get(v);
            float thrust=Rules.Complete(v)?(Flight.Active(move)?(move.Boost?1:move.VerticalInput>0?.85f:.55f):move.FlightMode==Flight.Phase.PowerLost?0:boost):boost;
            foreach(var jet in p.Jets){jet.gameObject.SetActive(thrust>.01f);jet.localScale=new Vector3(.12f,.15f+thrust*.5f,.12f);}
        }
        public static void Cleanup(World world){var remove=new List<int>();foreach(var p in parts)if(p.Value.Vehicle==null||world.GetEntity(p.Key)!=p.Value.Vehicle){Destroy(p.Value);remove.Add(p.Key);}foreach(int id in remove)parts.Remove(id);}
        static void Destroy(Parts p){if(p.Emitter!=null)UnityEngine.Object.Destroy(p.Emitter.gameObject);if(p.Eye!=null)UnityEngine.Object.Destroy(p.Eye.gameObject);if(p.Root!=null)UnityEngine.Object.Destroy(p.Root.gameObject);foreach(var j in p.Jets)if(j!=null)UnityEngine.Object.Destroy(j.gameObject);}
        public static void Clear(){foreach(var p in parts.Values)Destroy(p);parts.Clear();if(plate!=null)UnityEngine.Object.Destroy(plate);if(glow!=null)UnityEngine.Object.Destroy(glow);plate=glow=null;}
    }
}
