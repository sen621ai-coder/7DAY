using UnityEngine;

namespace AECT16RuntimeFix
{
    // Local presentation only. Never performs targeting, damage or input mutation.
    public static class ApacheGunnerPresentation
    {
        private static GUIStyle label, small, number;
        private static readonly Color Green=new Color(.48f,.93f,.75f), Amber=new Color(1,.72f,.27f), Red=new Color(1,.35f,.3f);
        public static GameObject Part(Transform parent,PrimitiveType type,string name,Vector3 position,Vector3 scale,Vector3 angles,Material material)
        {
            var obj=GameObject.CreatePrimitive(type);obj.name=name;obj.hideFlags=HideFlags.DontSave;
            var collider=obj.GetComponent<Collider>();if(collider!=null){collider.enabled=false;Object.Destroy(collider);}
            obj.transform.SetParent(parent,false);obj.transform.localPosition=position;
            obj.transform.localScale=scale;obj.transform.localRotation=Quaternion.Euler(angles);
            var renderer=obj.GetComponent<Renderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            return obj;
        }
        public static Transform BuildGun(Transform pivot,Material olive,Material steel,Material dark)
        {
            Part(pivot,PrimitiveType.Cube,"Receiver",new Vector3(0,0,.06f),new Vector3(.27f,.24f,.48f),Vector3.zero,olive);
            Part(pivot,PrimitiveType.Cube,"ReceiverTop",new Vector3(0,.145f,.02f),new Vector3(.21f,.055f,.38f),Vector3.zero,steel);
            Part(pivot,PrimitiveType.Cube,"FeedHousing",new Vector3(-.19f,-.015f,-.03f),new Vector3(.15f,.17f,.24f),new Vector3(0,0,-12),olive);
            Part(pivot,PrimitiveType.Cube,"DriveHousing",new Vector3(.175f,-.01f,.02f),new Vector3(.10f,.14f,.29f),Vector3.zero,steel);
            for(int side=-1;side<=1;side+=2){
                Part(pivot,PrimitiveType.Cube,"CradleArm",new Vector3(side*.21f,.035f,-.14f),new Vector3(.055f,.29f,.24f),new Vector3(12,0,0),olive);
                Part(pivot,PrimitiveType.Cylinder,"Trunnion",new Vector3(side*.25f,.06f,-.09f),new Vector3(.14f,.035f,.14f),new Vector3(0,0,90),steel);
            }
            var assembly=new GameObject("RecoilAssembly");assembly.hideFlags=HideFlags.DontSave;assembly.transform.SetParent(pivot,false);
            var barrel=assembly.transform;
            Part(barrel,PrimitiveType.Cylinder,"BarrelJacket",new Vector3(0,0,.43f),new Vector3(.105f,.18f,.105f),new Vector3(90,0,0),steel);
            Part(barrel,PrimitiveType.Cylinder,"BarrelTube",new Vector3(0,0,.89f),new Vector3(.055f,.31f,.055f),new Vector3(90,0,0),dark);
            for(int i=0;i<3;i++)Part(barrel,PrimitiveType.Cylinder,"Collar",new Vector3(0,0,.31f+i*.13f),new Vector3(.12f,.025f,.12f),new Vector3(90,0,0),dark);
            // An actual open ring, rather than a solid cylinder cap painted black.
            var muzzle=new GameObject("OpenMuzzle");muzzle.hideFlags=HideFlags.DontSave;muzzle.transform.SetParent(barrel,false);
            muzzle.transform.localPosition=new Vector3(0,0,1.20f);
            muzzle.AddComponent<MeshFilter>().sharedMesh=MuzzleMesh();
            var renderer=muzzle.AddComponent<MeshRenderer>();renderer.sharedMaterial=steel;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            return barrel;
        }
        // Four rings: outer rear/front and inner rear/front. Owned by this turret.
        private static Mesh MuzzleMesh()
        {
            const int n=16;var vertices=new Vector3[n*4];var triangles=new int[n*24];int k=0;
            for(int ring=0;ring<4;ring++)for(int i=0;i<n;i++){
                float a=i*Mathf.PI*2/n,r=ring<2?.046f:.027f;
                vertices[ring*n+i]=new Vector3(Mathf.Cos(a)*r,Mathf.Sin(a)*r,(ring%2)*.095f);
            }
            for(int i=0;i<n;i++){
                int j=(i+1)%n;
                Quad(triangles,ref k,i,j,n+j,n+i);Quad(triangles,ref k,2*n+j,2*n+i,3*n+i,3*n+j);
                Quad(triangles,ref k,n+i,n+j,3*n+j,3*n+i);Quad(triangles,ref k,j,i,2*n+i,2*n+j);
            }
            var mesh=new Mesh{name="Apache open muzzle"};mesh.vertices=vertices;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        private static void Quad(int[] t,ref int k,int a,int b,int c,int d){t[k++]=a;t[k++]=b;t[k++]=c;t[k++]=a;t[k++]=c;t[k++]=d;}
        public static void DestroyGun(GameObject pivot)
        {
            if(pivot==null)return;
            var muzzle=pivot.transform.Find("RecoilAssembly/OpenMuzzle");
            if(muzzle!=null)Object.Destroy(muzzle.GetComponent<MeshFilter>().sharedMesh);
            Object.Destroy(pivot);
        }
        private static void Box(float x,float y,float w,float h,Color color)
        {GUI.color=color;GUI.DrawTexture(new Rect(x,y,w,h),Texture2D.whiteTexture);}
        private static void Text(float x,float y,float w,float h,string text,GUIStyle style,Color color)
        {GUI.color=color;GUI.Label(new Rect(x,y,w,h),text,style);}
        private static string L(string key){return Localization.Get("pzApache"+key);}
        public static void Draw(EntityVehicle vehicle,int seat,int ammo,float heat,bool hot,Vector3 direction,byte reason=0,float recovery=0,float cooldown=0)
        {
            if(label==null){label=new GUIStyle(GUI.skin.label){fontSize=14};small=new GUIStyle(label){fontSize=12};number=new GUIStyle(label){fontSize=28,fontStyle=FontStyle.Bold};}
            var matrix=GUI.matrix;var color=GUI.color;
            float scale=Mathf.Min(Screen.width/1280f,Screen.height/720f);
            try{
                GUI.matrix=Matrix4x4.TRS(new Vector3((Screen.width-1280*scale)/2,(Screen.height-720*scale)/2,0),Quaternion.identity,new Vector3(scale,scale,1));
                if(seat==0){ApachePilotHUD.Draw(vehicle);return;}
                ApacheFlightAssist.Draw(vehicle,seat);
                var local=Quaternion.Inverse(ApacheWeapons.BodyRotation(vehicle))*direction.normalized;
                float yaw=Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg,pitch=Mathf.Atan2(local.y,new Vector2(local.x,local.z).magnitude)*Mathf.Rad2Deg;
                bool arc=reason!=1&&(seat==0||ApacheWeapons.InArc(vehicle,direction));
                bool blocked=reason!=0||ammo==0||!arc||(seat==1&&hot);
                var tint=blocked?Red:(seat==1&&heat>=75?Amber:Green);
                string status=reason==5?L("Syncing"):reason==3?L("StorageBusy"):reason==2?L("MuzzleBlocked"):ammo==0?L("Empty"):!arc?L("OutOfArc"):seat==1&&hot?L("Overheated")+" "+recovery.ToString("0.0")+"s":seat==0&&cooldown>0?L("Reload")+" "+cooldown.ToString("0.0")+"s":L("Ready");
                Box(360,515,560,96,new Color(.025f,.055f,.06f,.88f));Box(360,515,3,96,tint);
                Text(376,523,325,22,L(seat==0?"PilotHUD":"GunnerHUD"),label,Green);
                Text(713,523,198,22,status,label,tint);
                Text(376,550,100,38,ammo.ToString("D3"),number,tint);
                Text(483,551,220,20,L("CargoAmmo"),small,Green);
                Text(483,573,240,20,"["+ApacheWeapons.FireKey(vehicle,seat)+"]  "+L("Fire"),small,Green);
                if(seat==1){
                    Text(713,549,205,20,L("Heat")+"  "+Mathf.RoundToInt(heat)+"%",small,tint);
                    Box(714,575,188,7,new Color(.18f,.25f,.25f));Box(714,575,188*Mathf.Clamp01(heat/100),7,tint);
                    Box(714+188*ApacheWeaponRules.ResumeHeat/100,572,1,13,Green);
                    Text(713,587,205,20,L("Resume"),small,new Color(.65f,.73f,.7f));
                    // Open-center reticle preserves visibility of small targets.
                    Box(612,359,18,2,tint);Box(650,359,18,2,tint);Box(639,332,2,18,tint);Box(639,370,2,18,tint);
                    for(int s=-1;s<=1;s+=2){float x=640+s*46;Box(x,328,2,12,tint);Box(x,380,2,12,tint);Box(s<0?x:x-10,328,12,2,tint);Box(s<0?x:x-10,390,12,2,tint);}
                    Text(695,346,220,22,status,small,tint);
                    Text(553,401,280,22,L("DirectionSight"),small,Green);
                    // Mechanical limits, not a lock-on indicator or a range measurement.
                    Box(510,455,260,2,Green);
                    for(int i=0;i<=4;i++)Box(510+i*65,451,1,10,Green);
                    Box(510+(Mathf.Clamp(yaw,-100,100)+100)/200*260-3,449,6,14,tint);
                    Text(510,468,270,20,L("Azimuth")+" "+yaw.ToString("+0;-0;0")+"° / ±100°",small,tint);
                    Box(797,285,2,140,Green);Box(793,285,10,1,Green);Box(793,425,10,1,Green);
                    Box(791,285+(15-Mathf.Clamp(pitch,-70,15))/85*140-3,14,6,tint);
                    Text(813,285,200,20,"+15°",small,Green);Text(813,406,200,20,"-70°",small,Green);
                    Text(813,347,220,20,L("Elevation")+" "+pitch.ToString("+0;-0;0")+"°",small,tint);
                }else Text(713,554,205,40,L("SalvoHint"),small,Green);
            }finally{GUI.matrix=matrix;GUI.color=color;}
        }
    }
}
