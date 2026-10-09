using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
namespace PZAEC.FlyingSword
{
    public static class SwordControls
    {
        static float nextMotion;static int previousVehicle=-1;static bool wasReady,previousFirstPerson,wavePressed;
        public static bool Ready(EntityPlayerLocal p){if(p==null||p.IsDead()||!GameManager.Instance.GameIsFocused||GameManager.Instance.IsPaused())return false;var ui=LocalPlayerUI.GetUIForPlayer(p);return ui==null||!(LocalPlayerUI.AnyModalWindowOpen()||ui.windowManager.IsCursorWindowOpen()||ui.windowManager.IsInputActive());}
        public static void Install(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(PlayerMoveController),"Update"),prefix:new HarmonyMethod(typeof(SwordControls),nameof(InputPrefix)),transpiler:new HarmonyMethod(typeof(SwordControls),nameof(InputIL)));
            h.Patch(AccessTools.Method(typeof(EntityPlayerLocal),"HolsterWeapon"),prefix:new HarmonyMethod(typeof(SwordControls),nameof(Holster)));
            h.Patch(AccessTools.Method(typeof(EntityPlayerLocal),"MoveByInput"),transpiler:new HarmonyMethod(typeof(SwordControls),nameof(AimIL)));
            h.Patch(AccessTools.Method(typeof(EntityPlayerLocal),"LateUpdate"),postfix:new HarmonyMethod(typeof(SwordControls),nameof(Camera)));
            h.Patch(AccessTools.Method(typeof(EntityPlayerLocal),"Detach"),prefix:new HarmonyMethod(typeof(SwordControls),nameof(Detach)));
            h.Patch(AccessTools.Method(typeof(Entity),"SendDetach"),prefix:new HarmonyMethod(typeof(SwordControls),nameof(SendDetach)));
            h.Patch(AccessTools.Method(typeof(EntityAlive),"LateUpdate"),postfix:new HarmonyMethod(typeof(SwordControls),nameof(Pose)));
        }
        static void InputPrefix(EntityPlayerLocal ___entityPlayerLocal){var p=___entityPlayerLocal;if(p!=null&&p.AttachedToEntity is EntityJuque){p.playerInput.Enabled=true;p.playerInput.VehicleActions.Enabled=false;p.HolsterWeapon(false);}}
        public static Entity InputAttachment(Entity e){return e is EntityJuque?null:e;}
        static IEnumerable<CodeInstruction> InputIL(IEnumerable<CodeInstruction> input){int n=0;foreach(var c in input){yield return c;if(c.opcode==OpCodes.Ldfld&&Equals(c.operand,AccessTools.Field(typeof(Entity),"AttachedToEntity"))){yield return CodeInstruction.Call(typeof(SwordControls),nameof(InputAttachment));n++;}}if(n!=3)throw new InvalidOperationException("Unsupported Juque input attachment sites: "+n);}
        public static void SetAim(EntityAlive actor,bool aiming){if(actor.AttachedToEntity is EntityJuque&&actor is EntityPlayerLocal p)actor.AimingGun=Ready(p)&&p.playerInput.Secondary.IsPressed&&p.inventory.IsHoldingGun();else actor.AimingGun=aiming;}
        static IEnumerable<CodeInstruction> AimIL(IEnumerable<CodeInstruction> input){int n=0;foreach(var c in input){if(c.Calls(AccessTools.PropertySetter(typeof(EntityAlive),"AimingGun"))){c.opcode=OpCodes.Call;c.operand=AccessTools.Method(typeof(SwordControls),nameof(SetAim));n++;}yield return c;}if(n==0)throw new InvalidOperationException("Missing native aiming setter");}
        static void Holster(EntityPlayerLocal __instance,ref bool __0){if(__instance.AttachedToEntity is EntityJuque)__0=false;}
        static bool Detach(EntityPlayerLocal __instance){var v=__instance.AttachedToEntity as EntityJuque;if(v==null||__instance.IsDead()||SwordSafety.Detaching)return true;__instance.inventory.ReleaseAll(__instance.playerInput);SwordRuntime.Send(__instance,SwordOp.Return,v.entityId);return false;}
        static bool SendDetach(Entity __instance){return !(__instance is EntityPlayerLocal p)||p.IsDead()||Detach(p);}
        static void Pose(EntityAlive __instance){if(__instance is EntityPlayer p)SwordPresentation.Pose(p);}
        public static void ClampAim(EntityPlayerLocal p,EntityJuque v){float yaw=Mathf.DeltaAngle(v.rotation.y,p.rotation.y);var rot=p.rotation;rot.y=v.rotation.y+Mathf.Clamp(yaw,-135,135);rot.x=Mathf.Clamp(Mathf.DeltaAngle(0,rot.x),-80,45);p.rotation=rot;}
        public static void Tick(World w)
        {
            var p=w.GetPrimaryPlayer();if(p==null)return;bool ready=Ready(p);var v=p.AttachedToEntity as EntityJuque;
            bool waveReady=ready&&v==null&&SwordRules.IsSword(p.inventory.holdingItemItemValue)&&SwordRules.Deployed(p.inventory.holdingItemItemValue)==0;
            if(waveReady){var secondary=p.playerInput.Secondary;if(secondary.WasPressed){SwordRuntime.Send(p,SwordOp.Charge);wavePressed=true;}
                if(wavePressed&&!secondary.IsPressed){SwordRuntime.Send(p,SwordOp.Release);wavePressed=false;}}
            else if(wavePressed){SwordRuntime.Send(p,SwordOp.Cancel);wavePressed=false;}
            if(previousVehicle<0&&v!=null){previousFirstPerson=p.bFirstPersonView;p.SetFirstPersonView(false,false);}
            if(wasReady&&!ready){SwordRuntime.Send(p,SwordOp.Cancel);p.inventory.ReleaseAll(p.playerInput);}wasReady=ready;
            if(v!=null){ClampAim(p,v);if(ready&&p.playerInput.Activate.WasPressed)SwordRuntime.Send(p,SwordOp.Return,v.entityId);
                if(ready&&Input.GetKeyDown(SwordMod.Settings.Key(SwordMod.Settings.ViewKey,KeyCode.BackQuote)))p.SetFirstPersonView(!p.bFirstPersonView,false);
                if(Time.time>=nextMotion){nextMotion=Time.time+.1f;SwordRuntime.Send(p,SwordOp.Flight,v.entityId,new Vector3(v.Throttle,v.Turn,v.Vertical),new Vector3(v.Boost?1:0,0,0));}
            }
            if(previousVehicle>=0&&v==null){p.playerInput.Enabled=true;p.playerInput.VehicleActions.Enabled=false;p.SetFirstPersonView(previousFirstPerson,false);}previousVehicle=v!=null?v.entityId:-1;
            if(!ready)return;
            if(Input.GetKeyDown(SwordMod.Settings.Key(SwordMod.Settings.DeployKey,KeyCode.G))){if(v==null){var held=p.inventory.holdingItemItemValue;if(SwordRules.IsSword(held)&&SwordRules.Deployed(held)==0)SwordRuntime.Send(p,SwordOp.Deploy);else {var near=Nearest(w,p);if(near!=null)SwordRuntime.Send(p,SwordOp.Return,near.entityId);}}}
            if(v==null&&p.playerInput.Activate.WasPressed){var near=Nearest(w,p);if(near!=null)SwordRuntime.Send(p,SwordOp.Board,near.entityId);}
        }
        static EntityJuque Nearest(World w,EntityPlayer p){EntityJuque found=null;float d=16;foreach(var e in w.Entities.list)if(e is EntityJuque v&&SwordRules.Id(p.inventory.GetItem(Mathf.Clamp(v.OwnerSlot,0,p.inventory.SlotCount-1)).itemValue)==v.SwordId){float x=(v.position-p.position).sqrMagnitude;if(x<d){found=v;d=x;}}return found;}
        public static bool UsingDeployKey(EntityPlayerLocal p){return p!=null&&Ready(p)&&Input.GetKeyDown(SwordMod.Settings.Key(SwordMod.Settings.DeployKey,KeyCode.G))&&(SwordRules.IsSword(p.inventory.holdingItemItemValue)||p.AttachedToEntity is EntityJuque||Nearest(p.world,p)!=null);}
        static void Camera(EntityPlayerLocal __instance)
        {
            var p=__instance;var v=p.AttachedToEntity as EntityJuque;if(v==null||p.playerCamera==null)return;ClampAim(p,v);SwordPresentation.Pose(p);
            // Keep the native weapon/optic FOV. Only the riding camera's position is changed.
            var q=Quaternion.Euler(-p.rotation.x,p.rotation.y,0);var anchor=p.position-Origin.position+Vector3.up*1.55f;
            var offset=CameraOffset(p.bFirstPersonView,p.AimingGun);var target=anchor+q*offset;
            RaycastHit hit;var delta=target-anchor;if(Physics.Raycast(anchor,delta.normalized,out hit,delta.magnitude,-538750997,QueryTriggerInteraction.Ignore))target=hit.point+hit.normal*.15f;
            p.playerCamera.transform.SetPositionAndRotation(target,q);
        }
        public static Vector3 CameraOffset(bool firstPerson,bool aiming){return firstPerson?Vector3.zero:aiming?new Vector3(.38f,.1f,-1.15f):new Vector3(.45f,.4f,-4.2f);}
    }
}
