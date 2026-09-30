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

        public sealed class Rig
        {
            public Transform Root, Visual, Mount, Torso, Head, HandL, HandR, ShoulderL, ShoulderR, Backpack;
            public Transform HipL, HipR, KneeL, KneeR;
            public float LegUpper, LegLower, GroundY;
        }
        static readonly Dictionary<EntityVehicle, Rig> rigs = new Dictionary<EntityVehicle, Rig>();

        public static void Install(Harmony h)
        {
            var glbPath = System.IO.Path.Combine(Path, "combat_robot.glb");
            if (!File.Exists(glbPath)) throw new FileNotFoundException("combat_robot.glb missing");
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

        // Anchors are found by joint names created during the rig rebuild.
        public static Rig GetRig(EntityVehicle v)
        {
            Rig rig;
            if (rigs.TryGetValue(v, out rig) && rig.Visual != null) return rig;
            var root = Find(v.PhysicsTransform != null ? v.PhysicsTransform : v.transform, "MechaVisual");
            if (root == null) return null;
            rig = new Rig { Root = root.parent != null ? root.parent : root, Visual = root };
            rig.Mount = Find(root, "MechaMount");
            rig.Torso = Find(root, "MechaTorso");
            rig.Head = Find(root, "MechaHead");
            rig.HandL = Find(root, "MechaHandL"); rig.HandR = Find(root, "MechaHandR");
            rig.ShoulderL = Find(root, "MechaShoulderL"); rig.ShoulderR = Find(root, "MechaShoulderR");
            rig.Backpack = Find(root, "MechaBackpack");
            rig.HipL = Find(root, "MechaHipL"); rig.HipR = Find(root, "MechaHipR");
            rig.KneeL = Find(root, "MechaKneeL"); rig.KneeR = Find(root, "MechaKneeR");
            // Leg links are proportional to the QA-tuned target height.
            rig.LegUpper = Rules.TargetHeight * .48f;
            rig.LegLower = Rules.TargetHeight * .48f;
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
        { return reference == null ? -1 : glb.textures[reference.index].source; }

        // Builds a Standard material per GLB material; the display Floor
        // material maps to null and its meshes are skipped entirely.
        static Material BuildMaterial(GlbFile.GlbMaterial source)
        {
            if (source == null || (source.name != null && source.name.IndexOf("Floor", StringComparison.OrdinalIgnoreCase) >= 0))
                return null;
            var shader = Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("Standard shader missing");
            var m = new Material(shader) { name = "Mecha_" + (source.name ?? "part") };
            int color = TextureIndex(source.pbrMetallicRoughness == null ? null : source.pbrMetallicRoughness.baseColorTexture);
            if (color >= 0) m.mainTexture = Tex("tex_" + color + ".png", false);
            int bump = TextureIndex(source.normalTexture);
            if (bump >= 0) { m.SetTexture("_BumpMap", Tex("tex_" + bump + ".png", true)); m.EnableKeyword("_NORMALMAP"); }
            int ao = TextureIndex(source.occlusionTexture);
            if (ao >= 0) m.SetTexture("_OcclusionMap", Tex("tex_" + ao + ".png", true));
            int mr = TextureIndex(source.pbrMetallicRoughness == null ? null : source.pbrMetallicRoughness.metallicRoughnessTexture);
            if (mr >= 0) { m.SetTexture("_MetallicGlossMap", Tex("tex_" + mr + ".png", true)); m.EnableKeyword("_METALLICGLOSSMAP"); m.SetFloat("_GlossMapScale", 1f); }
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", .6f);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", .45f);
            int glow = TextureIndex(source.emissiveTexture);
            if (glow >= 0)
            {
                m.SetTexture("_EmissionMap", Tex("tex_" + glow + ".png", true));
                m.EnableKeyword("_EMISSION");
                var factor = source.emissiveFactor;
                var tint = factor != null && factor.Length == 3 ? new Color(factor[0], factor[1], factor[2]) : Color.white;
                m.SetColor("_EmissionColor", tint * 1.4f);
            }
            return m;
        }

        static Transform Add(Transform parent, string name)
        { var t = new GameObject(name).transform; t.SetParent(parent, false); return t; }

        // glTF is right-handed; Unity is left-handed. Mirror X for points,
        // quaternions and triangle winding so the robot is not inside out.
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

        static int BuildNode(int nodeIndex, Transform parent, Material[] materials, int layer, List<MeshRenderer> parts)
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
                    parts.Add(renderer);
                }
            }
            if (node.children != null)
                foreach (int child in node.children) BuildNode(child, transform, materials, layer, parts);
            return 0;
        }

        // The Sketchfab export is a flat sculpture: ~154 mesh nodes parented to
        // one root with no joint hierarchy. This rebuilds a biped rig at load
        // time by classifying every mesh part by world position and material
        // suffix (_Hands_0/_Torse_0/_Legs_0), then re-parenting parts under
        // runtime-created Hip/Knee/Shoulder joints with world positions kept.
        static void RebuildBipedRig(Transform mount, List<MeshRenderer> parts)
        {
            Bounds all = default(Bounds);
            bool first = true;
            foreach (var part in parts)
            {
                var b = part.bounds;
                if (first) { all = b; first = false; }
                else all.Encapsulate(b);
            }
            float height = all.size.y;
            float hipY = all.min.y + height * .5f;
            float kneeY = all.min.y + height * .27f;
            float torsoHalfWidth = Mathf.Max(.04f, all.size.x * .16f);

            Transform hipL = Add(mount, "MechaHipL"); hipL.position = new Vector3(-all.size.x * .12f, hipY, 0);
            Transform hipR = Add(mount, "MechaHipR"); hipR.position = new Vector3(all.size.x * .12f, hipY, 0);
            Transform kneeL = Add(hipL, "MechaKneeL"); kneeL.position = new Vector3(-all.size.x * .12f, kneeY, 0);
            Transform kneeR = Add(hipR, "MechaKneeR"); kneeR.position = new Vector3(all.size.x * .12f, kneeY, 0);
            Transform torso = Add(mount, "MechaTorso"); torso.position = new Vector3(0, hipY, 0);
            float shoulderY = all.max.y - height * .22f;
            Transform shoulderL = Add(torso, "MechaShoulderL"); shoulderL.position = new Vector3(-torsoHalfWidth * 1.1f, shoulderY, 0);
            Transform shoulderR = Add(torso, "MechaShoulderR"); shoulderR.position = new Vector3(torsoHalfWidth * 1.1f, shoulderY, 0);
            Transform head = Add(torso, "MechaHead"); head.position = new Vector3(0, all.max.y - height * .08f, all.center.z);
            float armLength = height * .3f;
            Transform handL = Add(shoulderL, "MechaHandL"); handL.position = shoulderL.position + Vector3.down * armLength;
            Transform handR = Add(shoulderR, "MechaHandR"); handR.position = shoulderR.position + Vector3.down * armLength;
            Transform backpack = Add(torso, "MechaBackpack"); backpack.position = new Vector3(0, hipY + height * .3f, all.min.z);

            foreach (var part in parts)
            {
                var name = RigSuffix(part.transform);
                var center = part.bounds.center;
                bool arm = name.IndexOf("Hands", StringComparison.OrdinalIgnoreCase) >= 0;
                bool leg = name.IndexOf("Legs", StringComparison.OrdinalIgnoreCase) >= 0;
                Transform target;
                if (arm && center.y > hipY)
                    target = center.x < 0 ? shoulderL : shoulderR;
                else if (leg || (!arm && center.y < hipY))
                {
                    bool left = center.x < 0;
                    bool upper = center.y >= kneeY;
                    target = upper ? (left ? hipL : hipR) : (left ? kneeL : kneeR);
                }
                else target = torso;
                part.transform.SetParent(target, true);
            }
        }

        static string RigSuffix(Transform part)
        {
            for (var node = part; node != null; node = node.parent)
            {
                if (node.name.IndexOf("_Hands_0", StringComparison.OrdinalIgnoreCase) >= 0) return "Hands";
                if (node.name.IndexOf("_Torse_0", StringComparison.OrdinalIgnoreCase) >= 0) return "Torse";
                if (node.name.IndexOf("_Legs_0", StringComparison.OrdinalIgnoreCase) >= 0) return "Legs";
                if (node.parent != null && node.parent.name == "MechaVisual") break;
            }
            return "Torse";
        }

        static Transform Build()
        {
            var native = DataLoader.LoadAsset<Transform>("@:Entities/Vehicles/VTruck4x4/VTruck4x4P.prefab", false);
            if (native == null) throw new InvalidOperationException("Native jeep prefab missing");
            cache = new GameObject("MechaPrefabCache"); cache.SetActive(false); UnityEngine.Object.DontDestroyOnLoad(cache);
            var root = UnityEngine.Object.Instantiate(native, cache.transform, false); root.name = "CombatRobot";
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

            var materials = new Material[glb.materials.Length];
            for (int i = 0; i < glb.materials.Length; i++) materials[i] = BuildMaterial(glb.materials[i]);

            var mount = Add(visual, "MechaMount");
            var parts = new List<MeshRenderer>();
            foreach (int sceneNode in glb.scenes[glb.scene != null ? glb.scene.Value : 0].nodes)
            {
                var node = glb.nodes[sceneNode];
                // Skip Sketchfab wrapper layers and the display floor.
                if (node.name == "Sketchfab_model" || node.name.IndexOf(".fbx", StringComparison.OrdinalIgnoreCase) >= 0 || node.name == "RootNode")
                {
                    foreach (int child in node.children ?? new int[0]) BuildNode(child, mount, materials, layer, parts);
                    continue;
                }
                if (node.name != null && node.name.StartsWith("Floor", StringComparison.OrdinalIgnoreCase)) continue;
                BuildNode(sceneNode, mount, materials, layer, parts);
            }
            // Drop meshes that mapped to the Floor material and rebuild bounds.
            for (int i = parts.Count - 1; i >= 0; i--)
                if (parts[i].sharedMaterial == null)
                { UnityEngine.Object.Destroy(parts[i].gameObject); parts.RemoveAt(i); }
            if (parts.Count == 0) throw new InvalidOperationException("Combat robot produced no renderable parts");

            RebuildBipedRig(mount, parts);

            // Auto-fit the mount to the target height, then rotate for facing.
            Bounds all = default(Bounds); bool firstB = true;
            foreach (var part in parts)
            { if (firstB) { all = part.bounds; firstB = false; } else all.Encapsulate(part.bounds); }
            float scale = Rules.TargetHeight / Mathf.Max(.01f, all.size.y);
            var fit = Find(visual, "MechaMount");
            if (fit != null)
            {
                Vector3 bottom = fit.position;
                fit.localScale = Vector3.one * scale;
                fit.position = bottom + Vector3.up * (all.min.y * (scale - 1f));
                fit.localRotation = Rules.MountRotation;
            }

            Box(physics, "MechaBodyHit", new Vector3(0, 1.5f, 0), new Vector3(2.2f, 2.6f, 1.8f), layer);
            // Low-friction belly pad: crater rims slide under the hull instead
            // of perching the robot on its keel with all wheels airborne.
            var belly = Add(physics, "MechaBellySlider");
            belly.localPosition = new Vector3(0, .35f, 0);
            belly.gameObject.layer = layer;
            var bellyCollider = belly.gameObject.AddComponent<BoxCollider>();
            bellyCollider.size = new Vector3(2f, .1f, 3.2f);
            var bellyMaterial = new PhysicMaterial("MechaBelly") { dynamicFriction = .05f, staticFriction = .05f, frictionCombine = PhysicMaterialCombine.Minimum };
            bellyCollider.material = bellyMaterial;
            rb.mass = 8000; rb.centerOfMass = new Vector3(0, 1f, 0);
            foreach (var wheel in root.GetComponentsInChildren<WheelCollider>(true))
            {
                var at = physics.InverseTransformPoint(wheel.transform.position);
                wheel.transform.position = physics.TransformPoint(new Vector3(at.x < 0 ? -.85f : .85f, .52f, at.z < 0 ? -1.5f : 1.5f));
                wheel.radius = .5f; wheel.suspensionDistance = .4f;
                var spring = wheel.suspensionSpring; spring.spring = 220000; spring.damper = 26000; spring.targetPosition = .5f;
                wheel.suspensionSpring = spring;
            }
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
