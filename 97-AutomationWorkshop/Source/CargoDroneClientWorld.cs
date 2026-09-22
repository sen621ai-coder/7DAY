using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace YFAutomation.CargoDrones
{
    public sealed class CargoVisualState
    {
        public Guid Hub;
        public CargoPoint Position;
        public CargoPhase Phase;
        public CargoHold Hold;
        public int Packages;
        public bool RestPose;
    }
    public static class CargoClientWorld
    {
        static World current;
        static Guid epoch;
        static long sequence,received=-1;
        static float nextPublish,lastReceive;
        static readonly Dictionary<Guid,CargoDroneVisual> drones=new Dictionary<Guid,CargoDroneVisual>();
        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(TileEntityComposite),"SetBlockEntityData"),postfix:new HarmonyMethod(typeof(CargoClientWorld),nameof(AttachHub)));
            harmony.Patch(AccessTools.Method(typeof(Block),"GetCollisionAABB"),postfix:new HarmonyMethod(typeof(CargoDock),nameof(CargoDock.Collision)));
            ModEvents.GameUpdate.RegisterHandler(Update);
        }
        public static void Clear()
        {
            foreach(var drone in drones.Values)if(drone!=null)UnityEngine.Object.Destroy(drone.gameObject);
            drones.Clear();current=null;epoch=Guid.Empty;sequence=0;received=-1;nextPublish=0;
        }
        static void Context(World world){if(current!=world){Clear();current=world;epoch=Guid.NewGuid();}}
        static void Update(ref ModEvents.SGameUpdateData data)
        {
            if(current!=null&&current!=GameManager.Instance?.World){Clear();return;}
            if(current!=null&&current.IsRemote()&&drones.Count>0&&Time.realtimeSinceStartup-lastReceive>10)Clear();
        }
        public static void Publish(CargoHubStatus[] states)
        {
            var world=GameManager.Instance?.World;if(world==null||world.IsRemote())return;Context(world);
            if(Time.realtimeSinceStartup<nextPublish)return;nextPublish=Time.realtimeSinceStartup+.5f;
            var packet=NetPackageManager.GetPackage<NetPackageYFCargoVisual>();packet.Epoch=epoch;packet.Sequence=++sequence;
            packet.States=states.Where(s=>s.Phase!=CargoPhase.RecoveryOnly).Select(s=>new CargoVisualState{Hub=s.Configuration.HubId,Position=s.Position,Phase=s.Phase,Hold=s.Hold,Packages=s.Packages,RestPose=CargoDock.Resting(s.Position,CargoNativeWorld.Current.Home(s.Configuration))}).ToArray();
            ConnectionManager.Instance.SendPackage(packet);
        }
        public static void Receive(NetPackageYFCargoVisual packet,World world)
        {
            if(world==null||world!=GameManager.Instance?.World||!world.IsRemote()||GameManager.IsDedicatedServer)return;
            Context(world);
            if(epoch!=packet.Epoch){foreach(var drone in drones.Values)if(drone!=null)UnityEngine.Object.Destroy(drone.gameObject);drones.Clear();epoch=packet.Epoch;received=-1;}
            if(packet.Sequence<=received)return;received=packet.Sequence;lastReceive=Time.realtimeSinceStartup;
            var alive=new HashSet<Guid>(packet.States.Select(s=>s.Hub));
            foreach(var id in drones.Keys.Where(id=>!alive.Contains(id)).ToArray()){if(drones[id]!=null)UnityEngine.Object.Destroy(drones[id].gameObject);drones.Remove(id);}
            foreach(var state in packet.States)
            {
                CargoDroneVisual visual;
                if(!drones.TryGetValue(state.Hub,out visual)||visual==null){visual=CargoDroneModel.Drone().AddComponent<CargoDroneVisual>();drones[state.Hub]=visual;}
                visual.Apply(packet.Sequence,state.Position,state.Phase,state.Hold,state.Packages,state.RestPose);
            }
        }
        public static void AttachHub(TileEntityComposite __instance,BlockEntityData __0)
        {
            if(GameManager.IsDedicatedServer||__0?.transform==null||__instance.block.GetBlockName()!=CargoRuntime.HubBlock)return;
            var root=__0.transform;if(root.Find("CargoHub")!=null)return;
            foreach(var renderer in root.GetComponentsInChildren<Renderer>())renderer.enabled=false;
            var model=CargoDroneModel.Hub();model.transform.SetParent(root,false);
            var p=__instance.ToWorldPos();model.transform.position=new Vector3(p.x+.5f,p.y,p.z+.5f)-Origin.position;
            var collider=model.AddComponent<BoxCollider>();collider.center=new Vector3(0,.5f,0);collider.size=new Vector3(1.94f,1,1.94f);
            MachineDisplay.BindInteraction(root);
        }
    }
    public sealed class NetPackageYFCargoVisual : NetPackage
    {
        public Guid Epoch;
        public long Sequence;
        public CargoVisualState[] States=new CargoVisualState[0];
        public override NetPackageDirection PackageDirection=>NetPackageDirection.ToClient;
        public override int GetLength()=>27+44*States.Length;
        public override void write(PooledBinaryWriter w)
        {
            base.write(w);w.Write(Epoch.ToByteArray());w.Write(Sequence);w.Write((byte)States.Length);
            foreach(var state in States){w.Write(state.Hub.ToByteArray());w.Write(state.Position.X);w.Write(state.Position.Y);w.Write(state.Position.Z);w.Write((byte)state.Phase);w.Write((byte)state.Hold);w.Write((byte)state.Packages);w.Write(state.RestPose);}
        }
        public override void read(PooledBinaryReader r)
        {
            Epoch=new Guid(r.ReadBytes(16));Sequence=r.ReadInt64();int count=r.ReadByte();
            if(Epoch==Guid.Empty||Sequence<0||count>16)throw new InvalidDataException("Invalid cargo visual snapshot");
            var ids=new HashSet<Guid>();States=new CargoVisualState[count];
            for(int i=0;i<count;i++)
            {
                var state=new CargoVisualState{Hub=new Guid(r.ReadBytes(16)),Position=new CargoPoint(r.ReadDouble(),r.ReadDouble(),r.ReadDouble()),Phase=(CargoPhase)r.ReadByte(),Hold=(CargoHold)r.ReadByte(),Packages=r.ReadByte(),RestPose=r.ReadBoolean()};
                if(state.Hub==Guid.Empty||!ids.Add(state.Hub)||!Enum.IsDefined(typeof(CargoPhase),state.Phase)||!Enum.IsDefined(typeof(CargoHold),state.Hold)||state.Packages>6)throw new InvalidDataException("Invalid cargo visual entry");States[i]=state;
            }
        }
        public override void ProcessPackage(World world,GameManager callbacks){CargoClientWorld.Receive(this,world);}
    }
}
