using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace AECT16RuntimeFix
{
    // Confirmed-shot feedback only. No damage, ammo, aim input, guidance or MD500 changes.
    public static class ApacheFiringFeedback
    {
        private sealed class Pulse
        {
            public bool HasSequence;
            public int Sequence;
            public float LastShot, CameraEnergy;
            public bool HasCannonShot;
            public float LastCannonShot;
            public bool Recoiling;
            public int Pilot;
            public float RecoilStarted, RecoilAge, NextRecoil, RecoilScale;
            public Vector3 RecoilAxis;
        }
        private static ConditionalWeakTable<EntityVehicle,Pulse> pulses = new ConditionalWeakTable<EntityVehicle,Pulse>();
        private static Camera shiftedCamera;
        private static Vector3 baseLocalPosition, appliedLocalPosition, localOffset;
        private static Quaternion baseLocalRotation, appliedLocalRotation, rollOffset;
        private static bool cameraRolled;
        private static bool enabled;

        public static void Install(Harmony harmony)
        {
            if(enabled)return;
            try
            {
                harmony.Patch(AccessTools.Method(typeof(EntityVehicle),"FixedUpdateForces"),
                    postfix:new HarmonyMethod(typeof(ApacheFiringFeedback),nameof(AfterForces)));
                enabled=true;
            }
            catch(Exception ex){Log.Warning("[Apache-Feedback] Body feedback disabled: "+ex.GetBaseException().Message);}
        }

        private static bool CanRecoil(EntityVehicle v)
        {
            return enabled && ApacheWeapons.IsApache(v) && MD500FlightControls.Applies(v.vehicle) &&
                !v.isEntityRemote && v.RBActive && v.vehicleRB!=null && !v.vehicleRB.isKinematic &&
                v.hasDriver && v.GetAttached(0)!=null && v.IsEngineRunning && !v.IsDead() &&
                v.vehicle.GetHealth()>0 && v.timeInWater<=0 && v.GetWheelsOnGround()==0 &&
                (v.vehicle.GetFuelLevel()>0 || EntityVehicle.VehicleFuelUsageModifier==0) &&
                (v.vehicleRB.rotation*Vector3.up).y>=.25f;
        }

        public static void Receive(EntityVehicle vehicle,int sequence,byte kind,Vector3 origin,Vector3 vector,float guided)
        {
            if(!ApacheWeapons.IsApache(vehicle) ||
                (kind!=ApacheWeapons.CannonEvent && kind!=ApacheWeapons.RocketEvent))return;
            if(!Finite(origin)||!Finite(vector)||!ApacheWeaponRules.Finite(guided))return;
            var pulse=pulses.GetValue(vehicle,key=>new Pulse());
            if(pulse.HasSequence && unchecked(sequence-pulse.Sequence)<=0)return;
            pulse.HasSequence=true;pulse.Sequence=sequence;
            pulse.LastShot=Time.time;
            // Replace the envelope on every confirmed shot; never sum pulses.
            pulse.CameraEnergy=kind==ApacheWeapons.CannonEvent?.65f:guided>0?.22f:.4f;
            if(kind==ApacheWeapons.CannonEvent){pulse.HasCannonShot=true;pulse.LastCannonShot=Time.time;}
            // One non-retriggerable slot. Cannon fire cannot start, refresh or amplify it.
            if(kind!=ApacheWeapons.RocketEvent || !CanRecoil(vehicle) || pulse.Recoiling || Time.time<pulse.NextRecoil)return;
            pulse.Recoiling=true;pulse.Pilot=vehicle.GetAttached(0).entityId;
            pulse.RecoilStarted=Time.time;pulse.RecoilAge=0;
            pulse.NextRecoil=Time.time+ApacheFeedbackMath.RecoilCooldown;
            pulse.RecoilScale=guided>0?.55f:1f;
            // Deliberate tiny gameplay nose lift, without translation or yaw torque.
            pulse.RecoilAxis=Vector3.ProjectOnPlane(vehicle.vehicleRB.rotation*new Vector3(-1,0,0),Vector3.up);
        }

        public static void AfterForces(EntityVehicle __instance)
        {
            if(__instance==null || !pulses.TryGetValue(__instance,out var pulse) || !pulse.Recoiling)return;
            float dt=Time.fixedDeltaTime,age=Time.time-pulse.RecoilStarted;
            if(!CanRecoil(__instance) || __instance.GetAttached(0).entityId!=pulse.Pilot ||
                age<0 || age>ApacheFeedbackMath.RecoilDuration+.1f ||
                !ApacheWeaponRules.Finite(dt) || dt<=0 || dt>.1f)
            {pulse.Recoiling=false;return;}
            float acceleration=ApacheFeedbackMath.RecoilStep(pulse.RecoilAge,dt)*pulse.RecoilScale;
            __instance.vehicleRB.AddTorque(pulse.RecoilAxis*acceleration,ForceMode.Acceleration);
            pulse.RecoilAge+=dt;
            if(pulse.RecoilAge>=ApacheFeedbackMath.RecoilDuration)pulse.Recoiling=false;
        }

        private static bool Finite(Vector3 v)
        {return ApacheWeaponRules.Finite(v.x)&&ApacheWeaponRules.Finite(v.y)&&ApacheWeaponRules.Finite(v.z);}

        public static void RestoreCamera()
        {
            if(shiftedCamera!=null && (shiftedCamera.transform.localPosition-appliedLocalPosition).sqrMagnitude<.00000001f)
                shiftedCamera.transform.localPosition=baseLocalPosition;
            if(shiftedCamera!=null && cameraRolled && SameRotation(shiftedCamera.transform.localRotation,appliedLocalRotation))
                shiftedCamera.transform.localRotation=baseLocalRotation;
            shiftedCamera=null;localOffset=Vector3.zero;cameraRolled=false;
        }

        private static bool SameRotation(Quaternion a,Quaternion b)
        {
            // Component distance resolves tiny rolls below Quaternion.Angle's float precision.
            float sign=Quaternion.Dot(a,b)<0?-1:1;
            float x=a.x-sign*b.x,y=a.y-sign*b.y,z=a.z-sign*b.z,w=a.w-sign*b.w;
            return x*x+y*y+z*z+w*w<1e-12f;
        }

        public static void UpdateCamera(EntityPlayerLocal player,bool usable,bool aiming)
        {
            RestoreCamera();
            if(!usable || player?.playerCamera==null)return;
            var vehicle=player.AttachedToEntity as EntityVehicle;
            if(!ApacheWeapons.IsApache(vehicle)||!pulses.TryGetValue(vehicle,out var pulse))return;
            int seat=ApacheWeapons.Seat(vehicle,player.entityId);
            if(seat<0)return;
            if(aiming)
            {
                // Gunner-only visual roll. It neither moves the cursor nor drives the turret.
                if(seat!=1 || !pulse.HasCannonShot)return;
                float roll=ApacheFeedbackMath.AimRoll(Time.time-pulse.LastCannonShot);
                if(roll==0)return;
                shiftedCamera=player.playerCamera;
                baseLocalPosition=appliedLocalPosition=shiftedCamera.transform.localPosition;
                baseLocalRotation=shiftedCamera.transform.localRotation;
                rollOffset=Quaternion.Euler(0,0,roll);
                appliedLocalRotation=baseLocalRotation*rollOffset;
                shiftedCamera.transform.localRotation=appliedLocalRotation;cameraRolled=true;
                return;
            }
            float age=Time.time-pulse.LastShot;
            if(age<0||age>ApacheFeedbackMath.Duration)return;
            float energy=ApacheFeedbackMath.CameraEnvelope(pulse.CameraEnergy,age);
            float amplitude=ApacheFeedbackMath.CameraScale*energy*(seat==0?.45f:1f);
            // Camera presentation only; no view rotation or cursor motion.
            localOffset=new Vector3((float)Math.Sin(age*105)*.55f,(float)Math.Sin(age*83),0)*amplitude;
            shiftedCamera=player.playerCamera;baseLocalPosition=shiftedCamera.transform.localPosition;
            appliedLocalPosition=baseLocalPosition+localOffset;
            shiftedCamera.transform.localPosition=appliedLocalPosition;
        }

        public static Ray StabilizeRay(Camera camera,Ray ray)
        {
            if(camera!=null && camera==shiftedCamera && cameraRolled &&
                SameRotation(camera.transform.localRotation,appliedLocalRotation))
            {
                // Undo display roll for off-center rays AND their near-plane origins.
                // Conjugation also accounts for the current parent/camera orientation.
                Quaternion world=camera.transform.rotation;
                Quaternion undo=world*Quaternion.Inverse(rollOffset)*Quaternion.Inverse(world);
                ray.origin=camera.transform.position+undo*(ray.origin-camera.transform.position);
                ray.direction=undo*ray.direction;
            }
            if(camera!=null && camera==shiftedCamera &&
                (camera.transform.localPosition-appliedLocalPosition).sqrMagnitude<.00000001f)
            {
                var parent=camera.transform.parent;
                ray.origin-=parent!=null?parent.TransformVector(localOffset):localOffset;
            }
            return ray;
        }

        public static void Clear()
        {RestoreCamera();pulses=new ConditionalWeakTable<EntityVehicle,Pulse>();}
    }
}
