using System.Collections.Generic;
using UnityEngine;

namespace AECT16RuntimeFix
{
    // Cosmetic-only objects. No colliders, damage callbacks or ammo mutation.
    public static class ApacheWeaponVisuals
    {
        private sealed class ProjectileVisual {public GameObject Object;public Vector3 Position,Velocity;public float Life;}
        private sealed class Tracer {public LineRenderer Line;public Vector3 A,B;public float Life;}
        private sealed class Turret {public EntityVehicle Vehicle;public GameObject Pivot,Mount;public Transform Barrel;public ApacheMuzzleFlash.Effect Flash;public Vector3 Direction;public float Heat,Recoil;public bool Overheated,StorageLocked;public float StatusTime=-100,Recovery,RocketWait;public int RocketAmmo,CannonAmmo;public byte AimReason;}
        private static readonly Dictionary<int,ProjectileVisual> projectiles=new Dictionary<int,ProjectileVisual>();
        private static readonly Dictionary<int,Turret> turrets=new Dictionary<int,Turret>();
        private static readonly List<Tracer> tracers=new List<Tracer>();
        private static readonly Stack<Tracer> tracerPool=new Stack<Tracer>();
        private static readonly List<int> remove=new List<int>();
        private static Material lightMaterial,gunMaterial,steelMaterial,darkMaterial;
        private static float nextNearbyScan;
        private static Material Material(bool gun)
        {
            if(gun ? gunMaterial!=null : lightMaterial!=null)return gun ? gunMaterial : lightMaterial;
            var shader=Shader.Find(gun ? "Standard" : "Sprites/Default") ?? Shader.Find("Unlit/Color");
            if(shader==null)return null;
            var material=new Material(shader){color=gun ? new Color(.17f,.19f,.16f) : new Color(1,.55f,.1f)};
            if(gun)gunMaterial=material;else lightMaterial=material;
            return material;
        }
        private static GameObject Primitive(PrimitiveType type,string name,Material material)
        {
            var obj=GameObject.CreatePrimitive(type);obj.name=name;obj.hideFlags=HideFlags.DontSave;
            var collider=obj.GetComponent<Collider>();if(collider!=null){collider.enabled=false;Object.Destroy(collider);}
            var renderer=obj.GetComponent<Renderer>();if(material!=null)renderer.sharedMaterial=material;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            return obj;
        }
        private static Turret GetTurret(EntityVehicle vehicle)
        {
            if(turrets.TryGetValue(vehicle.entityId,out var existing)&&existing.Vehicle==vehicle&&existing.Pivot!=null)return existing;
            if(existing!=null){ApacheGunnerPresentation.DestroyGun(existing.Pivot);if(existing.Mount!=null)Object.Destroy(existing.Mount);}
            var pivot=new GameObject("PZAEC_Apache_Cannon");pivot.hideFlags=HideFlags.DontSave;
            if(steelMaterial==null){steelMaterial=new Material(Material(true)){color=new Color(.25f,.28f,.29f)};if(steelMaterial.HasProperty("_Metallic"))steelMaterial.SetFloat("_Metallic",.7f);}
            if(darkMaterial==null)darkMaterial=new Material(Material(true)){color=new Color(.065f,.075f,.08f)};
            var mount=new GameObject("PZAEC_Apache_CannonMount");mount.hideFlags=HideFlags.DontSave;
            ApacheGunnerPresentation.Part(mount.transform,PrimitiveType.Cylinder,"AzimuthBearing",new Vector3(0,.29f,-.10f),new Vector3(.46f,.06f,.46f),Vector3.zero,steelMaterial);
            ApacheGunnerPresentation.Part(mount.transform,PrimitiveType.Cube,"Suspension",new Vector3(0,.17f,-.10f),new Vector3(.20f,.20f,.25f),Vector3.zero,Material(true));
            var barrel=ApacheGunnerPresentation.BuildGun(pivot.transform,Material(true),steelMaterial,darkMaterial);
            var turret=new Turret{Vehicle=vehicle,Pivot=pivot,Mount=mount,Barrel=barrel,Flash=ApacheMuzzleFlash.Create(barrel),Direction=ApacheWeapons.BodyRotation(vehicle)*Vector3.forward};
            turrets[vehicle.entityId]=turret;return turret;
        }
        public static void Clear()
        {
            foreach(var p in projectiles.Values)if(p.Object!=null)Object.Destroy(p.Object);
            foreach(var t in tracers)if(t.Line!=null)Object.Destroy(t.Line.gameObject);
            foreach(var t in turrets.Values){ApacheGunnerPresentation.DestroyGun(t.Pivot);if(t.Mount!=null)Object.Destroy(t.Mount);}
            foreach(var t in tracerPool)if(t.Line!=null)Object.Destroy(t.Line.gameObject);tracerPool.Clear();
            projectiles.Clear();tracers.Clear();turrets.Clear();
            ApacheMuzzleFlash.Clear();
            nextNearbyScan=0;
            if(lightMaterial!=null)Object.Destroy(lightMaterial);if(gunMaterial!=null)Object.Destroy(gunMaterial);
            if(steelMaterial!=null)Object.Destroy(steelMaterial);if(darkMaterial!=null)Object.Destroy(darkMaterial);
            lightMaterial=gunMaterial=steelMaterial=darkMaterial=null;
        }
        public static void Receive(World world,int vehicleId,int id,byte kind,Vector3 a,Vector3 b,float heat)
        {
            if(world?.GetPrimaryPlayer()==null)return;
            var vehicle=world.GetEntity(vehicleId) as EntityVehicle;
            if(kind==ApacheWeapons.PilotStatusEvent){ApachePilotHUD.Receive(world,vehicleId,id,a,b,heat);return;}
            if(kind==ApacheWeapons.GuidedMoveEvent){
                if(projectiles.TryGetValue(id,out var guided)){guided.Position=a;guided.Velocity=b;}
                return;
            }
            if(kind==ApacheWeapons.MarkerEvent){ApacheFlightAssist.ReceiveMark(world,vehicleId,a,heat);return;}
            if(kind==ApacheWeapons.ImpactEvent)
            {if(projectiles.TryGetValue(id,out var old)){if(old.Object!=null)Object.Destroy(old.Object);projectiles.Remove(id);}return;}
            if(!ApacheWeapons.IsApache(vehicle))return;
            if(kind==ApacheWeapons.StatusEvent)
            {
                var t=GetTurret(vehicle);t.StatusTime=Time.time;t.Heat=heat;t.Overheated=(id&1)!=0;t.StorageLocked=(id&2)!=0;
                t.RocketAmmo=Mathf.Max(0,Mathf.RoundToInt(a.x));t.CannonAmmo=Mathf.Max(0,Mathf.RoundToInt(a.y));t.RocketWait=a.z;t.Recovery=b.x;
                if(vehicle.GetAttached(1)!=world.GetPrimaryPlayer())t.AimReason=(byte)((id&8)!=0?2:(id&4)!=0?1:0);
            }
            else if(kind==ApacheWeapons.AimEvent)
            {var t=GetTurret(vehicle);t.Direction=b;}
            else if(kind==ApacheWeapons.CannonEvent)
            {
                var t=GetTurret(vehicle);t.Recoil=1;t.Direction=(b-a).normalized;t.Heat=heat;if(heat>=100)t.Overheated=true;
                ApacheMuzzleFlash.Trigger(t.Flash);
                // Visual start only; preserve server collision origin and hit result.
                // Server event already starts at the muzzle.
                var tracer=tracerPool.Count>0?tracerPool.Pop():new Tracer();
                if(tracer.Line==null)tracer.Line=new GameObject("PZAEC_Apache_Tracer").AddComponent<LineRenderer>();
                var line=tracer.Line;line.gameObject.SetActive(true);
                line.sharedMaterial=Material(false);line.startColor=new Color(1,.85f,.4f);line.endColor=new Color(1,.45f,.1f);
                line.startWidth=.035f;line.endWidth=.012f;line.positionCount=2;line.useWorldSpace=true;
                line.SetPosition(0,a-Origin.position);line.SetPosition(1,b-Origin.position);
                tracer.A=a;tracer.B=b;tracer.Life=.065f;tracers.Add(tracer);
                Audio.Manager.Play(vehicle,"pzApacheCannonFire",1,false);
            }
            else if(kind==ApacheWeapons.RocketEvent)
            {
                if(projectiles.ContainsKey(id))return;
                var obj=Primitive(PrimitiveType.Capsule,"PZAEC_Apache_Rocket",Material(false));
                obj.transform.localScale=new Vector3(.09f,.4f,.09f);
                obj.transform.position=a-Origin.position;obj.transform.rotation=Quaternion.FromToRotation(Vector3.up,b.normalized);
                var trail=obj.AddComponent<TrailRenderer>();trail.sharedMaterial=Material(false);trail.time=.4f;
                trail.startWidth=.09f;trail.endWidth=.015f;trail.startColor=new Color(1,.55f,.1f);trail.endColor=new Color(.5f,.5f,.5f,0);
                projectiles[id]=new ProjectileVisual{Object=obj,Position=a,Velocity=b,Life=ApacheWeaponRules.RocketLifetime+.5f};
                Audio.Manager.Play(vehicle,"pzApacheRocketFire",1,false);
            }
        }
        public static void Update(World world,float dt)
        {
            ApacheFlightAssist.Update(world);
            if(world.GetPrimaryPlayer()==null)return;
            var local=world.GetPrimaryPlayer().AttachedToEntity as EntityVehicle;
            if(ApacheWeapons.IsApache(local))GetTurret(local);
            // Parked and remotely crewed aircraft also show their turret before firing.
            if(Time.time>=nextNearbyScan){
                nextNearbyScan=Time.time+1;
                foreach(var entity in world.Entities.list){
                    var nearby=entity as EntityVehicle;
                    if(ApacheWeapons.IsApache(nearby)&&(nearby.position-world.GetPrimaryPlayer().position).sqrMagnitude<256*256)GetTurret(nearby);
                }
            }
            remove.Clear();
            foreach(var pair in projectiles)
            {
                var p=pair.Value;p.Position+=p.Velocity*Mathf.Min(Mathf.Max(0,dt),p.Life);p.Life-=dt;
                if(p.Life<=0||p.Object==null){if(p.Object!=null)Object.Destroy(p.Object);remove.Add(pair.Key);}
                else {p.Object.transform.position=p.Position-Origin.position;if(p.Velocity.sqrMagnitude>.01f)p.Object.transform.rotation=Quaternion.FromToRotation(Vector3.up,p.Velocity.normalized);}
            }
            foreach(int id in remove)projectiles.Remove(id);
            for(int i=tracers.Count-1;i>=0;i--)
            {
                var t=tracers[i];t.Life-=dt;
                if(t.Life<=0||t.Line==null){if(t.Line!=null){if(tracerPool.Count<64){t.Line.gameObject.SetActive(false);tracerPool.Push(t);}else Object.Destroy(t.Line.gameObject);}tracers.RemoveAt(i);}
                else {t.Line.SetPosition(0,t.A-Origin.position);t.Line.SetPosition(1,t.B-Origin.position);}
            }
            remove.Clear();
            foreach(var pair in turrets)
            {
                var t=pair.Value;
                if(t.Vehicle==null||world.GetEntity(pair.Key)!=t.Vehicle||t.Pivot==null){ApacheGunnerPresentation.DestroyGun(t.Pivot);if(t.Mount!=null)Object.Destroy(t.Mount);remove.Add(pair.Key);continue;}
                var state=ApacheWeapons.GetState(t.Vehicle);
                if(t.Vehicle.GetAttached(1)==null)t.Direction=ApacheWeapons.BodyRotation(t.Vehicle)*Vector3.forward;
                else if(t.Vehicle.GetAttached(1)==world.GetPrimaryPlayer())
                {
                    var sight=ApacheWeapons.SightRay(world.GetPrimaryPlayer());
                    t.AimReason=ApacheWeapons.ResolveAim(state,sight.origin,sight.direction,out var direction,out var muzzle);
                    if(t.AimReason==0)t.Direction=direction;
                }
                t.Pivot.transform.position=ApacheWeapons.CannonPivot(state)-Origin.position;
                if(t.Mount!=null){t.Mount.transform.position=t.Pivot.transform.position;t.Mount.transform.rotation=ApacheWeapons.BodyRotation(t.Vehicle);}
                t.Recoil=Mathf.Max(0,t.Recoil-dt*12);if(t.Barrel!=null)t.Barrel.localPosition=new Vector3(0,0,-.045f*t.Recoil);
                ApacheMuzzleFlash.Update(t.Flash);
                if(t.Direction.sqrMagnitude>.01f){var target=Quaternion.LookRotation(t.Direction,Vector3.up);t.Pivot.transform.rotation=t.Vehicle.GetAttached(1)==world.GetPrimaryPlayer()?target:Quaternion.Slerp(t.Pivot.transform.rotation,target,1-Mathf.Exp(-20*dt));}
                t.Heat=Mathf.Max(0,t.Heat-dt*ApacheWeaponRules.Cooling);
                // Overheat latch comes from the authoritative snapshot, not inferred heat.
            }
            foreach(int id in remove)turrets.Remove(id);
        }
        public static void DrawHUD(EntityPlayerLocal __instance)
        {
            if(__instance==null||__instance!=GameManager.Instance?.World?.GetPrimaryPlayer()||__instance.IsDead())return;
            var v=__instance.AttachedToEntity as EntityVehicle;if(!ApacheWeapons.IsApache(v))return;
            var ui=LocalPlayerUI.GetUIForPlayer(__instance);
            if(ui!=null&&(LocalPlayerUI.AnyModalWindowOpen()||ui.windowManager.IsCursorWindowOpen()||ui.windowManager.IsInputActive()))return;
            int seat=ApacheWeapons.Seat(v,__instance.entityId);if(seat<0)return;
            var ammo=ItemClass.GetItem(seat==0?ApacheWeaponRules.RocketAmmo:ApacheWeaponRules.CannonAmmo,false);
            int count=ammo!=null&&ammo.type>0&&v.bag!=null?v.bag.GetItemCount(ammo):0;
            if(!GameManager.Instance.GameIsFocused)return;
            turrets.TryGetValue(v.entityId,out var turret);
            bool fresh=turret!=null&&Time.time-turret.StatusTime<1;
            if(fresh)count=seat==0?turret.RocketAmmo:turret.CannonAmmo;
            byte reason=!fresh?(byte)5:turret.StorageLocked?(byte)3:seat==1?turret.AimReason:(byte)0;
            float elapsed=fresh?Time.time-turret.StatusTime:0;
            ApacheGunnerPresentation.Draw(v,seat,count,turret!=null?turret.Heat:0,turret!=null&&turret.Overheated,
                turret!=null?turret.Direction:ApacheWeapons.SightRay(__instance).direction,reason,
                fresh?Mathf.Max(0,turret.Recovery-elapsed):0,fresh?Mathf.Max(0,turret.RocketWait-elapsed):0);
        }
    }
}
