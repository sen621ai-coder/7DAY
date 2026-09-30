using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace PZAEC.Mecha
{
    public static class Model
    {
        public static string Path;
        static GlbFile glb;
        static Transform prefab; static GameObject cache;
        static Material bodyMaterial, legMaterial;

        public sealed class Rig
        {
            public Transform Root, Visual, Eye, Lens, Body, TurbineL, TurbineR;
            public Transform PanelL, PanelR;
            public readonly List<Transform> LegRoots = new List<Transform>();
            public readonly List<Transform> LegSegments = new List<Transform>();
            public readonly Dictionary<int, Transform> GlbNodes = new Dictionary<int, Transform>();
        }
        static readonly Dictionary<EntityVehicle, Rig> rigs = new Dictionary<EntityVehicle, Rig>();

        public static void Install(Harmony h)
        {
            var glbPath = System.IO.Path.Combine(Path, "buster_drone.glb");
            if (!File.Exists(glbPath)) throw new FileNotFoundException("buster_drone.glb missing");
            glb = GlbFile.Load(glbPath);
            h.Patch(AccessTools.Method(typeof(EntityInstanceAssets), "Load"), prefix: new HarmonyMethod(typeof(Model), nameof(LoadEntity)));
            h.Patch(AccessTools.Method(typeof(ItemActionSpawnVehicle), "StartHolding"), prefix: new HarmonyMethod(typeof(Model), nameof(Preview)));
            h.Patch(AccessTools.Method(typeof(EntityVehicle), "PostInit"), postfix: new HarmonyMethod(typeof(Model), nameof(Init)));
        }

        sealed class ReadyAsset : LoadManager.AssetRequestTask<GameObject>
        {
            public ReadyAsset(GameObject go) : base(null, false, null) { asset = go; assetRetrieved = true; }
            public override bool INTERNAL_IsPending => false;
            public override bool Load() => true; public override void LoadSync() { }
            public override void Complete() { } public override void CompleteNow() { } public override void Release() { }
        }

        static bool LoadEntity(EntityInstanceAssets __instance, EntityClass __1)
        {
            if (__1 == null || __1.entityClassName != Rules.VehicleName) return true;
            if (prefab == null) prefab = Build();
            __instance.PrefabT = prefab;
            __instance.prefabHandle = new ReadyAsset(prefab.gameObject);
            return false;
        }

        static bool Preview(ItemActionSpawnVehicle __instance, ItemActionData __0)
        {
            var player = __0.invData.holdingEntity as EntityPlayerLocal;
            if (player == null || player.inventory.holdingItem.GetItemName() != Rules.PlaceableItem) return true;
            var data = (ItemActionSpawnVehicle.ItemActionDataSpawnVehicle)__0;
            if (data.VehiclePreviewT != null) UnityEngine.Object.DestroyImmediate(data.VehiclePreviewT.gameObject);
            if (prefab == null) prefab = Build();
            var root = new GameObject("MechaPlacementPreview").transform;
            var visual = UnityEngine.Object.Instantiate(Find(prefab.transform, "MechaVisual").gameObject, root, false);
            visual.SetActive(true);
            foreach (var collider in visual.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            data.VehiclePreviewT = root;
            Vehicle.SetupPreview(root); data.PreviewRenderers = null; __instance.SetupPreview(data);
            GameManager.Instance.StartCoroutine(__instance.UpdatePreview(data));
            return false;
        }

        public static void Init(EntityVehicle __instance)
        {
            if (!Weapons.IsMecha(__instance)) return;
            GetRig(__instance);
            Deploy.Begin(__instance);
        }

        public static Rig GetRig(EntityVehicle v)
        {
            Rig rig;
            if (rigs.TryGetValue(v, out rig) && rig.Visual != null) return rig;
            var root = Find(v.PhysicsTransform != null ? v.PhysicsTransform : v.transform, "MechaVisual");
            if (root == null) return null;
            rig = new Rig { Root = root.parent != null ? root.parent : root, Visual = root };
            rig.Body = Find(root, "Drone_Body");
            rig.Eye = Find(root, "Drone_IEye"); rig.Lens = Find(root, "Drone_ILens");
            rig.TurbineL = Find(root, "Drone_Turb_Blade_L"); rig.TurbineR = Find(root, "Drone_Turb_Blade_R");
            rig.PanelL = Find(root, "Drone_UPanel_L"); rig.PanelR = Find(root, "Drone_UPanel_R");
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var n = t.name;
                if (n == "Drone_leg_F" || n == "Drone_leg_R" || n == "Drone_leg_L") rig.LegRoots.Add(t);
                else if (n.Length == 4 && (n[0] == 'F' || n[0] == 'R' || n[0] == 'L') && n[1] == '_' && n[2] == 'P' &&
                    n[3] >= '1' && n[3] <= '7') rig.LegSegments.Add(t);
            }
            rigs[v] = rig;
            return rig;
        }

        public static void Forget(EntityVehicle v)
        {
            rigs.Remove(v);
            Deploy.Forget(v);
        }

        static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        static Texture2D Tex(string file, bool linear)
        {
            var fullPath = System.IO.Path.Combine(Path, "MechaTextures", file);
            byte[] bytes = File.Exists(fullPath) ? File.ReadAllBytes(fullPath) : null;
            if (bytes == null) throw new FileNotFoundException("Mecha texture missing: " + file);
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear) { name = file, anisoLevel = 8, filterMode = FilterMode.Trilinear };
            if (!ImageConversion.LoadImage(t, bytes, true)) throw new InvalidDataException(file);
            return t;
        }

        static int TextureIndex(GlbFile.GlbTextureRef reference)
        {
            return reference == null ? -1 : glb.textures[reference.index].source;
        }

        static Material Material(GlbFile.GlbMaterial source, bool emissive)
        {
            var shader = Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("Standard shader missing");
            var m = new Material(shader) { name = "Mecha_" + source.name };
            int color = TextureIndex(source.pbrMetallicRoughness == null ? null : source.pbrMetallicRoughness.baseColorTexture);
            if (color >= 0) m.mainTexture = Tex("tex_" + color + ".png", false);
            int bump = TextureIndex(source.normalTexture);
            if (bump >= 0) { m.SetTexture("_BumpMap", Tex("tex_" + bump + ".png", true)); m.EnableKeyword("_NORMALMAP"); }
            int ao = TextureIndex(source.occlusionTexture);
            if (ao >= 0) m.SetTexture("_OcclusionMap", Tex("tex_" + ao + ".png", true));
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", .55f);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", .42f);
            if (emissive)
            {
                int glow = TextureIndex(source.emissiveTexture);
                if (glow >= 0)
                {
                    m.SetTexture("_EmissionMap", Tex("tex_" + glow + ".png", true));
                    m.EnableKeyword("_EMISSION");
                    var factor = source.emissiveFactor;
                    var color2 = factor != null && factor.Length == 3 ? new Color(factor[0], factor[1], factor[2]) : Color.white;
                    m.SetColor("_EmissionColor", color2 * 1.6f);
                }
            }
            return m;
        }

        static Transform Add(Transform parent, string name)
        { var t = new GameObject(name).transform; t.SetParent(parent, false); return t; }

        // glTF is right-handed; Unity is left-handed. Mirror X for points,
        // quaternions and triangle winding so the walker is not inside out.
        static Vector3 Vec3(float[] values, int offset)
        { return new Vector3(-values[offset], values[offset + 1], values[offset + 2]); }

        static Mesh BuildMesh(GlbFile.GlbMesh glbMesh, int primitiveIndex)
        {
            var primitive = glbMesh.primitives[primitiveIndex];
            var position = glb.ReadFloats(primitive.attributes.POSITION);
            var mesh = new Mesh { name = glbMesh.name != null ? glbMesh.name : "MechaPart", indexFormat = IndexFormat.UInt32 };
            var vertices = new Vector3[position.Length / 3];
            for (int i = 0; i < vertices.Length; i++) vertices[i] = Vec3(position, i * 3);
            mesh.SetVertices(vertices);
            if (primitive.attributes.NORMAL != 0)
            {
                var normal = glb.ReadFloats(primitive.attributes.NORMAL);
                var normals = new Vector3[normal.Length / 3];
                for (int i = 0; i < normals.Length; i++) normals[i] = Vec3(normal, i * 3);
                mesh.SetNormals(normals);
            }
            if (primitive.attributes.TEXCOORD_0 != 0)
            {
                var uv = glb.ReadFloats(primitive.attributes.TEXCOORD_0);
                var uvs = new Vector2[uv.Length / 2];
                for (int i = 0; i < uvs.Length; i++) uvs[i] = new Vector2(uv[i * 2], uv[i * 2 + 1]);
                mesh.SetUVs(0, uvs);
            }
            int[] triangles;
            if (primitive.indices != null)
            {
                var index = glb.ReadIndices(primitive.indices.Value);
                triangles = new int[index.Length];
                for (int i = 0; i + 2 < index.Length; i += 3)
                { triangles[i] = index[i]; triangles[i + 1] = index[i + 2]; triangles[i + 2] = index[i + 1]; }
            }
            else
            {
                triangles = new int[vertices.Length];
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                { triangles[i] = i; triangles[i + 1] = i + 2; triangles[i + 2] = i + 1; }
            }
            mesh.SetTriangles(triangles, 0);
            if (primitive.attributes.NORMAL == 0) mesh.RecalculateNormals();
            mesh.RecalculateTangents(); mesh.RecalculateBounds();
            return mesh;
        }

        static int BuildNode(int nodeIndex, Transform parent, Material[] materials, int layer, List<MeshRenderer> opaque)
        {
            var node = glb.nodes[nodeIndex];
            var transform = Add(parent, node.name != null ? node.name : "node" + nodeIndex);
            if (node.translation != null && node.translation.Length == 3) transform.localPosition = Vec3(node.translation, 0);
            if (node.scale != null && node.scale.Length == 3)
                transform.localScale = new Vector3(node.scale[0], node.scale[1], node.scale[2]);
            if (node.rotation != null && node.rotation.Length == 4)
                transform.localRotation = new Quaternion(-node.rotation[0], node.rotation[1], node.rotation[2], node.rotation[3]).normalized;
            if (node.mesh != null)
            {
                var glbMesh = glb.meshes[node.mesh.Value];
                for (int p = 0; p < glbMesh.primitives.Length; p++)
                {
                    var primitive = glbMesh.primitives[p];
                    if (p > 0) continue; // Sketchfab exports one primitive per mesh here.
                    var holder = Add(transform, glbMesh.name != null ? glbMesh.name + "_mesh" : "mesh");
                    holder.gameObject.layer = layer;
                    holder.gameObject.AddComponent<MeshFilter>().sharedMesh = BuildMesh(glbMesh, p);
                    var renderer = holder.gameObject.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = materials[primitive.material != null ? primitive.material.Value : 0];
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    opaque.Add(renderer);
                }
            }
            if (node.children != null)
                foreach (int child in node.children) BuildNode(child, transform, materials, layer, opaque);
            return 0;
        }

        static Transform Build()
        {
            var native = DataLoader.LoadAsset<Transform>("@:Entities/Vehicles/VTruck4x4/VTruck4x4P.prefab", false);
            if (native == null) throw new InvalidOperationException("Native jeep prefab missing");
            cache = new GameObject("MechaPrefabCache"); cache.SetActive(false); UnityEngine.Object.DontDestroyOnLoad(cache);
            var root = UnityEngine.Object.Instantiate(native, cache.transform, false); root.name = "BusterDrone";
            foreach (var lod in root.GetComponentsInChildren<LODGroup>(true)) lod.enabled = false;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            foreach (var particles in root.GetComponentsInChildren<ParticleSystem>(true))
            { particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); var emission = particles.emission; emission.enabled = false; }
            var colliders = root.GetComponentsInChildren<Collider>(true).ToList();
            var first = colliders.FirstOrDefault(c => !c.isTrigger && !(c is WheelCollider));
            int layer = first != null ? first.gameObject.layer : root.gameObject.layer;
            foreach (var c in colliders) if (!(c is WheelCollider)) c.enabled = false;
            var rb = root.GetComponentInChildren<Rigidbody>(true);
            if (rb == null) throw new InvalidOperationException("Native jeep physics root missing");
            var physics = rb.transform;
            var visual = Add(physics, "MechaVisual");

            if (bodyMaterial == null)
            {
                var byName = glb.materials.ToDictionary(m => m.name, StringComparer.OrdinalIgnoreCase);
                bodyMaterial = Material(byName["body"], true);
                legMaterial = Material(byName["material"], false);
            }
            var materials = new[] { legMaterial, bodyMaterial, legMaterial };

            var mount = Add(visual, "MechaRoot");
            mount.localRotation = Rules.MountRotation; mount.localScale = Vector3.one * Rules.MountScale;
            var renderers = new List<MeshRenderer>();
            foreach (int sceneNode in glb.scenes[glb.scene != null ? glb.scene.Value : 0].nodes)
            {
                var node = glb.nodes[sceneNode];
                // Skip the Sketchfab display environment (ground disc + sky dome).
                if (node.name == "Sketchfab_model" || node.name == "BusterDrone.fbx" || node.name == "Object_2")
                {
                    foreach (int child in node.children ?? new int[0]) BuildNode(child, mount, materials, layer, renderers);
                    continue;
                }
                BuildNode(sceneNode, mount, materials, layer, renderers);
            }
            if (renderers.Count > 0)
            {
                var group = visual.gameObject.AddComponent<LODGroup>();
                group.SetLODs(new[] { new LOD(0f, renderers.Cast<Renderer>().ToArray()) });
                group.RecalculateBounds();
            }

            Box(physics, "MechaBodyHit", new Vector3(0, 1.5f, 0), new Vector3(2.4f, 2.2f, 2.8f), layer);
            rb.mass = 8000; rb.centerOfMass = new Vector3(0, 1f, 0);
            foreach (var wheel in root.GetComponentsInChildren<WheelCollider>(true))
            {
                var at = physics.InverseTransformPoint(wheel.transform.position);
                wheel.transform.position = physics.TransformPoint(new Vector3(at.x < 0 ? -1.36f : 1.36f, .52f, at.z < 0 ? -2.1f : 2.1f));
                wheel.radius = .4f; wheel.suspensionDistance = .3f;
                var spring = wheel.suspensionSpring; spring.spring = 180000; spring.damper = 22000; spring.targetPosition = .5f;
                wheel.suspensionSpring = spring;
            }
            Deploy.LoadClip(glb);
            return root;
        }

        static void Box(Transform parent, string name, Vector3 center, Vector3 size, int layer)
        {
            var box = Add(parent, name);
            box.localPosition = center; box.gameObject.layer = layer;
            var collider = box.gameObject.AddComponent<BoxCollider>(); collider.size = size;
        }

        public static void ClearRuntime()
        {
            rigs.Clear();
            Deploy.Clear();
        }
    }
}
