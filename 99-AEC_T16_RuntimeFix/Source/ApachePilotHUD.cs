using UnityEngine;

namespace AECT16RuntimeFix
{
    public static class ApachePilotHUD
    {
        private static int vehicleId=-1,targetId=-1,ammo;
        private static float received=-100,progress,cooldown;
        private static byte reason;
        private static bool guided;
        private static Vector3 targetPoint;
        private static GUIStyle text;
        public static void Clear(){vehicleId=targetId=-1;received=-100;}
        public static void Receive(World world,int vehicle,int target,Vector3 point,Vector3 status,float fraction)
        {
            if(world?.GetPrimaryPlayer()?.AttachedToEntity?.entityId!=vehicle)return;
            vehicleId=vehicle;targetId=target;targetPoint=point;received=Time.time;
            ammo=Mathf.Max(0,Mathf.RoundToInt(status.x));cooldown=status.y;
            int flags=Mathf.RoundToInt(status.z);guided=(flags&16)!=0;reason=(byte)(flags&15);progress=Mathf.Clamp01(fraction);
        }
        private static string L(string key){return Localization.Get("pzApachePilot"+key);}
        private static void Box(float x,float y,float w,float h,Color color){GUI.color=color;GUI.DrawTexture(new Rect(x,y,w,h),Texture2D.whiteTexture);}
        private static void Label(float x,float y,float w,string value,Color color){GUI.color=color;GUI.Label(new Rect(x,y,w,24),value,text);}
        public static void Draw(EntityVehicle vehicle)
        {
            ApacheFlightAssist.Draw(vehicle,0);
            if(text==null)text=new GUIStyle(GUI.skin.label){fontSize=14};
            var player=GameManager.Instance.World.GetPrimaryPlayer();
            bool aiming=Input.GetKey(ApacheFlightAssist.Key(vehicle,"pzApacheZoomKey",KeyCode.Mouse1));
            bool mode=ApacheWeapons.LocalGuided;
            bool fresh=vehicleId==vehicle.entityId&&Time.time-received<1&&guided==mode;
            byte displayReason=reason;Vector3 point=targetPoint;
            // Immediate local feedback; only authoritative server status enables ready.
            if(aiming){var ray=ApacheWeapons.SightRay(player);var r=ApacheWeapons.ResolvePilot(ApacheWeapons.GetState(vehicle),ray.origin,ray.direction,out point,out var candidate);if(r!=0)displayReason=r;}
            float wait=fresh?Mathf.Max(0,cooldown-(Time.time-received)):0;
            string status=!aiming?L("AimRequired"):!fresh?L("Syncing"):displayReason==8?L("Release"):displayReason==1?L("Arc"):displayReason==2?L("Blocked"):displayReason==4?L("NoTarget"):displayReason==5?L("Acquiring"):displayReason!=0?L("Unavailable"):ammo==0?L("Empty"):wait>0?L("Cooldown")+" "+wait.ToString("0.0")+"s":mode?L("Locked"):L("Ready");
            bool ready=aiming&&fresh&&displayReason==0&&ammo>0&&wait<=0;
            var color=ready?new Color(.3f,1,.6f):new Color(1,.7f,.25f);
            Box(330,512,620,130,new Color(.02f,.045f,.06f,.92f));
            Label(346,522,360,L(mode?"Guided":"Rockets"),color);
            Label(700,522,245,status,color);
            Label(346,552,600,L("Ammo")+" "+(fresh?ammo.ToString():"—")+"    "+L("Range")+" 350m    "+L("Limits"),Color.white);
            Label(346,582,600,"["+ApacheFlightAssist.Key(vehicle,"pzApacheZoomKey",KeyCode.Mouse1)+"] "+L("Aim")+"   ["+ApacheFlightAssist.Key(vehicle,"pzApacheModeKey",KeyCode.R)+"] "+L("Switch")+"   ["+ApacheWeapons.FireKey(vehicle,0)+"] "+L("Fire"),Color.white);
            Label(346,610,600,L(mode?"GuidedHint":"RocketHint"),color);
            if(!aiming)return;
            var cursor=ApacheAimCursor.CanvasPoint();float cx=cursor.x,cy=cursor.y;
            Box(cx-26,cy-1,16,2,color);Box(cx+10,cy-1,16,2,color);Box(cx-1,cy-26,2,16,color);Box(cx-1,cy+10,2,16,color);
            Label(Mathf.Clamp(cx+34,20,900),Mathf.Clamp(cy-13,20,460),360,status,color);
            if(mode){float bx=Mathf.Clamp(cx-90,20,1080),by=Mathf.Clamp(cy+34,45,476);Box(bx,by,180,6,new Color(.2f,.25f,.25f));Box(bx,by,180*(fresh?progress:0),6,color);}
            var camera=player.playerCamera;
            if(camera!=null&&(displayReason==0||displayReason==5||displayReason==8)){
                // Correct view/mount parallax and inherited drift are applied to the
                // same point used for server launch; there is no center-only fake hit.
                var screen=camera.WorldToScreenPoint(point-Origin.position);
                float scale=Mathf.Min(Screen.width/1280f,Screen.height/720f);
                float x=(screen.x-(Screen.width-1280*scale)/2)/scale;
                float y=(Screen.height-screen.y-(Screen.height-720*scale)/2)/scale;
                if(screen.z>0&&x>40&&x<1240&&y>40&&y<480){
                    Box(x-12,y-12,24,1,color);Box(x-12,y+12,24,1,color);Box(x-12,y-12,1,24,color);Box(x+12,y-12,1,24,color);
                    string name="";var target=GameManager.Instance.World.GetEntity(targetId);
                    if(mode&&fresh&&target!=null)name=Localization.Get(EntityClass.list[target.entityClass].entityClassName)+" ";
                    Label(Mathf.Clamp(x-90,20,900),y+20,360,name+Vector3.Distance(vehicle.position,point).ToString("0")+"m",color);
                }
            }
        }
    }
}
