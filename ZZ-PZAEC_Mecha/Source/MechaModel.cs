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
        static Transform prefab,completePrefab; static GameObject cache;
        static Transform Prefab(bool full){if(full){if(completePrefab==null)completePrefab=Build(true);return completePrefab;}if(prefab==null)prefab=Build();return prefab;}

        public sealed class Rig
        {
            public Transform Root, Visual, Mount, Torso, Head, HandL, HandR, ShoulderL, ShoulderR, Backpack;
            public Transform ChestL, ChestR, ChestDoor, WingL, WingR, Sword;
            public Renderer[] FirstPersonHidden; public float ArmUpper,ArmLower;
            public float ShoulderLimit=115,ElbowLimit=140,WristLimit=175,BladeRadius=.065f;
            public Vector3 SwordRootAnchor=Samurai.BladeRoot-Samurai.Grip,SwordTipAnchor=Samurai.BladeTip-Samurai.Grip,HiltAnchor=new Vector3(.1607f,.3657f,.5823f);
            public Vector3 SwordRestNormal=new Vector3(.9664597f,-.0473823f,-.2524097f);
            public Transform HipL, HipR, KneeL, KneeR, AnkleL, AnkleR, FootL, FootR, ElbowL, ElbowR;
            public readonly Dictionary<Transform, Quaternion> RestRot = new Dictionary<Transform, Quaternion>();
            public readonly Dictionary<Transform, Vector3> RestPos = new Dictionary<Transform, Vector3>();
            public bool ActionBaseReady;
            public Vector3 ActionBaseTorsoPosition,ActionSoleL,ActionSoleR,ActionNormalL=Vector3.up,ActionNormalR=Vector3.up;
            public Quaternion ActionBaseTorsoRotation=Quaternion.identity;
            public Transform[] ContactJoints;
            public Quaternion[] ContactRotations;
            public Vector3[] ContactPositions;
            public void ResetPose() { ActionBaseReady=false;foreach(var p in RestRot) if(p.Key!=null) p.Key.localRotation=p.Value; foreach(var p in RestPos) if(p.Key!=null) p.Key.localPosition=p.Value; }
            public float LegUpper, LegLower, GroundY;
            public Vector3 TorsoBasePosition;
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
            if (__1 == null || !Rules.VehicleNameMatches(__1.entityClassName)) return true;
            var selected=Prefab(string.Equals(__1.entityClassName,Rules.CompleteVehicle,StringComparison.OrdinalIgnoreCase));
            __instance.PrefabT = selected;
            __instance.prefabHandle = new ReadyAsset(selected.gameObject);
            return false;
        }

        static bool Preview(ItemActionSpawnVehicle __instance, ItemActionData __0)
        {
            var player = __0.invData.holdingEntity as EntityPlayerLocal;
            if (player == null || !Rules.ItemNameMatches(player.inventory.holdingItem.GetItemName())) return true;
            var data = (ItemActionSpawnVehicle.ItemActionDataSpawnVehicle)__0;
            if (data.VehiclePreviewT != null) UnityEngine.Object.DestroyImmediate(data.VehiclePreviewT.gameObject);
            var selected=Prefab(string.Equals(player.inventory.holdingItem.GetItemName(),Rules.CompleteItem,StringComparison.OrdinalIgnoreCase));
            var root = new GameObject("MechaPlacementPreview").transform;
            var visual = UnityEngine.Object.Instantiate(Find(selected.transform, "MechaVisual").gameObject, root, false);
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
            rig.Sword=Find(root,"MechaSword");
            rig.WingL=Find(root,"MechaWingL");rig.WingR=Find(root,"MechaWingR");
            rig.ChestL=Find(root,"MechaChestL");rig.ChestR=Find(root,"MechaChestR");rig.ChestDoor=Find(root,"MechaChestDoor");
            rig.HipL = Find(root, "MechaHipL"); rig.HipR = Find(root, "MechaHipR");
            rig.KneeL = Find(root, "MechaKneeL"); rig.KneeR = Find(root, "MechaKneeR");
            // Gait/Boarding animate Torso relative to its rigged rest pose.
            if (rig.Torso != null) rig.TorsoBasePosition = rig.Torso.localPosition;
            rig.AnkleL=Find(root,"MechaAnkleL"); rig.AnkleR=Find(root,"MechaAnkleR");
            rig.FootL=Find(root,"MechaFootL"); rig.FootR=Find(root,"MechaFootR");
            rig.ElbowL=Find(root,"MechaElbowL"); rig.ElbowR=Find(root,"MechaElbowR");
            rig.LegUpper = rig.HipL!=null && rig.KneeL!=null ? Vector3.Distance(rig.HipL.position,rig.KneeL.position) : 0;
            rig.LegLower = rig.KneeL!=null && rig.AnkleL!=null ? Vector3.Distance(rig.KneeL.position,rig.AnkleL.position) : 0;
            foreach(var t in new[]{rig.Torso,rig.Head,rig.HipL,rig.HipR,rig.KneeL,rig.KneeR,rig.AnkleL,rig.AnkleR,rig.ShoulderL,rig.ShoulderR,rig.ElbowL,rig.ElbowR,rig.HandL,rig.HandR,rig.ChestL,rig.ChestR,rig.ChestDoor,rig.WingL,rig.WingR,rig.Sword})
                if(t!=null) { rig.RestRot[t]=t.localRotation; rig.RestPos[t]=t.localPosition; }
            rig.ArmUpper=Vector3.Distance(rig.ShoulderR.position,rig.ElbowR.position);rig.ArmLower=Vector3.Distance(rig.ElbowR.position,rig.HandR.position);
            CacheFirstPersonDisplay(rig);rigs[v] = rig;
            return rig;
        }

        public static void CacheFirstPersonDisplay(Rig rig)
        {
            var hidden=new List<Renderer>();foreach(var part in rig.Visual.GetComponentsInChildren<MechaRenderPart>(true))if(!part.FirstPersonVisible){var renderer=part.GetComponent<Renderer>();if(renderer!=null)hidden.Add(renderer);}
            foreach(var renderer in rig.Visual.GetComponentsInChildren<Renderer>(true))
                if(!hidden.Contains(renderer)&&(renderer.transform.IsChildOf(rig.Head)||renderer.GetComponentInParent<MechaRenderPart>()==null))hidden.Add(renderer);
            rig.FirstPersonHidden=hidden.ToArray();
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

        static Material CompleteMaterial()
        {
            var m=new Material(Shader.Find("Standard")){name="Mecha_Complete",color=Color.white};
            m.mainTexture=Tex("../CompleteTextures/base.png",false);
            m.SetTexture("_MetallicGlossMap",Tex("../CompleteTextures/metallic.png",true));m.EnableKeyword("_METALLICGLOSSMAP");m.SetFloat("_GlossMapScale",1f);
            return m;
        }
        static Transform Build(bool full=false)
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

            var materials = full?new[]{CompleteMaterial()}:new Material[glb.materials.Length];
            if(!full)for (int i = 0; i < glb.materials.Length; i++) materials[i] = BuildMaterial(glb.materials[i]);

            var mount = Add(visual, "MechaMount");
            RobotRig.Build(mount, materials, layer,full?"samurai_style_gundam_mecha":"combat_robot");
            mount.localRotation = Rules.MountRotation;

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
            rb.mass = 8000; rb.centerOfMass = new Vector3(0, .7f, 0);
            foreach (var wheel in root.GetComponentsInChildren<WheelCollider>(true))
            {
                var at = physics.InverseTransformPoint(wheel.transform.position);
                // Wide stance: the narrow biped track flipped the hull over
                // on spawn; keep the tripod-era footprint while the rig math
                // is being rebuilt.
                wheel.transform.position = physics.TransformPoint(new Vector3(at.x < 0 ? -1.2f : 1.2f, .52f, at.z < 0 ? -1.8f : 1.8f));
                var forwardFriction=wheel.forwardFriction;forwardFriction.stiffness=0f;wheel.forwardFriction=forwardFriction;
                var sideFriction=wheel.sidewaysFriction;sideFriction.stiffness=0f;wheel.sidewaysFriction=sideFriction;
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
