using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Asset pipeline smoke test only. Game animation/controller integration is
// deliberately a separate step; a successful bundle is not a completed NPC mod.
public static class EscortAssetBuild
{
    [Serializable] public class MaterialFile { public MaterialSpec[] materials; }
    [Serializable] public class MaterialSpec { public string name,texture; public float[] color; }
    const string Version = "2022.3.62f2";
    const string Model = "Assets/Character/Sakura.fbx";
    const string Prefab = "Assets/Character/SakuraPreview.prefab";

    [MenuItem("Sakura Escort/Build and validate Windows preview bundle")]
    public static void Build()
    {
        if (Application.unityVersion != Version && Application.unityVersion != Version + "c1")
            throw new Exception("Use Unity " + Version + "; found " + Application.unityVersion);
        AssetDatabase.Refresh();
        var importer = AssetImporter.GetAtPath(Model) as ModelImporter;
        if (importer == null) throw new Exception("Missing exported character FBX: " + Model);
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.importAnimation = false;
        importer.isReadable = true;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.SaveAndReimport();
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
        if (source == null) throw new Exception("FBX import failed");
        var specs=JsonUtility.FromJson<MaterialFile>(File.ReadAllText("Assets/Character/materials.json"));
        var materials=new System.Collections.Generic.Dictionary<string,Material>();
        foreach(var spec in specs.materials)
        {
            var mat=new Material(Shader.Find("Standard"));
            mat.color=new Color(spec.color[0],spec.color[1],spec.color[2],spec.color[3]);
            mat.SetFloat("_Metallic",0);mat.SetFloat("_Glossiness",0.1f);
            if(!string.IsNullOrEmpty(spec.texture))
            {
                var tex=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Character/"+spec.texture);
                if(tex==null)throw new Exception("Missing texture: "+spec.texture);
                mat.mainTexture=tex;
            }
            var path="Assets/Character/"+spec.name+".mat";
            var old=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(old==null)AssetDatabase.CreateAsset(mat,path);
            else {EditorUtility.CopySerialized(mat,old);UnityEngine.Object.DestroyImmediate(mat);mat=old;}
            materials.Add(spec.name,mat);
        }
        var root = new GameObject("SakuraPreview");
        try
        {
            var model = UnityEngine.Object.Instantiate(source, root.transform);
            model.name = "Model";
            var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length == 0 || renderers.All(r => r.sharedMesh == null))
                throw new Exception("Character has no skinned meshes");
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
            {
                renderer.sharedMaterials = renderer.sharedMaterials.Select(m => materials[m.name]).ToArray();
                if (renderer.sharedMesh == null || renderer.bones.Length == 0)
                    throw new Exception("Mesh lost skin or skeleton: " + renderer.name);
                if (renderer.sharedMaterials.Any(m => m == null || m.shader == null))
                    throw new Exception("Missing character material: " + renderer.name);
                bounds.Encapsulate(renderer.bounds);
            }
            if (bounds.size.y < 1.3f || bounds.size.y > 2.1f)
                throw new Exception("Unexpected character height in metres: " + bounds.size.y);
            PrefabUtility.SaveAsPrefabAsset(root, Prefab);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        AssetDatabase.SaveAssets();
        string output = Path.GetFullPath("Build/Windows");
        Directory.CreateDirectory(output);
        var map = new[] { new AssetBundleBuild {
            assetBundleName = "sakura-preview.unity3d", assetNames = new[] { Prefab }
        }};
        var manifest = BuildPipeline.BuildAssetBundles(output, map,
            BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,
            BuildTarget.StandaloneWindows64);
        if (manifest == null) throw new Exception("Asset bundle build failed");
        var bundle = AssetBundle.LoadFromFile(Path.Combine(output, "sakura-preview.unity3d"));
        if (bundle == null) throw new Exception("Built bundle cannot be loaded");
        try
        {
            var prefab = bundle.LoadAsset<GameObject>(Prefab.ToLowerInvariant());
            if (prefab == null) throw new Exception("Character prefab missing from bundle");
            var instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                foreach (var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
                    if (renderer.sharedMaterials.Any(m => m == null || m.shader == null))
                        throw new Exception("Bundle lost base color texture");
                if (instance.GetComponentsInChildren<SkinnedMeshRenderer>().Length == 0)
                    throw new Exception("Bundle instantiation lost the character renderers");
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }
        finally { bundle.Unload(true); }
        File.WriteAllText(Path.Combine(output, "verified.txt"),
            "Unity " + Application.unityVersion + "\n" + DateTime.UtcNow.ToString("O") +
            "\nPASS: FBX skin/material/height/TDA textures, Windows bundle build, reload and instantiate.\n" +
            "NOT YET VERIFIED: 7DTD model controller, animations, NPC AI or multiplayer.\n");
        Debug.Log("SAKURA_ASSET_PIPELINE_PASS " + output);
    }
}



