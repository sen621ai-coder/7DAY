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
            Log.Out("[BasementLight] v1.0.8 12x12 softlight for 5-6 block ceilings with shadowed wall/ceiling fill; power 15W.");
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
            __0.GetComponent<PanelView>().Retiring=true;
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
        [NonSerialized] public bool Retiring;
        public Vector3i Position;
        [SerializeField] Light lamp;
        [SerializeField] Light fill;
        [SerializeField] Renderer diffuser;
        float next;
        string colliderTag;
        int colliderLayer;
        static Material frame,off,on;
        static Cubemap cookie;
        static Cubemap fillCookie;
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
            // The workstation shader is lit and its emission keywords are not a
            // reliable lamp surface. Use a supported unlit surface when powered:
            // visible in darkness, without adding another light or HDR bloom.
            Shader shader=Shader.Find("Unlit/Color");
            if(shader==null || !shader.isSupported)shader=Shader.Find("Sprites/Default");
            if(shader!=null && shader.isSupported)
            {
                on=new Material(shader){name="Basement diffuser on",color=new Color(.85f,.82f,.74f),renderQueue=2000};
                if(on.HasProperty("_MainTex"))on.mainTexture=Texture2D.whiteTexture;
                if(on.HasProperty("_ZWrite"))on.SetFloat("_ZWrite",1);
            }
            else
            {
                on=MaterialFromNative("Basement diffuser on",new Color(.85f,.82f,.74f));
                if(on.HasProperty("_EmissionMap"))on.SetTexture("_EmissionMap",Texture2D.whiteTexture);
                if(on.HasProperty("_EmissionColor")){on.EnableKeyword("_EMISSION");on.SetColor("_EmissionColor",new Color(.85f,.82f,.74f));}
                Log.Out("[BasementLight] Unlit diffuser shader unavailable; native emission fallback in use.");
            }
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
            lamp.color=new Color(1f,.87f,.73f);lamp.renderMode=LightRenderMode.ForcePixel;
            lamp.shadows=LightShadows.Soft;lamp.shadowStrength=1;lamp.shadowBias=.02f;lamp.shadowNormalBias=.1f;
            lamp.cookie=Cookie();lamp.enabled=false;
            var bounce=new GameObject("BasementWallCeilingFill");bounce.transform.SetParent(transform,false);
            // A virtual reflected-light origin below the panel, never inside a
            // ceiling voxel. Full-strength shadows retain room/wall occlusion.
            bounce.transform.localPosition=new Vector3(0,-.15f,0);
            fill=bounce.AddComponent<Light>();fill.type=LightType.Point;
            fill.range=Photometry.FillRange;fill.intensity=Photometry.FillIntensity;
            fill.color=new Color(1f,.90f,.78f);fill.renderMode=LightRenderMode.ForcePixel;
            fill.shadows=LightShadows.Soft;fill.shadowStrength=1;fill.shadowBias=.02f;fill.shadowNormalBias=.1f;
            fill.cookie=Cookie(true);fill.enabled=false;
        }
        public void Bind(WorldBase world,Vector3i position)
        {
            World=world;Position=position;next=0;
            var rootRef=GetComponent<RootTransformRefParent>();
            if(rootRef!=null)rootRef.RootTransform=transform;
        }
        static Cubemap Cookie(bool isFill=false)
        {
            var cached=isFill?fillCookie:cookie;
            if(cached!=null)return cached;
            const int size=128;
            var result=new Cubemap(size,TextureFormat.RGBA32,false){name=isFill?"Basement soft bounce":"Basement batwing distribution",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
            for(int face=0;face<6;face++)
            {
                var pixels=new Color[size*size];
                for(int y=0;y<size;y++)for(int x=0;x<size;x++)
                {
                    float u=2*(x+.5f)/size-1,v=2*(y+.5f)/size-1;
                    Vector3 d=face==0?new Vector3(1,-v,-u):face==1?new Vector3(-1,-v,u):
                        face==2?new Vector3(u,1,v):face==3?new Vector3(u,-1,-v):
                        face==4?new Vector3(u,-v,1):new Vector3(-u,-v,-1);
                    float t=isFill?Photometry.FillTransmission(d.x,d.y,d.z):Photometry.Transmission(d.x,d.y,d.z);pixels[y*size+x]=new Color(t,t,t,t);
                }
                result.SetPixels(pixels,(CubemapFace)face);
            }
            result.Apply(false,true);
            if(isFill)fillCookie=result;else cookie=result;
            return result;
        }
        void Update()
        {
            if(lamp==null || World==null || Time.time<next)return;
            next=Time.time+.2f;
            var value=World.GetBlock(Position);
            if(value.Block.GetBlockName()!="pzaecBasementPanelLight"){ApplyState(false);return;}
            // Native meta synchronizes powered state to clients; native toggle remains authoritative.
            var te=World.GetTileEntity(Position) as TileEntityPoweredBlock;
            bool lit=(value.meta&2)!=0 && te!=null && te.IsToggled;
            ApplyState(lit);
        }
        void ApplyState(bool lit)
        {
            if(lamp!=null)lamp.enabled=lit;
            if(fill!=null)fill.enabled=lit;
            if(diffuser!=null)diffuser.sharedMaterial=lit?on:off;
        }
        void OnDisable()
        {
            if(Retiring){if(lamp!=null)lamp.enabled=false;if(fill!=null)fill.enabled=false;}
            else ApplyState(false);
            World=null;next=0;
        }
    }
}

