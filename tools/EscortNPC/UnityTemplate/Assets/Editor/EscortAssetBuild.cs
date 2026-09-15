using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Asset pipeline smoke test only. Game animation/controller integration is
// deliberately a separate step; a successful bundle is not a completed NPC mod.
public static class EscortAssetBuild
{
    const string Version = "2022.3.62f2";
    const string Model = "Assets/Character/Sakura.fbx";
    const string Prefab = "Assets/Character/SakuraPreview.prefab";

    [MenuItem("Sakura Escort/Build and validate Windows preview bundle")]
    public static void Build()
    {
        if (Application.unityVersion != Version)
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
                if (instance.GetComponentsInChildren<SkinnedMeshRenderer>().Length == 0)
                    throw new Exception("Bundle instantiation lost the character renderers");
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }
        finally { bundle.Unload(true); }
        File.WriteAllText(Path.Combine(output, "verified.txt"),
            "Unity " + Application.unityVersion + "\n" + DateTime.UtcNow.ToString("O") +
            "\nPASS: FBX skin/material/height, Windows bundle build, reload and instantiate.\n" +
            "NOT YET VERIFIED: 7DTD model controller, animations, NPC AI or multiplayer.\n");
        Debug.Log("SAKURA_ASSET_PIPELINE_PASS " + output);
    }
}
