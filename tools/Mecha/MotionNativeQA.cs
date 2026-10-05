using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using PZAEC.Mecha;

// Compiled ONLY into the isolated QA copy by Start-NativeQA -MotionProbe.
// Real Unity meshes/joints/IK and render pipeline; synthetic flat-plane trajectory.
public sealed class MechaMotionQA : IModApi
{
    static readonly List<string> report=new List<string>();
    static int failures;static string output;
    public void InitMod(Mod mod){if(Environment.GetCommandLineArgs().Contains("-mechaMotionQA"))ModEvents.GameStartDone.RegisterHandler(Run);}
    static void Check(string name,bool ok){report.Add((ok?"PASS ":"FAIL ")+name);if(!ok)failures++;}
    static bool Plane(EntityVehicle v,Vector3 at,ref Vector3 p,ref bool __result){p=new Vector3(at.x,300.02f+Origin.position.y,at.z);__result=true;return false;}
    static bool DisableUpdate(){return false;}
    static void Run(ref ModEvents.SGameStartDoneData data)
    {
        if(GamePrefs.GetString(EnumGamePrefs.GameName)!="MechaQA_Isolated")return;
        output=Path.Combine(GameIO.GetSaveGameDir(),"mecha-motion-qa");Directory.CreateDirectory(output);
        var world=GameManager.Instance.World;
        try
        {
            var h=new Harmony("mecha.motion.qa");h.Patch(AccessTools.Method(typeof(Gait),"Ground"),prefix:new HarmonyMethod(typeof(MechaMotionQA),nameof(Plane)));
            h.Patch(AccessTools.Method(typeof(Weapons),"Update"),prefix:new HarmonyMethod(typeof(MechaMotionQA),nameof(DisableUpdate)));
            AccessTools.Field(typeof(Weapons),"currentWorld").SetValue(null,world);
            var v=EntityFactory.CreateEntity(EntityClass.FromString(Rules.VehicleName),new Vector3(0,300,0)+Origin.position) as EntityVehicle;
            world.SpawnEntityInWorld(v);v.vehicle.SetItemValue(ItemClass.GetItem(Rules.PlaceableItem,false));
            var rb=v.vehicleRB;for(var t=rb.transform;t!=null;t=t.parent)t.gameObject.SetActive(true);rb.isKinematic=true;rb.position=new Vector3(0,300,0);rb.rotation=Quaternion.identity;
            var rig=Model.GetRig(v);Check("rig exists",rig!=null&&rig.FootL!=null&&rig.ElbowR!=null);
            Check("242 split render pieces",rig.Mount.GetComponentsInChildren<MeshRenderer>(true).Length==242);
            Check("measured leg lengths",rig.LegUpper>.6f&&rig.LegUpper<.9f&&rig.LegLower>.6f&&rig.LegLower<.9f);
            float maxError=0;
            for(int side=0;side<2;side++)for(int i=0;i<12;i++)
            {
                rig.ResetPose();var foot=side==0?rig.FootL:rig.FootR;
                var target=foot.position+new Vector3(0,0,(i-6)*.035f);
                Gait.Solve(rig,side,target,Vector3.up);maxError=Mathf.Max(maxError,Vector3.Distance(foot.position,target));
            }
            Check("native IK reachable target error < 0.001m, measured="+maxError,maxError<.001f);
            PhysicsTrial(v);
            rb.isKinematic=true;rb.position=new Vector3(0,300,0);rb.rotation=Quaternion.identity;v.SetPosition(rb.position+Origin.position);rig.ResetPose();
            var cameraGo=new GameObject("QA Camera");var camera=cameraGo.AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.1f,.13f);camera.fieldOfView=38;
            var texture=new RenderTexture(640,480,24);camera.targetTexture=texture;
            var light=new GameObject("QA Light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.7f;light.transform.rotation=Quaternion.Euler(35,-30,0);RenderSettings.ambientLight=new Color(.65f,.65f,.65f);
            var plane=GameObject.CreatePrimitive(PrimitiveType.Plane);plane.transform.position=new Vector3(0,299.98f,20);plane.transform.localScale=new Vector3(6,1,8);plane.GetComponent<Renderer>().sharedMaterial=new Material(Shader.Find("Standard")){color=new Color(.19f,.22f,.25f)};UnityEngine.Object.DestroyImmediate(plane.GetComponent<Collider>());
            Capture(camera,texture,rig,"rest",0);
            float drift=0,penetration=0;int planted=0;var last=new Vector3[2];bool[] had={false,false};
            float travel=0,travelSpeed=0;
            for(int tick=0;tick<360;tick++)
            {
                float time=tick/30f;
                float targetSpeed=time<6?4:time<9?-2:0;
                travelSpeed=Mathf.MoveTowards(travelSpeed,targetSpeed,(Mathf.Abs(targetSpeed)>Mathf.Abs(travelSpeed)?2:4)/30f);travel+=travelSpeed/30f;
                rb.position=new Vector3(0,300,travel);rb.rotation=Quaternion.Euler(0,time<10?0:(time-10)*40,0);
                v.SetPosition(rb.position+Origin.position);Physics.SyncTransforms();
                Gait.Update(world,v,rig,1f/30);
                var walkers=(System.Collections.IDictionary)AccessTools.Field(typeof(Gait),"walkers").GetValue(null);var walker=walkers[v.entityId];
                var legs=(Array)AccessTools.Field(walker.GetType(),"Legs").GetValue(walker);
                for(int side=0;side<2;side++)
                {
                    var leg=legs.GetValue(side);bool swing=(bool)AccessTools.Field(leg.GetType(),"Swing").GetValue(leg);
                    var foot=side==0?rig.FootL:rig.FootR;var pos=foot.position+Origin.position;
                    if(!swing&&had[side]){var plantedTarget=(Vector3)AccessTools.Field(leg.GetType(),"Foot").GetValue(leg);float error=Vector3.ProjectOnPlane(pos-plantedTarget,Vector3.up).magnitude;if(error>drift+.03f)report.Add("DRIFT tick="+tick+" side="+side+" foot="+pos+" target="+plantedTarget+" hip="+(side==0?rig.HipL:rig.HipR).position+" rb="+rb.position);drift=Mathf.Max(drift,error);planted++;}
                    penetration=Mathf.Max(penetration,300+Origin.position.y-pos.y);last[side]=pos;had[side]=!swing;
                }
                if(tick%2==0)Capture(camera,texture,rig,"walk",tick/2);
            }
            Check("flat planted horizontal drift <= .05m, measured="+drift+" samples="+planted,planted>20&&drift<=.05f);
            Check("flat penetration <= .03m, measured="+penetration,penetration<=.03f);
            report.Add("LIMITATION: scripted rig trajectory, not driver/physics/network acceptance; images are isolated Unity renders.");
            // A second entity must have independent motion state.
            var v2=EntityFactory.CreateEntity(EntityClass.FromString(Rules.VehicleName),new Vector3(8,300,0)+Origin.position) as EntityVehicle;world.SpawnEntityInWorld(v2);
            Locomotion.Get(v).HoverOn=true;Check("two vehicles do not share hover",!Locomotion.Get(v2).HoverOn);
            Check("motion new sequence accepted",Locomotion.Receive(v2,123,20,new Vector3(4,0,0)));
            Check("motion duplicate rejected",!Locomotion.Receive(v2,123,20,new Vector3(1,0,0))&&!Locomotion.Get(v2).HoverOn);
            Check("motion stale rejected",!Locomotion.Receive(v2,123,19,new Vector3(1,0,0)));
            v2.vehicleRB.gameObject.SetActive(false);
            v2.SetPosition(new Vector3(80,300,0)+Origin.position);
            Locomotion.Get(v).HoverOn=false;Gait.Clear();rig.ResetPose();
            AccessTools.Method(typeof(Boarding),"Start").Invoke(null,new object[]{v,123,false,true});
            var shows=(System.Collections.IDictionary)AccessTools.Field(typeof(Boarding),"shows").GetValue(null);var show=shows[v.entityId];
            for(int frame=0;frame<80;frame++)
            {
                AccessTools.Field(show.GetType(),"Started").SetValue(show,Time.time-frame/20f);
                Gait.Update(world,v,rig,.05f);Capture(camera,texture,rig,"board",frame);
            }
            Boarding.SkipLocal(v);Check("skip clears boarding lock",!Boarding.Active(v));Check("skip Space cannot charge jump",!Boarding.FilterJump(true));
            var rest=rig.TorsoBasePosition;Check("skip restores torso",Vector3.Distance(rig.Torso.localPosition,rest)<.001f);
            BoardingPoseTrial(world,v,rig,camera,texture);
            var lease=new Weapons.TriggerLease();lease.Accept(123,1,true,0);lease.Stop();lease.Accept(123,1,true,1);Check("old fire cannot renew stopped lease",!lease.Active(1));
            v.vehicleRB.gameObject.SetActive(false);CompleteTrial(world,camera,texture);v.vehicleRB.gameObject.SetActive(true);
            HotfixTrial(world,v,v2,rig);
            ViewTrial(v,rig,camera,texture,"prototype");
            PrototypeViews(world,v,rig,camera,texture);
            LowFrameBeamTrial(world,v,rig,camera,texture);
            SoundLandingTrial(world,v,camera,texture);
            RobotAudio.Clear();Boarding.Clear();Gait.Clear();Locomotion.Clear();
        }
        catch(Exception ex){failures++;report.Add("FAIL "+ex);}
        finally{ArticulationAudit.Save(output);report.AddRange(MechaLandingQA.Results());report.AddRange(MechaAudioQA.Results());failures+=MechaLandingQA.Failures+MechaAudioQA.Failures;new GameObject("Mecha Frame Timing QA").AddComponent<MechaTimingQA>().Begin(report,failures,output);}
    }
    static int entered,exited;
    static void CompleteTrial(World world,Camera camera,RenderTexture texture)
    {
        var v=EntityFactory.CreateEntity(EntityClass.FromString(Rules.CompleteVehicle),new Vector3(8,300,0)+Origin.position) as EntityVehicle;
        world.SpawnEntityInWorld(v);v.vehicle.SetItemValue(ItemClass.GetItem(Rules.CompleteItem,false));
        var rb=v.vehicleRB;for(var t=rb.transform;t!=null;t=t.parent)t.gameObject.SetActive(true);rb.isKinematic=true;rb.position=new Vector3(8,300,0);rb.rotation=Quaternion.identity;v.SetPosition(rb.position+Origin.position);
        var rig=Model.GetRig(v);rig.ResetPose();var skins=rig.Mount.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Check("complete variant identified independently",Weapons.IsMecha(v)&&Rules.Complete(v)&&Rules.AttributeScale(v)==1.5f);
        Check("complete hull 3M, measured="+v.vehicle.GetMaxHealth(),v.vehicle.GetMaxHealth()==3000000);
        int expectedParts=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(Model.Path,"samurai_style_gundam_mecha_rig.json")))["parts"].Count();
        Check("complete semantic textured skin batches match authored manifest (including recessed liners)",skins.Length==expectedParts&&skins.Length<=25&&skins.All(s=>s.sharedMaterial.mainTexture!=null));
        float error=0;int triangles=0;
        foreach(var skin in skins){var mesh=new Mesh();skin.BakeMesh(mesh);var a=mesh.vertices;var b=skin.sharedMesh.vertices;for(int i=0;i<a.Length;i++)error=Mathf.Max(error,Vector3.Distance(a[i],b[i]));triangles+=skin.sharedMesh.triangles.Length/3;UnityEngine.Object.DestroyImmediate(mesh);}
        Check("complete bind pose preserved, error="+error,error<.001f);
        var testSkin=skins.First(x=>x.GetComponent<MechaRenderPart>().Role=="Torso");var poseMesh=new Mesh();rig.Torso.localPosition+=Vector3.down*.15f;testSkin.BakeMesh(poseMesh);
        float poseShift=Vector3.Distance(poseMesh.vertices[0],testSkin.sharedMesh.vertices[0]);
        Check("complete skin follows animated joints, shift="+poseShift,poseShift>.14f&&poseShift<.16f);rig.ResetPose();UnityEngine.Object.DestroyImmediate(poseMesh);
        Check("complete optimized geometry under 250K triangles, measured="+triangles,triangles>150000&&triangles<250000);
        foreach(float amount in new[]{Rules.BeamEntityDamage,Rules.BeamSplashDamage,Rules.MissileDamage}){float total=amount*Rules.AttributeScale(v);int n=Weapons.DamagePackets(total);Check("safe complete damage delivery total="+total,total/n<=60000&&Mathf.Abs(total/n*n-total)<.01f);}
        var zombie=EntityFactory.CreateEntity(EntityClass.FromString("zombieBoe"),new Vector3(30,300,0)+Origin.position) as EntityAlive;
        world.SpawnEntityInWorld(zombie);zombie.Stats.Health.BaseMax=300000;zombie.Health=300000;int before=zombie.Health;
        var source=new DamageSourceEntity(EnumDamageSource.External,EnumDamageTypes.Electrical,-1,Vector3.forward){canHitSpecialBodyParts=false};
        zombie.DamageEntity(source,45000,false,0);zombie.DamageEntity(source,45000,false,0);
        Check("native two-packet damage reaches full 90000, measured="+(before-zombie.Health),before-zombie.Health==90000);
        Capture(camera,texture,rig,"complete-rest",0);
        for(int frame=0;frame<120;frame++){rb.position=new Vector3(8,300,frame/30f*2);v.SetPosition(rb.position+Origin.position);Gait.Update(world,v,rig,1f/30);if(frame%2==0)Capture(camera,texture,rig,"complete-walk",frame/2);}
        Check("complete measured leg segments",rig.LegUpper>.5f&&rig.LegUpper<1&&rig.LegLower>.5f&&rig.LegLower<1);
        rig.ResetPose();rb.position=new Vector3(8,300,0);v.SetPosition(rb.position+Origin.position);
        var iconRT=new RenderTexture(256,256,24,RenderTextureFormat.ARGB32);camera.targetTexture=iconRT;camera.backgroundColor=Color.clear;
        foreach(var plane in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())if(plane.gameObject.name=="Plane")plane.enabled=false;
        var focus=rig.Mount.position+Vector3.up*1.6f;camera.transform.position=focus+new Vector3(-3,1.1f,5);camera.transform.LookAt(focus);camera.Render();var previous=RenderTexture.active;RenderTexture.active=iconRT;var icon=new Texture2D(256,256,TextureFormat.RGBA32,false);icon.ReadPixels(new Rect(0,0,256,256),0,0);icon.Apply();File.WriteAllBytes(Path.Combine(output,"complete-icon.png"),icon.EncodeToPNG());RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(icon);camera.targetTexture=texture;camera.backgroundColor=new Color(.08f,.1f,.13f);iconRT.Release();UnityEngine.Object.DestroyImmediate(iconRT);
        SamuraiTrial(world,v,rig,camera,texture);
        foreach(var result in MeleeNativeQA.Run(world,v,rig,camera,output)){report.Add(result);if(result.StartsWith("FAIL "))failures++;}
        CeremonyTrial(world,v,rig,camera,texture);
        BoardingPoseTrial(world,v,rig,camera,texture);
        PresentationTrial(world,v,rig,camera,texture);
        WeightPresentationTrial(world,v,rig,camera,texture);
        foreach(var result in EquipmentOwnershipQA.Run(world,v,rig,camera,output)){report.Add(result);if(result.StartsWith("FAIL "))failures++;}
        LowFrameBeamTrial(world,v,rig,camera,texture);
        WeaponEffectTrial(world,v,rig,camera,texture);
        FlightTrial(world,v,rig,camera,texture);
        SoundLandingTrial(world,v,camera,texture);
        v.vehicleRB.gameObject.SetActive(false);
    }
    static void SoundLandingTrial(World world,EntityVehicle v,Camera camera,RenderTexture texture)
    {
        MechaLandingQA.Run(world,v);MechaAudioQA.Run(world,v);
        LandingVisualTrial(world,v,camera,texture);
    }
    static void LandingVisualTrial(World world,EntityVehicle v,Camera camera,RenderTexture texture)
    {
        string label=Rules.Complete(v)?"complete":"prototype";MechaFX.Clear();
        var receive=AccessTools.Method(typeof(MechaFX),"LandingContact");
        var array=(Array)AccessTools.Field(typeof(MechaFX),"landings").GetValue(null);
        var rig=Model.GetRig(v);rig.ResetPose();
        try
        {
            receive.Invoke(null,new object[]{world,v.entityId,200000014,v.position,Vector3.up,.8f,Rules.StompRadius});
            string initial=MechaFX.LandingDiagnostics();receive.Invoke(null,new object[]{world,v.entityId,200000014,v.position,Vector3.up,.8f,Rules.StompRadius});
            Check(label+" landing presentation serial duplicate suppressed",initial==MechaFX.LandingDiagnostics());
            var p=array.GetValue(0);var type=p.GetType();var root=(GameObject)AccessTools.Field(type,"Root").GetValue(p);
            Check(label+" landing contains no collider, light or explosion particles",root.GetComponentsInChildren<Collider>(true).Length==0&&root.GetComponentsInChildren<Light>(true).Length==0&&root.GetComponentsInChildren<ParticleSystem>(true).Length==0);
            AccessTools.Field(type,"Age").SetValue(p,.16f);AccessTools.Field(type,"BornFrame").SetValue(p,Time.frameCount-1);MechaFX.Update(0);
            var ring=(LineRenderer)AccessTools.Field(type,"Ring").GetValue(p);var dust=(Mesh)AccessTools.Field(type,"Dust").GetValue(p);
            var c=ring.startColor;Check(label+" landing gray 33-point mechanical wave and 10 dust quads",ring.positionCount==33&&dust.vertexCount==40&&Mathf.Abs(c.r-c.g)<.05f&&Mathf.Abs(c.g-c.b)<.05f);
            camera.transform.position=rig.Mount.position+new Vector3(6,3.2f,7);camera.transform.LookAt(rig.Mount.position+Vector3.up*1.1f);camera.Render();
            var previous=RenderTexture.active;RenderTexture.active=texture;var png=new Texture2D(texture.width,texture.height,TextureFormat.RGB24,false);png.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);png.Apply();File.WriteAllBytes(Path.Combine(output,label+"-mechanical-landing.png"),png.EncodeToPNG());RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(png);
            for(int i=1;i<=12;i++)receive.Invoke(null,new object[]{world,v.entityId,200000014+i,v.position,Vector3.up,.8f,Rules.StompRadius});
            Check(label+" landing presentation bounded at eight waves",MechaFX.LandingDiagnostics().Contains("active=8"));
            foreach(var item in array)if(item!=null){AccessTools.Field(item.GetType(),"Age").SetValue(item,1f);AccessTools.Field(item.GetType(),"BornFrame").SetValue(item,Time.frameCount-1);}
            MechaFX.Update(0);Check(label+" landing waves expire without retained visible effect",MechaFX.LandingDiagnostics().Contains("active=0"));
        }
        finally{MechaFX.Clear();Check(label+" landing effect cleanup releases all batches",array.Cast<object>().All(x=>x==null));}
    }
    static bool exitAvailable=true;
    static void FlightTrial(World world,EntityVehicle v,Model.Rig rig,Camera camera,RenderTexture texture)
    {
        Check("flight independent wing joints under backpack",rig.WingL!=null&&rig.WingR!=null&&rig.WingL.parent==rig.Backpack&&rig.WingR.parent==rig.Backpack);
        var trial=new Locomotion.MoveState();
        Flight.Advance(trial,true,true,true,0,100,.02f);
        Check("flight Q starts deploy with two metre target",trial.FlightMode==Flight.Phase.Takeoff&&trial.HoldY==102);
        Check("flight deployment holds before lift",Flight.VerticalTarget(trial,100,0,float.PositiveInfinity)==0);
        Flight.Advance(trial,true,false,true,0,100,.8f);
        Check("flight deploy then climbs",trial.FlightMode==Flight.Phase.Cruise&&Flight.VerticalTarget(trial,100,0,float.PositiveInfinity)>0);
        Flight.Advance(trial,true,false,false,1,110,.02f);Check("flight Space becomes climb without jump charge",trial.VerticalInput==1&&trial.Charge==0);
        Flight.Advance(trial,true,false,false,0,110,.02f);Check("flight UI/no input retains powered altitude hold",trial.FlightMode==Flight.Phase.Cruise&&trial.VerticalInput==0);
        Flight.Advance(trial,true,true,false,0,110,.02f);Check("flight Q requests controlled landing",trial.FlightMode==Flight.Phase.Landing&&trial.ControlledLanding);
        Flight.Advance(trial,true,true,false,0,110,.02f);Check("flight landing can resume at current altitude",trial.FlightMode==Flight.Phase.Cruise&&trial.HoldY==110);
        Check("flight roof blocks climb and clears altitude windup",Flight.VerticalTarget(trial,110,5,113.35f)<=.001f&&trial.HoldY<=110.001f);
        Check("flight clearance slows landing",Flight.LandingSpeed(10)==-2&&Flight.LandingSpeed(1)==-.8f);
        Flight.Advance(trial,true,false,true,-1,100,.1f);Check("flight brief wheel touch is not a landing",Flight.Active(trial));
        Flight.Advance(trial,true,false,false,-1,100,.1f);Check("flight lost contact resets landing timer",trial.ContactTime==0);
        Flight.Advance(trial,true,false,true,-1,100,.31f);Check("flight C contact finishes without leap",trial.FlightMode==Flight.Phase.Ground&&!trial.JumpWasHeld);
        Flight.Advance(trial,true,true,false,0,110,.02f);Flight.Advance(trial,false,false,false,0,110,.02f);
        Check("flight fuel/engine loss removes active lift and stomp suppression",trial.FlightMode==Flight.Phase.PowerLost&&!Flight.Active(trial)&&!trial.ControlledLanding);
        Flight.Advance(trial,false,false,true,0,100,.02f);Check("flight powerless touch returns folded ground",trial.FlightMode==Flight.Phase.Ground);
        var m=Locomotion.Get(v);int serial=500;int actor=777;
        bool remote=v.isEntityRemote;v.isEntityRemote=true;m.Grounded=false;m.WingBlend=0;m.FlightMode=Flight.Phase.Ground;m.LastPacket=Time.time-2;
        Locomotion.Tick(world);Check("flight remote ordinary jump timeout does not unfold wings",m.FlightMode==Flight.Phase.Ground&&m.WingBlend==0);
        m.FlightMode=Flight.Phase.Cruise;Locomotion.Tick(world);Check("flight remote lost snapshot retains airborne fault pose",m.FlightMode==Flight.Phase.PowerLost);v.isEntityRemote=remote;
        Check("flight network accepts climb state",Locomotion.Receive(v,actor,serial,new Vector3(8,0,1))&&m.FlightMode==Flight.Phase.Cruise);
        Check("flight network duplicate does not change stage",!Locomotion.Receive(v,actor,serial,new Vector3(24,0,-1))&&m.FlightMode==Flight.Phase.Cruise);
        foreach(var bad in new[]{new Vector3(8.5f,0,0),new Vector3(16,0,0),new Vector3(56,0,0),new Vector3(72,0,0),new Vector3(9,0,0),new Vector3(8,0,2),new Vector3(8,0,float.NaN),new Vector3(8,1,0),new Vector3(26,0,-1)})
            Check("flight network rejects illegal flags/scalars "+bad,!Locomotion.Receive(v,actor,++serial,bad));
        var prototype=EntityFactory.CreateEntity(EntityClass.FromString(Rules.VehicleName),v.position+Vector3.right*20) as EntityVehicle;world.SpawnEntityInWorld(prototype);
        Check("flight prototype rejects Complete flight snapshot",!Locomotion.Receive(prototype,actor,1,new Vector3(8,0,0)));prototype.vehicleRB.gameObject.SetActive(false);
        m.FlightMode=Flight.Phase.Cruise;m.WingBlend=1;m.Blend=1;m.Grounded=false;
        Check("flight ground sword/shield unavailable",!Samurai.GroundReady(v));
        var combat=Samurai.Get(v);combat.GuardHeld=combat.SwordHeld=true;Check("flight held sword/shield does not brace cruise",!Samurai.Braced(v));Samurai.Stop(v);
        FlightStepTrial(world,v);FlightPhysics(v);FlightLandingPhysics(v);
        var skins=rig.Mount.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        float maxWing=0,maxOther=0;
        rig.ResetPose();Flight.Pose(v,rig,.033f);
        foreach(var skin in skins){var mesh=new Mesh();skin.BakeMesh(mesh);var baked=mesh.vertices;var original=skin.sharedMesh.vertices;var weights=skin.sharedMesh.boneWeights;
            for(int i=0;i<weights.Length;i++)if(skin.bones[weights[i].boneIndex0]==rig.WingL||skin.bones[weights[i].boneIndex0]==rig.WingR)maxWing=Mathf.Max(maxWing,Vector3.Distance(baked[i],original[i]));
            UnityEngine.Object.DestroyImmediate(mesh);}
        Check("flight deploy really moves weighted wing geometry, measured="+maxWing,maxWing>.4f);
        // Reset the torso: rotating one wing must not pull any non-wing geometry.
        rig.ResetPose();rig.WingL.localRotation=Quaternion.Euler(0,0,-58);
        foreach(var skin in skins){var mesh=new Mesh();skin.BakeMesh(mesh);var baked=mesh.vertices;var original=skin.sharedMesh.vertices;var weights=skin.sharedMesh.boneWeights;
            for(int i=0;i<weights.Length;i++)if(skin.bones[weights[i].boneIndex0]!=rig.WingL&&skin.bones[weights[i].boneIndex1]!=rig.WingL)maxOther=Mathf.Max(maxOther,Vector3.Distance(baked[i],original[i]));
            UnityEngine.Object.DestroyImmediate(mesh);}
        Check("flight wing hinge leaves sword/shield/chest/backpack geometry fixed, measured="+maxOther,maxOther<.001f);
        var phases=new[]{"folded","takeoff","hover","cruise","boost","turn-left","turn-right","landing","power-loss"};
        foreach(var name in phases)for(int frame=0;frame<12;frame++){
            m.FlightMode=name=="folded"?Flight.Phase.Ground:name=="takeoff"?Flight.Phase.Takeoff:name=="landing"?Flight.Phase.Landing:name=="power-loss"?Flight.Phase.PowerLost:Flight.Phase.Cruise;
            m.Grounded=name=="folded";m.WingBlend=name=="folded"?0:name=="takeoff"?frame/11f:1;m.Blend=m.WingBlend;
            m.Boost=name=="boost";m.VisualForward=name=="boost"?20:name=="cruise"||name.StartsWith("turn")?12:0;m.VisualTurn=name=="turn-left"?-45:name=="turn-right"?45:0;m.FlightHeight=name=="landing"?Mathf.Lerp(4,0,frame/11f):10;
            rig.ResetPose();Samurai.Pose(v,rig,.033f,Time.time);Flight.Pose(v,rig,.033f);
            for(int side=0;side<2;side++){
                var foot=side==0?rig.FootL:rig.FootR;var home=rig.Mount.InverseTransformPoint(foot.position);
                float tuck=name=="landing"?Mathf.Clamp01((m.FlightHeight-.5f)/3):name=="folded"?0:1;
                Gait.Solve(rig,side,rig.Mount.TransformPoint(home)+(rig.Mount.up*.32f-rig.Mount.forward*.22f)*tuck,rig.Mount.up);
            }
            Capture(camera,texture,rig,"flight-"+name,frame);
            if(frame==11){Capture(camera,texture,rig,"flight-rear-"+name,frame);Capture(camera,texture,rig,"flight-side-"+name,frame);}
        }
        m.FlightMode=Flight.Phase.Ground;m.WingBlend=m.Blend=0;m.Boost=false;m.Grounded=true;rig.ResetPose();
        report.Add("FLIGHT LIMITATION: production state/controller and native PhysX verified; real human driving and multiplayer observations remain manual acceptance.");
    }
    static void FlightPhysics(EntityVehicle v)
    {
        var rb=v.vehicleRB;bool auto=Physics.autoSimulation;bool gravity=rb.useGravity;var wheels=rb.GetComponentsInChildren<WheelCollider>(true);var enabled=wheels.Select(w=>w.enabled).ToArray();
        try{
            Physics.autoSimulation=false;rb.isKinematic=false;rb.useGravity=true;foreach(var w in wheels)w.enabled=false;
            rb.position=new Vector3(8,320,0);rb.rotation=Quaternion.identity;rb.velocity=rb.angularVelocity=Vector3.zero;
            for(int i=0;i<500;i++){Flight.ApplyControl(rb,0,0,false,Mathf.Clamp((320-rb.position.y)*3,-4,5),.02f);Physics.Simulate(.02f);}
            float drift=Mathf.Abs(rb.position.y-320);Check("flight native ten second hold drift <= .2m, measured="+drift,drift<=.2f);
            // Relocate the physics origin while keeping the absolute altitude target.
            rb.position-=Vector3.up*64;
            for(int i=0;i<100;i++){Flight.ApplyControl(rb,0,0,false,Mathf.Clamp((320-(rb.position.y+64))*3,-4,5),.02f);Physics.Simulate(.02f);}
            Check("flight native held absolute altitude survives 64m origin relocation",Mathf.Abs(rb.position.y+64-320)<.2f);
            rb.position+=Vector3.up*64;
            var roofState=new Locomotion.MoveState{FlightMode=Flight.Phase.Cruise,HoldY=340};
            for(int i=0;i<200;i++){float up=Flight.VerticalTarget(roofState,rb.position.y,5,323.5f);Flight.ApplyControl(rb,0,0,false,up,.02f);Physics.Simulate(.02f);}
            Check("flight native low roof prevents accumulated climb",rb.position.y<=320.2f&&roofState.HoldY<=320.2f);
            foreach(float speed in new[]{12f,20f,-6f}){
                rb.position=new Vector3(8,320,0);rb.rotation=Quaternion.identity;rb.velocity=rb.angularVelocity=Vector3.zero;
                for(int i=0;i<400;i++){Flight.ApplyControl(rb,speed,0,speed==20,0,.02f);Physics.Simulate(.02f);}
                Check("flight native horizontal speed "+speed+", measured="+rb.velocity.z,Mathf.Abs(rb.velocity.z-speed)<.3f);
            }
            for(int i=0;i<160;i++){Flight.ApplyControl(rb,0,0,false,0,.02f);Physics.Simulate(.02f);}Check("flight native release brakes to hold",rb.velocity.magnitude<.15f);
            foreach(float vertical in new[]{5f,-4f}){
                for(int i=0;i<150;i++){Flight.ApplyControl(rb,0,0,false,vertical,.02f);Physics.Simulate(.02f);}
                Check("flight native vertical speed "+vertical+", measured="+rb.velocity.y,Mathf.Abs(rb.velocity.y-vertical)<.15f);
            }
            float yaw=rb.rotation.eulerAngles.y;
            for(int i=0;i<100;i++){Flight.ApplyControl(rb,0,1,false,0,.02f);Physics.Simulate(.02f);}
            Check("flight native yaw turns with upright hull",Mathf.Abs(Mathf.DeltaAngle(yaw,rb.rotation.eulerAngles.y))>45&&Vector3.Dot(rb.rotation*Vector3.up,Vector3.up)>.99f);
            float before=rb.velocity.y;for(int i=0;i<25;i++)Physics.Simulate(.02f);Check("flight native no lift falls under gravity",rb.velocity.y<before-3);
        }finally{rb.isKinematic=true;rb.useGravity=gravity;for(int i=0;i<wheels.Length;i++)wheels[i].enabled=enabled[i];rb.position=new Vector3(8,300,0);rb.rotation=Quaternion.identity;v.SetPosition(rb.position+Origin.position);Physics.autoSimulation=auto;}
    }
    static void FlightStepTrial(World world,EntityVehicle v)
    {
        var slots=AccessTools.Field(typeof(Entity),"attachedEntities");var saved=slots.GetValue(v);bool driver=v.hasDriver,engine=v.IsEngineRunning,kinematic=v.vehicleRB.isKinematic;
        float fuel=v.vehicle.GetFuelLevel();var movement=v.movementInput;
        var pilot=EntityFactory.CreateEntity(EntityClass.FromString("playerMale"),v.position+Vector3.right*30) as EntityPlayer;world.SpawnEntityInWorld(pilot);pilot.Health=pilot.GetMaxHealth();
        var s=Locomotion.Get(v);
        try{
            slots.SetValue(v,new Entity[]{pilot});pilot.AttachedToEntity=v;v.hasDriver=true;v.IsEngineRunning=true;v.vehicleRB.isKinematic=false;v.movementInput=new MovementInput();v.vehicle.SetFuelLevel(100);
            s.FlightActor=-1;s.FlightMode=Flight.Phase.Ground;s.Toggle=true;s.Grounded=false;
            bool on=Flight.Step(v,s,false,true,.02f);
            Check("flight production Step starts airborne hold",on&&s.FlightMode==Flight.Phase.Cruise&&s.HoldY==v.position.y&&!s.HoverOn);
            Check("flight production Step consumes selected cruise fuel",EntityVehicle.VehicleFuelUsageModifier==0?v.vehicle.GetFuelLevel()==100:Mathf.Abs(v.vehicle.GetFuelLevel()-99.985f)<.002f);
            s.Jump=true;s.Descend=false;Flight.Step(v,s,false,true,.02f);Check("flight production Step reads Space as ascent",s.VerticalInput==1&&s.Charge==0);
            Flight.Step(v,s,false,false,.02f);Check("flight production Step expired input brakes and keeps power",s.FlightMode==Flight.Phase.Cruise&&s.VerticalInput==0&&!s.Boost);
            s.Jump=false;s.Toggle=true;Flight.Step(v,s,false,true,.02f);Check("flight production Step Q enters descent",s.FlightMode==Flight.Phase.Landing&&s.VerticalInput==-1);
            s.Toggle=true;Flight.Step(v,s,false,true,.02f);Check("flight production Step Q cancels descent",s.FlightMode==Flight.Phase.Cruise);
            v.IsEngineRunning=false;Flight.Step(v,s,false,false,.02f);Check("flight production Step engine off removes lift",s.FlightMode==Flight.Phase.PowerLost&&!s.ControlledLanding);
            v.IsEngineRunning=true;s.Toggle=true;Flight.Step(v,s,false,true,.02f);
            s.FlightActor=-1;s.Actor=pilot.entityId+1;s.FlightMode=Flight.Phase.Cruise;s.HoldY=0;s.Jump=s.Descend=s.InputReady=true;
            Flight.Step(v,s,false,true,.02f);Check("flight production ownership handoff cannot reuse remote altitude/input",s.FlightMode==Flight.Phase.PowerLost&&!s.InputReady&&!s.Jump&&!s.Descend);
            s.Toggle=true;Flight.Step(v,s,false,true,.02f);
            slots.SetValue(v,new Entity[0]);v.hasDriver=false;Flight.Step(v,s,false,false,.02f);Check("flight production Step leaving seat clears flight and input",s.FlightMode==Flight.Phase.PowerLost&&!s.Jump&&s.FlightActor==-1);
        }finally{slots.SetValue(v,saved);pilot.AttachedToEntity=null;v.hasDriver=driver;v.IsEngineRunning=engine;v.vehicleRB.isKinematic=kinematic;v.movementInput=movement;v.vehicle.SetFuelLevel(fuel);s.FlightMode=Flight.Phase.Ground;s.Toggle=s.Jump=false;s.FlightActor=-1;}
    }
    static void FlightLandingPhysics(EntityVehicle v)
    {
        var rb=v.vehicleRB;bool auto=Physics.autoSimulation,detect=rb.detectCollisions;var wheels=rb.GetComponentsInChildren<WheelCollider>(true);
        var floor=new GameObject("Flight QA landing slope");floor.layer=16;floor.transform.position=new Vector3(8,319.7f,0);floor.AddComponent<BoxCollider>().size=new Vector3(20,.3f,20);
        try{
            Physics.autoSimulation=false;rb.isKinematic=false;rb.useGravity=true;rb.detectCollisions=true;
            foreach(var collider in rb.GetComponentsInChildren<Collider>(true))Physics.IgnoreLayerCollision(16,collider.gameObject.layer,false);
            // Entity.SetPosition queues a native MovePosition. Flush it before setting fixture height.
            Physics.SyncTransforms();Physics.Simulate(.02f);
            foreach(float slope in new[]{0f,12f}){
                floor.transform.rotation=Quaternion.Euler(0,0,slope);GroundSupport.Suspend(v);rb.position=new Vector3(8,323,0);rb.rotation=Quaternion.identity;rb.velocity=rb.angularVelocity=Vector3.zero;
                foreach(var wheel in wheels){wheel.gameObject.SetActive(true);wheel.enabled=false;}Physics.SyncTransforms();
                Check("flight support does not classify three metre air gap as landed on "+slope+" degree slope",!Flight.HullSupported(rb));
                var s=new Locomotion.MoveState{FlightMode=Flight.Phase.Landing,ControlledLanding=true};int contacts=0;
                for(int tick=0;tick<500&&Flight.Active(s);tick++){
                    var support=GroundSupport.Observe(v);bool ground=support.Grounded;if(ground)GroundSupport.Apply(support,.02f);if(ground)contacts++;
                    Flight.Advance(s,true,false,ground,0,rb.position.y,.02f);
                    if(Flight.Active(s)){Locomotion.PrepareSupport(wheels,true);Flight.ApplyControl(rb,0,0,false,Flight.LandingSpeed(rb.position.y-319.85f),.02f);}
                    Physics.Simulate(.02f);v.SetPosition(rb.position+Origin.position);Physics.SyncTransforms();
                }
                report.Add("FLIGHT LAND slope="+slope+" body="+rb.position+" velocity="+rb.velocity+" support="+Flight.HullSupported(rb));
                foreach(var c in rb.GetComponentsInChildren<Collider>(true))if(c.enabled&&!(c is WheelCollider))report.Add("FLIGHT LAND collider="+c.name+" bounds="+c.bounds);
                foreach(var hit in Physics.RaycastAll(rb.position+Vector3.up*3,Vector3.down,6,~0,QueryTriggerInteraction.Ignore))report.Add("FLIGHT LAND ray="+hit.collider.name+" point="+hit.point+" normal="+hit.normal+" own="+(hit.collider.attachedRigidbody==rb));
                Check("flight native controlled landing on "+slope+" degree support, contacts="+contacts,s.FlightMode==Flight.Phase.Ground&&contacts>=14&&s.ControlledLanding);
            }
        }finally{rb.isKinematic=true;rb.detectCollisions=detect;rb.position=new Vector3(8,300,0);rb.rotation=Quaternion.identity;v.SetPosition(rb.position+Origin.position);Physics.autoSimulation=auto;UnityEngine.Object.DestroyImmediate(floor);}
    }
    static bool TestExit(EntityVehicle v,out Vector3 point,ref bool __result){point=v.position+Vector3.forward*1.9f;__result=exitAvailable;return false;}
    static void CeremonyTrial(World world,EntityVehicle v,Model.Rig rig,Camera camera,RenderTexture texture)
    {
        Check("complete original chest has three independent joints",rig.ChestL!=null&&rig.ChestR!=null&&rig.ChestDoor!=null);
        Check("complete entry opens before transfer",Ceremony.Kneel(false,Ceremony.EnterTransfer)==1&&Ceremony.Hatch(false,true,Ceremony.EnterTransfer)>.999f);
        Check("complete exit opens before transfer",Ceremony.Kneel(true,Ceremony.ExitTransfer)==1&&Ceremony.Hatch(true,true,Ceremony.ExitTransfer)>.999f);
        Check("complete entry starts standing and ends standing",Ceremony.Kneel(false,0)==0&&Ceremony.Kneel(false,Ceremony.EnterSeconds)==0);
        Check("complete exit closes and recovers",Ceremony.Hatch(true,true,Ceremony.ExitSeconds)==0&&Ceremony.Kneel(true,Ceremony.ExitSeconds)<.001f);
        var p=EntityFactory.CreateEntity(EntityClass.FromString("playerMale"),v.position) as EntityPlayer;world.SpawnEntityInWorld(p);p.Health=p.GetMaxHealth();
        var h=new Harmony("mecha.ceremony.qa");
        h.Patch(AccessTools.Method(typeof(Weapons),"UIReady"),prefix:new HarmonyMethod(typeof(MechaMotionQA),nameof(Ready)));
        h.Patch(AccessTools.Method(typeof(EntityVehicle),"EnterVehicle"),prefix:new HarmonyMethod(typeof(MechaMotionQA),nameof(RecordEnter)));
        h.Patch(AccessTools.Method(typeof(Entity),"SendDetach"),prefix:new HarmonyMethod(typeof(MechaMotionQA),nameof(RecordExit)));
        var start=AccessTools.Method(typeof(Boarding),"Start");var shows=(System.Collections.IDictionary)AccessTools.Field(typeof(Boarding),"shows").GetValue(null);
        entered=exited=0;
        start.Invoke(null,new object[]{v,p.entityId,false,true});var show=shows[v.entityId];AccessTools.Field(show.GetType(),"Deferred").SetValue(show,true);
        AccessTools.Field(show.GetType(),"Started").SetValue(show,Time.time-Ceremony.EnterTransfer+.1f);Boarding.Update(world);
        Check("complete no native entry before transfer",entered==0);
        AccessTools.Field(show.GetType(),"Started").SetValue(show,Time.time-Ceremony.EnterTransfer-.1f);Boarding.Update(world);Boarding.Update(world);
        Check("complete native entry once after transfer",entered==1);
        Boarding.Clear();
        foreach(bool exit in new[]{false,true}){
            start.Invoke(null,new object[]{v,p.entityId,exit,true});show=shows[v.entityId];
            foreach(float t in new[]{0f,.7f,1.7f,2.55f,3.10f,3.85f,4.2f,4.95f,5.8f,6.6f,7f}){
                if(exit&&t>Ceremony.ExitSeconds)continue;AccessTools.Field(show.GetType(),"Started").SetValue(show,Time.time-t);
                Gait.Update(world,v,rig,.02f);Capture(camera,texture,rig,exit?"samurai-exit":"samurai-entry",(int)(t*100));
            }
            Boarding.Clear();
        }
        rig.ResetPose();RobotPresentation.Update(v,rig,0,1);
        Check("complete chest panels slide apart",rig.ChestL.localPosition.x<rig.RestPos[rig.ChestL].x-.15f&&rig.ChestR.localPosition.x>rig.RestPos[rig.ChestR].x+.15f);
        RobotPresentation.Update(v,rig,0,0);Check("complete chest returns exactly to rest",Vector3.Distance(rig.ChestL.localPosition,rig.RestPos[rig.ChestL])<.00001f&&Quaternion.Angle(rig.ChestDoor.localRotation,rig.RestRot[rig.ChestDoor])<.01f);
        var obstacle=GameObject.CreatePrimitive(PrimitiveType.Cube);obstacle.transform.position=v.position-Origin.position+Vector3.forward*5+Vector3.up;Physics.SyncTransforms();
        Check("exit capsule detects native blocking geometry",!Ceremony.ClearCapsule(v,p,v.position+Vector3.forward*5));UnityEngine.Object.DestroyImmediate(obstacle);
        Check("no floating exit without supporting ground",!Ceremony.FindExit(v,p,out var unused));
        var support=GameObject.CreatePrimitive(PrimitiveType.Cube);support.name="Ceremony exit support";support.layer=16;support.transform.position=v.position-Origin.position+new Vector3(0,-.2f,2);support.transform.localScale=new Vector3(6,.2f,6);Physics.SyncTransforms();
        Check("exit planner finds real supported native terrain",Ceremony.FindExit(v,p,out var safePoint));
        UnityEngine.Object.DestroyImmediate(support);
        h.Patch(AccessTools.Method(typeof(Ceremony),"FindExit"),prefix:new HarmonyMethod(typeof(MechaMotionQA),nameof(TestExit)));
        var slots=AccessTools.Field(typeof(Entity),"attachedEntities");var saved=slots.GetValue(v);slots.SetValue(v,new Entity[]{p});p.AttachedToEntity=v;
        start.Invoke(null,new object[]{v,p.entityId,true,true});show=shows[v.entityId];AccessTools.Field(show.GetType(),"Deferred").SetValue(show,true);
        AccessTools.Field(show.GetType(),"Started").SetValue(show,Time.time-Ceremony.ExitTransfer+.1f);Boarding.Update(world);Check("complete no detach before transfer",exited==0);
        AccessTools.Field(show.GetType(),"Started").SetValue(show,Time.time-Ceremony.ExitTransfer-.1f);Boarding.Update(world);Boarding.Update(world);Check("complete detach once after transfer",exited==1);
        p.AttachedToEntity=null;p.SetPosition(v.position+Vector3.forward*1.9f);
        AccessTools.Field(show.GetType(),"Started").SetValue(show,Time.time-Ceremony.ExitSeconds-.4f);Boarding.Update(world);Check("complete waits kneeling for player to clear door",Boarding.Active(v)&&Boarding.Kneel(v)>.999f&&Boarding.Hatch(v)>.999f);
        p.SetPosition(v.position+Vector3.forward*4);AccessTools.Field(show.GetType(),"Started").SetValue(show,Time.time-Ceremony.ExitSeconds-.4f);Boarding.Update(world);Check("complete finishes after door cleared",!Boarding.Active(v));
        p.AttachedToEntity=v;exitAvailable=false;start.Invoke(null,new object[]{v,p.entityId,true,true});show=shows[v.entityId];AccessTools.Field(show.GetType(),"Deferred").SetValue(show,true);AccessTools.Field(show.GetType(),"Started").SetValue(show,Time.time-Ceremony.ExitTransfer-.1f);Boarding.Update(world);
        Check("unsafe exit cancels without sending detach",exited==1&&!Boarding.Active(v));
        p.AttachedToEntity=null;slots.SetValue(v,saved);Boarding.Clear();rig.ResetPose();h.UnpatchSelf();entered=exited=0;world.RemoveEntity(p.entityId,EnumRemoveEntityReason.Despawned);
        report.Add("CEREMONY LIMITATION: transfer spies and fixture exit planner validate ordering; real player camera, terrain and multiplayer still require client acceptance.");
    }
    static void SamuraiTrial(World world,EntityVehicle v,Model.Rig rig,Camera camera,RenderTexture texture)
    {
        var slots=AccessTools.Field(typeof(Entity),"attachedEntities");var saved=slots.GetValue(v);
        var p=EntityFactory.CreateEntity(EntityClass.FromString("playerMale"),v.position) as EntityPlayer;world.SpawnEntityInWorld(p);p.Health=p.GetMaxHealth();slots.SetValue(v,new Entity[]{p});p.AttachedToEntity=v;
        var m=Locomotion.Get(v);m.HoverOn=m.Boost=false;m.Blend=0;m.Grounded=true;
        var s=Samurai.Get(v);float now=Time.time;
        Samurai.Request(v,p.entityId,1,Samurai.InputIdle,Vector3.forward,v.position,now);
        Samurai.Request(v,p.entityId,2,Samurai.InputSword,Vector3.forward,v.position,now);
        Check("samurai press prepares without immediate hit",s.Charging&&!s.Swing);
        Samurai.Request(v,p.entityId,3,Samurai.InputIdle,Vector3.forward,v.position,now+.1f);
        Check("samurai tap starts normal sword swing",s.Swing&&!s.Heavy);
        Samurai.Request(v,p.entityId,2,Samurai.Cancel,Vector3.zero,Vector3.zero,now);
        Check("samurai stale cancel cannot override newer input",s.Swing);
        Samurai.Request(v,p.entityId,4,Samurai.Cancel,Vector3.zero,Vector3.zero,now);
        Check("samurai UI cancel clears attack and guard",!s.Swing&&!s.Guarding&&!s.Charging);
        Samurai.Request(v,p.entityId,5,Samurai.InputIdle,Vector3.forward,v.position,now);
        Samurai.Request(v,p.entityId,6,Samurai.InputSword,Vector3.forward,v.position,now);
        Samurai.Request(v,p.entityId,7,Samurai.InputIdle,Vector3.forward,v.position,now+.9f);
        Check("samurai server timed hold starts heavy",s.Swing&&s.Heavy);
        Samurai.Stop(v);s.SuppressSword=false;s.Actor=p.entityId;s.InputAt=now;s.GuardHeld=s.Guarding=true;s.Energy=100;
        var front=new DamageSourceEntity(EnumDamageSource.External,EnumDamageTypes.Bashing,-1,-(Weapons.BodyRotation(v)*Vector3.forward));
        var rear=new DamageSourceEntity(EnumDamageSource.External,EnumDamageTypes.Bashing,-1,Weapons.BodyRotation(v)*Vector3.forward);
        Check("shield frontal hit reduces by 70 percent",Mathf.Abs(Samurai.ShieldFactor(v,front,30000,false)-.3f)<.001f);
        float energy=s.Energy;Check("shield rear hit bypasses without draining energy",Samurai.ShieldFactor(v,rear,30000,false)==1&&s.Energy==energy);
        int hp=v.Health;s.Energy=100;v.DamageEntity(front,30000,false,0);Check("native hull pipeline composes shield with armor, measured="+(hp-v.Health),hp-v.Health==1350);
        Check("shield blast mitigation limited to 35 percent",Mathf.Abs(Samurai.ShieldFactor(v,front,30000,true)-.65f)<.001f);
        s.Energy=2;float factor=Samurai.ShieldFactor(v,front,30000,false);Check("shield depletion only grants proportional final protection",factor>.8f&&s.Energy==0&&!s.Guarding&&s.BrokenUntil>now);
        Check("head laser restricted to authored forward arc",Samurai.Arc(Vector3.forward)&&!Samurai.Arc(Vector3.right)&&!Samurai.Arc(new Vector3(0,1,1)));
        var w=Weapons.GetState(v);Samurai.Stop(v);w.TriggerHeld=true;w.NextBeam=0;
        Check("head laser needs 0.6s warmup",!Samurai.LaserReady(w,now)&&Samurai.LaserReady(w,now+.61f));Samurai.LaserFired(v);
        Check("head laser cannot repeat while held",!Samurai.LaserReady(w,now+4));w.TriggerHeld=false;Samurai.LaserReady(w,now+4);
        Check("head laser release rearms trigger",!s.BeamSpent);
        Check("physical sword sweep detects crossed blade",Samurai.SweptContact(Vector3.zero,new Vector3(-1,-1,0),new Vector3(-1,1,0),new Vector3(1,-1,0),new Vector3(1,1,0),.1f));
        Check("physical sword sweep excludes distant target",!Samurai.SweptContact(new Vector3(0,0,2),Vector3.down,Vector3.up,Vector3.down,Vector3.up,.5f));
        s.Guarding=s.GuardHeld=s.Charging=false;s.BrokenUntil=0;s.Energy=100;
        foreach(string pose in new[]{"daily","alert","guard","charge","swing","heavy","boost"})
        {
            rig.ResetPose();s.Alert=pose=="daily"?0:1;s.LastCombat=pose=="daily"?now-20:now;s.Guarding=pose=="guard";s.GuardBlend=s.Guarding?1:0;s.Charging=pose=="charge";s.PressedAt=now-1;s.Swing=pose=="swing"||pose=="heavy";s.Heavy=pose=="heavy";s.Combo=1;s.Started=now-Samurai.Duration(s)*.45f;m.Blend=pose=="boost"?1:0;
            Samurai.Pose(v,rig,.1f,now);Capture(camera,texture,rig,"samurai-"+pose,0);
            if(pose=="daily"||pose=="guard")Capture(camera,texture,rig,"complete-"+pose,0);
            if(pose=="daily")Check("daily sword tip stays above ground",rig.HandR.TransformPoint(Samurai.BladeTip-Samurai.Grip).y>=v.position.y-Origin.position.y+.1f);
        }
        m.Blend=0;s.Guarding=s.GuardHeld=s.Charging=false;s.Actor=p.entityId;s.InputAt=now;
        foreach(float held in new[]{.05f,.35f,.75f,.90f})foreach(int priorCombo in new[]{0,1}){
            Samurai.Stop(v);s.Combo=priorCombo;s.Heavy=false;s.Alert=1;s.LastCombat=now;int sequence=s.Sequence+1;
            Samurai.Request(v,p.entityId,sequence++,Samurai.InputIdle,Vector3.forward,v.position,now);
            Samurai.Request(v,p.entityId,sequence++,Samurai.InputSword,Vector3.forward,v.position,now);
            rig.ResetPose();Samurai.Pose(v,rig,1f/60,now+held);var gripBefore=rig.HandR.position;var elbowBefore=rig.ElbowR.position;var tipBefore=SwordMotion.Tip(rig);var rotationBefore=rig.HandR.rotation;
            Samurai.Request(v,p.entityId,sequence++,Samurai.InputIdle,Vector3.forward,v.position,now+held);rig.ResetPose();Samurai.Pose(v,rig,1f/60,now+held);
            Check("held release uses server timed normal/heavy held="+held+" priorCombo="+priorCombo,s.Swing&&s.Heavy==(held>=Samurai.HeavyCharge));
            float gripJump=Vector3.Distance(gripBefore,rig.HandR.position),elbowJump=Vector3.Distance(elbowBefore,rig.ElbowR.position),tipJump=Vector3.Distance(tipBefore,SwordMotion.Tip(rig)),bladeTurn=Quaternion.Angle(rotationBefore,rig.HandR.rotation);
            Check("charge release joins sword path held="+held+" priorCombo="+priorCombo+" grip="+gripJump+" elbow="+elbowJump+" tip="+tipJump+" roll="+bladeTurn,gripJump<.025f&&elbowJump<.05f&&tipJump<.075f&&bladeTurn<3);
        }
        Samurai.Stop(v);s.Actor=p.entityId;s.InputAt=now;
        var z=EntityFactory.CreateEntity(EntityClass.FromString("zombieBoe"),v.position+Vector3.forward*2) as EntityAlive;world.SpawnEntityInWorld(z);z.Stats.Health.BaseMax=1000000;z.Health=1000000;
        foreach(bool heavy in new[]{false,true})
        {
            Samurai.Start(s,heavy,now-.33f*(heavy?Samurai.HeavyDuration:Samurai.NormalDuration));rig.ResetPose();Samurai.Pose(v,rig,.1f,now);
            var center=(SwordMotion.Root(rig)+SwordMotion.Tip(rig))*.5f+Origin.position;MeleeNativeQA.PlaceTarget(z,center);int before=z.Health;
            s.LastSweep=now-.02f;s.PreviousPosition=v.position;Samurai.Contacts(world,v,rig,now);report.Add("SWORD actual="+(before-z.Health)+" before="+before+" contacts="+s.Hit.Count+" center="+center);Check(heavy?"heavy sword delivers 135000 native damage":"normal sword delivers 90000 native damage",before-z.Health==(heavy?135000:90000));
            int after=z.Health;Samurai.Contacts(world,v,rig,now+.001f);Check("one contact per target per swing heavy="+heavy,z.Health==after);
        }
        Samurai.Stop(v);s.InputAt=Time.time-1;s.GuardHeld=s.Guarding=true;Samurai.Tick(world,.02f);
        Check("expired combat lease clears shield and sword",!s.Guarding&&!s.Swing&&!s.Charging);
        s.LastCombat=now-10;s.Alert=1;rig.ResetPose();Samurai.Pose(v,rig,1.1f,now);Check("quiet combat state returns to daily stance",s.Alert==0);
        float unchanged=s.Energy;Samurai.Request(v,-99,999,Samurai.InputGuard,Vector3.forward,v.position,now);Check("unseated actor cannot raise shield",!s.GuardHeld&&s.Energy==unchanged);
        Samurai.Stop(v);slots.SetValue(v,saved);p.AttachedToEntity=null;rig.ResetPose();world.RemoveEntity(p.entityId,EnumRemoveEntityReason.Despawned);world.RemoveEntity(z.entityId,EnumRemoveEntityReason.Despawned);
    }
    static bool Ready(ref bool __result){__result=true;return false;}
    static void ViewTrial(EntityVehicle v,Model.Rig rig,Camera camera,RenderTexture texture,string name)
    {
        const string preference="PZAEC.Mecha.ThirdPerson";bool hadPreference=PlayerPrefs.HasKey(preference);int savedPreference=PlayerPrefs.GetInt(preference,1);
        Optics.SetView(false);Check(name+" first view preference stored independently",!Optics.ThirdPerson&&PlayerPrefs.GetInt(preference)==0);Optics.SetView(true);Check(name+" third view preference stored independently",Optics.ThirdPerson&&PlayerPrefs.GetInt(preference)==1);if(hadPreference)PlayerPrefs.SetInt(preference,savedPreference);else PlayerPrefs.DeleteKey(preference);PlayerPrefs.Save();
        rig.ResetPose();if(Rules.Complete(v)){Samurai.Stop(v);Samurai.Pose(v,rig,.1f,Time.time);}
        var renderers=rig.Visual.GetComponentsInChildren<Renderer>(true);var initial=renderers.Select(x=>x.forceRenderingOff).ToArray();
        var look=Quaternion.Euler(8,Weapons.BodyRotation(v).eulerAngles.y,0);camera.fieldOfView=65;
        camera.transform.SetPositionAndRotation(Optics.CameraPosition(v,rig,look,true),look);Capture(camera,texture,rig,"view-"+name+"-third",0);
        Optics.FirstPersonVisibility(v);
        var parts=rig.Visual.GetComponentsInChildren<MechaRenderPart>(true);
        Check(name+" FP real arms and weapons remain visible",parts.Where(p=>p.FirstPersonVisible).All(p=>!p.GetComponent<Renderer>().forceRenderingOff)&&parts.Any(p=>p.FirstPersonVisible));
        Check(name+" FP obstructive roles hidden",parts.Where(p=>!p.FirstPersonVisible).All(p=>p.GetComponent<Renderer>().forceRenderingOff));
        camera.fieldOfView=80;camera.transform.SetPositionAndRotation(Optics.CameraPosition(v,rig,look,false),look);Capture(camera,texture,rig,"view-"+name+"-first",0);
        AccessTools.Method(typeof(Optics),"Visibility").Invoke(null,new object[]{false});
        Check(name+" camera-only visibility fully restored",initial.SequenceEqual(renderers.Select(x=>x.forceRenderingOff)));
        var pivot=rig.Torso.position+Vector3.up*.25f;var desired=pivot-Vector3.forward*4.8f;var wall=new GameObject("QA Camera Wall");wall.AddComponent<BoxCollider>().size=new Vector3(4,4,.15f);wall.transform.position=pivot-Vector3.forward*1.5f;Physics.SyncTransforms();
        var clipped=Optics.CollideCamera(v,pivot,desired);Check(name+" .2m camera radius stops before wall distance="+Vector3.Distance(clipped,pivot),Vector3.Distance(clipped,pivot)<1.4f&&Vector3.Distance(clipped,pivot)>.5f);foreach(var hit in Physics.SphereCastAll(pivot,.2f,(desired-pivot).normalized,4.8f,~0,QueryTriggerInteraction.Ignore))report.Add("CAMERA HIT "+name+" "+hit.collider.name+" distance="+hit.distance+" root="+hit.collider.transform.root.name+" ownRB="+hit.collider.transform.IsChildOf(v.vehicleRB.transform));UnityEngine.Object.DestroyImmediate(wall);
        Optics.Clear();camera.fieldOfView=38;
        var previousPosition=camera.transform.position;var previousRotation=camera.transform.rotation;float previousFov=camera.fieldOfView;
        AccessTools.Method(typeof(Optics),"Apply").Invoke(null,new object[]{camera,previousPosition+Vector3.right,Quaternion.Euler(20,30,0),22f});Optics.RestoreCamera();Check(name+" camera position / rotation / FOV fully restored",Vector3.Distance(camera.transform.position,previousPosition)<.0001f&&Quaternion.Angle(camera.transform.rotation,previousRotation)<.01f&&Mathf.Abs(camera.fieldOfView-previousFov)<.001f);
    }
    static void PresentationTrial(World world,EntityVehicle v,Model.Rig rig,Camera camera,RenderTexture texture)
    {
        var state=Samurai.Get(v);var motion=Locomotion.Get(v);float now=Time.time;motion.Grounded=true;motion.Blend=motion.WingBlend=0;motion.FlightMode=Flight.Phase.Ground;
        Check("explicit sword root and measured arm lengths",rig.Sword!=null&&rig.Sword.parent==rig.HandR&&rig.ArmUpper>.15f&&rig.ArmLower>.15f);
        float length=Vector3.Distance(Samurai.BladeRoot,Samurai.BladeTip),maxLengthError=0,maxArmError=0,minGround=100,maxTipStep=0;int collisions=0,samples=0;float shoulder=0,elbow=0,wrist=0;var poseFrames=new List<object>();var bones=rig.Mount.GetComponentInChildren<SkinnedMeshRenderer>().bones;
        foreach(string pose in new[]{"daily","alert","guard","charge","left-cut","right-cut","heavy","boost","flight"})
        {
            int frameCount=Mathf.RoundToInt((pose=="heavy"?Samurai.HeavyDuration:pose=="left-cut"||pose=="right-cut"?Samurai.NormalDuration:1.2f)*60);
            var previousTip=Vector3.zero;var previousElbow=Vector3.zero;float elbowStep=0;int extremeWrist=0;
            for(int frame=0;frame<=frameCount;frame++)
            {
                float t=frame/(float)frameCount;rig.ResetPose();Samurai.Stop(v);state.Alert=pose=="daily"?0:1;state.LastCombat=now;state.Guarding=pose=="guard";state.GuardBlend=state.Guarding?1:0;state.Charging=pose=="charge";state.PressedAt=now-t;
                state.Swing=pose=="left-cut"||pose=="right-cut"||pose=="heavy";state.Heavy=pose=="heavy";state.Combo=pose=="right-cut"?2:1;state.Started=now-t*Samurai.Duration(state);
                motion.Blend=pose=="boost"?t:0;motion.WingBlend=pose=="flight"?t:0;motion.FlightMode=pose=="flight"?Flight.Phase.Cruise:Flight.Phase.Ground;
                Samurai.Pose(v,rig,1f/60,now);Flight.Pose(v,rig,1f/60);SwordMotion.SafePose(v,rig);
                if(frame>0)maxTipStep=Mathf.Max(maxTipStep,Vector3.Distance(previousTip,SwordMotion.Tip(rig)));previousTip=SwordMotion.Tip(rig);
                if(!SwordMotion.SelfClear(rig)){collisions++;if(collisions<8)report.Add("SELF COLLISION "+pose+" frame="+frame);}
                maxLengthError=Mathf.Max(maxLengthError,Mathf.Abs(Vector3.Distance(SwordMotion.Root(rig),SwordMotion.Tip(rig))-length));
                maxArmError=Mathf.Max(maxArmError,Mathf.Abs(Vector3.Distance(rig.ShoulderR.position,rig.ElbowR.position)-rig.ArmUpper),Mathf.Abs(Vector3.Distance(rig.ElbowR.position,rig.HandR.position)-rig.ArmLower));
                minGround=Mathf.Min(minGround,SwordMotion.Root(rig).y-300.02f,SwordMotion.Tip(rig).y-300.02f);
                shoulder=Mathf.Max(shoulder,Quaternion.Angle(rig.RestRot[rig.ShoulderR],rig.ShoulderR.localRotation));elbow=Mathf.Max(elbow,Quaternion.Angle(rig.RestRot[rig.ElbowR],rig.ElbowR.localRotation));wrist=Mathf.Max(wrist,Quaternion.Angle(rig.RestRot[rig.HandR],rig.HandR.localRotation));samples++;
                if(Quaternion.Angle(rig.RestRot[rig.HandR],rig.HandR.localRotation)>=150)extremeWrist++;var elbowLocal=rig.Torso.InverseTransformPoint(rig.ElbowR.position);if(frame>0)elbowStep=Mathf.Max(elbowStep,Vector3.Distance(previousElbow,elbowLocal));previousElbow=elbowLocal;
                poseFrames.Add(new{action=pose,frame=frame,root=Point(rig.Mount.InverseTransformPoint(SwordMotion.Root(rig))),tip=Point(rig.Mount.InverseTransformPoint(SwordMotion.Tip(rig))),ground=300.02f-rig.Mount.position.y,bones=bones.Select(b=>Matrix(rig.Mount.worldToLocalMatrix*b.localToWorldMatrix)).ToArray()});
                if(pose=="left-cut"||pose=="right-cut"||pose=="heavy")
                {
                    CombatFeedback.Blade(v,rig);Capture(camera,texture,rig,"samurai-sequence-"+pose,frame);
                    var look=Quaternion.Euler(8,Weapons.BodyRotation(v).eulerAngles.y,0);camera.fieldOfView=65;camera.transform.SetPositionAndRotation(Optics.CameraPosition(v,rig,look,true),look);Capture(camera,texture,rig,"view-sequence-"+pose+"-third",frame);
                    Optics.FirstPersonVisibility(v);camera.fieldOfView=80;camera.transform.SetPositionAndRotation(Optics.CameraPosition(v,rig,look,false),look);Capture(camera,texture,rig,"view-sequence-"+pose+"-first",frame);AccessTools.Method(typeof(Optics),"Visibility").Invoke(null,new object[]{false});camera.fieldOfView=38;
                }
            }
            report.Add("POSE QUALITY "+pose+" wrist>=150 frames="+extremeWrist+"/"+(frameCount+1)+" elbow max step="+elbowStep);
            Check("pose "+pose+" elbow remains continuous relative to torso at 60Hz, step="+elbowStep,elbowStep<.20f);
            if(pose=="daily"||pose=="alert"||pose=="flight")Check("pose "+pose+" avoids extreme wrist twist",extremeWrist==0);
        }
        Check("all action phases self proxy clearance samples="+samples+" collisions="+collisions,collisions==0);
        Check("continuous sword trajectory at 60 Hz max tip step="+maxTipStep,maxTipStep<.60f);
        Check("rigid sword length error="+maxLengthError,maxLengthError<.001f);Check("IK arm lengths unchanged error="+maxArmError,maxArmError<.001f);
        Check("shoulder/elbow/wrist bounds="+shoulder+","+elbow+","+wrist,shoulder<=115.01f&&elbow<=140.01f&&wrist<=175.01f);
        Check("all action blade ground clearance="+minGround,minGround>=.08f);
        AuxiliaryPresentation(world,v,rig,poseFrames,bones);
        ReleasePresentation(world,v,rig,poseFrames,bones);
        File.WriteAllText(Path.Combine(output,"sword-poses.json"),Newtonsoft.Json.JsonConvert.SerializeObject(poseFrames));
        motion.Blend=motion.WingBlend=0;motion.FlightMode=Flight.Phase.Ground;Samurai.Stop(v);rig.ResetPose();
        ViewTrial(v,rig,camera,texture,"complete");
        var beam=(ExplosionData)AccessTools.Method(typeof(Weapons),"BeamExplosion").Invoke(null,new object[]{v,0});var missile=(ExplosionData)AccessTools.Method(typeof(Weapons),"BuildMissileExplosion").Invoke(null,new object[]{v,0});
        Check("laser splash has no native explosion particle and retains damage/radius",beam.ParticleIndex==0&&beam.EntityRadius==(byte)Rules.BeamSplashRadius&&Mathf.Abs(beam.EntityDamage-Rules.BeamSplashDamage*1.5f/Weapons.DamagePackets(Rules.BeamSplashDamage*1.5f))<.01f);
        Check("missile retains explosion particle and combat values",missile.ParticleIndex==5&&missile.EntityRadius==(byte)Rules.MissileEntityRadius&&Mathf.Abs(missile.EntityDamage-Rules.MissileDamage*1.5f/Weapons.DamagePackets(Rules.MissileDamage*1.5f))<.01f);
        LowFrameSwordTrial(world,v,rig);
        CombatFeedback.Clear();
        for(int i=0;i<22;i++)CombatFeedback.Receive(world,v.entityId,i,CombatFeedback.BeamImpact,v.position+Vector3.up*2,Vector3.back,10,1);
        var result=CombatFeedback.Get(v.entityId);CombatFeedback.Receive(world,v.entityId,21,CombatFeedback.BeamImpact,Vector3.zero,Vector3.up,999,1);Check("duplicate contact cannot replace real result",result.Damage==10);
        CombatFeedback.Receive(world,v.entityId+100,21,CombatFeedback.BeamImpact,v.position,Vector3.up,20,1);Check("feedback remains independent per vehicle",CombatFeedback.Get(v.entityId).Damage==10&&CombatFeedback.Get(v.entityId+100).Damage==20);
        CombatFeedback.Receive(world,v.entityId,21,CombatFeedback.SwordContact,v.position,Vector3.up,0,-1);Check("environment contact is blocked without hit confirmation",result.Damage==0&&result.Blocked);
        CombatFeedback.Receive(world,v.entityId,22,CombatFeedback.SwordContact,v.position,Vector3.up,1,2.5f);Check("heavy feedback retains cut type with mitigated damage",result.Heavy&&result.Damage==1&&!result.Blocked);
        Check("impact pool capped at 16",((Array)AccessTools.Field(typeof(CombatFeedback),"impacts").GetValue(null)).Cast<object>().Count(x=>x!=null)==16);
        CombatFeedback.Update(world);var batch=(GameObject)AccessTools.Field(typeof(CombatFeedback),"impactRoot").GetValue(null);var mesh=(Mesh)AccessTools.Field(typeof(CombatFeedback),"impactMesh").GetValue(null);Check("16 impacts share one bounded drawing batch",batch!=null&&batch.GetComponents<MeshRenderer>().Length==1&&mesh.vertexCount<=16*19*4);Capture(camera,texture,rig,"samurai-energy-contact",0);CombatFeedback.Clear();
        Check("feedback pools cleared on world exit",((System.Collections.IDictionary)AccessTools.Field(typeof(CombatFeedback),"results").GetValue(null)).Count==0);
    }
    static float[] Point(Vector3 p){return new[]{p.x,p.y,p.z};}
    static void PrototypeViews(World world,EntityVehicle v,Model.Rig rig,Camera camera,RenderTexture texture)
    {
        var rb=v.vehicleRB;var saved=rb.position;var m=Locomotion.Get(v);m.Grounded=true;m.HoverOn=m.Boost=false;m.Blend=0;Gait.Clear();MechaFX.Clear();Gait.Update(world,v,rig,1f/60);
        var walkers=(System.Collections.IDictionary)AccessTools.Field(typeof(Gait),"walkers").GetValue(null);var walker=walkers[v.entityId];var forward=Weapons.BodyRotation(v)*Vector3.forward;var look=Quaternion.Euler(8,Weapons.BodyRotation(v).eulerAngles.y,0);
        for(int frame=0;frame<120;frame++){
            rb.position=saved+forward*(frame/60f*2);v.SetPosition(rb.position+Origin.position);AccessTools.Field(walker.GetType(),"RecoilAt").SetValue(walker,Time.time-(frame%60)/60f);Gait.Update(world,v,rig,1f/60);
            if(frame%60==0){var muzzle=Weapons.MuzzleWorld(rig,v);AccessTools.Method(typeof(MechaFX),"BeamTrace").Invoke(null,new object[]{world,v.entityId,9500+frame,muzzle,muzzle+forward*20});}
            camera.fieldOfView=65;camera.transform.SetPositionAndRotation(Optics.CameraPosition(v,rig,look,true),look);Capture(camera,texture,rig,"view-prototype-walk-third",frame);
            Optics.FirstPersonVisibility(v);camera.fieldOfView=80;camera.transform.SetPositionAndRotation(Optics.CameraPosition(v,rig,look,false),look);Capture(camera,texture,rig,"view-prototype-walk-first",frame);AccessTools.Method(typeof(Optics),"Visibility").Invoke(null,new object[]{false});MechaFX.Update(1f/60);
        }
        MechaFX.Clear();Gait.Clear();rb.position=saved;v.SetPosition(saved+Origin.position);rig.ResetPose();camera.fieldOfView=38;
        report.Add("PROTOTYPE VIDEO: production gait / recoil / beam presentation with scripted positions and timestamps; no driver input, hit or ammunition claim.");
    }
    static void AuxiliaryPresentation(World world,EntityVehicle v,Model.Rig rig,List<object> poses,Transform[] bones)
    {
        var m=Locomotion.Get(v);var s=Samurai.Get(v);var rb=v.vehicleRB;var initial=rb.position;var rotation=rb.rotation;float maxStep=0,maxElbowStep=0,minFloor=100;int contacts=0,samples=0;
        foreach(var action in new[]{"entry","exit","jump","landing","slope-left","slope-right","slope-heavy"}){
            Boarding.Clear();Gait.Clear();Samurai.Stop(v);m.Charge=m.Blend=m.WingBlend=0;m.HoverOn=false;m.FlightMode=Flight.Phase.Ground;m.LandingAt=-100;rb.position=initial;rb.rotation=rotation;v.SetPosition(initial+Origin.position);rig.ResetPose();
            bool ceremony=action=="entry"||action=="exit";float duration=ceremony?(action=="exit"?Ceremony.ExitSeconds:Ceremony.EnterSeconds):action=="jump"?1.5f:action=="landing"?.5f:action=="slope-heavy"?Samurai.HeavyDuration:Samurai.NormalDuration;int count=Mathf.RoundToInt(duration*60);Vector3 previous=Vector3.zero,previousElbow=Vector3.zero;
            if(ceremony)AccessTools.Method(typeof(Boarding),"Start").Invoke(null,new object[]{v,123,action=="exit",true});
            for(int frame=0;frame<=count;frame++){
                float t=frame/60f;m.Grounded=true;s.Alert=1;s.LastCombat=Time.time;
                if(ceremony){var shows=(System.Collections.IDictionary)AccessTools.Field(typeof(Boarding),"shows").GetValue(null);var show=shows[v.entityId];AccessTools.Field(show.GetType(),"Started").SetValue(show,Time.time-t);}
                if(action=="jump"){m.Grounded=t<.35f||t>=1.4f;m.Charge=t<.35f?t/.35f:0;rb.position=initial+Vector3.up*(m.Grounded?0:Mathf.Sin((t-.35f)/1.05f*Mathf.PI)*2);v.SetPosition(rb.position+Origin.position);}
                if(action=="landing")m.LandingAt=Time.time-t;
                if(action.StartsWith("slope")){rb.rotation=rotation*Quaternion.Euler(0,0,12);s.Swing=true;s.Heavy=action=="slope-heavy";s.Combo=action=="slope-right"?2:1;s.Started=Time.time-t;}
                Gait.Update(world,v,rig,1f/60);var tip=SwordMotion.Tip(rig);var elbowLocal=rig.Torso.InverseTransformPoint(rig.ElbowR.position);if(frame>0){maxStep=Mathf.Max(maxStep,Vector3.Distance(previous,tip));maxElbowStep=Mathf.Max(maxElbowStep,Vector3.Distance(previousElbow,elbowLocal));}previous=tip;previousElbow=elbowLocal;if(!SwordMotion.SelfClear(rig))contacts++;samples++;
                minFloor=Mathf.Min(minFloor,tip.y-300.02f,SwordMotion.Root(rig).y-300.02f);
                poses.Add(new{action=action,frame=frame,root=Point(rig.Mount.InverseTransformPoint(SwordMotion.Root(rig))),tip=Point(rig.Mount.InverseTransformPoint(tip)),ground=300.02f-rig.Mount.position.y,bones=bones.Select(b=>Matrix(rig.Mount.worldToLocalMatrix*b.localToWorldMatrix)).ToArray()});
            }
        }
        Check("entry / exit / jump / landing / 12deg attacks self clearance samples="+samples+" contacts="+contacts,contacts==0);
        Check("auxiliary blade remains above native support, minimum="+minFloor,minFloor>=.07f);
        Check("entry / exit / jump / landing / slope continuous sword max tip step="+maxStep,maxStep<.60f);
        Check("auxiliary elbow remains continuous relative to torso at 60Hz, step="+maxElbowStep,maxElbowStep<.20f);
        report.Add("AUXILIARY trajectory max step="+maxStep+"; synthetic Gait/ceremony paths, not human navigation acceptance.");
        Boarding.Clear();Gait.Clear();Samurai.Stop(v);m.Charge=m.Blend=m.WingBlend=0;m.Grounded=true;m.LandingAt=-100;rb.position=initial;rb.rotation=rotation;v.SetPosition(initial+Origin.position);rig.ResetPose();
    }
    static Vector3 snapshotA,snapshotB;static float snapshotFlags,snapshotWait;static int snapshotSerial;static bool capturedSnapshot;
    static bool SnapshotSpy(int vehicle,int id,byte kind,Vector3 a,Vector3 b,float value,float c)
    {if(kind==Samurai.Snapshot){snapshotA=a;snapshotB=b;snapshotFlags=value;snapshotWait=c;snapshotSerial=id;capturedSnapshot=true;}return false;}
    static bool RemoteAuthority(ref bool __result){__result=false;return false;}
    static void ReleasePresentation(World world,EntityVehicle v,Model.Rig rig,List<object> poses,Transform[] bones)
    {
        var slots=AccessTools.Field(typeof(Entity),"attachedEntities");var saved=slots.GetValue(v);var pilot=EntityFactory.CreateEntity(EntityClass.FromString("playerMale"),v.position) as EntityPlayer;world.SpawnEntityInWorld(pilot);slots.SetValue(v,new Entity[]{pilot});pilot.AttachedToEntity=v;
        var s=Samurai.Get(v);var m=Locomotion.Get(v);m.Grounded=true;m.HoverOn=m.Boost=false;m.Blend=m.WingBlend=0;m.FlightMode=Flight.Phase.Ground;float now=Time.time;int samples=0,contacts=0;float maxTipStep=0,maxElbowStep=0;
        try{
            foreach(float held in new[]{.05f,.35f,.75f,.90f})foreach(int priorCombo in new[]{0,1}){
                Samurai.Stop(v);s.Combo=priorCombo;s.Heavy=false;s.Alert=1;s.LastCombat=now;int sequence=s.Sequence+1;
                Samurai.Request(v,pilot.entityId,sequence++,Samurai.InputIdle,Vector3.forward,v.position,now);Samurai.Request(v,pilot.entityId,sequence++,Samurai.InputSword,Vector3.forward,v.position,now);
                rig.ResetPose();Samurai.Pose(v,rig,1f/60,now+held);Samurai.Request(v,pilot.entityId,sequence++,Samurai.InputIdle,Vector3.forward,v.position,now+held);
                rig.ResetPose();Samurai.Pose(v,rig,1f/60,now+held);float expectedCharge=s.StartCharge;var expectedGrip=rig.Mount.InverseTransformPoint(rig.HandR.position);var expectedTip=rig.Mount.InverseTransformPoint(SwordMotion.Tip(rig));
                var h=new Harmony("mecha.release.snapshot.qa");capturedSnapshot=false;
                try{
                    h.Patch(AccessTools.Method(typeof(Weapons),"Broadcast"),prefix:new HarmonyMethod(typeof(MechaMotionQA),nameof(SnapshotSpy)));
                    AccessTools.Method(typeof(Samurai),"Broadcast").Invoke(null,new object[]{s,now+held});
                    h.Patch(AccessTools.PropertyGetter(typeof(Weapons),"Server"),prefix:new HarmonyMethod(typeof(MechaMotionQA),nameof(RemoteAuthority)));
                    s.StartCharge=0;Samurai.Receive(v,snapshotSerial,snapshotA,snapshotB,snapshotFlags,snapshotWait);rig.ResetPose();Samurai.Pose(v,rig,1f/60,Time.time);
                    Check("release snapshot preserves authority pose held="+held+" priorCombo="+priorCombo,capturedSnapshot&&Mathf.Abs(s.StartCharge-expectedCharge)<.0001f&&Vector3.Distance(expectedGrip,rig.Mount.InverseTransformPoint(rig.HandR.position))<.001f&&Vector3.Distance(expectedTip,rig.Mount.InverseTransformPoint(SwordMotion.Tip(rig)))<.001f);
                }finally{h.UnpatchSelf();}
                s.Started=now+held;string action="release-"+Mathf.RoundToInt(held*100)+"-combo"+priorCombo;int count=Mathf.RoundToInt(Samurai.Duration(s)*60);Vector3 previousTip=Vector3.zero,previousElbow=Vector3.zero;
                for(int frame=0;frame<=count;frame++){
                    rig.ResetPose();Samurai.Pose(v,rig,1f/60,now+held+frame/60f);var tip=SwordMotion.Tip(rig);var elbow=rig.Torso.InverseTransformPoint(rig.ElbowR.position);if(frame>0){maxTipStep=Mathf.Max(maxTipStep,Vector3.Distance(previousTip,tip));maxElbowStep=Mathf.Max(maxElbowStep,Vector3.Distance(previousElbow,elbow));}previousTip=tip;previousElbow=elbow;if(!SwordMotion.SelfClear(rig))contacts++;samples++;
                    poses.Add(new{action=action,frame=frame,root=Point(rig.Mount.InverseTransformPoint(SwordMotion.Root(rig))),tip=Point(rig.Mount.InverseTransformPoint(tip)),ground=300.02f-rig.Mount.position.y,bones=bones.Select(b=>Matrix(rig.Mount.worldToLocalMatrix*b.localToWorldMatrix)).ToArray()});
                }
            }
            Check("real held release paths self clearance samples="+samples+" contacts="+contacts,contacts==0);Check("real held release continuous sword tip step="+maxTipStep,maxTipStep<.60f);Check("real held release continuous elbow step="+maxElbowStep,maxElbowStep<.20f);
        }finally{Samurai.Stop(v);pilot.AttachedToEntity=null;slots.SetValue(v,saved);rig.ResetPose();world.RemoveEntity(pilot.entityId,EnumRemoveEntityReason.Despawned);}
        report.Add("RELEASE LIMITATION: actual Request / Broadcast / Receive with a captured snapshot and local authority switch; not a two-machine network session.");
    }
    static Color32[] CameraPixels(Camera camera,RenderTexture rt)
    {
        camera.Render();var previous=RenderTexture.active;RenderTexture.active=rt;var t=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);t.Apply();var pixels=t.GetPixels32();UnityEngine.Object.DestroyImmediate(t);RenderTexture.active=previous;return pixels;
    }
    static void LowFrameBeamTrial(World world,EntityVehicle v,Model.Rig rig,Camera camera,RenderTexture texture)
    {
        MechaFX.Clear();var method=AccessTools.Method(typeof(MechaFX),"BeamTrace");var list=(System.Collections.IList)AccessTools.Field(typeof(MechaFX),"tracers").GetValue(null);string variant=Rules.Complete(v)?"complete":"prototype";float life=Rules.Complete(v)?.32f:.22f;
        var savedBackground=camera.backgroundColor;camera.backgroundColor=new Color(.65f,.79f,.90f);
        // The game's render callbacks can replace the clear colour on a QA
        // camera. A real opaque surface verifies visibility on a bright scene.
        var backdrop=GameObject.CreatePrimitive(PrimitiveType.Quad);UnityEngine.Object.DestroyImmediate(backdrop.GetComponent<Collider>());backdrop.name="QA bright laser backdrop";
        var backdropMaterial=new Material(Shader.Find("Sprites/Default")??Shader.Find("Standard")){color=new Color(.65f,.79f,.90f)};backdrop.GetComponent<Renderer>().sharedMaterial=backdropMaterial;
        backdrop.transform.SetParent(camera.transform,false);backdrop.transform.localPosition=new Vector3(0,0,50);backdrop.transform.localRotation=Quaternion.identity;backdrop.transform.localScale=new Vector3(200,200,1);
        foreach(float fps in new[]{5f,8f,10f,15f})foreach(bool firstPerson in new[]{false,true}){
            MechaFX.Clear();var origin=Weapons.MuzzleWorld(rig,v);var forward=Weapons.BodyRotation(v)*Vector3.forward;var look=Quaternion.Euler(8,Weapons.BodyRotation(v).eulerAngles.y,0);camera.fieldOfView=firstPerson?80:65;
            camera.transform.SetPositionAndRotation(Optics.CameraPosition(v,rig,look,!firstPerson),look);if(firstPerson)Optics.FirstPersonVisibility(v);
            var before=CameraPixels(camera,texture);var corner=before[texture.width*texture.height-1];Check(variant+" bright background pixels fps="+fps+" first="+firstPerson+" rgb="+corner.r+","+corner.g+","+corner.b,corner.r>100&&corner.g>100&&corner.b>100);method.Invoke(null,new object[]{world,v.entityId,7200+(firstPerson?1:0),origin,origin+forward*24});MechaFX.Update(1/fps);
            Check(variant+" birth update preserves laser fps="+fps+" first="+firstPerson,list.Count==1);
            var tracer=list.Count>0?list[0]:null;if(tracer==null)continue;var type=tracer.GetType();var line=(LineRenderer)AccessTools.Field(type,"Line").GetValue(tracer);var glow=(LineRenderer)AccessTools.Field(type,"Glow").GetValue(tracer);
            Check(variant+" beam uses supported material and two view aligned lines",line.sharedMaterial!=null&&line.sharedMaterial.shader.isSupported&&glow!=null&&line.alignment==LineAlignment.View&&glow.alignment==LineAlignment.View);
            var after=CameraPixels(camera,texture);int changed=0;for(int i=0;i<before.Length;i++)if(Math.Abs(after[i].r-before[i].r)>15||Math.Abs(after[i].g-before[i].g)>15||Math.Abs(after[i].b-before[i].b)>15)changed++;
            Check(variant+" actual laser rendered pixels fps="+fps+" first="+firstPerson+" changed="+changed,changed>=20);
            Capture(camera,texture,rig,"view-laser-"+(int)fps+"fps-"+variant+(firstPerson?"-first":"-third"),0);
            int presented=(int)AccessTools.Field(type,"PresentedFrames").GetValue(tracer);Check(variant+" line callback records actual camera presentation",presented>=1);
            CameraPixels(camera,texture);Check(variant+" repeat render in one frame counted once",(int)AccessTools.Field(type,"PresentedFrames").GetValue(tracer)==presented);
            AccessTools.Field(type,"BornFrame").SetValue(tracer,Time.frameCount-1);MechaFX.Update(1/fps);Check(variant+" fps="+fps+" survives for second presentation",list.Count==1);
            AccessTools.Field(type,"LastPresentedFrame").SetValue(tracer,-1);CameraPixels(camera,texture);Check(variant+" actual second render callback",(int)AccessTools.Field(type,"PresentedFrames").GetValue(tracer)>=2);
            for(int step=0;step<16&&list.Count>0;step++){MechaFX.Update(1/fps);if(list.Count>0){AccessTools.Field(type,"LastPresentedFrame").SetValue(tracer,-1);CameraPixels(camera,texture);}}
            Check(variant+" laser returns to pool fps="+fps,list.Count==0);
            AccessTools.Method(typeof(Optics),"Visibility").Invoke(null,new object[]{false});
        }
        MechaFX.Clear();method.Invoke(null,new object[]{world,v.entityId,7400,v.position+Vector3.up*1000,v.position+Vector3.up*1000+Vector3.forward*20});var unseen=list[0];AccessTools.Field(unseen.GetType(),"BornFrame").SetValue(unseen,Time.frameCount-1);MechaFX.Update(Mathf.Max(.8f,life*3)+.01f);Check(variant+" off-screen beam lease is bounded",list.Count==0);
        MechaFX.Clear();UnityEngine.Object.DestroyImmediate(backdrop);UnityEngine.Object.DestroyImmediate(backdropMaterial);camera.fieldOfView=38;camera.backgroundColor=savedBackground;
        report.Add("LASER LIMITATION: actual shaders, pixels and render callbacks; 5/8/10/15FPS deltas injected synchronously, new frame tokens simulated between native camera renders.");
    }
    static void WeightPresentationTrial(World world,EntityVehicle v,Model.Rig rig,Camera camera,RenderTexture texture)
    {
        var s=Samurai.Get(v);var m=Locomotion.Get(v);var rb=v.vehicleRB;var initial=rb.position;var rotation=rb.rotation;float now=Time.time;
        Boarding.Clear();Gait.Clear();Samurai.Stop(v);m.Grounded=true;m.HoverOn=m.Boost=false;m.Blend=m.WingBlend=0;m.FlightMode=Flight.Phase.Ground;
        foreach(string cut in new[]{"left","right","heavy"}){
            Vector3 lo=Vector3.one*100,hi=-lo;Quaternion firstShoulder=Quaternion.identity;float shoulderSweep=0,bodySweep=0;
            s.Swing=true;s.Heavy=cut=="heavy";s.Combo=cut=="right"?2:1;s.Alert=1;s.LastCombat=now;
            for(int frame=0;frame<=120;frame++){
                rig.ResetPose();s.Started=now-frame/120f*Samurai.Duration(s);Samurai.Pose(v,rig,1f/60,now);
                var grip=rig.Mount.InverseTransformPoint(rig.HandR.position);lo=Vector3.Min(lo,grip);hi=Vector3.Max(hi,grip);
                if(frame==0)firstShoulder=rig.ShoulderR.rotation;else shoulderSweep=Mathf.Max(shoulderSweep,Quaternion.Angle(firstShoulder,rig.ShoulderR.rotation));
                bodySweep=Mathf.Max(bodySweep,Quaternion.Angle(rig.RestRot[rig.Torso],rig.Torso.localRotation));
            }
            Check("weight "+cut+" real grip travels >=.30m, span="+(hi-lo).magnitude,(hi-lo).magnitude>=.30f);
            Check("weight "+cut+" shoulder participates >=30deg, sweep="+shoulderSweep,shoulderSweep>=30);
            Check("weight "+cut+" torso participates >=12deg, sweep="+bodySweep,bodySweep>=12);
        }
        Check("weight attack window excludes anticipation and recovery",!SwordMotion.DamagePhase(.10f)&&SwordMotion.DamagePhase(.33f)&&!SwordMotion.DamagePhase(.80f));
        Check("weight complete ceremony durations 7 / 6 seconds",Mathf.Abs(Ceremony.EnterSeconds-7)<.01f&&Mathf.Abs(Ceremony.ExitSeconds-6)<.01f);
        Check("weight entry support dwell while hatch still closed",Ceremony.Kneel(false,2.65f)>.99f&&Ceremony.Kneel(false,3.05f)>.99f&&Ceremony.Hatch(false,true,3.05f)<.01f);
        Check("weight exit support dwell before hatch",Ceremony.Kneel(true,2.15f)>.99f&&Ceremony.Kneel(true,2.55f)>.99f&&Ceremony.Hatch(true,true,2.55f)<.01f);
        Check("weight cabin fully open at transfer",Ceremony.Hatch(false,true,Ceremony.EnterTransfer)>.99f&&Ceremony.Hatch(true,true,Ceremony.ExitTransfer)>.99f);
        var start=AccessTools.Method(typeof(Boarding),"Start");var shows=(System.Collections.IDictionary)AccessTools.Field(typeof(Boarding),"shows").GetValue(null);
        foreach(bool exit in new[]{false,true}){
            Gait.Clear();Samurai.Stop(v);start.Invoke(null,new object[]{v,123,exit,true});var show=shows[v.entityId];int frames=Mathf.RoundToInt((exit?Ceremony.ExitSeconds:Ceremony.EnterSeconds)*30);
            for(int frame=0;frame<=frames;frame++){
                AccessTools.Field(show.GetType(),"Started").SetValue(show,Time.time-frame/30f);Gait.Update(world,v,rig,1f/30);
                Capture(camera,texture,rig,exit?"weight-exit":"weight-entry",frame);
            }
            Boarding.Clear();
        }
        rig.ResetPose();Gait.Clear();Samurai.Stop(v);
        var skins=rig.Mount.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(x=>x.GetComponent<MechaRenderPart>().Role=="Backpack"||x.GetComponent<MechaRenderPart>().Role.StartsWith("Wing")).ToArray();
        Check("weight back and wings have independent semantic batches",skins.Any(x=>x.GetComponent<MechaRenderPart>().Role=="Backpack")&&skins.Any(x=>x.GetComponent<MechaRenderPart>().Role=="WingL")&&skins.Any(x=>x.GetComponent<MechaRenderPart>().Role=="WingR"));
        float edgeError=0;int edgeSamples=0;var baked=new Mesh();
        for(int frame=0;frame<=120;frame++){
            rb.position=initial+new Vector3(Mathf.Sin(frame/30f)*1.5f,0,frame/60f);rb.rotation=Quaternion.Euler(0,Mathf.Sin(frame/25f)*70,0);v.SetPosition(rb.position+Origin.position);Gait.Update(world,v,rig,1f/30);
            if(frame%5==0){foreach(var skin in skins){skin.BakeMesh(baked);var points=baked.vertices;var rest=skin.sharedMesh.vertices;var ix=skin.sharedMesh.triangles;
                for(int i=0;i<ix.Length;i+=33){int a=ix[i],b=ix[i+1];edgeError=Mathf.Max(edgeError,Mathf.Abs(Vector3.Distance(points[a],points[b])-Vector3.Distance(rest[a],rest[b])));edgeSamples++;}
            }}
            camera.transform.position=rig.Mount.position+Vector3.up*1.6f+rig.Mount.rotation*new Vector3(0,1,-6);camera.transform.LookAt(rig.Mount.position+Vector3.up*1.6f);
            Capture(camera,texture,rig,"view-weight-turn-rear",frame);
        }
        Check("weight back plates preserve every sampled edge through steering, error="+edgeError+" samples="+edgeSamples,edgeSamples>100&&edgeError<.001f);
        UnityEngine.Object.DestroyImmediate(baked);rb.position=initial;rb.rotation=rotation;v.SetPosition(initial+Origin.position);Gait.Clear();Samurai.Stop(v);rig.ResetPose();
        var voice=AccessTools.Method(typeof(RobotAudio),"Get").Invoke(null,new object[]{v});var audioRoot=(GameObject)AccessTools.Field(voice.GetType(),"Root").GetValue(voice);
        Check("weight no continuous reactor idle source",!audioRoot.GetComponentsInChildren<AudioSource>().Any(x=>x.loop&&x.clip!=null&&x.clip.name.Contains("reactor-idle")));
        var ah=new Harmony("mecha.weight.audio.qa");ah.Patch(AccessTools.PropertyGetter(typeof(RobotAudio),"Audible"),prefix:new HarmonyMethod(typeof(MechaMotionQA),nameof(AudioReady)));
        bool hadDriver=v.hasDriver,hadEngine=v.IsEngineRunning;float fuelModifier=EntityVehicle.VehicleFuelUsageModifier;
        try{
            v.hasDriver=v.IsEngineRunning=true;EntityVehicle.VehicleFuelUsageModifier=0;m.VisualForward=0;m.HoverOn=m.Boost=false;m.FlightMode=Flight.Phase.Ground;rig.ResetPose();
            RobotAudio.Update(v,0,false);RobotAudio.Update(v,0,false);
            Check("weight powered stationary pilot has zero servo and boost target",(float)AccessTools.Field(voice.GetType(),"ServoTarget").GetValue(voice)==0&&(float)AccessTools.Field(voice.GetType(),"BoostTarget").GetValue(voice)==0);
            RobotAudio.Update(v,0,true);Check("weight ceremony dwell is silent",(float)AccessTools.Field(voice.GetType(),"ServoTarget").GetValue(voice)==0);
            rig.ElbowR.localRotation=rig.RestRot[rig.ElbowR]*Quaternion.Euler(90,0,0);RobotAudio.Update(v,0,true);
            Check("weight actual moving elbow requests servo",(float)AccessTools.Field(voice.GetType(),"ServoTarget").GetValue(voice)>0);
            RobotAudio.Update(v,0,true);Check("weight stopped elbow immediately clears servo target",(float)AccessTools.Field(voice.GetType(),"ServoTarget").GetValue(voice)==0);
            for(int i=0;i<30;i++)RobotAudio.Update(v,0,true);var servo=(AudioSource)AccessTools.Field(voice.GetType(),"Servo").GetValue(voice);Check("weight stationary servo fades to zero and stops",servo.volume==0&&!servo.isPlaying);
            m.Boost=true;RobotAudio.Update(v,0,false);Check("weight stationary shift cannot run boost loop",(float)AccessTools.Field(voice.GetType(),"BoostTarget").GetValue(voice)==0);
            m.Boost=false;m.FlightMode=Flight.Phase.Cruise;RobotAudio.Update(v,0,false);Check("weight valid powered flight has thrust sound",(float)AccessTools.Field(voice.GetType(),"BoostTarget").GetValue(voice)>0);
            v.IsEngineRunning=false;RobotAudio.Update(v,0,false);Check("weight power loss clears thrust sound target",(float)AccessTools.Field(voice.GetType(),"BoostTarget").GetValue(voice)==0);
        }finally{v.hasDriver=hadDriver;v.IsEngineRunning=hadEngine;EntityVehicle.VehicleFuelUsageModifier=fuelModifier;m.Boost=false;m.FlightMode=Flight.Phase.Ground;rig.ResetPose();ah.UnpatchSelf();}
        report.Add("WEIGHT LIMITATION: scripted engine joints and frames prove paths, rigidity and dwell; human sound impression and load-bearing feel require client play.");
    }
    static bool AudioReady(ref bool __result){__result=true;return false;}
    static void WeaponEffectTrial(World world,EntityVehicle v,Model.Rig rig,Camera camera,RenderTexture texture)
    {
        CombatFeedback.Clear();MechaFX.Clear();var point=new Vector3(25,301,8)+Origin.position;var origin=point+new Vector3(-1.5f,.5f,-2);
        camera.fieldOfView=50;camera.transform.position=point-Origin.position+new Vector3(-1,1.2f,-4);camera.transform.LookAt(point-Origin.position);
        var beam=AccessTools.Method(typeof(MechaFX),"BeamTrace");beam.Invoke(null,new object[]{world,v.entityId,9001,origin,point});beam.Invoke(null,new object[]{world,v.entityId,9001,origin,point});
        Check("duplicate beam cannot duplicate tracer / fire presentation",((System.Collections.ICollection)AccessTools.Field(typeof(MechaFX),"tracers").GetValue(null)).Count==1);
        CombatFeedback.Receive(world,v.entityId,9001,CombatFeedback.BeamImpact,point,Vector3.back,0,0);CombatFeedback.Update(world);
        var particleFree=GameManager.Instance.ExplosionClient(point,Quaternion.identity,0,0,0f,0f,-1,new List<BlockChangeInfo>());Check("native laser particle zero creates no fireball",particleFree==null);
        Capture(camera,texture,rig,"view-energy-impact",0);
        // The standard missile's actual shipped particle prefab, without damage.
        var explosion=GameManager.Instance.ExplosionClient(point,Quaternion.identity,5,0,0f,0f,-1,new List<BlockChangeInfo>());Check("native missile explosion prefab remains available",explosion!=null);
        MechaFX.Clear();CombatFeedback.Clear();
        if(explosion!=null){var particles=explosion.GetComponentsInChildren<ParticleSystem>(true);for(int frame=0;frame<36;frame++){foreach(var p in particles)if(p.transform.parent==null||p.transform.parent.GetComponent<ParticleSystem>()==null)p.Simulate(frame/30f,true,true,false);Capture(camera,texture,rig,"view-missile-impact",frame);}UnityEngine.Object.DestroyImmediate(explosion);}
        camera.fieldOfView=38;
        report.Add("FX LIMITATION: native energy tracer/contact and missile particle rendering; not a human fired weapon recording.");
    }
    static float[] Matrix(Matrix4x4 m){return new[]{m.m00,m.m01,m.m02,m.m03,m.m10,m.m11,m.m12,m.m13,m.m20,m.m21,m.m22,m.m23};}
    static int packetRequests;
    static bool PacketRequestSpy(){packetRequests++;return false;}
    static void RepairPacketTrial(World world,EntityVehicle v,int actor)
    {
        var h=new Harmony("mecha.repair.packet.qa");h.Patch(AccessTools.Method(typeof(Weapons),"Request"),prefix:new HarmonyMethod(typeof(MechaMotionQA),nameof(PacketRequestSpy)));
        try{
            var sender=new ClientInfo{entityId=actor,bAttachedToEntity=false};packetRequests=0;
            foreach(byte op in new[]{Weapons.Repair,Weapons.RepairStop}){var p=new NetPackagePZAECMechaIntent().Setup(v.entityId,op,Vector3.zero,v.position,100);p.Sender=sender;p.ProcessPackage(world,GameManager.Instance);}
            Check("unseated non-host Repair/RepairStop reach server validation",packetRequests==2);
            var weapon=new NetPackagePZAECMechaIntent().Setup(v.entityId,Weapons.Aim,Vector3.forward,v.position,101);weapon.Sender=sender;weapon.ProcessPackage(world,GameManager.Instance);Check("unseated network weapon intent remains rejected",packetRequests==2);
        }finally{h.UnpatchSelf();}
        report.Add("PACKET LIMITATION: native ClientInfo/package gate with request spy; not two-machine inventory / connection acceptance.");
    }
    static void LowFrameSwordTrial(World world,EntityVehicle v,Model.Rig rig)
    {
        var slots=AccessTools.Field(typeof(Entity),"attachedEntities");var saved=slots.GetValue(v);var pilot=EntityFactory.CreateEntity(EntityClass.FromString("playerMale"),v.position) as EntityPlayer;world.SpawnEntityInWorld(pilot);slots.SetValue(v,new Entity[]{pilot});pilot.AttachedToEntity=v;
        var target=EntityFactory.CreateEntity(EntityClass.FromString("zombieBoe"),v.position+Vector3.forward*2) as EntityAlive;world.SpawnEntityInWorld(target);target.Stats.Health.BaseMax=1000000;
        var s=Samurai.Get(v);float now=Time.time;var m=Locomotion.Get(v);m.Grounded=true;m.HoverOn=m.Boost=false;m.Blend=m.WingBlend=0;m.FlightMode=Flight.Phase.Ground;s.Actor=pilot.entityId;
        foreach(float dt in new[]{1f/15,1f/10,.25f,.4f})
        {
            target.Health=1000000;Samurai.Start(s,false,now);rig.ResetPose();SwordMotion.Pose(v,rig,s,now+Samurai.NormalDuration*.33f);MeleeNativeQA.PlaceTarget(target,(SwordMotion.Root(rig)+SwordMotion.Tip(rig))*.5f+Origin.position);
            for(float time=0;time<Samurai.NormalDuration-.01f;time+=dt)Samurai.Contacts(world,v,rig,now+time);
            Check("native low FPS/catchup sword dt="+dt+" damage="+(1000000-target.Health),1000000-target.Health==90000);
        }
        target.Health=1000000;Samurai.Start(s,false,now);Samurai.Contacts(world,v,rig,now+.401f);Check("interruption longer than .4s cancels without deferred damage",s.Blocked&&target.Health==1000000);
        Samurai.Start(s,false,now);rig.ResetPose();SwordMotion.Pose(v,rig,s,now+Samurai.NormalDuration*.33f);var root=SwordMotion.Root(rig);var tip=SwordMotion.Tip(rig);MeleeNativeQA.PlaceTarget(target,(root+tip)*.5f+Origin.position);
        var wall=new GameObject("QA Sword Obstacle");wall.AddComponent<BoxCollider>().size=new Vector3(.2f,.5f,.5f);wall.transform.position=(root+tip)*.5f;Physics.SyncTransforms();target.Health=1000000;s.LastSweep=now+Samurai.NormalDuration*.33f-.02f;s.PreviousPosition=v.position;
        Samurai.Contacts(world,v,rig,now+Samurai.NormalDuration*.33f);Check("environment obstruction cancels sword before target damage",s.Blocked&&target.Health==1000000);UnityEngine.Object.DestroyImmediate(wall);
        Samurai.Stop(v);pilot.AttachedToEntity=null;slots.SetValue(v,saved);rig.ResetPose();world.RemoveEntity(pilot.entityId,EnumRemoveEntityReason.Despawned);world.RemoveEntity(target.entityId,EnumRemoveEntityReason.Despawned);
    }
    static bool RecordEnter(EntityVehicle __instance,EntityAlive _entity){entered++;return false;}
    static bool RecordExit(Entity __instance){exited++;return false;}
    static void HotfixTrial(World world,EntityVehicle v,EntityVehicle other,Model.Rig rig)
    {
        var p=EntityFactory.CreateEntity(EntityClass.FromString("playerMale"),v.position) as EntityPlayer;
        world.SpawnEntityInWorld(p);p.Health=p.GetMaxHealth();
        RepairPacketTrial(world,v,p.entityId);
        var heat=new Weapons.State();
        for(int shot=0;shot<9&&!heat.Overheated;shot++){if(shot>0)Weapons.CoolBeam(heat,Rules.BeamInterval);Weapons.AddBeamHeat(heat);}
        Check("sustained beam reaches overheat",heat.Overheated&&heat.Heat==100);
        Weapons.CoolBeam(heat,5);Check("overheat does not unlock above 30",heat.Overheated&&heat.Heat>30);
        Weapons.CoolBeam(heat,1);Check("cooldown unlocks at 30 or lower",!heat.Overheated&&heat.Heat<=30);
        var muzzle=new Vector3(1,1,0);var aim=new Vector3(0,2,10);var direction=Weapons.AimFromMuzzle(muzzle,aim,Vector3.forward);
        Check("palm beam converges on crosshair target",Vector3.Distance(muzzle+direction*Vector3.Distance(muzzle,aim),aim)<.0001f);
        Check("missile guidance stays on target body",Vector3.Distance(Weapons.MissileAimPoint(p),p.GetPosition())<2);
        Check("missile lifetime covers lock range with flight margin",Rules.MissileLifetime*Rules.MissileSpeed>Rules.MissileRange+50);
        var slots=AccessTools.Field(typeof(Entity),"attachedEntities");var saved=slots.GetValue(v);
        slots.SetValue(v,new Entity[]{p});p.AttachedToEntity=v;
        Weapons.Request(world,p.entityId,v.entityId,Weapons.SwitchMelee,Vector3.forward,v.position,10001);
        var combat=(Weapons.State)AccessTools.Method(typeof(Weapons),"GetState").Invoke(null,new object[]{v});
        Check("retired melee toggle rejected by server",!combat.MeleeMode);
        int hp=p.Health;int result=p.DamageEntity(DamageSource.fall,100,false,1);
        Check("seated falling damage does not reduce pilot HP",result==0&&p.Health==hp);
        Check("seated collision protected",MechaArmor.ProtectTravel(p,EnumDamageTypes.VehicleInside));
        Check("combat damage not made immune",!MechaArmor.ProtectTravel(p,EnumDamageTypes.Bashing));
        p.AttachedToEntity=null;slots.SetValue(v,saved);
        Check("outside falling damage remains enabled",!MechaArmor.ProtectTravel(p,EnumDamageTypes.Falling));
        var before=rig.Visual.GetComponentsInChildren<Renderer>(true).Select(r=>r.forceRenderingOff).ToArray();
        AccessTools.Field(typeof(Optics),"vehicle").SetValue(null,v);
        AccessTools.Method(typeof(Optics),"Visibility").Invoke(null,new object[]{true});
        Check("cockpit hides entire own model",rig.Visual.GetComponentsInChildren<Renderer>(true).All(r=>r.forceRenderingOff));
        Check("other mecha remains visible",Model.GetRig(other).Visual.GetComponentsInChildren<Renderer>(true).Any(r=>!r.forceRenderingOff));
        AccessTools.Method(typeof(Optics),"Visibility").Invoke(null,new object[]{false});
        Check("cockpit visibility restores",before.SequenceEqual(rig.Visual.GetComponentsInChildren<Renderer>(true).Select(r=>r.forceRenderingOff)));
        Optics.Clear();Boarding.Clear();
        var h=new Harmony("mecha.hotfix.qa");h.Patch(AccessTools.Method(typeof(Weapons),"UIReady"),prefix:new HarmonyMethod(typeof(MechaMotionQA),nameof(Ready)));
        h.Patch(AccessTools.Method(typeof(EntityVehicle),"EnterVehicle"),prefix:new HarmonyMethod(typeof(MechaMotionQA),nameof(RecordEnter)));
        h.Patch(AccessTools.Method(typeof(Entity),"SendDetach"),prefix:new HarmonyMethod(typeof(MechaMotionQA),nameof(RecordExit)));
        var start=AccessTools.Method(typeof(Boarding),"Start");var shows=(System.Collections.IDictionary)AccessTools.Field(typeof(Boarding),"shows").GetValue(null);
        start.Invoke(null,new object[]{v,p.entityId,false,true});var show=shows[v.entityId];
        AccessTools.Field(show.GetType(),"Deferred").SetValue(show,true);
        AccessTools.Field(show.GetType(),"Started").SetValue(show,Time.time-1.2f);Boarding.Update(world);
        Check("no native entry before kneel and hatch",entered==0&&p.AttachedToEntity==null&&Boarding.Kneel(v)>.99f);
        AccessTools.Field(show.GetType(),"Started").SetValue(show,Time.time-1.65f);Boarding.Update(world);Boarding.Update(world);
        Check("native entry requested once after open hatch",entered==1);
        Boarding.Clear();slots.SetValue(v,new Entity[]{p});p.AttachedToEntity=v;
        start.Invoke(null,new object[]{v,p.entityId,true,true});show=shows[v.entityId];AccessTools.Field(show.GetType(),"Deferred").SetValue(show,true);
        AccessTools.Field(show.GetType(),"Started").SetValue(show,Time.time-1.2f);Boarding.Update(world);
        Check("no detach packet before kneeling",exited==0&&p.AttachedToEntity==v&&Boarding.Kneel(v)>.99f);
        AccessTools.Field(show.GetType(),"Started").SetValue(show,Time.time-1.45f);Boarding.Update(world);Boarding.Update(world);
        Check("native detach requested once after opening",exited==1);
        p.AttachedToEntity=null;slots.SetValue(v,saved);Boarding.Clear();MechaArmor.ClearTravelProtection();
        report.Add("HOTFIX LIMITATION: native entry/detach intercepted by recording spies; validates ordering, not player UI or network handshakes.");
    }
    static void FootPhysicsTick(EntityVehicle v,float speed,float steer)
    {
        var rb=v.vehicleRB;var support=GroundSupport.Observe(v);GroundSupport.Walking(support,.02f,true);
        if(!GroundSupport.MotionClear(support,.02f)){GroundSupport.StopHorizontal(support);speed=0;}
        GroundSupport.Apply(support,.02f);Locomotion.ApplyDrive(rb,rb.rotation*Vector3.forward,speed,steer,speed>4,support.Normal,.02f);
        Physics.Simulate(.02f);v.SetPosition(rb.position+Origin.position);Physics.SyncTransforms();
    }
    static void PhysicsTrial(EntityVehicle v)
    {
        bool auto=Physics.autoSimulation;var rb=v.vehicleRB;var floor=new GameObject("QA Support");floor.layer=16;floor.transform.position=new Vector3(0,299.5f,20);floor.AddComponent<BoxCollider>().size=new Vector3(50,1,300);
        try{
            Physics.autoSimulation=false;
            var free=new GameObject("Force probe").AddComponent<Rigidbody>();free.mass=8000;free.useGravity=false;free.position=new Vector3(0,350,0);free.AddForce(Vector3.forward*2,ForceMode.Acceleration);Physics.Simulate(.02f);report.Add("FREE acceleration mode velocity="+free.velocity);UnityEngine.Object.DestroyImmediate(free.gameObject);
rb.isKinematic=false;rb.useGravity=true;rb.detectCollisions=true;v.RBActive=true;v.hasDriver=false;v.IsEngineRunning=false;v.isEntityRemote=false;v.movementInput=new MovementInput();v.wheelBrakes=0;GroundSupport.Suspend(v);rb.position=new Vector3(0,300+GroundSupport.Get(v).Shape.NeutralY+Rules.SoleClearance,0);rb.rotation=Quaternion.identity;rb.velocity=Vector3.zero;rb.angularVelocity=Vector3.zero;
            var wheels=rb.GetComponentsInChildren<WheelCollider>(true);foreach(var c in rb.GetComponentsInChildren<Collider>(true))Physics.IgnoreLayerCollision(16,c.gameObject.layer,false);
            foreach(var wheel in wheels){wheel.gameObject.SetActive(true);wheel.enabled=false;wheel.motorTorque=0;wheel.brakeTorque=0;wheel.steerAngle=0;}
            report.Add("BODY mass="+rb.mass+" constraints="+rb.constraints+" drag="+rb.drag+" wheels="+wheels.Length+" maxLinear="+rb.maxLinearVelocity+" maxAngular="+rb.maxAngularVelocity+" kinematic="+rb.isKinematic);foreach(var wheel in wheels)report.Add("WHEEL friction="+wheel.forwardFriction.stiffness+","+wheel.sidewaysFriction.stiffness+" brake="+wheel.brakeTorque+" damping="+wheel.wheelDampingRate+" body="+(wheel.attachedRigidbody!=null?wheel.attachedRigidbody.name:"disabled"));
            Physics.SyncTransforms();int contacts=0;float speed=0,stop=0,yaw=0,turnTravel=0;
            for(int tick=0;tick<650;tick++){
                if(GroundSupport.IsGrounded(v))contacts++;
                float target=tick<100?0:tick<350?4:0;float steer=tick>=500?1:0;
                Locomotion.PrepareSupport(wheels,target!=0||steer!=0||rb.velocity.sqrMagnitude>.01f);
                float priorYaw=rb.rotation.eulerAngles.y;FootPhysicsTick(v,target,steer);if(tick>=500)turnTravel+=Mathf.Abs(Mathf.DeltaAngle(priorYaw,rb.rotation.eulerAngles.y));
                if(tick==349)speed=Vector3.ProjectOnPlane(rb.velocity,Vector3.up).magnitude;
                if(tick==499){stop=Vector3.ProjectOnPlane(rb.velocity,Vector3.up).magnitude;yaw=rb.rotation.eulerAngles.y;}
            }
            float rotation=turnTravel;
            foreach(var c in rb.GetComponentsInChildren<Component>(true))if(!(c is Transform)&&!(c is MeshRenderer)&&!(c is MeshFilter))report.Add("COMPONENT "+c.GetType().FullName+" on "+c.name);
            report.Add("BODY final="+rb.position+" velocity="+rb.velocity+" sleeping="+rb.IsSleeping());
            foreach(var collider in rb.GetComponentsInChildren<Collider>(true))if(collider.enabled)report.Add("COLLIDER "+collider.name+" "+collider.GetType().Name+" trigger="+collider.isTrigger+" bounds="+collider.bounds+" layer="+collider.gameObject.layer);
            Check("real planted-foot support, hidden wheels disabled",contacts>0&&wheels.All(w=>!w.enabled));
            Check("native drive reaches 4m/s, measured="+speed,Mathf.Abs(speed-4)<.3f);
            Check("native braking settles, measured="+stop,stop<.15f);
            Check("native stationary turn actual accumulated degrees="+rotation,rotation>45);
            GroundSupport.Suspend(v);rb.position=new Vector3(0,300+GroundSupport.Get(v).Shape.NeutralY+Rules.SoleClearance,0);rb.rotation=Quaternion.identity;rb.velocity=Vector3.zero;rb.angularVelocity=Vector3.zero;
            for(int tick=0;tick<180;tick++){Locomotion.PrepareSupport(wheels,true);FootPhysicsTick(v,-2,0);}
            Check("native reverse reaches -2m/s, measured="+rb.velocity.z,Mathf.Abs(rb.velocity.z+2)<.15f);
            GroundSupport.Suspend(v);rb.position=new Vector3(0,300+GroundSupport.Get(v).Shape.NeutralY+Rules.SoleClearance,0);rb.rotation=Quaternion.identity;rb.velocity=Vector3.zero;rb.angularVelocity=Vector3.zero;
            for(int tick=0;tick<500;tick++){Locomotion.PrepareSupport(wheels,true);FootPhysicsTick(v,13.5f,0);}
            Check("native forward boost reaches 13.5m/s, measured="+rb.velocity.z,Mathf.Abs(rb.velocity.z-13.5f)<.3f);
            foreach(var wheel in wheels)wheel.enabled=false;rb.useGravity=false;rb.position=new Vector3(0,320,0);rb.velocity=Vector3.zero;
            for(int i=0;i<150;i++){Locomotion.ApplyDrive(rb,Vector3.forward,4,0,false,Vector3.up,.02f);Physics.Simulate(.02f);}
            report.Add("NO WHEELS speed="+rb.velocity);
            report.Add("PHYSICS fixture exercises production planted support and force controller on native rigidbody; does not simulate driver UI.");
        }finally{GroundSupport.Suspend(v);Physics.autoSimulation=auto;rb.isKinematic=true;UnityEngine.Object.DestroyImmediate(floor);}
    }
    static void BoardingPoseTrial(World world,EntityVehicle v,Model.Rig r,Camera camera,RenderTexture texture)
    {
        bool complete=Rules.Complete(v);string name=complete?"complete":"prototype";
        var m=Locomotion.Get(v);m.Grounded=true;m.HoverOn=m.Boost=false;m.Blend=m.WingBlend=0;m.FlightMode=Flight.Phase.Ground;
        var start=AccessTools.Method(typeof(Boarding),"Start");var shows=(System.Collections.IDictionary)AccessTools.Field(typeof(Boarding),"shows").GetValue(null);
        foreach(bool exit in new[]{false,true}){
            Boarding.Clear();Gait.Clear();r.ResetPose();start.Invoke(null,new object[]{v,123,exit,true});var show=shows[v.entityId];
            float duration=complete?(exit?6:7):(exit?2:4),body=0,elbow=0,head=0,stretch=0;
            var skins=r.Mount.GetComponentsInChildren<SkinnedMeshRenderer>(true);var mesh=new Mesh();
            for(int frame=0;frame<=60;frame++){
                float t=duration*frame/60;AccessTools.Field(show.GetType(),"Started").SetValue(show,Time.time-t);Gait.Update(world,v,r,duration/60);
                body=Mathf.Max(body,Quaternion.Angle(r.Torso.localRotation,r.RestRot[r.Torso]));
                elbow=Mathf.Max(elbow,Quaternion.Angle(r.ElbowL.localRotation,r.RestRot[r.ElbowL]));head=Mathf.Max(head,Quaternion.Angle(r.Head.localRotation,r.RestRot[r.Head]));
                if(frame%10==0)foreach(var skin in skins){skin.BakeMesh(mesh);var points=mesh.vertices;var rest=skin.sharedMesh.vertices;var ix=skin.sharedMesh.triangles;var weights=skin.sharedMesh.boneWeights;
                    for(int j=0;j<ix.Length;j+=3)for(int q=0;q<3;q++){int a=ix[j+q],b=ix[j+(q+1)%3];
                        if(weights[a].weight0>.9999f&&weights[b].weight0>.9999f&&weights[a].boneIndex0==weights[b].boneIndex0)
                            stretch=Mathf.Max(stretch,Mathf.Abs(Vector3.Distance(points[a],points[b])-Vector3.Distance(rest[a],rest[b])));
                    }
                }
                if(frame==30){int i=0;foreach(var view in new[]{new Vector3(0,.6f,6.5f),new Vector3(6.5f,.6f,0),new Vector3(0,.6f,-6.5f)}){
                    var focus=r.Mount.position+Vector3.up*1.2f;camera.transform.position=focus+view;camera.transform.LookAt(focus);Capture(camera,texture,r,"view-boarding-"+name+(exit?"-exit":"-entry"),i++);}
                    var before=r.Torso.localRotation;var support=GroundSupport.Get(v);bool recovery=support.Recovering;support.Recovering=true;Traversal.EquipmentPose(v,r);support.Recovering=recovery;
                    Check(name+" boarding wins over ground recovery "+exit,Quaternion.Angle(before,r.Torso.localRotation)<.01f);
                }
            }
            UnityEngine.Object.DestroyImmediate(mesh);
            Check(name+" boarding torso >=15 degrees "+exit+" measured="+body,body>=15);
            Check(name+" boarding elbow >=17 degrees "+exit+" measured="+elbow,elbow>=17);
            Check(name+" boarding neck counter-motion >=9 degrees "+exit+" measured="+head,head>=9);
            if(complete)Check(name+" boarding rigid armour edge error <.001m "+exit+" measured="+stretch,stretch<.001f);
            Check(name+" boarding upper body restores at end "+exit,Quaternion.Angle(r.Torso.localRotation,r.RestRot[r.Torso])<.1f&&Quaternion.Angle(r.ElbowL.localRotation,r.RestRot[r.ElbowL])<.1f);
            Boarding.Clear();
        }
        r.ResetPose();Gait.Clear();
    }
    static void Capture(Camera camera,RenderTexture rt,Model.Rig rig,string prefix,int frame)
    {
        ArticulationAudit.Sample(rig,prefix,frame);
        var focus=rig.Mount.position+Vector3.up*1.6f;var view=prefix.StartsWith("flight-rear-")?new Vector3(0,1.6f,-7):prefix.StartsWith("flight-side-")?new Vector3(7,1.6f,0):new Vector3(-4,1.8f,7);if(!prefix.StartsWith("view-")){camera.transform.position=focus+(prefix.StartsWith("flight-")?rig.Mount.rotation*view:view);camera.transform.LookAt(focus);}
        var flat=prefix.StartsWith("samurai-")||prefix.StartsWith("view-")?new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Standard")):null;
        var baked=new List<GameObject>();var skins=rig.Mount.GetComponentsInChildren<SkinnedMeshRenderer>();
        foreach(var skin in skins){var mesh=new Mesh();skin.BakeMesh(mesh);var go=new GameObject("QA baked pose");go.transform.SetPositionAndRotation(skin.transform.position,skin.transform.rotation);go.transform.localScale=skin.transform.lossyScale;go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();if(flat!=null){var material=new Material(flat);material.mainTexture=skin.sharedMaterial.mainTexture;renderer.sharedMaterial=material;}else renderer.sharedMaterial=skin.sharedMaterial;renderer.forceRenderingOff=skin.forceRenderingOff;skin.enabled=false;baked.Add(go);}
        camera.Render();
        foreach(var skin in skins)skin.enabled=true;foreach(var go in baked){if(flat!=null)UnityEngine.Object.DestroyImmediate(go.GetComponent<MeshRenderer>().sharedMaterial);UnityEngine.Object.DestroyImmediate(go.GetComponent<MeshFilter>().sharedMesh);UnityEngine.Object.DestroyImmediate(go);}if(flat!=null)UnityEngine.Object.DestroyImmediate(flat);var old=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,prefix+"-"+frame.ToString("D3")+".png"),image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);RenderTexture.active=old;
    }
}
