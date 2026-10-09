using System;
using UnityEngine;

namespace PZAEC.FlyingSword
{
    public sealed class EntityJuque : EntityVJeep
    {
        public string SwordId="";public int OwnerActor=-1,OwnerSlot=-1;public float Energy=1000;
        public float Throttle,Turn,Vertical;public bool Boost;public float InputAt=-100,HoldY;bool holdReady;
        public override AttachedToEntitySlotInfo GetAttachedToInfo(int slot)
        {var info=base.GetAttachedToInfo(slot);if(info!=null){info.bHolsterHeldItem=false;info.bKeep3rdPersonModelVisible=false;info.bAllow3rdPerson=true;info.yawRestriction=new Vector2(-135,135);info.pitchRestriction=new Vector2(-80,45);}return info;}
        public override void InitLocalActivationCommands(Action<EntityActivationCommand> add)
        {add(new EntityActivationCommand{commandId="use",icon="vehicle",enabled=true,commandText="御剑"});add(new EntityActivationCommand{commandId="take",icon="hand",enabled=true,commandText="收回巨阙剑"});}
        public override bool AllowActivationCommand(ReadOnlySpan<char> command,EntityPlayerLocal p){return p!=null&&p.PersistentPlayerData!=null&&(GetOwner()==null||IsOwner(p.PersistentPlayerData.PrimaryId))&&GetAttached(0)==null;}
        public override string GetActivationText(){return "巨阙剑：E 御剑 / G 收回";}
        public override void OnCollectServer(int playerId){var p=world.GetEntity(playerId) as EntityPlayer;if(p!=null)SwordRuntime.Return(p,this);}
        public override void Write(PooledBinaryWriter w,StreamModeWrite mode){base.Write(w,mode);w.Write((byte)1);w.Write(SwordId??"");w.Write(OwnerActor);w.Write(OwnerSlot);w.Write(Energy);}
        public override void Read(byte version,PooledBinaryReader r,StreamModeRead mode){base.Read(version,r,mode);if(r.ReadByte()!=1)throw new InvalidOperationException("Unsupported Juque entity data");SwordId=r.ReadString();OwnerActor=r.ReadInt32();OwnerSlot=r.ReadInt32();Energy=r.ReadSingle();}
        public override void MoveByAttachedEntity(EntityPlayerLocal p)
        {
            bool ready=SwordControls.Ready(p);var set=SwordMod.Settings;
            Throttle=ready?p.movementInput.moveForward:0;Turn=ready?p.movementInput.moveStrafe:0;
            Vertical=ready?((Input.GetKey(set.Key(set.RiseKey,KeyCode.Space))?1:0)-(Input.GetKey(set.Key(set.DescendKey,KeyCode.C))?1:0)):0;
            Boost=ready&&Input.GetKey(KeyCode.LeftShift)&&Throttle>0;InputAt=Time.time;
            p.AimingGun=ready&&p.playerInput.Secondary.IsPressed&&p.inventory.IsHoldingGun();
            SwordControls.ClampAim(p,this);
        }
        public void StepFlight(float dt)
        {
            var rb=vehicleRB;if(rb==null||rb.isKinematic||isEntityRemote||!RBActive)return;
            foreach(var wheel in rb.GetComponentsInChildren<WheelCollider>(true))wheel.enabled=false;
            var actor=GetAttached(0);bool occupied=actor!=null&&!actor.IsDead();bool fresh=occupied&&Time.time-InputAt<.5f;
            if(!holdReady){HoldY=position.y;holdReady=true;}
            var settings=SwordMod.Settings;float throttle=fresh?Mathf.Clamp(Throttle,-1,1):0,turn=fresh?Mathf.Clamp(Turn,-1,1):0,vertical=fresh?Mathf.Clamp(Vertical,-1,1):0;
            bool powered=occupied&&Energy>0;bool boost=powered&&fresh&&Boost;
            if(!powered){throttle=turn=0;vertical=-.33f;}
            float target=powered?throttle*(throttle<0?settings.Reverse:boost?settings.Boost:settings.Cruise):0;
            var forward=Vector3.ProjectOnPlane(rb.rotation*Vector3.forward,Vector3.up).normalized;
            var planar=Vector3.ProjectOnPlane(rb.velocity,Vector3.up);var desired=forward*target;
            var accel=Vector3.ClampMagnitude((desired-planar)*5,boost?60:45);
            if(Mathf.Abs(vertical)>.01f)HoldY=position.y;
            float rise=vertical>0?vertical*settings.Rise:vertical*settings.Descend;
            if(vertical==0)rise=Mathf.Clamp((HoldY-position.y)*4,-settings.Descend,settings.Rise);
            // Full rider volume is physical; sweep ahead as an additional high-speed safeguard.
            RaycastHit hit;var direction=rb.velocity.normalized;float distance=rb.velocity.magnitude*dt+.1f;
            if(rb.velocity.sqrMagnitude>.01f&&rb.SweepTest(direction,out hit,distance,QueryTriggerInteraction.Ignore)){
                if(hit.collider!=null&&!hit.collider.transform.IsChildOf(rb.transform)&&hit.distance<distance){var into=Vector3.Dot(rb.velocity,hit.normal);if(into<0)rb.velocity-=hit.normal*into;accel=Vector3.ProjectOnPlane(accel,hit.normal);}
            }
            accel.y=-Physics.gravity.y+Mathf.Clamp((rise-rb.velocity.y)*7,-40,40);accel+=rb.velocity*rb.drag;
            rb.AddForce(accel,ForceMode.Acceleration);
            float yaw=turn*(boost?70:100)*Mathf.Deg2Rad;
            var tilt=Vector3.Cross(rb.rotation*Vector3.up,Vector3.up);var rock=rb.angularVelocity-Vector3.up*rb.angularVelocity.y;
            rb.AddTorque(tilt*18-rock*7+Vector3.up*Mathf.Clamp((yaw-rb.angularVelocity.y)*9,-12,12),ForceMode.Acceleration);
        }
        public override void OnEntityActivated(EntityActivationCommand command,EntityPlayerLocal p)
        {if(command.commandId=="use")SwordRuntime.Send(p,SwordOp.Board,entityId);else if(command.commandId=="take")SwordRuntime.Send(p,SwordOp.Return,entityId);}
    }
}
