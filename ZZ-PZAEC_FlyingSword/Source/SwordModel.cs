using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace PZAEC.FlyingSword
{
    public static class SwordModel
    {
        static Mesh mesh;static Material metal,glow;static Transform prefab;static GameObject cache;
        public static void Install(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(EntityInstanceAssets),"Load"),prefix:new HarmonyMethod(typeof(SwordModel),nameof(Load)));
            h.Patch(AccessTools.Method(typeof(ItemInventoryData),"BuildModel"),postfix:new HarmonyMethod(typeof(SwordModel),nameof(Held)));
        }
        static void Assets()
        {
            if(mesh!=null)return;
            using(var r=new BinaryReader(File.OpenRead(Path.Combine(SwordMod.Root,"Resources/juque.mesh")))){
                if(new string(r.ReadChars(4))!="JQ01")throw new InvalidDataException("Juque mesh version");
                int n=r.ReadInt32(),k=r.ReadInt32();if(n<3||n>100000||k>1000000)throw new InvalidDataException("Juque mesh bounds");
                var v=new Vector3[n];var normals=new Vector3[n];var uv=new Vector2[n];var tri=new int[k];
                for(int i=0;i<n;i++){v[i]=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());normals[i]=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());uv[i]=new Vector2(r.ReadSingle(),r.ReadSingle());}for(int i=0;i<k;i++)tri[i]=r.ReadInt32();
                mesh=new Mesh{name="Juque original mesh"};mesh.vertices=v;mesh.normals=normals;mesh.uv=uv;mesh.triangles=tri;mesh.RecalculateBounds();mesh.RecalculateTangents();
            }
            var shader=Shader.Find("Standard");if(shader==null)throw new InvalidOperationException("Native Standard shader unavailable");
            metal=new Material(shader){name="Juque source material",color=new Color(.7313f,.7313f,.7313f)};
            metal.mainTexture=Texture("color.jpg",false);metal.SetTexture("_BumpMap",Texture("normal.png",true));metal.EnableKeyword("_NORMALMAP");metal.SetFloat("_BumpScale",.9259f);metal.SetFloat("_Metallic",.6097f);metal.SetFloat("_Glossiness",.7004f);
            metal.EnableKeyword("_EMISSION");metal.SetColor("_EmissionColor",Color.black);
            glow=new Material(Shader.Find("Sprites/Default")??shader){name="Juque cyan gold glow",color=new Color(.3f,.95f,1,.65f)};
        }
        static Texture2D Texture(string name,bool linear){var t=new Texture2D(2,2,TextureFormat.RGBA32,true,linear);ImageConversion.LoadImage(t,File.ReadAllBytes(Path.Combine(SwordMod.Root,"Resources",name)));if(linear){var colors=t.GetPixels();for(int i=0;i<colors.Length;i++)colors[i]=new Color(1,colors[i].g,1,colors[i].r);t.SetPixels(colors);t.Apply(true,false);}t.wrapMode=TextureWrapMode.Repeat;return t;}
        public static GameObject Visual(Transform parent,float length)
        {Assets();var go=new GameObject("JuqueVisual");go.transform.SetParent(parent,false);go.transform.localScale=Vector3.one*length;go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=metal;return go;}
        public static Material Glow{get{Assets();return glow;}}
        public static Transform Prefab()
        {
            if(prefab!=null)return prefab;
            var native=DataLoader.LoadAsset<Transform>("@:Entities/Vehicles/VTruck4x4/VTruck4x4P.prefab",false);if(native==null)throw new InvalidOperationException("Native vehicle prefab missing");
            cache=new GameObject("JuquePrefabCache");cache.SetActive(false);UnityEngine.Object.DontDestroyOnLoad(cache);
            prefab=UnityEngine.Object.Instantiate(native,cache.transform,false);prefab.name="Juque";
            foreach(var x in prefab.GetComponentsInChildren<LODGroup>(true))x.enabled=false;
            foreach(var x in prefab.GetComponentsInChildren<Renderer>(true))x.enabled=false;
            foreach(var x in prefab.GetComponentsInChildren<AudioSource>(true)){x.Stop();x.enabled=false;}
            foreach(var x in prefab.GetComponentsInChildren<ParticleSystem>(true)){x.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var e=x.emission;e.enabled=false;}
            var rb=prefab.GetComponentInChildren<Rigidbody>(true);if(rb==null)throw new InvalidOperationException("Juque rigidbody");
            int layer=prefab.GetComponentsInChildren<Collider>(true).First(x=>!x.isTrigger).gameObject.layer;
            foreach(var x in prefab.GetComponentsInChildren<Collider>(true))x.enabled=false;
            var model=Visual(rb.transform,3);model.transform.localPosition=Vector3.up*.16f;model.layer=layer;
            var body=new GameObject("JuqueCollision");body.transform.SetParent(rb.transform,false);body.layer=layer;var box=body.AddComponent<BoxCollider>();box.center=new Vector3(0,.12f,0);box.size=new Vector3(.62f,.20f,2.9f);
            var rider=new GameObject("JuqueRiderClearance");rider.transform.SetParent(rb.transform,false);rider.layer=layer;var cap=rider.AddComponent<CapsuleCollider>();cap.center=new Vector3(0,1.13f,0);cap.height=1.9f;cap.radius=.32f;
            rb.mass=110;rb.centerOfMass=Vector3.zero;rb.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            return prefab;
        }
        sealed class Ready : LoadManager.AssetRequestTask<GameObject>{public Ready(GameObject g):base(null,false,null){asset=g;assetRetrieved=true;}public override bool INTERNAL_IsPending=>false;public override bool Load()=>true;public override void LoadSync(){}public override void Complete(){}public override void CompleteNow(){}public override void Release(){}}
        static bool Load(EntityInstanceAssets __instance,EntityClass __1){if(__1==null||!string.Equals(__1.entityClassName,SwordRules.VehicleName,StringComparison.OrdinalIgnoreCase))return true;var p=Prefab();__instance.PrefabT=p;__instance.prefabHandle=new Ready(p.gameObject);return false;}
        static void Held(ItemInventoryData __instance)
        {
            if(!SwordRules.IsSword(__instance.itemValue)||__instance.model==null||__instance.model.Find("JuqueVisual")!=null)return;
            foreach(var r in __instance.model.GetComponentsInChildren<Renderer>(true))r.enabled=false;
            var go=Visual(__instance.model,1.3f);go.layer=__instance.model.gameObject.layer;go.transform.localPosition=new Vector3(0,0,.4615f);var grip=go.AddComponent<JuqueGrip>();grip.Player=__instance.holdingEntity as EntityPlayer;
        }
    }
    [DefaultExecutionOrder(10050)]
    public sealed class JuqueGrip:MonoBehaviour
    {
        public EntityPlayer Player;
        public static readonly Vector3 HandleCenter=new Vector3(0,0,-.355f);
        static Transform Finger(Transform hand,string name){return hand.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==name);}
        public static Vector3 Palm(Transform hand){var a=Finger(hand,"RightHandMiddle1");var b=Finger(hand,"RightHandMiddle3");var c=Finger(hand,"RightHandRing1");var d=Finger(hand,"RightHandRing3");return a!=null&&b!=null&&c!=null&&d!=null?(a.position+b.position+c.position+d.position)*.25f:hand.TransformPoint(new Vector3(.06f,-.025f,0));}
        public void Align()
        {
            Transform hand=null;for(var t=transform.parent;t!=null;t=t.parent)if(t.name=="RightHand"){hand=t;break;}
            if(hand==null&&Player!=null&&!(Player is EntityPlayerLocal local&&local.bFirstPersonView))hand=SwordPresentation.BodyAnimator(Player)?.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="RightHand");
            if(hand!=null){var index=Finger(hand,"RightHandIndex1");var pinky=Finger(hand,"RightHandPinky1");if(index!=null&&pinky!=null&&(index.position-pinky.position).sqrMagnitude>.00001f)transform.rotation=Quaternion.LookRotation(index.position-pinky.position,hand.right);transform.position+=Palm(hand)-transform.TransformPoint(HandleCenter);}
        }
        void LateUpdate(){Align();}
    }
}
