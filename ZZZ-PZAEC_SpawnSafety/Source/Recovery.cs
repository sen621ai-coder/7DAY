using System;
using UnityEngine;

namespace PZAEC.SpawnSafety
{
    // One bounded search per spawn request, also shared by selectors that fail
    // before creating a prefab. No detached queue and no synthetic success counts.
    public sealed class PerimeterSearch
    {
        public const int Limit=16;
        readonly int minimum,maximum;
        readonly Func<int,int,Vector3?> select;
        public int Attempts {get;private set;}
        public PerimeterSearch(int minimum,int maximum,Func<int,int,Vector3?> select)
        {this.minimum=minimum;this.maximum=Math.Max(minimum+1,maximum);this.select=select;}
        public Vector3? Next()
        {
            if(Attempts>=Limit)return null;
            // Four tries in each outward band, not sixteen tries under the same foundation.
            int band=Attempts++/4,span=maximum-minimum;
            int low=minimum+span*band/4,high=minimum+span*(band+1)/4;
            return select(low,Math.Max(low+1,high));
        }
    }
    public static class Recovery
    {
        public static Func<Vector3?> Create(Vector3 center,int minimum=8,int maximum=64,int playerDistance=12,bool checkBedrolls=true,bool checkClaims=false)
        {
            var search=new PerimeterSearch(minimum,maximum,(low,high)=>
            {
                var world=GameManager.Instance==null?null:GameManager.Instance.World;
                if(world==null||world.IsRemote()||!SiteRules.Finite(center.x)||!SiteRules.Finite(center.y)||!SiteRules.Finite(center.z))return null;
                Vector3 candidate;
                // Use the native selector's terrain-adjusted coordinates and protection
                // checks. Full body clearance still runs in Safety.Accept afterwards.
                if(!world.GetMobRandomSpawnPosWithWater(center,low,high,playerDistance,checkBedrolls,out candidate))return null;
                if(checkClaims&&!world.CanPlaceBlockAt(new Vector3i(candidate),null,false))return null;
                return (Vector3?)candidate;
            });
            return search.Next;
        }
        public static bool BeforeFactory(Request request,ref Vector3 position)
        {
            if(request==null)return false;
            if(!request.Surface||request.Fallback==null)
            {
                Diagnostics.SelectorFailed(request.Source,request.Center,0,!request.Surface?"underground-no-perimeter":"no-perimeter-selector");
                return false;
            }
            for(int i=0;i<PerimeterSearch.Limit;i++)
            {
                var candidate=request.Fallback();if(!candidate.HasValue)continue;
                position=candidate.Value;request.SelectorRecovered=true;
                Diagnostics.SelectorRecovered(request.Source,request.Center,position,i+1);
                return true;
            }
            Diagnostics.SelectorFailed(request.Source,request.Center,PerimeterSearch.Limit,"perimeter-exhausted");
            return false;
        }
    }
}
