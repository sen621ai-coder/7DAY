using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using PZAEC.M1;

// Controlled native-physics comparison, not a claim of client/network acceptance.
// Exact game vehicle bodies/wheels + declared motor torque; no position corrections during a trial.
public sealed class M1OffroadQA:IModApi
{
    static readonly List<string> lines=new List<string>();
    public void InitMod(Mod mod){if(Environment.GetCommandLineArgs().Contains("-m1NativeQA"))ModEvents.GameStartDone.RegisterHandler(Run);}
    static GameObject Box(string name,Vector3 p,Vector3 size){var o=new GameObject(name);o.layer=16;o.transform.position=p;o.AddComponent<BoxCollider>().size=size;return o;}
    static void Run(ref ModEvents.SGameStartDoneData data)
    {
        if(GamePrefs.GetString(EnumGamePrefs.GameName)!="M1QA_Isolated")return;
        var w=GameManager.Instance.World;bool auto=Physics.autoSimulation;float oldDt=Time.fixedDeltaTime;var oldGravity=Physics.gravity;
        var report=Path.Combine(GameIO.GetSaveGameDir(),"m1-native-report.txt");
        try{
            Physics.autoSimulation=false;Time.fixedDeltaTime=.02f;Physics.gravity=new Vector3(0,-9.81f,0);
            lines.Add("CONTROLLED PHYSICS: real vehicle shapes, native WheelCollider, fixed 20ms steps, scripted wheel throttle, no driver/network simulation");
            bool tune=Environment.GetCommandLineArgs().Contains("-m1OffroadTune");
            if(Environment.GetCommandLineArgs().Contains("-m1OffroadGuards")){
                for(int i=0;i<8;i++)Trial(w,"vehicleM1Abrams",.30f,0,0,"guard",0,false,i);
            }else if(Environment.GetCommandLineArgs().Contains("-m1OffroadFocus")){
                Trial(w,"vehicleM1Abrams",.45f,0,0,"step",15);
            }else if(Environment.GetCommandLineArgs().Contains("-m1OffroadAcceptance")){
                foreach(string name in new[]{"vehicleTruck4x4","vehicleM1Abrams"}){
                    foreach(float h in new[]{.15f,.30f,.45f,.60f,1f})for(int i=0;i<10;i++)Trial(w,name,h,0,0,"step",0,false,i);
                    foreach(float h in new[]{.30f,.45f})for(int i=0;i<10;i++)Trial(w,name,h,0,0,"step",15,false,i);
                    for(int i=0;i<10;i++)Trial(w,name,.30f,0,0,"step",0,true,i);
                    foreach(float width in new[]{.8f,1.2f})for(int i=0;i<10;i++)Trial(w,name,width,0,0,"gap",0,false,i);
                    foreach(float slope in new[]{15f,25f,30f})for(int i=0;i<10;i++)Trial(w,name,0,0,0,"slope",slope,false,i);
                }
                if(AccessTools.Method(typeof(Chassis),"ReadFrame")!=null)for(int i=0;i<8;i++)Trial(w,"vehicleM1Abrams",.30f,0,0,"guard",0,false,i);
            }else{
            foreach(string name in tune?new[]{"vehicleM1Abrams"}:new[]{"vehicleTruck4x4","vehicleM1Abrams"})
            foreach(float height in tune?new[]{0f,.30f,.60f}:new[]{0f,.15f,.30f,.45f,.60f,1f})
            foreach(float suspension in name=="vehicleM1Abrams"?new[]{.28f,.32f,.36f}:new[]{0f}){
                foreach(float mount in tune?new[]{.55f,.52f}:new[]{.615f})Trial(w,name,height,suspension,mount);
            }
            }
            lines.Add("FINISHED failures=0 (measurements, not all terrain targets passed)");
        }catch(Exception e){lines.Add("FAIL "+e);}
        finally{Physics.autoSimulation=auto;Time.fixedDeltaTime=oldDt;Physics.gravity=oldGravity;File.WriteAllLines(report,lines);foreach(var line in lines)Log.Out("[M1OffroadQA] "+line);Application.Quit();}
    }
    static void Trial(World world,string name,float height,float suspension,float mount,string scenario="step",float angle=0,bool reverse=false,int trial=0)
    {
        var origin=new Vector3(0,300,0);var ground=Box("M1QA-flat",origin+new Vector3(0,-.5f,0),new Vector3(40,1,70));
        GameObject step=height==0?null:Box("M1QA-step",origin+new Vector3(0,height*.5f,10),new Vector3(16,height,20));
        if(scenario=="gap"){
            ground.transform.position=origin+new Vector3(0,-.5f,-17.5f);ground.GetComponent<BoxCollider>().size=new Vector3(40,1,35);
            step.transform.position=origin+new Vector3(0,-.5f,17.5f+height*.5f);step.GetComponent<BoxCollider>().size=new Vector3(40,1,35-height);
        }else if(scenario=="slope"){
            step=Box("M1QA-slope",Vector3.zero,new Vector3(16,1,20));step.transform.rotation=Quaternion.Euler(-angle,0,0);step.transform.position=origin+step.transform.rotation*new Vector3(0,-.5f,10);
        }else if(step!=null&&angle!=0){step.transform.rotation=Quaternion.Euler(0,angle,0);step.transform.position=origin+step.transform.rotation*new Vector3(0,height*.5f,10);}
        EntityVehicle v=null;
        try{
            v=EntityFactory.CreateEntity(EntityClass.FromString(name),origin+Origin.position+new Vector3(0,.5f,-8)) as EntityVehicle;
            world.SpawnEntityInWorld(v);v.vehicle.SetItemValue(ItemClass.GetItem(name+"Placeable",false));v.vehicle.SetFuelLevel(100);
            var rb=v.vehicleRB;for(var t=rb.transform;t!=null;t=t.parent)t.gameObject.SetActive(true);rb.detectCollisions=true;rb.isKinematic=false;rb.useGravity=true;v.RBActive=true;v.isEntityRemote=false;v.hasDriver=true;v.IsEngineRunning=true;v.movementInput=new MovementInput();v.wheelBrakes=0;
            rb.position=origin+new Vector3((trial%3-1)*.02f,.5f,-8);rb.rotation=Quaternion.Euler(0,reverse?180:0,0);rb.velocity=Vector3.zero;rb.angularVelocity=Vector3.zero;
            var wheels=rb.GetComponentsInChildren<WheelCollider>(true);
            var frameReader=AccessTools.Method(typeof(Chassis),"ReadFrame");
            foreach(var c in rb.GetComponentsInChildren<Collider>(true))Physics.IgnoreLayerCollision(16,c.gameObject.layer,false);
            foreach(var wheel in wheels){wheel.gameObject.SetActive(true);wheel.enabled=true;}
            foreach(var wheel in wheels)if(suspension>0){wheel.suspensionDistance=suspension;var at=rb.transform.InverseTransformPoint(wheel.transform.position);at.y=mount+(suspension-.28f)*.5f;wheel.transform.position=rb.transform.TransformPoint(at);}
            Physics.SyncTransforms();
            float maxPitch=0,maxRoll=0,maxUp=0,standing=0,load=0;int grounded=0,trackSamples=0;bool success=false;string lastPhase="";
            for(int tick=0;tick<1600;tick++){
                // Settle for two seconds. Then approach at <=6 km/h using wheel torque only.
                bool driving=tick>=100;float speed=Vector3.Dot(rb.velocity,Vector3.forward);
                float target=(reverse?3f:6f)/3.6f;
                v.movementInput.moveForward=driving?(reverse?-1:1):0;
                foreach(var wheel in wheels){wheel.steerAngle=0;wheel.brakeTorque=driving&&speed>target?Mathf.Min(12000,(speed-target)*12000):0;wheel.motorTorque=driving&&speed<target?(reverse?-1:1)*(name=="vehicleTruck4x4"?3500:18000)*Mathf.Clamp01((target-speed)*2):0;}
                Chassis.Update(v);
                if(frameReader!=null){
                    var f=frameReader.Invoke(null,new object[]{v});float force=(float)AccessTools.Field(f.GetType(),"Force").GetValue(f);
                    bool supported=(bool)AccessTools.Field(f.GetType(),"Supported").GetValue(f);
                    if(force>rb.mass*.8001f||force<0||(!supported&&force>0))throw new Exception("Traction budget/support invariant failed");
                    if(scenario=="guard"&&force>1&&(int)AccessTools.Field(f.GetType(),"Tracks").GetValue(f)>0){
                        if(!rb.GetComponentsInChildren<Collider>(true).Any(c=>c.name.StartsWith("M1TrackContact")&&c.sharedMaterial.dynamicFriction<.1f))throw new Exception("Guard fixture never entered driven friction");
                        string[] guards={"brake","engine off","driver absent","fuel empty","water","remote owner","kinematic body","throttle release"};
                        switch(trial){case 0:v.wheelBrakes=1;break;case 1:v.IsEngineRunning=false;break;case 2:v.hasDriver=false;break;case 3:v.vehicle.SetFuelLevel(0);break;case 4:v.timeInWater=1;break;case 5:v.isEntityRemote=true;break;case 6:rb.isKinematic=true;break;case 7:v.movementInput.moveForward=0;break;}
                        Chassis.Update(v);var stopped=frameReader.Invoke(null,new object[]{v});
                        if((float)AccessTools.Field(stopped.GetType(),"Force").GetValue(stopped)!=0||(float)AccessTools.Field(stopped.GetType(),"PitchTorque").GetValue(stopped)!=0)throw new Exception("Immediate guard failed: "+guards[trial]);
                        foreach(var c in rb.GetComponentsInChildren<Collider>(true))if(c.name.StartsWith("M1TrackContact")&&Mathf.Abs(c.sharedMaterial.dynamicFriction-.15f)>.001f)throw new Exception("Track rest friction not restored: "+guards[trial]);
                        lines.Add("PASS immediate native guard after active traction: "+guards[trial]);success=true;break;
                    }
                }
                if(frameReader!=null&&((height==.6f&&suspension==.28f)||(angle==15&&height==.45f&&trial==0))){var f=frameReader.Invoke(null,new object[]{v});string phase=AccessTools.Field(f.GetType(),"Phase").GetValue(f).ToString();if(phase!=lastPhase||tick%100==0)lines.Add("TRACE tick="+tick+" mount="+mount+" z="+rb.position.z+" x="+rb.position.x+" "+string.Join(" ",f.GetType().GetFields().Select(x=>x.Name+"="+x.GetValue(f))));lastPhase=phase;}
                var contacts=rb.GetComponents<MonoBehaviour>().FirstOrDefault(c=>c!=null&&c.GetType().Name=="TrackContacts");
                if(contacts!=null){trackSamples=Math.Max(trackSamples,(int)AccessTools.Field(contacts.GetType(),"Count").GetValue(contacts));AccessTools.Method(contacts.GetType(),"Clear").Invoke(contacts,null);}
                Physics.Simulate(.02f);
                if(tick==99){standing=rb.position.y-origin.y;foreach(var wheel in wheels){WheelHit h;if(wheel.GetGroundHit(out h))load+=h.force;}}
                grounded=Math.Max(grounded,wheels.Count(x=>x.isGrounded));
                float pitch=Mathf.Asin(Mathf.Clamp(rb.transform.forward.y,-1,1))*Mathf.Rad2Deg;
                float roll=Mathf.Asin(Mathf.Clamp(rb.transform.right.y,-1,1))*Mathf.Rad2Deg;
                maxPitch=Mathf.Max(maxPitch,Mathf.Abs(pitch));maxRoll=Mathf.Max(maxRoll,Mathf.Abs(roll));maxUp=Mathf.Max(maxUp,rb.velocity.y);
                if(rb.position.z>6){success=true;break;}
                if(rb.position.y<origin.y-3||maxRoll>65)break;
            }
            if(grounded==0)throw new Exception("Invalid fixture: no wheel ever contacted flat ground: "+name+" wheels="+wheels.Length+" active="+rb.gameObject.activeInHierarchy+" collision="+rb.detectCollisions);
            if(scenario=="guard"&&!success)throw new Exception("Guard fixture never reached active traction: "+trial);
            lines.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,"MEASURE vehicle={0} step={1:F2} suspension={2:F2} pass={3} z={4:F2} bodyY={5:F3} pitch={6:F1} roll={7:F1} up={8:F2} wheels={9} trackContacts={10} mount={11:F3} standing={12:F3} wheelLoad={13:F0} scenario={14} angle={15} reverse={16} trial={17}",name,height,suspension,success,rb.position.z,rb.position.y-origin.y,maxPitch,maxRoll,maxUp,grounded,trackSamples,mount,standing,load,scenario,angle,reverse,trial));
        }finally{
            if(v!=null){if(v.vehicleRB!=null)v.vehicleRB.gameObject.SetActive(false);world.RemoveEntity(v.entityId,EnumRemoveEntityReason.Despawned);}
            UnityEngine.Object.DestroyImmediate(ground);if(step!=null)UnityEngine.Object.DestroyImmediate(step);
        }
    }
}
