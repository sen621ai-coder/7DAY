using System;using System.IO;using System.Linq;using System.Collections.Generic;using UnityEngine;using HarmonyLib;using PZAEC.Mecha;
public sealed class SharedFlightNativeQA:IModApi {
 static List<string> report=new List<string>();static int failures;
 public void InitMod(Mod mod){if(Environment.GetCommandLineArgs().Contains("-mechaMotionQA"))ModEvents.GameStartDone.RegisterHandler(Run);}
 static void Check(string name,bool ok){report.Add((ok?"PASS ":"FAIL ")+name);if(!ok)failures++;}
 static void Run(ref ModEvents.SGameStartDoneData data){try{
 var world=GameManager.Instance.World;AccessTools.Field(typeof(Weapons),"currentWorld").SetValue(null,world);
 foreach(bool complete in new[]{false,true}){
 var v=EntityFactory.CreateEntity(EntityClass.FromString(complete?Rules.CompleteVehicle:Rules.VehicleName),new Vector3(8,320,0)+Origin.position) as EntityVehicle;world.SpawnEntityInWorld(v);v.vehicle.SetItemValue(ItemClass.GetItem(complete?Rules.CompleteItem:Rules.PlaceableItem,false));
 for(var t=v.vehicleRB.transform;t!=null;t=t.parent)t.gameObject.SetActive(true);v.vehicleRB.isKinematic=true;report.Add("CHASSIS "+Rules.DisplayName(v));
 var takeoff=new Locomotion.MoveState{Charge=.8f,JumpWasHeld=true};Flight.Advance(takeoff,true,true,true,0,400,.02f);Check("Q takeoff resets jump and sets height",takeoff.FlightMode==Flight.Phase.Takeoff&&takeoff.HoldY==402&&takeoff.Charge==0&&!takeoff.JumpWasHeld);Flight.Advance(takeoff,true,false,true,0,400,.8f);Check("takeoff reaches cruise",takeoff.FlightMode==Flight.Phase.Cruise);Check("takeoff status matches chassis",Flight.Status(new Locomotion.MoveState{FlightMode=Flight.Phase.Takeoff},complete)==(complete?"起飞展翼":"起飞"));
 FlightStepTrial(world,v);FlightPhysics(v);FlightLandingPhysics(v);
 var s=Locomotion.Get(v);v.isEntityRemote=true;int seq=1000;
 foreach(var state in new[]{new Vector3(40,0,0),new Vector3(8,0,1),new Vector3(10,0,0),new Vector3(24,0,-1),new Vector3(64,0,0),new Vector3(128,0,0),new Vector3(258,0,0),new Vector3(384,0,0),new Vector3(4,0,0)}){Check("network roundtrip "+state,Locomotion.Receive(v,777,++seq,state)&&Locomotion.Snapshot(s)==state);}
 Check("duplicate rejected",!Locomotion.Receive(v,777,seq,new Vector3(8,0,0)));Check("stale rejected",!Locomotion.Receive(v,777,seq-1,new Vector3(8,0,0)));
 foreach(var bad in new[]{new Vector3(136,0,0),new Vector3(132,0,0),new Vector3(130,0,0),new Vector3(8.5f,0,0),new Vector3(16,0,0),new Vector3(56,0,0),new Vector3(72,0,0),new Vector3(9,0,0),new Vector3(8,0,2),new Vector3(8,0,float.NaN),new Vector3(8,1,0),new Vector3(26,0,-1)})Check("illegal state rejected "+bad,!Locomotion.Receive(v,777,++seq,bad));
 s.Grounded=false;s.FlightMode=Flight.Phase.Cruise;s.LastPacket=Time.time-2;Locomotion.Tick(world);Check("remote timeout loses power",s.FlightMode==Flight.Phase.PowerLost&&!s.Boost);
 s.FlightMode=Flight.Phase.Ground;s.SkimPhase=Skim.Phase.Cruise;s.LastPacket=Time.time-2;Locomotion.Tick(world);Check("remote skim timeout clears drive",s.SkimPhase==Skim.Phase.Off&&!s.Boost);
 v.isEntityRemote=false;VisualTrial(world,v);AudioTrial(v);v.vehicleRB.gameObject.SetActive(false);v.transform.gameObject.SetActive(false);world.RemoveEntity(v.entityId,EnumRemoveEntityReason.Despawned);GroundSupport.Forget(v);
 }
 }catch(Exception e){Check("exception "+e,false);}File.WriteAllLines(Path.Combine(GameIO.GetSaveGameDir(),"shared-flight-qa.txt"),report);foreach(var line in report)Log.Out("[SharedFlightQA] "+line);Log.Out("[MechaMotionQA] COMPLETE failures="+failures);
 }
    static bool Audible(ref bool __result){__result=true;return false;}
    static void AudioTrial(EntityVehicle v){
        var h=new Harmony("shared.flight.audio.qa");h.Patch(AccessTools.PropertyGetter(typeof(RobotAudio),"Audible"),prefix:new HarmonyMethod(typeof(SharedFlightNativeQA),nameof(Audible)));
        try{var s=Locomotion.Get(v);v.hasDriver=v.IsEngineRunning=true;v.vehicle.SetFuelLevel(100);
        foreach(bool skim in new[]{false,true}){
            s.SkimPhase=skim?Skim.Phase.Lifting:Skim.Phase.Off;s.FlightMode=skim?Flight.Phase.Ground:Flight.Phase.Cruise;s.Grounded=false;s.Boost=false;
            int count=RobotAudio.PlayedCueCount;RobotAudio.ContactEvent(v,"step-left",RobotAudio.NextPresentationSerial(),v.position);Check("air/skim footstep suppressed "+skim,RobotAudio.PlayedCueCount==count);
            RobotAudio.ContactEvent(v,"laser-impact",RobotAudio.NextPresentationSerial(),v.position);Check("air/skim laser impact remains audible "+skim,RobotAudio.PlayedCueCount==count+1);
            RobotAudio.Update(v,0,false);var voice=AccessTools.Method(typeof(RobotAudio),"Get").Invoke(null,new object[]{v});Check("powered flight/lifting thrust audio "+skim,(float)AccessTools.Field(voice.GetType(),"BoostTarget").GetValue(voice)>0);
        }
        s.SkimPhase=Skim.Phase.Off;s.FlightMode=Flight.Phase.Landing;s.Grounded=true;int before=RobotAudio.PlayedCueCount;RobotAudio.LandCue(v,v.position,.5f,RobotAudio.NextPresentationSerial());Check("controlled touchdown retains landing cue",RobotAudio.PlayedCueCount==before+1);
        }finally{h.UnpatchSelf();RobotAudio.StopChannels(v);}
    }
    static void VisualTrial(World world,EntityVehicle v)
    {
        var r=Model.GetRig(v);var m=Locomotion.Get(v);var rb=v.vehicleRB;rb.isKinematic=true;rb.position=new Vector3(0,400,0);rb.rotation=Quaternion.identity;v.SetPosition(rb.position+Origin.position);Gait.Forget(v);GroundSupport.Forget(v);Boarding.Clear();
        v.hasDriver=v.IsEngineRunning=true;v.vehicle.SetFuelLevel(100);
        var folder=Path.Combine(GameIO.GetSaveGameDir(),"shared-flight-review");Directory.CreateDirectory(folder);
        var camera=new GameObject("Shared flight camera").AddComponent<Camera>();var rt=new RenderTexture(768,768,24);camera.targetTexture=rt;camera.cullingMask=1<<30;camera.fieldOfView=40;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.15f,.19f);
        var light=new GameObject("Shared flight key").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.4f;light.transform.rotation=Quaternion.Euler(40,-35,0);light.cullingMask=1<<30;RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;var sh=new UnityEngine.Rendering.SphericalHarmonicsL2();sh.AddAmbientLight(Color.gray);RenderSettings.ambientProbe=sh;
        foreach(string pose in new[]{"ground","skim-lift","skim-cruise","takeoff","cruise","boost","landing-high","landing-low","power-loss","returned"}){
            bool ground=pose=="ground"||pose=="returned",skim=pose.StartsWith("skim"),low=pose=="landing-low";
            m.SkimPhase=skim?(pose=="skim-lift"?Skim.Phase.Lifting:Skim.Phase.Cruise):Skim.Phase.Off;
            m.FlightMode=ground||skim?Flight.Phase.Ground:pose=="takeoff"?Flight.Phase.Takeoff:pose.StartsWith("landing")?Flight.Phase.Landing:pose=="power-loss"?Flight.Phase.PowerLost:Flight.Phase.Cruise;
            m.Grounded=ground;m.WingBlend=ground||skim?0:1;m.Blend=ground?0:1;m.Boost=pose=="boost"||pose=="skim-cruise";m.FlightHeight=low?.3f:8;m.VerticalInput=pose.StartsWith("landing")?-1:0;m.LandingAt=-100;
            float speed=pose=="cruise"?12:pose=="boost"?20:skim?13.5f:0;
            for(int frame=0;frame<40;frame++){rb.position+=Vector3.forward*(speed/60);v.SetPosition(rb.position+Origin.position);Gait.Update(world,v,r,1f/60);}
            Check("finite feet/torso "+pose,Weapons.Finite(r.FootL.position.y)&&Weapons.Finite(r.FootR.position.y)&&Weapons.Finite(r.Torso.localRotation.x));
            if(!Rules.Complete(v)){
                var jets=r.Backpack.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="MechaThruster").ToArray();bool active=jets.Length==2&&jets.All(t=>t.gameObject.activeSelf);
                Check("prototype thruster "+pose,active==(!ground&&pose!="power-loss"));
                if(pose=="cruise")Check("wingless torso leans in flight",m.FlightLean>10);
                if(low)Check("near-ground legs extend",Mathf.Abs(r.FootL.position.y-r.Mount.position.y)<.12f&&Mathf.Abs(r.FootR.position.y-r.Mount.position.y)<.12f);
            }
            var copies=new List<GameObject>();var baked=new List<Mesh>();
            foreach(var renderer in r.Mount.GetComponentsInChildren<Renderer>(true)){
                if(!renderer.enabled||!renderer.gameObject.activeInHierarchy)continue;Mesh mesh;var skin=renderer as SkinnedMeshRenderer;
                if(skin!=null){mesh=new Mesh();skin.BakeMesh(mesh);baked.Add(mesh);}else{var filter=renderer.GetComponent<MeshFilter>();if(filter==null)continue;mesh=filter.sharedMesh;}
                var copy=new GameObject("review "+renderer.name);copy.layer=30;copy.transform.SetPositionAndRotation(renderer.transform.position,renderer.transform.rotation);copy.transform.localScale=renderer.transform.lossyScale;copy.AddComponent<MeshFilter>().sharedMesh=mesh;copy.AddComponent<MeshRenderer>().sharedMaterials=renderer.sharedMaterials;copies.Add(copy);
            }
            int view=0;foreach(var offset in new[]{new Vector3(5,1,7),new Vector3(-5,1,-7)}){
                var focus=r.Mount.position+Vector3.up*1.5f;camera.transform.position=focus+offset*(Rules.Complete(v)?1.65f:1f);camera.transform.LookAt(focus);camera.Render();RenderTexture.active=rt;var png=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);png.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);png.Apply();File.WriteAllBytes(Path.Combine(folder,(Rules.Complete(v)?"complete-":"prototype-")+pose+"-"+(view++)+".png"),png.EncodeToPNG());UnityEngine.Object.DestroyImmediate(png);
            }
            foreach(var copy in copies)UnityEngine.Object.DestroyImmediate(copy);foreach(var mesh in baked)UnityEngine.Object.DestroyImmediate(mesh);
        }
        RenderTexture.active=null;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(camera.gameObject);UnityEngine.Object.DestroyImmediate(light.gameObject);
        report.Add("VISUAL: production Gait/Flight poses, scripted state and trajectory; not human driving or two-client networking. Images: "+folder);
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
}
