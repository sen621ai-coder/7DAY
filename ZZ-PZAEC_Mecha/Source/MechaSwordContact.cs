using UnityEngine;
namespace PZAEC.Mecha
{
    public static class SwordContact
    {
        const float Radius=.10f; // 65mm blade proxy plus bounded sampling tolerance.
        static bool Segment(Bounds bounds,Vector3 a,Vector3 b,out Vector3 point)
        {
            point=a;var expanded=bounds;expanded.Expand(Radius*2);var delta=b-a;
            if(expanded.Contains(a)){point=bounds.ClosestPoint(a);return true;}
            float distance;if(delta.sqrMagnitude<.000001f||!expanded.IntersectRay(new Ray(a,delta.normalized),out distance)||distance>delta.magnitude)return false;
            point=bounds.ClosestPoint(a+delta.normalized*distance);return true;
        }
        public static bool Sweep(Bounds bounds,Collider collider,Vector3 a,Vector3 b,Vector3 c,Vector3 d,out Vector3 point)
        {
            point=Vector3.zero;int steps=Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(Vector3.Distance(a,c),Vector3.Distance(b,d))/.06f),1,64);
            for(int i=0;i<=steps;i++){
                var root=Vector3.Lerp(a,c,i/(float)steps);var tip=Vector3.Lerp(b,d,i/(float)steps);
                if(!Segment(bounds,root,tip,out point))continue;
                if(collider==null)return true;
                // Test the actual native limb capsule/box after the broad phase.
                int bladeSteps=Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(root,tip)/.06f),1,64);
                for(int j=0;j<=bladeSteps;j++){var at=Vector3.Lerp(root,tip,j/(float)bladeSteps);var surface=collider.ClosestPoint(at);if((surface-at).sqrMagnitude<=Radius*Radius){point=surface;return true;}}
            }
            return false;
        }
        public static bool Target(Samurai.State state,EntityAlive target,Vector3 a,Vector3 b,Vector3 c,Vector3 d,out Vector3 point)
        {
            Collider[] colliders;if(!state.ContactShapes.TryGetValue(target.entityId,out colliders)){colliders=target.GetComponentsInChildren<Collider>();state.ContactShapes[target.entityId]=colliders;}
            bool detailed=false;point=Vector3.zero;
            foreach(var collider in colliders){
                if(collider==null||!collider.enabled||!collider.gameObject.activeInHierarchy||!collider.tag.StartsWith("E_BP_"))continue;
                detailed=true;
                // Collider.ClosestPoint consumes floating-origin Unity coordinates.
                if(Sweep(collider.bounds,collider,a-Origin.position,b-Origin.position,c-Origin.position,d-Origin.position,out point)){point+=Origin.position;return true;}
            }
            if(detailed)return false;
            var native=target.nativeCollider;
            if(native!=null&&native.enabled&&native.gameObject.activeInHierarchy){bool hit=Sweep(native.bounds,native,a-Origin.position,b-Origin.position,c-Origin.position,d-Origin.position,out point);point+=Origin.position;return hit;}
            // SetPosition maintains absolute-world bounds (already including the
            // entity position). Do not add its position or Origin a second time.
            return Sweep(target.boundingBox,null,a,b,c,d,out point);
        }
    }
}
