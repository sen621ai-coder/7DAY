using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
namespace PZAEC.FlyingSword
{
    public static class SwordAttackVisuals
    {
        static Material material;static Texture2D texture;static AudioClip release,ready,impact;
        static readonly Dictionary<int,float> impactTimes=new Dictionary<int,float>();
        public static int CosmeticSuppressions{get;private set;}
        public static Material Material {get{if(material==null){texture=new Texture2D(64,64,TextureFormat.RGBA32,false){wrapMode=TextureWrapMode.Clamp,name="Juque attack soft light"};for(int y=0;y<64;y++)for(int x=0;x<64;x++){float u=(x-31.5f)/32,v=(y-31.5f)/32;texture.SetPixel(x,y,new Color(1,1,1,Mathf.Exp(-(u*u+v*v)*5)*Mathf.Clamp01((1-Mathf.Max(Mathf.Abs(u),Mathf.Abs(v)))*8)));}texture.Apply(false,true);material=new Material(Shader.Find("Legacy Shaders/Particles/Additive")??Shader.Find("Sprites/Default")){mainTexture=texture};}return material;}}
        public static void Install(Harmony h)
        {
            h.Patch(AccessTools.PropertySetter(typeof(EntityAlive),"RightArmAnimationAttack"),postfix:new HarmonyMethod(typeof(SwordAttackVisuals),nameof(Attack)));
            h.Patch(AccessTools.Method(typeof(EntityAlive),"DamageEntity"),postfix:new HarmonyMethod(typeof(SwordAttackVisuals),nameof(Hit)));
            h.Patch(AccessTools.Method(typeof(AnimatorMeleeAttackState),"OnStateEnter",new[]{typeof(Animator),typeof(AnimatorStateInfo),typeof(int)}),prefix:new HarmonyMethod(typeof(SwordAttackVisuals),nameof(CosmeticEnter)));
            h.Patch(AccessTools.Method(typeof(ItemActionDynamicMelee),"ExecuteAction",new[]{typeof(ItemActionData),typeof(bool)}),prefix:new HarmonyMethod(typeof(SwordAttackVisuals),nameof(RealMelee)));
        }
        static void RealMelee(ItemActionData __0,bool __1){if(!__1&&__0.invData.holdingEntity is EntityPlayer p){var pose=p.GetComponent<SwordAttackPose>();if(pose!=null)pose.CosmeticUntil=-1;}}
        static bool CosmeticEnter(AnimatorMeleeAttackState __instance,Animator __0)
        {
            var p=__0.GetComponent<AnimationEventBridge>()?.entity as EntityPlayer;var pose=p?.GetComponent<SwordAttackPose>();if(pose==null||pose.CosmeticUntil<Time.time||!Valid(p))return true;
            // The controller keeps action 0 to select the real slash clip; only
            // this state's native damage callback is suppressed for wave release.
            AccessTools.Field(typeof(AnimatorMeleeAttackState),"actionIndex").SetValue(__instance,-1);AccessTools.Field(typeof(AnimatorMeleeAttackState),"entity").SetValue(__instance,p);__0.SetFloat("MeleeAttackSpeed",1.5f);CosmeticSuppressions++;return false;
        }
        static void Attack(EntityAlive __instance,bool __0){if(__0&&__instance is EntityPlayer p&&p.world?.GetPrimaryPlayer()!=null&&Valid(p))For(p).Swing();}
        static void Hit(EntityAlive __instance,DamageSource __0,int __result)
        {
            if(!SwordRuntime.Server||__result<=0||!SwordRules.IsSword(__0?.AttackingItem))return;
            float last;if(impactTimes.TryGetValue(__0.getEntityId(),out last)&&Time.time-last<.045f)return;
            var actor=__instance.world.GetEntity(__0.getEntityId()) as EntityPlayer;if(actor==null)return;impactTimes[actor.entityId]=Time.time;
            SwordRuntime.Emit(8,actor,-1,__instance.entityId,__0.AttackingItem,__instance.position+Vector3.up*.9f,__0.direction,1);
        }
        public static bool Valid(EntityPlayer p){return p!=null&&!p.IsDead()&&p.AttachedToEntity==null&&SwordRules.IsSword(p.inventory?.holdingItemItemValue)&&SwordRules.Deployed(p.inventory.holdingItemItemValue)==0;}
        public static SwordAttackPose For(EntityPlayer p){var c=p.GetComponent<SwordAttackPose>();if(c==null)c=p.gameObject.AddComponent<SwordAttackPose>();c.Player=p;return c;}
        public static void Clear(){impactTimes.Clear();foreach(var c in Object.FindObjectsOfType<SwordAttackPose>())Object.Destroy(c);foreach(var c in Object.FindObjectsOfType<JuqueAttackBurst>())Object.Destroy(c.gameObject);}
        public static void Charge(EntityPlayer p){if(Valid(p))For(p).Charging=true;}
        public static void Cancel(EntityPlayer p){var c=p?.GetComponent<SwordAttackPose>();if(c!=null)c.Charging=false;}
        public static void Release(EntityPlayer p){Cancel(p);if(!Valid(p))return;
            var pose=For(p);pose.CosmeticUntil=Time.time+.6f;p.emodel?.avatarController?.UpdateInt("ItemActionIndex",0,true);p.RightArmAnimationAttack=true;pose.Swing();Sound(p.transform.position,0,p is EntityPlayerLocal);}
        public static ParticleSystem Particles(Transform parent,string name,bool world)
        {
            var go=new GameObject(name);if(parent!=null)go.transform.SetParent(parent,false);var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var m=ps.main;m.playOnAwake=false;m.loop=false;m.maxParticles=100;m.simulationSpace=world?ParticleSystemSimulationSpace.World:ParticleSystemSimulationSpace.Local;m.startLifetime=.3f;m.startSpeed=0;m.startSize=.2f;
            var e=ps.emission;e.enabled=false;var s=ps.shape;s.enabled=false;
            var color=ps.colorOverLifetime;color.enabled=true;var g=new Gradient();g.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(new Color(.25f,.8f,1),1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(0,1)});color.color=g;
            var size=ps.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,1,1,0));
            var r=ps.GetComponent<ParticleSystemRenderer>();r.sharedMaterial=Material;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;return ps;
        }
        public static GameObject Burst(Vector3 local,float power=1)
        {
            if(SwordMod.Settings.Effects<=0)return null;var ps=Particles(null,"Juque hit dispersal",true);ps.transform.position=local;
            for(int i=0;i<24;i++){var ep=new ParticleSystem.EmitParams{position=local,velocity=Random.onUnitSphere*Random.Range(.7f,3),startLifetime=Random.Range(.12f,.35f),startSize=Random.Range(.08f,.28f)*power,startColor=new Color(.75f,1,1,.7f*SwordMod.Settings.Effects)};ps.Emit(ep,1);}ps.Play();ps.gameObject.AddComponent<JuqueAttackBurst>();return ps.gameObject;
        }
        static AudioClip Clip(int kind)
        {
            const int rate=22050;float duration=kind==0?.28f:kind==1?.22f:.12f;var data=new float[(int)(duration*rate)];var rng=new System.Random(147+kind);
            for(int i=0;i<data.Length;i++){float t=i/(float)rate,u=t/duration,env=Mathf.Sin(Mathf.PI*u)*(1-u);float tone=Mathf.Sin(2*Mathf.PI*(kind==1?880:kind==0?230:460)*t);data[i]=(tone*(kind==1?.7f:.25f)+(float)(rng.NextDouble()*2-1)*(kind==1?.04f:.7f))*env;}
            var clip=AudioClip.Create("Juque "+kind,data.Length,1,rate,false);clip.SetData(data,0);return clip;
        }
        public static void Sound(Vector3 local,int kind,bool localPlayer=false)
        {
            if(SwordMod.Settings.Effects<=0)return;if(release==null){release=Clip(0);ready=Clip(1);impact=Clip(2);}
            var go=new GameObject("Juque attack sound");go.transform.position=local;var audio=go.AddComponent<AudioSource>();audio.clip=kind==0?release:kind==1?ready:impact;audio.outputAudioMixerGroup=Resources.FindObjectsOfTypeAll<UnityEngine.Audio.AudioMixerGroup>().FirstOrDefault(g=>g.name=="Master");audio.volume=kind==1?.13f:.2f;audio.spatialBlend=localPlayer?0:1;audio.minDistance=2;audio.maxDistance=30;audio.Play();Object.Destroy(go,1);
        }
    }
    public sealed class JuqueAttackBurst:MonoBehaviour {void Start(){Destroy(gameObject,.5f);}}
    [DefaultExecutionOrder(11000)]
    public sealed class SwordAttackPose:MonoBehaviour
    {
        public EntityPlayer Player;public bool Charging;public float CosmeticUntil=-1;float chargeStart=-1,swingUntil=-1,swingStart,emit;bool full,nativeSwing;
        ParticleSystem gather;Mesh mesh;GameObject ribbon;readonly List<Vector3> bases=new List<Vector3>(),tips=new List<Vector3>();readonly List<float> times=new List<float>();
        Animator poseAnimator;Transform[] arm;Quaternion[] target,input,applied;bool poseWritten;
        void Windup(float weight)
        {
            var animator=SwordPresentation.BodyAnimator(Player);if(animator==null)return;
            if(poseAnimator!=animator){poseAnimator=animator;var all=animator.GetComponentsInChildren<Transform>(true);arm=new[]{"RightShoulder","RightArm","RightForeArm","RightHand"}.Select(n=>all.FirstOrDefault(t=>t.name==n)).ToArray();if(arm.Any(t=>t==null)){arm=null;return;}
                var clip=animator.runtimeAnimatorController?.animationClips.FirstOrDefault(c=>c.name=="3P_Knife_AttackHeavy");if(clip==null){arm=null;return;}
                var positions=all.Select(t=>t.localPosition).ToArray();var rotations=all.Select(t=>t.localRotation).ToArray();var scales=all.Select(t=>t.localScale).ToArray();clip.SampleAnimation(animator.gameObject,clip.length*.16f);target=arm.Select(t=>t.localRotation).ToArray();
                for(int i=0;i<all.Length;i++){all[i].localPosition=positions[i];all[i].localRotation=rotations[i];all[i].localScale=scales[i];}input=new Quaternion[4];applied=new Quaternion[4];poseWritten=false;
            }
            if(arm==null)return;for(int i=0;i<4;i++){var current=arm[i].localRotation;if(!poseWritten||Quaternion.Angle(current,applied[i])>.01f)input[i]=current;applied[i]=Quaternion.Slerp(input[i],target[i],weight);arm[i].localRotation=applied[i];}poseWritten=true;
        }
        void RestoreWindup(){if(poseWritten&&arm!=null)for(int i=0;i<arm.Length;i++)if(arm[i]!=null&&Quaternion.Angle(arm[i].localRotation,applied[i])<.01f)arm[i].localRotation=input[i];poseWritten=false;}
        public void Swing(){if(Time.time<swingUntil-.25f)return;swingStart=Time.time+.025f;swingUntil=Time.time+.38f;}
        public void Advance(float now,float dt)
        {
            bool valid=SwordAttackVisuals.Valid(Player);var held=Player?.inventory?.GetHoldingItemTransform()?.Find("JuqueVisual");
            bool attacking=valid&&Player.emodel?.avatarController!=null&&Player.emodel.avatarController.IsAnimationAttackPlaying();if(attacking&&!nativeSwing)Swing();nativeSwing=attacking;
            if(!valid||held==null){Charging=false;chargeStart=-1;RestoreWindup();ClearTrail();if(gather!=null)gather.Clear();return;}
            if(SwordMod.Settings.Effects<=0){Charging=false;chargeStart=-1;RestoreWindup();ClearTrail();if(gather!=null)gather.Clear();return;}
            if(gather==null)gather=SwordAttackVisuals.Particles(transform,"Juque gathering light",true);
            if(Charging){if(chargeStart<0){chargeStart=now;full=false;}Windup(Mathf.Clamp01((now-chargeStart)/.2f)*.85f);float c=SwordRules.Charge(now-chargeStart);emit+=dt;
                while(emit>.025f){emit-=.025f;var target=held.TransformPoint(new Vector3(0,0,.28f));var offset=Random.onUnitSphere*Mathf.Lerp(.3f,.65f,c);gather.Emit(new ParticleSystem.EmitParams{position=target+offset,velocity=-offset/.22f,startLifetime=.22f,startSize=Mathf.Lerp(.06f,.14f,c),startColor=new Color(.65f,.95f,1,.75f*SwordMod.Settings.Effects)},1);}
                if(c>=1&&!full){full=true;SwordAttackVisuals.Burst(held.TransformPoint(new Vector3(0,0,.3f)),.6f);SwordAttackVisuals.Sound(held.position,1,Player is EntityPlayerLocal);}
            }else {chargeStart=-1;full=false;gather.Clear();RestoreWindup();}
            if(now>=swingStart&&now<=swingUntil){bases.Add(held.TransformPoint(new Vector3(0,0,-.16f)));tips.Add(held.TransformPoint(new Vector3(0,0,.48f)));times.Add(now);}
            while(times.Count>0&&(now-times[0]>.10f||times.Count>16)){times.RemoveAt(0);bases.RemoveAt(0);tips.RemoveAt(0);}
            DrawTrail(now);
        }
        void LateUpdate(){Advance(Time.time,Mathf.Min(Time.deltaTime,.05f));}
        void ClearTrail(){bases.Clear();tips.Clear();times.Clear();if(mesh!=null)mesh.Clear();}
        void DrawTrail(float now)
        {
            if(times.Count<2){if(mesh!=null)mesh.Clear();return;}if(mesh==null){ribbon=new GameObject("Juque blade afterimage");mesh=new Mesh();ribbon.AddComponent<MeshFilter>().sharedMesh=mesh;ribbon.AddComponent<MeshRenderer>().sharedMaterial=SwordAttackVisuals.Material;}
            var vertices=new Vector3[times.Count*2];var uv=new Vector2[vertices.Length];var colors=new Color[vertices.Length];var triangles=new int[(times.Count-1)*6];
            for(int i=0;i<times.Count;i++){vertices[i*2]=bases[i];vertices[i*2+1]=tips[i];uv[i*2]=new Vector2(.5f,0);uv[i*2+1]=new Vector2(.5f,1);float alpha=Mathf.Clamp01(1-(now-times[i])/.1f)*.8f*SwordMod.Settings.Effects;colors[i*2]=new Color(.4f,.9f,1,alpha*.2f);colors[i*2+1]=new Color(.8f,1,1,alpha);if(i>0){int n=(i-1)*6,a=(i-1)*2;triangles[n]=a;triangles[n+1]=a+1;triangles[n+2]=a+2;triangles[n+3]=a+1;triangles[n+4]=a+3;triangles[n+5]=a+2;}}
            mesh.Clear();mesh.vertices=vertices;mesh.uv=uv;mesh.colors=colors;mesh.triangles=triangles;mesh.RecalculateBounds();
        }
        void OnDestroy(){RestoreWindup();if(gather!=null)Destroy(gather.gameObject);if(ribbon!=null)Destroy(ribbon);if(mesh!=null)Destroy(mesh);}
    }
    public sealed class SwordCrescent:MonoBehaviour
    {
        Mesh mesh;ParticleSystem wisps;float charge,lastTravel;Vector3 direction,right,up;public float Width{get;private set;}
        public void Init(Vector3 dir,float value){charge=value;direction=dir.normalized;right=Vector3.Cross(Vector3.up,direction).normalized;if(right.sqrMagnitude<.1f)right=Vector3.right;up=Vector3.Cross(direction,right).normalized;Width=Mathf.Lerp(1,8,charge);mesh=new Mesh{name="Juque filled crescent"};gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;gameObject.AddComponent<MeshRenderer>().sharedMaterial=SwordAttackVisuals.Material;wisps=SwordAttackVisuals.Particles(transform,"Juque crescent wisps",true);}
        public void Shape(Vector3 start,float travel,float[] stops,float fade)
        {
            const int cols=33,rows=7;var vertices=new Vector3[cols*rows];var uv=new Vector2[vertices.Length];var colors=new Color[vertices.Length];var triangles=new int[(cols-1)*(rows-1)*6];int n=0;
            for(int x=0;x<cols;x++){float s=x/32f*2-1,edge=Mathf.Pow(Mathf.Max(0,1-s*s),.65f),stop=stops[x];float active=travel<stop?1:0;
                for(int y=0;y<rows;y++){float t=y/6f;int k=x*rows+y;vertices[k]=start+direction*(Mathf.Min(travel,stop)-s*s*.2f-(t-.5f)*.25f*edge)+right*s*Width*.5f+up*(-s*s*Mathf.Lerp(.1f,.65f,charge)+(t-.5f)*Mathf.Lerp(.2f,.85f,charge)*edge);uv[k]=new Vector2(.5f,t);colors[k]=new Color(.65f,.95f,1,active*edge*fade*SwordMod.Settings.Effects);if(x>0&&y>0){int a=(x-1)*rows+y-1;triangles[n++]=a;triangles[n++]=a+1;triangles[n++]=a+rows;triangles[n++]=a+1;triangles[n++]=a+rows+1;triangles[n++]=a+rows;}}}
            mesh.Clear();mesh.vertices=vertices;mesh.uv=uv;mesh.colors=colors;mesh.triangles=triangles;mesh.RecalculateBounds();
            if(travel>lastTravel&&SwordMod.Settings.Effects>0){for(int i=0;i<Mathf.Min(12,Mathf.CeilToInt((travel-lastTravel)*4));i++){float side=Random.Range(-1f,1f);int lane=Mathf.Clamp(Mathf.RoundToInt((side+1)*16),0,32);if(travel>=stops[lane])continue;wisps.Emit(new ParticleSystem.EmitParams{position=start+direction*(travel-side*side*.8f)+right*side*Width*.5f,velocity=-direction*2+Random.insideUnitSphere,startLifetime=.2f,startSize=Mathf.Lerp(.1f,.25f,charge),startColor=new Color(.45f,.9f,1,.35f*fade*SwordMod.Settings.Effects)},1);}lastTravel=travel;wisps.Play();}
        }
        void OnDestroy(){if(mesh!=null)Destroy(mesh);}
    }
}
