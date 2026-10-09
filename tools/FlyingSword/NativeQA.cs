using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object=UnityEngine.Object;
using PZAEC.FlyingSword;

// Compiled into the isolated QA copy only. Uses the installed game's real classes.
public sealed class JuqueNativeQA : IModApi
{
    public void InitMod(Mod mod){if(Environment.GetCommandLineArgs().Contains("-juqueQA"))ModEvents.GameStartDone.RegisterHandler(Start);}
    static void Start(ref ModEvents.SGameStartDoneData data){new GameObject("Juque QA").AddComponent<JuqueProbe>();}
}
public sealed class JuqueProbe : MonoBehaviour
{
    int failures,checks;
    void Check(string name,bool ok){checks++;if(!ok)failures++;Log.Out("[JuqueQA] "+(ok?"PASS ":"FAIL ")+name);}
    IEnumerator Start(){yield return new WaitForSeconds(2);try{Run();}catch(Exception e){failures++;Log.Error("[JuqueQA] "+e);}Log.Out("[JuqueQA] COMPLETE failures="+failures+" checks="+checks);}
    void Run()
    {
        var w=GameManager.Instance.World;
        Check("four sword classes and custom action",Enumerable.Range(16,4).All(t=>ItemClass.GetItem(SwordRules.Prefix+t,false).ItemClass.Actions[1] is ItemActionPZAECJuqueWave));
        Check("native Chinese sword names loaded",Enumerable.Range(16,4).All(t=>Localization.Get(SwordRules.Prefix+t,false,"schinese")=="巨阙剑·T"+t));
        Check("spirit stone action",ItemClass.GetItem(SwordRules.Crystal,false).ItemClass.Actions[0] is ItemActionPZAECSpiritStone);
        var prefab=SwordModel.Prefab();Check("source mesh present",prefab.GetComponentsInChildren<MeshFilter>(true).Any(m=>m.sharedMesh!=null&&m.sharedMesh.name=="Juque original mesh"));
        Check("input IL patched",Harmony.GetPatchInfo(AccessTools.Method(typeof(PlayerMoveController),"Update")).Owners.Contains("pzaec.juque.v1"));
        var v=EntityFactory.CreateEntity(EntityClass.FromString(SwordRules.VehicleName),new Vector3(0,350,0)) as EntityJuque;Check("custom entity factory",v!=null);w.SpawnEntityInWorld(v);
        v.vehicle.SetItemValue(ItemClass.GetItem(SwordRules.Prefix+16,false));
        Check("native physics",v.vehicleRB!=null);Check("one seat",v.GetAttachedToInfo(0)!=null);
        Render(v);
        var p=EntityFactory.CreateEntity(EntityClass.FromString("playerMale"),new Vector3(0,351,0)) as EntityPlayer;w.SpawnEntityInWorld(p);Check("player inventory",p.inventory!=null);
        var item=ItemClass.GetItem(SwordRules.Prefix+16,false);SwordRules.EnsureId(item);item.SetMetadata(SwordRules.EnergyKey,333f);p.inventory.SetItem(0,new ItemStack(item,1));item=p.inventory.GetItem(0).itemValue;
        v.OwnerActor=p.entityId;v.OwnerSlot=0;v.SwordId=SwordRules.Id(item);v.Energy=333;SwordInventory.Reserve(p,0,item);item.SetMetadata(SwordRules.DeployedKey,v.entityId);
        Check("reserved sort slot",p.inventory.ItemGrid.IsLocked(0));Check("reserved recipe exclusion",p.inventory.GetItemCount(ItemClass.GetItem(SwordRules.Prefix+16,false))==0);
        Check("canonical ownership",SwordRuntime.Owned(p,v));Log.Out("[JuqueQA] owner="+v.GetOwner()+" player="+p.PersistentPlayerData+" id="+SwordRules.Id(item)+" entity="+SwordRules.Deployed(item));
        var clone=item.Clone();Check("metadata clone",SwordRules.Energy(clone)==333&&SwordRules.Id(clone)==v.SwordId);
        p.StartAttachToEntity(v,0);Check("native attachment at 350 metres",p.AttachedToEntity==v&&v.GetAttached(0)==p);
        Check("native firearm seat flags",!v.GetAttachedToInfo(0).bHolsterHeldItem&&v.GetAttachedToInfo(0).bAllow3rdPerson);
        JuquePoseAudit.Run(v,p,Check);
        Check("first person eye camera without shoulder offset",SwordControls.CameraOffset(true,false)==Vector3.zero&&SwordControls.CameraOffset(true,true)==Vector3.zero);
        Check("third person shoulder and chase cameras",SwordControls.CameraOffset(false,true).z<0&&SwordControls.CameraOffset(false,false).z<SwordControls.CameraOffset(false,true).z);
        Check("view key defaults to physical backquote",new SwordSettings().Key(new SwordSettings().ViewKey,KeyCode.None)==KeyCode.BackQuote);
        Check("first person does not force full body visible",!v.GetAttachedToInfo(0).bKeep3rdPersonModelVisible);
        var aura=SwordFlightAura.For(v);aura.Step(true,false,false,1,2,Quaternion.identity);
        var particles=aura.GetComponentsInChildren<ParticleSystem>();
        Action<string> captureAura=name=>{var animator=SwordPresentation.BodyAnimator(p);SwordPresentation.PrepareAnimator(p,animator);animator.Update(1);SwordPresentation.ApplyPose(p,1f/60);JuquePoseAudit.Capture(v,p,name);};
        float auraTime=2;Action<Vector3,int> advanceAura=(velocity,frames)=>{for(int frame=0;frame<frames;frame++){auraTime+=1f/60;aura.Step(true,false,false,1,auraTime,Quaternion.identity,velocity);foreach(var ps in particles)ps.Simulate(1f/60,false,false,true);}};
        advanceAura(Vector3.zero,60);Check("hover emits no tail but retains sword coat",particles.All(ps=>ps.particleCount==0&&ps.emission.rateOverTime.constant==0)&&aura.GetComponentsInChildren<MeshRenderer>().Any(r=>r.enabled));
        captureAura("flight-tail-hover");
        advanceAura(Vector3.forward*26,1);Check("launch fades in below cruise speed",aura.MotionSpeed>0&&aura.MotionSpeed<5);
        advanceAura(Vector3.forward*26,10);captureAura("flight-tail-launch");advanceAura(Vector3.forward*26,80);
        Check("flame has three soft particle layers and no lines",particles.Length==3&&aura.GetComponentsInChildren<LineRenderer>().Length==0&&aura.GetComponentsInChildren<TrailRenderer>().Length==0);
        var tail=particles.First(t=>t.main.simulationSpace==ParticleSystemSimulationSpace.World);float normalWidth=tail.main.startSize.constantMax,normalTime=tail.main.startLifetime.constantMax,normalAlpha=tail.main.startColor.color.a;
        advanceAura(Vector3.forward*40,90);Check("boost extends and widens flame",tail.main.startLifetime.constantMax>normalTime&&tail.main.startSize.constantMax>normalWidth);
        foreach(var ps in particles){ps.useAutoRandomSeed=false;ps.randomSeed=127;ps.Simulate(.85f,true,true,true);}
        Check("native flame simulation emits particles",particles.All(ps=>ps.particleCount>0));
        var coats=aura.GetComponentsInChildren<MeshFilter>();Check("fitted sword glow shells",coats.Length==3&&coats.All(c=>c.sharedMesh.vertexCount==v.vehicleRB.transform.Find("JuqueVisual").GetComponent<MeshFilter>().sharedMesh.vertexCount));
        var coatSource=v.vehicleRB.transform.Find("JuqueVisual");var originalVertices=coatSource.GetComponent<MeshFilter>().sharedMesh.vertices;
        Check("glow shell world position within soft 3.5 centimetre envelope",coats.All(c=>c.sharedMesh.vertices.Select((point,index)=>Vector3.Distance(c.transform.TransformPoint(point),coatSource.TransformPoint(originalVertices[index]))).Max()<.035f));
        coatSource.localRotation=Quaternion.Euler(9,0,-12);aura.Step(true,true,false,1,auraTime,coatSource.localRotation,Vector3.forward*40);
        Check("banked glow shell follows sword offset and rotation",coats.All(c=>c.sharedMesh.vertices.Select((point,index)=>Vector3.Distance(c.transform.TransformPoint(point),coatSource.TransformPoint(originalVertices[index]))).Max()<.035f));
        coatSource.localRotation=Quaternion.identity;aura.Step(true,true,false,1,auraTime,Quaternion.identity,Vector3.forward*40);
        var body= SwordPresentation.BodyAnimator(p);SwordPresentation.PrepareAnimator(p,body);body.Update(1);SwordPresentation.ApplyPose(p,1f/60);
        JuquePoseAudit.Capture(v,p,"flight-aura-boost");
        var initialPosition=v.vehicleRB.transform.position;
        for(int f=0;f<60;f++){var velocity=Quaternion.Euler(0,f*1.5f,0)*Vector3.forward*26;v.vehicleRB.transform.position+=velocity/60;advanceAura(velocity,1);}
        Check("turn increases plume spread with actual trajectory",aura.TurnAmount>.7f&&tail.shape.angle>20);
        var tailPoints=new ParticleSystem.Particle[160];int tailCount=tail.GetParticles(tailPoints);Check("turn keeps particles behind in world space",tailCount>5&&tailPoints.Take(tailCount).Any(pt=>Vector3.Distance(pt.position,tail.transform.position)>2));
        captureAura("flight-tail-turn");
        advanceAura(Vector3.zero,1);Check("braking preserves fading wake",tail.particleCount>0&&aura.MotionSpeed>1);
        advanceAura(Vector3.zero,120);Check("stopped wake expires without lingering plume",particles.All(ps=>ps.particleCount==0));
        captureAura("flight-tail-stopped");
        aura.Step(true,true,false,1,auraTime+.016f,Quaternion.identity,Vector3.zero);Check("boost key at rest cannot create a tail",tail.emission.rateOverTime.constant==0);auraTime+=.016f;
        for(int f=0;f<60;f++){v.vehicleRB.transform.position+=Vector3.right*(26f/60);auraTime+=1f/60;aura.Step(true,false,false,1,auraTime,Quaternion.identity);}
        Check("remote position samples drive wake without rigidbody velocity",aura.MotionSpeed>24&&aura.MotionSpeed<28);
        v.vehicleRB.transform.position+=Vector3.right*100;auraTime+=1f/60;aura.Step(true,false,false,1,auraTime,Quaternion.identity);Check("teleport clears remote wake",tail.particleCount==0&&aura.MotionSpeed==0);
        v.vehicleRB.transform.position=initialPosition;
        aura.Step(true,false,true,1,auraTime,Quaternion.identity,Vector3.zero);Check("first person aura subdued",tail.main.startColor.color.a<normalAlpha*.3f);
        aura.Step(true,true,false,0,auraTime,Quaternion.identity);Check("effects zero disables all aura renderers",aura.GetComponentsInChildren<Renderer>().All(r=>!r.enabled));
        Check("effects zero clears existing flame",particles.All(ps=>ps.particleCount==0));
        aura.Step(false,false,false,1,auraTime,Quaternion.identity);Check("unoccupied sword aura off",aura.GetComponentsInChildren<Renderer>().All(r=>!r.enabled));
        Flight(v);
        for(int i=1;i<p.inventory.SlotCount;i++)p.inventory.SetItem(i,new ItemStack(ItemClass.GetItem("resourceWood",false),6000));
        p.inventory.SetHoldingItemIdxNoHolsterTime(1);
        SwordRuntime.Return(p,v);Check("immediate high altitude detachment",p.AttachedToEntity==null&&p.position.y>300);Check("selection retained on recall",p.inventory.holdingItemIdx==1);
        Check("return restores original slot with full toolbar",SwordRules.Deployed(p.inventory.GetItem(0).itemValue)==0&&SwordRules.Energy(p.inventory.GetItem(0).itemValue)==333);Check("world representation removed",w.GetEntity(v.entityId)==null);Check("original sort lock restored",!p.inventory.ItemGrid.IsLocked(0));
        SwordRuntime.Return(p,v);Check("repeated return creates no extra sword",p.inventory.GetItemCount(ItemClass.GetItem(SwordRules.Prefix+16,false))==1);
        SwordSafety.Grant(p);Check("fall only protection active",SwordSafety.Protected(p));
        var falling=new DamageSource(EnumDamageSource.External,EnumDamageTypes.Falling);int health=p.Health;
        p.DamageEntity(falling,25,false,0);Check("fall damage suppressed",p.Health==health);Check("fall protection consumed by first impact",!SwordSafety.Protected(p));
        var physical=new DamageSource(EnumDamageSource.External,EnumDamageTypes.Piercing);object[] args={p,physical,0};
        Check("physical damage remains enabled",(bool)AccessTools.Method(typeof(SwordSafety),"Damage").Invoke(null,args));
        SwordSafety.Grant(p);p.onGround=true;((System.Collections.Generic.Dictionary<EntityPlayer,float>)AccessTools.Field(typeof(SwordSafety),"granted").GetValue(null))[p]=Time.time-1;
        AccessTools.Method(typeof(SwordSafety),"Tick").Invoke(null,new object[]{p});Check("first valid landing clears fall protection",!SwordSafety.Protected(p));args=new object[]{p,falling,0};Check("later fall damage enabled",(bool)AccessTools.Method(typeof(SwordSafety),"Damage").Invoke(null,args));p.onGround=false;
        Check("fusion supported",AECT16RuntimeFix.EquipmentFusion.IsFusionItem(item));
        AECT16RuntimeFix.EquipmentFusion.SetRank(item,13);item.UseTimes=item.MaxUseTimes*.25f;
        var upgraded=ItemClass.GetItem(SwordRules.Prefix+17,false);var recipe=new Recipe{count=1,itemValueType=upgraded.type,ingredients=new System.Collections.Generic.List<ItemStack>{new ItemStack(item,1)}};
        upgraded=AECT16RuntimeFix.FusionTierUpgrade.Apply(upgraded,recipe);
        Check("upgrade retains full fusion rank",AECT16RuntimeFix.EquipmentFusion.Rank(upgraded)==13);
        Check("upgrade retains identity and energy proportion",SwordRules.Id(upgraded)==SwordRules.Id(item)&&Mathf.Abs(SwordRules.Energy(upgraded)-416.25f)<.01f);
        Check("upgrade retains wear proportion",Mathf.Abs(upgraded.UseTimes/upgraded.MaxUseTimes-.25f)<.001f);
        AECT16RuntimeFix.EquipmentFusion.SetRank(item,0);
        using(var stream=new System.IO.MemoryStream()){var writer=new PooledBinaryWriter();writer.SetBaseStream(stream);item.Write(writer);writer.Flush();stream.Position=0;var reader=new PooledBinaryReader();reader.SetBaseStream(stream);var loaded=new ItemValue();loaded.Read(reader);Check("native item persistence preserves identity energy and wear",SwordRules.Id(loaded)==SwordRules.Id(item)&&SwordRules.Energy(loaded)==SwordRules.Energy(item)&&loaded.UseTimes==item.UseTimes);}
        p.inventory.SetHoldingItemIdxNoHolsterTime(0);var state=SwordRuntime.State(p);state.NextShot=0;
        float tapEnergy=SwordRules.Energy(p.inventory.GetItem(0).itemValue);
        SwordRuntime.Request(w,p.entityId,SwordOp.Charge,0,98,p.getHeadPosition(),p.GetLookVector());SwordRuntime.Request(w,p.entityId,SwordOp.Release,0,99,p.getHeadPosition(),p.GetLookVector());
        Check("zero duration tap spends only normal wave cost",Mathf.Abs(SwordRules.Energy(p.inventory.GetItem(0).itemValue)-(tapEnergy-15))<.01f);
        p.inventory.GetItem(0).itemValue.SetMetadata(SwordRules.EnergyKey,tapEnergy);state.NextShot=0;SwordCombat.Clear();
        SwordRuntime.Request(w,p.entityId,SwordOp.Charge,0,100,p.getHeadPosition(),p.GetLookVector());state.ChargeAt=Time.time-1.5f;
        SwordRuntime.Request(w,p.entityId,SwordOp.Release,0,101,p.getHeadPosition(),p.GetLookVector());
        Check("server full charge spends exactly 60",Mathf.Abs(SwordRules.Energy(item)-273)<.01f);
        SwordRuntime.Request(w,p.entityId,SwordOp.Release,0,101,p.getHeadPosition(),p.GetLookVector());
        Check("replayed request cannot spend again",Mathf.Abs(SwordRules.Energy(item)-273)<.01f);SwordCombat.Clear();
        p.inventory.SetItem(1,new ItemStack(ItemClass.GetItem(SwordRules.Crystal,false),5));
        SwordRuntime.Request(w,p.entityId,SwordOp.Refill,0,102,p.getHeadPosition(),p.GetLookVector());
        Check("one crystal restores 250 and consumes one",Mathf.Abs(SwordRules.Energy(item)-523)<.01f&&p.inventory.GetItem(1).count==4);
        SwordRuntime.Request(w,p.entityId,SwordOp.Refill,0,102,p.getHeadPosition(),p.GetLookVector());Check("crystal replay rejected",Mathf.Abs(SwordRules.Energy(item)-523)<.01f&&p.inventory.GetItem(1).count==4);
        Check("Damage32 integration active",AECT16RuntimeFix.HighDamageNetworking.Active);
        Check("damage saturation cannot wrap negative",SwordRules.Saturate(double.MaxValue)==int.MaxValue);
        Check("270 degree aim bounds",SwordRules.InArc(Vector3.forward,Quaternion.Euler(0,135,0)*Vector3.forward)&&!SwordRules.InArc(Vector3.forward,Quaternion.Euler(0,140,0)*Vector3.forward));
        for(int tier=0;tier<4;tier++){
            var sword=ItemClass.GetItem(SwordRules.Prefix+(16+tier),false);float value=EffectManager.GetValue(PassiveEffects.EntityDamage,sword,SwordRules.Melee[tier],p,null,FastTags<TagGroup.Global>.Parse("perkDeepCuts"));
            Log.Out("[JuqueQA] DAMAGE tier="+(16+tier)+" effective="+value+" configured="+SwordRules.Melee[tier]);
            Check("native melee damage tier "+tier,value>100000);
            var boss=EntityFactory.CreateEntity(EntityClass.FromString("AECTheExecutionerBossT"+(16+tier)),new Vector3(20+tier*5,350,0)) as EntityAlive;
            if(boss==null){Check("boss exists "+tier,false);continue;}w.SpawnEntityInWorld(boss);int before=boss.Health;
            var source=new DamageSourceEntity(EnumDamageSource.External,EnumDamageTypes.Piercing,p.entityId,Vector3.forward){AttackingItem=sword,canHitSpecialBodyParts=false};
            boss.DamageEntity(source,SwordRules.Charged[tier],false,0);int lost=before-boss.Health;
            Log.Out("[JuqueQA] BOSS tier="+(16+tier)+" health="+before+" chargedLoss="+lost+" idealChargedSeconds="+(lost>0?(3.0*before/lost).ToString("F2"):"none"));Check("boss actual damage "+tier,lost>65535);
            before=boss.Health;SwordCombat.Fire(w,p,sword,boss.position-Vector3.forward*5+Vector3.up*.9f,Vector3.forward,1);
            for(int tick=0;tick<12;tick++)SwordCombat.Tick(w,.1f);
            Check("server wave hits once at body damage "+tier,before-boss.Health==lost);
            w.RemoveEntity(boss.entityId,EnumRemoveEntityReason.Despawned);
        }
        JuqueCombatAudit.Run(p,Check);
        w.RemoveEntity(p.entityId,EnumRemoveEntityReason.Despawned);
    }
    void Flight(EntityJuque v)
    {
        var auto=Physics.simulationMode;var rb=v.vehicleRB;GameObject wall=null;
        try{
            Physics.simulationMode=SimulationMode.Script;rb.isKinematic=false;rb.useGravity=true;v.RBActive=true;v.isEntityRemote=false;rb.velocity=rb.angularVelocity=Vector3.zero;rb.rotation=Quaternion.identity;v.Throttle=1;v.Vertical=v.Turn=0;v.Boost=false;
            for(int i=0;i<180;i++)FlightTick(v);Check("cruise speed 26 m/s (native physics)",Mathf.Abs(rb.velocity.z-26)<1);float altitude=v.position.y;
            v.Throttle=0;for(int i=0;i<40;i++)FlightTick(v);Check("80 percent inertia dissipates within 0.8 s",rb.velocity.magnitude<5.2f);Check("hover retains altitude",Mathf.Abs(v.position.y-altitude)<.1f);
            v.Throttle=1;v.Boost=true;for(int i=0;i<180;i++)FlightTick(v);Check("boost speed 40 m/s",Mathf.Abs(rb.velocity.z-40)<1);
            v.Throttle=-1;v.Boost=false;for(int i=0;i<180;i++)FlightTick(v);Check("reverse speed 8 m/s",Mathf.Abs(rb.velocity.z+8)<.5f);
            v.Throttle=0;v.Vertical=1;for(int i=0;i<100;i++)FlightTick(v);Check("ascent speed 8 m/s",Mathf.Abs(rb.velocity.y-8)<.5f);
            v.Vertical=-1;for(int i=0;i<100;i++)FlightTick(v);Check("descent speed 6 m/s",Mathf.Abs(rb.velocity.y+6)<.5f);
            v.Energy=0;v.Boost=true;v.Throttle=1;for(int i=0;i<100;i++)FlightTick(v);Check("empty spirit disables acceleration and descends",Mathf.Abs(rb.velocity.z)<1&&rb.velocity.y<-.5f&&rb.velocity.y>-3);
            v.Energy=333;v.Vertical=0;v.Throttle=1;v.Boost=true;rb.velocity=Vector3.forward*40;
            wall=new GameObject("Juque collision QA");wall.layer=16;wall.transform.position=rb.position+Vector3.forward*8+Vector3.up;wall.AddComponent<BoxCollider>().size=new Vector3(20,6,1);Physics.SyncTransforms();float boundary=wall.transform.position.z-.5f;
            for(int i=0;i<100;i++)FlightTick(v);Check("boost collision cannot cross solid wall",rb.position.z<boundary);Check("rider and sword colliders active",rb.GetComponentsInChildren<CapsuleCollider>().Any(c=>c.enabled)&&rb.GetComponentsInChildren<BoxCollider>().Any(c=>c.enabled));
        }finally{if(wall!=null)Object.DestroyImmediate(wall);Physics.simulationMode=auto;rb.velocity=rb.angularVelocity=Vector3.zero;v.Throttle=v.Vertical=v.Turn=0;v.Boost=false;}
    }
    void FlightTick(EntityJuque v){v.InputAt=Time.time;v.StepFlight(.02f);Physics.Simulate(.02f);v.SetPosition(v.vehicleRB.position+Origin.position);Physics.SyncTransforms();}
    void Render(EntityJuque v)
    {
        var visual=v.vehicleRB.transform.Find("JuqueVisual");for(var t=visual;t!=null;t=t.parent)t.gameObject.SetActive(true);visual.gameObject.layer=30;
        var c=new GameObject("Juque QA camera").AddComponent<Camera>();c.cullingMask=1<<30;c.targetTexture=new RenderTexture(960,640,24);c.clearFlags=CameraClearFlags.SolidColor;c.backgroundColor=new Color(.035f,.065f,.085f);c.fieldOfView=35;
        c.transform.position=visual.position+new Vector3(3,3,-4);c.transform.LookAt(visual.position);
        var light=new GameObject("Juque QA light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=3;light.transform.rotation=Quaternion.Euler(35,-25,0);var ambient=RenderSettings.ambientMode;var color=RenderSettings.ambientLight;RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.6f,.65f,.7f);
        c.Render();var old=RenderTexture.active;RenderTexture.active=c.targetTexture;var texture=new Texture2D(960,640,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,960,640),0,0);texture.Apply();System.IO.File.WriteAllBytes(System.IO.Path.Combine(SwordMod.Root,"qa-model.png"),ImageConversion.EncodeToPNG(texture));RenderTexture.active=old;
        Log.Out("[JuqueQA] material color="+visual.GetComponent<Renderer>().sharedMaterial.color+" triangles="+visual.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3);
        RenderSettings.ambientMode=ambient;RenderSettings.ambientLight=color;Object.Destroy(texture);Object.Destroy(c.targetTexture);Object.Destroy(c.gameObject);Object.Destroy(light.gameObject);visual.gameObject.layer=v.vehicleRB.gameObject.layer;
    }
}
