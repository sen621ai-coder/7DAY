using System.Linq;
using UnityEngine;

namespace PZAEC.Fishing.Runtime
{
    // The native weapon socket is an asset pivot, not the centre of the closed hand.
    // Locate the space between curled fingers and thumb after the native arm animation.
    public sealed class NativePalmGrip
    {
        Transform root;
        readonly Transform[] fingers=new Transform[4];
        public void Resolve(Transform next)
        {
            if(next==root)return;
            root=next;
            var candidates=root!=null?root.GetComponentsInChildren<Transform>(true):new Transform[0];
            string[] names={"RightHandIndex2","RightHandMiddle2","RightHandRing2","RightHandThumb2"};
            for(int i=0;i<fingers.Length;i++)fingers[i]=candidates.FirstOrDefault(t=>t.name==names[i]);
        }
        public bool TryGet(out Vector3 centre)
        {
            centre=Vector3.zero;
            // Require a full known rig; an incomplete hand must not pull the rod to a stray bone.
            if(root==null||!root.gameObject.activeInHierarchy||fingers.Any(t=>t==null))return false;
            var curled=(fingers[0].position+fingers[1].position+fingers[2].position)/3;
            centre=(curled+fingers[3].position)*.5f;
            return true;
        }
        public void Reset(){root=null;for(int i=0;i<fingers.Length;i++)fingers[i]=null;}
    }
}
