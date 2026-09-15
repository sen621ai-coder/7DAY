using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
public static class MintAssetBuild
{
    const string Prefab="Assets/Character/MintGuardian.prefab";
    public static void Build()
    {
        AssetDatabase.Refresh();
        var importer=(ModelImporter)AssetImporter.GetAtPath("Assets/Character/Mint.fbx");
        importer.animationType=ModelImporterAnimationType.Legacy;importer.importAnimation=true;importer.isReadable=true;
        importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;
        importer.SaveAndReimport();
        var clips=importer.defaultClipAnimations;
        foreach(var clip in clips){clip.loopTime=true;clip.wrapMode=WrapMode.Loop;}
        importer.clipAnimations=clips;importer.SaveAndReimport();
        var map=new Dictionary<string,string>{
            {"MI_019_mint_swimsuit_hair_01","T_019_mint_swimsuit_hair_01_d.png"},
            {"MI_player_019_mint_eyes","T_player_019_mint_eyes_d.png"},
            {"MI_player_019_mint_face_new","T_player_019_mint_face_d1.png"},
            {"MI_player_019_mint_hair_2","T_player_019_mint_hair_02_d.png"},
            {"MI_player_019_mint_jiemao","T_player_019_mint_face_d1.png"},
            {"MI_player_019_mint_swimsuit_01","T_player_019_mint_swimsuit_01_d.png"},
            {"MI_player_019_mint_swimsuit_02","image_(1).jpg"},
            {"MI_player_019_mint_swimsuit_03","T_player_019_mint_swimsuit_02_d.png"},
            {"MI_player_019_mint_swimsuit_04","T_player_019_mint_swimsuit_03_d.png"}
        };
        var root=new GameObject("MintGuardian");
        var model=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/Mint.fbx"),root.transform);
        foreach(var loose in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            if(!loose.name.StartsWith("player_019_mint_swimsuit_skin",StringComparison.Ordinal))
                UnityEngine.Object.DestroyImmediate(loose.gameObject);
        var initialAnimation=model.GetComponent<Animation>();
        if(initialAnimation!=null && initialAnimation.clip!=null)initialAnimation.clip.SampleAnimation(model,0);
        var materials=new Dictionary<string,Material>();
        foreach(var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            renderer.updateWhenOffscreen=true;
            renderer.sharedMaterials=renderer.sharedMaterials.Select(original=>{
                string name=original.name;Material result;
                if(materials.TryGetValue(name,out result))return result;
                result=new Material(Shader.Find("Standard")){name=name};
                string filename;
                if(map.TryGetValue(name,out filename)){
                    result.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Character/Textures/"+filename);
                    if(result.mainTexture==null)throw new Exception("Missing texture "+filename);
                    result.SetFloat("_Mode",1);result.SetFloat("_Cutoff",.25f);result.EnableKeyword("_ALPHATEST_ON");result.renderQueue=2450;
                }else if(name=="MI_common_face_mask" || name=="MI_player_019_mint_gaoguang"){
                    result.color=Color.clear;result.SetFloat("_Mode",1);result.SetFloat("_Cutoff",.5f);result.EnableKeyword("_ALPHATEST_ON");result.renderQueue=2450;
                }else throw new Exception("Unmapped material "+name);
                result.SetFloat("_Glossiness",.1f);result.SetFloat("_Metallic",0);
                string path="Assets/Character/"+name+".mat";
                var old=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(old==null)AssetDatabase.CreateAsset(result,path);else{EditorUtility.CopySerialized(result,old);UnityEngine.Object.DestroyImmediate(result);result=old;}
                materials.Add(name,result);return result;
            }).ToArray();
        }
        var renderers=model.GetComponentsInChildren<SkinnedMeshRenderer>();
        var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
        float scale=1.68f/bounds.size.y;model.transform.localScale*=scale;
        model.transform.localPosition=new Vector3(-bounds.center.x*scale,-bounds.min.y*scale,-bounds.center.z*scale);
        var animation=model.GetComponent<Animation>();
        if(animation==null || animation.clip==null || animation.clip.length<=0)throw new Exception("Mint animation missing");
        animation.playAutomatically=true;animation.wrapMode=WrapMode.Loop;
        PrefabUtility.SaveAsPrefabAsset(root,Prefab);AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Build/Windows");
        var manifest=BuildPipeline.BuildAssetBundles("Build/Windows",new[]{new AssetBundleBuild{assetBundleName="mint-guardian.unity3d",assetNames=new[]{Prefab}}},BuildAssetBundleOptions.ChunkBasedCompression|BuildAssetBundleOptions.StrictMode,BuildTarget.StandaloneWindows64);
        if(manifest==null)throw new Exception("Bundle build failed");
        var bundle=AssetBundle.LoadFromFile("Build/Windows/mint-guardian.unity3d");
        if(bundle==null || bundle.LoadAsset<GameObject>(Prefab)==null)throw new Exception("Bundle reload failed");
        bundle.Unload(true);
        File.WriteAllText("Build/Windows/verified.txt","PASS Mint prefab, texture mapping, animation and bundle reload; animation seconds="+animation.clip.length);
        // Visual check of the imported clip at its beginning and middle.
        var camera=new GameObject("PreviewCamera").AddComponent<Camera>();
        camera.transform.position=new Vector3(2,1.7f,5);camera.transform.LookAt(new Vector3(0,.88f,0));camera.orthographic=true;camera.orthographicSize=1.1f;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.15f,.17f,.2f);
        var light=new GameObject("Light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.2f;light.transform.rotation=Quaternion.Euler(25,180,0);
        RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=Color.gray;
        var target=new RenderTexture(680,900,24);camera.targetTexture=target;
        for(int i=0;i<2;i++){
            animation.clip.SampleAnimation(model,i==0?0:animation.clip.length/2);
            var objects=new List<GameObject>();
            foreach(var skin in renderers){var mesh=new Mesh();skin.BakeMesh(mesh);var go=new GameObject("Bake");go.transform.SetPositionAndRotation(skin.transform.position,skin.transform.rotation);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;objects.Add(go);skin.enabled=false;}
            camera.Render();RenderTexture.active=target;var image=new Texture2D(680,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,680,900),0,0);image.Apply();File.WriteAllBytes("Build/mint-"+i+".png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
            foreach(var go in objects){UnityEngine.Object.DestroyImmediate(go.GetComponent<MeshFilter>().sharedMesh);UnityEngine.Object.DestroyImmediate(go);}foreach(var skin in renderers)skin.enabled=true;
        }
        RenderTexture.active=null;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(root);
    }
}

