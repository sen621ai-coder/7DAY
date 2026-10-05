using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using PZAEC.Mecha;

// Uses ordinary game frames, with no manual Physics.Simulate/controller calls.
public sealed class MechaPassiveGroundQA : IModApi
{
    public void InitMod(Mod mod){if(Environment.GetCommandLineArgs().Contains("-mechaMotionQA"))ModEvents.GameStartDone.RegisterHandler(Run);}
    static void Run(ref ModEvents.SGameStartDoneData data){new GameObject("Mecha passive ground QA").AddComponent<PassiveGroundFrames>().Begin();}
}
public sealed class PassiveGroundFrames : MonoBehaviour
{
    EntityVehicle[] vehicles=new EntityVehicle[2];GameObject[] floors=new GameObject[2];float started;int stage,failures;List<string> lines=new List<string>();
    void Check(string label,bool ok){lines.Add((ok?"PASS ":"FAIL ")+label);if(!ok)failures++;}
    public void Begin(){started=Time.realtimeSinceStartup;var world=GameManager.Instance.World;
        for(int i=0;i<2;i++){
            var v=EntityFactory.CreateEntity(EntityClass.FromString(i==0?Rules.VehicleName:Rules.CompleteVehicle),new Vector3(i*30,400,0)+Origin.position) as EntityVehicle;vehicles[i]=v;world.SpawnEntityInWorld(v);
            v.vehicle.SetItemValue(ItemClass.GetItem(i==0?Rules.PlaceableItem:Rules.CompleteItem,false));v.vehicle.SetFuelLevel(0);v.hasDriver=false;v.IsEngineRunning=false;
            var rb=v.vehicleRB;for(var t=rb.transform;t!=null;t=t.parent)t.gameObject.SetActive(true);rb.isKinematic=false;v.RBActive=true;rb.position=new Vector3(i*30,400+GroundSupport.Get(v).Shape.NeutralY+Rules.SoleClearance,0);rb.rotation=Quaternion.identity;rb.velocity=rb.angularVelocity=Vector3.zero;v.SetPosition(rb.position+Origin.position);
            var floor=new GameObject("Unattended support floor");floors[i]=floor;floor.layer=16;floor.transform.position=new Vector3(i*30,399.5f,0);floor.AddComponent<BoxCollider>().size=new Vector3(20,1,20);
        }Physics.SyncTransforms();
    }
    void Update(){if(Time.realtimeSinceStartup-started<(stage==0?6:8))return;
        for(int i=0;i<2;i++){var v=vehicles[i];var rb=v.vehicleRB;string facts=Rules.DisplayName(v)+" root="+rb.position+" grounded="+GroundSupport.IsGrounded(v)+" active="+v.RBActive+" kinematic="+rb.isKinematic;
            if(stage==0){Check("unattended native-frame standing "+facts,GroundSupport.IsGrounded(v)&&Mathf.Abs(rb.position.y-(400+GroundSupport.Get(v).Shape.NeutralY+Rules.SoleClearance))<.05f);UnityEngine.Object.Destroy(floors[i]);}
            else {Check("unattended native-frame collapse falls "+facts,!GroundSupport.IsGrounded(v)&&rb.position.y<399);GameManager.Instance.World.RemoveEntity(v.entityId,EnumRemoveEntityReason.Despawned);}
        }
        if(stage++==0)return;
        var path=Path.Combine(GameIO.GetSaveGameDir(),"mecha-passive-ground-qa.txt");File.WriteAllLines(path,lines);foreach(var line in lines)Log.Out("[MechaPassiveGroundQA] "+line);Log.Out("[MechaMotionQA] COMPLETE failures="+failures+" report="+path);Destroy(gameObject);
    }
}
