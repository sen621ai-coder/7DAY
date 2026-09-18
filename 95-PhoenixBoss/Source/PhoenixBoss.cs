using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Xml.Serialization;
using UnityEngine;
using HarmonyLib;
namespace YFPhoenix {
 public sealed class PhoenixMod:IModApi {
  public void InitMod(Mod mod){PhoenixVisual.ModPath=mod.Path;new Harmony("yf.phoenix.fire").Patch(AccessTools.Method(typeof(GameManager),"ExplosionClient"),postfix:new HarmonyMethod(typeof(PhoenixFire),nameof(PhoenixFire.SynchronizeLifetime)));ModEvents.GameUpdate.RegisterHandler(PhoenixEncounter.Update);Log.Out("[PhoenixBoss] T16-T19 blood moon finale loaded.");}
 }
 public sealed class EncounterState {public int Night=-1,LastConsumedNight=-1;public bool Armed;public ulong Deadline;public List<string> Participants=new List<string>();}
 public static class PhoenixRules {
  public static int Tier(int stage)=>stage>=480000?19:stage>=380000?18:stage>=280000?17:stage>=180000?16:0;
  public static int Night(int day,int hour)=>hour<12?day-1:day;
  public static bool InWindow(int night,int day)=>day==night||day==night+1;
 }
 public static class PhoenixEncounter {
  static World world;static EncounterState state;static string file;static float next;static bool blocked;
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
    if(state.Deadline==0){state.Deadline=time+1000;Save();} // one in-game hour to find an eligible loaded participant
    if(time>state.Deadline){Consume();return;}
    var target=w.Players.list.Where(p=>p!=null&&!p.IsDead()&&PhoenixRules.Tier(p.gameStage)>0&&state.Participants.Contains(Key(p))&&!w.IsWithinTraderArea(new Vector3i(p.position))).OrderByDescending(p=>p.gameStage).FirstOrDefault();
    if(target==null)return;
    if(w.Entities.list.OfType<EntityPhoenixBoss>().Any(e=>!e.IsDead())){Consume();return;}
    Vector3 site=Vector3.zero;bool found=false;
    for(int i=0;i<12;i++){float a=i*Mathf.PI/6;var candidate=target.position+new Vector3(Mathf.Cos(a)*40,0,Mathf.Sin(a)*40);if(!w.IsChunkAreaLoaded(candidate)||w.IsWithinTraderArea(new Vector3i(candidate)))continue;candidate.y=Mathf.Max(target.position.y+12,w.GetHeightAt(candidate.x,candidate.z)+12);if(candidate.y>245)continue;if(!w.GetBlock(new Vector3i(candidate)).isair||!w.GetBlock(new Vector3i(candidate+Vector3.up*2)).isair)continue;site=candidate;found=true;break;}
    if(!found)return;
    int tier=PhoenixRules.Tier(target.gameStage),id=EntityClass.FromString("yfPhoenixBossT"+tier);if(id<0)throw new Exception("Missing phoenix entity class");
    var boss=EntityFactory.CreateEntity(id,site) as EntityPhoenixBoss;if(boss==null)throw new Exception("Phoenix entity construction failed");
    // Reserve before spawning: a crash may skip a finale, but never awards a second boss for the same night.
    Consume();w.SpawnEntityInWorld(boss);boss.SetAttackTarget(target,1200);boss.SetRevengeTarget(target);
    GameManager.Instance.ChatMessageServer(null,EChatType.Global,-1,"[血月终章] T"+tier+" 焚天凤凰降临！位置："+Mathf.RoundToInt(site.x)+", "+Mathf.RoundToInt(site.z),null,EMessageSender.Server,GeneratedTextManager.BbCodeSupportMode.NotSupported);
    Log.Out("[PhoenixBoss] Spawned T"+tier+" entity="+boss.entityId+" night="+state.Night+" pos="+site);
   }catch(Exception ex){blocked=true;Log.Error("[PhoenixBoss] Disabled for this session: "+ex);}
  }
  static void Consume(){state.Armed=false;state.LastConsumedNight=Math.Max(state.LastConsumedNight,state.Night);Save();}
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
  public override void PostInit(){base.PostInit();foreach(var a in GetComponentsInChildren<Animator>(true))a.cullingMode=AnimatorCullingMode.AlwaysAnimate;if(!GameManager.IsDedicatedServer)gameObject.AddComponent<PhoenixVisual>().Owner=this;}
 }
 public sealed class PhoenixVisual:MonoBehaviour {
  public static string ModPath;static AssetBundle bundle;public EntityPhoenixBoss Owner;GameObject model;Transform carrier;Renderer[] originals;bool failed;
  void LateUpdate(){if(Owner==null||failed)return;
   if(model==null){carrier=Owner.emodel?.GetModelTransform();if(carrier==null||carrier.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length==0)return;
    try{const string asset="Assets/Phoenix/PhoenixBoss.prefab";if(bundle==null)bundle=AssetBundle.GetAllLoadedAssetBundles().FirstOrDefault(b=>b.Contains(asset))??AssetBundle.LoadFromFile(Path.Combine(ModPath,"Resources/phoenix.unity3d"));var prefab=bundle?.LoadAsset<GameObject>(asset);if(prefab==null)throw new Exception("Missing phoenix asset");originals=carrier.GetComponentsInChildren<Renderer>(true);model=Instantiate(prefab,carrier.parent);model.transform.localScale=carrier.localScale;foreach(var r in originals)r.enabled=false;foreach(var a in model.GetComponentsInChildren<Animation>()){a.wrapMode=WrapMode.Loop;a.Play();}Log.Out("[PhoenixBoss] Custom animated model attached entity="+Owner.entityId);
    }catch(Exception ex){failed=true;Log.Error("[PhoenixBoss] Model failed: "+ex);return;}}
   if(carrier!=null){model.transform.localPosition=carrier.localPosition;model.transform.localRotation=carrier.localRotation;}foreach(var r in originals)if(r!=null)r.enabled=false;
   if(Owner.IsDead())foreach(var a in model.GetComponentsInChildren<Animation>())a.Stop();
  }
  void OnDestroy(){if(model!=null)Destroy(model);}
 }
}
