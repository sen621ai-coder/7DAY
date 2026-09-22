using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Xml.Serialization;
using UnityEngine;
using HarmonyLib;
namespace YFPhoenix {
 public sealed class PhoenixMod:IModApi {
  public void InitMod(Mod mod){PhoenixVisual.ModPath=mod.Path;new Harmony("yf.phoenix.fire").Patch(AccessTools.Method(typeof(GameManager),"ExplosionClient"),postfix:new HarmonyMethod(typeof(PhoenixFire),nameof(PhoenixFire.SynchronizeLifetime)));ModEvents.GameUpdate.RegisterHandler(PhoenixEncounter.Update);Log.Out("[PhoenixBoss] 0.1.3 T16-T19 blood moon finale loaded; registry-based entity lookup.");}
 }
 public sealed class EncounterState {public int Night=-1,LastConsumedNight=-1;public bool Armed;public ulong Deadline;public List<string> Participants=new List<string>();}
 public static class PhoenixRules {
  public static int Tier(int stage)=>stage>=480000?19:stage>=380000?18:stage>=280000?17:stage>=180000?16:0;
  public static int Night(int day,int hour)=>hour<12?day-1:day;
  public static bool InWindow(int night,int day)=>day==night||day==night+1;
 }
 public static class PhoenixEncounter {
  static World world;static EncounterState state;static string file;static float next,nextReason;static bool blocked;
  static readonly XmlSerializer serializer=new XmlSerializer(typeof(EncounterState));
  static string Key(EntityPlayer p)=>p.PersistentPlayerData?.PrimaryId?.CombinedString;
  static void Save(){string temp=file+".tmp";using(var s=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)){serializer.Serialize(s,state);s.Flush(true);}if(File.Exists(file))File.Replace(temp,file,file+".bak");else File.Move(temp,file);}
  public static void Update(ref ModEvents.SGameUpdateData data){
   if(ConnectionManager.Instance==null||!ConnectionManager.Instance.IsServer||GameManager.Instance?.IsPaused()==true||Time.realtimeSinceStartup<next)return;next=Time.realtimeSinceStartup+2;
   var w=GameManager.Instance.World;if(w==null||w.IsRemote())return;
   try{
    if(world!=w){world=w;blocked=false;file=Path.Combine(GameIO.GetSaveGameDir(),"phoenix-bloodmoon.xml");if(File.Exists(file)){using(var s=File.OpenRead(file))state=(EncounterState)serializer.Deserialize(s);}else state=new EncounterState();}
    if(blocked||!GameStats.GetBool(EnumGameStats.EnemySpawnMode))return;
    ulong time=w.GetWorldTime();int day=GameUtils.WorldTimeToDays(time),night=PhoenixRules.Night(day,GameUtils.WorldTimeToHours(time));
    if(w.isEventBloodMoon){
     if(night<=state.LastConsumedNight)return;
     bool dirty=false;if(state.Night!=night){state.Night=night;state.Armed=false;state.Deadline=0;state.Participants.Clear();dirty=true;}
     foreach(var p in w.Players.list){if(p==null||p.IsDead()||PhoenixRules.Tier(p.gameStage)==0)continue;string key=Key(p);if(!string.IsNullOrEmpty(key)&&!state.Participants.Contains(key)){state.Participants.Add(key);state.Armed=true;dirty=true;}}
     if(dirty)Save();return;
    }
    if(!state.Armed)return;
    if(!PhoenixRules.InWindow(state.Night,day)){Consume();return;}
    // isEventBloodMoon can briefly become false during the night. Do not start
    // the finale timeout until the recorded night has actually reached dawn.
    ulong dawn=GameUtils.DayTimeToWorldTime(state.Night+1,w.DawnHour,0);
    if(time<dawn){if(state.Deadline!=0){state.Deadline=0;Save();}return;}
    // Repair a pre-dawn deadline written by older versions.
    if(state.Deadline<dawn){state.Deadline=time+3000;Save();Log.Out("[PhoenixBoss] Dawn finale pending night="+state.Night);}
    if(time>state.Deadline){Log.Out("[PhoenixBoss] Finale expired: no eligible participant or loaded spawn site night="+state.Night);Consume();return;}
    var target=w.Players.list.Where(p=>p!=null&&!p.IsDead()&&PhoenixRules.Tier(p.gameStage)>0&&state.Participants.Contains(Key(p))&&!w.IsWithinTraderArea(new Vector3i(p.position))).OrderByDescending(p=>p.gameStage).FirstOrDefault();
    if(target==null){Reason("Waiting for a living T16-T19 participant outside trader protection");return;}
    if(w.Entities.list.OfType<EntityPhoenixBoss>().Any(e=>!e.IsDead())){Consume();return;}
    Vector3 site=Vector3.zero;bool found=false;
    for(int i=0;i<12;i++){float a=i*Mathf.PI/6;var candidate=target.position+new Vector3(Mathf.Cos(a)*40,0,Mathf.Sin(a)*40);if(!w.IsChunkAreaLoaded(candidate)||w.IsWithinTraderArea(new Vector3i(candidate)))continue;candidate.y=Mathf.Max(target.position.y+12,w.GetHeightAt(candidate.x,candidate.z)+12);if(candidate.y>245)continue;if(!w.GetBlock(new Vector3i(candidate)).isair||!w.GetBlock(new Vector3i(candidate+Vector3.up*2)).isair)continue;site=candidate;found=true;break;}
    if(!found){Reason("Waiting for loaded open air near participant entity="+target.entityId);return;}
    int tier=PhoenixRules.Tier(target.gameStage);string entityName="yfPhoenixBossT"+tier;
    int id=PhoenixEntityLookup.Resolve(entityName);
    var boss=EntityFactory.CreateEntity(id,site) as EntityPhoenixBoss;if(boss==null)throw new Exception("Phoenix entity construction failed: name="+entityName+" id="+id);
    // Reserve before spawning: a crash may skip a finale, but never awards a second boss for the same night.
    Consume();w.SpawnEntityInWorld(boss);boss.SetAttackTarget(target,1200);boss.SetRevengeTarget(target);
    GameManager.Instance.ChatMessageServer(null,EChatType.Global,-1,"[血月终章] T"+tier+" 焚天凤凰降临！位置："+Mathf.RoundToInt(site.x)+", "+Mathf.RoundToInt(site.z),null,EMessageSender.Server,GeneratedTextManager.BbCodeSupportMode.NotSupported);
    Log.Out("[PhoenixBoss] Spawned T"+tier+" entity="+boss.entityId+" night="+state.Night+" pos="+site);
   }catch(Exception ex){blocked=true;Log.Error("[PhoenixBoss] Disabled for this session: "+ex);}
  }
  static void Consume(){state.Armed=false;state.LastConsumedNight=Math.Max(state.LastConsumedNight,state.Night);Save();}
  static void Reason(string reason){if(Time.realtimeSinceStartup<nextReason)return;nextReason=Time.realtimeSinceStartup+30;Log.Out("[PhoenixBoss] "+reason);}
 }
 public static class PhoenixFire {
  // Native explosion packets do not carry Duration. Match the server's 30s
  // TemporaryObject lifetime on remote clients without applying damage twice.
  public static void SynchronizeLifetime(int __2,int __6,GameObject __result){
   if(__2!=10||__result==null||!(GameManager.Instance?.World?.GetEntity(__6) is EntityPhoenixBoss))return;
   __result.GetComponent<TemporaryObject>()?.SetLife(30f);
  }
 }
 public sealed class EntityPhoenixBoss:EntityVulture {
  float nextVolley,fireUntil,nextTarget,nextRing;bool firing,radial;Vector3 radialDirection;EntityPlayer fireTarget;
  bool Eligible(EntityPlayer p)=>p!=null&&!p.IsDead()&&(p.position-position).sqrMagnitude<10000&&!world.IsWithinTraderArea(new Vector3i(p.position));
  public override bool IsAttackValid(){
   if(!firing)return base.IsAttackValid();
   // Custom visual animation must not inherit a stuck hidden-carrier attack gate.
   return !IsDead()&&!Electrocuted&&bodyDamage.CurrentStun!=EnumEntityStunType.Prone&&bodyDamage.CurrentStun!=EnumEntityStunType.Kneel;
  }
  public override bool UseHoldingItem(int action,bool released){if(action==0)return false;return base.UseHoldingItem(action,released);}
  public override Vector3 GetLookVector(){if(radial)return radialDirection;if(firing&&fireTarget!=null)return (fireTarget.position+Vector3.up-position).normalized;return base.GetLookVector();}
  ItemActionVomit.ItemActionDataVomit AttackData=>inventory?.holdingItemData?.actionData[1] as ItemActionVomit.ItemActionDataVomit;
  void ResetShot(){
   UseHoldingItem(1,true);
   var action=inventory.holdingItem.Actions[1] as ItemActionRanged;
   if(action!=null&&AttackData!=null)action.ResetBurstShot(AttackData);
  }
  void Fire(){
   var action=AttackData;if(action==null)return;
   action.numWarningsPlayed=999;action.muzzle=emodel.GetHeadTransform();
   // Ranged OnHoldingUpdate recalculates this from shared item effects. This
   // boss has a fixed, explicit fire interval independent of melee modifiers.
   action.Delay=1.2f;
   UseHoldingItem(1,false);
  }
  void FireRing(float now){
   nextRing=now+20+rand.RandomFloat*20;
   bool wasFiring=firing;firing=true;
   try{
    radial=true;float offset=rand.RandomFloat*Mathf.PI*2;
    for(int i=0;i<12;i++){
     ResetShot();float angle=offset+i*Mathf.PI/6;
     // Downward component lays the fire around the boss instead of losing it
     // into the sky. All twelve directions use the native replicated projectile.
     radialDirection=new Vector3(Mathf.Cos(angle),-.55f,Mathf.Sin(angle)).normalized;
     AttackData.m_LastShotTime=now-10;
     Fire();
    }
   }finally{ResetShot();radial=false;firing=wasFiring;}
  }
  public override void updateTasks(){
   isAttack2On=false;attack2Delay=2;attackDelay=2;
   base.updateTasks();
   if(world==null||world.IsRemote()||IsDead()||GamePrefs.GetBool(EnumGamePrefs.DebugStopEnemiesMoving))return;
   float now=Time.time;
   if(nextRing==0)nextRing=now+20+rand.RandomFloat*20;
   if(!Eligible(fireTarget)||now>=nextTarget){
    var choices=world.Players.list.Where(Eligible).ToList();
    if(choices.Count>1)choices.Remove(fireTarget);
    fireTarget=choices.Count==0?null:choices[Mathf.Min(choices.Count-1,(int)(rand.RandomFloat*choices.Count))];
    nextTarget=now+6;
   }
   if(fireTarget==null){if(firing)ResetShot();firing=false;return;}
   SetAttackTarget(fireTarget,600);SetRevengeTarget(fireTarget);
   // Orbit above the target. Native entity physics still handles collision and
   // replication; neither movement nor fire is simulated by remote clients.
   float orbit=now*.18f+entityId;
   Vector3 station=fireTarget.position+new Vector3(Mathf.Cos(orbit)*24,16,Mathf.Sin(orbit)*24);
   station.y=Mathf.Min(245,Mathf.Max(station.y,world.GetHeightAt(station.x,station.z)+10));
   var delta=station-position;motion=Vector3.MoveTowards(motion,delta.normalized*Mathf.Min(.4f,delta.magnitude*.04f),.12f);
   if((fireTarget.position-position).sqrMagnitude>3600){if(firing)ResetShot();firing=false;return;}
   if(!IsDead()&&!Electrocuted&&bodyDamage.CurrentStun!=EnumEntityStunType.Prone&&bodyDamage.CurrentStun!=EnumEntityStunType.Kneel&&now>=nextRing&&AttackData!=null)FireRing(now);
   if(!firing&&now>=nextVolley){ResetShot();firing=true;fireUntil=now+2.6f;nextVolley=now+6;}
   if(!firing)return;
   if(now>=fireUntil){ResetShot();firing=false;return;}
   SetLookPosition(fireTarget.position+Vector3.up);Fire();
  }
  public override bool IsSavedToFile()=>true;
  public override string LocalizedEntityName=>"焚天凤凰 · T"+EntityClass.GetEntityClassName(entityClass).Replace("yfPhoenixBossT","");
  public override void PostInit(){base.PostInit();foreach(var a in GetComponentsInChildren<Animator>(true))a.cullingMode=AnimatorCullingMode.AlwaysAnimate;gameObject.AddComponent<PhoenixVisual>().Owner=this;}
 }
 public sealed class PhoenixVisual:MonoBehaviour {
  public static string ModPath;static AssetBundle bundle;public EntityPhoenixBoss Owner;
  GameObject model;Transform carrier,pelvis;Renderer[] originals;bool failed;
  readonly List<Tuple<Transform,Transform,CapsuleCollider,float>> hitboxes=new List<Tuple<Transform,Transform,CapsuleCollider,float>>();
  void AddHitbox(string from,string to,float radius,Collider template){
   var bones=model.GetComponentsInChildren<Transform>();var a=bones.FirstOrDefault(t=>t.name==from);var b=bones.FirstOrDefault(t=>t.name==to);
   if(a==null||b==null)throw new Exception("Missing hitbox bone "+from+" / "+to);
   var go=new GameObject("PhoenixHit_"+from);go.transform.SetParent(Owner.transform,false);go.layer=template.gameObject.layer;go.tag=template.tag;
   go.AddComponent<RootTransformRefEntity>().RootTransform=Owner.transform;
   var collider=go.AddComponent<CapsuleCollider>();collider.direction=2;collider.isTrigger=template.isTrigger;
   hitboxes.Add(Tuple.Create(a,b,collider,radius));
  }
  void LateUpdate(){
   if(Owner==null||failed)return;
   if(model==null){
    carrier=Owner.emodel?.GetModelTransform();if(carrier==null)return;
    var templates=carrier.GetComponentsInChildren<Collider>(true).Where(c=>c.tag.StartsWith("E_BP_")).ToArray();if(templates.Length==0)return;
    try{
     const string asset="Assets/Phoenix/PhoenixBoss.prefab";
     if(bundle==null)bundle=AssetBundle.GetAllLoadedAssetBundles().FirstOrDefault(b=>b.Contains(asset))??AssetBundle.LoadFromFile(Path.Combine(ModPath,"Resources/phoenix.unity3d"));
     var prefab=bundle?.LoadAsset<GameObject>(asset);if(prefab==null)throw new Exception("Missing phoenix asset");
     originals=carrier.GetComponentsInChildren<Renderer>(true);model=Instantiate(prefab,Owner.transform);
     // Never copy the imported vulture carrier scale: it is not a world-size
     // multiplier and can make the replacement many times larger than its hits.
     var parent=Owner.transform.lossyScale;
     model.transform.localScale=new Vector3(1/parent.x,1/parent.y,1/parent.z);
     foreach(var a in model.GetComponentsInChildren<Animation>()){a.wrapMode=WrapMode.Loop;a.Play();a[a.clip.name].time=.8f;a.Sample();}
     // Imported skinned bounds are not the animated mesh bounds. Normalize
     // actual skinned vertices after sampling, not the FBX's stale AABB.
     var baked=new Mesh();var bounds=new Bounds();bool first=true;
     foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>()){
      skin.BakeMesh(baked);
      foreach(var v in baked.vertices){var point=model.transform.InverseTransformPoint(skin.transform.TransformPoint(v));if(first){bounds=new Bounds(point,Vector3.zero);first=false;}else bounds.Encapsulate(point);}
     }
     Destroy(baked);float extent=Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z));
     if(first||extent<.001f)throw new Exception("Invalid animated Phoenix bounds");
     float fit=6/extent;model.transform.localScale=new Vector3(fit/parent.x,fit/parent.y,fit/parent.z);
     pelvis=model.GetComponentsInChildren<Transform>().Single(t=>t.name=="B_Pelvis");
     var body=templates.FirstOrDefault(c=>!c.tag.ToLowerInvariant().Contains("head"))??templates[0];
     var head=templates.FirstOrDefault(c=>c.tag.ToLowerInvariant().Contains("head"))??body;
     AddHitbox("B_Pelvis","B_Spine",.45f,body);AddHitbox("B_Spine","b_Head",.35f,head);
     foreach(string side in new[]{"Left","Right"}){
      AddHitbox("B_"+side+"_Wing_0","B_"+side+"_Wing_2",.35f,body);
      AddHitbox("B_"+side+"_Wing_2","B_"+side+"_Wing_8",.4f,body);
     }
     AddHitbox("B_Tail_0","B_Tail_3",.35f,body);AddHitbox("B_Tail_3","B_Tail_5",.3f,body);
     foreach(var r in originals)r.enabled=false;
     if(GameManager.IsDedicatedServer)foreach(var r in model.GetComponentsInChildren<Renderer>())r.enabled=false;
     Log.Out("[PhoenixBoss] Animated model and eight body/wing hitboxes attached entity="+Owner.entityId+" carrierScale="+carrier.lossyScale);
    }catch(Exception ex){failed=true;Log.Error("[PhoenixBoss] Model failed: "+ex);return;}
   }
   model.transform.rotation=Owner.transform.rotation;
   model.transform.position+=Owner.transform.position+Vector3.up*.4f-pelvis.position;
   foreach(var r in originals)if(r!=null)r.enabled=false;
   var scale=Owner.transform.lossyScale;
   foreach(var entry in hitboxes){
    var a=entry.Item1.position;var b=entry.Item2.position;var c=entry.Item3;
    c.transform.localScale=new Vector3(1/scale.x,1/scale.y,1/scale.z);c.transform.position=(a+b)*.5f;
    if((b-a).sqrMagnitude>.0001f)c.transform.rotation=Quaternion.LookRotation(b-a);
    c.radius=entry.Item4;c.height=Vector3.Distance(a,b)+2*c.radius;c.enabled=!Owner.IsDead();
   }
   if(Owner.IsDead())foreach(var a in model.GetComponentsInChildren<Animation>())a.Stop();
  }
  void OnDestroy(){if(model!=null)Destroy(model);foreach(var entry in hitboxes)if(entry.Item3!=null)Destroy(entry.Item3.gameObject);}
 }
}
