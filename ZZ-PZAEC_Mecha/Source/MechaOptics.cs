using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace PZAEC.Mecha
{
    public static class Optics
    {
        static EntityVehicle vehicle;static Transform eye,lens;
        static float yaw,pitch;static bool zoom;static int zoomStep;
        static bool third=true;static float switched=-100;static Vector3 transitionPosition;static Quaternion transitionRotation;
        static readonly Dictionary<Renderer,bool> hidden=new Dictionary<Renderer,bool>();
        static readonly RaycastHit[] cameraHits=new RaycastHit[64];
        static Camera applied;static Vector3 savedPosition,lastPosition;static Quaternion savedRotation,lastRotation;static float savedFov,lastFov;
        static Quaternion StoredLook=Quaternion.identity;
        static Vector3 aimTarget;static bool hasAimTarget;
        static float eyeY,eyeVelocity;static int eyeFrame=-1;static bool eyeReady;
        public static bool ThirdPerson {get{return third;}}
        public static Quaternion Look {get{return StoredLook;}}
        public static bool Active(EntityVehicle v){return vehicle==v&&eye!=null;}
        public static void Install(Harmony h){h.Patch(AccessTools.Method(typeof(GameManager),"Update"),prefix:new HarmonyMethod(typeof(Optics),nameof(RestoreCamera)));Camera.onPreCull+=BeforeRender;Camera.onPostRender+=AfterRender;}
        static float Magnification(){return zoom?(zoomStep==0?2f:4f):1f;}
        public static float Fov(float normal,float magnification){return 2*Mathf.Atan(Mathf.Tan(normal*Mathf.Deg2Rad*.5f)/magnification)*Mathf.Rad2Deg;}
        public static Quaternion Advance(ref float y,ref float p,float dx,float dy,float magnification){y=Mathf.Repeat(y+dx*2/magnification,360);p=Mathf.Clamp(p+dy*2/magnification,-85,85);return Quaternion.Euler(-p,y,0);}
        public static void SetView(bool value){third=value;PlayerPrefs.SetInt("PZAEC.Mecha.ThirdPerson",third?1:0);PlayerPrefs.Save();switched=Time.time;transitionPosition=lastPosition;transitionRotation=lastRotation;}
        public static void UpdateInput(EntityPlayerLocal player,EntityVehicle v)
        {
            var rig=Model.GetRig(v);if(rig==null||rig.Head==null)return;
            if(vehicle!=v||eye==null){Clear();vehicle=v;eye=lens=rig.Head;var forward=Weapons.BodyRotation(v)*Vector3.forward;yaw=Mathf.Atan2(forward.x,forward.z)*Mathf.Rad2Deg;pitch=8;third=PlayerPrefs.GetInt("PZAEC.Mecha.ThirdPerson",1)!=0;switched=-100;}
            if(!Boarding.Active(v)&&Weapons.UIReady(player)&&Input.GetKeyDown(KeyCode.BackQuote))SetView(!third);
            zoom=Input.GetKey(Rules.Complete(v)?KeyCode.V:KeyCode.Mouse1);if(zoom&&Input.GetKeyDown(KeyCode.Z))zoomStep=(zoomStep+1)%2;
            StoredLook=Advance(ref yaw,ref pitch,Input.GetAxisRaw("Mouse X"),Input.GetAxisRaw("Mouse Y"),Magnification());
        }
        public static Vector3 CollideCamera(EntityVehicle v,Vector3 pivot,Vector3 desired)
        {
            var d=desired-pivot;float length=d.magnitude;if(length<.001f)return desired;float safe=length;
            int n=Physics.SphereCastNonAlloc(pivot,.2f,d/length,cameraHits,length,~0,QueryTriggerInteraction.Ignore);
            for(int i=0;i<n;i++){var hit=cameraHits[i];var t=hit.collider.transform;var crew=v.GetAttached(0);var entity=GameUtils.GetHitRootEntity(hit.collider.tag,t);
                // Character body-part colliders may live under the pooled Players
                // hierarchy, outside the rider transform. They are not walls.
                if(hit.collider.tag.StartsWith("E_BP_")||entity==v||(crew!=null&&entity==crew)||t.IsChildOf(v.transform)||(v.vehicleRB!=null&&t.IsChildOf(v.vehicleRB.transform))||(crew!=null&&t.IsChildOf(crew.transform)))continue;safe=Mathf.Min(safe,Mathf.Max(.05f,hit.distance-.1f));}
            return pivot+d/length*safe;
        }
        public static Vector3 CameraPosition(EntityVehicle v,Model.Rig r,Quaternion look,bool external)
        {
            // A recessed cockpit lens leaves the real forearms in the lower view.
            // The firing origin remains the physical head / palm, not this lens.
            if(!external)return r.Justice!=null?r.Head.position+r.Head.forward*.10f:r.Head.position-Vector3.up*.15f-look*Vector3.forward*.70f;
            var pivot=r.Torso.position+Vector3.up*.25f;return CollideCamera(v,pivot,pivot+look*new Vector3(.6f,.85f,-4.8f));
        }
        static Vector3 Position(Model.Rig r){var end=CameraPosition(vehicle,r,StoredLook,third);
            if(!third){float absolute=end.y+Origin.position.y;if(!eyeReady){eyeY=absolute;eyeVelocity=0;eyeReady=true;}if(eyeFrame!=Time.frameCount){eyeFrame=Time.frameCount;eyeY=Mathf.SmoothDamp(eyeY,absolute,ref eyeVelocity,.10f,20,Time.deltaTime);}end.y=eyeY-Origin.position.y;}else eyeReady=false;
            float t=Mathf.SmoothStep(0,1,Mathf.Clamp01((Time.time-switched)/.2f));return CollideCamera(vehicle,r.Torso.position+Vector3.up*.25f,Vector3.Lerp(transitionPosition,end,t));}
        public static bool ProjectWorld(Vector3 world,out Vector2 screen){screen=Vector2.zero;var p=Quaternion.Inverse(lastRotation)*(world-Origin.position-lastPosition);if(p.z<=.05f)return false;float scale=Screen.height*.5f/Mathf.Tan(Mathf.Max(1,lastFov)*Mathf.Deg2Rad*.5f);screen=new Vector2(Screen.width*.5f+p.x/p.z*scale,Screen.height*.5f-p.y/p.z*scale);return screen.x>=0&&screen.x<=Screen.width&&screen.y>=0&&screen.y<=Screen.height;}
        public static bool TryRay(out Ray ray)
        {
            ray=default(Ray);if(vehicle==null||eye==null)return false;var rig=Model.GetRig(vehicle);if(rig==null)return false;
            var camera=Position(rig)+Origin.position;var forward=StoredLook*Vector3.forward;var target=camera+forward*Rules.BeamRange;
            if(Weapons.Trace(vehicle,camera,forward,Rules.BeamRange,out var hit))target=hit.hit.pos;
            aimTarget=target;hasAimTarget=true;var origin=eye.position+Origin.position;ray=new Ray(origin,Weapons.AimFromMuzzle(origin,target,forward));return true;
        }
        public static Vector3 WeaponDirection(EntityVehicle v,Model.Rig rig){if(Rules.Complete(v))return rig.Head.forward;return vehicle==v&&hasAimTarget?Weapons.AimFromMuzzle(Weapons.MuzzleWorld(rig,v),aimTarget,rig.HandR.forward):rig.HandR.forward;}
        public static void RestoreCamera(){if(applied!=null){var t=applied.transform;if((t.position-lastPosition).sqrMagnitude<.00001f)t.position=savedPosition;if(Quaternion.Angle(t.rotation,lastRotation)<.01f)t.rotation=savedRotation;if(Mathf.Abs(applied.fieldOfView-lastFov)<.01f)applied.fieldOfView=savedFov;}applied=null;}
        static void HideRenderer(Renderer r){if(r==null||hidden.ContainsKey(r))return;hidden.Add(r,r.forceRenderingOff);r.forceRenderingOff=true;}
        static void Visibility(bool hide){if(!hide){foreach(var p in hidden)if(p.Key!=null)p.Key.forceRenderingOff=p.Value;hidden.Clear();return;}var rig=vehicle!=null?Model.GetRig(vehicle):null;if(rig!=null)foreach(var r in rig.Visual.GetComponentsInChildren<Renderer>(true))HideRenderer(r);}
        public static void FirstPersonVisibility(EntityVehicle v){var rig=Model.GetRig(v);if(rig==null)return;foreach(var r in rig.FirstPersonHidden)HideRenderer(r);}
        static void Apply(Camera c,Vector3 p,Quaternion q,float f){RestoreCamera();applied=c;savedPosition=c.transform.position;savedRotation=c.transform.rotation;savedFov=c.fieldOfView;lastPosition=p;lastRotation=q;lastFov=f;c.transform.SetPositionAndRotation(p,q);c.fieldOfView=f;}
        static void BeforeRender(Camera camera)
        {
            var player=GameManager.Instance!=null&&GameManager.Instance.World!=null?GameManager.Instance.World.GetPrimaryPlayer():null;if(player==null||camera!=player.playerCamera)return;
            if(Boarding.CameraRide(out var ride,out var rot,out var fov)){Visibility(false);Visibility(Boarding.CockpitCamera(vehicle));Apply(camera,ride,rot,fov);return;}
            // Menus freeze input, not the selected driving view. Restoring the
            // native seat lens on a menu frame would put it inside the armour.
            if(vehicle==null||player.AttachedToEntity!=vehicle||eye==null||player.IsDead()||vehicle.IsDead()){RestoreCamera();Visibility(false);return;}
            Visibility(false);var rig=Model.GetRig(vehicle);var position=Position(rig);bool close=!third||Vector3.Distance(position,rig.Torso.position+Vector3.up*.25f)<1.2f;if(close)FirstPersonVisibility(vehicle);
            var shake=CombatFeedback.Kick(vehicle.entityId);Apply(camera,position+StoredLook*new Vector3(0,shake*.012f,0),StoredLook*Quaternion.Euler(shake*.45f,0,0),Fov(close?Mathf.Max(80,player.GetCameraFOV()):player.GetCameraFOV(),Magnification()));
        }
        static void AfterRender(Camera camera){if(camera==applied)Visibility(false);}
        public static void Clear(){eyeReady=false;eyeVelocity=0;RestoreCamera();Visibility(false);vehicle=null;eye=lens=null;zoom=false;zoomStep=0;switched=-100;hasAimTarget=false;}
    }

    // Seated pilots ride inside the walker shell; suppress their presentation
    // on every client exactly like the closed-cabin M1 crew handling.
    public static class CrewVisibility
    {
        static readonly Dictionary<Renderer, bool> original = new Dictionary<Renderer, bool>();
        static readonly HashSet<Renderer> current = new HashSet<Renderer>();
        static readonly List<Renderer> removed = new List<Renderer>();

        public static void Update(World world)
        {
            current.Clear();
            foreach (var entity in world.Entities.list)
            {
                var mecha = entity as EntityVehicle;
                if (!Weapons.IsMecha(mecha)) continue;
                var occupant = mecha.GetAttached(0) as EntityPlayer;
                if (occupant == null) continue;
                foreach (var renderer in occupant.transform.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null) continue;
                    current.Add(renderer);
                    if (!original.ContainsKey(renderer)) original.Add(renderer, renderer.forceRenderingOff);
                    renderer.forceRenderingOff = true;
                }
            }
            removed.Clear();
            foreach (var pair in original)
                if (!current.Contains(pair.Key))
                {
                    if (pair.Key != null) pair.Key.forceRenderingOff = pair.Value;
                    removed.Add(pair.Key);
                }
            foreach (var renderer in removed) original.Remove(renderer);
        }

        public static void Clear()
        {
            foreach (var pair in original) if (pair.Key != null) pair.Key.forceRenderingOff = pair.Value;
            original.Clear(); current.Clear(); removed.Clear();
        }
    }
}
