using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace PZAEC.FlyingSword
{
    public static class SwordCombat
    {
        sealed class Wave {public int Actor,Damage;public ItemValue Item;public Vector3 Start,Direction,Right;public float Travel,Range,Width;public float[] Stops;public bool WallFX;public readonly HashSet<int> Hit=new HashSet<int>();}
        static readonly List<Wave> waves=new List<Wave>();
        sealed class Pending {public EntityPlayer Player;public ItemValue Item;public Vector3 Direction;public float Charge,Delay=.18f;}
        static readonly List<Pending> pending=new List<Pending>();
        public static void Clear(){waves.Clear();pending.Clear();}
        public static Vector3 BladeOrigin(EntityPlayer p,Vector3 direction){var right=Vector3.Cross(Vector3.up,direction).normalized;return p.getHeadPosition()-Vector3.up*.35f+right*.25f+direction*.25f;}
        public static void Schedule(EntityPlayer p,ItemValue item,Vector3 direction,float charge){if(pending.Count<128)pending.Add(new Pending{Player=p,Item=item.Clone(),Direction=direction,Charge=charge,Delay=charge>0?.18f:0});}
        public static void Fire(World world,EntityPlayer p,ItemValue item,Vector3 start,Vector3 dir,float charge)
        {
            if(waves.Count>=128)return;int tier=SwordRules.Tier(item);
            float melee=EffectManager.GetValue(PassiveEffects.EntityDamage,item,SwordRules.Melee[tier],p,null,FastTags<TagGroup.Global>.Parse("perkDeepCuts"));
            int damage=SwordRules.Saturate((double)SwordRules.Damage(tier,charge)*melee/SwordRules.Melee[tier]);
            var right=Vector3.Cross(Vector3.up,dir).normalized;if(right.sqrMagnitude<.1f)right=Vector3.right;
            var wave=new Wave{Actor=p.entityId,Item=item.Clone(),Damage=damage,Start=start,Direction=dir,Right=right,Range=Mathf.Lerp(35,45,charge),Width=Mathf.Lerp(1,8,charge),Stops=new float[17]};
            for(int i=0;i<wave.Stops.Length;i++)wave.Stops[i]=wave.Range;
            waves.Add(wave);
        }
        public static void Tick(World world,float dt)
        {
            for(int i=pending.Count-1;i>=0;i--){var queued=pending[i];queued.Delay-=Mathf.Clamp(dt,0,.1f);if(queued.Delay>0)continue;pending.RemoveAt(i);var p=queued.Player;if(p==null||p.IsDead()||p.AttachedToEntity!=null||!SwordRules.IsSword(p.inventory.holdingItemItemValue)||SwordRules.Id(p.inventory.holdingItemItemValue)!=SwordRules.Id(queued.Item)||SwordRules.Deployed(p.inventory.holdingItemItemValue)>0)continue;
                var origin=BladeOrigin(p,queued.Direction);var path=origin-p.getHeadPosition();if(Voxel.Raycast(world,new Ray(p.getHeadPosition(),path.normalized),path.magnitude,-538750997,8,0)&&Voxel.voxelRayHitInfo.hit.blockValue.type!=0)origin=p.getHeadPosition();
                Fire(world,p,queued.Item,origin,queued.Direction,queued.Charge);SwordRuntime.Emit(1,p,-1,0,queued.Item,origin,queued.Direction,queued.Charge);
            }
            if(waves.Count==0)return;var entities=world.Entities.list.ToArray();
            for(int n=waves.Count-1;n>=0;n--){var w=waves[n];float from=w.Travel,to=Mathf.Min(w.Range,from+50*Mathf.Clamp(dt,0,.1f));
                for(int i=0;i<w.Stops.Length;i++){
                    if(w.Stops[i]<=from)continue;float lateral=((float)i/(w.Stops.Length-1)-.5f)*w.Width;
                    Vector3 a=w.Start+w.Direction*from+w.Right*lateral;
                    // Block-only voxel cast. A blocked strip stays blocked for the wave's lifetime.
                    if(Voxel.Raycast(world,new Ray(a,w.Direction),to-from,-538750997,8,0)&&Voxel.voxelRayHitInfo.hit.blockValue.type!=0)
                    {var point=Voxel.voxelRayHitInfo.hit.pos;w.Stops[i]=Mathf.Min(w.Stops[i],from+Vector3.Distance(a,point));if(!w.WallFX&&world.GetEntity(w.Actor) is EntityPlayer p){w.WallFX=true;SwordRuntime.Emit(8,p,-1,0,w.Item,point,w.Direction,0);}}
                }
                foreach(var entity in entities){var alive=entity as EntityAlive;if(alive==null||alive.IsDead()||alive.entityId==w.Actor||alive is EntityVehicle||!alive.CanDamageEntity(w.Actor))continue;
                    var actor=world.GetEntity(w.Actor) as EntityAlive;if(actor==null)continue;
                    if(FactionManager.Instance!=null&&(int)FactionManager.Instance.GetRelationshipTier(actor,alive)>=(int)FactionManager.Relationship.Like)continue;
                    var center=alive.GetPosition()+Vector3.up*.9f;var delta=center-w.Start;float along=Vector3.Dot(delta,w.Direction),side=Vector3.Dot(delta,w.Right);
                    if(along<from-.45f||along>to+.45f||Mathf.Abs(side)>w.Width*.5f+.35f||Mathf.Abs(Vector3.Dot(delta,Vector3.Cross(w.Direction,w.Right)))>1)continue;
                    int lane=Mathf.Clamp(Mathf.RoundToInt((side/w.Width+.5f)*16),0,16);if(along>w.Stops[lane])continue;
                    // A second line-of-sight check closes gaps between sampled strips.
                    Vector3 origin=w.Start+w.Right*Mathf.Clamp(side,-w.Width*.5f,w.Width*.5f);var segment=center-origin;
                    if(Voxel.Raycast(world,new Ray(origin,segment.normalized),Mathf.Max(0,segment.magnitude-.35f),-538750997,8,0)&&Voxel.voxelRayHitInfo.hit.blockValue.type!=0)continue;
                    if(!w.Hit.Add(alive.entityId))continue;
                    var source=new DamageSourceEntity(EnumDamageSource.External,EnumDamageTypes.Piercing,w.Actor,w.Direction){AttackingItem=w.Item,canHitSpecialBodyParts=false,DismemberChance=0};
                    alive.DamageEntity(source,w.Damage,false,0);
                }
                w.Travel=to;if(to>=w.Range)waves.RemoveAt(n);
            }
        }
    }
}
public sealed class ItemActionPZAECJuqueWave : ItemAction
{
    // Raw button transitions are handled by SwordControls, independent of the
    // native item's hold/release animation gate, so a quick tap is never lost.
    public override void ExecuteAction(ItemActionData data,bool released){}
    public override void StopHolding(ItemActionData data){var p=data.invData.holdingEntity as EntityPlayerLocal;if(p!=null)PZAEC.FlyingSword.SwordRuntime.Send(p,PZAEC.FlyingSword.SwordOp.Cancel);base.StopHolding(data);}
    public override void CancelAction(ItemActionData data){}
}
public sealed class ItemActionPZAECSpiritStone : ItemAction
{
    readonly HashSet<ItemActionData> pressed=new HashSet<ItemActionData>();
    public override void ExecuteAction(ItemActionData data,bool released){if(released){pressed.Remove(data);return;}if(pressed.Add(data)&&data.invData.holdingEntity is EntityPlayerLocal p)PZAEC.FlyingSword.SwordRuntime.Send(p,PZAEC.FlyingSword.SwordOp.Refill);}
    public override void StopHolding(ItemActionData data){pressed.Remove(data);base.StopHolding(data);}
    public override void CancelAction(ItemActionData data){pressed.Remove(data);base.CancelAction(data);}
}
