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
            var lease=new Weapons.TriggerLease();lease.Accept(123,1,true,0);lease.Stop();lease.Accept(123,1,true,1);Check("old fire cannot renew stopped lease",!lease.Active(1));
            v.vehicleRB.gameObject.SetActive(false);CompleteTrial(world,camera,texture);v.vehicleRB.gameObject.SetActive(true);
            HotfixTrial(world,v,v2,rig);
            RobotAudio.Clear();Boarding.Clear();Gait.Clear();Locomotion.Clear();
        }
        catch(Exception ex){failures++;report.Add("FAIL "+ex);}
        finally{report.Add("COMPLETE failures="+failures);File.WriteAllLines(Path.Combine(output,"report.txt"),report);foreach(var line in report)Log.Out("[MechaMotionQA] "+line);Application.Quit();}
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
        Check("complete 17 textured skinned source chunks",skins.Length==17&&skins.All(s=>s.sharedMaterial.mainTexture!=null));
        float error=0;int triangles=0;
        foreach(var skin in skins){var mesh=new Mesh();skin.BakeMesh(mesh);var a=mesh.vertices;var b=skin.sharedMesh.vertices;for(int i=0;i<a.Length;i++)error=Mathf.Max(error,Vector3.Distance(a[i],b[i]));triangles+=skin.sharedMesh.triangles.Length/3;UnityEngine.Object.DestroyImmediate(mesh);}
        Check("complete bind pose preserved, error="+error,error<.001f);
        var testSkin=skins[0];var poseMesh=new Mesh();rig.Torso.localPosition+=Vector3.down*.15f;testSkin.BakeMesh(poseMesh);
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
        var focus=rig.Mount.position+Vector3.up*1.6f;camera.transform.position=focus+new Vector3(-3,1.1f,5);camera.transform.LookAt(focus);camera.Render();var previous=RenderTexture.active;RenderTexture.active=iconRT;var icon=new Texture2D(256,256,TextureFormat.RGBA32,false);icon.ReadPixels(new Rect(0,0,256,256),0,0);icon.Apply();File.WriteAllBytes(Path.Combine(output,"complete-icon.png"),icon.EncodeToPNG());RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(icon);camera.targetTexture=texture;iconRT.Release();UnityEngine.Object.DestroyImmediate(iconRT);
        v.vehicleRB.gameObject.SetActive(false);
    }
    static bool Ready(ref bool __result){__result=true;return false;}
    static bool RecordEnter(EntityVehicle __instance,EntityAlive _entity){entered++;return false;}
    static bool RecordExit(Entity __instance){exited++;return false;}
    static void HotfixTrial(World world,EntityVehicle v,EntityVehicle other,Model.Rig rig)
    {
        var p=EntityFactory.CreateEntity(EntityClass.FromString("playerMale"),v.position) as EntityPlayer;
        world.SpawnEntityInWorld(p);p.Health=p.GetMaxHealth();
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
    static void PhysicsTrial(EntityVehicle v)
    {
        bool auto=Physics.autoSimulation;var rb=v.vehicleRB;var floor=new GameObject("QA Support");floor.layer=16;floor.transform.position=new Vector3(0,299.5f,20);floor.AddComponent<BoxCollider>().size=new Vector3(50,1,300);
        try{
            Physics.autoSimulation=false;
            var free=new GameObject("Force probe").AddComponent<Rigidbody>();free.mass=8000;free.useGravity=false;free.position=new Vector3(0,350,0);free.AddForce(Vector3.forward*2,ForceMode.Acceleration);Physics.Simulate(.02f);report.Add("FREE acceleration mode velocity="+free.velocity);UnityEngine.Object.DestroyImmediate(free.gameObject);
rb.isKinematic=false;rb.useGravity=true;rb.detectCollisions=true;v.RBActive=true;v.hasDriver=true;v.IsEngineRunning=true;v.isEntityRemote=false;v.movementInput=new MovementInput();v.wheelBrakes=0;rb.position=new Vector3(0,300.5f,0);rb.rotation=Quaternion.identity;rb.velocity=Vector3.zero;rb.angularVelocity=Vector3.zero;
            var wheels=rb.GetComponentsInChildren<WheelCollider>(true);foreach(var c in rb.GetComponentsInChildren<Collider>(true))Physics.IgnoreLayerCollision(16,c.gameObject.layer,false);
            foreach(var wheel in wheels){wheel.gameObject.SetActive(true);wheel.enabled=true;wheel.motorTorque=0;wheel.brakeTorque=0;wheel.steerAngle=0;}
            report.Add("BODY mass="+rb.mass+" constraints="+rb.constraints+" drag="+rb.drag+" wheels="+wheels.Length+" maxLinear="+rb.maxLinearVelocity+" maxAngular="+rb.maxAngularVelocity+" kinematic="+rb.isKinematic);foreach(var wheel in wheels)report.Add("WHEEL friction="+wheel.forwardFriction.stiffness+","+wheel.sidewaysFriction.stiffness+" brake="+wheel.brakeTorque+" damping="+wheel.wheelDampingRate+" body="+wheel.attachedRigidbody.name);
            Physics.SyncTransforms();int contacts=0;float speed=0,stop=0,yaw=0;
            for(int tick=0;tick<650;tick++){
                foreach(var wheel in wheels)if(wheel.GetGroundHit(out var contact))contacts++;
                float target=tick<100?0:tick<350?4:0;float steer=tick>=500?1:0;
                Locomotion.PrepareSupport(wheels,target!=0||steer!=0||rb.velocity.sqrMagnitude>.01f);
                Locomotion.ApplyDrive(rb,rb.rotation*Vector3.forward,target,steer,false,Vector3.up,.02f);Physics.Simulate(.02f);
                if(tick==349)speed=Vector3.ProjectOnPlane(rb.velocity,Vector3.up).magnitude;
                if(tick==499){stop=Vector3.ProjectOnPlane(rb.velocity,Vector3.up).magnitude;yaw=rb.rotation.eulerAngles.y;}
            }
            float rotation=Mathf.Abs(Mathf.DeltaAngle(yaw,rb.rotation.eulerAngles.y));
            foreach(var c in rb.GetComponentsInChildren<Component>(true))if(!(c is Transform)&&!(c is MeshRenderer)&&!(c is MeshFilter))report.Add("COMPONENT "+c.GetType().FullName+" on "+c.name);
            report.Add("BODY final="+rb.position+" velocity="+rb.velocity+" sleeping="+rb.IsSleeping());
            foreach(var collider in rb.GetComponentsInChildren<Collider>(true))if(collider.enabled)report.Add("COLLIDER "+collider.name+" "+collider.GetType().Name+" trigger="+collider.isTrigger+" bounds="+collider.bounds+" layer="+collider.gameObject.layer);
            Check("real WheelCollider support",contacts>0);
            Check("native drive reaches 4m/s, measured="+speed,Mathf.Abs(speed-4)<.3f);
            Check("native braking settles, measured="+stop,stop<.15f);
            Check("native stationary turn, degrees="+rotation,rotation>45);
            rb.position=new Vector3(0,300.18f,0);rb.rotation=Quaternion.identity;rb.velocity=Vector3.zero;rb.angularVelocity=Vector3.zero;
            for(int tick=0;tick<180;tick++){Locomotion.PrepareSupport(wheels,true);Locomotion.ApplyDrive(rb,Vector3.forward,-2,0,false,Vector3.up,.02f);Physics.Simulate(.02f);}
            Check("native reverse reaches -2m/s, measured="+rb.velocity.z,Mathf.Abs(rb.velocity.z+2)<.15f);
            rb.position=new Vector3(0,300.18f,0);rb.rotation=Quaternion.identity;rb.velocity=Vector3.zero;rb.angularVelocity=Vector3.zero;
            for(int tick=0;tick<500;tick++){Locomotion.PrepareSupport(wheels,true);Locomotion.ApplyDrive(rb,Vector3.forward,13.5f,0,true,Vector3.up,.02f);Physics.Simulate(.02f);}
            Check("native forward boost reaches 13.5m/s, measured="+rb.velocity.z,Mathf.Abs(rb.velocity.z-13.5f)<.3f);
            foreach(var wheel in wheels)wheel.enabled=false;rb.useGravity=false;rb.position=new Vector3(0,320,0);rb.velocity=Vector3.zero;
            for(int i=0;i<150;i++){Locomotion.ApplyDrive(rb,Vector3.forward,4,0,false,Vector3.up,.02f);Physics.Simulate(.02f);}
            report.Add("NO WHEELS speed="+rb.velocity);
            report.Add("PHYSICS fixture exercises production force controller on native vehicle/wheels; does not simulate driver UI.");
        }finally{Physics.autoSimulation=auto;rb.isKinematic=true;UnityEngine.Object.DestroyImmediate(floor);}
    }
    static void Capture(Camera camera,RenderTexture rt,Model.Rig rig,string prefix,int frame)
    {
        var focus=rig.Mount.position+Vector3.up*1.6f;camera.transform.position=focus+new Vector3(-4,1.8f,7);camera.transform.LookAt(focus);
        camera.Render();var old=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,prefix+"-"+frame.ToString("D3")+".png"),image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);RenderTexture.active=old;
    }
}
