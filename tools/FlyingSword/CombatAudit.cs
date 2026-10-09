using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using PZAEC.FlyingSword;
using Object=UnityEngine.Object;
public static class JuqueCombatAudit
{
    static Action<string,bool> verify;
    static void Step(EntityPlayer p,Animator a,SwordAttackPose pose,float now)
    {p.emodel.avatarController.Update();SwordPresentation.PrepareAnimator(p,a);a.Update(1f/60);HarmonyLib.AccessTools.Method(p.emodel.avatarController.GetType(),"LateUpdate")?.Invoke(p.emodel.avatarController,null);p.inventory.GetHoldingItemTransform()?.Find("JuqueVisual")?.GetComponent<JuqueGrip>()?.Align();pose.Advance(now,1f/60);foreach(var ps in p.GetComponentsInChildren<ParticleSystem>())ps.Simulate(1f/60,false,false,true);}
    public static void Run(EntityPlayer p,Action<string,bool> check)
    {
        verify=check;
        p.SetPosition(new Vector3(0,350,0));p.rotation=Vector3.zero;p.onGround=true;p.AimingGun=false;
        var item=ItemClass.GetItem(SwordRules.Prefix+19,false);SwordRules.EnsureId(item);p.inventory.SetItem(0,new ItemStack(item,1));p.inventory.SetHoldingItemIdxNoHolsterTime(0);
        SwordPresentation.ApplyPose(p,0);var a=SwordPresentation.BodyAnimator(p);a.transform.position=p.position-Origin.position;
        foreach(var bridge in a.GetComponentsInChildren<AnimationEventBridge>(true))HarmonyLib.AccessTools.Field(typeof(AnimationEventBridge),"_entity").SetValue(bridge,p);
        a.Rebind();a.Update(0);var pose=SwordAttackVisuals.For(p);
        for(int i=0;i<60;i++)Step(p,a,pose,Time.time+i/60f);
        var visual=p.inventory.GetHoldingItemTransform().Find("JuqueVisual");check("ground sword original renderer visible",visual!=null&&visual.GetComponent<Renderer>().enabled);
        var hand=a.GetComponentsInChildren<Transform>(true).First(t=>t.name=="RightHand");check("sword handle center meets actual palm",Vector3.Distance(visual.TransformPoint(JuqueGrip.HandleCenter),JuqueGrip.Palm(hand))<.005f);
        Capture(p,"ground-idle");
        Capture(p,"ground-grip-close");Capture(p,"ground-grip-back");
        pose.Charging=true;for(int i=0;i<100;i++)Step(p,a,pose,Time.time+i/60f);
        check("charge gathering emits real particles",p.GetComponentsInChildren<ParticleSystem>().Any(ps=>ps.name=="Juque gathering light"&&ps.particleCount>0));Capture(p,"ground-full-charge");
        int suppressed=SwordAttackVisuals.CosmeticSuppressions;pose.Charging=false;SwordAttackVisuals.Release(p);var played=new HashSet<string>();for(int i=0;i<14;i++){Step(p,a,pose,Time.time+i/60f);for(int layer=0;layer<a.layerCount;layer++)foreach(var c in a.GetCurrentAnimatorClipInfo(layer).Concat(a.GetNextAnimatorClipInfo(layer)))if(c.weight>.001f)played.Add(c.clip.name);}Log.Out("[JuqueGround] release clips="+string.Join(",",played));
        check("wave swing suppresses native melee damage callback",SwordAttackVisuals.CosmeticSuppressions>suppressed);
        check("wave release plays actual native attack clip",played.Contains("3P_1HMelee_Fire"));
        var ribbon=Object.FindObjectsOfType<MeshFilter>().FirstOrDefault(m=>m.name=="Juque blade afterimage");check("native swing produces blade path mesh",ribbon!=null&&ribbon.sharedMesh.vertexCount>=4&&ribbon.sharedMesh.bounds.size.magnitude>.3f);Capture(p,"ground-release-swing");
        var head=p.getHeadPosition();var origin=SwordCombat.BladeOrigin(p,Vector3.forward);check("wave origin is below head and on sword side",origin.y<head.y-.3f&&origin.x>head.x+.2f);
        var go=new GameObject("Juque audit crescent");var wave=go.AddComponent<SwordCrescent>();wave.Init(Vector3.forward,1);wave.Shape(origin-Origin.position,2,Enumerable.Repeat(45f,33).ToArray(),1);
        check("charged crescent is a filled mesh eight metres wide",wave.Width==8&&go.GetComponent<MeshFilter>().sharedMesh.triangles.Length>500&&go.GetComponent<LineRenderer>()==null);
        Capture(p,"ground-charged-wave",true);
        var mesh=go.GetComponent<MeshFilter>().sharedMesh;wave.Shape(origin-Origin.position,3,Enumerable.Repeat(0f,33).ToArray(),1);check("blocked crescent lanes become transparent",mesh.colors.All(c=>c.a==0));Object.DestroyImmediate(go);
        go=new GameObject("Juque audit normal crescent");wave=go.AddComponent<SwordCrescent>();wave.Init(Vector3.forward,0);wave.Shape(origin-Origin.position,2,Enumerable.Repeat(35f,33).ToArray(),1);check("normal crescent remains one metre wide",wave.Width==1);Capture(p,"ground-normal-wave");Object.DestroyImmediate(go);
        var burst=SwordAttackVisuals.Burst(origin-Origin.position+Vector3.forward*1.2f);check("impact dispersal emits real particles",burst!=null&&burst.GetComponent<ParticleSystem>().particleCount>0);burst.GetComponent<ParticleSystem>().Simulate(.09f,false,false,true);Capture(p,"ground-impact");Object.DestroyImmediate(burst);
        var old=SwordMod.Settings.Effects;SwordMod.Settings.Effects=0;pose.Charging=true;pose.Advance(Time.time,.016f);check("effects disabled cancels charge particles",!pose.Charging&&p.GetComponentsInChildren<ParticleSystem>().All(ps=>ps.particleCount==0));SwordMod.Settings.Effects=old;
        SwordCombat.Clear();SwordCombat.Schedule(p,p.inventory.GetItem(0).itemValue,Vector3.forward,1);SwordCombat.Tick(p.world,.1f);
        var waves=HarmonyLib.AccessTools.Field(typeof(SwordCombat),"waves").GetValue(null) as System.Collections.IList;check("release has real swing anticipation",waves.Count==0);SwordCombat.Tick(p.world,.1f);check("wave spawns after anticipation",waves.Count==1);SwordCombat.Clear();
        SwordCombat.Schedule(p,p.inventory.GetItem(0).itemValue,Vector3.forward,0);SwordCombat.Tick(p.world,.001f);check("uncharged wave launches immediately without anticipation",waves.Count==1);SwordCombat.Clear();
        SwordCombat.Schedule(p,p.inventory.GetItem(0).itemValue,Vector3.forward,0);p.inventory.SetHoldingItemIdxNoHolsterTime(1);SwordCombat.Tick(p.world,.1f);SwordCombat.Tick(p.world,.1f);check("switching weapon before launch cancels pending wave",waves.Count==0);SwordCombat.Clear();
        Object.DestroyImmediate(pose);
    }
    static void Capture(EntityPlayer p,string name,bool wide=false)
    {
        var root=p.emodel.GetModelTransform();var layers=new Dictionary<GameObject,int>();
        var roots=new List<GameObject>{root.gameObject};roots.AddRange(Object.FindObjectsOfType<SwordCrescent>().Select(c=>c.gameObject));roots.AddRange(Object.FindObjectsOfType<JuqueAttackBurst>().Select(c=>c.gameObject));roots.AddRange(Object.FindObjectsOfType<MeshFilter>().Where(c=>c.name=="Juque blade afterimage").Select(c=>c.gameObject));roots.AddRange(p.GetComponentsInChildren<ParticleSystem>().Select(c=>c.gameObject));
        foreach(var r in roots)foreach(var t in r.GetComponentsInChildren<Transform>(true))if(!layers.ContainsKey(t.gameObject)){layers[t.gameObject]=t.gameObject.layer;t.gameObject.layer=30;}
        var baked=new List<GameObject>();var skins=root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();
        foreach(var skin in skins){var mesh=new Mesh();skin.BakeMesh(mesh);var go=new GameObject("QA baked ground body");go.transform.SetParent(skin.transform,false);go.layer=30;go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;skin.enabled=false;baked.Add(go);}
        var cam=new GameObject("Juque ground QA camera").AddComponent<Camera>();cam.cullingMask=1<<30;cam.targetTexture=new RenderTexture(1400,1000,24);cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.12f,.16f,.2f);cam.fieldOfView=wide?50:40;
        bool close=name=="ground-grip-close";var hand=root.GetComponentsInChildren<Transform>(true).First(t=>t.name=="RightHand");var focus=close?JuqueGrip.Palm(hand):root.position+Vector3.up*.9f+(wide?Vector3.forward:Vector3.zero);cam.transform.position=focus+(close?new Vector3(.45f,.2f,.55f):name=="ground-grip-back"?new Vector3(3,1.4f,-4):wide?new Vector3(6,3,8):new Vector3(3,1.4f,4));cam.transform.LookAt(focus);
        Log.Out("[JuqueGround] "+name+" p="+p.position+" origin="+Origin.position+" root="+root.position+" head="+p.getHeadPosition()+" camera="+cam.transform.position+" focus="+focus+" skins="+skins.Length+" active="+root.gameObject.activeInHierarchy);
        var light=new GameObject("Juque ground QA light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1;light.transform.rotation=Quaternion.Euler(40,160,0);
        cam.Render();var old=RenderTexture.active;RenderTexture.active=cam.targetTexture;var tex=new Texture2D(1400,1000,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1400,1000),0,0);tex.Apply();var pixels=tex.GetPixels32();var bg=pixels[0];verify(name+" rendered nonempty frame",pixels.Count(c=>c.r!=bg.r||c.g!=bg.g||c.b!=bg.b)>1000);File.WriteAllBytes(Path.Combine(SwordMod.Root,name+".png"),tex.EncodeToPNG());RenderTexture.active=old;
        foreach(var pair in layers)pair.Key.layer=pair.Value;foreach(var skin in skins)skin.enabled=true;foreach(var go in baked){Object.DestroyImmediate(go.GetComponent<MeshFilter>().sharedMesh);Object.DestroyImmediate(go);}Object.Destroy(tex);Object.Destroy(cam.targetTexture);Object.Destroy(cam.gameObject);Object.Destroy(light.gameObject);
    }
}
