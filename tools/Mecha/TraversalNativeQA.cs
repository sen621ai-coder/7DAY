using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using PZAEC.Mecha;

// Real Unity/PhysX fixed-step fixtures, using the shipped support/planner/controllers.
// Initial placement is the only teleport. Crossing is exclusively AddForce.
public sealed class MechaTraversalQA : IModApi
{
    static List<string> report=new List<string>();static int failures;
    static int Repetitions=Environment.GetEnvironmentVariable("MECHA_QA_FAST")=="1"?1:20;
    public void InitMod(Mod mod){if(Environment.GetCommandLineArgs().Contains("-mechaMotionQA"))ModEvents.GameStartDone.RegisterHandler(Run);}
    static bool Pause(){return false;}
    static void Check(string name,bool ok){report.Add((ok?"PASS ":"FAIL ")+name);if(!ok)failures++;}
    static GameObject Box(string name,Vector3 center,Vector3 size)
    {var go=new GameObject(name);go.layer=16;go.transform.position=center;go.AddComponent<BoxCollider>().size=size;return go;}
    static void Reset(EntityVehicle v,float floor=400,float yaw=0)
    {
        Traversal.Forget(v);GroundSupport.Suspend(v);var s=GroundSupport.Get(v);var rb=v.vehicleRB;
        rb.isKinematic=false;rb.useGravity=true;rb.constraints=RigidbodyConstraints.None;rb.drag=.05f;
        rb.position=new Vector3(0,floor+s.Shape.NeutralY+Rules.SoleClearance,0);rb.rotation=Quaternion.Euler(0,yaw,0);rb.velocity=rb.angularVelocity=Vector3.zero;
        v.SetPosition(rb.position+Origin.position);Physics.SyncTransforms();GroundSupport.Observe(v);
    }
    static void Tick(EntityVehicle v,float dt,float speed=0,float steer=0)
    {
        var support=GroundSupport.Observe(v);var state=Traversal.Get(v);
        if(state.Current!=null)Traversal.Advance(v,support,state,dt);
        else{if(speed!=0){Traversal.Get(v).SearchAt=-100;speed=Traversal.LimitSpeed(v,support,speed,dt);}GroundSupport.Walking(support,dt,speed!=0||steer!=0);if(!GroundSupport.MotionClear(support,dt)){GroundSupport.StopHorizontal(support);speed=0;}GroundSupport.Apply(support,dt);Locomotion.ApplyDrive(v.vehicleRB,v.vehicleRB.rotation*Vector3.forward,speed,steer,false,support.Normal,dt);}
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
            float edge=homeZ+(mode=="up"?.55f:.425f);
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
                    for(int i=0;i<2;i++){
                        var f=support.Feet[i];var foot=i==0?rig.FootL:rig.FootR;
                        error=Mathf.Max(error,Vector3.Distance(foot.position+Origin.position,f.Position));
                        if(!support.Shape.Reach(v.vehicleRB.position+Origin.position,v.vehicleRB.rotation,i,f.Position,f.Normal))stretch++;
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
        float edge=homeZ+.425f;var floor=Box("Safety floor",new Vector3(0,399.5f,0),new Vector3(30,1,120));
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
        UnityEngine.Object.DestroyImmediate(floor);
    }
    static void RenderRates(World world,EntityVehicle v,Model.Rig rig,float homeZ)
    {
        var floor=Box("Frame near bank",new Vector3(0,399.5f,homeZ+.55f-15),new Vector3(30,1,30));
        var step=Box("Frame far bank",new Vector3(0,400.5f,homeZ+.55f+15),new Vector3(30,1,30));
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
        for(int i=0;i<8;i++){float height=.15f*(i+1);objects.Add(Box("Stair "+i,new Vector3(0,400+height*.5f,3+i*.85f+15),new Vector3(30,height,30)));}
        Reset(v);for(int i=0;i<550;i++)Tick(v,.02f,2);
        Check(Rules.DisplayName(v)+" continuous static stairs",GroundSupport.IsGrounded(v)&&v.vehicleRB.position.z>10&&v.vehicleRB.position.y>400.8f);
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
        var floor=Box("Net near bank",new Vector3(0,399.5f,homeZ+.55f-15),new Vector3(30,1,30));
        var step=Box("Net far bank",new Vector3(0,400.5f,homeZ+.55f+15),new Vector3(30,1,30));
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
            foreach(string name in new[]{Rules.VehicleName,Rules.CompleteVehicle}){
                var v=EntityFactory.CreateEntity(EntityClass.FromString(name),new Vector3(0,400,0)+Origin.position) as EntityVehicle;
                world.SpawnEntityInWorld(v);var rb=v.vehicleRB;for(var t=rb.transform;t!=null;t=t.parent)t.gameObject.SetActive(true);
                var rig=Model.GetRig(v);rig.ResetPose();var support=GroundSupport.Get(v);float homeZ=support.Shape.Home[0].z;
                report.Add("PROFILE "+name+" neutral="+support.Shape.NeutralY+" hips="+support.Shape.Hip[0]+" home="+support.Shape.Home[0]+" ankle="+support.Shape.AnkleOffset[0]);
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
                    Check(name+" automatic small step "+small+" pos="+rb.position+" vel="+rb.velocity,GroundSupport.IsGrounded(v)&&rb.position.z>6&&rb.position.y>400+small+support.Shape.NeutralY-.20f);
                    UnityEngine.Object.DestroyImmediate(obstacle);
                }
                foreach(float height in new[]{.50f,.75f,1f,1.2f,-.50f,-1f}){
                    bool down=height<0;float edge=homeZ+.425f;
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
                    UnityEngine.Object.DestroyImmediate(floor);float edge=homeZ+.425f;
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
            Log.Out("[MechaMotionQA] COMPLETE failures="+failures+" report="+path);
        }
    }
}
