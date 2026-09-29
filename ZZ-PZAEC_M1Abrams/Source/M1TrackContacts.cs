using UnityEngine;
namespace PZAEC.M1
{
    // Samples actual compound-body collision callbacks, never downward support rays.
    // Impulses are not treated as per-contact loads. All traction shares the chassis budget.
    public sealed class TrackContacts:MonoBehaviour
    {
        public struct Sample { public Vector3 Point,Normal; public Collider Ground; }
        public readonly Sample[] Samples=new Sample[24];
        readonly ContactPoint[] buffer=new ContactPoint[32];
        readonly Collider[] tracks=new Collider[2];
        readonly PhysicMaterial[] original=new PhysicMaterial[2];
        PhysicMaterial moving;bool driving;
        void Awake()
        {
            int index=0;
            foreach(var c in GetComponentsInChildren<Collider>(true))if((c.name=="M1TrackContactL"||c.name=="M1TrackContactR")&&index<2){tracks[index]=c;original[index++]=c.sharedMaterial;}
            moving=new PhysicMaterial("M1DrivenTrack"){dynamicFriction=.04f,staticFriction=.06f,bounciness=0,frictionCombine=PhysicMaterialCombine.Minimum,bounceCombine=PhysicMaterialCombine.Minimum};
        }
        public void SetDriving(bool value)
        {
            if(driving==value)return;driving=value;
            for(int i=0;i<2;i++)if(tracks[i]!=null)tracks[i].sharedMaterial=value?moving:original[i];
        }
        void OnDisable(){SetDriving(false);Clear();}
        void OnDestroy(){SetDriving(false);if(moving!=null)Destroy(moving);}
        public int Count; public float Stamp=-100; public bool Overflow;
        public void Clear(){Count=0;Stamp=-100;Overflow=false;}
        void OnCollisionEnter(Collision collision){Collect(collision);}
        void OnCollisionStay(Collision collision){Collect(collision);}
        void OnCollisionExit(Collision collision)
        {for(int i=Count-1;i>=0;i--)if(Samples[i].Ground==collision.collider)Samples[i]=Samples[--Count];}
        void Collect(Collision collision)
        {
            if(Stamp!=Time.fixedTime){Count=0;Overflow=false;Stamp=Time.fixedTime;}
            if(Overflow)return;
            if(collision.contactCount>=buffer.Length){Overflow=true;Count=0;return;}
            int n=collision.GetContacts(buffer);
            for(int i=0;i<n;i++){
                var c=buffer[i];var own=c.thisCollider;var ground=c.otherCollider;
                if(own==null||ground==null||ground.attachedRigidbody!=null||ground.GetComponentInParent<Entity>()!=null)continue;
                if(own.name!="M1TrackContactL"&&own.name!="M1TrackContactR")continue;
                var at=own.transform.InverseTransformPoint(c.point);
                if(c.normal.y<.65f||at.y>.76f||Mathf.Abs(at.z)>3.37f)continue;
                if(Count==Samples.Length){Overflow=true;Count=0;return;}
                Samples[Count++]=new Sample{Point=c.point,Normal=c.normal,Ground=ground};
            }
        }
        public bool Fresh
        {
            get{
                for(int i=Count-1;i>=0;i--){var c=Samples[i].Ground;if(c==null||!c.enabled||!c.gameObject.activeInHierarchy)Samples[i]=Samples[--Count];}
                return !Overflow&&Time.fixedTime-Stamp<=Time.fixedDeltaTime*1.5f&&Time.fixedTime>=Stamp;
            }
        }
    }
}
