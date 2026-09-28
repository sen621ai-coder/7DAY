using System;
using System.Collections.Generic;
using UnityEngine;

namespace PZAEC.Surveillance
{
    public sealed class TargetMarkerDetector
    {
        struct Candidate
        {
            public EntityAlive Entity;
            public Bounds Bounds;
            public float Distance;
        }
        readonly List<Entity> nearby=new List<Entity>();
        readonly List<Candidate> candidates=new List<Candidate>(TargetMarkerRules.MaxCandidates);
        readonly MarkerPoint[] points=new MarkerPoint[8];
        readonly Plane[] planes=new Plane[6];
        readonly WorldRayHitInfo scratchHit=new WorldRayHitInfo();
        int rays;
        public readonly List<MarkerRect> Boxes=new List<MarkerRect>(TargetMarkerRules.MaxBoxes);
        public int Revision {get;private set;}
        public int LastRayCount {get;private set;}
        public void Clear(){Boxes.Clear();Revision++;}
        bool Visible(World world,Camera camera,EntityAlive entity,Vector3 absolutePoint)
        {
            if(rays>=TargetMarkerRules.MaxRays)return false;rays++;
            Vector3 origin=camera.transform.position+Origin.position;
            Vector3 delta=absolutePoint-origin;float distance=delta.magnitude;
            if(distance<=camera.nearClipPlane)return false;
            Vector3 direction=delta/distance;
            // Match the camera's near clip; do not skip any occluding world block.
            origin+=direction*camera.nearClipPlane;distance-=camera.nearClipPlane;
            var previous=Voxel.voxelRayHitInfo;var previousPhysics=Voxel.phyxRaycastHit;
            try
            {
                Voxel.voxelRayHitInfo=scratchHit;
                if(!Voxel.Raycast(world,new Ray(origin,direction),distance+.1f,-538751005,Voxel.HM_All,0))return false;
                var hit=Voxel.voxelRayHitInfo;return hit!=null&&hit.transform!=null&&GameUtils.GetHitRootEntity(hit.tag,hit.transform)==entity;
            }
            finally{Voxel.voxelRayHitInfo=previous;Voxel.phyxRaycastHit=previousPhysics;}
        }
        public void Capture(World world,Camera camera,int width,int height)
        {
            Boxes.Clear();candidates.Clear();nearby.Clear();rays=0;Revision++;
            var absolute=camera.transform.position+Origin.position;
            world.GetEntitiesInBounds(typeof(EntityAlive),new Bounds(absolute,Vector3.one*(TargetMarkerRules.Range*2)),nearby);
            GeometryUtility.CalculateFrustumPlanes(camera,planes);
            foreach(var item in nearby)
            {
                var entity=item as EntityAlive;if(entity==null)continue;
                EntityClass definition;EntityClass.list.TryGetValue(entity.entityClass,out definition);
                if(!TargetMarkerRules.IsTarget(entity is EntityPlayer,entity.IsDead(),entity is EntityZombie,definition!=null&&definition.bIsAnimalEntity))continue;
                var bounds=entity.getBoundingBox();float distance=(bounds.center-absolute).sqrMagnitude;
                if(distance>TargetMarkerRules.Range*TargetMarkerRules.Range)continue;
                var renderBounds=bounds;renderBounds.center-=Origin.position;
                if(!GeometryUtility.TestPlanesAABB(planes,renderBounds))continue;
                int index=0;while(index<candidates.Count&&candidates[index].Distance<=distance)index++;
                if(index>=TargetMarkerRules.MaxCandidates)continue;
                candidates.Insert(index,new Candidate{Entity=entity,Bounds=bounds,Distance=distance});
                if(candidates.Count>TargetMarkerRules.MaxCandidates)candidates.RemoveAt(candidates.Count-1);
            }
            foreach(var candidate in candidates)
            {
                if(Boxes.Count>=TargetMarkerRules.MaxBoxes||rays>=TargetMarkerRules.MaxRays)break;
                var bounds=candidate.Bounds;var min=bounds.min;var max=bounds.max;
                for(int i=0;i<8;i++)
                {
                    var p=camera.WorldToViewportPoint(new Vector3((i&1)==0?min.x:max.x,(i&2)==0?min.y:max.y,(i&4)==0?min.z:max.z)-Origin.position);
                    points[i]=new MarkerPoint(p.x,p.y,p.z);
                }
                MarkerRect rect;if(!TargetMarkerRules.TryRect(points,camera.nearClipPlane,camera.farClipPlane,width,height,out rect))continue;
                // Two samples allow a visible head above a low wall, without marking fully hidden entities.
                if(Visible(world,camera,candidate.Entity,bounds.center)||Visible(world,camera,candidate.Entity,candidate.Entity.getHeadPosition()))Boxes.Add(rect);
            }
            LastRayCount=rays;nearby.Clear();candidates.Clear();
        }
    }
}
