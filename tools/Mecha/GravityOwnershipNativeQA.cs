using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using PZAEC.Mecha;

// Executes the patched native vehicle method, then one real PhysX step.
// No fake player/seat update and no replacement of the production gravity hook.
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
        if(lift){if(Rules.Complete(v))Flight.ApplyControl(v.vehicleRB,0,0,false,2,.02f);else v.vehicleRB.AddForce(Vector3.up*(9.81f+6f)*v.vehicleRB.mass,ForceMode.Force);}
        Physics.Simulate(.02f);
    }
    static void Run(ref ModEvents.SGameStartDoneData data){
        bool automatic=Physics.autoSimulation;Physics.autoSimulation=false;var world=GameManager.Instance.World;
        try{for(int i=0;i<2;i++){
            var v=EntityFactory.CreateEntity(EntityClass.FromString(i==0?Rules.VehicleName:Rules.CompleteVehicle),new Vector3(i*30,410,0)+Origin.position) as EntityVehicle;world.SpawnEntityInWorld(v);
            try{
                v.vehicle.SetItemValue(ItemClass.GetItem(i==0?Rules.PlaceableItem:Rules.CompleteItem,false));v.vehicle.SetFuelLevel(100);v.IsEngineRunning=false;v.movementInput=null;
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
        var path=Path.Combine(GameIO.GetSaveGameDir(),"mecha-gravity-qa.txt");File.WriteAllLines(path,lines);foreach(var line in lines)Log.Out("[MechaGravityQA] "+line);Log.Out("[MechaMotionQA] COMPLETE failures="+failures+" report="+path);
    }
}
