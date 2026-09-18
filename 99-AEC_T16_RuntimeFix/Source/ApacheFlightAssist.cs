using System;
using HarmonyLib;
using UnityEngine;

namespace AECT16RuntimeFix
{
    // Camera and informational overlays only. Marks are stationary world points.
    public static class ApacheFlightAssist
    {
        private static Camera zoomCamera;
        private static float baseFov,lastFov,nextPrediction,markUntil;
        private static int predictionVehicle=-1,markVehicle=-1;
        private static Vector3 marker;
        private static readonly Vector3[] predicted=new Vector3[2];
        private static readonly float[] flightTime=new float[2];
        private static readonly bool[] collision=new bool[2];
        private static GUIStyle label;
        public static bool Zooming {get{return zoomCamera!=null;}}
        public static void Install(Harmony harmony)
        {
            try{harmony.Patch(AccessTools.Method(typeof(vp_FPCamera),"LateUpdate"),prefix:new HarmonyMethod(typeof(ApacheFlightAssist),nameof(BeforeCameraUpdated)),postfix:new HarmonyMethod(typeof(ApacheFlightAssist),nameof(CameraUpdated)));}
            catch(Exception ex){Log.Warning("[Apache-Assist] Zoom unavailable: "+ex.Message);}
        }
        public static KeyCode Key(EntityVehicle v,string name,KeyCode fallback)
        {return v.vehicle.Properties.Values.TryGetValue(name,out var value)&&Enum.TryParse<KeyCode>(value,true,out var key)&&key!=KeyCode.None?key:fallback;}
        private static void RestoreZoom()
        {
            // If another system already restored/changed FOV, do not overwrite it.
            if(zoomCamera!=null&&Mathf.Abs(zoomCamera.fieldOfView-lastFov)<.05f)zoomCamera.fieldOfView=baseFov;
            zoomCamera=null;
        }
        public static void Clear(){RestoreZoom();predictionVehicle=markVehicle=-1;markUntil=nextPrediction=0;}
        public static void BeforeCameraUpdated()
        {
            // Native damping must see its own FOV, not yesterday's magnified FOV.
            if(zoomCamera!=null&&Mathf.Abs(zoomCamera.fieldOfView-lastFov)<.05f)zoomCamera.fieldOfView=baseFov;
        }
        public static void CameraUpdated()
        {
            var player=GameManager.Instance?.World?.GetPrimaryPlayer();
            var vehicle=player?.AttachedToEntity as EntityVehicle;
            var ui=player!=null?LocalPlayerUI.GetUIForPlayer(player):null;
            bool usable=player!=null&&!player.IsDead()&&ApacheWeapons.IsApache(vehicle)&&ApacheWeapons.Seat(vehicle,player.entityId)==1&&
                GameManager.Instance.GameIsFocused&&!GameManager.Instance.IsPaused()&&
                !(ui!=null&&(LocalPlayerUI.AnyModalWindowOpen()||ui.windowManager.IsCursorWindowOpen()||ui.windowManager.IsInputActive()));
            if(!usable||!Input.GetKey(Key(vehicle,"pzApacheZoomKey",KeyCode.Mouse1))||player.playerCamera==null){RestoreZoom();return;}
            var camera=player.playerCamera;
            if(zoomCamera!=camera){RestoreZoom();zoomCamera=camera;baseFov=camera.fieldOfView;}
            else baseFov=camera.fieldOfView;
            lastFov=(float)(2*Math.Atan(Math.Tan(baseFov*Math.PI/360)/2)*180/Math.PI);
            camera.fieldOfView=lastFov;
        }
        public static void ReceiveMark(World world,int vehicle,Vector3 point,float remaining)
        {
            if(world?.GetPrimaryPlayer()?.AttachedToEntity?.entityId!=vehicle)return;
            if(!ApacheWeaponRules.Finite(point.x)||!ApacheWeaponRules.Finite(point.y)||!ApacheWeaponRules.Finite(point.z)||!ApacheWeaponRules.Finite(remaining))return;
            markVehicle=vehicle;marker=point;markUntil=Time.time+Mathf.Clamp(remaining,0,15);
        }
        public static void Update(World world)
        {
            var player=world?.GetPrimaryPlayer();var vehicle=player?.AttachedToEntity as EntityVehicle;
            if(!ApacheWeapons.IsApache(vehicle)||player.IsDead()) {RestoreZoom();predictionVehicle=-1;markVehicle=-1;return;}
            if(ApacheWeapons.Seat(vehicle,player.entityId)!=1)RestoreZoom();
            if(ApacheWeapons.Seat(vehicle,player.entityId)!=0)return;
            if(predictionVehicle!=vehicle.entityId||Time.time>=nextPrediction){
                predictionVehicle=vehicle.entityId;nextPrediction=Time.time+.1f;
                for(int i=0;i<2;i++)collision[i]=ApacheWeapons.PredictRocket(vehicle,i==1,out predicted[i],out flightTime[i]);
            }
        }
        private static string L(string key){return Localization.Get("pzApache"+key);}
        private static void Rect(float x,float y,float w,float h){GUI.DrawTexture(new Rect(x,y,w,h),Texture2D.whiteTexture);}
        // Called inside the HUD's centered 1280x720 transform.
        private static void Point(Camera camera,Vector3 point,string text,Color color,int row)
        {
            var screen=camera.WorldToScreenPoint(point-Origin.position);
            float scale=Mathf.Min(Screen.width/1280f,Screen.height/720f);
            float x=(screen.x-(Screen.width-1280*scale)/2)/scale;
            float y=(Screen.height-screen.y-(Screen.height-720*scale)/2)/scale;
            if(screen.z<=0){x=1280-x;y=80;}
            bool outside=screen.z<=0||x<100||x>1180||y<80||y>475;
            x=Mathf.Clamp(x,100,1180);y=Mathf.Clamp(y,80,475);
            GUI.color=color;Rect(x-7,y-7,14,1);Rect(x-7,y+7,14,1);Rect(x-7,y-7,1,14);Rect(x+7,y-7,1,14);
            GUI.Label(new Rect(Mathf.Clamp(x-95,5,1070),y+12+row*17,210,22),(outside?L("Offscreen")+" ":"")+text,label);
        }
        public static void Draw(EntityVehicle vehicle,int seat)
        {
            var player=GameManager.Instance?.World?.GetPrimaryPlayer();var camera=player?.playerCamera;if(camera==null)return;
            if(label==null)label=new GUIStyle(GUI.skin.label){fontSize=12};
            var old=GUI.color;
            try{
                if(seat==0&&predictionVehicle==vehicle.entityId){
                    for(int i=0;i<2;i++)if(collision[i])Point(camera,predicted[i],L(i==0?"RocketLeft":"RocketRight")+" "+flightTime[i].ToString("0.0")+"s",new Color(1,.76f,.3f),i);
                    GUI.color=new Color(1,.76f,.3f);GUI.Label(new Rect(360,490,700,22),L(collision[0]||collision[1]?"PredictionHint":"NoPrediction"),label);
                }
                if(markVehicle==vehicle.entityId&&Time.time<markUntil)Point(camera,marker,L("SharedMark")+" "+Vector3.Distance(vehicle.position,marker).ToString("0")+"m",new Color(.45f,.85f,1),2);
                if(seat==1){GUI.color=new Color(.6f,.88f,.82f);GUI.Label(new Rect(360,490,780,22),"["+Key(vehicle,"pzApacheZoomKey",KeyCode.Mouse1)+"] "+L(Zooming?"ZoomActive":"ZoomHint")+"    ["+Key(vehicle,"pzApacheMarkKey",KeyCode.Mouse2)+"] "+L("MarkHint"),label);}
            }finally{GUI.color=old;}
        }
    }
}
