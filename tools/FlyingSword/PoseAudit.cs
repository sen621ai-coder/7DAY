using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using PZAEC.FlyingSword;
using Object=UnityEngine.Object;

// Runs only in the isolated native QA assembly, never shipped in the mod DLL.
public static class JuquePoseAudit
{
    static Action<string,bool> check;
    public static void Run(EntityJuque v,EntityPlayer p,Action<string,bool> verify)
    {
        check=verify;
        var sdcs=p.emodel as EModelSDCS;
        if(sdcs!=null){HarmonyLib.AccessTools.Field(typeof(EModelSDCS),"archetype").SetValue(sdcs,Archetype.GetArchetype("BaseFemale").Clone());foreach(var name in new[]{"armorLumberjackOutfit","armorLumberjackBoots","armorLumberjackGloves"}){var iv=ItemClass.GetItem(name,false);p.equipment.SetSlotItem((int)((ItemClassArmor)iv.ItemClass).EquipSlot,iv);}sdcs.SwitchModelAndView(false,false);sdcs.SetVisible(true,false);}
        var root=p.emodel?.GetModelTransform();var animator=p.emodel?.avatarController?.GetAnimator();
        var template=DataLoader.LoadAsset<GameObject>("@:Entities/Player/Common/BaseRigs/baseRigPrefab.prefab",false).GetComponent<Animator>();
        Log.Out("[JuquePose] template avatar="+template.avatar+" human="+template.isHuman+" liveAvatar="+animator.avatar+" humanClip="+animator.runtimeAnimatorController.animationClips[0].isHumanMotion);
        if(animator.avatar==null&&template.avatar!=null)animator.avatar=template.avatar;
        Log.Out("[JuquePose] model="+root+" animator="+animator+" human="+(animator!=null&&animator.isHuman));
        if(root==null)return;
        var dump=new List<string>();
        foreach(var t in root.GetComponentsInChildren<Transform>(true))dump.Add(t.name+" pos="+t.position+" local="+t.localPosition+" rot="+t.localEulerAngles);
        if(animator!=null){foreach(var a in animator.parameters)dump.Add("PARAM "+a.name+" "+a.type);foreach(var clip in animator.runtimeAnimatorController.animationClips.Distinct())dump.Add("CLIP "+clip.name);}
        foreach(var r in root.GetComponentsInChildren<Renderer>(true))dump.Add("RENDER "+r.name+" enabled="+r.enabled+" active="+r.gameObject.activeInHierarchy+" bounds="+r.bounds);
        File.WriteAllLines(Path.Combine(SwordMod.Root,"pose-rig.txt"),dump);
        Capture(v,p,"pose-initial");
        Log.Out("[JuquePose] animation speed="+animator.speed+" initialized="+animator.isInitialized+" clips="+animator.GetCurrentAnimatorClipInfo(0).Length);
        animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.enabled=true;animator.speed=1;animator.Rebind();animator.Update(0);
        for(int layer=0;layer<animator.layerCount;layer++)Log.Out("[JuquePose] LAYER "+layer+" "+animator.GetLayerName(layer)+" weight="+animator.GetLayerWeight(layer)+" clips="+string.Join(",",animator.GetCurrentAnimatorClipInfo(layer).Select(c=>c.clip.name)));
        p.rotation=Vector3.zero;v.rotation=Vector3.zero;
        for(int i=0;i<60;i++)Step(p,animator);
        Capture(v,p,"pose-empty-hover");
        v.Throttle=1;v.Boost=true;v.vehicleRB.transform.Find("JuqueVisual").localRotation=Quaternion.Euler(9,0,-12);
        for(int i=0;i<30;i++)Step(p,animator);
        Capture(v,p,"pose-empty-boost-turn");
        p.rotation=new Vector3(-60,120,0);for(int i=0;i<40;i++)Step(p,animator);
        var deck=v.vehicleRB.transform.Find("JuqueVisual");check("unarmed rider follows sword instead of mouse",Vector3.Angle(animator.transform.forward,deck.forward)<1);
        foreach(var side in new[]{"Left","Right"}){var hand=animator.GetComponentsInChildren<Transform>(true).First(t=>t.name==side+"Hand");var local=animator.transform.InverseTransformPoint(hand.position);check("unarmed "+side+" hand outside torso",Mathf.Abs(local.x)>.24f&&local.y>.7f&&local.y<1.15f);}
        Capture(v,p,"pose-empty-look-away");p.rotation=Vector3.zero;
        var gun=ItemClass.GetItem("gunMGT3M60",false);p.inventory.SetItem(1,new ItemStack(gun,1));p.inventory.SetHoldingItemIdxNoHolsterTime(1);
        v.Throttle=0;v.Boost=false;v.vehicleRB.transform.Find("JuqueVisual").localRotation=Quaternion.identity;p.AimingGun=true;
        foreach(float yaw in new[]{0f,90f,135f,-135f}){
            p.rotation=new Vector3(-35,yaw,0);p.emodel.avatarController.SetAiming(true);animator.SetInteger("WeaponHoldType",p.inventory.holdingItem.HoldType.Value);
            for(int i=0;i<90;i++)Step(p,animator);
            Capture(v,p,"pose-M60-yaw"+yaw.ToString("0"));
        }
        foreach(float pitch in new[]{-80f,45f}){p.rotation=new Vector3(pitch,0,0);for(int i=0;i<90;i++)Step(p,animator);Capture(v,p,"pose-M60-pitch"+pitch.ToString("0"));}
        foreach(float yaw in new[]{135f,-135f}){p.rotation=new Vector3(-80,yaw,0);for(int i=0;i<90;i++)Step(p,animator);Capture(v,p,"pose-M60-down80-yaw"+yaw.ToString("0"));}
        p.rotation=new Vector3(-35,-135,0);for(int i=0;i<90;i++)Step(p,animator);
        p.emodel.avatarController.StartAnimationFiring();for(int i=0;i<6;i++)Step(p,animator);Capture(v,p,"pose-M60-firing");
        p.emodel.avatarController.StartAnimationReloading();for(int i=0;i<30;i++)Step(p,animator);Capture(v,p,"pose-M60-reload");
        Log.Out("[JuquePose] active clips="+string.Join(",",animator.GetCurrentAnimatorClipInfo(0).Select(c=>c.clip.name)));
        p.AimingGun=false;p.rotation=Vector3.zero;p.inventory.SetHoldingItemIdxNoHolsterTime(0);
    }
    static void Step(EntityPlayer p,Animator a){p.emodel.avatarController.Update();SwordPresentation.PrepareAnimator(p,a);a.Update(1f/60);HarmonyLib.AccessTools.Method(p.emodel.avatarController.GetType(),"LateUpdate")?.Invoke(p.emodel.avatarController,null);SwordPresentation.ApplyPose(p,1f/60);}
    public static void Capture(EntityJuque v,EntityPlayer p,string name)
    {
        var root=p.emodel.GetModelTransform();var visual=v.vehicleRB.transform.Find("JuqueVisual");
        var anim=p.emodel.avatarController.GetAnimator();Log.Out("[JuquePose] "+name+" hold="+p.inventory.holdingItem.GetItemName()+" param="+anim.GetInteger("WeaponHoldType")+" state="+anim.GetCurrentAnimatorStateInfo(0).fullPathHash+" initialized="+anim.isInitialized);
        bool assess=name!="pose-initial";
        if(assess){check(name+" body meshes",root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Count(r=>r.enabled&&r.gameObject.activeInHierarchy)>=3);var held=p.inventory.GetHoldingItemTransform()?.Find("JuqueVisual");if(name.StartsWith("pose-empty"))check(name+" empty hands",held==null||!held.GetComponent<Renderer>().enabled);}
        foreach(var side in new[]{"Left","Right"}){var foot=root.GetComponentsInChildren<Transform>(true).First(t=>t.name==side+"Foot");var toe=foot.GetComponentsInChildren<Transform>(true).First(t=>t.name==side+"ToeBase");var center=foot.position-visual.up*Mathf.Abs(toe.localPosition.y)+foot.forward*.045f;var local=Quaternion.Inverse(visual.rotation)*(center-visual.position);Log.Out("[JuquePose] "+name+" "+side+" sole="+local.ToString("F4"));}
        if(assess)foreach(var side in new[]{"Left","Right"}){var foot=root.GetComponentsInChildren<Transform>(true).First(t=>t.name==side+"Foot");var toe=foot.GetComponentsInChildren<Transform>(true).First(t=>t.name==side+"ToeBase");var local=Quaternion.Inverse(visual.rotation)*(foot.position-visual.up*Mathf.Abs(toe.localPosition.y)+foot.forward*.045f-visual.position);check(name+" "+side+" support",Mathf.Abs(local.y-.025f)<.008f&&Mathf.Abs(local.x)<=.06f);}
        if(p.inventory.IsHoldingGun()){var data=p.inventory.holdingItemData.actionData[0] as ItemActionRanged.ItemActionDataRanged;float angle=data?.muzzle!=null?Vector3.Angle(data.muzzle.forward,p.GetLookVector()):180;Log.Out("[JuquePose] muzzle="+data?.muzzle+" forward="+data?.muzzle?.forward+" look="+p.GetLookVector()+" errorDegrees="+angle);if(name!="pose-M60-reload"){check(name+" native gun carry",anim.GetInteger("WeaponCarry")==AnimationDelayData.AnimationDelay[p.inventory.holdingItem.HoldType.Value].Carry);check(name+" muzzle aim within 15 degrees",angle<15);}}
        var layers=new Dictionary<GameObject,int>();foreach(var t in root.GetComponentsInChildren<Transform>(true)){layers[t.gameObject]=t.gameObject.layer;t.gameObject.layer=30;}
        var aura=v.vehicleRB.transform.Find("Juque flight aura");if(aura!=null)foreach(var t in aura.GetComponentsInChildren<Transform>(true)){layers[t.gameObject]=t.gameObject.layer;t.gameObject.layer=30;}
        // Camera.Render in a synchronous dedicated fixture does not advance the
        // normal skinning frame. Bake from the current native bone transforms.
        var baked=new List<GameObject>();var skins=root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();
        foreach(var skin in skins){var mesh=new Mesh();skin.BakeMesh(mesh);var go=new GameObject("QA baked "+skin.name);go.transform.SetParent(skin.transform,false);go.layer=30;go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;skin.enabled=false;baked.Add(go);}
        int oldLayer=visual.gameObject.layer;visual.gameObject.layer=30;
        var camera=new GameObject("Juque pose QA camera").AddComponent<Camera>();camera.cullingMask=1<<30;camera.targetTexture=new RenderTexture(1200,1000,24);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.15f,.19f,.23f);camera.fieldOfView=36;
        var focus=visual.position+Vector3.up*.9f;camera.transform.position=focus+new Vector3(4,2,-6);camera.transform.LookAt(focus);
        var light=new GameObject("Juque pose QA light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=.6f;light.transform.rotation=Quaternion.Euler(45,-25,0);
        camera.Render();var old=RenderTexture.active;RenderTexture.active=camera.targetTexture;var tex=new Texture2D(1200,1000,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1200,1000),0,0);tex.Apply();File.WriteAllBytes(Path.Combine(SwordMod.Root,name+".png"),tex.EncodeToPNG());RenderTexture.active=old;
        foreach(var pair in layers)pair.Key.layer=pair.Value;visual.gameObject.layer=oldLayer;
        foreach(var skin in skins)skin.enabled=true;foreach(var go in baked){Object.DestroyImmediate(go.GetComponent<MeshFilter>().sharedMesh);Object.DestroyImmediate(go);}
        Object.Destroy(tex);Object.Destroy(camera.targetTexture);Object.Destroy(camera.gameObject);Object.Destroy(light.gameObject);
    }
}
