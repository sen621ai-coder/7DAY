using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace PZAEC.M1
{
    // Local sight orientation is independent of both the native chase camera and
    // the server's finite-speed gun traverse. Rendering never supplies fire aim.
    public static class Optics
    {
        static EntityVehicle vehicle;
        static Transform model,sight;
        static int seat=-1,frame=-1,mode;
        static float yaw,pitch,range=600;
        static bool zoom;
        static readonly int[] zoomSteps=new int[3];
        static Vector3 origin,target;
        static Quaternion look;
        static Camera applied;
        static Vector3 savedPosition,lastPosition;
        static Quaternion savedRotation,lastRotation;
        static float savedFov,lastFov;
        static readonly Dictionary<Renderer,bool> hidden=new Dictionary<Renderer,bool>();

        public static void Install(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(GameManager),"Update"),prefix:new HarmonyMethod(typeof(Optics),nameof(RestoreCamera)));
            // The player's LateUpdate writes FOV after vp_FPCamera on some frames.
            // Apply only to the local world camera immediately before culling.
            Camera.onPreCull+=BeforeRender;
        }
        public static float Magnification(int weapon,int step)=>weapon==0?(step==0?2:step==1?4:8):weapon==1?(step==0?1.5f:3):(step==0?2:4);
        public static float Fov(float normal,float magnification)=>2*Mathf.Atan(Mathf.Tan(normal*Mathf.Deg2Rad*.5f)/magnification)*Mathf.Rad2Deg;
        public static Quaternion Advance(ref float yaw,ref float pitch,float dx,float dy,float magnification)
        {
            // Mouse axes are per-frame deltas; multiplying by deltaTime would
            // change sensitivity with frame rate. No hull rotation is added.
            yaw=Mathf.Repeat(yaw+dx*2/magnification,360);
            pitch=Mathf.Clamp(pitch+dy*2/magnification,-80,85);
            return Quaternion.Euler(-pitch,yaw,0);
        }
        public static bool Active(EntityVehicle v)=>vehicle==v&&model!=null;
        public static byte Mode(EntityVehicle v,int s)=>Active(v)&&seat==s?(byte)mode:SecondaryPresentation.Mode(v,s);
        static void Angles(Vector3 direction)
        {yaw=Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg;pitch=Mathf.Asin(Mathf.Clamp(direction.normalized.y,-1,1))*Mathf.Rad2Deg;look=Quaternion.Euler(-pitch,yaw,0);}
        public static void UpdateInput(EntityPlayerLocal p,EntityVehicle v,byte weapon)
        {
            if(frame==Time.frameCount&&vehicle==v&&mode==weapon)return;
            frame=Time.frameCount;
            int s=Weapons.Seat(v,p.entityId);bool entering=vehicle!=v||seat!=s;
            if(entering){Clear();vehicle=v;seat=s;model=SecondaryModel.Find(v.PhysicsTransform!=null?v.PhysicsTransform:v.transform,"M1Visual");if(model==null){Clear();return;}Angles(Weapons.States.TryGetValue(v.entityId,out var main)?Weapons.Direction(main):Weapons.Body(v)*Vector3.forward);}
            frame=Time.frameCount;
            bool nextZoom=Input.GetKey(KeyCode.Mouse1),transition=!entering&&(zoom!=nextZoom||mode!=weapon);
            var previousTarget=target;float previousRange=range;
            mode=weapon;zoom=nextZoom;
            sight=SecondaryModel.Find(model,mode==1?"RoofMGSight":mode==2?"AAPitch":"GunnerSight");
            if(zoom&&Input.GetKeyDown(KeyCode.Z))zoomSteps[mode]=(zoomSteps[mode]+1)%(mode==0?3:2);
            // Preserve a visible world target when moving from chase to optic.
            origin=CameraOrigin();
            if(transition&&previousRange>10)Angles(previousTarget-origin);
            look=Advance(ref yaw,ref pitch,Input.GetAxisRaw("Mouse X"),Input.GetAxisRaw("Mouse Y"),zoom?Magnification(mode,zoomSteps[mode]):1);
            origin=CameraOrigin();UpdateTarget();
        }
        static Vector3 CameraOrigin()
        {
            if(zoom)return (sight!=null?sight.position:model.TransformPoint(new Vector3(0,2.8f,0)))+Origin.position+(mode==2?Vector3.up*.25f:Vector3.zero);
            var pivot=model.TransformPoint(new Vector3(0,2.8f,0))+Origin.position;
            var offset=Vector3.up*1.6f-look*Vector3.forward*10;
            if(Weapons.Trace(vehicle,pivot,offset.normalized,offset.magnitude,out var hit))return pivot+offset.normalized*Mathf.Max(.15f,Vector3.Distance(pivot,hit.hit.pos)-.25f);
            return pivot+offset;
        }
        static void UpdateTarget()
        {
            float maximum=mode==1?200:600;var direction=look*Vector3.forward;
            target=Weapons.Trace(vehicle,origin,direction,maximum,out var hit)?hit.hit.pos:origin+direction*maximum;
            range=Vector3.Distance(origin,target);
        }
        public static bool TryRay(EntityPlayerLocal p,out Ray ray)
        {
            ray=default(Ray);if(vehicle==null||p.AttachedToEntity!=vehicle||model==null)return false;
            ray=new Ray(origin,look*Vector3.forward);return true;
        }
        public static void RestoreCamera()
        {
            if(applied!=null){var t=applied.transform;if((t.position-lastPosition).sqrMagnitude<.00001f)t.position=savedPosition;if(Quaternion.Angle(t.rotation,lastRotation)<.01f)t.rotation=savedRotation;if(Mathf.Abs(applied.fieldOfView-lastFov)<.01f)applied.fieldOfView=savedFov;}
            applied=null;
        }
        static void Visibility(bool hide)
        {
            if(!hide){foreach(var pair in hidden)if(pair.Key!=null)pair.Key.forceRenderingOff=pair.Value;hidden.Clear();return;}
            // CrewVisibility independently owns seated player renderers. Only
            // touch the tank visual subtree here, so restoration cannot unhide crew.
            foreach(var r in model.GetComponentsInChildren<Renderer>(true)){if(!hidden.ContainsKey(r))hidden.Add(r,r.forceRenderingOff);r.forceRenderingOff=true;}
        }
        static void BeforeRender(Camera camera)
        {
            var p=GameManager.Instance?.World?.GetPrimaryPlayer();if(p==null||camera!=p.playerCamera)return;
            if(vehicle==null||p.AttachedToEntity!=vehicle||model==null||!Weapons.UIReady(p)||GameManager.Instance.IsPaused()){Clear();return;}
            Visibility(zoom);
            // The optic follows the real mount position, but not hull pitch/roll.
            // Inputs/rays use this same unshaken orientation in world coordinates.
            origin=CameraOrigin();
            float shake=Presentation.ShotPulse(vehicle)*(zoom?.06f:.15f);
            ApplyPose(camera,origin-Origin.position,look*Quaternion.Euler(-shake,shake*.13f,0),zoom?Fov(p.GetCameraFOV(),Magnification(mode,zoomSteps[mode])):p.GetCameraFOV());
        }
        static void ApplyPose(Camera camera,Vector3 position,Quaternion rotation,float fov)
        {
            RestoreCamera();applied=camera;savedPosition=camera.transform.position;savedRotation=camera.transform.rotation;savedFov=camera.fieldOfView;
            lastPosition=position;lastRotation=rotation;lastFov=fov;
            camera.transform.SetPositionAndRotation(position,rotation);camera.fieldOfView=fov;
        }
        public static void Draw(EntityPlayerLocal p,EntityVehicle v,Transform muzzle,Vector3 direction,int reason,bool ap=false)
        {
            if(!Active(v)||p.playerCamera==null)return;
            var old=GUI.color;float cx=Screen.width*.5f,cy=Screen.height*.5f;
            GUI.color=new Color(.8f,.95f,.85f,.9f);
            Line(cx-15,cy,10,1);Line(cx+5,cy,10,1);Line(cx,cy-15,1,10);Line(cx,cy+5,1,10);
            GUI.Label(new Rect(cx+22,cy+18,290,25),(zoom?Magnification(mode,zoomSteps[mode]).ToString("0.#")+"×":"第三人称")+" · "+range.ToString("0")+" m");
            GUI.Label(new Rect(cx-220,Screen.height-214,520,24),zoom?"稳定瞄准 · 鼠标观察 · Z变倍 · 松开右键返回":"鼠标独立观察 · 按住右键进入瞄准镜");
            if(muzzle!=null){
                float distance=Mathf.Clamp(Vector3.Distance(muzzle.position+Origin.position,target),5,mode==1?200:600);
                var point=muzzle.position+direction*distance;
                if(mode==0){float t=distance/Rules.ShellSpeed(ap);point+=Vector3.down*(4.905f*t*t)+(v.vehicleRB!=null?Vector3.ClampMagnitude(v.vehicleRB.velocity,20)*t:Vector3.zero);}
                var screen=p.playerCamera.WorldToScreenPoint(point);bool outside=screen.z<=0||screen.x<24||screen.x>Screen.width-24||screen.y<40||screen.y>Screen.height-40;
                GUI.color=reason==0?new Color(.3f,1,.4f):new Color(1,.65f,.15f);
                if(!outside){float x=screen.x,y=Screen.height-screen.y;Line(x-7,y-7,14,1);Line(x-7,y+7,14,1);Line(x-7,y-7,1,14);Line(x+7,y-7,1,14);}
                else GUI.Label(new Rect(cx-180,cy+48,400,24),"武器仍在视野外，等待炮塔转向");
            }
            GUI.color=old;
        }
        static void Line(float x,float y,float w,float h)=>GUI.DrawTexture(new Rect(x,y,w,h),Texture2D.whiteTexture);
        public static void Clear(){RestoreCamera();Visibility(false);vehicle=null;model=sight=null;seat=-1;frame=-1;zoom=false;range=600;}
    }
}
