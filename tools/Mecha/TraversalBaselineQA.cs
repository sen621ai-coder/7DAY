using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using PZAEC.Mecha;

// Legacy 0.12.10 only. Real PhysX, isolated world, no animation-only verdicts.
public sealed class MechaTraversalBaselineQA : IModApi
{
    public void InitMod(Mod mod) { if(Environment.GetCommandLineArgs().Contains("-mechaMotionQA")) ModEvents.GameStartDone.RegisterHandler(Run); }
    static bool Pause() { return false; }
    static void Run(ref ModEvents.SGameStartDoneData data)
    {
        if(GamePrefs.GetString(EnumGamePrefs.GameName)!="MechaQA_Isolated")return;
        var lines=new List<string>();bool simulation=Physics.autoSimulation;
        try {
            new Harmony("mecha.traversal.baseline").Patch(AccessTools.Method(typeof(Weapons),"Update"),prefix:new HarmonyMethod(typeof(MechaTraversalBaselineQA),nameof(Pause)));
            Physics.autoSimulation=false;
            var world=GameManager.Instance.World;
            foreach(string name in new[]{Rules.VehicleName,Rules.CompleteVehicle}) {
                var v=EntityFactory.CreateEntity(EntityClass.FromString(name),new Vector3(0,400,0)+Origin.position) as EntityVehicle;
                world.SpawnEntityInWorld(v);var rb=v.vehicleRB;
                for(var t=rb.transform;t!=null;t=t.parent)t.gameObject.SetActive(true);
                rb.isKinematic=false;rb.useGravity=true;rb.drag=.1f;rb.constraints=RigidbodyConstraints.None;
                foreach(float height in new[]{0f,.15f,.30f,.50f,.75f,1f,1.2f}) {
                    var floor=new GameObject("Baseline floor");floor.layer=16;floor.transform.position=new Vector3(0,399.5f,10);floor.AddComponent<BoxCollider>().size=new Vector3(30,1,50);
                    var step=new GameObject("Baseline step");step.layer=16;step.transform.position=new Vector3(0,400+height/2,15);step.AddComponent<BoxCollider>().size=new Vector3(30,Mathf.Max(.001f,height),20);
                    rb.position=new Vector3(0,400.25f,0);rb.rotation=Quaternion.identity;rb.velocity=rb.angularVelocity=Vector3.zero;Physics.SyncTransforms();
                    var wheels=rb.GetComponentsInChildren<WheelCollider>(true);
                    for(int tick=0;tick<1000;tick++) {
                        Locomotion.PrepareSupport(wheels,tick>150);
                        Locomotion.ApplyDrive(rb,Vector3.forward,tick>150?4:0,0,false,Vector3.up,.02f);
                        Physics.Simulate(.02f);
                    }
                    lines.Add(name+" height="+height+" reached="+(rb.position.z>8)+" position="+rb.position+" velocity="+rb.velocity+" nativeWheelContacts="+v.GetWheelsOnGround());
                    UnityEngine.Object.DestroyImmediate(step);UnityEngine.Object.DestroyImmediate(floor);
                }
                rb.gameObject.SetActive(false);world.RemoveEntity(v.entityId,EnumRemoveEntityReason.Despawned);
            }
        } catch(Exception e){lines.Add("FAIL "+e);}
        finally {
            Physics.autoSimulation=simulation;
            string path=Path.Combine(GameIO.GetSaveGameDir(),"mecha-traversal-baseline.txt");File.WriteAllLines(path,lines);
            foreach(var line in lines)Log.Out("[MechaBaselineQA] "+line);
            Log.Out("[MechaMotionQA] COMPLETE failures="+(lines.Any(s=>s.StartsWith("FAIL"))?1:0)+" baseline "+path);
        }
    }
}
