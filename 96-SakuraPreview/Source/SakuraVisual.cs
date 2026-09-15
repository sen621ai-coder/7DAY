using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SakuraPreview
{
    // Visual-only procedural animation. Native entity remains the sole physics owner.
    public sealed class SakuraVisual : MonoBehaviour
    {
        public EntitySakura Owner;
        public static string ModPath;
        static AssetBundle bundle;
        static AssetBundle mintBundle;
        GameObject model; SakuraPose pose;
        Renderer[] carrierRenderers;
        Transform carrierTransform;
        GameObject interactionTarget;
        readonly Dictionary<string,Transform> bones=new Dictionary<string,Transform>();
        readonly Dictionary<string,Quaternion> rest=new Dictionary<string,Quaternion>();
        Vector3 previous; float speed,phase;
        bool attempted;
        void LateUpdate()
        {
            if(Owner==null)return;
            if(model==null && !attempted)
            {
                // PostInit may precede renderer construction. Retry until model is ready.
                // Entity.GetModelTransform() is a null-returning stub in V3.2.
                // The real instantiated visual belongs to EModelBase.
                var carrier=Owner.emodel?.GetModelTransform();
                if(carrier==null || carrier.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length==0)return;
                attempted=true;
                try
                {
                    string asset=Owner.IsGuardian?"Assets/Character/MintGuardian.prefab":"Assets/Character/SakuraPreview.prefab";
                    var selected=Owner.IsGuardian?mintBundle:bundle;
                    if(selected==null)
                        foreach(var loaded in AssetBundle.GetAllLoadedAssetBundles())
                            if(loaded.Contains(asset)){selected=loaded;break;}
                    if(selected==null)selected=AssetBundle.LoadFromFile(Path.Combine(ModPath,Owner.IsGuardian?"Resources/mint-guardian.unity3d":"Resources/sakura-preview.unity3d"));
                    if(selected==null)throw new Exception("Cannot load model bundle");
                    if(Owner.IsGuardian)mintBundle=selected;else bundle=selected;
                    var prefab=selected.LoadAsset<GameObject>(asset);
                    if(prefab==null)throw new Exception("Missing character prefab");
                    // Only hide carrier after replacement is known to be available.
                    carrierTransform=carrier;
                    carrierRenderers=carrier.GetComponentsInChildren<Renderer>(true);
                    foreach(var renderer in carrierRenderers)renderer.enabled=false;
                    model=Instantiate(prefab,carrier.parent);
                    model.transform.localPosition=carrier.localPosition;
                    model.transform.localRotation=carrier.localRotation;
                    foreach(var animator in model.GetComponentsInChildren<Animator>())animator.enabled=false;
                    foreach(var t in model.GetComponentsInChildren<Transform>(true))
                    {bones[t.name]=t;rest[t.name]=t.localRotation;t.gameObject.layer=carrier.gameObject.layer;}
                    previous=Owner.position;
                    if(Owner.IsGuardian){foreach(var a in model.GetComponentsInChildren<Animation>()){a.wrapMode=WrapMode.Loop;a.Play();}}
                    else pose=new SakuraPose(model.transform);
                    // The imported model has no gameplay hit collider. Do not rely on
                    // hidden SDCS renderers to keep the original body hitboxes active.
                    interactionTarget=new GameObject("SakuraInteractionHitbox");
                    interactionTarget.transform.SetParent(Owner.transform,false);
                    interactionTarget.layer=0; // PlayerSDCS body collision layer in physicsbodies.xml.
                    interactionTarget.tag="E_BP_Body";
                    interactionTarget.AddComponent<RootTransformRefEntity>().RootTransform=Owner.transform;
                    var hitbox=interactionTarget.AddComponent<CapsuleCollider>();
                    hitbox.direction=1;hitbox.height=1.68f;hitbox.radius=.32f;
                    hitbox.center=new Vector3(0,.84f,0);
                    foreach(var ownCollider in Owner.GetComponentsInChildren<Collider>(true))
                        if(ownCollider!=hitbox)Physics.IgnoreCollision(hitbox,ownCollider,true);
                    Log.Out("[SakuraPreview] Interaction hitbox ready: entity="+Owner.entityId+" commands="+Owner.GetActivationCommands().Length);
                    Log.Out("[SakuraPreview] Custom visual attached: "+asset+" entity="+Owner.entityId);
                }
                catch(Exception ex){Log.Error("[SakuraPreview] Visual initialization failed: "+ex.Message);}
            }
            if(model==null)return;
            foreach(var renderer in carrierRenderers)if(renderer!=null)renderer.enabled=false;
            if(carrierTransform!=null){model.transform.localPosition=carrierTransform.localPosition;model.transform.localRotation=carrierTransform.localRotation;}
            float dt=Mathf.Clamp(Time.deltaTime,.001f,.05f);
            var movement=Owner.position-previous;previous=Owner.position;movement.y=0;
            speed=Mathf.Lerp(speed,Mathf.Min(3,movement.magnitude/dt),dt*8);
            phase+=dt*Mathf.Lerp(1.6f,7,Mathf.Clamp01(speed));
            if(pose!=null)pose.Apply(speed,phase,Owner.GestureUntil);
        }
        void OnDestroy(){if(model!=null)Destroy(model);if(interactionTarget!=null)Destroy(interactionTarget);}
    }
}

