using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using PZAEC.Mecha;

// Diagnostic probe only. Never linked into the installed DLL; all edits are in
// isolated UserData. Controlled concave meshes AND actual native blast terrain.
public sealed class MechaCraterQA : IModApi
{
    sealed class Pit {public float X,Z,R,D;public Pit(float x,float z,float r,float d){X=x;Z=z;R=r;D=d;}}
    sealed class Case {public string Name;public Pit[] Pits;public float Rim,Rough;public Case(string n,float rim,float rough,params Pit[] p){Name=n;Rim=rim;Rough=rough;Pits=p;}}
    static readonly List<string> report=new List<string>();
    static readonly List<GameObject> fixtures=new List<GameObject>();
    static World world;static int failures,phase,wave;static float due,deadline;
    static readonly int[] repeats={1,4,16};
    static readonly Vector3 mapCenter=new Vector3(8,120,24);
    static Vector3 nativeStart;static float nativeYaw;static EntityVehicle launcher;static EntityPlayer observer;
    static readonly List<Vector3> shots=new List<Vector3>();
    static readonly Dictionary<Vector3i,BlockValue> beforeBlocks=new Dictionary<Vector3i,BlockValue>();
    public void InitMod(Mod mod){if(!Environment.GetCommandLineArgs().Contains("-mechaMotionQA"))return;ModEvents.GameStartDone.RegisterHandler(Ready);ModEvents.GameUpdate.RegisterHandler(Update);}
    static bool Pause(){return false;}
    static bool ObserverUpdate(EntityPlayer __instance){return __instance!=observer;}
    static void Line(string s){report.Add(s);Log.Out("[MechaCraterQA] "+s);}
    static void Check(string n,bool ok){Line((ok?"PASS ":"FAIL ")+n);if(!ok)failures++;}
    static float Height(Case c,float x,float z){
        float h=0;
        foreach(var p in c.Pits){float r=Mathf.Sqrt((x-p.X)*(x-p.X)+(z-p.Z)*(z-p.Z))/p.R;
            if(r<1)h-=p.D*.5f*(1+Mathf.Cos(Mathf.PI*r));
            if(r>.75f&&r<1.25f)h+=c.Rim*Mathf.Pow(Mathf.Sin((r-.75f)*Mathf.PI/.5f),2);
        }
        if(z>2&&z<25)h+=c.Rough*(Mathf.Sin(x*5.7f+z*3.1f)+Mathf.Sin(x*2.9f-z*4.2f))*.5f;
        return 400+h;
    }
    static GameObject Terrain(Case c){
        const int nx=160,nz=300;const float spacing=.1f;var points=new Vector3[(nx+1)*(nz+1)];var faces=new int[nx*nz*6];int k=0;
        for(int z=0;z<=nz;z++)for(int x=0;x<=nx;x++){float xx=x*spacing-8,zz=z*spacing-2;points[z*(nx+1)+x]=new Vector3(xx,Height(c,xx,zz),zz);}
        for(int z=0;z<nz;z++)for(int x=0;x<nx;x++){int a=z*(nx+1)+x,b=a+1,d=a+nx+1,e=d+1;faces[k++]=a;faces[k++]=d;faces[k++]=b;faces[k++]=b;faces[k++]=d;faces[k++]=e;}
        var mesh=new Mesh();mesh.vertices=points;mesh.triangles=faces;mesh.RecalculateNormals();mesh.RecalculateBounds();
        var go=new GameObject("Crater QA "+c.Name);go.layer=16;go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshCollider>().sharedMesh=mesh;
        var shader=Shader.Find("Standard")??Shader.Find("Unlit/Color");var material=new Material(shader);material.color=new Color(.35f,.28f,.17f);go.AddComponent<MeshRenderer>().sharedMaterial=material;
        fixtures.Add(go);Physics.SyncTransforms();return go;
    }
    static EntityVehicle Spawn(string name){
        var v=EntityFactory.CreateEntity(EntityClass.FromString(name),new Vector3(0,400,0)+Origin.position) as EntityVehicle;world.SpawnEntityInWorld(v);
        for(var t=v.vehicleRB.transform;t!=null;t=t.parent)t.gameObject.SetActive(true);Model.GetRig(v).ResetPose();GroundSupport.Get(v);return v;
    }
    static void Remove(EntityVehicle v){if(v==null)return;foreach(var c in v.transform.GetComponentsInChildren<Collider>(true))c.enabled=false;v.vehicleRB.gameObject.SetActive(false);v.transform.gameObject.SetActive(false);world.RemoveEntity(v.entityId,EnumRemoveEntityReason.Despawned);GroundSupport.Forget(v);}
    static void Reset(EntityVehicle v,Vector3 start,float yaw){
        Model.GetRig(v).ResetPose();Traversal.Forget(v);GroundSupport.Suspend(v);var support=GroundSupport.Get(v);var rb=v.vehicleRB;
        rb.isKinematic=false;rb.useGravity=true;rb.constraints=RigidbodyConstraints.None;rb.drag=.05f;
        rb.position=start-Origin.position+Vector3.up*(support.Shape.NeutralY+Rules.SoleClearance);rb.rotation=Quaternion.Euler(0,yaw,0);rb.velocity=rb.angularVelocity=Vector3.zero;
        v.SetPosition(rb.position+Origin.position);Physics.SyncTransforms();GroundSupport.Observe(v);
    }
    static void DestroyTerrain(){foreach(var go in fixtures){UnityEngine.Object.DestroyImmediate(go.GetComponent<MeshFilter>().sharedMesh);UnityEngine.Object.DestroyImmediate(go.GetComponent<MeshRenderer>().sharedMaterial);UnityEngine.Object.DestroyImmediate(go);}fixtures.Clear();Physics.SyncTransforms();}
    static void Route(EntityVehicle v,string scene,Vector3 start,float yaw,float speed,bool active,float length){
        Reset(v,start,yaw);var support=GroundSupport.Get(v);var rig=Model.GetRig(v);var forward=v.vehicleRB.rotation*Vector3.forward;
        int steps=0,attempts=0,falls=0,air=0,maxAir=0,groundFrames=0,drops=0;float stall=0,peakSlip=0,peakPen=0,lowest=9999;
        string reason="",status="TIMEOUT";float previousDistance=0;bool wasAir=false;
        const float dt=.02f;int count=Mathf.CeilToInt((length/Mathf.Min(1,Mathf.Max(.8f,speed))+12)/dt);
        for(int tick=0;tick<count;tick++){
            bool leftPlanted=support.Feet[0].Planted,rightPlanted=support.Feet[1].Planted;
            support=GroundSupport.Observe(v);var state=Traversal.Get(v);
            for(int i=0;i<2;i++)if((i==0?leftPlanted:rightPlanted)&&!support.Feet[i].Planted&&drops++<8){var f=support.Feet[i];GroundSupport.Pad pad;bool padOK=GroundSupport.PadAt(v,f.Position,v.vehicleRB.rotation,.45f,.45f,out pad);
                bool boxOK=GroundSupport.BoxClear(v,f.Position+f.Normal*.15f,f.Position+f.Normal*.15f,new Vector3(.20f,.125f,.29f),Quaternion.FromToRotation(Vector3.up,f.Normal)*v.vehicleRB.rotation);
                Line("CONTACT_DROP scene="+scene+" mech="+Rules.DisplayName(v)+" speed="+speed+" active="+active+" tick="+tick+" side="+i+" pad="+padOK+" residual="+pad.Residual+" volumeClear="+boxOK+" reach="+support.Shape.Reach(v.vehicleRB.position+Origin.position,v.vehicleRB.rotation,i,f.Position,f.Normal)+" root="+(v.vehicleRB.position+Origin.position)+" foot="+f.Position+" blocker="+GroundSupport.Blocked);}
            if(tick%5==0)state.SearchAt=-100; // 10 Hz candidates; simulated clock does not advance Unity Time.time.
            if(active&&state.Current==null&&support.Grounded&&support.Feet.All(f=>f.Planted)&&v.vehicleRB.velocity.magnitude<.45f&&stall>.35f&&tick%5==0){
                string why;var p=Traversal.Search(v,support,support.Next,out why);reason=why;
                if(p!=null&&v.vehicleRB.position.y+Origin.position.y>=Mathf.Min(support.Feet[0].Position.y,support.Feet[1].Position.y)+support.Shape.NeutralY-.025f){attempts++;if(Traversal.Begin(v,support,p,false))steps++;}
            }
            if(state.Current!=null)Traversal.Advance(v,support,state,dt);
            else{support.DesiredVelocity=forward*speed;float target=Traversal.LimitSpeed(v,support,speed,dt);GroundSupport.Walking(support,dt,true);if(!GroundSupport.MotionClear(support,dt)){GroundSupport.StopHorizontal(support);target=0;}GroundSupport.Apply(support,dt);target=Mathf.Clamp(target,-support.DriveCap,support.DriveCap);if(support.Grounded)Locomotion.ApplyDrive(v.vehicleRB,forward,target,0,speed>4&&!support.Recovering,support.Normal,dt);}
            Physics.Simulate(dt);v.SetPosition(v.vehicleRB.position+Origin.position);Physics.SyncTransforms();Locomotion.Get(v).Grounded=support.Grounded;Gait.Update(world,v,rig,dt);
            var root=v.vehicleRB.position+Origin.position;float distance=Vector3.Dot(root-start,forward);lowest=Mathf.Min(lowest,root.y);
            if(support.Grounded){groundFrames++;if(wasAir)falls++;air=0;}else{air++;maxAir=Mathf.Max(maxAir,air);}wasAir=!support.Grounded;
            for(int i=0;i<2;i++)if(support.Feet[i].Planted){var foot=(i==0?rig.FootL:rig.FootR).position+Origin.position;peakSlip=Mathf.Max(peakSlip,Vector3.Distance(foot,support.Feet[i].Position));peakPen=Mathf.Max(peakPen,support.Feet[i].Position.y-foot.y);}
            stall=distance-previousDistance<.0005f&&!Traversal.Active(v)?stall+dt:0;previousDistance=distance;
            if(tick%50==0||(Environment.GetEnvironmentVariable("MECHA_CRATER_TRACE")=="1"&&tick<200))Line("TRACE scene="+scene+" mech="+Rules.DisplayName(v)+" speed="+speed+" active="+active+" tick="+tick+" pos="+root+" vel="+v.vehicleRB.velocity+" "+GroundSupport.Diagnostics(v)+" "+Traversal.Diagnostics(v));
            if(distance>=length){status="CROSSED";break;}
            if(root.y<start.y-5){status="FELL";break;}
            if(stall>2.5f){status=support.Grounded?"STOPPED":"LOST_SUPPORT";break;}
        }
        var end=v.vehicleRB.position+Origin.position;float travelled=Vector3.Dot(end-start,forward);
        Line("RESULT scene="+scene+" mech="+Rules.DisplayName(v)+" speed="+speed+" active="+active+" status="+status+" progress="+travelled+"/"+length+" root="+end+" traversals="+steps+" attempts="+attempts+" fallingLandings="+falls+" maxAirSeconds="+maxAir*dt+" plantedFootError="+peakSlip+" solePenetration="+peakPen+" lowest="+lowest+" reason="+reason+" "+GroundSupport.Diagnostics(v)+" "+Traversal.Diagnostics(v));
        if(scene=="flat-control")Check(Rules.DisplayName(v)+" flat control speed="+speed, status=="CROSSED");
    }
    static void Controlled(){
        var cases=new[]{
            new Case("flat-control",0,0),
            new Case("shallow-dense-0.15m",.04f,.015f,new Pit(-.15f,5,1.45f,.15f),new Pit(.35f,8,1.5f,.15f),new Pit(-.4f,11,1.35f,.15f),new Pit(.2f,14,1.5f,.15f),new Pit(-.2f,17,1.5f,.15f)),
            new Case("dense-0.30m",.08f,.025f,new Pit(-.2f,5,1.25f,.3f),new Pit(.3f,7.2f,1.3f,.3f),new Pit(-.3f,9.4f,1.2f,.3f),new Pit(.2f,11.6f,1.35f,.3f),new Pit(-.2f,14,1.25f,.3f),new Pit(.1f,16.4f,1.3f,.3f)),
            new Case("overlapping-0.60m",.12f,.035f,new Pit(-.3f,5.5f,1.8f,.6f),new Pit(.4f,8,1.7f,.6f),new Pit(-.4f,10.4f,1.65f,.6f),new Pit(.2f,13.1f,1.8f,.6f),new Pit(-.2f,16,1.8f,.6f)),
            new Case("deep-steep-1.00m",.15f,.02f,new Pit(0,6,1.5f,1),new Pit(.2f,11,1.5f,1),new Pit(-.2f,16,1.5f,1)),
            new Case("wide-deep-1.50m",.15f,.025f,new Pit(0,7,2.2f,1.5f),new Pit(.3f,14,2.2f,1.5f)),
            new Case("broken-rim-0.30m",.20f,.10f,new Pit(-.3f,5,1.2f,.3f),new Pit(.35f,7,1.2f,.3f),new Pit(-.25f,9,1.2f,.3f),new Pit(.4f,11,1.2f,.3f),new Pit(-.2f,13,1.2f,.3f),new Pit(.2f,15,1.2f,.3f))
        };
        bool old=Physics.autoSimulation;Physics.autoSimulation=false;
        try{foreach(var c in cases){var filter=Environment.GetEnvironmentVariable("MECHA_CRATER_SCENE");if(!string.IsNullOrEmpty(filter)&&c.Name!=filter)continue;Terrain(c);foreach(var name in new[]{Rules.VehicleName,Rules.CompleteVehicle}){var v=Spawn(name);try{foreach(var speed in new[]{2f,4f,13.5f})foreach(var active in new[]{false,true})Route(v,c.Name,new Vector3(speed==2?-.25f:speed==4?0:.25f,400,0)+Origin.position,0,speed,active,20);}finally{Remove(v);}}DestroyTerrain();}}
        finally{Physics.autoSimulation=old;DestroyTerrain();}
        Line("LIMIT: controlled meshes reproduce concave overlapping pits; scripted controllers/PhysX, not human keys, combat AI or two-client networking.");
    }
    static bool RayTerrain(Vector3 absolute,out RaycastHit hit){
        var all=Physics.RaycastAll(new Vector3(absolute.x,220,absolute.z)-Origin.position,Vector3.down,220,~0,QueryTriggerInteraction.Ignore);
        hit=new RaycastHit();foreach(var h in all.OrderBy(h=>h.distance))if(h.collider.attachedRigidbody==null&&GameUtils.GetHitRootEntity(h.collider.tag,h.collider.transform)==null&&(h.collider.name.IndexOf("terrain",StringComparison.OrdinalIgnoreCase)>=0||h.collider.tag=="Terrain"||h.collider.name.IndexOf("chunk",StringComparison.OrdinalIgnoreCase)>=0)){hit=h;return true;}return false;
    }
    static void NativeBaseline(){
        // Select an actual loaded terrain lane, preferring a relatively level start.
        bool found=false;
        for(int x=2;x<=14&&!found;x+=2)for(int z=2;z<=10&&!found;z+=2){RaycastHit a,b,c;if(!RayTerrain(new Vector3(x,0,z),out a)||!RayTerrain(new Vector3(x-.7f,0,z+.5f),out b)||!RayTerrain(new Vector3(x+.7f,0,z+.5f),out c))continue;if(Mathf.Abs(b.point.y-c.point.y)<.15f&&a.normal.y>.95f){nativeStart=a.point+Origin.position;nativeYaw=0;found=true;}}
        Check("actual native terrain lane found",found);if(!found){foreach(var h in Physics.RaycastAll(new Vector3(8,220,8)-Origin.position,Vector3.down,220,~0,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance))Line("NATIVE RAY collider="+h.collider.name+" tag="+h.collider.tag+" type="+h.collider.GetType()+" layer="+h.collider.gameObject.layer+" point="+h.point);Line("NATIVE COLLIDERS "+string.Join("; ",UnityEngine.Object.FindObjectsOfType<MeshCollider>().Take(20).Select(c=>c.name+" tag="+c.tag+" pos="+c.transform.position+" bounds="+c.bounds)));Finish();return;}
        foreach(var name in new[]{Rules.VehicleName,Rules.CompleteVehicle}){var v=Spawn(name);bool old=Physics.autoSimulation;Physics.autoSimulation=false;try{foreach(var speed in new[]{2f,4f,13.5f})foreach(var active in new[]{false,true})Route(v,"native-before-blasts",nativeStart,nativeYaw,speed,active,24);}finally{Physics.autoSimulation=old;Remove(v);}}
        launcher=Spawn(Rules.VehicleName);launcher.vehicleRB.isKinematic=true;launcher.vehicleRB.position=new Vector3(50,400,50);
        for(int i=0;i<7;i++){var q=nativeStart+new Vector3(i%2==0?-.35f:.35f,0,4.5f+i*2.1f);RaycastHit hit;if(RayTerrain(q,out hit)){var at=hit.point+Origin.position;shots.Add(at);for(int dx=-3;dx<=3;dx++)for(int dz=-3;dz<=3;dz++)for(int dy=-4;dy<=1;dy++){var p=new Vector3i(Mathf.FloorToInt(at.x)+dx,Mathf.FloorToInt(at.y)+dy,Mathf.FloorToInt(at.z)+dz);if(!beforeBlocks.ContainsKey(p))beforeBlocks[p]=world.GetBlock(p);}}}
        Check("native bombardment has seven terrain targets",shots.Count==7);
        wave=0;Bombard();
    }
    static void Bombard(){
        // Repeated shipped prototype missile explosions: 40 block damage / 3m radius.
        if(launcher==null){launcher=Spawn(Rules.VehicleName);launcher.vehicleRB.isKinematic=true;launcher.vehicleRB.position=new Vector3(50,400,50);}
        int extra=repeats[wave]-(wave==0?0:repeats[wave-1]);
        for(int repeat=0;repeat<extra;repeat++)foreach(var at in shots){var blast=(ExplosionData)AccessTools.Method(typeof(Weapons),"BuildMissileExplosion").Invoke(null,new object[]{launcher,0});blast.EntityDamage=0;blast.BlastPower=0;blast.ParticleIndex=0;
            GameManager.Instance.ExplosionServer(at,new Vector3i(at),Quaternion.identity,blast,-1,0,false,ItemClass.GetItem(Rules.MissileAmmo,false));}
        Line("BOMBARDMENT actual native ExplosionServer calls this wave="+shots.Count*extra+" cumulative="+shots.Count*repeats[wave]+" shipped missile BlockDamage="+Rules.MissileBlockDamage+" BlockRadius="+Rules.MissileBlockRadius+" (entity damage and visual particles disabled for this terrain probe)");
        Remove(launcher);launcher=null;phase=3;due=Time.realtimeSinceStartup+12;
    }
    static void NativeAfter(){
        int changed=0,removed=0;foreach(var p in beforeBlocks){var value=world.GetBlock(p.Key);if(value.rawData!=p.Value.rawData)changed++;if(p.Value.type!=0&&value.type==0)removed++;}
        Line("NATIVE BLOCKS cumulative="+shots.Count*repeats[wave]+" changed="+changed+" removed="+removed);if(wave==repeats.Length-1)Check("native bombardment actually removed terrain",removed>0);
        foreach(var at in shots){RaycastHit h;Line("NATIVE PIT target="+at+" floor="+(RayTerrain(at,out h)?(h.point+Origin.position).ToString():"missing")+" depth="+(h.collider!=null?at.y-h.point.y-Origin.position.y:999));}
        bool old=Physics.autoSimulation;Physics.autoSimulation=false;
        try{foreach(var name in new[]{Rules.VehicleName,Rules.CompleteVehicle}){var v=Spawn(name);try{foreach(var speed in new[]{2f,4f,13.5f})foreach(var active in new[]{false,true})Route(v,"native-after-"+shots.Count*repeats[wave]+"-missiles",nativeStart,nativeYaw,speed,active,24);}finally{Remove(v);}}}
        finally{Physics.autoSimulation=old;}
        if(++wave<repeats.Length)Bombard();else Finish();
    }
    static void Ready(ref ModEvents.SGameStartDoneData data){if(GamePrefs.GetString(EnumGamePrefs.GameName)!="MechaQA_Isolated")return;world=GameManager.Instance.World;
        new Harmony("mecha.crater.qa").Patch(AccessTools.Method(typeof(Weapons),"Update"),prefix:new HarmonyMethod(typeof(MechaCraterQA),nameof(Pause)));
        try{if(Environment.GetEnvironmentVariable("MECHA_CRATER_NATIVE_ONLY")!="1")Controlled();if(Environment.GetEnvironmentVariable("MECHA_CRATER_CONTROLLED_ONLY")=="1"){Finish();return;}
            // Native terrain meshing/lighting gets Players.Count * 2 chunks per tick.
            // An observer without a player leaves the collision chunks uninitialized.
            observer=EntityFactory.CreateEntity(EntityClass.FromString("playerMale"),mapCenter) as EntityPlayer;observer.MinEventContext.ItemValue=ItemValue.None;world.SpawnEntityInWorld(observer);
            new Harmony("mecha.crater.qa.observer").Patch(AccessTools.Method(typeof(EntityPlayer),"OnUpdateEntity"),prefix:new HarmonyMethod(typeof(MechaCraterQA),nameof(ObserverUpdate)));
            GameManager.Instance.AddChunkObserver(mapCenter,false,4,4);phase=1;deadline=Time.realtimeSinceStartup+150;}catch(Exception e){Check("probe exception "+e,false);Finish();}}
    static void Update(ref ModEvents.SGameUpdateData data){try{if(phase==1){if(!world.IsChunkAreaLoaded(mapCenter)){if(Time.realtimeSinceStartup>deadline){Check("native chunk loading timed out",false);Finish();}return;}phase=2;due=Time.realtimeSinceStartup+10;}
        else if(phase==2&&Time.realtimeSinceStartup>=due)NativeBaseline();else if(phase==3&&Time.realtimeSinceStartup>=due)NativeAfter();}catch(Exception e){Check("native probe exception "+e,false);Finish();}}
    static void Finish(){phase=0;Line("DIAGNOSTIC: failures counts control/setup/exception checks only. Terrain acceptance is each RESULT status; COMPLETE is not a traversability pass.");var path=Path.Combine(GameIO.GetSaveGameDir(),"mecha-crater-qa.txt");File.WriteAllLines(path,report);Log.Out("[MechaMotionQA] COMPLETE failures="+failures+" report="+path);}
}
