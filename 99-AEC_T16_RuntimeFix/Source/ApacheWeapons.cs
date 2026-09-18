using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace AECT16RuntimeFix
{
    // Only the server consumes cargo ammo, advances projectiles and invokes native damage.
    // Client packets contain intent/direction, never a hit result, damage, or ammo count.
    public static partial class ApacheWeapons
    {
        public const byte Aim = 0, Fire = 1, Stop = 2, Mark = 3;
        public const byte AimEvent = 0, CannonEvent = 1, RocketEvent = 2, ImpactEvent = 3, StatusEvent = 4, MarkerEvent = 5;
        private static bool enabled;
        private static World currentWorld;
        private static readonly Dictionary<int, State> states = new Dictionary<int, State>();
        private static readonly List<Rocket> rockets = new List<Rocket>();
        private static int serial, inputSequence;
        private static bool inputHeld;
        private static readonly List<int> removeStates=new List<int>();
        private static float nextInput, nextAim, nextError;
        private static int inputVehicle = -1, inputSeat = -1;
        public sealed partial class State
        {
            public EntityVehicle Vehicle;
            public Transform Mesh, Left, Right;
            public readonly ApacheWeaponRules.Gate Gate = new ApacheWeaponRules.Gate();
            public int SalvoRemaining, SalvoActor = -1, Side;
            public float NextSalvo, NextAim, NextBagSync;
            public bool BagDirty;
            public Vector3 AimDirection, SightOffset, SightDirection;
            public byte AimReason; public bool HasSight; public float NextStatus;
            public Vector3 MarkPoint; public float MarkUntil,NextMark,NextMarkSync;
            public readonly ApacheWeaponRules.TriggerLease MarkLease=new ApacheWeaponRules.TriggerLease();
            public readonly ApacheWeaponRules.TriggerLease[] Triggers={new ApacheWeaponRules.TriggerLease(),new ApacheWeaponRules.TriggerLease()};
        }
        private sealed class Rocket
        {
            public int Id, VehicleId, ShooterId;
            public EntityVehicle Vehicle;
            public Vector3 Position, Velocity;
            public float Age;
            public bool Guided;
            public EntityAlive Target;
            public Vector3 TargetOffset;
            public float NextSync;
        }
        public static bool Server { get { return ConnectionManager.Instance != null && ConnectionManager.Instance.IsServer; } }
        public static bool IsApache(EntityVehicle v)
        { return v != null && v.vehicle != null && string.Equals(v.vehicle.GetName(), MD500FlightControls.ApacheVehicleName, StringComparison.OrdinalIgnoreCase); }
        public static void Install(Harmony harmony)
        {
            try
            {
                harmony.Patch(AccessTools.Method(typeof(GameManager), "Update"),
                    postfix: new HarmonyMethod(typeof(ApacheWeapons), nameof(Update)));
                harmony.Patch(AccessTools.Method(typeof(GameManager), "SaveAndCleanupWorld"),
                    prefix: new HarmonyMethod(typeof(ApacheWeapons), nameof(Clear)));
                harmony.Patch(AccessTools.Method(typeof(EntityPlayerLocal), "OnGUI"),
                    postfix: new HarmonyMethod(typeof(ApacheWeaponVisuals), nameof(ApacheWeaponVisuals.DrawHUD)));
                ApacheFlightAssist.Install(harmony);
                enabled = true;
                Log.Out("[Apache-Weapons] Pilot rockets / gunner cannon enabled; server-authoritative cargo ammunition.");
            }
            catch (Exception ex) { enabled = false; Log.Error("[Apache-Weapons] Disabled: " + ex.GetBaseException().Message); }
        }
        public static void Clear()
        {
            states.Clear(); rockets.Clear(); currentWorld = null;
            nextInput = nextAim = 0; inputVehicle = inputSeat = -1; inputHeld=false;
            ApacheWeaponVisuals.Clear();
            ApacheAirframeAppearance.Clear();
            ApacheFlightAssist.Clear();
            ApachePilotHUD.Clear();localGuided=false;
        }
        private static void EnsureWorld(World world)
        { if (world != currentWorld) { Clear(); currentWorld = world; } }
        public static State GetState(EntityVehicle vehicle)
        {
            if (!states.TryGetValue(vehicle.entityId, out var state) || state.Vehicle != vehicle)
            {
                state = new State { Vehicle = vehicle };
                foreach (var t in vehicle.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "Rocket01") state.Left = t;
                    if (t.name == "Rocket02") state.Right = t;
                    if (t.name == "Mesh" && t.Find("Origin/TopPropellerJoint") != null) state.Mesh = t;
                }
                state.AimDirection = BodyRotation(vehicle) * Vector3.forward;
                states[vehicle.entityId] = state;
            }
            return state;
        }
        public static Quaternion BodyRotation(EntityVehicle v)
        { return v.vehicleRB != null ? v.vehicleRB.rotation : v.transform.rotation; }
        public static int Seat(EntityVehicle v, int actor)
        {
            for (int seat=0; seat<2; seat++) if (v.GetAttached(seat)?.entityId == actor) return seat;
            return -1;
        }
        public static bool InArc(EntityVehicle v, Vector3 direction)
        {
            if (!ApacheWeaponRules.ValidDirection(direction.x,direction.y,direction.z)) return false;
            var local = Quaternion.Inverse(BodyRotation(v)) * direction.normalized;
            return ApacheWeaponRules.InArc(local.x,local.y,local.z);
        }
        public static KeyCode FireKey(EntityVehicle v,int seat)
        {
            var fallback=seat==0?KeyCode.G:KeyCode.Mouse0;
            if(v.vehicle.Properties.Values.TryGetValue(seat==0?"pzApachePilotFireKey":"pzApacheGunnerFireKey",out var text)&&
                Enum.TryParse<KeyCode>(text,true,out var parsed)&&parsed!=KeyCode.None)return parsed;
            return fallback;
        }
        public static Vector3 CannonPivot(State state)
        {
            var local = CannonAnchorLocal;
            return state.Mesh != null ? state.Mesh.TransformPoint(local) + Origin.position :
                state.Vehicle.position + BodyRotation(state.Vehicle) * local;
        }
        public static Vector3 CannonAnchorLocal { get { return new Vector3(0,.35f,3.65f); } }
        private static bool ReadyOperator(State state, int actor, int seat)
        {
            var player = currentWorld?.GetEntity(actor) as EntityPlayer;
            bool storageOpen = LockManager.Instance != null && LockManager.Instance.IsLockedServer(state.Vehicle,0);
            return ApacheWeaponRules.OperatorAllowed(seat,actor,state.Vehicle.GetAttached(seat)?.entityId ?? -1,
                player == null || player.IsDead(),state.Vehicle.IsDead() || state.Vehicle.vehicle.GetHealth() <= 0,storageOpen);
        }
        private static bool Consume(State state, string name)
        {
            var value = ItemClass.GetItem(name, false);
            if (value == null || value.type == 0 || state.Vehicle.bag == null || state.Vehicle.bag.GetItemCount(value) < 1) return false;
            if (state.Vehicle.bag.DecItem(value,1) != 1) return false;
            state.BagDirty = true;
            return true;
        }
        public static Ray SightRay(EntityPlayerLocal player)
        {
            if(player.playerCamera==null)return player.GetLookRay();
            var ray=player.playerCamera.ViewportPointToRay(new Vector3(.5f,.5f,0));
            ray.origin+=Origin.position;return ray;
        }
        public static byte ResolveAim(State state,Vector3 origin,Vector3 view,out Vector3 direction,out Vector3 muzzle)
        {
            var pivot=CannonPivot(state);direction=view;muzzle=pivot;
            if(currentWorld==null||!ApacheWeaponRules.ValidDirection(view.x,view.y,view.z))return 1;
            view.Normalize();var target=origin+view*ApacheWeaponRules.CannonRange;
            if(Trace(state.Vehicle,origin,view,ApacheWeaponRules.CannonRange,out var sightHit))target=sightHit.hit.pos;
            var delta=target-pivot;
            if(delta.sqrMagnitude<2f)return 2;
            direction=delta.normalized;
            if(!InArc(state.Vehicle,direction))return 1;
            muzzle=pivot+direction*ApacheWeaponRules.MuzzleOffset;
            // Do not let the muzzle cross a wall even when the camera can see beyond it.
            if(Trace(state.Vehicle,pivot,direction,ApacheWeaponRules.MuzzleOffset,out var obstruction))return 2;
            return 0;
        }
        private static bool ValidSight(EntityVehicle vehicle,Vector3 origin,Vector3 direction)
        {
            return ApacheWeaponRules.ValidDirection(direction.x,direction.y,direction.z)&&
                ApacheWeaponRules.Finite(origin.x)&&ApacheWeaponRules.Finite(origin.y)&&ApacheWeaponRules.Finite(origin.z)&&
                (origin-vehicle.position).sqrMagnitude<=40*40;
        }        public static void Request(World world,int actor,int vehicleId,byte op,Vector3 direction,Vector3 origin,int sequence)
        {
            if(!enabled||!Server||world==null||op>GuidedFire)return;
            EnsureWorld(world);var vehicle=world.GetEntity(vehicleId) as EntityVehicle;
            if(!IsApache(vehicle))return;
            int seat=Seat(vehicle,actor);if(seat<0)return;
            var state=GetState(vehicle);
            if(op>=PilotAim){if(seat==0)PilotRequest(state,actor,op,origin,direction,sequence);return;}
            if(op==Mark){
                if(seat!=1||!ReadyOperator(state,actor,seat)||!ValidSight(vehicle,origin,direction)||
                    !state.MarkLease.Accept(actor,sequence,false,Time.time)||Time.time<state.NextMark)return;
                state.NextMark=Time.time+1;
                if(Trace(vehicle,origin,direction.normalized,ApacheWeaponRules.CannonRange,out var marked)){
                    state.MarkPoint=marked.hit.pos;state.MarkUntil=Time.time+15;
                }else state.MarkUntil=0;
                Broadcast(vehicle.entityId,actor,MarkerEvent,state.MarkPoint,Vector3.zero,Mathf.Max(0,state.MarkUntil-Time.time));
                return;
            }
            var trigger=state.Triggers[seat];
            if(!trigger.Accept(actor,sequence,op==Fire,Time.time))return;
            if(op==Stop){trigger.Stop();if(seat==0){state.SalvoRemaining=0;ResetPilot(state);state.GuidedSpent=false;}return;}
            if(seat==0){ResetPilot(state);trigger.Stop();return;}
            if(!ReadyOperator(state,actor,seat)){trigger.Stop();return;}
            if(seat==1){
                if(!ApacheWeaponRules.ValidDirection(direction.x,direction.y,direction.z)||
                    !ApacheWeaponRules.Finite(origin.x)||!ApacheWeaponRules.Finite(origin.y)||!ApacheWeaponRules.Finite(origin.z)||
                    (origin-vehicle.position).sqrMagnitude>40*40){trigger.Stop();state.HasSight=false;return;}
                state.SightOffset=origin-vehicle.position;state.SightDirection=direction.normalized;state.HasSight=true;
            }
        }
        private static void FireHeld(State s,int seat,float now)
        {
            var lease=s.Triggers[seat];int occupant=s.Vehicle.GetAttached(seat)?.entityId??-1;
            if(!lease.Active(occupant,now)||!ReadyOperator(s,lease.Actor,seat)){
                lease.Stop();if(seat==0)s.SalvoRemaining=0;return;
            }
            if(seat==1&&(!s.HasSight||s.AimReason!=0))return;
            if(seat==0&&!PilotCanFire(s,now))return;
            if(seat==0&&s.PilotGuided){FireGuided(s,lease.Actor,now);return;}
            if(!s.Gate.Ready(seat,now))return;
            if(seat==0){
                if(s.SalvoRemaining>0||!Consume(s,ApacheWeaponRules.RocketAmmo))return;
                s.Gate.Commit(0,now);s.SalvoActor=lease.Actor;s.SalvoRemaining=ApacheWeaponRules.SalvoSize-1;
                s.NextSalvo=now+ApacheWeaponRules.SalvoInterval;Launch(s,lease.Actor);
            }else{
                if(!Consume(s,ApacheWeaponRules.CannonAmmo))return;
                s.Gate.Commit(1,now);ShootCannon(s,lease.Actor);
            }
        }
        private static int Ammo(State s,string name){var item=ItemClass.GetItem(name,false);return item!=null&&item.type!=0&&s.Vehicle.bag!=null?s.Vehicle.bag.GetItemCount(item):0;}
        private static void SendStatus(State s,float now)
        {
            if(now>=s.NextMarkSync){s.NextMarkSync=now+1;Broadcast(s.Vehicle.entityId,0,MarkerEvent,s.MarkPoint,Vector3.zero,Mathf.Max(0,s.MarkUntil-now));}
            bool locked=LockManager.Instance!=null&&LockManager.Instance.IsLockedServer(s.Vehicle,0);
            int flags=(s.Gate.Overheated?1:0)|(locked?2:0)|(s.AimReason==1?4:0)|(s.AimReason==2?8:0);
            Broadcast(s.Vehicle.entityId,flags,StatusEvent,new Vector3(Ammo(s,ApacheWeaponRules.RocketAmmo),Ammo(s,ApacheWeaponRules.CannonAmmo),Mathf.Max(0,s.Gate.NextRocket-now)),
                new Vector3(s.Gate.Overheated?Mathf.Max(0,(s.Gate.Heat-ApacheWeaponRules.ResumeHeat)/ApacheWeaponRules.Cooling):0,0,0),s.Gate.Heat);
            SendPilotStatus(s,now);
        }
        public static void RocketKinematics(State state,bool right,out Vector3 origin,out Vector3 velocity)
        {
            var mount=right?state.Right:state.Left;var rotation=BodyRotation(state.Vehicle);
            origin=mount!=null?mount.position+Origin.position:state.Vehicle.position+rotation*new Vector3(right?1.87f:-1.892f,.924f,4.508f);
            var inherited=state.Vehicle.vehicleRB!=null?Vector3.ClampMagnitude(state.Vehicle.vehicleRB.velocity,40):Vector3.zero;
            Vector3 direction=state.PilotAiming?(state.PilotPoint-origin).normalized:rotation*Vector3.forward;
            float along=Vector3.Dot(inherited,direction);
            float lateral=Mathf.Max(0,inherited.sqrMagnitude-along*along);
            // Compensate inherited lateral velocity so the indicated sight point
            // is on the actual flight line, independently for both launchers.
            velocity=state.PilotAiming?direction*(along+Mathf.Sqrt(Mathf.Max(1,ApacheWeaponRules.RocketSpeed*ApacheWeaponRules.RocketSpeed-lateral))):direction*ApacheWeaponRules.RocketSpeed+inherited;
        }
        public static bool PredictRocket(EntityVehicle vehicle,bool right,out Vector3 point,out float seconds)
        {
            RocketKinematics(GetState(vehicle),right,out var origin,out var velocity);
            seconds=ApacheWeaponRules.RocketLifetime;point=origin+velocity*seconds;
            if(currentWorld==null||velocity.sqrMagnitude<.01f)return false;
            if(!Trace(vehicle,origin,velocity.normalized,velocity.magnitude*seconds,out var hit))return false;
            point=hit.hit.pos;seconds=Vector3.Distance(origin,point)/velocity.magnitude;return true;
        }
        private static void Launch(State state,int actor)
        {
            bool right=(state.Side++&1)!=0;RocketKinematics(state,right,out var origin,out var velocity);
            var rocket=new Rocket{Id=++serial,VehicleId=state.Vehicle.entityId,Vehicle=state.Vehicle,ShooterId=actor,Position=origin,Velocity=velocity,Guided=state.PilotGuided,Target=state.PilotGuided?state.LockTarget:null,TargetOffset=state.PilotGuided?state.LockOffset:Vector3.zero};
            rockets.Add(rocket);Broadcast(rocket.VehicleId,rocket.Id,RocketEvent,origin,velocity,rocket.Guided?1:0);
        }        // Raycast through the originating vehicle/crew only. All other geometry remains opaque.
        private static bool Trace(EntityVehicle vehicle, Vector3 start, Vector3 direction, float range, out WorldRayHitInfo hit)
        {
            hit = null;
            float remaining = range;
            for (int i=0;i<32 && remaining>.001f;i++)
            {
                if (!Voxel.Raycast(currentWorld,new Ray(start,direction),remaining,-538750997,8,0f)) return false;
                var candidate = Voxel.voxelRayHitInfo.Clone();
                var entity = ItemActionAttack.FindHitEntity(candidate);
                if (entity != null && vehicle != null && (entity == vehicle || entity == vehicle.GetAttached(0) || entity == vehicle.GetAttached(1)))
                {
                    float travelled = Mathf.Max(.1f,Vector3.Distance(start,candidate.hit.pos)+.15f);
                    start += direction*travelled; remaining -= travelled; continue;
                }
                hit = candidate; return true;
            }
            return false;
        }
        private static void ShootCannon(State state, int actor)
        {
            Vector3 direction=state.AimDirection, start=CannonPivot(state)+direction*ApacheWeaponRules.MuzzleOffset;
            Vector3 end = start + direction*ApacheWeaponRules.CannonRange;
            if (Trace(state.Vehicle,start,direction,ApacheWeaponRules.CannonRange,out var hit))
            {
                end=hit.hit.pos;
                var ammo=ItemClass.GetItem(ApacheWeaponRules.CannonAmmo,false);
                ItemActionAttack.Hit(hit,actor,EnumDamageTypes.Piercing,ApacheWeaponRules.CannonBlockDamage,
                    ApacheWeaponRules.CannonEntityDamage,1,1,0,.05f,"metal",new DamageMultiplier(),null,
                    new ItemActionAttack.AttackHitInfo(),0,1,1,null,null,ItemActionAttack.EnumAttackMode.RealNoHarvesting,
                    null,-1,ammo);
            }
            Broadcast(state.Vehicle.entityId,0,CannonEvent,start,end,state.Gate.Heat);
        }
        private static ExplosionData RocketExplosion()
        {
            var action=new DynamicProperties(); var explosion=new DynamicProperties();
            action.Classes.Add("Explosion",explosion);
            return new ExplosionData(action,null) { ParticleIndex=5, BlockRadius=5, EntityRadius=10,
                EntityDamage=ApacheWeaponRules.RocketEntityDamage, BlockDamage=ApacheWeaponRules.RocketBlockDamage, BlastPower=200 };
        }
        private static void AdvanceRockets(float delta)
        {
            for (int i=rockets.Count-1;i>=0;i--)
            {
                var r=rockets[i]; float dt=Mathf.Min(Mathf.Max(0,delta),ApacheWeaponRules.RocketLifetime-r.Age);
                r.Age += dt;
                GuideRocket(r,dt);
                Vector3 step=r.Velocity*dt;
                WorldRayHitInfo impact=null;
                bool hit=step.sqrMagnitude>.00001f && Trace(r.Vehicle,r.Position,step.normalized,step.magnitude,out impact);
                // Repeat-free impact: remove the projectile before invoking native damage callbacks.
                if (hit)
                {
                    rockets.RemoveAt(i);
                    Vector3 point=impact.hit.pos;
                    Broadcast(r.VehicleId,r.Id,ImpactEvent,point,Vector3.zero,0);
                    GameManager.Instance.ExplosionServer(point,new Vector3i(Mathf.FloorToInt(point.x),Mathf.FloorToInt(point.y),Mathf.FloorToInt(point.z)),
                        Quaternion.identity,r.Guided?GuidedExplosion():RocketExplosion(),r.ShooterId,0,false,ItemClass.GetItem(r.Guided?ApacheWeaponRules.GuidedAmmo:ApacheWeaponRules.RocketAmmo,false));
                }
                else if (r.Age>=ApacheWeaponRules.RocketLifetime)
                { rockets.RemoveAt(i); Broadcast(r.VehicleId,r.Id,ImpactEvent,r.Position,Vector3.zero,0); }
                else {r.Position += step;if(r.Guided&&Time.time>=r.NextSync){r.NextSync=Time.time+.1f;Broadcast(r.VehicleId,r.Id,GuidedMoveEvent,r.Position,r.Velocity,0);}}
            }
        }
        public static void Update()
        {
            if (!enabled) return;
            try
            {
                var game=GameManager.Instance; var world=game?.World;
                if (world == null) { if(currentWorld!=null) Clear(); return; }
                EnsureWorld(world);
                if (game.IsPaused()) return;
                if (Server)
                {
                    removeStates.Clear();
                    foreach(var pair in states)
                    {
                        var s=pair.Value;
                        if(s.Vehicle==null || world.GetEntity(pair.Key)!=s.Vehicle) {removeStates.Add(pair.Key);continue;}
                        s.Gate.Cool(Time.time);
                        UpdatePilot(s,Time.time);
                        if(s.HasSight&&s.Vehicle.GetAttached(1)!=null){s.AimReason=ResolveAim(s,s.Vehicle.position+s.SightOffset,s.SightDirection,out var aim,out var muzzle);if(s.AimReason==0)s.AimDirection=aim;}
                        FireHeld(s,0,Time.time);FireHeld(s,1,Time.time);
                        if((s.Vehicle.GetAttached(0)!=null||s.Vehicle.GetAttached(1)!=null)&&Time.time>=s.NextStatus){s.NextStatus=Time.time+.2f;SendStatus(s,Time.time);Broadcast(s.Vehicle.entityId,0,AimEvent,CannonPivot(s),s.AimDirection,s.Gate.Heat);}
                        if(s.SalvoRemaining>0 && Time.time>=s.NextSalvo)
                        {
                            if(!ReadyOperator(s,s.SalvoActor,0)||!PilotCanFire(s,Time.time)||s.PilotGuided||!Consume(s,ApacheWeaponRules.RocketAmmo)) s.SalvoRemaining=0;
                            else {s.SalvoRemaining--;s.NextSalvo=Time.time+ApacheWeaponRules.SalvoInterval;Launch(s,s.SalvoActor);}
                        }
                        if(s.BagDirty && Time.time>=s.NextBagSync)
                        { s.BagDirty=false;s.NextBagSync=Time.time+.2f;s.Vehicle.SendSyncData(EntityVehicle.cSyncStorage); }
                    }
                    foreach(int id in removeStates) states.Remove(id);
                    AdvanceRockets(Time.deltaTime);
                }else{removeStates.Clear();foreach(var pair in states)if(pair.Value.Vehicle==null||world.GetEntity(pair.Key)!=pair.Value.Vehicle)removeStates.Add(pair.Key);foreach(int id in removeStates)states.Remove(id);
                }
                LocalInput(world);
                ApacheWeaponVisuals.Update(world,Time.deltaTime);
            }
            catch(Exception ex)
            { if(Time.time>=nextError){nextError=Time.time+10;Log.Error("[Apache-Weapons] " + ex);} }
        }
        private static void ReleaseInput(World world,EntityPlayerLocal player)
        {
            var previous=world.GetEntity(inputVehicle) as EntityVehicle;
            if(player!=null&&IsApache(previous))SendIntent(player,previous,Stop,new Ray(Vector3.zero,Vector3.forward));
            inputHeld=false;
        }
        private static void LocalInput(World world)
        {
            var player=world.GetPrimaryPlayer();var v=player?.AttachedToEntity as EntityVehicle;
            if(!IsApache(v)||player.IsDead()){ReleaseInput(world,player);inputVehicle=inputSeat=-1;return;}
            int seat=Seat(v,player.entityId);if(seat<0){ReleaseInput(world,player);return;}
            if(inputVehicle!=v.entityId||inputSeat!=seat){ReleaseInput(world,player);inputVehicle=v.entityId;inputSeat=seat;nextInput=Time.time+.25f;nextAim=0;}
            var ui=LocalPlayerUI.GetUIForPlayer(player);
            if(!GameManager.Instance.GameIsFocused||(ui!=null&&(LocalPlayerUI.AnyModalWindowOpen()||ui.windowManager.IsCursorWindowOpen()||ui.windowManager.IsInputActive()))){ReleaseInput(world,player);nextInput=Time.time+.25f;return;}
            if(seat==1&&Time.time>=nextInput&&Input.GetKeyDown(ApacheFlightAssist.Key(v,"pzApacheMarkKey",KeyCode.Mouse2)))SendIntent(player,v,Mark,SightRay(player));
            if(seat==0){PilotInput(player,v);return;}
            bool held=Time.time>=nextInput&&Input.GetKey(FireKey(v,seat));
            if(held!=inputHeld||Time.time>=nextAim){
                nextAim=Time.time+.1f;SendIntent(player,v,held?Fire:inputHeld?Stop:Aim,SightRay(player));inputHeld=held;
            }
        }
        private static void SendIntent(EntityPlayerLocal player,EntityVehicle vehicle,byte op,Ray ray)
        {
            int sequence=unchecked(++inputSequence);
            if(Server)Request(currentWorld,player.entityId,vehicle.entityId,op,ray.direction,ray.origin,sequence);
            else ConnectionManager.Instance.SendToServer(NetPackageManager.GetPackage<NetPackagePZApacheIntent>().Setup(vehicle.entityId,op,ray.direction,ray.origin,sequence));
        }
        private static void Broadcast(int vehicle,int id,byte kind,Vector3 a,Vector3 b,float heat)
        {
            var packet=NetPackageManager.GetPackage<NetPackagePZApacheEvent>().Setup(vehicle,id,kind,a,b,heat);
            ConnectionManager.Instance.SendPackage(packet,false,-1,-1,vehicle,null,512);
            // Cosmetic failure must not cancel server damage or future salvo processing.
            try { ApacheWeaponVisuals.Receive(currentWorld,vehicle,id,kind,a,b,heat); }
            catch(Exception ex) { if(Time.time>=nextError){nextError=Time.time+10;Log.Warning("[Apache-Weapons] Visual: "+ex.Message);} }
        }
    }

    public sealed class NetPackagePZApacheIntent : NetPackage
    {
        public int Vehicle,Sequence; public byte Op; public Vector3 Direction,Origin;
        public override NetPackageDirection PackageDirection {get{return NetPackageDirection.ToServer;}}
        public NetPackagePZApacheIntent Setup(int vehicle,byte op,Vector3 direction,Vector3 origin,int sequence){Vehicle=vehicle;Op=op;Direction=direction;Origin=origin;Sequence=sequence;return this;}
        public override int GetLength(){return 33;}
        public override void read(PooledBinaryReader r){Vehicle=r.ReadInt32();Op=r.ReadByte();Direction=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());Origin=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());Sequence=r.ReadInt32();}
        public override void write(PooledBinaryWriter w){base.write(w);w.Write(Vehicle);w.Write(Op);w.Write(Direction.x);w.Write(Direction.y);w.Write(Direction.z);w.Write(Origin.x);w.Write(Origin.y);w.Write(Origin.z);w.Write(Sequence);}
        public override void ProcessPackage(World world,GameManager callbacks)
        {if(world!=null&&ApacheWeapons.Server&&Sender!=null&&Sender.loginDone&&Sender.bAttachedToEntity)ApacheWeapons.Request(world,Sender.entityId,Vehicle,Op,Direction,Origin,Sequence);}
    }
    public sealed class NetPackagePZApacheEvent : NetPackage
    {
        public int Vehicle,Id;public byte Kind;public Vector3 A,B;public float Heat;
        public override NetPackageDirection PackageDirection {get{return NetPackageDirection.ToClient;}}
        public NetPackagePZApacheEvent Setup(int v,int id,byte kind,Vector3 a,Vector3 b,float heat){Vehicle=v;Id=id;Kind=kind;A=a;B=b;Heat=heat;return this;}
        public override int GetLength(){return 37;}
        public override void read(PooledBinaryReader r){Vehicle=r.ReadInt32();Id=r.ReadInt32();Kind=r.ReadByte();A=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());B=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());Heat=r.ReadSingle();}
        public override void write(PooledBinaryWriter w){base.write(w);w.Write(Vehicle);w.Write(Id);w.Write(Kind);w.Write(A.x);w.Write(A.y);w.Write(A.z);w.Write(B.x);w.Write(B.y);w.Write(B.z);w.Write(Heat);}
        public override void ProcessPackage(World world,GameManager callbacks)
        {if(world!=null&&!ApacheWeapons.Server)ApacheWeaponVisuals.Receive(world,Vehicle,Id,Kind,A,B,Heat);}
    }
}
