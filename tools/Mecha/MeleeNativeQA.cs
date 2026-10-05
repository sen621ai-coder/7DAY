using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using PZAEC.Mecha;

// Runs inside the isolated native-engine fixture, never in the live mod DLL.
public static class MeleeNativeQA
{
    static readonly List<string> lines=new List<string>();
    static void Check(string name,bool ok){lines.Add((ok?"PASS ":"FAIL ")+"melee "+name);}
    static float[] Point(Vector3 p){return new[]{p.x,p.y,p.z};}
    static float[] Matrix(Matrix4x4 m){var a=new float[12];for(int row=0;row<3;row++)for(int col=0;col<4;col++)a[row*4+col]=m[row,col];return a;}
    static Vector3 snapA,snapB;static float snapFlags,snapWait;static int snapSerial;
    static bool CaptureSnapshot(int vehicle,int id,byte kind,Vector3 a,Vector3 b,float value,float c){if(kind==Samurai.Snapshot){snapA=a;snapB=b;snapFlags=value;snapWait=c;snapSerial=id;}return false;}
    static bool Client(ref bool __result){__result=false;return false;}
    public static void PlaceTarget(EntityAlive target,Vector3 contact)
    {
        // This fixture runs synchronously without a model update frame. Native
        // SetPosition moves the physics/logical root but the detached hit skeleton
        // otherwise stays at its spawn position. Put the real chest on the blade.
        var body=target.GetComponentsInChildren<Collider>().Where(c=>c.enabled&&c.tag=="E_BP_Body").OrderByDescending(c=>c.bounds.size.sqrMagnitude).First();
        target.SetPosition(target.position+contact-Origin.position-body.bounds.center);Physics.SyncTransforms();
        var model=target.emodel.GetModelTransform();if(!body.transform.IsChildOf(model))throw new Exception("QA chest is not in native model hierarchy");
        model.position+=contact-Origin.position-body.bounds.center;Physics.SyncTransforms();
    }
    public static string[] Run(World world,EntityVehicle v,Model.Rig rig,Camera camera,string output)
    {
        lines.Clear();var poses=new List<object>();var slots=AccessTools.Field(typeof(Entity),"attachedEntities");var saved=slots.GetValue(v);
        var pilot=EntityFactory.CreateEntity(EntityClass.FromString("playerMale"),v.position) as EntityPlayer;world.SpawnEntityInWorld(pilot);pilot.Health=pilot.GetMaxHealth();slots.SetValue(v,new Entity[]{pilot});pilot.AttachedToEntity=v;
        var s=Samurai.Get(v);var m=Locomotion.Get(v);float now=Time.time;var bones=rig.Mount.GetComponentInChildren<SkinnedMeshRenderer>().bones;
        try{
            m.Grounded=true;m.HoverOn=m.Boost=false;m.WingBlend=m.Blend=0;m.FlightMode=Flight.Phase.Ground;Boarding.Clear();
            Samurai.Stop(v);s.Actor=pilot.entityId;s.Alert=1;s.InputAt=now;
            Samurai.Start(s,true,now);Check("heavy release active at 140ms",!SwordMotion.DamagePhase(SwordMotion.Phase(s,now+.13f))&&SwordMotion.DamagePhase(SwordMotion.Phase(s,now+.141f)));
            Check("heavy recovery outside damage",!SwordMotion.DamagePhase(SwordMotion.Phase(s,now+.8f)));
            float maxJump=0,maxStep=0,maxElbow=0;int collisions=0,samples=0;
            foreach(int first in new[]{0,1})foreach(bool heavy in new[]{false,true})foreach(float join in new[]{.57f,.70f,.87f}){
                Samurai.Stop(v);s.Actor=pilot.entityId;s.InputAt=now;s.Alert=1;s.Combo=first==1?0:1;Samurai.Start(s,false,now);
                float at=now+Samurai.NormalDuration*join;s.Queued=true;s.QueuedHeavy=heavy;s.QueuedAt=at;s.LastSweep=at;s.PreviousPosition=v.position;
                rig.ResetPose();Samurai.Pose(v,rig,1f/60,at);var tip=SwordMotion.Tip(rig);var hand=rig.HandR.position;
                int serial=s.AttackSerial;Samurai.Advance(world,s,at);rig.ResetPose();Samurai.Pose(v,rig,1f/60,at);
                Check("chain identity and branch first="+first+" heavy="+heavy+" join="+join,s.Swing&&s.Chained&&s.Heavy==heavy&&s.AttackSerial==serial+1&&!s.Queued);
                maxJump=Mathf.Max(maxJump,Vector3.Distance(tip,SwordMotion.Tip(rig)));Check("chain grip continuous first="+first+" heavy="+heavy+" join="+join,Vector3.Distance(hand,rig.HandR.position)<.03f);
                var expected=rig.HandR.position;var expectedTip=SwordMotion.Tip(rig);var net=new Harmony("mecha.melee.snapshot.qa");
                try{net.Patch(AccessTools.Method(typeof(Weapons),"Broadcast"),prefix:new HarmonyMethod(typeof(MeleeNativeQA),nameof(CaptureSnapshot)));AccessTools.Method(typeof(Samurai),"Broadcast").Invoke(null,new object[]{s,at});net.Patch(AccessTools.PropertyGetter(typeof(Weapons),"Server"),prefix:new HarmonyMethod(typeof(MeleeNativeQA),nameof(Client)));s.Received=-1;s.Chained=false;s.StartCharge=0;Samurai.Receive(v,snapSerial,snapA,snapB,snapFlags,snapWait);rig.ResetPose();Samurai.Pose(v,rig,1f/60,Time.time);Check("chain snapshot reconstructs exact release first="+first+" heavy="+heavy+" join="+join,s.Chained&&Vector3.Distance(expected,rig.HandR.position)<.001f&&Vector3.Distance(expectedTip,SwordMotion.Tip(rig))<.001f);}
                finally{net.UnpatchSelf();s.Started=at;}
                var previousTip=SwordMotion.Tip(rig);var previousElbow=rig.Torso.InverseTransformPoint(rig.ElbowR.position);
                int count=Mathf.CeilToInt(Samurai.Duration(s)*60);string action="chain-"+first+"-"+heavy+"-"+join;
                for(int frame=0;frame<=count;frame++){
                    rig.ResetPose();Samurai.Pose(v,rig,1f/60,at+frame/60f);var p=SwordMotion.Tip(rig);var elbow=rig.Torso.InverseTransformPoint(rig.ElbowR.position);
                    if(frame>0){maxStep=Mathf.Max(maxStep,Vector3.Distance(previousTip,p));maxElbow=Mathf.Max(maxElbow,Vector3.Distance(previousElbow,elbow));}previousTip=p;previousElbow=elbow;
                    if(!SwordMotion.SelfClear(rig)){collisions++;if(collisions<30)lines.Add("CONTACT "+action+" frame="+frame);}samples++;
                    poses.Add(new{action=action,frame=frame,root=Point(rig.Mount.InverseTransformPoint(SwordMotion.Root(rig))),tip=Point(rig.Mount.InverseTransformPoint(p)),ground=300.02f-rig.Mount.position.y,bones=bones.Select(b=>Matrix(rig.Mount.worldToLocalMatrix*b.localToWorldMatrix)).ToArray()});
                    if(first==1&&join==.57f&&frame%8==0)Capture(camera,rig,Path.Combine(output,"melee-"+(heavy?"heavy":"return")+"-"+frame.ToString("000")+".png"));
                }
            }
            Check("chain release tip jump <75mm measured="+maxJump,maxJump<.075f);
            Check("chain native self clearance samples="+samples+" collisions="+collisions,collisions==0);
            Check("chain tip steps <.60m at 60Hz measured="+maxStep,maxStep<.60f);
            Check("chain elbow steps <.20m at 60Hz measured="+maxElbow,maxElbow<.20f);
            Samurai.Stop(v);Samurai.Start(s,false,now);s.Queued=true;s.QueuedAt=now;s.LastSweep=now+.9f;s.PreviousPosition=v.position;int before=s.AttackSerial;Samurai.Advance(world,s,now+.9f);Check("expired queue cannot execute",!s.Queued&&s.AttackSerial==before);
            s.Queued=true;s.QueuedAt=now+.9f;s.GuardHeld=true;Samurai.Advance(world,s,now+.9f);Check("guard discards queued attack",!s.Queued&&s.AttackSerial==before);
            s.Started=now-Samurai.NormalDuration*.60f;s.InputAt=now;s.Guarding=true;s.Energy=100;s.BrokenUntil=0;
            var front=new DamageSourceEntity(EnumDamageSource.External,EnumDamageTypes.Bashing,-1,-(Weapons.BodyRotation(v)*Vector3.forward));
            Check("guard available after braking",Mathf.Abs(Samurai.ShieldFactor(v,front,3000,false)-.3f)<.001f);
            s.Started=now-Samurai.NormalDuration*.33f;Check("guard unavailable during cut",Samurai.ShieldFactor(v,front,3000,false)==1);
            Samurai.Stop(v);Samurai.Start(s,false,now);Check("approach fades at cut end",Samurai.AttackDrive(v,now+.25f)>0&&Samurai.AttackDrive(v,now+Samurai.NormalDuration*.5f)==0);
            s.Blocked=true;Check("blocked sword stops approach",Samurai.AttackDrive(v,now+.25f)==0);
            Vector3 contact;var low=new Bounds(new Vector3(0,.2f,0),new Vector3(.6f,.4f,1.5f));
            Check("low target hit by low sweep",SwordContact.Sweep(low,null,new Vector3(-1,.2f,-1),new Vector3(-1,.2f,1),new Vector3(1,.2f,-1),new Vector3(1,.2f,1),out contact)&&contact.y<=.4f);
            Check("low target missed by overhead sweep",!SwordContact.Sweep(low,null,new Vector3(-1,1.4f,-1),new Vector3(-1,1.4f,1),new Vector3(1,1.4f,-1),new Vector3(1,1.4f,1),out contact));
            var shape=new GameObject("Melee contact QA capsule");var collider=shape.AddComponent<CapsuleCollider>();collider.height=3;collider.radius=.3f;shape.transform.position=new Vector3(0,310,0);Physics.SyncTransforms();
            Check("native tall capsule hit at actual blade height",SwordContact.Sweep(collider.bounds,collider,new Vector3(-1,311,-.5f),new Vector3(-1,311,.5f),new Vector3(1,311,-.5f),new Vector3(1,311,.5f),out contact)&&Mathf.Abs(contact.y-311)<.1f);
            UnityEngine.Object.DestroyImmediate(shape);
            var zombie=EntityFactory.CreateEntity(EntityClass.FromString("zombieBoe"),v.position+Vector3.forward*3) as EntityAlive;world.SpawnEntityInWorld(zombie);zombie.Stats.Health.BaseMax=1000000;zombie.Health=1000000;
            try{
                foreach(bool heavy in new[]{false,true}){
                    Samurai.Stop(v);s.Actor=pilot.entityId;Samurai.Start(s,heavy,now);float age=heavy?.30f:Samurai.NormalDuration*.33f;rig.ResetPose();Samurai.Pose(v,rig,.016f,now+age);var mid=(SwordMotion.Root(rig)+SwordMotion.Tip(rig))*.5f+Origin.position;PlaceTarget(zombie,mid);
                    Vector3 p;bool hit=SwordContact.Target(s,zombie,SwordMotion.Root(rig)+Origin.position,SwordMotion.Tip(rig)+Origin.position,SwordMotion.Root(rig)+Origin.position,SwordMotion.Tip(rig)+Origin.position,out p);Check("real chest collider contact heavy="+heavy,hit&&Vector3.Distance(p,mid)<.5f);
                    int health=zombie.Health;s.LastSweep=now+age-.02f;s.PreviousPosition=v.position;Samurai.Contacts(world,v,rig,now+age);Check("real chest receives once heavy="+heavy+" damage="+(health-zombie.Health),health-zombie.Health==(heavy?135000:90000));
                }
            }finally{world.RemoveEntity(zombie.entityId,EnumRemoveEntityReason.Despawned);}
        }catch(Exception e){Check("exception "+e,false);}
        finally{Samurai.Stop(v);slots.SetValue(v,saved);pilot.AttachedToEntity=null;world.RemoveEntity(pilot.entityId,EnumRemoveEntityReason.Despawned);rig.ResetPose();}
        File.WriteAllText(Path.Combine(output,"melee-poses.json"),Newtonsoft.Json.JsonConvert.SerializeObject(poses));
        lines.Add("LIMIT: scripted native action / collision fixtures, not two-machine networking or human driving.");return lines.ToArray();
    }
    static void Capture(Camera camera,Model.Rig rig,string path)
    {
        var rt=camera.targetTexture;camera.transform.position=rig.Mount.position+new Vector3(4,2.7f,5);camera.transform.LookAt(rig.Mount.position+Vector3.up*1.7f);
        // A synchronous probe has no Unity skinning frame between authored poses.
        // Bake the actual native skin so captures show this pose, not cached GPU bones.
        var skins=rig.Mount.GetComponentsInChildren<SkinnedMeshRenderer>();var enabled=skins.Select(s=>s.enabled).ToArray();var baked=new List<GameObject>();
        try{foreach(var skin in skins){var mesh=new Mesh();skin.BakeMesh(mesh);var go=new GameObject("Melee QA baked skin");go.transform.SetPositionAndRotation(skin.transform.position,skin.transform.rotation);go.transform.localScale=skin.transform.lossyScale;go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=skin.sharedMaterial;renderer.forceRenderingOff=skin.forceRenderingOff;skin.enabled=false;baked.Add(go);}camera.Render();}
        finally{for(int i=0;i<skins.Length;i++)skins[i].enabled=enabled[i];foreach(var go in baked){UnityEngine.Object.DestroyImmediate(go.GetComponent<MeshFilter>().sharedMesh);UnityEngine.Object.DestroyImmediate(go);}}
        var old=RenderTexture.active;RenderTexture.active=rt;var texture=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);RenderTexture.active=old;
    }
}

