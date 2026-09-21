using System;
using UnityEngine;

namespace PZAEC.SpawnSafety
{
    public interface ISiteWorld
    {
        bool Loaded(Vector3 point);
        float Terrain(int x,int z);
        bool Floor(Vector3 point,out float y);
        bool Clear(Bounds body);
        bool StructuralCover(int x,int z,int low,int high);
    }
    public static class SiteRules
    {
        public static bool Finite(float n) {return !float.IsNaN(n)&&!float.IsInfinity(n);}
        public static bool Validate(ISiteWorld world,Vector3 point,float radius,float height,bool surface,out Vector3 feet,out string reason)
        {
            feet=point;reason="invalid-coordinates";
            if(!Finite(point.x)||!Finite(point.y)||!Finite(point.z)||!Finite(radius)||!Finite(height)||radius<=0||height<=0||radius>8||height>24)return false;
            reason="unloaded";
            foreach(var offset in new[]{new Vector3(0,0,0),new Vector3(radius,0,radius),new Vector3(-radius,0,radius),new Vector3(radius,0,-radius),new Vector3(-radius,0,-radius)})
                if(!world.Loaded(point+offset))return false;
            reason="no-support";
            float ground;
            if(!world.Floor(point,out ground)||!Finite(ground)||point.y-ground>3.5f||ground-point.y>.6f)return false;
            feet.y=ground+.08f;
            reason="outside-world";
            if(feet.y<1||feet.y+height>254)return false;
            // Check the footprint, not just a single central block above an empty pit.
            float supportRadius=radius*.7f,highest=ground;
            foreach(var offset in new[]{new Vector3(supportRadius,0,supportRadius),new Vector3(-supportRadius,0,supportRadius),new Vector3(supportRadius,0,-supportRadius),new Vector3(-supportRadius,0,-supportRadius)})
            {
                float edge;reason="unstable-support";
                if(!world.Floor(feet+offset,out edge)||!Finite(edge)||Math.Abs(edge-ground)>.65f)return false;
                highest=Math.Max(highest,edge);
            }
            feet.y=highest+.08f;
            reason="outside-world";if(feet.y+height>254)return false;
            reason="body-overlap";
            if(!world.Clear(new Bounds(feet+new Vector3(0,height*.5f+.03f,0),new Vector3(radius*2,height-.06f,radius*2))))return false;
            if(surface)
            {
                float terrain=world.Terrain(Mathf.FloorToInt(feet.x),Mathf.FloorToInt(feet.z));
                reason="below-terrain";
                if(!Finite(terrain)||feet.y+.5f<terrain)return false;
                reason="elevated-platform";
                // Do not fix a foundation collision by lifting the spawn onto its roof.
                if(feet.y>terrain+2.5f)return false;
                int cover=0;int cx=Mathf.FloorToInt(feet.x),cz=Mathf.FloorToInt(feet.z);
                reason="covered-ground";
                if(world.StructuralCover(cx,cz,Mathf.FloorToInt(feet.y+height),254))return false;
                foreach(var offset in new[]{new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(-1,0,0),new Vector3(0,0,1),new Vector3(0,0,-1)})
                    if(world.StructuralCover(cx+(int)offset.x,cz+(int)offset.z,Mathf.FloorToInt(feet.y+height),254))cover++;
                reason="covered-ground";
                if(cover>=3)return false;
            }
            reason="ok";return true;
        }
    }
}
