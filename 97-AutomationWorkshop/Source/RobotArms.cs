using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace YFAutomation
{
    // Cargo lives in native storage between pickup and release, including across save/load.
    public static class RobotArms
    {
        static World world;static float next;
        static readonly Dictionary<Vector3i,TileEntityComposite> arms=new Dictionary<Vector3i,TileEntityComposite>();
        public static int Reach(string name)=>name=="yfAutoArm1"?1:name=="yfAutoArm2"?2:name=="yfAutoArm3"?3:0;
        public static void Observe(TileEntityComposite tile,World w)
        {
            if(w==null||w.IsRemote()||tile==null||Reach(tile.block.GetBlockName())==0)return;
            if(world!=w){world=w;arms.Clear();next=0;}arms[tile.ToWorldPos()]=tile;
        }
        public static void Tick()
        {
            if(world==null||world!=GameManager.Instance?.World||world.IsRemote()||world.Players.Count==0||GameManager.Instance.IsPaused()||Time.realtimeSinceStartup<next)return;
            next=Time.realtimeSinceStartup+1;
            foreach(var pair in arms.ToArray()){
                var t=pair.Value;if(world.GetTileEntity(pair.Key)!=t||t.IsRemoving){arms.Remove(pair.Key);continue;}
                try{Step(world,t);}catch(Exception e){Log.Error("[YFAutomation] robot arm paused at "+pair.Key+": "+e);}
            }
        }
        static string Owner(TileEntityComposite t)=>(t.GetFeature<TEFeatureLockable>()?.GetOwner()??t.Owner)?.CombinedString;
        static bool Current(World w,TileEntityComposite t)=>t!=null&&!t.IsRemoving&&w.GetTileEntity(t.ToWorldPos())==t&&!Logistics.Busy(t);
        static void Status(TileEntityComposite t,string text)
        {
            var s=t.GetFeature<TEFeatureAutomationState>();if(s==null||s.Job==text)return;
            s.Job=text;s.Seconds++;t.SetChunkModified();t.SetModified();
        }
        public static void Step(World w,TileEntityComposite arm)
        {
            if(w==null||w.IsRemote()||!Current(w,arm))return;
            var at=arm.ToWorldPos();var chunk=w.GetChunkFromWorldPos(at) as Chunk;if(chunk==null||chunk.IsLocked)return;
            lock(ChunkTransferLock.For(chunk)){
                if(!Current(w,arm))return;
                int reach=Reach(arm.block.GetBlockName());if(reach==0)return;
                if(!Logistics.Powered(w,at)){Status(arm,"缺电：4格内需通电供电口");return;}
                var value=w.GetBlock(at);var offset=ConveyorPath.Offset(value,Vector3.forward);
                var from=new Vector3i(at.x-offset.x*reach,at.y,at.z-offset.z*reach);
                var to=new Vector3i(at.x+offset.x*reach,at.y,at.z+offset.z*reach);
                if(!TransferRules.SameChunk(at.x,at.z,from.x,from.z)||!TransferRules.SameChunk(at.x,at.z,to.x,to.z)){Status(arm,"取放点必须与底座在同一区块");return;}
                // A raised straight corridor can bridge other belts, but cannot pass through walls.
                for(int d=-reach;d<=reach;d++){
                    var p=new Vector3i(at.x+offset.x*d,at.y+1,at.z+offset.z*d);
                    if(!w.GetBlock(p).isair){Status(arm,"上方机械臂路径被遮挡");return;}
                    p.y--;var block=w.GetBlock(p);
                    if(d!=0&&!block.isair&&!ConveyorPath.IsBelt(block.Block.GetBlockName())){Status(arm,"取放路径只允许空气或传送带");return;}
                }
                var store=arm.GetFeature<TEFeatureStorage>();if(store==null)return;
                bool carrying=store.items.Any(s=>s!=null&&!s.IsEmpty());
                var target=w.GetTileEntity(to) as TileEntityComposite;
                if(target==null||!ConveyorPath.IsBelt(target.block.GetBlockName())){Status(arm,"前方缺少目标传送带");return;}
                var source=carrying?arm:w.GetTileEntity(from) as TileEntityComposite;
                if(source==null||!carrying&&!ConveyorPath.IsBelt(source.block.GetBlockName())){Status(arm,"后方缺少来源传送带");return;}
                if(!TransferRules.SameOwner(Owner(arm),Owner(source))||!TransferRules.SameOwner(Owner(arm),Owner(target))){Status(arm,"传送带所有者不匹配");return;}
                if(!Current(w,source)||!Current(w,target)){Status(arm,"等待传送带库存关闭");return;}
                var src=source.GetFeature<TEFeatureStorage>();var dst=target.GetFeature<TEFeatureStorage>();if(src==null||dst==null)return;
                var a=ProductionInventory.Clone(src.items);var b=ProductionInventory.Clone((carrying?dst:store).items);
                int moved=ConveyorTransfer.Move(a,b,i=>Logistics.Locked(src,i),i=>Logistics.Locked(carrying?dst:store,i),16,16,v=>v.ItemClass.Stacknumber.Value);
                if(moved==0){Status(arm,carrying?"出口堵塞，持货等待":"等待来源物品");return;}
                if(carrying&&moved!=Math.Min(16,src.items.Where(s=>s!=null&&!s.IsEmpty()).Sum(s=>s.count))){Status(arm,"出口空间不足，持货等待");return;}
                if(!carrying){
                    var probe=ProductionInventory.Clone(b);var output=ProductionInventory.Clone(dst.items);
                    if(ConveyorTransfer.Move(probe,output,i=>false,i=>Logistics.Locked(dst,i),16,16,v=>v.ItemClass.Stacknumber.Value)!=moved){Status(arm,"出口空间不足，等待取货");return;}
                }
                if(!Current(w,arm)||!Current(w,source)||!Current(w,target))return;
                // Detached two-sided transaction under the native chunk serialization gate.
                Array.Copy(a,src.items,a.Length);var receiver=carrying?target:arm;var destination=carrying?dst:store;Array.Copy(b,destination.items,b.Length);
                source.SetChunkModified();receiver.SetChunkModified();source.SetModified();receiver.SetModified();
                Status(arm,carrying?(a.Any(s=>s!=null&&!s.IsEmpty())?"继续放货":"放货返回"):"取货搬运");
            }
        }
    }
    public sealed class RobotArmVisual : MonoBehaviour
    {
        TileEntityComposite tile;Transform upper,forearm,claw,parcel,joint;int reach;float revision=-1,started;string action="";
        public static void Attach(TileEntityComposite __instance,BlockEntityData __0)
        {
            if(GameManager.IsDedicatedServer||__0?.transform==null||RobotArms.Reach(__instance.block.GetBlockName())==0)return;
            var root=__0.transform;var v=root.GetComponent<RobotArmVisual>()??root.gameObject.AddComponent<RobotArmVisual>();
            v.tile=__instance;v.reach=RobotArms.Reach(__instance.block.GetBlockName());var parts=root.GetComponentsInChildren<Transform>(true);
            v.upper=parts.FirstOrDefault(t=>t.name=="UpperArm");v.forearm=parts.FirstOrDefault(t=>t.name=="Forearm");v.claw=parts.FirstOrDefault(t=>t.name=="Gripper");v.parcel=parts.FirstOrDefault(t=>t.name=="Cargo");
            v.joint=parts.FirstOrDefault(t=>t.name=="Elbow");
        }
        public static void ActivationText(Vector3i __1,BlockValue __2,ref string __result)
        {
            int r=RobotArms.Reach(__2.Block.GetBlockName());if(r==0)return;
            var tile=GameManager.Instance?.World?.GetTileEntity(__1) as TileEntityComposite;
            __result+="\n后方"+r+"格 → 前方"+r+"格 · "+(tile?.GetFeature<TEFeatureAutomationState>()?.Job??"等待供电");
        }
        static void Beam(Transform t,Vector3 a,Vector3 b,float width)
        {
            if(t==null)return;t.localPosition=(a+b)*.5f;t.localRotation=Quaternion.LookRotation(b-a);t.localScale=new Vector3(width,width,Vector3.Distance(a,b));
        }
        void Update()
        {
            if(tile==null||tile.IsRemoving||claw==null||parcel==null)return;
            var state=tile.GetFeature<TEFeatureAutomationState>();bool holding=tile.GetFeature<TEFeatureStorage>()?.items.Any(s=>s!=null&&!s.IsEmpty())==true;
            if(state!=null&&state.Seconds!=revision){bool first=revision<0;revision=state.Seconds;started=Time.time-(first?1:0);action=state.Job;}
            float progress=Mathf.SmoothStep(0,1,Mathf.Clamp01((Time.time-started)/.9f));
            float t=action=="取货搬运"?progress:action=="放货返回"?1-progress:holding?1:0;
            var hand=new Vector3(0,.52f+Mathf.Sin(t*Mathf.PI)*.8f,Mathf.Lerp(-reach,reach,t));
            var elbow=new Vector3(0,1.2f+reach*.18f,hand.z*.25f);
            Beam(upper,new Vector3(0,.65f,0),elbow,.20f);Beam(forearm,elbow,hand+Vector3.up*.13f,.14f);
            if(joint!=null)joint.localPosition=elbow;
            claw.localPosition=hand+Vector3.up*.12f;parcel.localPosition=hand;parcel.gameObject.SetActive(holding);
        }
    }
}
