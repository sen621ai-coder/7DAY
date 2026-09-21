using System;
using System.Collections.Generic;
using UnityEngine;

namespace PZAEC.SpawnSafety
{
    public sealed class Sites : ISiteWorld
    {
        private readonly World world;
        private readonly List<Bounds> boxes=new List<Bounds>();
        private readonly Dictionary<Vector3i,bool> covers=new Dictionary<Vector3i,bool>();
        public Sites(World world){this.world=world;}
        public bool Loaded(Vector3 p){return world.IsChunkAreaLoaded(p);}
        public float Terrain(int x,int z){return world.GetTerrainHeight(x,z);}
        public bool Floor(Vector3 p,out float y)
        {
            y=0;
            if(!Voxel.Raycast(world,new Ray(p+Vector3.up*.55f,Vector3.down),4f,true,false))return false;
            var hit=Voxel.voxelRayHitInfo;
            if(hit==null||!hit.bHitValid)return false;
            y=hit.hit.pos.y;
            return world.GetBlock(hit.hit.blockPos).Block.IsCollideMovement;
        }
        public bool Clear(Bounds body)
        {
            // Terrain's BlockShape exposes a full voxel cube, not the density mesh.
            // Query its real collision surface or ordinary slopes would all be rejected.
            if(Physics.CheckBox(body.center-Origin.position,body.extents,Quaternion.identity,
                (1<<Constants.cLayerTerrainCollision)|(1<<Constants.cLayerTerrain),QueryTriggerInteraction.Ignore))return false;
            var min=body.min;var max=body.max;
            for(int x=Mathf.FloorToInt(min.x);x<=Mathf.FloorToInt(max.x);x++)
            for(int z=Mathf.FloorToInt(min.z);z<=Mathf.FloorToInt(max.z);z++)
            for(int y=Mathf.FloorToInt(min.y);y<=Mathf.FloorToInt(max.y);y++)
            {
                var v=world.GetBlock(x,y,z);if(v.isair||v.Block==null||!v.Block.IsCollideMovement)continue;
                if(v.Block.shape.IsTerrain())continue;
                boxes.Clear();v.Block.GetCollisionAABB(v,x,y,z,0,boxes);
                foreach(var box in boxes)if(box.Intersects(body))return false;
            }
            return true;
        }
        public bool StructuralCover(int x,int z,int low,int high)
        {
            // Cached only within this spawn attempt; construction changes are seen next attempt.
            var key=new Vector3i(x,low,z);bool cached;if(covers.TryGetValue(key,out cached))return cached;
            // GetHeight is the live column height (including buildings), unlike
            // GetHeightAt, which samples the terrain generator. Avoid scanning empty sky.
            if(!world.IsChunkAreaLoaded(new Vector3(x,low,z)))return true;
            high=Math.Min(high,(int)world.GetHeight(x,z)+1);
            for(int y=low;y<=high;y++)
            {
                var v=world.GetBlock(x,y,z);var b=v.Block;
                if(v.isair||b==null||!b.IsCollideMovement||b is BlockModelTree)continue;
                boxes.Clear();b.GetCollisionAABB(v,x,y,z,0,boxes);
                foreach(var box in boxes)
                    if(box.size.x>=.6f&&box.size.z>=.6f){covers[key]=true;return true;}
            }
            covers[key]=false;return false;
        }
    }
    public sealed class Request
    {
        public string Source;
        public Vector3 Center;
        public float Min,Max;
        public bool Surface,Bypass,PerEntity,Direct;
        public int Remaining=12;
        public Func<Vector3?> Next;
        public Func<Vector3,bool> Allowed;
        public Vector3? LastAccepted;
    }
    public static class Safety
    {
        [ThreadStatic] public static Request Current;
        public static bool Enemy(Entity e)
        {EntityClass c;return e is EntityAlive && !(e is EntityPlayer) && EntityClass.list.TryGetValue(e.entityClass,out c)&&c.bIsEnemyEntity;}
        public static bool Flying(Entity e){return e is EntityFlying;}
        public static bool IsSurface(World world,Vector3 origin)
        {
            // Unknown/unloaded anchors are not evidence of a legitimate underground spawn.
            if(world==null||!SiteRules.Finite(origin.x)||!SiteRules.Finite(origin.y)||!SiteRules.Finite(origin.z)||origin.y<=1||!world.IsChunkAreaLoaded(origin))return true;
            return origin.y+.5f>=world.GetTerrainHeight(Mathf.FloorToInt(origin.x),Mathf.FloorToInt(origin.z));
        }
        public static bool Accept(Entity e,Request request)
        {
            if(request==null||request.Bypass||!Enemy(e)||Flying(e))return true;
            World world=GameManager.Instance==null?null:GameManager.Instance.World;
            if(world==null||world.IsRemote())return true;
            var sites=new Sites(world);Vector3 original=e.position,chosen;string reason="no-candidate";
            // PhysicsInit has already built the real capsule; no fixed two-block clearance.
            EntityClass entityClass;float scale=EntityClass.list.TryGetValue(e.entityClass,out entityClass)?Math.Max(1,entityClass.SizeScale):1;
            float radius=e.physicsColliderRadius>0?e.physicsColliderRadius:.4f*scale;
            float height=e.physicsHeight>0?e.physicsHeight:2.3f*scale;
            radius=Math.Max(radius,Math.Max(e.boundingBox.extents.x,e.boundingBox.extents.z));
            height=Math.Max(height,e.boundingBox.size.y);
            for(int attempt=0;attempt<12&&request.Remaining>0;attempt++)
            {
                request.Remaining--;
                Vector3 candidate;
                if(attempt==0)candidate=original;
                else
                {
                    if(request.Next==null)break;
                    Vector3? next=request.Next();
                    if(!next.HasValue)continue;
                    candidate=next.Value;
                }
                // The originating selector owns allowed distances and protection rules.
                if(request.Allowed!=null&&!request.Allowed(candidate)){reason="source-disallowed";continue;}
                if(!SiteRules.Validate(sites,candidate,radius,height,request.Surface,out chosen,out reason))continue;
                if(request.Allowed!=null&&!request.Allowed(chosen)){reason="source-disallowed";continue;}
                e.SetPosition(chosen,true);
                request.LastAccepted=chosen;
                Diagnostics.Accepted(world,e,request.Source,original,chosen,attempt,request.Surface);
                return true;
            }
            Diagnostics.Rejected(request.Source,e.entityClass,original,reason);
            // The entity has not entered World.SpawnEntityInWorld. Destroy the unregistered
            // prefab only; do not fire death/kill/reward paths or create a corpse.
            e.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(e.gameObject);
            return false;
        }
    }
}
