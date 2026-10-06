using System;
using System.IO;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
namespace PZAEC.M1
{
    public static class Presentation
    {
        public sealed class View
        {
            public EntityVehicle Vehicle;public Transform Root,Yaw,Pitch,Recoil,Muzzle;public int Epoch,Sequence,Shot,Reason,Ammo;public bool AP;public float RepairRemaining,ShellLife,FlashLife=.09f;public int ImpactShot;public Vector3 ImpactPosition;public AudioSource ImpactAudio;public Light ImpactLight;public float ImpactLightAt=-100;
            public float ShotAt=-100,NextReady,LastStatus=-100,YawAngle,PitchAngle,ClockOffset=float.PositiveInfinity,LastMove;
            public int VisualShot;public float FlashAt=-100;
            public Vector3 LastPosition;public Quaternion LastBody;public double LeftDistance,RightDistance;
            public readonly List<Transform> Wheels=new List<Transform>();public readonly List<TrackMotion> Tracks=new List<TrackMotion>();
            public Transform Flame;public Light Flash;public AudioSource Blast,Mechanism,Ready;public bool ReadyPlayed=true;
            public LineRenderer Tracer;public Vector3 ShellOrigin,ShellVelocity;public float ShellAt;public int ShellId;
            public readonly MaterialPropertyBlock Properties=new MaterialPropertyBlock();
        }
        sealed class Puff{public GameObject Go;public Renderer Renderer;public Vector3 World,Velocity;public float Start,Life,Size,Hold;public bool Debris,Fire;public int SourceVehicle;public Color Color;public MaterialPropertyBlock Properties=new MaterialPropertyBlock();}
        static readonly Dictionary<int,View> views=new Dictionary<int,View>();static readonly List<int> remove=new List<int>();
        static readonly List<Puff> puffs=new List<Puff>();static readonly Stack<Puff> pool=new Stack<Puff>();
        static Material smoke,flame;static Texture2D smokeTex,flameTex;static Mesh flameMesh;static AudioClip blastClip,mechanismClip,readyClip,impactAPClip;
        public static void Install(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(EntityPlayerLocal),"OnGUI"),postfix:new HarmonyMethod(typeof(Presentation),nameof(HUD)));
            Optics.Install(h);
            // Optics changes the camera pose in its earlier pre-cull callback.
            Camera.onPreCull+=BeforeRender;
        }
        static void BeforeRender(Camera camera)
        {
            if(camera!=GameManager.Instance?.World?.GetPrimaryPlayer()?.playerCamera)return;
            FacePuffs(camera);
        }
        static void FacePuffs(Camera camera){if(camera==null)return;foreach(var puff in puffs)puff.Go.transform.rotation=camera.transform.rotation;}
        static Transform Find(Transform root,string name){foreach(var t in root.GetComponentsInChildren<Transform>(true))if(t.name==name)return t;return null;}
        static AudioClip ReadWave(string name)
        {
            using(var r=new BinaryReader(File.OpenRead(Path.Combine(Model.Path,name+".wav")))){
                r.BaseStream.Position=24;int rate=r.ReadInt32();r.BaseStream.Position=40;int size=r.ReadInt32();var samples=new float[size/2];for(int i=0;i<samples.Length;i++)samples[i]=r.ReadInt16()/32768f;
                var clip=AudioClip.Create(name,samples.Length,1,rate,false);clip.SetData(samples,0);return clip;
            }
        }
        static void Resources()
        {
            if(smoke!=null)return;var shader=Shader.Find("Sprites/Default")??Shader.Find("Unlit/Transparent");if(shader==null)throw new InvalidOperationException("M1 effect shader unavailable");
            smokeTex=new Texture2D(64,64,TextureFormat.RGBA32,false);flameTex=new Texture2D(64,128,TextureFormat.RGBA32,false);
            var pixels=new Color[64*64];for(int y=0;y<64;y++)for(int x=0;x<64;x++){float dx=(x-31.5f)/31.5f,dy=(y-31.5f)/31.5f;float a=Mathf.Clamp01(1-dx*dx-dy*dy);pixels[y*64+x]=new Color(1,1,1,a*a*(.8f+.2f*Mathf.Sin(x*.7f)*Mathf.Cos(y*.5f)));}smokeTex.SetPixels(pixels);smokeTex.Apply(false,true);
            pixels=new Color[64*128];for(int y=0;y<128;y++)for(int x=0;x<64;x++){
                float v=y/127f,u=Mathf.Abs(x/63f*2-1);float width=(1-v)*(.75f+.16f*Mathf.Sin(v*32));float a=Mathf.Clamp01(1-u/Mathf.Max(.01f,width));
                var c=Color.Lerp(new Color(1,.23f,.025f),new Color(1,.98f,.73f),a*(1-v));c.a=a*a*Mathf.Clamp01(v*22)*Mathf.Clamp01((1-v)*5);pixels[y*64+x]=c;
            }flameTex.SetPixels(pixels);flameTex.Apply(false,true);smokeTex.wrapMode=flameTex.wrapMode=TextureWrapMode.Clamp;
            smoke=new Material(shader){name="M1 smoke",mainTexture=smokeTex};flame=new Material(shader){name="M1 flame",mainTexture=flameTex};
            var vv=new List<Vector3>();var uv=new List<Vector2>();var tt=new List<int>();
            for(int q=0;q<3;q++){
                var rot=Quaternion.Euler(0,0,q*60);int k=vv.Count;vv.Add(rot*new Vector3(-.4f,0,0));vv.Add(rot*new Vector3(.4f,0,0));vv.Add(rot*new Vector3(.4f,0,1.4f));vv.Add(rot*new Vector3(-.4f,0,1.4f));
                uv.AddRange(new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(1,1),new Vector2(0,1)});foreach(int t in new[]{0,1,2,0,2,3,2,1,0,3,2,0})tt.Add(k+t);
            }
            // The longitudinal ribbons collapse to thin edges when viewed along
            // the bore. A small transverse core remains visible from the optic.
            int core=vv.Count;vv.AddRange(new[]{new Vector3(-.24f,-.24f,.12f),new Vector3(.24f,-.24f,.12f),new Vector3(.24f,.24f,.12f),new Vector3(-.24f,.24f,.12f)});
            uv.AddRange(new[]{new Vector2(.25f,.12f),new Vector2(.75f,.12f),new Vector2(.75f,.55f),new Vector2(.25f,.55f)});
            foreach(int t in new[]{0,1,2,0,2,3,2,1,0,3,2,0})tt.Add(core+t);
            flameMesh=new Mesh{name="M1 crossed cannon flash"};flameMesh.SetVertices(vv);flameMesh.SetUVs(0,uv);var colors=new Color[vv.Count];for(int i=0;i<colors.Length;i++)colors[i]=Color.white;flameMesh.colors=colors;flameMesh.SetTriangles(tt,0);flameMesh.RecalculateBounds();
            blastClip=ReadWave("cannon-blast");mechanismClip=ReadWave("cannon-mechanism");readyClip=ReadWave("cannon-ready");impactAPClip=ReadWave("impact-ap");
        }
        static AudioSource Audio(Transform t,AudioClip clip,float volume,float distance)
        {var source=t.gameObject.AddComponent<AudioSource>();source.clip=clip;source.playOnAwake=false;source.spatialBlend=1;source.rolloffMode=AudioRolloffMode.Logarithmic;source.minDistance=8;source.maxDistance=distance;source.volume=volume;return source;}
        static View Get(EntityVehicle v)
        {
            if(views.TryGetValue(v.entityId,out var result)&&result.Vehicle==v)return result;
            if(result!=null)Dispose(result);
            var root=Find(v.PhysicsTransform!=null?v.PhysicsTransform:v.transform,"M1Visual");if(root==null)return null;Resources();
            var x=new View{Vehicle=v,Root=root,Yaw=Find(root,"TurretYaw"),Pitch=Find(root,"GunPitch"),Recoil=Find(root,"GunRecoil"),Muzzle=Find(root,"Muzzle"),LastPosition=v.position,LastBody=Weapons.Body(v)};
            foreach(var t in root.GetComponentsInChildren<Transform>(true)){
                if((t.name.StartsWith("RoadWheel_")||t.name.StartsWith("EndWheel_"))&&!t.name.Contains("LOD"))x.Wheels.Add(t);
                if(t.name.StartsWith("TrackL_LOD")||t.name.StartsWith("TrackR_LOD"))x.Tracks.Add(new TrackMotion(t,t.name.StartsWith("TrackL")));
            }
            x.Flame=new GameObject("M1CannonMuzzleFX").transform;x.Flame.SetParent(x.Muzzle,false);x.Flame.gameObject.AddComponent<MeshFilter>().sharedMesh=flameMesh;
            var mr=x.Flame.gameObject.AddComponent<MeshRenderer>();mr.sharedMaterial=flame;mr.shadowCastingMode=ShadowCastingMode.Off;mr.receiveShadows=false;
            x.Flash=x.Flame.gameObject.AddComponent<Light>();x.Flash.type=LightType.Point;x.Flash.range=5;x.Flash.color=new Color(1,.68f,.28f);x.Flash.shadows=LightShadows.None;x.Flash.intensity=0;x.Flame.gameObject.SetActive(false);
            x.Blast=Audio(x.Muzzle,blastClip,1,350);x.Mechanism=Audio(x.Pitch,mechanismClip,.4f,35);x.Ready=Audio(x.Pitch,readyClip,.13f,12);
            x.ImpactAudio=Audio(new GameObject("M1APImpactAudio").transform,impactAPClip,.8f,180);
            x.ImpactLight=x.ImpactAudio.gameObject.AddComponent<Light>();x.ImpactLight.type=LightType.Point;x.ImpactLight.range=9;x.ImpactLight.color=new Color(1,.6f,.25f);x.ImpactLight.shadows=LightShadows.None;x.ImpactLight.intensity=0;
            var tracer=new GameObject("M1ShellTracer");x.Tracer=tracer.AddComponent<LineRenderer>();x.Tracer.sharedMaterial=smoke;x.Tracer.positionCount=2;x.Tracer.useWorldSpace=true;x.Tracer.startWidth=.045f;x.Tracer.endWidth=.025f;x.Tracer.startColor=new Color(1,.8f,.4f,.7f);x.Tracer.endColor=new Color(1,.5f,.1f,0);x.Tracer.shadowCastingMode=ShadowCastingMode.Off;x.Tracer.enabled=false;
            views[v.entityId]=x;return x;
        }
        public static Transform CreateMGFlash(Transform muzzle)
        {
            Resources();var t=new GameObject("M1MGMuzzleFX").transform;t.SetParent(muzzle,false);
            t.localScale=new Vector3(.14f,.14f,.22f);t.gameObject.AddComponent<MeshFilter>().sharedMesh=flameMesh;
            var r=t.gameObject.AddComponent<MeshRenderer>();r.sharedMaterial=flame;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;t.gameObject.SetActive(false);return t;
        }
        public static void Receive(World w,NetPackageM1Event p)
        {
            var vehicle=w?.GetEntity(p.Vehicle) as EntityVehicle;if(!Weapons.IsTank(vehicle))return;
            // Recoil impulse belongs only to the native body authority. A separate
            // visual receiver must not apply it on an authoritative server twice.
            if(w.GetPrimaryPlayer()==null)return;
            var v=Get(vehicle);if(v==null)return;
            if(v.Epoch!=0&&v.Epoch!=p.Epoch)return;if(v.Epoch==0)v.Epoch=p.Epoch;
            if(p.Kind==Weapons.StateEvent){if(p.Sequence<=v.Sequence)return;v.Sequence=p.Sequence;}
            float sample=Time.time-p.Time;v.ClockOffset=Mathf.Min(v.ClockOffset,sample);float age=Mathf.Max(0,Time.time-(p.Time+v.ClockOffset));
            if(p.Kind==Weapons.StateEvent){v.LastStatus=Time.time;v.YawAngle=p.A.x;v.PitchAngle=p.A.y;v.Reason=Mathf.RoundToInt(p.A.z);v.Ammo=Mathf.RoundToInt(p.Y);v.AP=p.B.y>.5f;v.RepairRemaining=p.B.z;v.NextReady=Time.time+Mathf.Max(0,p.X-age);
                if(p.Shot>v.Shot){v.Shot=p.Shot;v.ShotAt=Time.time-p.B.x-age;v.ReadyPlayed=true;}return;
            }
            if(p.Kind==Weapons.ImpactEvent){if(p.Shot==v.ShellId)v.Tracer.enabled=false;
                if(p.X<=0&&p.Shot>v.ImpactShot&&age<.5f){v.ImpactShot=p.Shot;ImpactFX(v,p);}return;}
            if(p.Kind!=Weapons.ShotEvent||p.Shot<=v.VisualShot)return;
            v.VisualShot=p.Shot;v.Shot=Math.Max(v.Shot,p.Shot);v.ShotAt=Time.time-age;v.ReadyPlayed=false;v.NextReady=Time.time+Mathf.Max(0,p.X-age);v.ShellLife=Rules.Range/Rules.ShellSpeed(p.Y>.5f);
            Vector3 direction=p.B.normalized;
            bool ap=p.Y>.5f;v.FlashLife=EffectRules.FlashLife(Time.unscaledDeltaTime,false);
            v.Tracer.startColor=ap?new Color(1,.95f,.72f,.85f):new Color(1,.55f,.18f,.75f);v.Tracer.startWidth=ap?.014f:.022f;v.Tracer.endWidth=.006f;
            v.ShellId=p.Shot;v.ShellOrigin=p.A;v.ShellVelocity=p.B;v.ShellAt=Time.time-age;v.Tracer.enabled=age<v.ShellLife;
            if(EffectRules.Fresh(age)){v.FlashAt=Time.time;v.Flame.localRotation=Quaternion.Euler(0,0,(p.Shot*137)%360);float size=(ap?.45f:.65f)*(.88f+(p.Shot%5)*.06f);v.Flame.localScale=Vector3.one*size;v.Flame.gameObject.SetActive(true);}
            if(age<.35f){
                bool crew=w.GetPrimaryPlayer().AttachedToEntity==vehicle;v.Blast.volume=crew?.55f:1;v.Blast.pitch=(crew?.82f:1)*(ap?1.12f:.9f);v.Blast.Play();v.Mechanism.Play();
                var inherited=vehicle.vehicleRB!=null?Vector3.ClampMagnitude(vehicle.vehicleRB.velocity,20)*.25f:Vector3.zero;
                Emit(p.A,direction*1.8f+Vector3.up*.3f+inherited,new Color(.60f,.59f,.55f,.16f),ap?3:5,ap?.16f:.22f,ap?.45f:.7f,p.Shot,false,vehicle.entityId);
                if(Physics.Raycast(p.A-Origin.position,Vector3.down,out var ground,2.5f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)&&ground.collider.GetComponentInParent<EntityVehicle>()==null){
                    var at=ground.point+Origin.position-ground.normal*.05f;var block=w.GetBlock(new Vector3i(Mathf.FloorToInt(at.x),Mathf.FloorToInt(at.y),Mathf.FloorToInt(at.z)));
                    string surface=block.Block.GetBlockName().ToLowerInvariant();
                    var dust=surface.Contains("sand")||surface.Contains("clay")||surface.Contains("dirt")?new Color(.60f,.49f,.32f,.24f):surface.Contains("snow")?new Color(.85f,.86f,.87f,.22f):new Color(.48f,.47f,.45f,.24f);
                    if(!surface.Contains("water"))Emit(ground.point+Origin.position+ground.normal*.1f,ground.normal*.2f,dust,4,.35f,.65f,p.Shot+23,false,vehicle.entityId);
                }
                if(!Weapons.Server)Weapons.RecoilImpulse(vehicle,direction);
            }
        }
        static void ImpactFX(View v,NetPackageM1Event p)
        {
            Resources();bool ap=p.Y>.5f;var surface=ImpactRules.Decode(p.X);var normal=p.B.sqrMagnitude>.001f?p.B.normalized:Vector3.up;var origin=p.A+normal*.15f;
            int fire=ImpactRules.FireCount(ap,surface),sparks=ImpactRules.Sparks(ap,surface),dust=ImpactRules.Dust(ap,surface),burn=ImpactRules.Smoke(ap,surface);
            int needed=(ap?fire:6)+sparks+dust+burn;
            while(puffs.Count>160-needed){var old=puffs[0];old.Go.SetActive(false);pool.Push(old);puffs.RemoveAt(0);}
            GameObject native=null;
            if(ap){
                Emit(origin,normal*.2f+Vector3.up*.15f,new Color(1,.6f,.18f,.8f),1,.55f,.45f,p.Shot+303,false,-1,true,.12f);
                Emit(origin,Vector3.zero,new Color(1,.95f,.7f,.9f),1,.24f,.15f,p.Shot+304,false,-1,true,.06f);
            }else{
                try{native=GameManager.Instance.ExplosionClient(origin,Quaternion.identity,ImpactRules.BlastParticle(false),0,5,2500,-1,new List<BlockChangeInfo>());if(native!=null)native.transform.localScale*=2f;}
                catch(Exception e){Log.Warning("[M1] Native impact particle fallback: "+e.Message);}
                if(native==null){
                    Emit(origin,normal*.6f+Vector3.up*.5f,new Color(1,.65f,.2f,.95f),4,2.6f,.85f,p.Shot+303,false,-1,true,.18f);
                    Emit(origin,Vector3.zero,new Color(1,.95f,.7f,1),2,1.15f,.16f,p.Shot+304,false,-1,true,.06f);
                }
            }
            // Dust describes the struck surface; it never pretends a block broke.
            Color tint;switch(surface){
                case ImpactSurface.Earth:tint=new Color(.42f,.32f,.22f,.4f);break;
                case ImpactSurface.Sand:tint=new Color(.7f,.59f,.4f,.35f);break;
                case ImpactSurface.Snow:tint=new Color(.9f,.95f,1,.35f);break;
                case ImpactSurface.Water:tint=new Color(.65f,.8f,.9f,.25f);break;
                case ImpactSurface.Wood:tint=new Color(.45f,.34f,.22f,.25f);break;
                case ImpactSurface.Glass:tint=new Color(.7f,.8f,.85f,.2f);break;
                case ImpactSurface.Cloth:tint=new Color(.5f,.48f,.44f,.2f);break;
                default:tint=new Color(.5f,.49f,.46f,.35f);break;
            }
            Emit(origin,normal*(ap?.8f:1.6f)+Vector3.up*(ap?.6f:1.1f),tint,dust,ap?.25f:.85f,surface==ImpactSurface.Water?.65f:ap?.6f:1.8f,p.Shot+101);
            Emit(origin,Vector3.up*1.2f,new Color(.24f,.23f,.22f,.28f),burn,.85f,2f,p.Shot+404);
            Emit(origin,normal*3+Vector3.up*1.5f,new Color(1,.75f,.28f,1),sparks,.025f,.22f,p.Shot+202,true);
            v.ImpactPosition=p.A;v.ImpactAudio.transform.position=p.A-Origin.position;v.ImpactAudio.clip=ap?impactAPClip:blastClip;v.ImpactAudio.volume=ap?.65f:.8f;v.ImpactAudio.maxDistance=ap?180:300;v.ImpactAudio.pitch=ap?1:.75f;if(ap||native==null)v.ImpactAudio.Play();
            if(v.ImpactLight!=null)v.ImpactLightAt=Time.time;
        }

        public static AudioSource CreateMissileImpactSound()
        {Resources();var sound=Audio(new GameObject("M1AAImpactSound").transform,blastClip,.75f,300);sound.pitch=.9f;return sound;}
        public static void MissileImpact(Vector3 point,Vector3 direction,int seed,AudioSource sound)
        {
            Resources();var normal=direction.sqrMagnitude>.001f?-direction.normalized:Vector3.up;var origin=point+normal*.15f;
            while(puffs.Count>153){var old=puffs[0];old.Go.SetActive(false);pool.Push(old);puffs.RemoveAt(0);}
            Emit(origin,normal*.5f+Vector3.up*.3f,new Color(1,.65f,.2f,.9f),3,1.4f,.65f,seed+505,false,-1,true,.12f);
            Emit(origin,Vector3.zero,new Color(1,.95f,.7f,1),1,.65f,.15f,seed+506,false,-1,true,.06f);
            Emit(origin,Vector3.up*.8f,new Color(.3f,.29f,.27f,.3f),3,.6f,1.2f,seed+507);
            if(sound!=null){sound.transform.position=point-Origin.position;sound.Play();}
        }
        static void Emit(Vector3 origin,Vector3 velocity,Color color,int count,float size,float life,int seed,bool debris=false,int sourceVehicle=-1,bool fire=false,float hold=0)
        {
            var rng=new System.Random(seed);for(int i=0;i<count;i++){
                if(puffs.Count>=160)break;Puff p;
                if(pool.Count>0)p=pool.Pop();else{var go=GameObject.CreatePrimitive(PrimitiveType.Quad);go.name="M1WorldSmoke";var c=go.GetComponent<Collider>();c.enabled=false;UnityEngine.Object.Destroy(c);var r=go.GetComponent<Renderer>();r.sharedMaterial=smoke;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;p=new Puff{Go=go,Renderer=r};}
                p.Go.SetActive(true);p.SourceVehicle=sourceVehicle;p.Debris=debris;p.Fire=fire;p.World=origin;p.Velocity=velocity+new Vector3((float)rng.NextDouble()-.5f,(float)rng.NextDouble()*.5f,(float)rng.NextDouble()-.5f)*(debris?6:1);p.Start=Time.time;p.Hold=hold;float variation=.7f+(float)rng.NextDouble()*.5f;p.Life=hold>0?life:life*variation;p.Size=size*(.6f+(float)rng.NextDouble());p.Color=color;
                p.Go.transform.position=origin-Origin.position;p.Go.transform.localScale=Vector3.one*p.Size;p.Properties.SetColor("_Color",fire?color:Color.clear);p.Renderer.SetPropertyBlock(p.Properties);puffs.Add(p);
            }
        }
        public static void Update(World w)
        {
            var player=w.GetPrimaryPlayer();if(player==null)return;
            CrewVisibility.Update(w);
            foreach(var entry in Weapons.States)if((entry.Value.Vehicle.position-player.position).sqrMagnitude<300*300)Get(entry.Value.Vehicle);
            remove.Clear();int lights=0;
            foreach(var pair in views){var v=pair.Value;if(w.GetEntity(pair.Key)!=v.Vehicle||v.Root==null){remove.Add(pair.Key);continue;}
                float dt=Time.deltaTime;var body=Weapons.Body(v.Vehicle);var movement=v.Vehicle.position-v.LastPosition;
                if(movement.sqrMagnitude<100){float distance=Vector3.Dot(movement,body*Vector3.forward),yaw=Mathf.DeltaAngle(v.LastBody.eulerAngles.y,body.eulerAngles.y)*Mathf.Deg2Rad;v.LeftDistance+=distance+yaw*1.36;v.RightDistance+=distance-yaw*1.36;}
                v.LastPosition=v.Vehicle.position;v.LastBody=body;
                foreach(var wheel in v.Wheels){double distance=wheel.name.Contains("_L_")?v.LeftDistance:v.RightDistance;float radius=wheel.name.Contains("Rear")?.37f:.30135f;wheel.localRotation=Quaternion.Euler((float)(distance/radius*Mathf.Rad2Deg%360),0,0);}
                if(Time.time-v.LastMove>=.033f){foreach(var track in v.Tracks)track.Move(track.Left?v.LeftDistance:v.RightDistance);v.LastMove=Time.time;}
                if(!Weapons.Server){v.Yaw.localRotation=Quaternion.Euler(0,v.YawAngle,0);v.Pitch.localRotation=Quaternion.Euler(-v.PitchAngle,0,0);}
                float age=Time.time-v.ShotAt;if(!Weapons.Server)v.Recoil.localPosition=Vector3.back*Rules.Recoil(age);
                if(v.Tracer.enabled){float t=Time.time-v.ShellAt;if(t>=v.ShellLife)v.Tracer.enabled=false;else{var end=v.ShellOrigin+v.ShellVelocity*t+Vector3.down*(4.905f*t*t)-Origin.position;v.Tracer.SetPosition(0,end);v.Tracer.SetPosition(1,end-v.ShellVelocity.normalized*1.5f);}}
                if(v.ImpactAudio.isPlaying)v.ImpactAudio.transform.position=v.ImpactPosition-Origin.position;
                if(v.Flame.gameObject.activeSelf){float alpha=Mathf.Clamp01(1-(Time.time-v.FlashAt)/v.FlashLife);v.Properties.SetColor("_Color",new Color(1,1,1,alpha*(Optics.Scoped(v.Vehicle.entityId)?.6f:1)));v.Flame.GetComponent<Renderer>().SetPropertyBlock(v.Properties);v.Flash.intensity=lights++<2?1.4f*alpha*(Optics.Scoped(v.Vehicle.entityId)?.4f:1):0;if(alpha<=0)v.Flame.gameObject.SetActive(false);}
                if(v.ImpactLight!=null){float lamp=Time.time-v.ImpactLightAt;v.ImpactLight.intensity=lamp<.3f&&lights++<2?1.6f*(1-lamp/.3f):0;}
                if(!v.ReadyPlayed&&Time.time>=v.NextReady){v.ReadyPlayed=true;if(player.AttachedToEntity==v.Vehicle)v.Ready.Play();}
            }
            foreach(int id in remove){Dispose(views[id]);views.Remove(id);}
            UpdatePuffs(player.playerCamera,Time.deltaTime);
        }
        static void UpdatePuffs(Camera cam,float delta)
        {
            for(int i=puffs.Count-1;i>=0;i--){var p=puffs[i];float t=(Time.time-p.Start)/p.Life;
                if(t>=1){p.Go.SetActive(false);pool.Push(p);puffs.RemoveAt(i);continue;}
                if(p.Debris)p.Velocity+=Vector3.down*(9.81f*delta);
                var movement=p.Velocity*delta;
                if(p.Debris&&movement.sqrMagnitude>.000001f&&Physics.Raycast(p.World-Origin.position,movement.normalized,movement.magnitude,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)){p.Go.SetActive(false);pool.Push(p);puffs.RemoveAt(i);continue;}
                p.World+=movement;p.Go.transform.position=p.World-Origin.position;p.Go.transform.localScale=Vector3.one*p.Size*(p.Debris?1:1+2.5f*t);if(cam!=null)p.Go.transform.rotation=cam.transform.rotation;
                float fade=p.Hold>0?EffectRules.Fade(Time.time-p.Start,p.Life,p.Hold):t;
                var color=p.Fire?Color.Lerp(p.Color,new Color(1,.16f,.015f,0),fade):p.Color;
                color.a*=p.Fire?(p.Hold>0?1:1-t):Mathf.Clamp01(t*15)*(1-t)*(1-t)*(Optics.Scoped(p.SourceVehicle)?.2f:1);p.Properties.SetColor("_Color",color);p.Renderer.SetPropertyBlock(p.Properties);
            }
        }
        public static Ray SightRay(EntityPlayerLocal p)
        {
            if(Optics.TryRay(p,out var sight))return sight;
            if(p.playerCamera==null)return p.GetLookRay();
            var ray=p.playerCamera.ViewportPointToRay(new Vector3(.5f,.5f,0));ray.origin+=Origin.position;return ray;
        }
        public static float ShotPulse(EntityVehicle v)
        {
            if(!views.TryGetValue(v.entityId,out var view))return 0;
            float age=Time.time-view.ShotAt,amount=1;
            if(v.vehicle.Properties.Values.TryGetValue("m1CameraShake",out var text)&&float.TryParse(text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var value))amount=Mathf.Clamp01(value);
            return age>=0&&age<.35f?Mathf.Sin(age/.35f*Mathf.PI)*Mathf.Exp(-age*8)*amount:0;
        }
        public static void HUD()
        {
            var p=GameManager.Instance?.World?.GetPrimaryPlayer();var vehicle=p?.AttachedToEntity as EntityVehicle;
            if(p!=null&&p.AttachedToEntity==null&&Weapons.UIReady(p)){
                foreach(var entry in views){var other=entry.Value;if(other.RepairRemaining>0&&Time.time-other.LastStatus<1&&(other.Vehicle.position-p.position).sqrMagnitude<=64)
                    GUI.Box(new Rect(Screen.width/2-170,Screen.height-150,340,36),"M1装甲维修 · 保持G · 剩余 "+other.RepairRemaining.ToString("0.0")+"s");}
                if(Weapons.RepairTarget>=0&&views.TryGetValue(Weapons.RepairTarget,out var repair)&&repair.RepairRemaining<=0)
                    GUI.Box(new Rect(Screen.width/2-260,Screen.height-150,520,36),"维修需要：空乘员、停车、停火/未受伤10秒、货仓维修包、车辆使用权限");
            }
            if(!Weapons.IsTank(vehicle)||!Weapons.UIReady(p)||!views.TryGetValue(vehicle.entityId,out var v))return;
            if(SecondaryPresentation.HUD(p,vehicle))return;
            bool control=Weapons.Allowed(vehicle,p.entityId);float remaining=Mathf.Max(0,v.NextReady-Time.time);
            string reason=Time.time-v.LastStatus>1?"等待同步":!control?"炮手控制主炮":v.Reason==2?"炮口受阻":v.Reason==1?"超过俯仰范围":v.Reason==4?"炮塔转向中":v.Reason==3?"武器不可用":v.Ammo==0?"货仓缺少主炮弹":remaining>0?"装填 "+remaining.ToString("0.0")+"s":"主炮就绪";
            GUI.Box(new Rect(Screen.width/2-250,Screen.height-145,500,102),"M1 T"+(16+Weapons.Tier(vehicle))+" | "+(Weapons.Seat(vehicle,p.entityId)==1?"炮手":control?"驾驶 / 主炮":"驾驶员"));
            GUI.Label(new Rect(Screen.width/2-230,Screen.height-122,470,24),"耐久 "+vehicle.vehicle.GetHealth().ToString("N0")+" / "+vehicle.vehicle.GetMaxHealth().ToString("N0")+" · "+Weapons.Spec(vehicle).Horsepower+"马力");
            GUI.Label(new Rect(Screen.width/2-230,Screen.height-98,470,24),(v.AP?"AP穿甲":"HE榴弹")+" ×"+v.Ammo+"  |  "+reason);
            GUI.Label(new Rect(Screen.width/2-230,Screen.height-74,470,24),Modules.Invalid(vehicle)?"M1模组异常：效果已停用，请停车卸下并重新安装":"左键开火 · 右键瞄准 · R切弹 · W/S行驶 · A/D转向");
            if(control){
                var dir=Weapons.Body(vehicle)*Quaternion.Euler(0,v.YawAngle,0)*Quaternion.Euler(-v.PitchAngle,0,0)*Vector3.forward;
                Optics.Draw(p,vehicle,v.Muzzle,dir,v.Reason,v.AP);
            }
        }

        static void Dispose(View v){foreach(var t in v.Tracks)t.Dispose();if(v.Flame!=null)UnityEngine.Object.Destroy(v.Flame.gameObject);if(v.Tracer!=null)UnityEngine.Object.Destroy(v.Tracer.gameObject);if(v.ImpactAudio!=null)UnityEngine.Object.Destroy(v.ImpactAudio.gameObject);foreach(var a in new[]{v.Blast,v.Mechanism,v.Ready})if(a!=null)UnityEngine.Object.Destroy(a);}
        public static void Clear()
        {
            CrewVisibility.Clear();Optics.Clear();foreach(var v in views.Values)Dispose(v);views.Clear();foreach(var p in puffs)UnityEngine.Object.Destroy(p.Go);foreach(var p in pool)UnityEngine.Object.Destroy(p.Go);puffs.Clear();pool.Clear();
            foreach(var resource in new UnityEngine.Object[]{smoke,flame,smokeTex,flameTex,flameMesh,blastClip,mechanismClip,readyClip,impactAPClip})if(resource!=null)UnityEngine.Object.Destroy(resource);
            smoke=flame=null;smokeTex=flameTex=null;flameMesh=null;blastClip=mechanismClip=readyClip=impactAPClip=null;
        }
    }
}
