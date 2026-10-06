using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using PZAEC.M1;

// Opt-in isolated native fixture, never installed into the live Mods directory.
public sealed class M1NativeQA:IModApi
{
    static readonly List<string> results=new List<string>();
    static readonly List<Entity> entities=new List<Entity>();static int serial;
    static void Check(bool ok,string label){if(!ok)throw new Exception(label);results.Add("PASS "+label);}
    static void CheckFlashPixels(Transform flash,bool billboard=false)
    {
        var cameraObject=new GameObject("M1 flash render QA");var camera=cameraObject.AddComponent<Camera>();
        var target=new RenderTexture(256,256,24);var pixels=new Texture2D(256,256,TextureFormat.RGB24,false);var previous=RenderTexture.active;
        var position=flash.localPosition;var rotation=flash.localRotation;var layer=flash.gameObject.layer;
        try{
            flash.position=new Vector3(0,300,0);flash.rotation=Quaternion.identity;flash.gameObject.layer=30;
            camera.enabled=false;camera.targetTexture=target;camera.cullingMask=1<<30;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.fieldOfView=30;camera.nearClipPlane=.01f;camera.farClipPlane=3;
            Check(flash.GetComponent<Renderer>().sharedMaterial.shader.isSupported,"effect shader supported by native graphics device");
            foreach(var offset in billboard?new[]{Vector3.back}:new[]{Vector3.back,Vector3.right}){
                camera.transform.position=flash.position+offset;camera.transform.LookAt(flash.position);
                var renderer=flash.GetComponent<Renderer>();renderer.enabled=false;camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,256,256),0,0);pixels.Apply();var baseline=pixels.GetPixels32();
                renderer.enabled=true;camera.Render();pixels.ReadPixels(new Rect(0,0,256,256),0,0);pixels.Apply();var visible=pixels.GetPixels32();int changed=0;
                for(int i=0;i<visible.Length;i++)if(Math.Abs(visible[i].r-baseline[i].r)+Math.Abs(visible[i].g-baseline[i].g)+Math.Abs(visible[i].b-baseline[i].b)>30)changed++;
                Check(changed>5,"effect changes rendered pixels from "+offset+" pixels="+changed);
            }
        }finally{flash.localPosition=position;flash.localRotation=rotation;flash.gameObject.layer=layer;RenderTexture.active=previous;camera.targetTexture=null;target.Release();UnityEngine.Object.Destroy(cameraObject);UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(pixels);}
    }
    public void InitMod(Mod mod){if(Environment.GetCommandLineArgs().Contains("-m1NativeQA"))ModEvents.GameStartDone.RegisterHandler(Run);}
    static void CheckNativeParticle(GameObject effect,string name,float age)
    {
        Check(effect!=null,name+" native prefab spawns");var cameraObject=new GameObject("M1 native particle render QA");var camera=cameraObject.AddComponent<Camera>();var target=new RenderTexture(256,256,24);var pixels=new Texture2D(256,256,TextureFormat.RGB24,false);var previous=RenderTexture.active;
        try{
            effect.transform.position=new Vector3(0,300,0);foreach(var t in effect.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
            var systems=effect.GetComponentsInChildren<ParticleSystem>(true);Check(systems.Length>0,name+" contains actual particle systems");foreach(var system in systems){system.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);system.Simulate(age,false,true,false);}
            results.Add("INFO "+name+" simulated age="+age+" particles="+systems.Sum(p=>p.particleCount));
            var renderers=effect.GetComponentsInChildren<Renderer>(true);var enabled=renderers.Select(r=>r.enabled).ToArray();camera.enabled=false;camera.targetTexture=target;camera.cullingMask=1<<30;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.fieldOfView=60;camera.nearClipPlane=.01f;camera.farClipPlane=30;camera.transform.position=effect.transform.position+Vector3.back*6;camera.transform.LookAt(effect.transform.position);
            foreach(var r in renderers)r.enabled=false;camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,256,256),0,0);pixels.Apply();var baseline=pixels.GetPixels32();
            for(int i=0;i<renderers.Length;i++)renderers[i].enabled=enabled[i];camera.Render();pixels.ReadPixels(new Rect(0,0,256,256),0,0);pixels.Apply();var visible=pixels.GetPixels32();int changed=0;for(int i=0;i<visible.Length;i++)if(Math.Abs(visible[i].r-baseline[i].r)+Math.Abs(visible[i].g-baseline[i].g)+Math.Abs(visible[i].b-baseline[i].b)>30)changed++;
            if(changed>5)Check(true,name+" native particles change rendered pixels="+changed);
            else{results.Add("FINDING "+name+" native resource emits particles but rendered pixel difference="+changed+"; client visibility not confirmed");foreach(var r in renderers)results.Add("INFO renderer "+r.name+" enabled="+r.enabled+" shader="+(r.sharedMaterial==null?"none":r.sharedMaterial.shader.name)+" bounds="+r.bounds);}
        }finally{RenderTexture.active=previous;camera.targetTexture=null;target.Release();UnityEngine.Object.Destroy(effect);UnityEngine.Object.Destroy(cameraObject);UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(pixels);}
    }
    static T Spawn<T>(World world,string name,Vector3 position) where T:Entity
    {var e=EntityFactory.CreateEntity(EntityClass.FromString(name),position) as T;if(e==null)throw new Exception("factory "+name);entities.Add(e);world.SpawnEntityInWorld(e);return e;}
    static void Intent(World w,EntityVehicle vehicle,EntityPlayer player,byte mode,bool fire,bool zoom,Vector3 point)
    {
        var origin=vehicle.position+Vector3.up*3;
        Secondary.Request(w,player.entityId,new NetPackageM1SecondaryIntent{Vehicle=vehicle.entityId,Serial=++serial,Select=mode,Flags=(byte)((fire?1:0)|(zoom?2:0)),Origin=origin,Direction=(point-origin).normalized});
    }
    static bool NativeFlashSuccess(ref bool __result){__result=true;return false;}
    static void CheckNetworkEffects(World world,EntityVehicle vehicle)
    {
        // Inactive marker bypasses gameplay initialization. It enables the real
        // cosmetic receivers in this dedicated fixture, not a playable client.
        var marker=new GameObject("M1 cosmetic receiver QA player");marker.SetActive(false);
        var local=marker.AddComponent<EntityPlayerLocal>();var cameraObject=new GameObject("M1 cosmetic receiver QA camera");local.playerCamera=cameraObject.AddComponent<Camera>();local.playerCamera.enabled=false;
        var primary=AccessTools.Field(typeof(World),"m_LocalPlayerEntity");var previous=primary.GetValue(world);primary.SetValue(world,local);
        try{
            CheckNativeParticle(GameManager.Instance.ExplosionClient(vehicle.position+Vector3.forward*20,Quaternion.identity,5,0,5,2500,-1,new List<BlockChangeInfo>()),"HE rocket explosion",.1f);
            results.Add("INFO Native M60 is optional decoration; guaranteed MG muzzle core is tested independently of native success");
            var mainViews=(System.Collections.IDictionary)AccessTools.Field(typeof(Presentation),"views").GetValue(null);
            Presentation.Receive(world,NetPackageM1Event.Make(vehicle.entityId,123456,100,700,Weapons.StateEvent,Time.time,Vector3.zero,Vector3.zero,0,0));
            Presentation.Receive(world,NetPackageM1Event.Make(vehicle.entityId,123456,99,700,Weapons.ShotEvent,Time.time,vehicle.position+Vector3.up*2,Vector3.forward*250,4.8f,0));
            var view=(Presentation.View)mainViews[vehicle.entityId];Check(view.Flame.gameObject.activeSelf,"main cannon real shot receiver survives newer state packet");CheckFlashPixels(view.Flame);
            var puffs=(System.Collections.IList)AccessTools.Field(typeof(Presentation),"puffs").GetValue(null);
            int beforeImpact=puffs.Count;
            Presentation.Receive(world,NetPackageM1Event.Make(vehicle.entityId,123456,101,700,Weapons.ImpactEvent,Time.time,vehicle.position+Vector3.forward*20,Vector3.back,ImpactRules.Encode(ImpactSurface.Organic),1));
            Check(puffs.Count==beforeImpact+2&&view.ImpactShot==700,"AP real impact receiver generates fireball and flash on organic target");int before=puffs.Count;
            Presentation.Receive(world,NetPackageM1Event.Make(vehicle.entityId,123456,101,700,Weapons.ImpactEvent,Time.time,vehicle.position+Vector3.forward*20,Vector3.back,ImpactRules.Encode(ImpactSurface.Organic),1));
            Check(puffs.Count==before,"duplicate main impact cannot replay effects");
            var secondaryViews=(System.Collections.IDictionary)AccessTools.Field(typeof(SecondaryPresentation),"views").GetValue(null);
            var status=new NetPackageM1SecondaryEvent{Vehicle=vehicle.entityId,Epoch=123456,Serial=100,Kind=1,Time=Time.time};SecondaryPresentation.Receive(world,status);
            var mg=new NetPackageM1SecondaryEvent{Vehicle=vehicle.entityId,Epoch=123456,Serial=99,Kind=2,Time=Time.time,A=vehicle.position+Vector3.up*3,B=vehicle.position+Vector3.forward*100};mg.I[0]=3;var nativeMethod=AccessTools.Method(typeof(SecondaryPresentation),"NativeFlash");var forcedSuccess=AccessTools.Method(typeof(M1NativeQA),"NativeFlashSuccess");var helper=new Harmony("M1.QA.nativeFlashSuccess");helper.Patch(nativeMethod,prefix:new HarmonyMethod(forcedSuccess));try{SecondaryPresentation.Receive(world,mg);}finally{helper.Unpatch(nativeMethod,forcedSuccess);}
            var secondary=secondaryViews[vehicle.entityId];var type=secondary.GetType();var mgFlash=(Transform)type.GetField("Flash").GetValue(secondary);Check(Math.Abs((float)type.GetField("ShotAt").GetValue(secondary)-Time.time)<.00001f,"MG real receiver always schedules guaranteed muzzle flash");SecondaryPresentation.Update(world);
            Check(mgFlash.gameObject.activeSelf,"MG fallback muzzle flash activates through presentation update");CheckFlashPixels(mgFlash);
            results.Add("INFO MG hit feedback: no independent hit particle; original target damage feedback only");
            var trails=(System.Collections.IList)AccessTools.Field(typeof(SecondaryPresentation),"trails").GetValue(null);Check(trails.Count==1,"MG every-third-shot tracer survives newer state packet");
            var aa=new NetPackageM1SecondaryEvent{Vehicle=vehicle.entityId,Epoch=123456,Serial=101,Kind=3,Time=Time.time,A=SecondaryModel.Find(vehicle.PhysicsTransform,"AAMuzzleL").position+Origin.position,B=Vector3.forward};aa.I[0]=900;SecondaryPresentation.Receive(world,aa);SecondaryPresentation.Update(world);
            var aaFlash=(Transform)type.GetField("ActiveAA").GetValue(secondary);Check(aaFlash!=null&&aaFlash.gameObject.activeSelf,"AA launch event activates selected tube flash");CheckFlashPixels(aaFlash);
            var flight=new NetPackageM1SecondaryEvent{Vehicle=vehicle.entityId,Epoch=123456,Serial=102,Kind=4,Time=Time.time,A=vehicle.position+Vector3.forward*50,B=Vector3.forward};flight.I[0]=900;SecondaryPresentation.Receive(world,flight);SecondaryPresentation.Update(world);
            Check(trails.Cast<object>().Any(t=>(bool)t.GetType().GetField("Missile").GetValue(t)),"AA motion receiver retains missile trail");
            int beforeMissile=puffs.Count;
            var impact=new NetPackageM1SecondaryEvent{Vehicle=vehicle.entityId,Epoch=123456,Serial=103,Kind=SecondaryRules.MissileHit,Time=Time.time,A=flight.A,B=Vector3.forward};impact.I[0]=900;SecondaryPresentation.Receive(world,impact);SecondaryPresentation.Update(world);
            Check(!trails.Cast<object>().Any(t=>(bool)t.GetType().GetField("Missile").GetValue(t)),"AA hit removes flight trail");
            Check(puffs.Count==beforeMissile+7,"AA real impact receiver creates four fire layers and three smoke puffs");
            var missileSound=(AudioSource)type.GetField("ImpactSound").GetValue(secondary);Check(missileSound!=null&&missileSound.clip!=null&&missileSound.isPlaying,"AA impact starts dedicated explosion audio");
            var fire=puffs[beforeMissile];var fireType=fire.GetType();Check((float)fireType.GetField("Life").GetValue(fire)==.65f,"AA fireball has a fixed visible lifetime");CheckFlashPixels(((GameObject)fireType.GetField("Go").GetValue(fire)).transform,true);
            int afterMissile=puffs.Count;SecondaryPresentation.Receive(world,impact);Check(puffs.Count==afterMissile,"duplicate AA hit cannot replay explosion");
            var expired=new NetPackageM1SecondaryEvent{Vehicle=vehicle.entityId,Epoch=123456,Serial=104,Kind=SecondaryRules.MissileExpired,Time=Time.time,A=flight.A,B=Vector3.forward};expired.I[0]=901;SecondaryPresentation.Receive(world,expired);
            Check(puffs.Count==afterMissile,"AA expiry creates no fake impact explosion");
            var visualEpoch=123456;var mainState=Weapons.States[vehicle.entityId];var authority=Secondary.States[vehicle.entityId];mainState.Epoch=visualEpoch;authority.Sequence=200;missileSound.Stop();
            var missileType=typeof(Secondary).GetNestedType("Missile",System.Reflection.BindingFlags.NonPublic);var missile=Activator.CreateInstance(missileType);var live=(System.Collections.IList)AccessTools.Field(typeof(Secondary),"missiles").GetValue(null);live.Clear();
            missileType.GetField("Vehicle").SetValue(missile,vehicle);missileType.GetField("Epoch").SetValue(missile,visualEpoch);missileType.GetField("Id").SetValue(missile,902);missileType.GetField("Target").SetValue(missile,-1);missileType.GetField("Guided").SetValue(missile,false);missileType.GetField("Age").SetValue(missile,5f);missileType.GetField("Position").SetValue(missile,vehicle.position+Vector3.up*50);missileType.GetField("Direction").SetValue(missile,Vector3.forward);live.Add(missile);
            Secondary.Advance(.01f);Check(live.Count==0&&puffs.Count==afterMissile&&!missileSound.isPlaying,"actual server missile timeout sends expiry without explosion or sound");

        }finally{primary.SetValue(world,previous);Presentation.Clear();SecondaryPresentation.Clear();UnityEngine.Object.Destroy(marker);UnityEngine.Object.Destroy(cameraObject);}
    }
    static void Run(ref ModEvents.SGameStartDoneData data)
    {
        if(GamePrefs.GetString(EnumGamePrefs.GameName)!="M1QA_Isolated")return;
        var world=GameManager.Instance.World;string report=Path.Combine(GameIO.GetSaveGameDir(),"m1-native-report.txt");
        try{
            Check(Weapons.Server,"native server authority");
            var apachePath=Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith("-m1ApacheDll="));
            if(apachePath!=null){
                var assembly=System.Reflection.Assembly.LoadFrom(apachePath.Substring("-m1ApacheDll=".Length));var type=assembly.GetType("AECT16RuntimeFix.ApacheArmor",true);
                new Harmony("pzaec.apache.armor.v1").Patch(AccessTools.Method(typeof(EntityVehicle),"ApplyDamage"),transpiler:new HarmonyMethod(AccessTools.Method(type,"ThresholdTranspiler")));
                Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(EntityVehicle),"ApplyDamage")).Transpilers.Count>=2,"real Apache and M1 threshold transpilers coexist");
            }
            var prefab=(Transform)AccessTools.Method(typeof(Model),"GetPrefab").Invoke(null,null);
            Check(prefab!=null,"native jeep-derived M1 prefab loads");
            var root=SecondaryModel.Find(prefab,"M1Visual");var lod=root.GetComponent<LODGroup>();Check(lod.GetLODs().Length==3,"three native LOD levels");
            var curb=new GameObject("M1QACurbProbe");var curbBox=curb.AddComponent<BoxCollider>();curbBox.size=new Vector3(4,.2f,.2f);
            try{
                foreach(string side in new[]{"L","R"}){
                    var sourceTrack=SecondaryModel.Find(prefab,"M1TrackContact"+side).GetComponent<MeshCollider>();
                    Check(sourceTrack.enabled&&sourceTrack.convex&&!sourceTrack.isTrigger,"solid convex track support "+side);
                    // The cached vehicle is inactive. Cook an active collider
                    // from its exact mesh before querying native penetration.
                    var probe=new GameObject("M1QAActiveTrack"+side);probe.transform.SetParent(curb.transform,false);
                    var track=probe.AddComponent<MeshCollider>();track.sharedMesh=sourceTrack.sharedMesh;track.convex=true;Physics.SyncTransforms();
                    Vector3 direction;float distance;
                    Check(Physics.ComputePenetration(track,Vector3.zero,Quaternion.identity,curbBox,new Vector3(0,.1f,0),Quaternion.identity,out direction,out distance)&&distance>.01f,"narrow curb contacts middle of track "+side);
                    Check(!Physics.ComputePenetration(track,Vector3.zero,Quaternion.identity,curbBox,new Vector3(0,-.1f,0),Quaternion.identity,out direction,out distance),"track support clears level ground "+side);
                    Check(!Physics.ComputePenetration(track,Vector3.zero,Quaternion.identity,curbBox,new Vector3(0,.1f,3.2f),Quaternion.identity,out direction,out distance),"front track bevel clears low approach "+side);
                    curbBox.size=new Vector3(.4f,.2f,.2f);
                    Check(!Physics.ComputePenetration(track,Vector3.zero,Quaternion.identity,curbBox,new Vector3(0,.1f,0),Quaternion.identity,out direction,out distance),"central belly gap remains open "+side);
                    curbBox.size=new Vector3(4,.2f,.2f);
                }
            }finally{UnityEngine.Object.Destroy(curb);}
            foreach(var level in lod.GetLODs())Check(level.renderers.All(r=>r!=null&&r.sharedMaterial!=null&&r.sharedMaterial.mainTexture!=null),"native meshes retain materials and textures");
            var preview=(Transform)AccessTools.Method(typeof(Model),"BuildPreview").Invoke(null,null);
            try{
                Check(preview.Find("Physics")==null,"placement preview has no physics subtree for native cleanup");
                Vehicle.SetupPreview(preview);
                Check(preview.GetComponentsInChildren<Renderer>(true).Any(r=>r.enabled&&r.sharedMaterial!=null),"native placement preview retains visible M1 geometry");
            }finally{UnityEngine.Object.Destroy(preview.gameObject);}
            foreach(string name in new[]{"RoofMGBase","RoofMGYaw","RoofMGPitch","RoofMGMuzzle","RoofMGSight","AAMount","AAPitch","AAMuzzleL","AAMuzzleR"})Check(SecondaryModel.Find(root,name)!=null,"native anchor "+name);
            var geometry=new SecondaryModel.Geometry(root);var pivot=SecondaryModel.Find(root,"RoofMGPitch");var muzzle=SecondaryModel.Find(root,"RoofMGMuzzle");
            Check(geometry.Clear(pivot.position+Origin.position,muzzle.position+Origin.position+muzzle.forward*.15f,SecondaryRules.MG),"neutral MG clears original tank triangles");
            var aa=SecondaryModel.Find(root,"AAPitch");muzzle=SecondaryModel.Find(root,"AAMuzzleL");
            Check(geometry.Clear(aa.position+Origin.position,muzzle.position+Origin.position+muzzle.forward*2,SecondaryRules.AA),"neutral AA clears original tank triangles");
            var vehicle=Spawn<EntityVehicle>(world,"vehicleM1Abrams",new Vector3(8,150,8));results.Add("INFO entity="+vehicle.EntityClass.entityClassName+" vehicle="+vehicle.vehicle.GetName()+" nativeId="+EntityClass.FromString("vehicleM1Abrams"));vehicle.vehicle.SetItemValue(ItemClass.GetItem("vehicleM1AbramsPlaceable",false));
            Check(Weapons.IsTank(vehicle),"native vehicle name resolves to an M1 tier: "+vehicle.vehicle.GetName());
            int initialHealth=vehicle.Health;vehicle.ApplyDamage(100000);Check(!vehicle.IsDead()&&vehicle.Health==initialHealth-100000,"native six-digit damage does not trigger vehicle instant destruction");
            Check(Combat.CombinedThreshold(123456,null)==123456,"non-M1 threshold preserves prior mod result");
            var main=Weapons.Register(vehicle);var state=Secondary.Get(main);Check(state.Muzzle!=null&&state.Left!=null&&state.Right!=null,"spawned server vehicle secondary rig");
            Weapons.Update();Physics.SyncTransforms();
            var probeStart=main.Model.TransformPoint(new Vector3(0,1.8f,-10))+Origin.position;
            var probeDirection=main.Model.forward;
            if(Voxel.Raycast(world,new Ray(probeStart,probeDirection),20,-538750997,8,0)){
                var h=Voxel.voxelRayHitInfo;results.Add("INFO chase hit tag="+h.tag+" transform="+h.transform+" entity="+ItemActionAttack.FindHitEntity(h));
            }
            Check(!Weapons.Trace(vehicle,probeStart,probeDirection,20,out var probeHit),"chase sight excludes own tank colliders");
            float aimYaw=15,aimPitch=4;var aimBefore=Optics.Advance(ref aimYaw,ref aimPitch,0,0,1);
            var aimAfter=Optics.Advance(ref aimYaw,ref aimPitch,0,0,8);
            Check(Quaternion.Angle(aimBefore,aimAfter)<.001f,"magnification does not move world sight direction");
            var fine=Optics.Advance(ref aimYaw,ref aimPitch,8,0,8);
            Check(Math.Abs(aimYaw-17)<.001f,"8x mouse aiming reduces angular delta eightfold");
            Optics.Advance(ref aimYaw,ref aimPitch,0,10000,1);Check(aimPitch==85,"sight elevation safely clamps before vertical singularity");
            foreach(int weapon in new[]{0,1,2}){float last=90;for(int step=0;step<(weapon==0?3:2);step++){float fov=Optics.Fov(65,Optics.Magnification(weapon,step));Check(fov>0&&fov<last,"native optic FOV decreases per step "+weapon+"/"+step);last=fov;}}
            var opticalObject=new GameObject("M1QAOpticCamera");var opticalCamera=opticalObject.AddComponent<Camera>();opticalCamera.enabled=false;
            try{
                opticalCamera.transform.position=new Vector3(3,4,5);opticalCamera.transform.rotation=Quaternion.Euler(10,20,30);opticalCamera.fieldOfView=65;
                var pose=AccessTools.Method(typeof(Optics),"ApplyPose");
                for(int i=0;i<5;i++)pose.Invoke(null,new object[]{opticalCamera,new Vector3(10,20,30),Quaternion.Euler(-4,90,0),Optics.Fov(65,8)});
                Check(Math.Abs(opticalCamera.fieldOfView-Optics.Fov(65,8))<.001f,"repeat camera render does not accumulate magnification");
                Optics.RestoreCamera();Check(opticalCamera.transform.position==new Vector3(3,4,5)&&Quaternion.Angle(opticalCamera.transform.rotation,Quaternion.Euler(10,20,30))<.01f&&Math.Abs(opticalCamera.fieldOfView-65)<.001f,"optic exit restores position rotation and native FOV");
                pose.Invoke(null,new object[]{opticalCamera,Vector3.zero,Quaternion.identity,15f});opticalCamera.fieldOfView=75;Optics.RestoreCamera();Check(opticalCamera.fieldOfView==75,"optic restoration preserves another camera system's FOV change");
            }finally{Optics.Clear();UnityEngine.Object.Destroy(opticalObject);}
            var opticRenderers=main.Model.GetComponentsInChildren<Renderer>(true);var originallyHidden=opticRenderers[0];originallyHidden.forceRenderingOff=true;
            AccessTools.Field(typeof(Optics),"model").SetValue(null,main.Model);
            AccessTools.Method(typeof(Optics),"Visibility").Invoke(null,new object[]{true});
            Check(opticRenderers.All(r=>r.forceRenderingOff),"optic hides tank model without disabling entity or colliders");
            Optics.Clear();Check(originallyHidden.forceRenderingOff&&opticRenderers.Skip(1).All(r=>!r.forceRenderingOff),"optic exit preserves originally hidden renderer and restores remaining hull");originallyHidden.forceRenderingOff=false;
            state.Gun.Rounds=37;state.Gun.Heat=66;state.Gun.Overheated=true;state.LeftAt=Time.time+8;state.RightAt=Time.time+3;Secondary.Save(state);
            var item=vehicle.vehicle.GetUpdatedItemValue().Clone();Check(item.TryGetMetadata("m1swRounds",out int rounds)&&rounds==37,"native vehicle ItemValue retains partial belt");
            using(var stream=new MemoryStream()){item.Write(new BinaryWriter(stream));stream.Position=0;var restored=new ItemValue();restored.Read(new BinaryReader(stream));Check(restored.TryGetMetadata("m1swRounds",out int savedRounds)&&savedRounds==37&&restored.TryGetMetadata("m1swHeat",out int heat)&&heat==66000,"native ItemValue binary save/read retains ammunition and heat");Check(restored.TryGetMetadata("m1swLeft",out int left)&&left>=7900&&restored.TryGetMetadata("m1swRight",out int right)&&right>=2900,"native binary save/read retains independent cooldowns");}
            Secondary.States.Remove(vehicle.entityId);state=Secondary.Get(main);Check(state.Gun.Rounds==37&&Math.Abs(state.Gun.Heat-66)<.001f&&state.Gun.Overheated,"reload state restores heat and ammunition");Check(state.LeftAt>Time.time+7.9f&&state.RightAt>Time.time+2.9f,"independent tube cooldown restored");
            for(int tier=1;tier<4;tier++){var other=Spawn<EntityVehicle>(world,"vehicleM1AbramsT"+(16+tier),vehicle.position+Vector3.right*(tier*20));Check(Weapons.Tier(other)==tier,"native lowercase tier "+(16+tier));Check(other.vehicle.GetMaxHealth()==Rules.Specs[tier].Health,"native tier health "+(16+tier));Check(Secondary.Get(Weapons.Register(other)).Left!=null,"native tier AA rig "+(16+tier));}
            var player=Spawn<EntityPlayer>(world,"playerMale",vehicle.position);player.MinEventContext.ItemValue=ItemValue.None;vehicle.AttachEntityToSelf(player,0);Check(Weapons.Seat(vehicle,player.entityId)==0,"native driver attachment");
            vehicle.bag.AddItem(new ItemStack(ItemClass.GetItem(Rules.Ammo,false),3));
            var cannonOrigin=SecondaryModel.Find(main.Model,"GunnerSight").position+Origin.position;
            var cannonTarget=Weapons.Pivot(main)+Weapons.Direction(main)*150;
            Secondary.Request(world,player.entityId,new NetPackageM1SecondaryIntent{Vehicle=vehicle.entityId,Serial=++serial,Select=0,Flags=2,Origin=cannonOrigin,Direction=(cannonTarget-cannonOrigin).normalized});
            state.Inputs[0].SwitchUntil=Time.time-1;main.NextFire=0;
            Secondary.Request(world,player.entityId,new NetPackageM1SecondaryIntent{Vehicle=vehicle.entityId,Serial=++serial,Select=0,Flags=3,Origin=cannonOrigin,Direction=(cannonTarget-cannonOrigin).normalized});
            AccessTools.Method(typeof(Weapons),"AimGun").Invoke(null,new object[]{main,1f});
            results.Add("INFO cannon reason="+main.Reason+" trigger="+main.Trigger.Held+" origin="+cannonOrigin);
            AccessTools.Method(typeof(Weapons),"Shoot").Invoke(null,new object[]{main});
            Check(main.Shot==1&&vehicle.bag.GetItemCount(ItemClass.GetItem(Rules.Ammo,false))==2,"native cannon optic intent passes aim, authority and fires exactly one shell");
            var oldBody=vehicle.vehicleRB.rotation;main.LastBody=oldBody;main.BodySeen=true;var worldGun=Weapons.Direction(main);
            vehicle.vehicleRB.rotation=Quaternion.Euler(3,10,2)*oldBody;Physics.SyncTransforms();Weapons.Stabilize(main);
            Check(Vector3.Angle(worldGun,Weapons.Direction(main))<.05f,"main gun compensates yaw pitch and roll of moving hull within limits");
            vehicle.vehicleRB.rotation=oldBody;Physics.SyncTransforms();Weapons.Stabilize(main);
            // Restore the fixture's neutral rig before independent MG/AA checks.
            main.Yaw=main.Pitch=0;main.YawNode.localRotation=main.PitchNode.localRotation=Quaternion.identity;main.LastShot=-100;main.Trigger.Stop();Physics.SyncTransforms();
            var targetPoint=state.Muzzle.position+Origin.position+state.Muzzle.forward*100;
            Intent(world,vehicle,player,1,false,false,targetPoint);state.Inputs[0].SwitchUntil=Time.time-1;
            state.Gun.Rounds=10;state.Gun.Heat=0;state.Gun.Overheated=false;state.Gun.NextShot=0;
            Intent(world,vehicle,player,1,true,false,targetPoint);Physics.SyncTransforms();Secondary.Update(main,.1f);
            results.Add("INFO MG reason="+state.MGReason+" rounds="+state.Gun.Rounds);
            Check(state.Gun.Rounds==9,"actual native MG firing consumes exactly one loaded round");
            Secondary.Update(main,0);Check(state.Gun.Rounds==9,"same-time update cannot duplicate MG shot");Check(main.LastWeaponActivity==Time.time&&main.LastShot<0,"MG activity does not animate cannon recoil");
            var gunner=Spawn<EntityPlayer>(world,"playerMale",vehicle.position);gunner.MinEventContext.ItemValue=ItemValue.None;vehicle.AttachEntityToSelf(gunner,1);Check(Weapons.Seat(vehicle,gunner.entityId)==1,"native gunner attachment");
            Intent(world,vehicle,player,0,false,false,targetPoint);Check(state.Inputs[0].Mode==1,"driver cannot steal cannon with gunner present");
            Intent(world,vehicle,gunner,1,false,false,targetPoint);Check(state.Inputs[1].Mode==0,"gunner cannot steal driver MG");
            var bird=Spawn<EntityVulture>(world,"animalZombieVulture",vehicle.position+new Vector3(0,45,100));bird.Stats.Health.BaseMax=100000000;bird.Health=100000000;Physics.SyncTransforms();
            vehicle.bag.AddItem(new ItemStack(ItemClass.GetItem(SecondaryRules.Missile,false),2));state.LeftAt=state.RightAt=state.GlobalAt=0;
            Intent(world,vehicle,gunner,2,false,true,bird.position+Vector3.up*.8f);state.Inputs[1].SwitchUntil=Time.time-1;
            Secondary.Update(main,1.5f);results.Add("INFO AA reason="+state.AAReason+" target="+state.Target+" lock="+state.Lock);
            Check(state.Target==bird.entityId&&state.Lock>=1.5f,"native AA acquires live flying enemy");
            Intent(world,vehicle,gunner,2,true,true,bird.position+Vector3.up*.8f);Secondary.Update(main,.1f);
            Check(vehicle.bag.GetItemCount(ItemClass.GetItem(SecondaryRules.Missile,false))==1,"native AA shot consumes one missile");Check(state.LeftAt>Time.time+11.9f&&state.RightAt<=Time.time,"only fired tube begins cooldown");
            Intent(world,vehicle,gunner,0,false,false,bird.position);int hp=bird.Health;for(int i=0;i<100;i++)Secondary.Advance(.01f);
            Check(bird.Health<hp,"native guided missile still hits after switching to cannon");
            bird.SetPosition(vehicle.position+new Vector3(0,-10,100));Physics.SyncTransforms();
            state.LeftAt=state.RightAt=state.GlobalAt=0;
            Intent(world,vehicle,gunner,2,false,true,bird.position+Vector3.up*.8f);state.Inputs[1].SwitchUntil=Time.time-1;
            Secondary.Update(main,1.5f);
            results.Add("INFO low AA reason="+state.AAReason+" pitch="+state.AAP+" lock="+state.Lock);
            Check(state.Target==bird.entityId&&state.AAP<0&&state.AAReason==0,"AA acquires below-horizon flying target with real hull clearance");
            Intent(world,vehicle,gunner,2,true,true,bird.position+Vector3.up*.8f);Secondary.Update(main,.1f);
            Check(vehicle.bag.GetItemCount(ItemClass.GetItem(SecondaryRules.Missile,false))==0,"AA launches at low-altitude target and consumes one missile");
            var flash=Presentation.CreateMGFlash(state.Muzzle);flash.gameObject.SetActive(true);
            AccessTools.Field(typeof(Optics),"model").SetValue(null,main.Model);AccessTools.Method(typeof(Optics),"Visibility").Invoke(null,new object[]{true});
            Check(!flash.GetComponent<Renderer>().forceRenderingOff,"scope hides armor but retains separate muzzle flash renderer");
            CheckFlashPixels(flash);

            var impactPuffs=(System.Collections.IList)AccessTools.Field(typeof(Presentation),"puffs").GetValue(null);
            Check(GameManager.Instance.ExplosionClient(vehicle.position+Vector3.forward*20,Quaternion.identity,0,0,2,0,player.entityId,new List<BlockChangeInfo>())==null,"zero native particle index creates no duplicate explosion prefab");
            var effectAudio=flash.gameObject.AddComponent<AudioSource>();
            foreach(ImpactSurface surface in Enum.GetValues(typeof(ImpactSurface)))foreach(bool ap in new[]{true,false}){
                AccessTools.Method(typeof(Presentation),"ImpactFX").Invoke(null,new object[]{new Presentation.View{ImpactAudio=effectAudio},new NetPackageM1Event{A=vehicle.position+Vector3.forward*20,B=Vector3.back,X=ImpactRules.Encode(surface),Y=ap?1:0,Shot=987}});
                int fires=0,sparks=0;foreach(var puff in impactPuffs){if((bool)puff.GetType().GetField("Fire").GetValue(puff))fires++;if((bool)puff.GetType().GetField("Debris").GetValue(puff))sparks++;}
                Check((ap?fires==2:fires==0||fires==6)&&sparks==ImpactRules.Sparks(ap,surface),"material fire/spark policy "+surface+" AP="+ap);
                if(ap){for(int layer=0;layer<2;layer++){var puff=impactPuffs[layer];var type=puff.GetType();float life=(float)type.GetField("Life").GetValue(puff),hold=(float)type.GetField("Hold").GetValue(puff);Check(Mathf.Abs(life-(layer==0?.45f:.15f))<.00001f&&Mathf.Abs(hold-(layer==0?.12f:.06f))<.00001f,"AP fixed lifetime and brightness hold "+surface+" layer="+layer);}}
                Check(effectAudio.clip!=null,"impact audio assigned "+surface+" AP="+ap);
                if(impactPuffs.Count>0){var puff=impactPuffs[0];var ft=puff.GetType();ft.GetField("Start").SetValue(puff,Time.time-.05f);AccessTools.Method(typeof(Presentation),"UpdatePuffs").Invoke(null,new object[]{null,0f});CheckFlashPixels(((GameObject)ft.GetField("Go").GetValue(puff)).transform,true);}
                foreach(var puff in impactPuffs)puff.GetType().GetField("Start").SetValue(puff,Time.time-10);
                AccessTools.Method(typeof(Presentation),"UpdatePuffs").Invoke(null,new object[]{null,0f});Check(impactPuffs.Count==0,"material effect pool cleanup "+surface+" AP="+ap);
            }
            var originalExplosion=WorldStaticData.prefabExplosions[5];
            try{
                WorldStaticData.prefabExplosions[5]=null;
                AccessTools.Method(typeof(Presentation),"ImpactFX").Invoke(null,new object[]{new Presentation.View{ImpactAudio=effectAudio},new NetPackageM1Event{A=vehicle.position+Vector3.forward*20,B=Vector3.back,Y=0,X=ImpactRules.Encode(ImpactSurface.Organic),Shot=988}});
                Check(impactPuffs.Count==6,"missing native HE prefab still creates six fallback fire layers");
                var cameraObject=new GameObject("M1 final camera facing QA");var camera=cameraObject.AddComponent<Camera>();camera.transform.rotation=Quaternion.Euler(15,95,0);
                AccessTools.Method(typeof(Presentation),"FacePuffs").Invoke(null,new object[]{camera});
                foreach(var puff in impactPuffs){var go=(GameObject)puff.GetType().GetField("Go").GetValue(puff);Check(Quaternion.Angle(go.transform.rotation,camera.transform.rotation)<.01f,"impact billboard follows final camera pose");}
                UnityEngine.Object.Destroy(cameraObject);
                CheckFlashPixels(((GameObject)impactPuffs[0].GetType().GetField("Go").GetValue(impactPuffs[0])).transform,true);
                foreach(var puff in impactPuffs)puff.GetType().GetField("Start").SetValue(puff,Time.time-10);
                AccessTools.Method(typeof(Presentation),"UpdatePuffs").Invoke(null,new object[]{null,0f});
            }finally{WorldStaticData.prefabExplosions[5]=originalExplosion;}
            var materials=new[]{"Mmetal","Mstone","Mdirt","Msand","Msnow","Mwood","Mglass","Mwater","Morganic","Mcloth"};
            var surfaces=new[]{ImpactSurface.Metal,ImpactSurface.Stone,ImpactSurface.Earth,ImpactSurface.Sand,ImpactSurface.Snow,ImpactSurface.Wood,ImpactSurface.Glass,ImpactSurface.Water,ImpactSurface.Organic,ImpactSurface.Cloth};
            for(int mi=0;mi<materials.Length;mi++){var mat=MaterialBlock.materials[materials[mi]];Check(ImpactRules.Classify(mat.SurfaceCategory,mat.DamageCategory,mat.id,mat.IsLiquid)==surfaces[mi],"loaded native material mapping "+materials[mi]);}
            Optics.Clear();UnityEngine.Object.Destroy(flash.gameObject);
            AccessTools.Method(typeof(SecondaryPresentation),"Add").Invoke(null,new object[]{vehicle.entityId,123,vehicle.position,vehicle.position+Vector3.forward*200,false,0f});
            var visualTrails=(System.Collections.IList)AccessTools.Field(typeof(SecondaryPresentation),"trails").GetValue(null);var shortTrail=visualTrails[visualTrails.Count-1];
            shortTrail.GetType().GetField("Start").SetValue(shortTrail,Time.time-.05f);SecondaryPresentation.Update(world);
            var shortLine=(LineRenderer)shortTrail.GetType().GetField("Line").GetValue(shortTrail);
            Check(Vector3.Distance(shortLine.GetPosition(0),shortLine.GetPosition(1))<2.51f&&Vector3.Distance(shortLine.GetPosition(1)+Origin.position,vehicle.position)>40,"MG tracer is a moving short segment rather than a 200m beam");
            SecondaryPresentation.Clear();
            Presentation.Clear();CheckNetworkEffects(world,vehicle);
            int playerHealth=player.Health;Combat.SecondaryHit(player,gunner.entityId,5000000,true,Vector3.forward,player.position);Check(player.Health==playerHealth,"secondary damage refuses player target");
            results.Add("FINISHED failures=0");
        }catch(Exception e){results.Add("FAIL "+e);}
        finally{File.WriteAllLines(report,results);foreach(var line in results)Log.Out("[M1NativeQA] "+line);foreach(var e in entities)if(world.GetEntity(e.entityId)!=null)world.RemoveEntity(e.entityId,EnumRemoveEntityReason.Despawned);Application.Quit();}
    }
}
