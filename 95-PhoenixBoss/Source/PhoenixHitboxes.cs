using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace YFPhoenix {
 // Each box is fitted in its dominant skin bone's local space. It then follows
 // the same animation as the visible mesh, including on a headless server.
 public sealed class PhoenixHitboxes:MonoBehaviour {
  public readonly Dictionary<string,Transform> Hits=new Dictionary<string,Transform>();
  public readonly List<BoxCollider> Colliders=new List<BoxCollider>();
  public static int DominantBone(BoneWeight w){int bone=w.boneIndex0;float weight=w.weight0;if(w.weight1>weight){bone=w.boneIndex1;weight=w.weight1;}if(w.weight2>weight){bone=w.boneIndex2;weight=w.weight2;}if(w.weight3>weight)bone=w.boneIndex3;return bone;}
  static bool IsHead(Transform bone)=>bone.name=="b_Head"||bone.name=="B_Jaw";
  static void Include(Dictionary<Transform,Bounds> bounds,Transform[] bones,Matrix4x4[] poses,int index,Vector3 vertex){
   if(index<0||index>=bones.Length||index>=poses.Length||bones[index]==null)return;
   var bone=bones[index];var point=poses[index].MultiplyPoint3x4(vertex);Bounds bound;
   if(bounds.TryGetValue(bone,out bound))bound.Encapsulate(point);else bound=new Bounds(point,Vector3.zero);bounds[bone]=bound;
  }
  public void Build(EntityPhoenixBoss owner){
   var bounds=new Dictionary<Transform,Bounds>();
   foreach(var renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true)){
    var mesh=renderer.sharedMesh;if(mesh==null||!mesh.isReadable)throw new Exception("Phoenix mesh must be readable for hitboxes");
    var vertices=mesh.vertices;var weights=mesh.boneWeights;var poses=mesh.bindposes;var bones=renderer.bones;
    if(vertices.Length!=weights.Length)throw new Exception("Phoenix mesh has no complete skin weights");
    for(int i=0;i<vertices.Length;i++){
     var w=weights[i];Include(bounds,bones,poses,DominantBone(w),vertices[i]);
     // Cover blended joints as well as rigid bone regions; otherwise wing and
     // neck vertices can fall through the gaps during the flapping cycle.
     if(w.weight0>=.25f)Include(bounds,bones,poses,w.boneIndex0,vertices[i]);
     if(w.weight1>=.25f)Include(bounds,bones,poses,w.boneIndex1,vertices[i]);
     if(w.weight2>=.25f)Include(bounds,bones,poses,w.boneIndex2,vertices[i]);
     if(w.weight3>=.25f)Include(bounds,bones,poses,w.boneIndex3,vertices[i]);
    }
   }
   // Hierarchy traversal gives identical names on host and clients; do not use
   // dictionary iteration order or instance IDs in networked hit names.
   foreach(var bone in GetComponentsInChildren<Transform>(true)){
    Bounds bound;if(!bounds.TryGetValue(bone,out bound))continue;
    var hit=new GameObject("yfPhoenixHit_"+Colliders.Count.ToString("D3"));hit.transform.SetParent(bone,false);
    hit.tag=IsHead(bone)?"E_BP_Head":"E_BP_Body";hit.layer=0;
    hit.AddComponent<RootTransformRefEntity>().RootTransform=owner.transform;
    var box=hit.AddComponent<BoxCollider>();box.center=bound.center;
    Vector3 size=bound.size*1.12f,scale=bone.lossyScale;
    // Keep thin feather surfaces hittable, without enlarging the whole bird.
    box.size=new Vector3(Mathf.Max(size.x,.04f/Mathf.Max(Mathf.Abs(scale.x),.00001f)),Mathf.Max(size.y,.04f/Mathf.Max(Mathf.Abs(scale.y),.00001f)),Mathf.Max(size.z,.04f/Mathf.Max(Mathf.Abs(scale.z),.00001f)));
    Hits.Add(hit.name,hit.transform);Colliders.Add(box);
   }
   if(Colliders.Count==0||!Colliders.Any(c=>c.CompareTag("E_BP_Head")))throw new Exception("Phoenix body/head hitboxes missing");
  }
  public static bool ResolveHit(EModelBase __instance,DamageSource __0,ref Transform __result){
   string name=__0?.getHitTransformName();if(name==null||!name.StartsWith("yfPhoenixHit_"))return true;
   var owner=__instance.entity as EntityPhoenixBoss;if(owner==null)return true;
   var boxes=owner.GetComponent<PhoenixVisual>()?.Hitboxes;
   if(boxes==null||!boxes.Hits.TryGetValue(name,out __result))return true;
   return false;
  }
 }
}
