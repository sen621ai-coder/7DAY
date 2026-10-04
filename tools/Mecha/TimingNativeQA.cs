using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using PZAEC.Mecha;

// Actual frame-clock / delayed packet replay in the isolated game, not aged AirSince.
public sealed class MechaTimingQA : MonoBehaviour
{
    sealed class Packet {public float Due;public byte Op;public Vector3 A,B;public int Sequence;}
    static readonly Queue<Packet> packets=new Queue<Packet>();
    static EntityVehicle vehicle;static EntityPlayer pilot;static int sequence,events;static float serverAir=-1,serverGround=-1;static Vector3 acknowledgement;static int ackSerial;
    static bool Capture(EntityVehicle vehicle,byte op,Vector3 direction,Vector3 origin)
    {if(vehicle==MechaTimingQA.vehicle)packets.Enqueue(new Packet{Due=Time.time+.08f,Op=op,A=direction,B=origin,Sequence=++sequence});return false;}
    static bool Broadcast(int vehicle,int id,byte kind,Vector3 a){if(MechaTimingQA.vehicle!=null&&vehicle==MechaTimingQA.vehicle.entityId){if(kind==Weapons.LandingEvent)events++;if(kind==Samurai.CancelAck){acknowledgement=a;ackSerial=id;}}return false;}
    static bool Audible(ref bool __result){__result=true;return false;}
    List<string> report;int failures;string folder;
    void Check(string name,bool ok){report.Add((ok?"PASS ":"FAIL ")+"frame timing: "+name);if(!ok)failures++;}
    public void Begin(List<string> existing,int failed,string output){report=existing;failures=failed;folder=output;StartCoroutine(Run());}
    void Deliver(World world)
    {
        while(packets.Count>0&&packets.Peek().Due<=Time.time){var p=packets.Dequeue();var move=Locomotion.Get(vehicle);bool ground=move.Grounded;
            Weapons.Request(world,pilot.entityId,vehicle.entityId,p.Op,p.A,p.B,p.Sequence);
            if(ground&&!move.Grounded)serverAir=Time.time;if(!ground&&move.Grounded)serverGround=Time.time;
        }
    }
    IEnumerator Run()
    {
        var world=GameManager.Instance.World;var h=new Harmony("mecha.frame.timing.qa");
        h.Patch(AccessTools.Method(typeof(Weapons),"SendLocalIntent"),prefix:new HarmonyMethod(typeof(MechaTimingQA),nameof(Capture)));
        h.Patch(AccessTools.Method(typeof(Weapons),"Broadcast"),prefix:new HarmonyMethod(typeof(MechaTimingQA),nameof(Broadcast)));
        h.Patch(AccessTools.PropertyGetter(typeof(RobotAudio),"Audible"),prefix:new HarmonyMethod(typeof(MechaTimingQA),nameof(Audible)));
        bool completed=false;try{
            // Let the synchronous geometry capture frame finish before measuring intervals.
            yield return new WaitForSeconds(.3f);
            foreach(string name in new[]{Rules.VehicleName,Rules.CompleteVehicle}){
                vehicle=EntityFactory.CreateEntity(EntityClass.FromString(name),new Vector3(80,300,0)+Origin.position) as EntityVehicle;world.SpawnEntityInWorld(vehicle);
                vehicle.vehicle.SetItemValue(ItemClass.GetItem(name==Rules.VehicleName?Rules.PlaceableItem:Rules.CompleteItem,false));vehicle.vehicleRB.isKinematic=true;vehicle.isEntityRemote=true;vehicle.hasDriver=true;vehicle.IsEngineRunning=true;
                pilot=EntityFactory.CreateEntity(EntityClass.FromString("playerMale"),vehicle.position) as EntityPlayer;world.SpawnEntityInWorld(pilot);pilot.AttachedToEntity=vehicle;
                AccessTools.Field(typeof(Entity),"attachedEntities").SetValue(vehicle,new Entity[]{pilot});
                var remote=Locomotion.Get(vehicle);var owner=new Locomotion.MoveState{Vehicle=vehicle};
                events=0;sequence=0;packets.Clear();Locomotion.Sync(vehicle,owner);yield return new WaitForSeconds(.025f);
                owner.Grounded=false;owner.AirSince=Time.time;owner.JumpAt=Time.time;float departure=Time.time;Locomotion.Sync(vehicle,owner);
                Check(name+" takeoff snapshot bypasses fresh 0.2s throttle",packets.Count==2);
                while(Time.time-departure<.6f){Locomotion.Sync(vehicle,owner);Deliver(world);yield return null;}
                owner.Grounded=true;bool stomp=Locomotion.MarkLanding(owner,Time.time,true);float arrival=Time.time;Locomotion.Sync(vehicle,owner);
                if(stomp)Weapons.SendLocalIntent(vehicle,Weapons.Stomp,Vector3.down*owner.LandingStrength,vehicle.position);
                while(packets.Count>0){Deliver(world);yield return null;}
                Check(name+" short jump survives delayed Motion / Stomp path owner="+(arrival-departure)+" server="+(serverGround-serverAir),stomp&&events==1&&serverGround-serverAir>=Rules.StompAirborneSeconds);
                Weapons.SendLocalIntent(vehicle,Weapons.Stomp,Vector3.down*.8f,vehicle.position);
                while(packets.Count>0){Deliver(world);yield return null;}
                Check(name+" duplicate delayed stomp remains rejected",events==1);
                // A contact whose heavy event never arrives must get one light fallback.
                if(Rules.Complete(vehicle)){
                    var combat=Samurai.Get(vehicle);var weapon=Weapons.GetState(vehicle);weapon.TriggerHeld=true;combat.LaserCharge=.7f;int beforeSerial=combat.Serial;
                    Samurai.Request(vehicle,pilot.entityId,1000,Samurai.Cancel,new Vector3(123,0,0),Vector3.zero,Time.time);
                    var lease=(Weapons.TriggerLease)AccessTools.Method(typeof(Weapons),"GetLease").Invoke(null,new object[]{vehicle.entityId});
                    Check("server cancellation stops laser and rejects pre-cancel fire lease",!weapon.TriggerHeld&&combat.LaserCharge==0&&!lease.Active(Time.time)&&!lease.Accept(pilot.entityId,999,true,Time.time));
                    Check("server acknowledges exact token and actor in snapshot serial stream",acknowledgement.x==123&&((int)acknowledgement.y|((int)acknowledgement.z<<16))==pilot.entityId&&ackSerial==beforeSerial+1);
                }
                var rig=Model.GetRig(vehicle);Gait.Update(world,vehicle,rig,.02f);yield return null;remote.Grounded=true;remote.FlightMode=Flight.Phase.Ground;remote.LandingAt=Time.time;remote.LandingEventExpected=true;
                int count=RobotAudio.PlayedCueCount;float contact=Time.time;Gait.Update(world,vehicle,rig,.02f);
                while(Time.time-contact<.86f){yield return null;Gait.Update(world,vehicle,rig,Time.deltaTime);}
                int landCount=0;foreach(var cue in RobotAudio.RecentCues())if(cue.Vehicle==vehicle.entityId&&cue.Cue=="land"&&cue.At>=contact)landCount++;
                Check(name+" absent authority event gets exactly one timed light fallback",landCount==1&&RobotAudio.PlayedCueCount>count);
                pilot.AttachedToEntity=null;AccessTools.Field(typeof(Entity),"attachedEntities").SetValue(vehicle,new Entity[0]);world.RemoveEntity(pilot.entityId,EnumRemoveEntityReason.Despawned);world.RemoveEntity(vehicle.entityId,EnumRemoveEntityReason.Despawned);vehicle=null;pilot=null;
            }
            completed=true;
        }finally{
            if(!completed)Check("coroutine completed without exception",false);
            h.UnpatchSelf();if(vehicle!=null){if(pilot!=null)pilot.AttachedToEntity=null;world.RemoveEntity(vehicle.entityId,EnumRemoveEntityReason.Despawned);}if(pilot!=null)world.RemoveEntity(pilot.entityId,EnumRemoveEntityReason.Despawned);
            report.Add("LIMITATION: delayed packets run in one native process using real frame time; not a two-computer network test.");
            report.Add("COMPLETE failures="+failures);File.WriteAllLines(Path.Combine(folder,"report.txt"),report);foreach(var line in report)Log.Out("[MechaMotionQA] "+line);Application.Quit();
        }
    }
}
