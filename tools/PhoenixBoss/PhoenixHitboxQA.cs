using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using YFPhoenix;
using HarmonyLib;

public sealed class PhoenixHitboxQA:IModApi {
 static readonly List<string> report=new List<string>();
 static EntityPhoenixBoss boss,copy;static World world;static int phase;static float deadline;
 static string Folder=>Path.Combine(GameIO.GetUserGameDataDir(),"phoenix-hitbox-report.txt");
 public void InitMod(Mod mod){if(!Environment.GetCommandLineArgs().Contains("-yfPhoenixHitboxQA"))return;ModEvents.GameStartDone.RegisterHandler(Ready);ModEvents.GameUpdate.RegisterHandler(Update);}
 static void Check(bool ok,string msg){if(!ok)throw new Exception(msg);report.Add("PASS "+msg);}
 static void Ready(ref ModEvents.SGameStartDoneData data){if(GamePrefs.GetString(EnumGamePrefs.GameName)!="PhoenixHitboxQA_Isolated")return;world=GameManager.Instance.World;GameManager.Instance.AddChunkObserver(new Vector3(8,80,8),false,4,4);phase=1;deadline=Time.realtimeSinceStartup+120;}
 static void Update(ref ModEvents.SGameUpdateData data){if(phase==0)return;try{
  if(phase==1){if(!world.IsChunkAreaLoaded(new Vector3(8,80,8))){if(Time.realtimeSinceStartup>deadline)throw new Exception("Chunk load timeout");return;}
   GamePrefs.Set(EnumGamePrefs.DebugStopEnemiesMoving,true);
   boss=EntityFactory.CreateEntity(EntityClass.FromString("yfPhoenixBossT17"),new Vector3(8,100,8)) as EntityPhoenixBoss;world.SpawnEntityInWorld(boss);
   copy=EntityFactory.CreateEntity(EntityClass.FromString("yfPhoenixBossT17"),new Vector3(18,100,8)) as EntityPhoenixBoss;world.SpawnEntityInWorld(copy);
   phase=2;deadline=Time.realtimeSinceStartup+15;return;
  }
  var visual=boss.GetComponent<PhoenixVisual>();var boxes=visual?.Hitboxes;var remote=copy.GetComponent<PhoenixVisual>()?.Hitboxes;
  if(boxes==null||remote==null||boxes.Colliders.Count==0){if(Time.realtimeSinceStartup>deadline)throw new Exception("Model/hitboxes failed to initialize");return;}
  Check(GameManager.IsDedicatedServer,"Headless server creates animated phoenix hitboxes");
  Check(boxes.Colliders.Any(c=>c.CompareTag("E_BP_Head"))&&boxes.Colliders.Any(c=>c.CompareTag("E_BP_Body")),"Both head and body have fitted hit regions");
  Check(boxes.Hits.Keys.SequenceEqual(remote.Hits.Keys),"Hit names are deterministic between independent instances");
  var old=boss.emodel.GetModelTransform().GetComponentsInChildren<Collider>(true).Where(c=>c.tag.StartsWith("E_BP_")).ToArray();
  Check(old.Length>0&&old.All(c=>!c.enabled),"Hidden vulture detail colliders are disabled");
  foreach(var box in boxes.Colliders){
   Check(GameUtils.GetHitRootEntity(box.tag,box.transform)==boss,"Native ray hit resolves owner: "+box.name);
   var source=new DamageSourceEntity(EnumDamageSource.External,EnumDamageTypes.Piercing,-1,Vector3.forward,box.name,box.transform.position,Vector2.zero);
   Check(boss.emodel.GetHitTransform(source)==box.transform,"Native damage resolves animated bone: "+box.name);
   Check(copy.emodel.GetHitTransform(source)==remote.Hits[box.name],"Transmitted hit name resolves on second entity: "+box.name);
   EnumBodyPartHit part;EquipmentSlots slot;source.GetEntityDamageBodyPartAndEquipmentSlot(boss,out part,out slot);
   Check(part==DamageSource.TagToBodyPart(box.tag),"Native body/head classification: "+box.name+"="+part);
   var direction=box.transform.forward;var center=box.transform.TransformPoint(box.center);RaycastHit hit;
   Check(box.Raycast(new Ray(center-direction*20,direction),out hit,40),"Bullet ray intersects fitted collider: "+box.name);
   report.Add("INFO "+box.name+" bone="+box.transform.parent.name+" tag="+box.tag+" bounds="+box.bounds);
  }
  var anim=boxes.GetComponentInChildren<Animation>();anim.clip.SampleAnimation(anim.gameObject,0);
  var before=boxes.Colliders.Select(c=>c.transform.position).ToArray();anim.clip.SampleAnimation(anim.gameObject,anim.clip.length*.37f);
  Check(boxes.Colliders.Where((c,i)=>(c.transform.position-before[i]).sqrMagnitude>.0001f).Any(),"Hitboxes follow actual flapping animation");
  for(int frame=0;frame<6;frame++){
   anim.clip.SampleAnimation(anim.gameObject,anim.clip.length*frame/6f);Physics.SyncTransforms();int covered=0,total=0;
   foreach(var skin in boxes.GetComponentsInChildren<SkinnedMeshRenderer>()){
    var baked=new Mesh();skin.BakeMesh(baked,false);var vertices=baked.vertices;
    // BakeMesh(false) uses a scale-free renderer transform; do not multiply
    // its already skinned vertices by the renderer's lossy scale a second time.
    var bakedToWorld=Matrix4x4.TRS(skin.transform.position,skin.transform.rotation,Vector3.one);
    if(frame==0){var shared=skin.sharedMesh;var w=shared.boneWeights[0];var v=shared.vertices[0];var p=shared.bindposes;var b=skin.bones;
     Vector3 expected=b[w.boneIndex0].TransformPoint(p[w.boneIndex0].MultiplyPoint3x4(v))*w.weight0+b[w.boneIndex1].TransformPoint(p[w.boneIndex1].MultiplyPoint3x4(v))*w.weight1+b[w.boneIndex2].TransformPoint(p[w.boneIndex2].MultiplyPoint3x4(v))*w.weight2+b[w.boneIndex3].TransformPoint(p[w.boneIndex3].MultiplyPoint3x4(v))*w.weight3;
     Check(Vector3.Distance(expected,bakedToWorld.MultiplyPoint3x4(vertices[0]))<.02f,"Baked surface conversion agrees with independent weighted skinning");}
    var inverses=boxes.Colliders.Select(c=>c.transform.worldToLocalMatrix).ToArray();
    for(int i=0;i<vertices.Length;i+=8){total++;var point=bakedToWorld.MultiplyPoint3x4(vertices[i]);
     for(int j=0;j<boxes.Colliders.Count;j++){var c=boxes.Colliders[j];if(new Bounds(c.center,c.size).Contains(inverses[j].MultiplyPoint3x4(point))){covered++;break;}}
    }UnityEngine.Object.Destroy(baked);
   }
   float coverage=(float)covered/total;report.Add("INFO animated surface coverage frame="+frame+" fraction="+coverage);
   Check(coverage>=.95f,"At least 95% of sampled visible surface is hittable at animation frame "+frame);
  }
  var head=boxes.Colliders.First(c=>c.transform.parent.name=="b_Head");var origin=head.bounds.center+boss.transform.forward*20;
  Physics.SyncTransforms();Check(Voxel.Raycast(world,new Ray(origin,(head.bounds.center-origin).normalized),40,-538751005,-1,0),"Native firearm ray hits phoenix");
  Check(GameUtils.GetHitRootEntity(Voxel.voxelRayHitInfo.tag,Voxel.voxelRayHitInfo.transform)==boss&&Voxel.voxelRayHitInfo.tag=="E_BP_Head","Aimed frontal head shot reaches head through native firearm raycast");
  var wireSource=new DamageSourceEntity(EnumDamageSource.External,EnumDamageTypes.Piercing,-1,Vector3.forward,head.name,head.bounds.center,Vector2.zero);
  var packet=new NetPackageRangeCheckDamageEntity().Setup(copy.entityId,origin,10000,wireSource,100,false,new List<string>(),"",new ParticleEffect{debugName="",soundName="",rot=Quaternion.identity});
  using(var stream=new MemoryStream()){
   var writer=new PooledBinaryWriter();writer.SetBaseStream(stream);packet.write(writer);writer.Flush();stream.Position=0;
   var reader=new PooledBinaryReader();reader.SetBaseStream(stream);reader.ReadUInt16();var received=new NetPackageRangeCheckDamageEntity();received.read(reader);
   var hitName=(string)AccessTools.Field(typeof(NetPackageRangeCheckDamageEntity),"hitTransformName").GetValue(received);
   var receivedSource=new DamageSourceEntity(EnumDamageSource.External,EnumDamageTypes.Piercing,-1,Vector3.forward,hitName,head.bounds.center,Vector2.zero);
   EnumBodyPartHit part;EquipmentSlots slot;receivedSource.GetEntityDamageBodyPartAndEquipmentSlot(copy,out part,out slot);
   Check(hitName==head.name&&part==DamageSource.TagToBodyPart("E_BP_Head"),"Native damage packet round trip preserves remote headshot classification");
  }
  report.Add("FINISHED failures=0 colliders="+boxes.Colliders.Count);Finish();
 }catch(Exception ex){report.Add("FAIL "+ex);Finish();}}
 static void Finish(){phase=0;File.WriteAllLines(Folder,report);Log.Out("[PhoenixHitboxQA] "+report.Last());Application.Quit();}
}
