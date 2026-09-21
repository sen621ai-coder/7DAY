using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace PZAEC.BasementLight
{
    public sealed class ModApi : IModApi
    {
        static GameObject cache;
        static Transform prefab;
        public void InitMod(Mod mod)
        {
            var harmony=new Harmony("pzaec.basementlight");
            harmony.Patch(AccessTools.Method(typeof(BlockShapeModelEntity),"getPrefab"),
                prefix:new HarmonyMethod(typeof(ModApi),nameof(GetPrefab)));
            harmony.Patch(AccessTools.Method(typeof(GameObjectPool),"DestroyObject",new[]{typeof(GameObject)}),
                prefix:new HarmonyMethod(typeof(ModApi),nameof(BeforePoolDestroy)));
            harmony.Patch(AccessTools.Method(typeof(BlockPoweredLight),
                nameof(BlockPoweredLight.OnBlockEntityTransformAfterActivated)),
                postfix:new HarmonyMethod(typeof(ModApi),nameof(Attach)));
            Log.Out("[BasementLight] v1.0.4 independent 15W panel; native root collision and wire anchor installed.");
        }
        public static bool GetPrefab(BlockShapeModelEntity __instance,ref Transform __result)
        {
            if(__instance.block==null || __instance.block.GetBlockName()!="pzaecBasementPanelLight")return true;
            if(prefab==null)
            {
                cache=new GameObject("BasementPanelCache");cache.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(cache);
                var root=new GameObject("pzaecBasementPanelRuntime");root.transform.SetParent(cache.transform,false);
                try
                {
                    root.AddComponent<PanelView>().Build();
                    prefab=root.transform;
                }
                catch{UnityEngine.Object.Destroy(cache);cache=null;throw;}
            }
            __result=prefab;return false;
        }
        public static void BeforePoolDestroy(GameObject __0)
        {
            if(__0==null || __0.GetComponent<PanelView>()==null)return;
            // The native pool destroys runtime shared materials during retirement.
            // Detach this instance so its removal cannot erase other lamps/previews.
            foreach(var renderer in __0.GetComponentsInChildren<Renderer>(true))
                renderer.sharedMaterials=new Material[0];
        }
        public static void Attach(WorldBase _world,Vector3i _blockPos,BlockValue _blockValue,BlockEntityData _ebcd)
        {
            if(_blockValue.Block.GetBlockName()!="pzaecBasementPanelLight" || _ebcd?.transform==null ||
                SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)return;
            try
            {
                var view=_ebcd.transform.GetComponent<PanelView>();
                if(view==null)throw new InvalidOperationException("Independent panel prefab missing; restart with matching lamp config and DLL");
                view.Bind(_world,_blockPos);
            }
            catch(Exception e){Log.Error("[BasementLight] Panel creation failed: "+e);}
        }
    }

    public sealed class PanelView : MonoBehaviour
    {
        public WorldBase World;
        public Vector3i Position;
        [SerializeField] Light lamp;
        [SerializeField] Renderer diffuser;
        float next;
        string colliderTag;
        int colliderLayer;
        static Material frame,off,on;
        static Cubemap cookie;
        static Material MaterialFromNative(string name,Color color)
        {
            var prefab=DataLoader.LoadAsset<Transform>("@:Entities/Crafting/woodWorkBenchPrefab.prefab",false);
            foreach(var r in prefab.GetComponentsInChildren<Renderer>(true))
            foreach(var source in r.sharedMaterials)
            {
                if(source==null || source.shader==null || !source.shader.isSupported || source.renderQueue>=3000 ||
                    !source.HasProperty("_MainTex") || !source.HasProperty("_Color"))continue;
                var m=new Material(source){name=name,color=color};
                // Preserve native shader masks/lookups; use opaque white albedo.
                m.mainTexture=Texture2D.whiteTexture;
                m.mainTextureScale=Vector2.one;m.mainTextureOffset=Vector2.zero;
                if(m.HasProperty("_Glossiness"))m.SetFloat("_Glossiness",.12f);
                if(m.HasProperty("_EmissionColor"))m.SetColor("_EmissionColor",Color.black);
                return m;
            }
            throw new InvalidOperationException("Supported native material unavailable");
        }
        static void Materials()
        {
            if(frame!=null && off!=null && on!=null)return;
            frame=MaterialFromNative("Basement charcoal frame",new Color(.16f,.18f,.20f));
            off=MaterialFromNative("Basement diffuser off",new Color(.58f,.60f,.61f));
            on=MaterialFromNative("Basement diffuser on",new Color(.82f,.84f,.83f));
            if(on.HasProperty("_EmissionColor")){on.EnableKeyword("_EMISSION");on.SetColor("_EmissionColor",new Color(.28f,.29f,.28f));}
        }
        GameObject Box(string name,Vector3 position,Vector3 scale,Material material)
        {
            var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;
            g.layer=colliderLayer;g.tag=colliderTag;g.transform.SetParent(transform,false);
            // One root collider handles every hit; decorative children cannot
            // resolve to an unregistered BlockEntityData transform.
            var collider=g.GetComponent<Collider>();collider.enabled=false;
            UnityEngine.Object.DestroyImmediate(collider);
            g.transform.localPosition=position;g.transform.localScale=scale;
            g.GetComponent<Renderer>().sharedMaterial=material;
            return g;
        }
        public void Build()
        {
            var native=DataLoader.LoadAsset<Transform>("@:Entities/Crafting/woodWorkBenchPrefab.prefab",false);
            var nativeCollider=native.GetComponentInChildren<Collider>(true);
            if(nativeCollider==null)throw new InvalidOperationException("Native block collision template missing");
            colliderTag=nativeCollider.tag;colliderLayer=nativeCollider.gameObject.layer;
            gameObject.tag=colliderTag;gameObject.layer=colliderLayer;
            // Explicit reference while inactive, before Awake can see a chunk/cache parent.
            gameObject.AddComponent<RootTransformRefParent>().RootTransform=transform;
            var bounds=gameObject.AddComponent<BoxCollider>();
            bounds.center=new Vector3(0,.913f,0);bounds.size=new Vector3(.92f,.154f,.92f);
            var wire=new GameObject("WireOffset");wire.transform.SetParent(transform,false);
            wire.transform.localPosition=new Vector3(0,.836f,0);
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)return;
            Materials();
            // The entire physical fixture stays in one voxel; underside is 0.85 above its base.
            Box("PanelFrame",new Vector3(0,.92f,0),new Vector3(.92f,.14f,.92f),frame);
            var face=Box("MilkDiffuser",new Vector3(0,.845f,0),new Vector3(.82f,.018f,.82f),off);
            diffuser=face.GetComponent<Renderer>();
            diffuser.shadowCastingMode=ShadowCastingMode.Off;
            foreach(float x in new[]{-.42f,.42f})foreach(float z in new[]{-.42f,.42f})
                Box("CornerFastener",new Vector3(x,.844f,z),new Vector3(.025f,.008f,.025f),off);
            var g=new GameObject("BasementSoftlight");g.transform.SetParent(transform,false);
            g.transform.localPosition=new Vector3(0,.79f,0);
            lamp=g.AddComponent<Light>();lamp.type=LightType.Point;
            lamp.range=Photometry.Range;lamp.intensity=Photometry.Intensity;
            lamp.color=new Color(1f,.97f,.92f);lamp.renderMode=LightRenderMode.ForcePixel;
            lamp.shadows=LightShadows.Soft;lamp.shadowStrength=1;lamp.shadowBias=.02f;lamp.shadowNormalBias=.1f;
            lamp.cookie=Cookie();lamp.enabled=false;
        }
        public void Bind(WorldBase world,Vector3i position)
        {
            World=world;Position=position;next=0;
            var rootRef=GetComponent<RootTransformRefParent>();
            if(rootRef!=null)rootRef.RootTransform=transform;
        }
        static Cubemap Cookie()
        {
            if(cookie!=null)return cookie;
            const int size=128;
            cookie=new Cubemap(size,TextureFormat.RGBA32,false){name="Basement batwing distribution",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
            for(int face=0;face<6;face++)
            {
                var pixels=new Color[size*size];
                for(int y=0;y<size;y++)for(int x=0;x<size;x++)
                {
                    float u=2*(x+.5f)/size-1,v=2*(y+.5f)/size-1;
                    Vector3 d=face==0?new Vector3(1,-v,-u):face==1?new Vector3(-1,-v,u):
                        face==2?new Vector3(u,1,v):face==3?new Vector3(u,-1,-v):
                        face==4?new Vector3(u,-v,1):new Vector3(-u,-v,-1);
                    float t=Photometry.Transmission(d.x,d.y,d.z);pixels[y*size+x]=new Color(t,t,t,t);
                }
                cookie.SetPixels(pixels,(CubemapFace)face);
            }
            cookie.Apply(false,true);return cookie;
        }
        void Update()
        {
            if(lamp==null || World==null || Time.time<next)return;
            next=Time.time+.2f;
            var value=World.GetBlock(Position);
            if(value.Block.GetBlockName()!="pzaecBasementPanelLight"){lamp.enabled=false;return;}
            // Native meta synchronizes powered state to clients; native toggle remains authoritative.
            var te=World.GetTileEntity(Position) as TileEntityPoweredBlock;
            bool lit=(value.meta&2)!=0 && te!=null && te.IsToggled;
            lamp.enabled=lit;diffuser.sharedMaterial=lit?on:off;
        }
        void OnDisable(){if(lamp!=null)lamp.enabled=false;World=null;next=0;}
    }
}
