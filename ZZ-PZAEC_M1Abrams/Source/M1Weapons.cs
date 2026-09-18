using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace PZAEC.M1
{
    public static class Weapons
    {
        public const byte Aim=0,Fire=1,Stop=2,SwitchAmmo=3,Repair=4,RepairStop=5,ShotEvent=1,StateEvent=2,ImpactEvent=3;
        public sealed class State
        {
            public EntityVehicle Vehicle;public Transform Model,YawNode,PitchNode,RecoilNode;
            public readonly Rules.Trigger Trigger=new Rules.Trigger();
            public readonly Rules.Trigger RepairTrigger=new Rules.Trigger();
            public bool AP;public float LastDamage=-100,RepairStarted=-1;
            public int Epoch,Sequence,Shot,Actor=-1,Reason;public float Yaw,Pitch,NextFire,NextStatus,LastShot=-100;
            public Vector3 SightOffset,View=Vector3.forward;public bool HasSight;
        }
        sealed class Shell{public EntityVehicle Vehicle;public int Epoch,Id,Actor,Tier;public bool AP;public Vector3 Position,Velocity;public float Age,Distance;}
        public static readonly Dictionary<int,State> States=new Dictionary<int,State>();
        static readonly List<Shell> shells=new List<Shell>();static readonly List<int> removed=new List<int>();
        static World world;static int generation=Environment.TickCount,sequence,inputVehicle=-1,repairVehicle=-1;static bool held;static float nextInput,nextError,nextDiscovery,nextRepair;
        public static bool Server=>ConnectionManager.Instance!=null&&ConnectionManager.Instance.IsServer;
        public static int RepairTarget=>repairVehicle;
        public static bool IsTank(EntityVehicle v)=>v?.vehicle!=null&&Rules.Index(v.vehicle.GetName())>=0;
        public static int Tier(EntityVehicle v)=>Rules.Index(v.vehicle.GetName());
        public static Rules.Spec Spec(EntityVehicle v)=>Rules.Specs[Tier(v)];
        public static Quaternion Body(EntityVehicle v)=>v.vehicleRB!=null?v.vehicleRB.rotation:v.transform.rotation;
        public static int Seat(EntityVehicle v,int actor){for(int i=0;i<2;i++)if(v.GetAttached(i)?.entityId==actor)return i;return -1;}
        public static bool Allowed(EntityVehicle v,int actor)=>Rules.Operator(Seat(v,actor),v.GetAttached(1)!=null);
        public static void Install(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(GameManager),"Update"),postfix:new HarmonyMethod(typeof(Weapons),nameof(Update)));
            h.Patch(AccessTools.Method(typeof(GameManager),"SaveAndCleanupWorld"),prefix:new HarmonyMethod(typeof(Weapons),nameof(Clear)));
            Presentation.Install(h);
            Combat.Install(h);Service.Install(h);
        }
        public static void Clear(){States.Clear();shells.Clear();world=null;held=false;inputVehicle=-1;repairVehicle=-1;nextRepair=0;nextInput=0;nextDiscovery=0;Presentation.Clear();}
        public static State Register(EntityVehicle v)
        {
            if(!States.TryGetValue(v.entityId,out var s)||s.Vehicle!=v){
                Transform model=null;foreach(var t in (v.PhysicsTransform!=null?v.PhysicsTransform:v.transform).GetComponentsInChildren<Transform>(true))if(t.name=="M1Visual"){model=t;break;}
                s=new State{Vehicle=v,Model=model,Epoch=unchecked(++generation)};States[v.entityId]=s;
                if(model!=null){s.YawNode=model.Find("TurretYaw");s.PitchNode=s.YawNode?.Find("GunPitch");s.RecoilNode=s.PitchNode?.Find("GunRecoil");}
            }return s;
        }
        static bool Ready(State s,int actor)
        {
            var p=world?.GetEntity(actor) as EntityPlayer;
            return p!=null&&!p.IsDead()&&!s.Vehicle.IsDead()&&s.Vehicle.vehicle.GetHealth()>0&&Allowed(s.Vehicle,actor)&&
                !(LockManager.Instance!=null&&LockManager.Instance.IsLockedServer(s.Vehicle,0));
        }
        static bool Valid(Vector3 v)=>Rules.Finite(v.x)&&Rules.Finite(v.y)&&Rules.Finite(v.z);
        public static void Request(World w,int actor,int vehicle,byte op,int serial,Vector3 origin,Vector3 direction)
        {
            if(!Server||w==null||op>RepairStop)return;var v=w.GetEntity(vehicle) as EntityVehicle;
            if(!IsTank(v)||!Valid(origin)||!Valid(direction)||direction.sqrMagnitude<.5f||direction.sqrMagnitude>1.5f||(origin-v.position).sqrMagnitude>40*40)return;
            if(world!=w){Clear();world=w;}
            var s=Register(v);if(op>=Repair){Service.Request(w,actor,s,op,serial,origin,direction.normalized);return;}
            if(!Allowed(v,actor)||!s.Trigger.Accept(actor,serial,op==Fire,Time.time))return;
            if(op==Stop||!Ready(s,actor)){s.Trigger.Stop();return;}
            if(op==SwitchAmmo){s.AP=!s.AP;s.NextFire=Mathf.Max(s.NextFire,Time.time+Spec(v).Reload);s.Trigger.Stop();}
            s.Actor=actor;s.SightOffset=origin-v.position;s.View=direction.normalized;s.HasSight=true;
        }
        public static Vector3 Pivot(State s)
        {
            var local=Model.YawPivot+Quaternion.Euler(0,s.Yaw,0)*(Model.PitchPivot-Model.YawPivot);
            return s.Model!=null?s.Model.TransformPoint(local)+Origin.position:s.Vehicle.position+Body(s.Vehicle)*local;
        }
        public static Vector3 Direction(State s)=>Body(s.Vehicle)*Quaternion.Euler(0,s.Yaw,0)*Quaternion.Euler(-s.Pitch,0,0)*Vector3.forward;
        public static bool Trace(EntityVehicle v,Vector3 start,Vector3 direction,float range,out WorldRayHitInfo hit)
        {
            hit=null;float remaining=range;
            for(int i=0;i<32&&remaining>.001f;i++){
                if(!Voxel.Raycast(world,new Ray(start,direction),remaining,-538750997,8,0))return false;
                var candidate=Voxel.voxelRayHitInfo.Clone();var entity=ItemActionAttack.FindHitEntity(candidate);
                if(entity!=null&&(entity==v||entity==v.GetAttached(0)||entity==v.GetAttached(1))){hit=candidate;float d=Mathf.Max(.1f,Vector3.Distance(start,candidate.hit.pos)+.15f);start+=direction*d;remaining-=d;continue;}
                hit=candidate;return true;
            }
            // Exhausted self-skip budget: fail closed for obstruction queries.
            return remaining>.001f&&hit!=null;
        }
        static void AimGun(State s,float dt)
        {
            if(!s.HasSight||!Ready(s,s.Actor)){s.Trigger.Stop();s.HasSight=false;s.Reason=3;return;}
            var origin=s.Vehicle.position+s.SightOffset;var target=origin+s.View*Rules.Range;
            if(Trace(s.Vehicle,origin,s.View,Rules.Range,out var sight))target=sight.hit.pos;
            var delta=target-Pivot(s);if(delta.sqrMagnitude<(Model.BarrelLength+.25f)*(Model.BarrelLength+.25f)){s.Reason=2;return;}
            var local=Quaternion.Inverse(Body(s.Vehicle))*delta.normalized;
            float yaw=Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg,pitch=Mathf.Asin(Mathf.Clamp(local.y,-1,1))*Mathf.Rad2Deg;
            float lower=Rules.MinPitch(yaw);bool outside=pitch<lower||pitch>20;
            var spec=Spec(s.Vehicle);float damaged=s.Vehicle.vehicle.GetHealthPercent()<.3f?.75f:1;
            s.Yaw=Mathf.MoveTowardsAngle(s.Yaw,yaw,spec.Yaw*damaged*dt);s.Pitch=Mathf.MoveTowards(s.Pitch,Mathf.Clamp(pitch,lower,20),spec.Pitch*dt);
            s.Pitch=Mathf.Max(s.Pitch,Rules.MinPitch(s.Yaw));
            // Sweep clearance uses the actual authoritative orientation, not the requested aim.
            s.Reason=outside?1:Quaternion.Angle(Quaternion.LookRotation(Direction(s)),Quaternion.LookRotation(delta))>1.5f?4:0;
            if(Trace(s.Vehicle,Pivot(s),Direction(s),Model.BarrelLength+.15f,out var obstruction))s.Reason=2;
        }
        public static int Ammo(State s)
        {var item=ItemClass.GetItem(Rules.AmmoName(s.AP),false);return item!=null&&item.type!=0&&s.Vehicle.bag!=null?s.Vehicle.bag.GetItemCount(item):0;}
        static void Shoot(State s)
        {
            if(!s.Trigger.Active(Time.time)||!Ready(s,s.Trigger.Actor)||s.Reason!=0||Time.time<s.NextFire)return;
            var ammo=ItemClass.GetItem(Rules.AmmoName(s.AP),false);if(ammo==null||ammo.type==0||s.Vehicle.bag==null||s.Vehicle.bag.GetItemCount(ammo)<1||s.Vehicle.bag.DecItem(ammo,1)!=1)return;
            s.NextFire=Time.time+Spec(s.Vehicle).Reload;s.LastShot=Time.time;s.Shot++;s.Vehicle.SendSyncData(EntityVehicle.cSyncStorage);
            var direction=Direction(s);var muzzle=Pivot(s)+direction*Model.BarrelLength;
            var velocity=direction*Rules.ShellSpeed(s.AP)+(s.Vehicle.vehicleRB!=null?Vector3.ClampMagnitude(s.Vehicle.vehicleRB.velocity,20):Vector3.zero);
            shells.Add(new Shell{Vehicle=s.Vehicle,Epoch=s.Epoch,Id=s.Shot,Actor=s.Trigger.Actor,Tier=Tier(s.Vehicle),AP=s.AP,Position=muzzle,Velocity=velocity});
            Broadcast(s,ShotEvent,s.Shot,muzzle,velocity,Spec(s.Vehicle).Reload,s.AP?1:0);
            // Native noise is server-only and separate from the local sound layers.
            Audio.Manager.SignalAI(s.Vehicle,muzzle,"pzM1CannonNoise",1);
            RecoilImpulse(s.Vehicle,direction);
        }
        public static void RecoilImpulse(EntityVehicle vehicle,Vector3 direction)
        {if(!vehicle.isEntityRemote&&vehicle.RBActive&&vehicle.vehicleRB!=null&&!vehicle.vehicleRB.isKinematic&&vehicle.GetWheelsOnGround()>0)vehicle.vehicleRB.AddForce(-direction*.04f,ForceMode.VelocityChange);}
        static void Advance(float delta)
        {
            for(int i=shells.Count-1;i>=0;i--){
                var shell=shells[i];float lifetime=Rules.Range/Rules.ShellSpeed(shell.AP);float left=Mathf.Min(Mathf.Max(delta,0),lifetime-shell.Age);bool finished=false;
                while(left>0&&!finished){
                    float dt=Mathf.Min(.025f,left);left-=dt;var step=Vector3.ClampMagnitude(shell.Velocity*dt+Vector3.down*(4.905f*dt*dt),Mathf.Max(0,Rules.Range-shell.Distance));
                    if(Trace(shell.Vehicle,shell.Position,step.normalized,step.magnitude,out var impact)){
                        shells.RemoveAt(i);finished=true;var point=impact.hit.pos;
                        if(States.TryGetValue(shell.Vehicle.entityId,out var s)&&s.Epoch==shell.Epoch)Broadcast(s,ImpactEvent,shell.Id,point,-shell.Velocity.normalized,0,shell.AP?1:0);
                        Combat.Impact(world,shell.Vehicle,shell.Actor,shell.Tier,shell.AP,impact,shell.Velocity.normalized);
                    }else{shell.Position+=step;shell.Distance+=step.magnitude;shell.Velocity+=Vector3.down*(9.81f*dt);shell.Age+=dt;if(shell.Distance>=Rules.Range-.001f){shell.Age=lifetime;break;}}
                }
                if(!finished&&shell.Age>=lifetime){shells.RemoveAt(i);if(States.TryGetValue(shell.Vehicle.entityId,out var s))Broadcast(s,ImpactEvent,shell.Id,shell.Position,Vector3.zero,1,0);}
            }
        }
        static void Broadcast(State s,byte kind,int shot,Vector3 a,Vector3 b,float x,float y)
        {
            var packet=NetPackageM1Event.Make(s.Vehicle.entityId,s.Epoch,++s.Sequence,shot,kind,Time.time,a,b,x,y);
            ConnectionManager.Instance.SendPackage(packet,false,-1,-1,s.Vehicle.entityId,null,512);
            try{Presentation.Receive(world,packet);}catch(Exception e){Log.Warning("[M1] Cosmetic event: "+e.Message);}
        }
        public static void Update()
        {
            try{
                var w=GameManager.Instance?.World;if(w==null){if(world!=null)Clear();return;}
                if(world!=w){Clear();world=w;}if(GameManager.Instance.IsPaused())return;
                // PostInit can precede world registration; discover tanks already loaded.
                if(Time.time>=nextDiscovery){nextDiscovery=Time.time+1;foreach(var entity in w.Entities.list)if(entity is EntityVehicle v&&IsTank(v))Register(v);}
                removed.Clear();foreach(var entry in States){var s=entry.Value;
                    if(w.GetEntity(entry.Key)!=s.Vehicle){removed.Add(entry.Key);continue;}
                    if(Server){AimGun(s,Time.deltaTime);Shoot(s);Service.Update(w,s);
                        // Dedicated servers also need the authoritative turret collider pose.
                        if(s.YawNode!=null)s.YawNode.localRotation=Quaternion.Euler(0,s.Yaw,0);
                        if(s.PitchNode!=null)s.PitchNode.localRotation=Quaternion.Euler(-s.Pitch,0,0);
                        if(s.RecoilNode!=null)s.RecoilNode.localPosition=Vector3.back*Rules.Recoil(Time.time-s.LastShot);
                        if(Time.time>=s.NextStatus){s.NextStatus=Time.time+(s.Vehicle.hasDriver||s.Vehicle.GetAttached(1)!=null||s.RepairStarted>=0?.1f:1f);Broadcast(s,StateEvent,s.Shot,new Vector3(s.Yaw,s.Pitch,s.Reason),new Vector3(Mathf.Max(0,Time.time-s.LastShot),s.AP?1:0,s.RepairStarted<0?0:Mathf.Max(0,8-(Time.time-s.RepairStarted))),Mathf.Max(0,s.NextFire-Time.time),Ammo(s));}
                    }
                }
                foreach(int id in removed)States.Remove(id);
                if(Server)Advance(Time.deltaTime);
                InputUpdate(w);Presentation.Update(w);
            }catch(Exception e){if(Time.time>=nextError){nextError=Time.time+10;Log.Error("[M1-Abrams] "+e);}}
        }
        public static bool UIReady(EntityPlayerLocal p)
        {var ui=LocalPlayerUI.GetUIForPlayer(p);return GameManager.Instance.GameIsFocused&&!p.IsDead()&&!(ui!=null&&(LocalPlayerUI.AnyModalWindowOpen()||ui.windowManager.IsCursorWindowOpen()||ui.windowManager.IsInputActive()));}
        static void Send(EntityPlayerLocal p,EntityVehicle v,byte op)
        {
            var ray=Presentation.SightRay(p);int serial=unchecked(++sequence);
            if(Server)Request(world,p.entityId,v.entityId,op,serial,ray.origin,ray.direction);
            else ConnectionManager.Instance.SendToServer(NetPackageManager.GetPackage<NetPackageM1Intent>().Setup(v.entityId,op,serial,ray.origin,ray.direction));
        }
        static void InputUpdate(World w)
        {
            var p=w.GetPrimaryPlayer();var v=p?.AttachedToEntity as EntityVehicle;
            if(p==null)return;
            RepairInput(w,p);
            if(!IsTank(v)||!Allowed(v,p.entityId)||!UIReady(p)){
                if(held&&w.GetEntity(inputVehicle) is EntityVehicle old&&IsTank(old))Send(p,old,Stop);held=false;inputVehicle=-1;return;
            }
            if(inputVehicle!=v.entityId){if(held&&w.GetEntity(inputVehicle) is EntityVehicle old)Send(p,old,Stop);held=false;inputVehicle=v.entityId;nextInput=0;}
            if(Input.GetKeyDown(KeyCode.R)){Send(p,v,SwitchAmmo);held=false;nextInput=Time.time+.1f;return;}
            var key=KeyCode.Mouse0;if(v.vehicle.Properties.Values.TryGetValue("m1FireKey",out var text))Enum.TryParse(text,true,out key);
            bool fire=Input.GetKey(key);
            if(fire!=held||Time.time>=nextInput){nextInput=Time.time+.1f;Send(p,v,fire?Fire:held?Stop:Aim);held=fire;}
        }
        static void RepairInput(World w,EntityPlayerLocal p)
        {
            EntityVehicle target=null;
            if(p.AttachedToEntity==null&&UIReady(p)&&Input.GetKey(KeyCode.G)){
                var ray=Presentation.SightRay(p);if(Voxel.Raycast(w,ray,8,-538750997,8,0))target=ItemActionAttack.FindHitEntity(Voxel.voxelRayHitInfo) as EntityVehicle;
                if(!IsTank(target))target=null;
            }
            if(repairVehicle>=0&&(target==null||target.entityId!=repairVehicle)){if(w.GetEntity(repairVehicle) is EntityVehicle old)Send(p,old,RepairStop);repairVehicle=-1;}
            if(target!=null&&(repairVehicle!=target.entityId||Time.time>=nextRepair)){repairVehicle=target.entityId;nextRepair=Time.time+.1f;Send(p,target,Repair);}
        }
    }
    public sealed class NetPackageM1Intent:NetPackage
    {
        public int Vehicle,Sequence;public byte Op;public Vector3 Origin,Direction;
        public override NetPackageDirection PackageDirection=>NetPackageDirection.ToServer;
        public NetPackageM1Intent Setup(int v,byte op,int seq,Vector3 o,Vector3 d){Vehicle=v;Op=op;Sequence=seq;Origin=o;Direction=d;return this;}
        public override int GetLength()=>33;
        public override void read(PooledBinaryReader r){Vehicle=r.ReadInt32();Op=r.ReadByte();Sequence=r.ReadInt32();Origin=Read(r);Direction=Read(r);}
        public override void write(PooledBinaryWriter w){base.write(w);w.Write(Vehicle);w.Write(Op);w.Write(Sequence);Write(w,Origin);Write(w,Direction);}
        public static Vector3 Read(PooledBinaryReader r)=>new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
        public static void Write(PooledBinaryWriter w,Vector3 v){w.Write(v.x);w.Write(v.y);w.Write(v.z);}
        public override void ProcessPackage(World world,GameManager callbacks)
        {if(world!=null&&Weapons.Server&&Sender!=null&&Sender.loginDone&&Sender.bAttachedToEntity)Weapons.Request(world,Sender.entityId,Vehicle,Op,Sequence,Origin,Direction);}
    }
    public sealed class NetPackageM1Event:NetPackage
    {
        public int Vehicle,Epoch,Sequence,Shot;public byte Kind;public float Time,X,Y;public Vector3 A,B;
        public override NetPackageDirection PackageDirection=>NetPackageDirection.ToClient;
        public static NetPackageM1Event Make(int v,int epoch,int seq,int shot,byte kind,float time,Vector3 a,Vector3 b,float x,float y)
        {var p=NetPackageManager.GetPackage<NetPackageM1Event>();p.Vehicle=v;p.Epoch=epoch;p.Sequence=seq;p.Shot=shot;p.Kind=kind;p.Time=time;p.A=a;p.B=b;p.X=x;p.Y=y;return p;}
        public override int GetLength()=>53;
        public override void read(PooledBinaryReader r){Vehicle=r.ReadInt32();Epoch=r.ReadInt32();Sequence=r.ReadInt32();Shot=r.ReadInt32();Kind=r.ReadByte();Time=r.ReadSingle();A=NetPackageM1Intent.Read(r);B=NetPackageM1Intent.Read(r);X=r.ReadSingle();Y=r.ReadSingle();}
        public override void write(PooledBinaryWriter w){base.write(w);w.Write(Vehicle);w.Write(Epoch);w.Write(Sequence);w.Write(Shot);w.Write(Kind);w.Write(Time);NetPackageM1Intent.Write(w,A);NetPackageM1Intent.Write(w,B);w.Write(X);w.Write(Y);}
        public override void ProcessPackage(World world,GameManager callbacks){if(world!=null&&!Weapons.Server)Presentation.Receive(world,this);}
    }
}
