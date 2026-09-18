using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace PZAEC.M1
{
    public sealed class M1Mod : IModApi
    {
        public void InitMod(Mod mod)
        {
            try {
                Model.Path=System.IO.Path.Combine(mod.Path,"Resources");
                var harmony=new Harmony("pzaec.m1.abrams");Model.Install(harmony);Weapons.Install(harmony);
                Log.Out("[M1-Abrams] Model, dual-seat main gun and firing presentation installed.");
            } catch(Exception e){Log.Error("[M1-Abrams] Initialization failed: "+e);}
        }
    }
    public static class Model
    {
        public static string Path;
        static Transform prefab;static GameObject cache;
        public static readonly Vector3 YawPivot=new Vector3(.0013f,1.45f,.3208f),PitchPivot=new Vector3(.0065f,1.7991f,1.38f);
        public const float BarrelLength=4.2058f;
        [Serializable]public sealed class AnchorFile{public Anchor[] nodes;}
        [Serializable]public sealed class Anchor{public string name,parent;public float[] position;}
        public static void Install(Harmony h)
        {
            if(!File.Exists(System.IO.Path.Combine(Path,"M1.meshbin")))throw new FileNotFoundException("M1.meshbin missing");
            h.Patch(AccessTools.Method(typeof(EntityInstanceAssets),"Load"),prefix:new HarmonyMethod(typeof(Model),nameof(LoadEntity)));
            h.Patch(AccessTools.Method(typeof(ItemActionSpawnVehicle),"StartHolding"),prefix:new HarmonyMethod(typeof(Model),nameof(Preview)));
            h.Patch(AccessTools.Method(typeof(EntityVehicle),"PostInit"),postfix:new HarmonyMethod(typeof(Model),nameof(Init)));
            h.Patch(AccessTools.Method(typeof(EntityVehicle),"FixedUpdateForces"),postfix:new HarmonyMethod(typeof(Chassis),nameof(Chassis.Update)));
        }
        sealed class ReadyAsset:LoadManager.AssetRequestTask<GameObject>
        {
            public ReadyAsset(GameObject go):base(null,false,null){asset=go;assetRetrieved=true;}
            public override bool INTERNAL_IsPending=>false;
            public override bool Load()=>true;public override void LoadSync(){}public override void Complete(){}public override void CompleteNow(){}public override void Release(){}
        }
        static Transform GetPrefab(){if(prefab==null)prefab=Build();return prefab;}
        static bool LoadEntity(EntityInstanceAssets __instance,EntityClass __1)
        {if(Rules.Index(__1.entityClassName)<0)return true;__instance.PrefabT=GetPrefab();__instance.prefabHandle=new ReadyAsset(prefab.gameObject);return false;}
        static bool Preview(ItemActionSpawnVehicle __instance,ItemActionData __0)
        {
            var player=__0.invData.holdingEntity as EntityPlayerLocal;if(player==null||Rules.Index(player.inventory.holdingItem.GetItemName())<0)return true;
            var data=(ItemActionSpawnVehicle.ItemActionDataSpawnVehicle)__0;if(data.VehiclePreviewT!=null)UnityEngine.Object.DestroyImmediate(data.VehiclePreviewT.gameObject);
            data.VehiclePreviewT=UnityEngine.Object.Instantiate(GetPrefab().gameObject).transform;
            Vehicle.SetupPreview(data.VehiclePreviewT);data.PreviewRenderers=null;__instance.SetupPreview(data);
            GameManager.Instance.StartCoroutine(__instance.UpdatePreview(data));return false;
        }
        static Transform Add(Transform parent,string name,Vector3 pos)
        {var t=new GameObject(name).transform;t.SetParent(parent,false);t.localPosition=pos;return t;}
        static Texture2D Tex(string name,bool linear)
        {
            var t=new Texture2D(2,2,TextureFormat.RGBA32,true,linear){name=name,anisoLevel=8,filterMode=FilterMode.Trilinear};
            if(!ImageConversion.LoadImage(t,File.ReadAllBytes(System.IO.Path.Combine(Path,name)),true))throw new InvalidDataException(name);
            return t;
        }
        static Material Mat(int i)
        {
            var shader=Shader.Find("Standard");if(shader==null)throw new InvalidOperationException("Standard shader missing");
            var m=new Material(shader){name="M1_"+i};m.mainTexture=Tex("color"+i+".png",false);
            m.SetTexture("_MetallicGlossMap",Tex("metal"+i+".png",true));m.EnableKeyword("_METALLICGLOSSMAP");m.SetFloat("_GlossMapScale",1);
            m.SetTexture("_BumpMap",Tex("normalPacked"+i+".png",true));m.EnableKeyword("_NORMALMAP");m.SetTexture("_OcclusionMap",Tex("ao"+i+".png",true));
            if(i==1){m.SetFloat("_Mode",1);m.SetFloat("_Cutoff",.5f);m.EnableKeyword("_ALPHATEST_ON");m.SetOverrideTag("RenderType","TransparentCutout");m.renderQueue=2450;}
            return m;
        }
        static string Str(BinaryReader r){int n=r.ReadInt32();if(n<0||n>128)throw new InvalidDataException("M1 string length");return System.Text.Encoding.UTF8.GetString(r.ReadBytes(n));}
        static Vector3 Vec(BinaryReader r)=>new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
        static Transform Build()
        {
            var native=DataLoader.LoadAsset<Transform>("@:Entities/Vehicles/VTruck4x4/VTruck4x4P.prefab",false);
            if(native==null)throw new InvalidOperationException("Native jeep prefab missing");
            cache=new GameObject("M1PrefabCache");cache.SetActive(false);UnityEngine.Object.DontDestroyOnLoad(cache);
            var root=UnityEngine.Object.Instantiate(native,cache.transform,false);root.name="M1Abrams";
            foreach(var lod in root.GetComponentsInChildren<LODGroup>(true))lod.enabled=false;
            foreach(var r in root.GetComponentsInChildren<Renderer>(true))r.enabled=false;
            foreach(var p in root.GetComponentsInChildren<ParticleSystem>(true)){p.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var em=p.emission;em.enabled=false;}
            var colliders=root.GetComponentsInChildren<Collider>(true);var first=colliders.FirstOrDefault(c=>!c.isTrigger&&!(c is WheelCollider));
            int layer=first!=null?first.gameObject.layer:root.gameObject.layer;
            foreach(var c in colliders)if(!(c is WheelCollider))c.enabled=false;
            // Retain native engine/wheel/seat/network components and transforms;
            // replace only renderer and body collision resources on this clone.
            var rb=root.GetComponentInChildren<Rigidbody>(true);if(rb==null)throw new InvalidOperationException("Native jeep physics root missing");
            var physics=rb.transform;var visual=Add(physics,"M1Visual",Vector3.zero);
            var nodes=new Dictionary<string,Transform>();nodes["VisualRoot"]=visual;
            nodes["TurretYaw"]=Add(visual,"TurretYaw",YawPivot);
            nodes["GunPitch"]=Add(nodes["TurretYaw"],"GunPitch",PitchPivot-YawPivot);
            nodes["GunRecoil"]=Add(nodes["GunPitch"],"GunRecoil",Vector3.zero);
            nodes["Muzzle"]=Add(nodes["GunRecoil"],"Muzzle",Vector3.forward*BarrelLength);
            nodes["GunObstructionStart"]=Add(nodes["GunPitch"],"GunObstructionStart",Vector3.zero);
            nodes["GunnerSight"]=Add(nodes["TurretYaw"],"GunnerSight",new Vector3(.45f,2.32f,.65f)-YawPivot);
            nodes["M1AbramsRoot"]=physics;
            var anchors=Newtonsoft.Json.JsonConvert.DeserializeObject<AnchorFile>(File.ReadAllText(System.IO.Path.Combine(Path,"anchors.json")));
            if(anchors==null||anchors.nodes==null)throw new InvalidDataException("M1 anchors.json is missing its nodes array");
            foreach(var a in anchors.nodes){
                if(nodes.ContainsKey(a.name))continue;
                var parent=nodes[a.parent];var global=new Vector3(a.position[0],a.position[1],a.position[2]);
                nodes[a.name]=Add(parent,a.name,parent.InverseTransformPoint(physics.TransformPoint(global)));
            }
            var materials=new[]{Mat(0),Mat(1)};var renderers=new[]{new List<Renderer>(),new List<Renderer>(),new List<Renderer>()};
            using(var r=new BinaryReader(File.OpenRead(System.IO.Path.Combine(Path,"M1.meshbin")))) {
                if(new string(r.ReadChars(4))!="M1B1")throw new InvalidDataException("M1 magic");int count=r.ReadInt32();if(count!=24)throw new InvalidDataException("M1 part count");
                for(int part=0;part<count;part++){
                    string name=Str(r),parent=Str(r);var pivot=Vec(r);int mat=r.ReadInt32();
                    if(!nodes.ContainsKey(parent))nodes[parent]=Add(visual,parent,pivot);
                    for(int lod=0;lod<3;lod++){
                        int nv=r.ReadInt32(),nf=r.ReadInt32();if(nv<0||nv>200000||nf<0||nf>100000)throw new InvalidDataException("M1 mesh size");
                        var v=new Vector3[nv];var n=new Vector3[nv];var uv=new Vector2[nv];var tris=new int[nf*3];
                        for(int j=0;j<nv;j++){v[j]=Vec(r);n[j]=Vec(r);uv[j]=new Vector2(r.ReadSingle(),r.ReadSingle());}
                        for(int j=0;j<tris.Length;j++)tris[j]=r.ReadInt32();
                        var mesh=new Mesh{name=name+"_LOD"+lod,indexFormat=IndexFormat.UInt32};mesh.vertices=v;mesh.normals=n;mesh.uv=uv;mesh.triangles=tris;mesh.RecalculateBounds();mesh.RecalculateTangents();
                        var t=Add(nodes[parent],mesh.name,Vector3.zero);t.gameObject.layer=layer;t.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
                        var mr=t.gameObject.AddComponent<MeshRenderer>();mr.sharedMaterial=materials[mat];renderers[lod].Add(mr);
                    }
                }
                if(r.BaseStream.Position!=r.BaseStream.Length)throw new InvalidDataException("M1 trailing data");
            }
            // Recessed dark closures also exist in the editable source.
            var dark=new Material(materials[0]){name="M1_Interior",color=new Color(.08f,.07f,.055f)};dark.mainTexture=null;
            Primitive(nodes["TurretYaw"],"TurretBaseCap",PrimitiveType.Cylinder,new Vector3(0,-.002f,0),new Vector3(1.94f,.0125f,1.94f),Quaternion.identity,dark);
            Primitive(nodes["GunPitch"],"RecoilSleeve",PrimitiveType.Cylinder,new Vector3(0,0,.42f),new Vector3(.236f,.24f,.236f),Quaternion.Euler(90,0,0),dark);
            var group=visual.gameObject.AddComponent<LODGroup>();group.SetLODs(new[]{new LOD(.32f,renderers[0].ToArray()),new LOD(.12f,renderers[1].ToArray()),new LOD(.025f,renderers[2].ToArray())});group.RecalculateBounds();
            Chassis.BuildLower(physics,layer);
            Box(physics,"M1Upper",new Vector3(0,1.17f,0),new Vector3(3.45f,.54f,7.2f),layer);
            Box(nodes["TurretYaw"],"M1TurretHit",new Vector3(0,.4f,-.3f),new Vector3(2.65f,.65f,3.7f),layer);
            rb.mass=12000;rb.centerOfMass=new Vector3(0,.72f,0);
            foreach(var wheel in root.GetComponentsInChildren<WheelCollider>(true)){
                var at=physics.InverseTransformPoint(wheel.transform.position);wheel.transform.position=physics.TransformPoint(new Vector3(at.x<0?-1.36f:1.36f,.615f,at.z<0?-2.15f:2.15f));
                wheel.radius=.4f;wheel.suspensionDistance=.28f;var spring=wheel.suspensionSpring;spring.spring=250000;spring.damper=28000;spring.targetPosition=.5f;wheel.suspensionSpring=spring;
            }
            return root;
        }
        public static Transform Primitive(Transform parent,string name,PrimitiveType type,Vector3 pos,Vector3 scale,Quaternion rot,Material mat)
        {
            var o=GameObject.CreatePrimitive(type);o.name=name;var c=o.GetComponent<Collider>();c.enabled=false;UnityEngine.Object.DestroyImmediate(c);
            o.transform.SetParent(parent,false);o.transform.localPosition=pos;o.transform.localRotation=rot;o.transform.localScale=scale;o.GetComponent<Renderer>().sharedMaterial=mat;return o.transform;
        }
        static void Box(Transform parent,string name,Vector3 pos,Vector3 size,int layer)
        {var t=Add(parent,name,pos);t.gameObject.layer=layer;var c=t.gameObject.AddComponent<BoxCollider>();c.size=size;}
        static void Init(EntityVehicle __instance)
        {if(Weapons.IsTank(__instance)){Weapons.Register(__instance);}}
    }
}