public sealed class MeleeNativeRunner:IModApi
{
    public void InitMod(Mod mod){if(Environment.GetCommandLineArgs().Contains("-mechaMotionQA")&&GetType().Assembly.GetType("MechaMotionQA")==null)ModEvents.GameStartDone.RegisterHandler(Run);}
    static bool Pause(){return false;}
    static bool Plane(EntityVehicle v,Vector3 at,ref Vector3 p,ref bool __result){p=new Vector3(at.x,300.02f+Origin.position.y,at.z);__result=true;return false;}
    static void Run(ref ModEvents.SGameStartDoneData data){if(GamePrefs.GetString(EnumGamePrefs.GameName)!="MechaQA_Isolated")return;var report=new List<string>();
        try{var h=new Harmony("mecha.melee.only");h.Patch(AccessTools.Method(typeof(Weapons),"Update"),prefix:new HarmonyMethod(typeof(MeleeNativeRunner),nameof(Pause)));h.Patch(AccessTools.Method(typeof(Gait),"Ground"),prefix:new HarmonyMethod(typeof(MeleeNativeRunner),nameof(Plane)));var world=GameManager.Instance.World;AccessTools.Field(typeof(Weapons),"currentWorld").SetValue(null,world);
            var v=EntityFactory.CreateEntity(EntityClass.FromString(Rules.CompleteVehicle),new Vector3(8,300,0)+Origin.position) as EntityVehicle;world.SpawnEntityInWorld(v);v.vehicle.SetItemValue(ItemClass.GetItem(Rules.CompleteItem,false));for(var t=v.vehicleRB.transform;t!=null;t=t.parent)t.gameObject.SetActive(true);v.vehicleRB.isKinematic=true;v.vehicleRB.position=new Vector3(8,300,0);v.vehicleRB.rotation=Quaternion.identity;v.SetPosition(v.vehicleRB.position+Origin.position);
            var camera=new GameObject("melee QA camera").AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.1f,.13f);camera.fieldOfView=38;camera.targetTexture=new RenderTexture(640,480,24);var light=new GameObject("melee QA light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.7f;light.transform.rotation=Quaternion.Euler(35,-30,0);RenderSettings.ambientLight=new Color(.65f,.65f,.65f);
            var output=Path.Combine(GameIO.GetSaveGameDir(),"mecha-motion-qa");Directory.CreateDirectory(output);report.AddRange(MeleeNativeQA.Run(world,v,Model.GetRig(v),camera,output));File.WriteAllLines(Path.Combine(output,"melee-report.txt"),report);
        }catch(Exception e){report.Add("FAIL "+e);}foreach(var line in report)Log.Out("[MeleeQA] "+line);Log.Out("[MechaMotionQA] COMPLETE failures="+report.Count(s=>s.StartsWith("FAIL")));
    }
}
