using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
namespace YFAutomation
{
 public static class ConveyorPath
 {
  public static bool IsBelt(string n)=>n!=null&&n.StartsWith("yfAutoBelt",StringComparison.Ordinal);
  public static Vector3 LocalExit(string n)=>n.EndsWith("Left")?Vector3.left:n.EndsWith("Right")?Vector3.right:n.EndsWith("Up")?new Vector3(0,1,1):n.EndsWith("Down")?new Vector3(0,-1,1):Vector3.forward;
  public static Vector3i Offset(BlockValue v,Vector3 local){var d=v.Block.shape.GetRotation(v)*local;return new Vector3i(Mathf.RoundToInt(d.x),Mathf.RoundToInt(d.y),Mathf.RoundToInt(d.z));}
  public static Vector3 Point(string n,float t){
   t=Mathf.Clamp01(t);if(n.EndsWith("Left"))return t<.5f?new Vector3(0,0,t-.5f):new Vector3(.5f-t,0,0);
   if(n.EndsWith("Right"))return t<.5f?new Vector3(0,0,t-.5f):new Vector3(t-.5f,0,0);
   return new Vector3(0,n.EndsWith("Up")?t:n.EndsWith("Down")?-t:0,t-.5f);
  }
 }
 // Native storage owns every in-flight item. A tick uses an initial source budget,
 // so a packet can never cross multiple belt segments in the same tick.
 public static class ConveyorTransfer
 {
  public static int Move(ItemStack[] a,ItemStack[] b,Func<int,bool> skipA,Func<int,bool> skipB,int budget,int capacity,Func<ItemValue,int> stackLimit,int filter=0){
   if(a==null||b==null||ReferenceEquals(a,b)||budget<=0)return 0;
   for(int i=0;i<a.Length;i++){
    var s=a[i];if(s==null||s.IsEmpty()||skipA(i)||filter!=0&&s.itemValue.type!=filter)continue;
    for(int pass=0;pass<2;pass++)for(int j=0;j<b.Length;j++){
     if(skipB(j))continue;var d=b[j];bool empty=d==null||d.IsEmpty();if(pass==0&&empty||pass==1&&!empty||!empty&&!s.itemValue.Equals(d.itemValue))continue;
     int max=Math.Min(capacity,stackLimit(s.itemValue));int count=Math.Min(Math.Min(16,budget),Math.Min(s.count,max-(empty?0:d.count)));if(count<=0)continue;
     var from=s.Clone();var to=empty?s.Clone():d.Clone();from.count-=count;to.count=(empty?0:d.count)+count;a[i]=from.count==0?ItemStack.Empty:from;b[j]=to;return count;
    }
   }return 0;
  }
 }
 public static class Conveyors
 {
  static World world;static float next;static readonly Dictionary<Vector3i,TileEntityComposite> belts=new Dictionary<Vector3i,TileEntityComposite>();
  public static void Observe(TileEntityComposite te,World w){if(w==null||w.IsRemote()||!ConveyorPath.IsBelt(te.block.GetBlockName()))return;if(world!=w){world=w;belts.Clear();next=0;}belts[te.ToWorldPos()]=te;}
  static string Owner(TileEntityComposite t)=>(t?.GetFeature<TEFeatureLockable>()?.GetOwner()??t?.Owner)?.CombinedString;
  static Vector3i Add(Vector3i a,Vector3i b)=>Logistics.Add(a,b);
  static Vector3i Exit(TileEntityComposite t)=>Add(t.ToWorldPos(),ConveyorPath.Offset(world.GetBlock(t.ToWorldPos()),ConveyorPath.LocalExit(t.block.GetBlockName())));
  static Vector3i Entry(TileEntityComposite t)=>Add(t.ToWorldPos(),ConveyorPath.Offset(world.GetBlock(t.ToWorldPos()),Vector3.back));
  static bool Matches(TileEntityComposite a,TileEntityComposite b){var p=a.ToWorldPos();var e=Entry(b);return Exit(a)==b.ToWorldPos()&&p.x==e.x&&p.z==e.z&&TransferRules.SameOwner(Owner(a),Owner(b));}
  public static void Tick(){
   if(GameManager.Instance?.World!=world||world==null||world.IsRemote()||world.Players.Count==0||GameManager.Instance.IsPaused()||Time.realtimeSinceStartup<next)return;next=Time.realtimeSinceStartup+1;
   foreach(var p in belts.ToArray())if(world.GetTileEntity(p.Key)!=p.Value||p.Value.IsRemoving)belts.Remove(p.Key);
   // Same-chunk transactions share the existing native chunk serialization gate.
   foreach(var group in belts.Values.GroupBy(t=>world.GetChunkFromWorldPos(t.ToWorldPos()) as Chunk)){
    var chunk=group.Key;if(chunk==null||chunk.IsLocked)continue;
    try{lock(ChunkTransferLock.For(chunk))Step(group.OrderBy(t=>t.ToWorldPos().x).ThenBy(t=>t.ToWorldPos().y).ThenBy(t=>t.ToWorldPos().z).ToArray());}
    catch(Exception ex){Log.Error("[YFAutomation] conveyor tick paused: "+ex);}
   }
  }
  static void Step(TileEntityComposite[] nodes){
   var map=nodes.ToDictionary(t=>t.ToWorldPos());var powered=new HashSet<TileEntityComposite>();var seen=new HashSet<TileEntityComposite>();
   foreach(var root in nodes){if(seen.Contains(root))continue;var members=new List<TileEntityComposite>();var q=new Queue<TileEntityComposite>();q.Enqueue(root);seen.Add(root);bool power=false;
    while(q.Count>0){var b=q.Dequeue();members.Add(b);power|=Logistics.Powered(world,b.ToWorldPos());foreach(var n in nodes)if(!seen.Contains(n)&&(Matches(b,n)||Matches(n,b))){seen.Add(n);q.Enqueue(n);}}
    if(power&&members.Count<=32)foreach(var b in members)powered.Add(b);
   }
   var working=new Dictionary<TileEntityComposite,ItemStack[]>();var changed=new HashSet<TileEntityComposite>();var moved=new HashSet<TileEntityComposite>();
   Func<TileEntityComposite,ItemStack[]> inventory=t=>{ItemStack[] a;if(!working.TryGetValue(t,out a)){a=ProductionInventory.Clone(t.GetFeature<TEFeatureStorage>().items);working[t]=a;}return a;};
   Func<TileEntityComposite,TileEntityComposite,bool> allowed=(a,b)=>b!=null&&!b.IsRemoving&&TransferRules.SameOwner(Owner(a),Owner(b))&&TransferRules.SameChunk(a.ToWorldPos().x,a.ToWorldPos().z,b.ToWorldPos().x,b.ToWorldPos().z)&&!Logistics.Busy(b)&&b.GetFeature<TEFeatureStorage>()!=null;
   var budget=nodes.ToDictionary(t=>t,t=>t.GetFeature<TEFeatureStorage>().items.Where(s=>s!=null&&!s.IsEmpty()).Sum(s=>s.count));
   foreach(var b in nodes){if(!powered.Contains(b)||Logistics.Busy(b)||budget[b]==0)continue;var target=world.GetTileEntity(Exit(b)) as TileEntityComposite;if(!allowed(b,target))continue;
    bool belt=ConveyorPath.IsBelt(target.block.GetBlockName());string kind=target.block.GetBlockName();if(belt&&(!map.ContainsKey(target.ToWorldPos())||!Matches(b,target)||!powered.Contains(target)))continue;bool machine=MachineInventory.UsesInternal(target);if(!belt&&!machine&&kind!="yfAutoInput"&&kind!="yfAutoOutput")continue;
    var src=b.GetFeature<TEFeatureStorage>();var dst=target.GetFeature<TEFeatureStorage>();
    // Output boxes are general conveyor storage: no sample, filter or reserved first slot.
    int count=ConveyorTransfer.Move(inventory(b),inventory(target),i=>Logistics.Locked(src,i),i=>Logistics.Locked(dst,i)||machine&&(!MachineInventory.IsInput(i)||kind=="yfAutoWaterPump"),budget[b],belt?16:int.MaxValue,v=>v.ItemClass.Stacknumber.Value);
    if(count>0){changed.Add(b);changed.Add(target);moved.Add(b);if(belt)moved.Add(target);}
   }
   // Load boxes only after movement; newly loaded parcels wait for the next tick.
   foreach(var b in nodes){if(!powered.Contains(b)||Logistics.Busy(b))continue;var source=world.GetTileEntity(Entry(b)) as TileEntityComposite;if(!allowed(b,source))continue;string kind=source.block.GetBlockName();bool machine=MachineInventory.UsesInternal(source);if(!machine&&kind!="yfAutoInput"&&kind!="yfAutoOutput")continue;
    var src=source.GetFeature<TEFeatureStorage>();var dst=b.GetFeature<TEFeatureStorage>();int count=ConveyorTransfer.Move(inventory(source),inventory(b),i=>Logistics.Locked(src,i)||machine&&!MachineInventory.IsOutput(i),i=>Logistics.Locked(dst,i),16,16,v=>v.ItemClass.Stacknumber.Value);
    if(count>0){changed.Add(source);changed.Add(b);moved.Add(b);}
   }
   // Abort the entire detached batch before publishing either side of any transfer.
   if(changed.Any(t=>world.GetTileEntity(t.ToWorldPos())!=t||Logistics.Busy(t)))return;
   foreach(var t in changed){var s=t.GetFeature<TEFeatureStorage>();Array.Copy(working[t],s.items,s.items.Length);t.SetChunkModified();}
   foreach(var t in changed)t.SetModified();
   foreach(var b in nodes){if(Logistics.Busy(b))continue;var state=b.GetFeature<TEFeatureAutomationState>();if(state==null)continue;string status=!powered.Contains(b)?"缺电/线路超过32段":moved.Contains(b)?"运输中":"等待物品/出口堵塞";if(state.Job!=status||moved.Contains(b)){state.Job=status;state.Seconds+=1;b.SetChunkModified();b.SetModified();}}
  }
 }
 public sealed class ConveyorVisual : MonoBehaviour
 {
  TileEntityComposite tile;Transform packet;float received;float revision=-1;string kind;
  public static void Attach(TileEntityComposite __instance,BlockEntityData __0){
   if(GameManager.IsDedicatedServer||__0?.transform==null||!ConveyorPath.IsBelt(__instance.block.GetBlockName()))return;
   var v=__0.transform.GetComponent<ConveyorVisual>()??__0.transform.gameObject.AddComponent<ConveyorVisual>();v.Bind(__instance,__0.transform);
  }
  public void Bind(TileEntityComposite t,Transform root){tile=t;kind=t.block.GetBlockName();packet=root.GetComponentsInChildren<Transform>(true).FirstOrDefault(v=>v.name=="Cargo");}
  public static void ActivationText(Vector3i __1,BlockValue __2,ref string __result){if(!ConveyorPath.IsBelt(__2.Block.GetBlockName()))return;var t=GameManager.Instance?.World?.GetTileEntity(__1) as TileEntityComposite;var state=t?.GetFeature<TEFeatureAutomationState>();if(state!=null)__result+="\n"+state.Job+" · "+(t.GetFeature<TEFeatureStorage>()?.items.Sum(s=>s.count)??0)+" 件";}
  void Update(){if(tile==null||tile.IsRemoving||packet==null)return;var store=tile.GetFeature<TEFeatureStorage>();bool full=store!=null&&store.items.Any(s=>s!=null&&!s.IsEmpty());packet.gameObject.SetActive(full);if(!full)return;var state=tile.GetFeature<TEFeatureAutomationState>();if(state!=null&&revision!=state.Seconds){revision=state.Seconds;received=Time.time;}float t=state!=null&&state.Job=="运输中"?Mathf.Clamp01((Time.time-received)/1f):.85f;packet.localPosition=ConveyorPath.Point(kind,Mathf.Lerp(.12f,.88f,t))+Vector3.up*(kind.EndsWith("Up")||kind.EndsWith("Down")?.55f:.43f);}
 }
}
