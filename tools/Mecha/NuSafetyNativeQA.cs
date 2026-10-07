using System;using System.IO;using System.Linq;using System.Collections.Generic;using UnityEngine;using HarmonyLib;using PZAEC.Mecha;
public sealed class MechaTraversalQA : IModApi
{
    static List<string> report=new List<string>();static int failures;
    static int Repetitions=Environment.GetEnvironmentVariable("MECHA_QA_FAST")=="1"?3:20;
    public void InitMod(Mod mod){if(Environment.GetCommandLineArgs().Contains("-mechaMotionQA"))ModEvents.GameStartDone.RegisterHandler(Run);}
    static bool Pause(){return false;}
    // Place the rim beyond the whole sole at all tested approach angles.
    static float Rim(EntityVehicle v,float legacy){return Rules.Complete(v)?Mathf.Max(legacy,Rules.SoleDepth(v)*.5f+Rules.SoleWidth(v)*.5f+.05f):legacy;}
    static void Check(string name,bool ok){report.Add((ok?"PASS ":"FAIL ")+name);if(!ok)failures++;}
    static GameObject Box(string name,Vector3 center,Vector3 size)
    {var go=new GameObject(name);go.layer=16;go.transform.position=center;go.AddComponent<BoxCollider>().size=size;return go;}
    static void Reset(EntityVehicle v,float floor=400,float yaw=0)
    {
        Model.GetRig(v).ResetPose();Traversal.Forget(v);GroundSupport.Suspend(v);var s=GroundSupport.Get(v);var rb=v.vehicleRB;
        rb.isKinematic=false;rb.useGravity=true;rb.constraints=RigidbodyConstraints.None;rb.drag=.05f;
        rb.position=new Vector3(0,floor+s.Shape.NeutralY+Rules.SoleClearance,0);rb.rotation=Quaternion.Euler(0,yaw,0);rb.velocity=rb.angularVelocity=Vector3.zero;
        v.SetPosition(rb.position+Origin.position);Physics.SyncTransforms();GroundSupport.Observe(v);
    }
    static void Tick(EntityVehicle v,float dt,float speed=0,float steer=0)
    {
        var support=GroundSupport.Observe(v);var state=Traversal.Get(v);
        bool enabled=speed!=0||steer!=0;support.DesiredVelocity=v.vehicleRB.rotation*Vector3.forward*speed;
        if(state.Current!=null)Traversal.Advance(v,support,state,dt);
        else{if(speed!=0){Traversal.Get(v).SearchAt=-100;speed=Traversal.LimitSpeed(v,support,speed,dt);}GroundSupport.Walking(support,dt,enabled);if(!GroundSupport.MotionClear(support,dt)){GroundSupport.StopHorizontal(support);speed=0;}GroundSupport.Apply(support,dt);speed=Mathf.Clamp(speed,-support.DriveCap,support.DriveCap);if(support.Grounded)Locomotion.ApplyDrive(v.vehicleRB,v.vehicleRB.rotation*Vector3.forward,speed,steer,false,support.Normal,dt);}
        Physics.Simulate(dt);v.SetPosition(v.vehicleRB.position+Origin.position);Physics.SyncTransforms();
    }
    static void Capture(Model.Rig rig,string name)
    {
        var root=new GameObject("Traversal pose camera");var camera=root.AddComponent<Camera>();var rt=new RenderTexture(960,720,24);camera.targetTexture=rt;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.1f,.13f);camera.fieldOfView=38;
        var focus=rig.Mount.position+Vector3.up*1.55f;camera.transform.position=focus+new Vector3(-4,2.2f,-7);camera.transform.LookAt(focus);
        var lamp=new GameObject("Traversal pose light").AddComponent<Light>();lamp.type=LightType.Directional;lamp.intensity=1.7f;lamp.transform.rotation=Quaternion.Euler(35,-30,0);RenderSettings.ambientLight=new Color(.65f,.65f,.65f);
        var baked=new List<GameObject>();var skins=rig.Mount.GetComponentsInChildren<SkinnedMeshRenderer>();
        var wings=skins.Where(s=>s.GetComponent<MechaRenderPart>().Role=="WingL"||s.GetComponent<MechaRenderPart>().Role=="WingR").OrderBy(s=>s.GetComponent<MechaRenderPart>().Role).ToArray();
        if(wings.Length==2){var mesh=new Mesh();var pair=new Vector3[2][];
            for(int i=0;i<2;i++){wings[i].BakeMesh(mesh);pair[i]=mesh.vertices;for(int j=0;j<pair[i].Length;j++)pair[i][j]=rig.Mount.InverseTransformPoint(wings[i].transform.TransformPoint(pair[i][j]));}
            float error=0;for(int j=0;j<pair[0].Length;j++){var left=pair[0][j];left.x=-left.x;error=Mathf.Max(error,Vector3.Distance(left,pair[1][j]));}
            Check(name+" whole wing geometry mirrors including lower fins, error="+error,error<.001f);UnityEngine.Object.DestroyImmediate(mesh);
        }
        foreach(var skin in skins){var mesh=new Mesh();skin.BakeMesh(mesh);var go=new GameObject("Traversal baked skin");go.transform.SetPositionAndRotation(skin.transform.position,skin.transform.rotation);go.transform.localScale=skin.transform.lossyScale;go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=skin.sharedMaterial;skin.enabled=false;baked.Add(go);}
        camera.Render();var previous=RenderTexture.active;RenderTexture.active=rt;var texture=new Texture2D(960,720,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,960,720),0,0);texture.Apply();
        File.WriteAllBytes(Path.Combine(GameIO.GetSaveGameDir(),name+".png"),texture.EncodeToPNG());RenderTexture.active=previous;
        foreach(var skin in skins)skin.enabled=true;foreach(var go in baked){UnityEngine.Object.DestroyImmediate(go.GetComponent<MeshFilter>().sharedMesh);UnityEngine.Object.DestroyImmediate(go);}
        UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(lamp.gameObject);UnityEngine.Object.DestroyImmediate(root);
    }
    static void Matrix(World world,EntityVehicle v,Model.Rig rig,float homeZ)
    {
        int attempts=0,success=0;float worst=0,slip=0,penetration=0;var stopwatch=System.Diagnostics.Stopwatch.StartNew();
        foreach(string mode in new[]{"up","down","gap"})foreach(float yaw in new[]{-15f,0f,15f})foreach(int side in new[]{0,1}) {
            float edge=homeZ+Rim(v,mode=="up"?.55f:.425f);
            var a=Box("Matrix near bank",new Vector3(0,399.5f,edge-15),new Vector3(30,1,30));
            var b=Box("Matrix far bank",new Vector3(0,399.5f+(mode=="up"?1:0),edge+(mode=="gap"?.75f:0)+15),new Vector3(30,1,30));
            if(mode=="down")a.transform.position+=Vector3.up;
            Physics.SyncTransforms();
            for(int repeat=0;repeat<Repetitions;repeat++) {
                attempts++;rig.ResetPose();Reset(v,mode=="down"?401:400,yaw);var support=GroundSupport.Get(v);
                string reason;var plan=Traversal.Search(v,support,side,out reason);
                if(plan==null){report.Add("MATRIX REJECT "+Rules.DisplayName(v)+" mode="+mode+" yaw="+yaw+" side="+side+" reason="+reason);continue;}
                Traversal.Begin(v,support,plan,false);
                float dt=repeat%3==0?1f/30:repeat%3==1?1f/60:1f/120;
                int frames=Mathf.CeilToInt(2.0f/dt);float error=0;int stretch=0;
                for(int frame=0;frame<frames;frame++){
                    Tick(v,dt);Locomotion.Get(v).Grounded=support.Grounded;Gait.Update(world,v,rig,dt);
                    if(frame%3==0)ArticulationAudit.Sample(rig,"traversal-"+mode+"-yaw"+yaw+"-side"+side,frame);
                    for(int i=0;i<2;i++){
                        var f=support.Feet[i];var foot=i==0?rig.FootL:rig.FootR;
                        error=Mathf.Max(error,Vector3.Distance(foot.position+Origin.position,f.Position));
                        if(!support.Shape.Reach(v.vehicleRB.position+Origin.position,v.vehicleRB.rotation,i,f.Position,f.Normal)){if(stretch<2&&repeat==2){var ankle=f.Position-Quaternion.FromToRotation(Vector3.up,f.Normal)*v.vehicleRB.rotation*support.Shape.AnkleOffset[i];float distance=Vector3.Distance(v.vehicleRB.position+Origin.position+v.vehicleRB.rotation*support.Shape.Hip[i],ankle);report.Add("REACH DETAIL mode="+mode+" frame="+frame+" side="+i+" age="+Traversal.Get(v).Age+" distance="+distance+" min="+(Mathf.Abs(support.Shape.UpperAt(i)-support.Shape.LowerAt(i))+.01f)+" max="+((support.Shape.UpperAt(i)+support.Shape.LowerAt(i))*.95f)+" tracking="+Vector3.Distance(v.vehicleRB.position+Origin.position,Traversal.Get(v).TargetRoot));}stretch++;}
                        if(f.Planted)penetration=Mathf.Max(penetration,f.Position.y-(foot.position.y+Origin.position.y));
                    }
                }
                float end=Vector3.Distance(v.vehicleRB.position+Origin.position,plan.End);worst=Mathf.Max(worst,end);slip=Mathf.Max(slip,error);
                if(end<=.05f&&error<=.05f&&stretch==0)success++;else report.Add("MATRIX FAIL "+Rules.DisplayName(v)+" mode="+mode+" yaw="+yaw+" side="+side+" dt="+dt+" end="+end+" foot="+error+" reserve="+stretch+" reason="+Traversal.Get(v).Reason);
            }
            UnityEngine.Object.DestroyImmediate(a);UnityEngine.Object.DestroyImmediate(b);
        }
        Check(Rules.DisplayName(v)+" "+Repetitions+" repetitions both feet / 3 angles / 30,60,120 fixed samples, success="+success+"/"+attempts+" end="+worst+" foot="+slip+" penetration="+penetration+" seconds="+stopwatch.Elapsed.TotalSeconds,success==attempts&&penetration<=.02f);
    }
    static void Safety(EntityVehicle v,float homeZ)
    {
        float edge=homeZ+Rim(v,.425f);var floor=Box("Safety floor",new Vector3(0,399.5f,0),new Vector3(30,1,120));
        var step=Box("Safety step",new Vector3(0,400.5f,edge+15),new Vector3(30,1,30));Reset(v);
        var roof=Box("Safety roof",new Vector3(0,402.75f,edge+2),new Vector3(30,.4f,4));Physics.SyncTransforms();
        string reason;Check(Rules.DisplayName(v)+" low roof rejected",Traversal.Search(v,GroundSupport.Get(v),0,out reason)==null);
        UnityEngine.Object.DestroyImmediate(roof);
        step.GetComponent<BoxCollider>().size=new Vector3(.25f,1,30);Reset(v);
        Check(Rules.DisplayName(v)+" narrow platform rejected",Traversal.Search(v,GroundSupport.Get(v),0,out reason)==null);
        step.GetComponent<BoxCollider>().size=new Vector3(30,1,30);Reset(v);var p=Traversal.Search(v,GroundSupport.Get(v),0,out reason);
        if(p!=null){Traversal.Begin(v,GroundSupport.Get(v),p,false);for(int i=0;i<12;i++)Tick(v,.02f);
            var blocker=Box("Dynamic blocker",new Vector3(0,402,edge+1),new Vector3(3,3,1));Physics.SyncTransforms();for(int i=0;i<80;i++)Tick(v,.02f);
            Check(Rules.DisplayName(v)+" dynamic obstruction cancels",!Traversal.Active(v)&&Vector3.Distance(v.vehicleRB.position+Origin.position,p.End)>.10f);
            UnityEngine.Object.DestroyImmediate(blocker);
        } else Check("dynamic fixture precondition",false);
        Reset(v);p=Traversal.Search(v,GroundSupport.Get(v),0,out reason);
        if(p!=null){Traversal.Begin(v,GroundSupport.Get(v),p,false);for(int i=0;i<25;i++)Tick(v,.02f);UnityEngine.Object.DestroyImmediate(step);for(int i=0;i<100;i++)Tick(v,.02f);Check(Rules.DisplayName(v)+" landing destroyed cancels instead of hovering",!Traversal.Active(v)&&v.vehicleRB.position.y<400.1f);}
        if(step!=null)UnityEngine.Object.DestroyImmediate(step);UnityEngine.Object.DestroyImmediate(floor);
    }
    static void RenderRates(World world,EntityVehicle v,Model.Rig rig,float homeZ)
    {
        var floor=Box("Frame near bank",new Vector3(0,399.5f,homeZ+Rim(v,.55f)-15),new Vector3(30,1,30));
        var step=Box("Frame far bank",new Vector3(0,400.5f,homeZ+Rim(v,.55f)+15),new Vector3(30,1,30));
        foreach(float fps in new[]{30f,60f,120f}){
            float error=0;int ok=0;
            for(int side=0;side<2;side++)for(int repeat=0;repeat<Repetitions;repeat++){
                rig.ResetPose();Reset(v);var support=GroundSupport.Get(v);string reason;var p=Traversal.Search(v,support,side,out reason);if(p==null)continue;
                Traversal.Begin(v,support,p,false);float render=0;
                for(int tick=0;tick<100;tick++){
                    Tick(v,.02f);Locomotion.Get(v).Grounded=support.Grounded;render+=.02f;
                    while(render>=1/fps){render-=1/fps;Gait.Update(world,v,rig,1/fps);for(int i=0;i<2;i++)error=Mathf.Max(error,Vector3.Distance((i==0?rig.FootL:rig.FootR).position+Origin.position,support.Feet[i].Position));}
                }
                if(Vector3.Distance(v.vehicleRB.position+Origin.position,p.End)<=.05f)ok++;
            }
            Check(Rules.DisplayName(v)+" render cadence "+fps+"Hz / fixed physics 50Hz passes="+ok+"/"+(2*Repetitions)+" foot="+error,ok==2*Repetitions&&error<=.05f);
        }
        UnityEngine.Object.DestroyImmediate(floor);UnityEngine.Object.DestroyImmediate(step);
    }
    static void Slopes(EntityVehicle v)
    {
        foreach(float angle in new[]{15f,35f,45f,50f}){
            var ramp=Box("Slope",new Vector3(0,399.5f,0),new Vector3(30,1,120));ramp.transform.rotation=Quaternion.Euler(-angle,0,0);Physics.SyncTransforms();
            float plane=399.5f+.5f/Mathf.Cos(angle*Mathf.Deg2Rad)+Mathf.Tan(angle*Mathf.Deg2Rad)*GroundSupport.Get(v).Shape.Home[0].z;
            Reset(v,plane);var support=GroundSupport.Get(v);for(int tick=0;tick<100;tick++)Tick(v,.02f);
            for(int i=0;i<2;i++){var at=v.vehicleRB.position+Origin.position+v.vehicleRB.rotation*support.Shape.Home[i];GroundSupport.Pad pad;bool found=GroundSupport.PadAt(v,at,v.vehicleRB.rotation,.6f,.4f,out pad);report.Add("SLOPE PAD angle="+angle+" root="+v.vehicleRB.position+" pad="+found+" point="+pad.Point+" normal="+pad.Normal+" reach="+support.Shape.Reach(v.vehicleRB.position+Origin.position,v.vehicleRB.rotation,i,pad.Point,pad.Normal)+" "+GroundSupport.Diagnostics(v));}
            Check(Rules.DisplayName(v)+" slope support "+angle,angle>45?!support.Grounded:support.Grounded);
            if(angle<=45){for(int tick=0;tick<200;tick++){Tick(v,.02f,2);if(angle==35&&tick%20==0)report.Add("SLOPE TRACE tick="+tick+" root="+v.vehicleRB.position+" vel="+v.vehicleRB.velocity+" "+GroundSupport.Diagnostics(v));}Check(Rules.DisplayName(v)+" slope forward "+angle+" pos="+v.vehicleRB.position+" grounded="+support.Grounded,support.Grounded&&v.vehicleRB.position.z>1);}
            UnityEngine.Object.DestroyImmediate(ramp);
        }
    }
    static void StairsAndEdges(World world,EntityVehicle v,Model.Rig rig)
    {
        var objects=new List<GameObject>();objects.Add(Box("Stairs floor",new Vector3(0,399.5f,0),new Vector3(30,1,120)));
        float tread=Mathf.Max(.85f,Rules.SoleDepth(v)+.15f);
        for(int i=0;i<8;i++){float height=.15f*(i+1);objects.Add(Box("Stair "+i,new Vector3(0,400+height*.5f,3+i*tread+15),new Vector3(30,height,30)));}
        Reset(v);for(int i=0;i<550;i++)Tick(v,.02f,2);
        Check(Rules.DisplayName(v)+" continuous static stairs pos="+v.vehicleRB.position+" "+GroundSupport.Diagnostics(v)+" "+Traversal.Diagnostics(v),GroundSupport.IsGrounded(v)&&v.vehicleRB.position.z>10&&v.vehicleRB.position.y>400.8f);
        foreach(var go in objects)UnityEngine.Object.DestroyImmediate(go);objects.Clear();
        objects.Add(Box("Edge near bank",new Vector3(0,399.5f,2-15),new Vector3(30,1,30)));
        objects.Add(Box("Edge far bank",new Vector3(0,399.5f,3+15),new Vector3(30,1,30)));
        Reset(v);for(int i=0;i<180;i++)Tick(v,.02f,13.5f);
        Check(Rules.DisplayName(v)+" oversize gap brakes before unsupported edge",GroundSupport.IsGrounded(v)&&v.vehicleRB.position.z<1.8f);
        foreach(var go in objects)UnityEngine.Object.DestroyImmediate(go);objects.Clear();
        objects.Add(Box("Uneven floor",new Vector3(0,399.5f,0),new Vector3(30,1,120)));
        objects.Add(Box("Single raised sole",new Vector3(-7,400.15f,0),new Vector3(14,.3f,120)));
        Reset(v);for(int i=0;i<100;i++)Tick(v,.02f);var support=GroundSupport.Get(v);Locomotion.Get(v).Grounded=support.Grounded;Gait.Update(world,v,rig,.02f);
        bool reachable=true;for(int side=0;side<2;side++)reachable&=support.Shape.Reach(v.vehicleRB.position+Origin.position,v.vehicleRB.rotation,side,support.Feet[side].Position,support.Feet[side].Normal);
        Check(Rules.DisplayName(v)+" asymmetric terrain bears weight without stretching",support.Feet.All(f=>f.Planted)&&reachable);
        foreach(var go in objects)UnityEngine.Object.DestroyImmediate(go);
    }
    static TraverseWire Copy(TraverseWire data)
    {
        using(var stream=new MemoryStream()){
            var writer=new PooledBinaryWriter();writer.SetBaseStream(stream);data.Write(writer);writer.Flush();
            Check("dedicated traversal payload length",stream.Length==TraverseWire.Bytes);stream.Position=0;
            var reader=new PooledBinaryReader();reader.SetBaseStream(stream);var result=new TraverseWire();result.Read(reader);return result;
        }
    }
    static void Network(World world,EntityVehicle v,float homeZ)
    {
        var floor=Box("Net near bank",new Vector3(0,399.5f,homeZ+Rim(v,.55f)-15),new Vector3(30,1,30));
        var step=Box("Net far bank",new Vector3(0,400.5f,homeZ+Rim(v,.55f)+15),new Vector3(30,1,30));
        var pilot=EntityFactory.CreateEntity(EntityClass.FromString("playerMale"),new Vector3(10,400,0)) as EntityPlayer;world.SpawnEntityInWorld(pilot);
        var seats=AccessTools.Field(typeof(Entity),"attachedEntities");var old=seats.GetValue(v);bool driver=v.hasDriver,engine=v.IsEngineRunning;
        try{
            seats.SetValue(v,new Entity[]{pilot});pilot.AttachedToEntity=v;v.hasDriver=true;v.IsEngineRunning=true;v.vehicle.SetFuelLevel(100);
            Reset(v);string reason;var p=Traversal.Search(v,GroundSupport.Get(v),0,out reason);var s=Traversal.Get(v);
            Check("network plan precondition "+reason,p!=null);if(p==null)return;
            var motion=Locomotion.Get(v);motion.LandingPendingUntil=Time.time+.75f;motion.LandingEventExpected=true;motion.AirSince=Time.time-1;
            Traversal.Begin(v,GroundSupport.Get(v),p,false);Check("traversal discards old jump landing authorization",motion.LandingPendingUntil<0&&!motion.LandingEventExpected&&motion.AirSince<0);
            motion.Grounded=true;motion.LandingPendingUntil=Time.time+.75f;
            var serial=AccessTools.Field(typeof(Weapons),"serial");int serialBefore=(int)serial.GetValue(null);var combat=Weapons.GetState(v);
            AccessTools.Method(typeof(Weapons),"TrampleRequest").Invoke(null,new object[]{combat,pilot.entityId,Weapons.Stomp,Time.time,1f});Check("controlled traversal cannot emit stomp damage or landing FX",(int)serial.GetValue(null)==serialBefore);motion.LandingPendingUntil=-100;
            s.Accepted=false;s.LastPacket=Time.time;var rootBefore=v.vehicleRB.position;
            for(int i=0;i<30;i++){Traversal.Step(v,GroundSupport.Observe(v),Locomotion.Get(v),0,true,.02f);Physics.Simulate(.02f);v.SetPosition(v.vehicleRB.position+Origin.position);Physics.SyncTransforms();}
            Check("acknowledgment wait retains planted support without starting swing",s.Phase==Traversal.Stage.Prepare&&GroundSupport.IsGrounded(v)&&Vector3.Distance(rootBefore,v.vehicleRB.position)<.02f&&GroundSupport.Get(v).Feet.All(f=>f.Planted));
            s.LastPacket=Time.time-1.1f;Traversal.Step(v,GroundSupport.Observe(v),Locomotion.Get(v),0,true,.02f);Check("unacknowledged request times out",!Traversal.Active(v));
            Reset(v);p=Traversal.Search(v,GroundSupport.Get(v),0,out reason);s=Traversal.Get(v);Traversal.Begin(v,GroundSupport.Get(v),p,false);var start=Copy(new TraverseWire().Setup(v,s,0));
            Check("server accepts validated driver/terrain/path",TraversalNet.ServerReceive(world,pilot.entityId,start));
            Check("server rejects duplicate start",!TraversalNet.ServerReceive(world,pilot.entityId,start));
            var bad=Copy(start);bad.Actor++;bad.Tick++;Check("server rejects foreign driver",!TraversalNet.ServerReceive(world,bad.Actor,bad));
            bad=Copy(start);bad.Plan.Land[0].Point.x=float.NaN;Check("nonfinite landing rejected",!bad.Valid());
            bad=Copy(start);bad.Plan.StartNormal[0]=Vector3.down;Check("invalid support normal rejected",!bad.Valid());
            bad=Copy(start);bad.Mode=1;bad.Tick+=2;bad.Age=.1f;bad.Phase=p.Phase(bad.Age);bad.Plan.End.x+=.5f;
            Check("progress cannot replace accepted path",!TraversalNet.ServerReceive(world,pilot.entityId,bad));
            var progress=Copy(start);progress.Mode=1;progress.Tick++;progress.Age=.1f;progress.Phase=p.Phase(progress.Age);
            Check("server accepts monotonic progress",TraversalNet.ServerReceive(world,pilot.entityId,progress));
            Check("server drops old/duplicate progress",!TraversalNet.ServerReceive(world,pilot.entityId,progress));
            var blocker=Box("Net new obstruction",new Vector3(0,402,homeZ+.75f),new Vector3(3,2,1));Physics.SyncTransforms();
            progress=Copy(progress);progress.Tick++;progress.Age=.30f;progress.Phase=p.Phase(progress.Age);
            Check("server rejects changed path during execution",!TraversalNet.ServerReceive(world,pilot.entityId,progress));UnityEngine.Object.DestroyImmediate(blocker);
            Traversal.Forget(v);v.isEntityRemote=true;
            var incoming=Copy(start);incoming.Mode=1;incoming.Tick=10;incoming.Age=.45f;incoming.Phase=p.Phase(incoming.Age);var velocity=v.vehicleRB.velocity;
            Check("remote mid-action join replays accepted feet",TraversalNet.ClientReceive(world,incoming)&&Traversal.Get(v).Remote&&Traversal.Active(v));
            Check("remote replay never applies physics force",v.vehicleRB.velocity==velocity);
            Check("remote drops out-of-order event",!TraversalNet.ClientReceive(world,start));
            incoming=Copy(incoming);incoming.Tick++;incoming.Mode=2;incoming.Phase=Traversal.Stage.Exit;
            Check("remote stop clears unfinished plan",TraversalNet.ClientReceive(world,incoming)&&!Traversal.Active(v));
            Check("stop event cannot replay twice",!TraversalNet.ClientReceive(world,incoming));
            incoming=Copy(start);incoming.Action++;incoming.Tick=20;incoming.Plan.Id=incoming.Action;
            Check("next action accepted",TraversalNet.ClientReceive(world,incoming));
            seats.SetValue(v,new Entity[0]);Traversal.Tick(world,.02f);Check("driver departure cancels replay",!Traversal.Active(v));
            report.Add("NETWORK LIMIT: same-process native protocol test; not a real two-client session.");
        }finally{v.isEntityRemote=false;v.hasDriver=driver;v.IsEngineRunning=engine;seats.SetValue(v,old);pilot.AttachedToEntity=null;world.RemoveEntity(pilot.entityId,EnumRemoveEntityReason.Despawned);Traversal.Forget(v);UnityEngine.Object.DestroyImmediate(floor);UnityEngine.Object.DestroyImmediate(step);}
    }
    static void Run(ref ModEvents.SGameStartDoneData data)
    {
        if(GamePrefs.GetString(EnumGamePrefs.GameName)!="MechaQA_Isolated")return;
        bool simulation=Physics.autoSimulation;Physics.autoSimulation=false;
        var world=GameManager.Instance.World;
        try{
            new Harmony("mecha.traversal.qa").Patch(AccessTools.Method(typeof(Weapons),"Update"),prefix:new HarmonyMethod(typeof(MechaTraversalQA),nameof(Pause)));
            foreach(string name in new[]{Rules.VehicleName,Rules.UltimateVehicle}){
                var v=EntityFactory.CreateEntity(EntityClass.FromString(name),new Vector3(0,400,0)+Origin.position) as EntityVehicle;
                world.SpawnEntityInWorld(v);var rb=v.vehicleRB;for(var t=rb.transform;t!=null;t=t.parent)t.gameObject.SetActive(true);
                var rig=Model.GetRig(v);rig.ResetPose();var support=GroundSupport.Get(v);float homeZ=support.Shape.Home[0].z;
                report.Add("PROFILE "+name+" neutral="+support.Shape.NeutralY+" hips="+support.Shape.Hip[0]+" home="+support.Shape.Home[0]+" ankle="+support.Shape.AnkleOffset[0]+" backpack="+support.Shape.Backpack);
                foreach(var c in UnityEngine.Object.FindObjectsOfType<Collider>())if(c.enabled&&!c.isTrigger&&Mathf.Abs(c.transform.position.y-400)<3){var path=c.name;for(var t=c.transform.parent;t!=null;t=t.parent)path=t.name+"/"+path;report.Add("COLLIDER "+path+" rb="+(c.attachedRigidbody!=null?c.attachedRigidbody.name:"none")+" entity="+GameUtils.GetHitRootEntity(c.tag,c.transform)+" type="+c.GetType()+" pos="+c.transform.position+" tag="+c.tag);}
                Check(name+" all wheel load disabled",rb.GetComponentsInChildren<WheelCollider>(true).All(w=>!w.enabled));
                var floor=Box("Traversal floor",new Vector3(0,399.5f,0),new Vector3(30,1,300));
                Reset(v);for(int tick=0;tick<150;tick++)Tick(v,.02f);
                Check(name+" passive foot support",GroundSupport.IsGrounded(v)&&Mathf.Abs(rb.position.y-(400+support.Shape.NeutralY+Rules.SoleClearance))<.02f);
                Reset(v);for(int tick=0;tick<400;tick++)Tick(v,.02f,4);
                Check(name+" supported walking 4m/s pos="+rb.position+" velocity="+rb.velocity,GroundSupport.IsGrounded(v)&&rb.position.z>15&&Mathf.Abs(rb.velocity.z-4)<.15f);
                Reset(v);for(int tick=0;tick<500;tick++)Tick(v,.02f,13.5f);
                Check(name+" supported boost 13.5m/s pos="+rb.position+" velocity="+rb.velocity+" "+GroundSupport.Diagnostics(v),GroundSupport.IsGrounded(v)&&Mathf.Abs(rb.velocity.z-13.5f)<.3f);
                Reset(v);float turn=0;
                for(int tick=0;tick<150;tick++){float yawBefore=rb.rotation.eulerAngles.y;Tick(v,.02f,0,1);turn+=Mathf.Abs(Mathf.DeltaAngle(yawBefore,rb.rotation.eulerAngles.y));if(tick%20==0)report.Add("TURN tick="+tick+" yaw="+rb.rotation.eulerAngles.y+" rate="+rb.angularVelocity+" root="+rb.position+" "+GroundSupport.Diagnostics(v));}
                Check(name+" planted stationary turn degrees="+turn,turn>120&&GroundSupport.IsGrounded(v));
                foreach(float small in new[]{.15f,.30f}){
                    var obstacle=Box("Small step",new Vector3(0,400+small*.5f,18),new Vector3(30,small,30));Reset(v);
                    for(int tick=0;tick<400;tick++)Tick(v,.02f,4);
                    Check(name+" automatic small step "+small+" pos="+rb.position+" vel="+rb.velocity+" "+GroundSupport.Diagnostics(v),GroundSupport.IsGrounded(v)&&rb.position.z>6&&rb.position.y>400+small+support.Shape.NeutralY-.20f);
                    UnityEngine.Object.DestroyImmediate(obstacle);
                }
                foreach(float height in new[]{.50f,.75f,1f,1.2f,-.50f,-1f}){
                    bool down=height<0;float edge=homeZ+Rim(v,.425f);
                    GameObject step;
                    if(down){UnityEngine.Object.DestroyImmediate(floor);floor=Box("Traversal lower floor",new Vector3(0,399.5f,0),new Vector3(30,1,120));step=Box("Traversal upper bank",new Vector3(0,400-height/2,edge-15),new Vector3(30,-height,30));Reset(v,400-height);}
                    else{step=Box("Traversal step",new Vector3(0,400+height/2,edge+15),new Vector3(30,height,30));Reset(v);}
                    for(int side=0;side<2;side++){
                        if(side==1)Reset(v,down?400-height:400);
                        string reason;var p=Traversal.Search(v,support,side,out reason);
                        if(height>1){Check(name+" reject 1.20m step",p==null);continue;}
                        Check(name+" plan "+height+" side="+side+" reason="+reason+" support="+GroundSupport.Diagnostics(v),p!=null);
                        if(p==null)continue;
                        report.Add("PLAN "+height+" root="+p.Root+" end="+p.End+" feet="+p.Start[0]+" -> "+p.Land[0].Point);
                        Traversal.Begin(v,support,p,false);float drift=0,error=0,reach=0;
                        var anchors=new Vector3[]{support.Feet[0].Position,support.Feet[1].Position};
                        for(int tick=0;tick<200;tick++){
                            Tick(v,.02f);
                            var m=Locomotion.Get(v);m.Grounded=support.Grounded;
                            Gait.Update(world,v,rig,.02f);
                            if(height==1&&side==0&&(tick==10||tick==25||tick==40))Capture(rig,name+"-traverse-"+tick);
                            for(int i=0;i<2;i++){
                                var f=support.Feet[i];var foot=i==0?rig.FootL:rig.FootR;
                                error=Mathf.Max(error,Vector3.Distance(foot.position+Origin.position,f.Position));
                                if(f.Planted&&Vector3.Distance(f.Position,anchors[i])<.01f)drift=Mathf.Max(drift,Vector3.Distance(foot.position+Origin.position,anchors[i]));
                                anchors[i]=f.Position;
                                if(!support.Shape.Reach(rb.position+Origin.position,rb.rotation,i,f.Position,f.Normal)){reach++;if(reach<5)report.Add("REACHFAIL h="+height+" tick="+tick+" side="+i+" root="+rb.position+" foot="+f.Position);}
                            }
                            if(tick%10==0&&tick<80)report.Add("TRACE h="+height+" side="+side+" tick="+tick+" rb="+rb.position+" "+Traversal.Diagnostics(v)+" "+GroundSupport.Diagnostics(v));
                        }
                        Check(name+" physical endpoint "+height+" side="+side+" error="+Vector3.Distance(rb.position+Origin.position,p.End)+" stop="+Traversal.Get(v).Reason,Vector3.Distance(rb.position+Origin.position,p.End)<=.05f);
                        Check(name+" IK contact error "+height+" side="+side+" error="+error+" slip="+drift,error<=.05f&&drift<=.05f);
                        Check(name+" 5% extension reserve "+height+" side="+side+" violations="+reach,reach==0);
                    }
                    UnityEngine.Object.DestroyImmediate(step);
                    rig.ResetPose();
                }
                foreach(float width in new[]{.40f,.75f,1f}){
                    UnityEngine.Object.DestroyImmediate(floor);float edge=homeZ+Rim(v,.425f);
                    var left=Box("Traversal near bank",new Vector3(0,399.5f,edge-15),new Vector3(30,1,30));
                    var right=Box("Traversal far bank",new Vector3(0,399.5f,edge+width+15),new Vector3(30,1,30));
                    Reset(v);string reason;var p=Traversal.Search(v,support,0,out reason);
                    Check(name+" gap plan "+width+" reason="+reason,width>Rules.ActiveGapWidth?p==null:p!=null);
                    if(p!=null){Traversal.Begin(v,support,p,false);for(int tick=0;tick<200;tick++)Tick(v,.02f);Check(name+" physical gap "+width+" error="+Vector3.Distance(rb.position+Origin.position,p.End)+" stop="+Traversal.Get(v).Reason,Vector3.Distance(rb.position+Origin.position,p.End)<=.05f);}
                    UnityEngine.Object.DestroyImmediate(left);UnityEngine.Object.DestroyImmediate(right);
                }
                Matrix(world,v,rig,homeZ);
                RenderRates(world,v,rig,homeZ);
                Safety(v,homeZ);
                Slopes(v);
                StairsAndEdges(world,v,rig);
                Network(world,v,homeZ);
                floor=Box("Traversal floor",new Vector3(0,399.5f,0),new Vector3(30,1,300));Reset(v);
                UnityEngine.Object.DestroyImmediate(floor);float before=rb.position.y;for(int tick=0;tick<25;tick++)Tick(v,.02f);
                Check(name+" removed floor falls without support",!support.Grounded&&rb.position.y<before-.8f);
                foreach(var c in v.transform.GetComponentsInChildren<Collider>(true))c.enabled=false;
                rb.gameObject.SetActive(false);v.transform.gameObject.SetActive(false);world.RemoveEntity(v.entityId,EnumRemoveEntityReason.Despawned);GroundSupport.Forget(v);
            }
        }catch(Exception e){Check("unexpected exception "+e,false);}
        finally{
            Physics.autoSimulation=simulation;
            var path=Path.Combine(GameIO.GetSaveGameDir(),"mecha-traversal-qa.txt");File.WriteAllLines(path,report);
            foreach(var line in report.Where(s=>!s.StartsWith("TRACE")))Log.Out("[MechaTraversalQA] "+line);
            ArticulationAudit.Save(GameIO.GetSaveGameDir());
            NuSafetySuite.Count+=failures;Log.Out("[NuSubQA] COMPLETE failures="+failures+" report="+path);
        }
    }
}

