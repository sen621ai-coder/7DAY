using System;
using UnityEngine;

namespace YFAutomation.CargoDrones
{
    // Imported drone geometry stays inside the server's 1.6 x 1.2
    // collision body. Cosmetic geometry has no physical authority.
    public static class CargoDroneModel
    {
        internal static Material Material(Color color)
        {
            var shader=Shader.Find("Standard")??Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Unlit/Color");
            if(shader==null)throw new InvalidOperationException("Cargo model shader unavailable");
            var material=new Material(shader){color=color};if(material.HasProperty("_Metallic"))material.SetFloat("_Metallic",.35f);if(material.HasProperty("_Glossiness"))material.SetFloat("_Glossiness",.42f);return material;
        }
        internal static Transform Part(Transform root,string name,Vector3 at,Vector3 scale,Material material,PrimitiveType shape=PrimitiveType.Cube)
        {
            var part=GameObject.CreatePrimitive(shape);part.name=name;part.transform.SetParent(root,false);part.transform.localPosition=at;part.transform.localScale=scale;
            var collider=part.GetComponent<Collider>();if(collider!=null){collider.enabled=false;UnityEngine.Object.Destroy(collider);}part.GetComponent<Renderer>().sharedMaterial=material;return part.transform;
        }
        public static GameObject Drone()
        {
            var root=new GameObject("CargoDrone");
            try{root.AddComponent<CargoBusterRig>().Initialize();root.AddComponent<CargoDroneInteraction>();return root;}
            catch{UnityEngine.Object.Destroy(root);throw;}
        }
        public static GameObject Hub()
        {
            return CargoHubModel.Create();
        }
    }
    public sealed class CargoModelMaterials : MonoBehaviour
    {
        public Material[] Values;
        public Mesh[] Meshes;
        public Texture2D[] Textures;
        void OnDestroy(){if(Values!=null)foreach(var value in Values)if(value!=null)Destroy(value);if(Meshes!=null)foreach(var value in Meshes)if(value!=null)Destroy(value);if(Textures!=null)foreach(var value in Textures)if(value!=null)Destroy(value);}
    }
    public sealed class CargoDroneVisual : MonoBehaviour
    {
        public Guid Hub;
        CargoBusterRig rig;

        World world;
        long sequence=-1;
        float received;
        Vector3 previous,destination;
        bool spinning,restPose;
        // Set by the authenticated world snapshot receiver, never by cargo UI.
        public bool Apply(long revision,CargoPoint position,CargoPhase phase,CargoHold hold,int cargoCount,bool restPose=false)
        {
            if(revision<0||revision<=sequence||cargoCount<0||cargoCount>6||!Enum.IsDefined(typeof(CargoPhase),phase)||!Enum.IsDefined(typeof(CargoHold),hold))return false;
            if(GameManager.Instance?.World==null)return false;
            if(world!=null&&world!=GameManager.Instance.World){Destroy(gameObject);return false;}
            world=GameManager.Instance.World;
            var next=new Vector3((float)position.X,(float)position.Y,(float)position.Z);
            previous=sequence<0?next:Vector3.Lerp(previous,destination,Mathf.Clamp01((Time.realtimeSinceStartup-received)/.5f));destination=next;received=Time.realtimeSinceStartup;sequence=revision;
            spinning=phase!=CargoPhase.Docked&&phase!=CargoPhase.Docking&&phase!=CargoPhase.RecoveryOnly&&hold!=CargoHold.OwnerOffline;
            this.restPose=restPose;
            if(rig==null)rig=GetComponent<CargoBusterRig>();
            if(rig!=null)rig.SetFlight(spinning,cargoCount);
            return true;
        }
        void Update()
        {
            if(world==null||world!=GameManager.Instance?.World){Destroy(gameObject);return;}
            var at=Vector3.Lerp(previous,destination,Mathf.Clamp01((Time.realtimeSinceStartup-received)/.5f));
            transform.position=at-Origin.position; // Re-evaluate origin after world-origin shifts.
            var direction=destination-previous;direction.y=0;if(direction.sqrMagnitude>.0001f)transform.rotation=Quaternion.Slerp(transform.rotation,Quaternion.LookRotation(direction),Mathf.Clamp01(Time.deltaTime*8));
            if(rig!=null){rig.Advance(Time.deltaTime);if(restPose)rig.Sample(0,0);}
        }
    }
}
