using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using UnityEngine;
using PZAEC.Mecha;

// Same source compiles against the pre-change backup and the current mod.
// Measures production gait/contact CPU and synchronous native rendering, not
// overall human-client FPS. The latter still needs the same saved game scene.
public sealed class MechaPresentationPerfQA:IModApi
{
    public void InitMod(Mod mod){if(Environment.GetCommandLineArgs().Contains("-mechaMotionQA"))ModEvents.GameStartDone.RegisterHandler(Run);}
    static bool Plane(EntityVehicle v,Vector3 at,ref Vector3 p,ref bool __result){p=new Vector3(at.x,300.02f+Origin.position.y,at.z);__result=true;return false;}
    static bool Disable(){return false;}
    static void Run(ref ModEvents.SGameStartDoneData data)
    {
        if(GamePrefs.GetString(EnumGamePrefs.GameName)!="MechaQA_Isolated")return;
        var folder=Path.Combine(GameIO.GetSaveGameDir(),"mecha-presentation-perf");Directory.CreateDirectory(folder);var rows=new List<string>{"case,samples,cpu_mean_ms,cpu_p95_ms,render_mean_ms,render_p95_ms,total_mean_ms,total_p95_ms"};int failures=0;
        try{
            var world=GameManager.Instance.World;AccessTools.Field(typeof(Weapons),"currentWorld").SetValue(null,world);
            var harmony=new Harmony("mecha.presentation.perf.qa");harmony.Patch(AccessTools.Method(typeof(Gait),"Ground"),prefix:new HarmonyMethod(typeof(MechaPresentationPerfQA),nameof(Plane)));harmony.Patch(AccessTools.Method(typeof(Weapons),"Update"),prefix:new HarmonyMethod(typeof(MechaPresentationPerfQA),nameof(Disable)));
            var vehicles=new EntityVehicle[2];var rigs=new Model.Rig[2];
            for(int i=0;i<2;i++){
                var v=EntityFactory.CreateEntity(EntityClass.FromString(i==0?Rules.CompleteVehicle:Rules.VehicleName),new Vector3(i*4,300,0)+Origin.position) as EntityVehicle;world.SpawnEntityInWorld(v);v.vehicle.SetItemValue(ItemClass.GetItem(i==0?Rules.CompleteItem:Rules.PlaceableItem,false));vehicles[i]=v;for(var t=v.vehicleRB.transform;t!=null;t=t.parent)t.gameObject.SetActive(true);v.vehicleRB.isKinematic=true;v.vehicleRB.position=new Vector3(i*4,300,0);v.vehicleRB.rotation=Quaternion.identity;v.SetPosition(v.vehicleRB.position+Origin.position);rigs[i]=Model.GetRig(v);Locomotion.Get(v).Grounded=true;
            }
            var pilot=EntityFactory.CreateEntity(EntityClass.FromString("playerMale"),vehicles[0].position) as EntityPlayer;world.SpawnEntityInWorld(pilot);AccessTools.Field(typeof(Entity),"attachedEntities").SetValue(vehicles[0],new Entity[]{pilot});pilot.AttachedToEntity=vehicles[0];Samurai.Get(vehicles[0]).Actor=pilot.entityId;
            var camera=new GameObject("QA Performance Camera").AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.1f,.13f);camera.fieldOfView=60;camera.transform.position=new Vector3(1.7f,302.5f,-6);camera.transform.LookAt(new Vector3(1.7f,301.6f,0));var rt=new RenderTexture(1280,720,24);camera.targetTexture=rt;var pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
            var light=new GameObject("QA Performance Light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.5f;light.transform.rotation=Quaternion.Euler(35,-30,0);RenderSettings.ambientLight=new Color(.65f,.65f,.65f);
            var feedback=typeof(Model).Assembly.GetType("PZAEC.Mecha.CombatFeedback");Action<World> update=feedback==null?null:(Action<World>)Delegate.CreateDelegate(typeof(Action<World>),feedback.GetMethod("Update"));
            foreach(string mode in new[]{"idle","sword-60Hz","sword-400ms","contacts-16"}){
                Samurai.Stop(vehicles[0]);if(feedback!=null)feedback.GetMethod("Clear").Invoke(null,null);
                if(mode=="contacts-16"&&feedback!=null)for(int i=0;i<16;i++)feedback.GetMethod("Receive").Invoke(null,new object[]{world,vehicles[0].entityId,i,(byte)17,vehicles[0].position+new Vector3((i%4-1.5f)*.2f,1.8f,(i/4)*.12f),Vector3.back,10f,1f});
                var cpu=new List<double>();var render=new List<double>();var total=new List<double>();var watch=new Stopwatch();
                for(int frame=0;frame<360;frame++){
                    var s=Samurai.Get(vehicles[0]);s.Swing=mode.StartsWith("sword");s.Heavy=false;s.Combo=1;s.Started=Time.time-Samurai.NormalDuration*(.19f+(frame%72)/72f*.5f);s.LastCombat=Time.time;s.LastSweep=Time.time-(mode=="sword-400ms"?.4f:1f/60);s.PreviousPosition=vehicles[0].position;
                    watch.Restart();for(int i=0;i<2;i++)Gait.Update(world,vehicles[i],rigs[i],1f/60);Samurai.Contacts(world,vehicles[0],rigs[0],Time.time);if(update!=null)update(world);watch.Stop();double a=watch.Elapsed.TotalMilliseconds;
                    watch.Restart();camera.Render();var old=RenderTexture.active;RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0,false);RenderTexture.active=old;watch.Stop();double b=watch.Elapsed.TotalMilliseconds;
                    if(frame>=60){cpu.Add(a);render.Add(b);total.Add(a+b);}
                }
                rows.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,"{0},{1},{2:F6},{3:F6},{4:F6},{5:F6},{6:F6},{7:F6}",mode,cpu.Count,cpu.Average(),P95(cpu),render.Average(),P95(render),total.Average(),P95(total)));
            }
            File.WriteAllLines(Path.Combine(folder,"timings.csv"),rows);File.WriteAllText(Path.Combine(folder,"scope.txt"),"Two native mecha, same 1280x720 scene, 60 warmup + 300 samples per case. CPU: production gait/contact/feedback. Render: native Camera.Render + synchronous readback. No normal player Update/input/network/audio/HUD; not overall saved-game frame budget acceptance.");
            foreach(var line in rows)Log.Out("[MechaPerfQA] "+line);
        }catch(Exception ex){failures++;File.WriteAllText(Path.Combine(folder,"failure.txt"),ex.ToString());Log.Error("[MechaPerfQA] "+ex);}
        finally{Log.Out("[MechaMotionQA] COMPLETE failures="+failures);Application.Quit();}
    }
    static double P95(List<double> data){var a=data.OrderBy(x=>x).ToArray();return a[(int)Math.Ceiling(a.Length*.95)-1];}
}
