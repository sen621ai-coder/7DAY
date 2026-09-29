using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace PZAEC.M1
{
    public static class SecondaryPresentation
    {
        sealed class View{public EntityVehicle Vehicle;public int Epoch,Serial;public float At,Clock=float.PositiveInfinity;public readonly float[] F=new float[16];public readonly int[] I=new int[8];public Transform Yaw,Pitch,AA,Muzzle;public AudioSource MGSound,AASound;public Transform Flash;public float ShotAt=-100;public readonly MaterialPropertyBlock FlashProperties=new MaterialPropertyBlock();}
        sealed class Trail{public int Vehicle,Id;public LineRenderer Line;public Vector3 A,B;public float Until,Start;public bool Missile;}
        static readonly Dictionary<int,View> views=new Dictionary<int,View>();static readonly List<Trail> trails=new List<Trail>();static Material material;static AudioClip mgClip,aaClip;
        static AudioClip Clip(string name,bool missile)
        {int rate=22050,count=missile?11025:2205;var data=new float[count];var rng=new System.Random(42);for(int i=0;i<count;i++){float t=(float)i/count;data[i]=(float)((rng.NextDouble()*2-1)*Math.Exp(-t*(missile?4:12))*.4+Math.Sin(i*(missile?.045:.14))*Math.Exp(-t*15)*.35);}var clip=AudioClip.Create(name,count,1,rate,false);clip.SetData(data,0);return clip;}
        static AudioSource Sound(Transform t,AudioClip clip){var audio=t.gameObject.AddComponent<AudioSource>();audio.clip=clip;audio.playOnAwake=false;audio.spatialBlend=1;audio.minDistance=6;audio.maxDistance=180;audio.volume=.5f;return audio;}
        static View Get(EntityVehicle v)
        {
            if(views.TryGetValue(v.entityId,out var existing)&&existing.Vehicle==v)return existing;
            var root=SecondaryModel.Find(v.PhysicsTransform!=null?v.PhysicsTransform:v.transform,"M1Visual");var yaw=SecondaryModel.Find(root,"RoofMGYaw");if(yaw==null)return null;
            if(material==null){material=new Material(Shader.Find("Sprites/Default"));mgClip=Clip("M1 MG",false);aaClip=Clip("M1 AA",true);}
            var x=new View{Vehicle=v,Yaw=yaw,Pitch=SecondaryModel.Find(root,"RoofMGPitch"),AA=SecondaryModel.Find(root,"AAPitch"),Muzzle=SecondaryModel.Find(root,"RoofMGMuzzle")};x.Flash=Presentation.CreateMGFlash(x.Muzzle);x.MGSound=Sound(x.Muzzle,mgClip);x.AASound=Sound(x.AA,aaClip);x.I[0]=v.GetAttached(1)!=null?1:0;views[v.entityId]=x;return x;
        }
        public static byte Mode(EntityVehicle v,int seat)=>seat<0?(byte)0:views.TryGetValue(v.entityId,out var s)?(byte)s.I[seat]:(seat==0&&v.GetAttached(1)!=null?(byte)1:(byte)0);
        public static void SaveItem(EntityVehicle vehicle)
        {
            if(!views.TryGetValue(vehicle.entityId,out var v)||vehicle.vehicle.itemValue==null)return;
            // Preserve the last authoritative remaining times conservatively. Never
            // advance ammo/reload on the client while collecting a remote vehicle.
            int[] values={v.I[2],Mathf.CeilToInt(v.F[3]*1000),(int)v.F[10],Mathf.CeilToInt(v.F[4]*1000),(int)v.F[11],Mathf.CeilToInt(v.F[5]*1000),Mathf.CeilToInt(v.F[6]*1000),Mathf.CeilToInt(v.F[7]*1000),Mathf.CeilToInt(v.F[12]*1000),Mathf.CeilToInt(v.F[13]*1000)};
            for(int i=0;i<Secondary.Keys.Length;i++)vehicle.vehicle.itemValue.SetMetadata(Secondary.Keys[i],values[i]);
        }
        public static void Receive(World w,NetPackageM1SecondaryEvent p)
        {
            if(p.Version!=SecondaryRules.Protocol||w?.GetPrimaryPlayer()==null)return;var vehicle=w.GetEntity(p.Vehicle) as EntityVehicle;if(!Weapons.IsTank(vehicle))return;var v=Get(vehicle);if(v==null)return;
            if(v.Epoch!=0&&v.Epoch!=p.Epoch)return;if(v.Epoch==0)v.Epoch=p.Epoch;if(unchecked(p.Serial-v.Serial)<=0)return;v.Serial=p.Serial;v.Clock=Mathf.Min(v.Clock,UnityEngine.Time.time-p.Time);float age=Mathf.Max(0,UnityEngine.Time.time-p.Time-v.Clock);
            if(p.Kind==1){Array.Copy(p.F,v.F,16);Array.Copy(p.I,v.I,8);v.At=Time.time;return;}
            if(age>.5f)return;
            if(p.Kind==2){v.ShotAt=Time.time-age;if(p.I[0]%3==0)Add(p.Vehicle,p.I[0],p.A,p.B,false,age);v.MGSound.PlayOneShot(mgClip);}
            else if(p.Kind==3){Add(p.Vehicle,p.I[0],p.A,p.A+p.B*2,true);v.AASound.PlayOneShot(aaClip);}
            else if(p.Kind==4){var t=trails.Find(x=>x.Missile&&x.Vehicle==p.Vehicle&&x.Id==p.I[0]);if(t==null)Add(p.Vehicle,p.I[0],p.A-p.B*2,p.A,true);else{t.A=p.A-p.B*2;t.B=p.A;t.Until=Time.time+.25f;}}
            else if(p.Kind==5){for(int i=trails.Count-1;i>=0;i--)if(trails[i].Missile&&trails[i].Vehicle==p.Vehicle&&trails[i].Id==p.I[0]){UnityEngine.Object.Destroy(trails[i].Line.gameObject);trails.RemoveAt(i);}Add(p.Vehicle,p.I[0],p.A-Vector3.up*.2f,p.A+Vector3.up*.2f,false);}
        }
        static void Add(int vehicle,int id,Vector3 a,Vector3 b,bool missile,float age=0)
        {
            if(!missile&&age>=Vector3.Distance(a,b)/850f+.015f)return;
            if(trails.Count>=64){UnityEngine.Object.Destroy(trails[0].Line.gameObject);trails.RemoveAt(0);}var line=new GameObject(missile?"M1 AA trail":"M1 MG tracer").AddComponent<LineRenderer>();line.sharedMaterial=material;line.positionCount=2;line.useWorldSpace=true;line.startWidth=missile?.10f:.012f;line.endWidth=missile?.045f:.004f;line.startColor=missile?new Color(1,.7f,.3f):new Color(1,.85f,.4f);line.endColor=new Color(.8f,.7f,.5f,.1f);line.shadowCastingMode=ShadowCastingMode.Off;trails.Add(new Trail{Vehicle=vehicle,Id=id,A=a,B=b,Missile=missile,Line=line,Start=Time.time-age,Until=Time.time-age+(missile?.3f:Vector3.Distance(a,b)/850f+.015f)});
        }
        public static void Update(World w)
        {
            foreach(var v in views.Values)if(v.Flash!=null){float age=Time.time-v.ShotAt;bool active=age>=0&&age<.035f;v.Flash.gameObject.SetActive(active);if(active){v.FlashProperties.SetColor("_Color",new Color(1,1,1,(1-age/.035f)*(Optics.Scoped(v.Vehicle.entityId)?.4f:1)));v.Flash.GetComponent<Renderer>().SetPropertyBlock(v.FlashProperties);}}
            foreach(var v in views.Values)if(v.Yaw!=null&&!Weapons.Server){v.Yaw.localRotation=Quaternion.Euler(0,v.F[0],0);v.Pitch.localRotation=Quaternion.Euler(-v.F[1],0,0);v.AA.localRotation=Quaternion.Euler(-v.F[2],0,0);}
            foreach(var id in new List<int>(views.Keys))if(w.GetEntity(id)!=views[id].Vehicle){var v=views[id];if(v.Flash!=null)UnityEngine.Object.Destroy(v.Flash.gameObject);if(v.MGSound!=null)UnityEngine.Object.Destroy(v.MGSound);if(v.AASound!=null)UnityEngine.Object.Destroy(v.AASound);views.Remove(id);}
            for(int i=trails.Count-1;i>=0;i--){var t=trails[i];if(Time.time>=t.Until){UnityEngine.Object.Destroy(t.Line.gameObject);trails.RemoveAt(i);}else{var a=t.A;var b=t.B;if(!t.Missile){var delta=b-a;float length=delta.magnitude;float travel=Mathf.Min(length,Mathf.Max(0,Time.time-t.Start)*850);b=a+delta.normalized*travel;a+=delta.normalized*Mathf.Max(0,travel-2.5f);}t.Line.SetPosition(0,a-Origin.position);t.Line.SetPosition(1,b-Origin.position);}}
        }
        static string Reason(int n)=>n==0?"就绪":n==1?"超出射界":n==2?"枪口/车体遮挡":n==3?"待命":n==4?"转动对准中":n==5?"过热：松开扳机散热":n==6?"装填/冷却中":n==7?"货仓缺少弹药":n==8?"按住右键搜索":n==9?"无合法飞行目标（40～600米）":n==10?"目标遮挡/离开准星":n==11?"锁定中":"不可发射";
        public static bool HUD(EntityPlayerLocal p,EntityVehicle vehicle)
        {
            int seat=Weapons.Seat(vehicle,p.entityId);if(seat<0)return false;byte mode=Optics.Mode(vehicle,seat);string name=mode==1?"M1车顶机枪":mode==2?"M1双联防空导弹":"M1主炮";
            GUI.Label(new Rect(Screen.width/2-300,Screen.height-183,620,30),name+"  |  Alt+1 主炮 · Alt+2 机枪 · Alt+3 防空导弹");
            if(views.TryGetValue(vehicle.entityId,out var input)&&Time.time-input.At<1&&input.F[14+seat]<.5f)
                GUI.Box(new Rect(Screen.width/2-230,Screen.height/2+78,460,30),"切换/暂停后请先松开左键，再按下开火");
            if(mode==0)return false;
            if(!views.TryGetValue(vehicle.entityId,out var v)||Time.time-v.At>1){GUI.Box(new Rect(Screen.width/2-250,Screen.height-145,500,90),"等待服务器武器状态；请确保客户端/服务器均已更新 M1");return true;}
            int reason=v.I[mode==1?4:5];float age=Time.time-v.At;GUI.Box(new Rect(Screen.width/2-300,Screen.height-145,600,105),name+" · "+Reason(reason));
            string status=mode==1?"弹链 "+v.I[2]+" / 100 · 备用 "+v.I[6]+" · 热量 "+v.F[3].ToString("0")+"% · 换链 "+Mathf.Max(0,v.F[4]-age).ToString("0.0")+"s":"左管 "+Mathf.Max(0,v.F[5]-age).ToString("0.0")+"s · 右管 "+Mathf.Max(0,v.F[6]-age).ToString("0.0")+"s · 货仓 "+v.I[7]+" · 锁定 "+(v.F[8]*100).ToString("0")+"%";
            GUI.Label(new Rect(Screen.width/2-280,Screen.height-117,580,24),status);
            var target=GameManager.Instance.World.GetEntity(v.I[3]) as EntityAlive;
            string help=mode==1?"按住左键连射 · 右键瞄准 · Z变倍 · 打空自动换链":"按住右键锁定 · 左键单发 · 发射后可切回主炮";
            if(mode==2&&target!=null)help=target.EntityClass.entityClassName+" · "+Vector3.Distance(target.position,vehicle.position).ToString("0")+"m · "+help;
            GUI.Label(new Rect(Screen.width/2-280,Screen.height-91,580,45),help);
            var muzzle=mode==1?v.Muzzle:v.AA;Optics.Draw(p,vehicle,muzzle,muzzle.forward,reason);
            if(p.playerCamera!=null&&mode==2&&target!=null){var screen=p.playerCamera.WorldToScreenPoint(target.position-Origin.position+Vector3.up*.8f);if(screen.z>0)GUI.Box(new Rect(screen.x-24,Screen.height-screen.y-24,48,48),v.F[8]>=1&&v.F[9]>0?"锁定":"…");}return true;
        }
        public static void Clear(){foreach(var v in views.Values){if(v.Flash!=null)UnityEngine.Object.Destroy(v.Flash.gameObject);if(v.MGSound!=null)UnityEngine.Object.Destroy(v.MGSound);if(v.AASound!=null)UnityEngine.Object.Destroy(v.AASound);}views.Clear();foreach(var t in trails)UnityEngine.Object.Destroy(t.Line.gameObject);trails.Clear();foreach(var o in new UnityEngine.Object[]{material,mgClip,aaClip})if(o!=null)UnityEngine.Object.Destroy(o);material=null;mgClip=aaClip=null;}
    }
}