public sealed class SharedFlightNativeQA:IModApi {
 static List<string> report=new List<string>();static int failures;
 public void InitMod(Mod mod){if(Environment.GetCommandLineArgs().Contains("-mechaMotionQA"))ModEvents.GameStartDone.RegisterHandler(Run);}
 static void Check(string name,bool ok){report.Add((ok?"PASS ":"FAIL ")+name);if(!ok)failures++;}
 static void Run(ref ModEvents.SGameStartDoneData data){try{
 var world=GameManager.Instance.World;AccessTools.Field(typeof(Weapons),"currentWorld").SetValue(null,world);
 foreach(bool complete in new[]{false,true}){
 var v=EntityFactory.CreateEntity(EntityClass.FromString(complete?Rules.UltimateVehicle:Rules.VehicleName),new Vector3(8,320,0)+Origin.position) as EntityVehicle;world.SpawnEntityInWorld(v);v.vehicle.SetItemValue(ItemClass.GetItem(complete?Rules.UltimateItem:Rules.PlaceableItem,false));
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
 }catch(Exception e){Check("exception "+e,false);}File.WriteAllLines(Path.Combine(GameIO.GetSaveGameDir(),"shared-flight-qa.txt"),report);foreach(var line in report)Log.Out("[SharedFlightQA] "+line);NuSafetySuite.Count+=failures;Log.Out("[NuSubQA] COMPLETE failures="+failures);
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

public sealed class MechaGravityQA : IModApi
{
    static List<string> lines=new List<string>();static int failures;
    public void InitMod(Mod mod){if(Environment.GetCommandLineArgs().Contains("-mechaMotionQA"))ModEvents.GameStartDone.RegisterHandler(Run);}
    static void Check(string label,bool ok){lines.Add((ok?"PASS ":"FAIL ")+label);if(!ok)failures++;}
    static void Reset(EntityVehicle v){
        var rb=v.vehicleRB;rb.isKinematic=false;v.RBActive=true;rb.position=new Vector3(v.entityId%10*30,410,0);rb.velocity=rb.angularVelocity=Vector3.zero;rb.rotation=Quaternion.identity;rb.drag=0;
        v.SetPosition(rb.position+Origin.position);GroundSupport.Suspend(v);Physics.SyncTransforms();
    }
    static void Step(EntityVehicle v,bool lift){
        AccessTools.Method(typeof(EntityVehicle),"PhysicsFixedUpdate").Invoke(v,null);
        if(lift)Flight.ApplyControl(v.vehicleRB,0,0,false,2,.02f);
        Physics.Simulate(.02f);
    }
    static void Run(ref ModEvents.SGameStartDoneData data){
        bool automatic=Physics.autoSimulation;Physics.autoSimulation=false;var world=GameManager.Instance.World;
        try{for(int i=0;i<2;i++){
            var v=EntityFactory.CreateEntity(EntityClass.FromString(i==0?Rules.VehicleName:Rules.UltimateVehicle),new Vector3(i*30,410,0)+Origin.position) as EntityVehicle;world.SpawnEntityInWorld(v);
            try{
                v.vehicle.SetItemValue(ItemClass.GetItem(i==0?Rules.PlaceableItem:Rules.UltimateItem,false));v.vehicle.SetFuelLevel(100);v.IsEngineRunning=false;v.movementInput=null;
                for(var t=v.vehicleRB.transform;t!=null;t=t.parent)t.gameObject.SetActive(true);
                string name=Rules.DisplayName(v);v.hasDriver=false;Reset(v);Step(v,false);
                Check(name+" parked has one gravity vy="+v.vehicleRB.velocity.y,v.vehicleRB.useGravity&&Mathf.Abs(v.vehicleRB.velocity.y+.1962f)<.005f);
                v.hasDriver=true;Reset(v);Step(v,false);
                Check(name+" boarding native gravity handoff vy="+v.vehicleRB.velocity.y,!v.vehicleRB.useGravity&&Mathf.Abs(v.vehicleRB.velocity.y+.1962f)<.005f);
                Reset(v);Step(v,true);
                Check(name+" native thrust net acceleration is +6 vy="+v.vehicleRB.velocity.y,!v.vehicleRB.useGravity&&Mathf.Abs(v.vehicleRB.velocity.y-.12f)<.005f);
                v.hasDriver=false;Reset(v);Step(v,false);
                Check(name+" unboarding restores one gravity vy="+v.vehicleRB.velocity.y,v.vehicleRB.useGravity&&Mathf.Abs(v.vehicleRB.velocity.y+.1962f)<.005f);
            }finally{world.RemoveEntity(v.entityId,EnumRemoveEntityReason.Despawned);}
        }}catch(Exception e){Check("fixture exception "+e,false);}finally{Physics.autoSimulation=automatic;}
        var path=Path.Combine(GameIO.GetSaveGameDir(),"mecha-gravity-qa.txt");File.WriteAllLines(path,lines);foreach(var line in lines)Log.Out("[MechaGravityQA] "+line);NuSafetySuite.Count+=failures;Log.Out("[MechaMotionQA] COMPLETE failures="+NuSafetySuite.Count+" report="+path);
    }
}

static class NuSafetySuite{public static int Count;}
