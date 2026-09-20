using System;
using System.IO;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace AECT16RuntimeFix
{
    // Intercepts the common prefab path used by both placement ghosts and pooled
    // world instances. No tile entity, inventory or network state is replaced.
    public static class AutoForestryModel
    {
        static string assetPath;
        static GameObject cache;
        static Transform prefab;
        static Material timberMaterial;
        static Material nativeMaterial;
        const string BlockName = "yfAutoForestry";

        public static void Install(Harmony harmony, string modPath)
        {
            assetPath = Path.GetFullPath(Path.Combine(modPath, "../98-AECxProjectZ_Tweaks/Resources/Forestry"));
            if (!File.Exists(Path.Combine(assetPath, "sawmill.meshbin")))
                throw new FileNotFoundException("Auto forestry model resources missing", assetPath);
            var method = AccessTools.Method(typeof(BlockShapeModelEntity), "getPrefab");
            if (method == null) throw new MissingMethodException("BlockShapeModelEntity.getPrefab");
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(AutoForestryModel), nameof(GetPrefab)));
            AutoForestryActivity.Install(harmony);
            Log.Out("[AutoForestry] Sawmill prefab provider installed (10x6 footprint).");
        }

        static bool GetPrefab(BlockShapeModelEntity __instance, ref Transform __result)
        {
            if (__instance.block == null || __instance.block.GetBlockName() != BlockName) return true;
            if (__instance.block.Properties.GetValue("Model") != "yfAutoForestryRuntime.prefab") return true;
            if (prefab == null)
            {
                prefab = CreatePrefab();
                // Optional cleanup mode restores the previous 6x4 occupied cells.
                // Resize children and selection bounds together, keeping root scale 1.
                if (__instance.block.Properties.GetValue("MultiBlockDim") == "6,4,4")
                {
                    foreach (Transform child in prefab)
                    {
                        child.localPosition *= .6f;
                        child.localScale *= .6f;
                    }
                    var bounds = prefab.GetComponent<BoxCollider>();
                    bounds.center *= .6f; bounds.size *= .6f;
                }
            }
            __result = prefab;
            return false;
        }

        static Texture2D Texture(string filename, bool linear)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear);
            texture.name = "Forestry_" + filename;
            if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(Path.Combine(assetPath, filename)), true))
                throw new InvalidDataException("Cannot read forestry texture: " + filename);
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.anisoLevel = 8;
            texture.filterMode = FilterMode.Trilinear;
            return texture;
        }

        static Material Solid(Shader shader, string name, Color color)
        {
            var m = NativeMaterial(name); m.color=color;
            m.SetFloat("_Glossiness", .2f);
            return m;
        }

        // Reuse a shipped workstation material and its compiled shader keywords.
        // Shader.Find("Standard") can return a shader without the player variants
        // needed by dynamically generated materials on another client's renderer.
        static Material NativeMaterial(string name)
        {
            if(nativeMaterial==null)
            {
                var native=DataLoader.LoadAsset<Transform>("@:Entities/Crafting/woodWorkBenchPrefab.prefab",false);
                if(native!=null)
                foreach(var renderer in native.GetComponentsInChildren<Renderer>(true))
                {
                    foreach(var candidate in renderer.sharedMaterials)
                    {
                        if(candidate==null||candidate.shader==null||!candidate.shader.isSupported
                            ||candidate.renderQueue>=3000||!candidate.HasProperty("_MainTex")||!candidate.HasProperty("_Color"))continue;
                        nativeMaterial=candidate;break;
                    }
                    if(nativeMaterial!=null)break;
                }
                if(nativeMaterial==null)throw new InvalidOperationException("No supported native workstation material for forestry");
                Log.Out("[AutoForestry] Native material v2: "+nativeMaterial.name+", shader="+nativeMaterial.shader.name
                    +", graphics="+SystemInfo.graphicsDeviceType+", keywords="+String.Join(",",nativeMaterial.shaderKeywords));
            }
            var material=new Material(nativeMaterial){name=name,color=Color.white};
            // Do not retain the workbench's atlas, normal or emission textures.
            foreach(var property in material.GetTexturePropertyNames())material.SetTexture(property,null);
            material.mainTextureScale=new Vector2(1,1);material.mainTextureOffset=Vector2.zero;
            if(material.HasProperty("_EmissionColor"))material.SetColor("_EmissionColor",Color.black);
            return material;
        }

        internal static Material GetTimberMaterial()
        {
            if(timberMaterial!=null)return timberMaterial;
            timberMaterial=NativeMaterial("ForestryBarkAndEndgrain");
            timberMaterial.mainTexture=Texture("timber.png",false);
            timberMaterial.SetFloat("_Glossiness",.12f);
            return timberMaterial;
        }

        static GameObject Box(Transform parent, string name, Vector3 at, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        static GameObject DetailBox(Transform parent, string name, Vector3 at, Vector3 size, Material material)
        {
            var part = Box(parent,name,at,size,material);
            UnityEngine.Object.DestroyImmediate(part.GetComponent<Collider>());
            return part;
        }

        internal static GameObject Timber(Transform parent, Material material, int number)
        {
            const int segments = 32, rings = 4;
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var normals = new List<Vector3>();
            var triangles = new List<int>();
            float length = 1.8f + number * .065f, radius = .14f + (number%3)*.008f;
            for (int r = 0; r < rings; r++)
            for (int s = 0; s <= segments; s++)
            {
                float a = s * Mathf.PI * 2 / segments;
                float rough = 1 + .045f*Mathf.Sin(a*5+number) + .018f*Mathf.Sin(a*11+number);
                float taper = 1-.07f*r/(rings-1);
                var normal = new Vector3(0,Mathf.Cos(a),Mathf.Sin(a));
                vertices.Add(new Vector3(length*(r/(float)(rings-1)-.5f),normal.y*radius*rough*taper,normal.z*radius*rough*taper));
                normals.Add(normal); uv.Add(new Vector2(.008f+.48f*s/segments,.015f+.97f*r/(rings-1)));
            }
            for (int r=0;r<rings-1;r++)
            for (int s=0;s<segments;s++)
            {
                int a=r*(segments+1)+s,b=a+segments+1;
                triangles.AddRange(new[]{a,a+1,b,a+1,b+1,b});
            }
            for(int end=0;end<2;end++)
            {
                int center=vertices.Count; var normal=end==0?Vector3.left:Vector3.right;
                vertices.Add(new Vector3((end-.5f)*length,0,0)); normals.Add(normal); uv.Add(new Vector2(.75f,.5f));
                for(int s=0;s<=segments;s++)
                {
                    var p=vertices[end*(rings-1)*(segments+1)+s];
                    vertices.Add(p); normals.Add(normal);
                    uv.Add(new Vector2(.75f+.235f*p.y/radius,.5f+.47f*p.z/radius));
                    if(s<segments) triangles.AddRange(end==0?new[]{center,center+s+2,center+s+1}:new[]{center,center+s+1,center+s+2});
                }
            }
            var mesh=new Mesh{name="DetailedTimber"+number};
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0,uv); mesh.SetTriangles(triangles,0);
            mesh.RecalculateBounds(); mesh.RecalculateTangents();
            var log=new GameObject("Timber"+number); log.transform.SetParent(parent,false);
            log.transform.localPosition=new Vector3(3.6f,.36f+(number/3)*.30f,-2.55f+(number%3)*.32f);
            log.transform.localRotation=Quaternion.Euler(0,number*1.3f-2.6f,0);
            log.AddComponent<MeshFilter>().sharedMesh=mesh; log.AddComponent<MeshRenderer>().sharedMaterial=material;
            var collider=log.AddComponent<CapsuleCollider>(); collider.direction=0; collider.radius=radius; collider.height=length;
            return log;
        }

        static Transform CreatePrefab()
        {
            var shader = GetTimberMaterial().shader;
            // Obtain collision layer/tag from an installed native workstation rather
            // than hard-coding version-dependent game layer numbers.
            var native = DataLoader.LoadAsset<Transform>("@:Entities/Crafting/woodWorkBenchPrefab.prefab", false);
            if (native == null) throw new InvalidOperationException("Native workstation template unavailable");
            var nativeCollider = native.GetComponentInChildren<Collider>(true);
            if (nativeCollider == null) throw new InvalidOperationException("Native workstation collider missing");
            cache = new GameObject("AutoForestryPrefabCache");
            cache.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(cache);
            var go = new GameObject("yfAutoForestryRuntime");
            go.transform.SetParent(cache.transform, false);
            // Native ray hits resolve child colliders through this reference before
            // looking up BlockEntityData. Without it they look up the child itself.
            // Set explicitly while inactive: Awake must not pick the cache/chunk root.
            go.AddComponent<RootTransformRefParent>().RootTransform = go.transform;
            try
            {
                Material[] materials;
                using (var reader = new BinaryReader(File.OpenRead(Path.Combine(assetPath, "sawmill.meshbin"))))
                {
                    if (new string(reader.ReadChars(4)) != "YFF1") throw new InvalidDataException("Bad forestry mesh header");
                    int materialCount = reader.ReadInt32(), partCount = reader.ReadInt32();
                    if (materialCount != 6 || partCount < 1 || partCount > 32) throw new InvalidDataException("Bad forestry mesh counts");
                    materials = new Material[materialCount];
                    for (int i = 0; i < materialCount; i++)
                    {
                        var m = NativeMaterial("ForestrySurface" + i);
                        m.SetTexture("_MainTex", Texture("color" + i + ".png", false));
                        m.SetTexture("_MetallicGlossMap", Texture("metal" + i + ".png", true));
                        m.SetTexture("_BumpMap", Texture("normal" + i + ".png", true));
                        m.SetFloat("_GlossMapScale", .65f);
                        m.SetFloat("_BumpScale", i == 1 ? .55f : .85f);
                        if(i>=2)
                        {
                            m.SetTexture("_OcclusionMap",Texture("ao"+i+".png",true));
                            m.SetFloat("_OcclusionStrength",.45f);
                        }
                        if (i == 0)
                        {
                            if(m.HasProperty("_Cutoff"))m.SetFloat("_Cutoff", .4f);
                        }
                        materials[i] = m;
                    }
                    for (int p = 0; p < partCount; p++)
                    {
                        int material = reader.ReadInt32(), count = reader.ReadInt32(), indexCount = reader.ReadInt32();
                        if (material < 0 || material >= materialCount || count < 3 || count > 200000 || indexCount < 3 || indexCount > 600000 || indexCount % 3 != 0)
                            throw new InvalidDataException("Invalid forestry mesh dimensions");
                        var vertices = new Vector3[count]; var normals = new Vector3[count]; var uv = new Vector2[count];
                        for (int v = 0; v < count; v++)
                        {
                            vertices[v] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                            normals[v] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                            uv[v] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                            var a = vertices[v];
                            if (float.IsNaN(a.x + a.y + a.z) || Math.Abs(a.x) > 5 || a.y < 0 || a.y > 4 || Math.Abs(a.z) > 3)
                                throw new InvalidDataException("Forestry vertex outside footprint");
                        }
                        int[] indices = new int[indexCount];
                        for (int t = 0; t < indexCount; t++)
                        {
                            indices[t] = reader.ReadInt32();
                            if (indices[t] < 0 || indices[t] >= count) throw new InvalidDataException("Bad forestry triangle index");
                        }
                        var mesh = new Mesh { name = "ForestryMesh" + p, indexFormat = IndexFormat.UInt32 };
                        mesh.vertices = vertices; mesh.normals = normals; mesh.uv = uv; mesh.triangles = indices;
                        mesh.RecalculateBounds(); mesh.RecalculateTangents();
                        var child = new GameObject("SawmillPart" + p); child.transform.SetParent(go.transform, false);
                        child.AddComponent<MeshFilter>().sharedMesh = mesh;
                        child.AddComponent<MeshRenderer>().sharedMaterial = materials[material];
                        // Static concave mesh collision retains the real roof/rail contours.
                        child.AddComponent<MeshCollider>().sharedMesh = mesh;
                    }
                    if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Trailing forestry mesh data");
                }
                var steel = new Material(materials[5]) { name="ForestrySteel" };
                var dark = Solid(shader, "ForestryBase", new Color(.18f,.17f,.15f));
                var wood = new Material(materials[4]) { name="ForestryWood" };
                var green = Solid(shader, "ForestryScreen", new Color(.18f,.55f,.26f));
                var amber = Solid(shader, "ForestryWarning", new Color(.85f,.45f,.08f));
                dark.mainTexture=materials[0].mainTexture;
                dark.mainTextureScale=new Vector2(.25f,.30f); dark.mainTextureOffset=new Vector2(.72f,.06f);
                var timber=GetTimberMaterial();
                var trim=Solid(shader,"ForestryMetalTrim",new Color(.48f,.49f,.44f));
                trim.SetFloat("_Metallic",.8f); trim.SetFloat("_Glossiness",.35f);
                var red=Solid(shader,"ForestryStopButton",new Color(.6f,.055f,.035f));
                green.SetColor("_EmissionColor",new Color(.015f,.10f,.025f));
                // A trigger supplies full native selection bounds without creating
                // invisible physical walls. Child colliders follow the real geometry.
                var foundation = Box(go.transform, "Foundation", new Vector3(0,.045f,0), new Vector3(9.95f,.09f,5.95f), dark);
                var rootCollider = go.AddComponent<BoxCollider>();
                rootCollider.isTrigger = true;
                rootCollider.center = new Vector3(0,1.9f,0); rootCollider.size = new Vector3(9.95f,3.8f,5.95f);
                Box(go.transform, "ControlCabinet", new Vector3(0,.55f,-2.65f), new Vector3(.6f,.95f,.42f), steel);
                DetailBox(go.transform,"ScreenBezel",new Vector3(0,.87f,-2.869f),new Vector3(.48f,.26f,.025f),dark);
                DetailBox(go.transform, "ControlScreen", new Vector3(0,.88f,-2.89f), new Vector3(.40f,.18f,.015f), green);
                for(int row=0;row<4;row++)
                    DetailBox(go.transform,"DisplayLine"+row,new Vector3(-.035f,.93f-row*.03f,-2.900f),new Vector3(.22f-row*.035f,.008f,.004f),trim);
                for(int button=0;button<3;button++)
                    DetailBox(go.transform,"ControlButton"+button,new Vector3(-.16f+button*.16f,.65f,-2.884f),new Vector3(.065f,.045f,.028f),button==2?red:amber);
                for(int slot=0;slot<6;slot++)
                    DetailBox(go.transform,"VentSlot"+slot,new Vector3(-.055f,.46f-slot*.035f,-2.868f),new Vector3(.31f,.012f,.008f),dark);
                DetailBox(go.transform,"CabinetHandle",new Vector3(.24f,.43f,-2.885f),new Vector3(.026f,.15f,.035f),trim);
                foreach(float x in new[]{-.26f,.26f})
                foreach(float y in new[]{.15f,.99f})
                    DetailBox(go.transform,"PanelFastener",new Vector3(x,y,-2.872f),new Vector3(.025f,.025f,.012f),trim);
                DetailBox(go.transform,"SafetyStrip",new Vector3(0,.205f,-2.872f),new Vector3(.45f,.055f,.014f),amber);
                for(int i=0;i<6;i++)
                    DetailBox(go.transform,"SafetyStripe"+i,new Vector3(-.18f+i*.07f,.205f,-2.881f),new Vector3(.026f,.055f,.003f),dark);
                for (int i = 0; i < 5; i++) Timber(go.transform,timber,i);
                var speed = Box(go.transform, "ForestrySpeed", new Vector3(-.8f,.30f,-2.55f), new Vector3(.7f,.4f,.45f), steel);
                var packer = Box(go.transform, "ForestryPacker", new Vector3(1.4f,.53f,-2.13f), new Vector3(.60f,.40f,.38f), wood);
                var siren = Box(go.transform, "ForestrySiren", new Vector3(.20f,1.13f,-2.65f), new Vector3(.14f,.18f,.14f), amber);
                for(int i=0;i<6;i++)
                    DetailBox(speed.transform,"CoolingFin"+i,new Vector3(-.4f+i*.16f,.42f,0),new Vector3(.06f,.13f,.9f),trim);
                for(int i=0;i<5;i++)
                    DetailBox(packer.transform,"BoardJoint"+i,new Vector3(0,-.35f+i*.17f,-.505f),new Vector3(.94f,.018f,.012f),dark);
                foreach(float x in new[]{-.3f,.3f})
                    DetailBox(packer.transform,"PackingBand",new Vector3(x,0,-.515f),new Vector3(.05f,.96f,.018f),trim);
                DetailBox(siren.transform,"BeaconBase",new Vector3(0,-.4f,0),new Vector3(1.1f,.2f,1.1f),trim);
                speed.SetActive(false); packer.SetActive(false); siren.SetActive(false);
                AutoForestryMachinery.Build(go.transform,steel,trim,dark,wood,amber);
                AutoForestryWoodworking.Build(go.transform,wood,steel,trim,dark);
                AutoForestryRailStock.Build(go.transform,timber,wood,steel,amber);
                if(!GameManager.IsDedicatedServer)AutoForestryPresentation.Build(go,assetPath,materials,trim,dark);
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                {
                    t.gameObject.layer = nativeCollider.gameObject.layer;
                    t.gameObject.tag = "T_Block";
                }
                Log.Out("[AutoForestry] Model ready: six surface materials, machinery, production visuals and distance LOD.");
                return go.transform;
            }
            catch
            {
                UnityEngine.Object.Destroy(cache); cache = null;
                throw;
            }
        }
    }
}
