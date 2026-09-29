using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
namespace PZAEC.M1
{
    public static class Secondary
    {
        public sealed class State
        {
            public Weapons.State Main;public readonly SecondaryRules.Gun Gun=new SecondaryRules.Gun();
            public readonly SecondaryRules.InputLease[] Inputs={new SecondaryRules.InputLease(),new SecondaryRules.InputLease()};
            public readonly Vector3[] Origins=new Vector3[2],Views={Vector3.forward,Vector3.forward};
            public Transform MGYaw,MGPitch,Muzzle,AAPitch,Left,Right;public SecondaryModel.Geometry Geometry;
            public float Yaw,Pitch,AAP=20,LeftAt,RightAt,GlobalAt,Lock,LostAt=-1,NextStatus,LastTick;
            public int Target=-1,MGReason,AAReason,Preferred,Sequence,Shot;public bool FireEdge,GoodLock,MGStabilized;public Vector3 MGWorldDirection;
            public float NextDiagnostic;public int DiagnosticCode=-1;
        }
        sealed class Missile{public EntityVehicle Vehicle;public int Epoch,Id,Actor,Tier,Target;public Vector3 Position,Direction;public float Age,Distance,Lost,NextEvent;public bool Guided=true;}
        public static readonly Dictionary<int,State> States=new Dictionary<int,State>();
        static readonly List<Missile> missiles=new List<Missile>();
        static int inputVehicle=-1,inputSeat=-1,inputSerial;static float nextInput;static byte inputMode;static bool localHeld,localZoom;
        [ThreadStatic]static bool remoteSync;
        public static readonly string[] Keys={"m1swRounds","m1swHeat","m1swLoading","m1swReload","m1swOverheat","m1swLeft","m1swRight","m1swGlobal","m1swNextShot","m1swCoolingDelay"};
        static int Read(ItemValue item,string key,int maximum){return item!=null&&item.TryGetMetadata(key,out int n)?Math.Max(0,Math.Min(n,maximum)):0;}
        public static void Install(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(Vehicle),"GetUpdatedItemValue"),prefix:new HarmonyMethod(typeof(Secondary),nameof(SaveVehicle)));
            h.Patch(AccessTools.Method(typeof(EntityVehicle),"Write"),prefix:new HarmonyMethod(typeof(Secondary),nameof(SaveEntity)));
            h.Patch(AccessTools.Method(typeof(EntityVehicle),"ReadSyncData"),prefix:new HarmonyMethod(typeof(Secondary),nameof(BeginSync)),finalizer:new HarmonyMethod(typeof(Secondary),nameof(EndSync)));
            h.Patch(AccessTools.Method(typeof(Vehicle),"LoadItems"),prefix:new HarmonyMethod(typeof(Secondary),nameof(ProtectMetadata)));
            h.Patch(AccessTools.Method(typeof(EntityVehicle),"PhysicsFixedUpdate"),transpiler:new HarmonyMethod(typeof(Secondary),nameof(Steering)));
        }
        static IEnumerable<CodeInstruction> Steering(IEnumerable<CodeInstruction> codes){int found=0;foreach(var c in codes){yield return c;if(c.opcode==OpCodes.Ldsfld&&Equals(c.operand,AccessTools.Field(typeof(EntityVehicle),"isTurnTowardsLook"))){yield return new CodeInstruction(OpCodes.Ldarg_0);yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(Secondary),nameof(LookSteering)));found++;}}if(found!=1)throw new InvalidOperationException("M1 look steering hook changed");}
        static bool LookSteering(bool enabled,EntityVehicle vehicle)=>enabled&&!Weapons.IsTank(vehicle);
        // Disk deserialization must retain saved ammunition and cooldowns,
        // rather than replace them with the newly created entity's defaults.
        static void BeginSync(int __2,out bool __state){__state=remoteSync;remoteSync=!Modules.ReadingSave&&__2>=0;}
        static Exception EndSync(bool __state,Exception __exception){remoteSync=__state;return __exception;}
        static void ProtectMetadata(Vehicle __instance,ItemStack[] __0)
        {
            if(!remoteSync||!Weapons.Server||!Weapons.IsTank(__instance.entity)||__0==null||__0.Length==0||__0[0]?.itemValue==null)return;
            SaveVehicle(__instance);foreach(var k in Keys)__0[0].itemValue.SetMetadata(k,Read(__instance.itemValue,k,1000000));
        }
        static void SaveVehicle(Vehicle __instance){if(__instance?.entity==null)return;if(Weapons.Server){if(States.TryGetValue(__instance.entity.entityId,out var s))Save(s);}else SecondaryPresentation.SaveItem(__instance.entity);}
        static void SaveEntity(EntityVehicle __instance){if(Weapons.Server&&States.TryGetValue(__instance.entityId,out var s))Save(s);}
        public static void Save(State s)
        {
            var item=s.Main.Vehicle.vehicle.itemValue;if(item==null)return;float now=Time.time;var g=s.Gun;
            int[] values={g.Rounds,Mathf.CeilToInt(g.Heat*1000),g.Loading?1:0,SecondaryRules.Remaining(g.ReloadUntil,now),g.Overheated?1:0,SecondaryRules.Remaining(s.LeftAt,now),SecondaryRules.Remaining(s.RightAt,now),SecondaryRules.Remaining(s.GlobalAt,now),SecondaryRules.Remaining(g.NextShot,now),SecondaryRules.Remaining(g.LastShot+.5f,now)};
            for(int i=0;i<Keys.Length;i++)item.SetMetadata(Keys[i],values[i]);
        }
        public static bool Empty(ItemValue item)=>Read(item,Keys[0],100)==0&&Read(item,Keys[2],1)==0;
        public static State Get(Weapons.State main)
        {
            if(States.TryGetValue(main.Vehicle.entityId,out var s)&&s.Main==main)return s;
            s=new State{Main=main,LastTick=Time.time};var root=main.Model;
            s.MGYaw=SecondaryModel.Find(root,"RoofMGYaw");s.MGPitch=SecondaryModel.Find(root,"RoofMGPitch");s.Muzzle=SecondaryModel.Find(root,"RoofMGMuzzle");s.AAPitch=SecondaryModel.Find(root,"AAPitch");s.Left=SecondaryModel.Find(root,"AAMuzzleL");s.Right=SecondaryModel.Find(root,"AAMuzzleR");s.Geometry=new SecondaryModel.Geometry(root);
            var item=main.Vehicle.vehicle.itemValue;var g=s.Gun;float now=Time.time;
            g.Rounds=Read(item,Keys[0],100);g.Heat=Read(item,Keys[1],100000)/1000f;g.Loading=Read(item,Keys[2],1)!=0;g.ReloadUntil=now+Read(item,Keys[3],4000)/1000f;g.Overheated=Read(item,Keys[4],1)!=0;
            s.LeftAt=now+Read(item,Keys[5],12000)/1000f;s.RightAt=now+Read(item,Keys[6],12000)/1000f;s.GlobalAt=now+Read(item,Keys[7],1000)/1000f;g.NextShot=now+Read(item,Keys[8],100)/1000f;g.LastShot=now-.5f+Read(item,Keys[9],500)/1000f;
            s.Inputs[0].Mode=main.Vehicle.GetAttached(1)!=null?SecondaryRules.MG:SecondaryRules.Main;States[main.Vehicle.entityId]=s;return s;
        }
        public static bool MainControl(EntityVehicle v,int actor)=>States.TryGetValue(v.entityId,out var s)&&s.Inputs.Any(i=>i.Actor==actor&&i.Mode==0&&i.Active(Time.time));
        static bool Ready(State s,int seat)
        {
            var v=s.Main.Vehicle;var i=s.Inputs[seat];var p=GameManager.Instance.World.GetEntity(i.Actor) as EntityPlayer;
            return p!=null&&!p.IsDead()&&Weapons.Seat(v,i.Actor)==seat&&!v.IsDead()&&v.vehicle.GetHealth()>0&&SecondaryRules.Allowed(seat,v.GetAttached(1)!=null,i.Mode)&&i.Active(Time.time)&&(LockManager.Instance==null||!LockManager.Instance.IsLockedServer(v,0));
        }
        public static void Request(World w,int actor,NetPackageM1SecondaryIntent p)
        {
            if(!Weapons.Server||p.Version!=SecondaryRules.Protocol||p.Select>3||p.Flags>15)return;var v=w.GetEntity(p.Vehicle) as EntityVehicle;if(!Weapons.IsTank(v))return;
            int seat=Weapons.Seat(v,actor);var player=w.GetEntity(actor) as EntityPlayer;if(seat<0||player==null||player.IsDead())return;
            if(!Rules.Finite(p.Origin.x)||!Rules.Finite(p.Origin.y)||!Rules.Finite(p.Origin.z)||!Rules.Finite(p.Direction.x)||!Rules.Finite(p.Direction.y)||!Rules.Finite(p.Direction.z)||(p.Origin-v.position).sqrMagnitude>1600||(p.Origin-player.position).sqrMagnitude>1600||p.Direction.sqrMagnitude<.5f||p.Direction.sqrMagnitude>1.5f)return;
            var s=Get(Weapons.Register(v));var lease=s.Inputs[seat];byte mode=p.Select==3?lease.Mode:p.Select;
            if(!SecondaryRules.Allowed(seat,v.GetAttached(1)!=null,mode))mode=seat==0?SecondaryRules.MG:SecondaryRules.Main;
            bool held=(p.Flags&1)!=0,zoom=(p.Flags&2)!=0,edge=held&&!lease.Held;byte old=lease.Mode;
            if(!lease.Accept(actor,p.Serial,held,zoom,Time.time,mode))return;
            if((p.Flags&8)!=0){lease.Stop();s.Main.Trigger.Stop();s.FireEdge=false;ClearLock(s);return;}
            if(old!=mode){s.Main.Trigger.Stop();s.FireEdge=false;if(old==2)ClearLock(s);}
            s.Origins[seat]=p.Origin-v.position;s.Views[seat]=p.Direction.normalized;
            if(mode==0){byte op=(p.Flags&4)!=0?Weapons.SwitchAmmo:lease.Firing(Time.time)?Weapons.Fire:Weapons.Aim;Weapons.Request(w,actor,p.Vehicle,op,p.Serial,p.Origin,p.Direction.normalized);}
            else if(mode==2&&edge&&lease.Firing(Time.time))s.FireEdge=true;
        }
        static void ClearLock(State s){s.Target=-1;s.Lock=0;s.LostAt=-1;s.GoodLock=false;}
        public static void Update(Weapons.State main,float dt)
        {
            var s=Get(main);float now=Time.time;var v=main.Vehicle;
            for(int seat=0;seat<2;seat++){var i=s.Inputs[seat];if(!Ready(s,seat)){i.Stop();if(seat==0&&v.GetAttached(1)!=null)i.Mode=SecondaryRules.MG;}}
            s.Gun.Tick(now,Mathf.Max(0,now-s.LastTick));s.LastTick=now;
            // AA rotates the shared turret; resolve its parent pose before MG
            // stabilization/clearance, including simultaneous two-seat operation.
            if(s.MGYaw!=null){int aa=Ready(s,1)&&s.Inputs[1].Mode==2?1:Ready(s,0)&&s.Inputs[0].Mode==2?0:-1;UpdateAA(s,aa,dt);int mg=Ready(s,0)&&s.Inputs[0].Mode==1?0:-1;UpdateMG(s,mg,dt);}
            Diagnose(s);s.FireEdge=false;Save(s);
            if(now>=s.NextStatus){s.NextStatus=now+(v.hasDriver||v.GetAttached(1)!=null?.1f:1);Status(s);}
        }
        static int Count(State s,string name){var item=ItemClass.GetItem(name,false);return item==null||item.type==0||s.Main.Vehicle.bag==null?0:s.Main.Vehicle.bag.GetItemCount(item);}
        static bool Spend(State s,string name){var item=ItemClass.GetItem(name,false);var v=s.Main.Vehicle;if(item==null||item.type==0||v.bag==null||v.bag.GetItemCount(item)<1||v.bag.DecItem(item,1)!=1)return false;v.SendSyncData(EntityVehicle.cSyncStorage);return true;}
        static Vector3 Position(Transform t)=>t.position+Origin.position;
        static Vector3 Aim(State s,int seat,float range){var at=s.Main.Vehicle.position+s.Origins[seat];var d=s.Views[seat];return Weapons.Trace(s.Main.Vehicle,at,d,range,out var hit)?hit.hit.pos:at+d*range;}
        static bool Corridor(State s,Transform pivot,Transform muzzle,byte weapon,float length)
        {
            var a=Position(pivot);var b=Position(muzzle);var end=b+muzzle.forward*length;
            // Five rays approximate the barrel/receiver swept thickness as well as its center.
            foreach(var offset in new[]{Vector3.zero,muzzle.right*.10f,-muzzle.right*.10f,muzzle.up*.10f,-muzzle.up*.10f})if(!s.Geometry.Clear(a+offset,end+offset,weapon))return false;
            return !Weapons.Trace(s.Main.Vehicle,a,(end-a).normalized,(end-a).magnitude,out var _);
        }
        static void UpdateMG(State s,int seat,float dt)
        {
            s.MGReason=3;if(seat<0){s.MGStabilized=false;return;}var v=s.Main.Vehicle;var lease=s.Inputs[seat];var target=Aim(s,seat,200);var delta=target-Position(s.MGPitch);var local=s.MGYaw.parent.InverseTransformDirection(delta.normalized);float yaw=Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg,pitch=Mathf.Asin(Mathf.Clamp(local.y,-1,1))*Mathf.Rad2Deg;
            if(s.MGStabilized){var stable=s.MGYaw.parent.InverseTransformDirection(s.MGWorldDirection);s.Yaw=Mathf.Atan2(stable.x,stable.z)*Mathf.Rad2Deg;s.Pitch=Mathf.Clamp(Mathf.Asin(Mathf.Clamp(stable.y,-1,1))*Mathf.Rad2Deg,-10,80);}
            float factor=ModuleRules.Tracking(Modules.Get(v));float oldYaw=s.Yaw,oldPitch=s.Pitch;
            float nextYaw=Mathf.MoveTowardsAngle(s.Yaw,yaw,100*factor*dt),nextPitch=Mathf.MoveTowards(s.Pitch,Mathf.Clamp(pitch,-10,80),70*factor*dt);bool clear=true;
            int steps=Mathf.Max(1,Mathf.CeilToInt(Mathf.Max(Mathf.Abs(Mathf.DeltaAngle(oldYaw,nextYaw)),Mathf.Abs(nextPitch-oldPitch))/5));
            for(int j=1;j<=steps;j++){float t=(float)j/steps;s.MGYaw.localRotation=Quaternion.Euler(0,Mathf.LerpAngle(oldYaw,nextYaw,t),0);s.MGPitch.localRotation=Quaternion.Euler(-Mathf.Lerp(oldPitch,nextPitch,t),0,0);if(!Corridor(s,s.MGPitch,s.Muzzle,1,.15f)){clear=false;break;}}
            if(clear){s.Yaw=nextYaw;s.Pitch=nextPitch;}s.MGYaw.localRotation=Quaternion.Euler(0,s.Yaw,0);s.MGPitch.localRotation=Quaternion.Euler(-s.Pitch,0,0);
            s.MGWorldDirection=s.Muzzle.forward;s.MGStabilized=true;
            s.MGReason=pitch< -10||pitch>80?1:!clear?2:Vector3.Angle(s.Muzzle.forward,delta)>1.5f?4:0;
            var g=s.Gun;if(g.Overheated){s.MGReason=5;lease.Armed=false;}else if(g.Loading)s.MGReason=6;else if(g.Rounds==0)s.MGReason=7;
            if(!lease.Firing(Time.time))return;
            if(g.Rounds==0&&!g.Loading){if(Spend(s,SecondaryRules.Belt)){g.Loading=true;g.ReloadUntil=Time.time+4*ModuleRules.Reload(Modules.Get(v));Save(s);}return;}
            if(s.MGReason!=0||!g.CanShoot(Time.time))return;
            float spread=SecondaryRules.Spread(g.Heat,(Modules.Get(v)&ModuleRules.Stabilizer)!=0)*Mathf.Deg2Rad;float angle=UnityEngine.Random.value*Mathf.PI*2,radius=Mathf.Sqrt(UnityEngine.Random.value)*Mathf.Tan(spread);var d=(s.Muzzle.forward+radius*(Mathf.Cos(angle)*s.Muzzle.right+Mathf.Sin(angle)*s.Muzzle.up)).normalized;var origin=Position(s.Muzzle);
            // Recheck the dispersed shot, not only the unspread sight line.
            if(!s.Geometry.Clear(origin,origin+d*200,1)){s.MGReason=2;return;}
            float distance=200;var end=origin+d*200;if(Weapons.Trace(v,origin,d,200,out var hit)){end=hit.hit.pos;distance=Vector3.Distance(origin,end);Combat.SecondaryHit(ItemActionAttack.FindHitEntity(hit) as EntityAlive,lease.Actor,Mathf.RoundToInt(SecondaryRules.MGDamage[Weapons.Tier(v)]*SecondaryRules.Falloff(distance)),false,d,end);}
            g.Fired(Time.time);mainShot(s);if(g.Overheated)lease.Armed=false;Send(s,2,origin,end,s.Shot);Save(s);Audio.Manager.SignalAI(v,origin,"pzM1MGNoise",1);
        }
        static void mainShot(State s){s.Main.LastWeaponActivity=Time.time;s.Main.RepairTrigger.Stop();s.Main.RepairStarted=-1;s.Shot++;}
        static void Diagnose(State s)
        {
            foreach(var input in s.Inputs){if(!input.Active(Time.time)||!input.Held)continue;
                int reason=input.Mode==0?s.Main.Reason:input.Mode==1?s.MGReason:s.AAReason;
                int code=input.Mode*100+reason+(input.Armed?0:1000);
                if(Time.time<s.NextDiagnostic)continue;
                s.NextDiagnostic=Time.time+(code==s.DiagnosticCode?10:2);s.DiagnosticCode=code;
                Log.Out("[M1-Fire] vehicle="+s.Main.Vehicle.entityId+" actor="+input.Actor+" mode="+input.Mode+" reason="+reason+" armed="+input.Armed+" mainAmmo="+Weapons.Ammo(s.Main)+" belt="+s.Gun.Rounds+" missiles="+Count(s,SecondaryRules.Missile)+" lock="+s.Lock.ToString("0.00")+" reload="+Mathf.Max(0,s.Main.NextFire-Time.time).ToString("0.00"));
            }
        }
        static bool Target(EntityAlive e)=>Combat.Hostile(e)&&e is EntityVulture;
        static Vector3 Center(EntityAlive e)=>e.GetPosition()+Vector3.up*.8f;
        static bool Visible(State s,Vector3 origin,EntityAlive target){var delta=Center(target)-origin;return !Weapons.Trace(s.Main.Vehicle,origin,delta.normalized,delta.magnitude,out var hit)||ItemActionAttack.FindHitEntity(hit)==target;}
        static bool Candidate(State s,int seat,EntityAlive e)
        {
            if(!Target(e))return false;var origin=s.Main.Vehicle.position+s.Origins[seat];var delta=Center(e)-origin;float distance=Vector3.Distance(Center(e),s.Main.Vehicle.position);var local=Quaternion.Inverse(Weapons.Body(s.Main.Vehicle))*(Center(e)-Position(s.AAPitch)).normalized;float pitch=Mathf.Asin(Mathf.Clamp(local.y,-1,1))*Mathf.Rad2Deg;
            return distance>=40&&distance<=600&&SecondaryRules.AASearchPitch(pitch)&&Vector3.Angle(delta,s.Views[seat])<=6&&Visible(s,origin,e);
        }
        static void UpdateAA(State s,int seat,float dt)
        {
            s.AAReason=3;s.GoodLock=false;if(seat<0){ClearLock(s);return;}var input=s.Inputs[seat];var v=s.Main.Vehicle;var w=GameManager.Instance.World;
            var target=w.GetEntity(s.Target) as EntityAlive;
            if(!input.Zoom){ClearLock(s);s.AAReason=8;return;}
            if(target!=null&&(!Target(target)||Vector3.Distance(Center(target),v.position)<40||Vector3.Distance(Center(target),v.position)>600)){ClearLock(s);target=null;}
            if(s.Target>=0&&target==null)ClearLock(s);
            if(s.Target<0){float best=7,bestDistance=float.MaxValue;foreach(var entity in w.Entities.list){var candidate=entity as EntityAlive;if(!Candidate(s,seat,candidate))continue;var d=Center(candidate)-(v.position+s.Origins[seat]);float a=Vector3.Angle(d,s.Views[seat]);if(a<best||Mathf.Approximately(a,best)&&d.sqrMagnitude<bestDistance){best=a;bestDistance=d.sqrMagnitude;target=candidate;}}if(target!=null)s.Target=target.entityId;}
            Vector3 aim=target!=null?Center(target):v.position+s.Origins[seat]+s.Views[seat]*600;var delta=aim-Position(s.AAPitch);var local=Quaternion.Inverse(Weapons.Body(v))*delta.normalized;float yaw=Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg,pitch=Mathf.Asin(Mathf.Clamp(local.y,-1,1))*Mathf.Rad2Deg;float tracking=ModuleRules.Tracking(Modules.Get(v));
            s.Main.Trigger.Stop();s.Main.HasSight=false;s.Main.Yaw=Mathf.MoveTowardsAngle(s.Main.Yaw,yaw,Weapons.Spec(v).Yaw*tracking*(v.vehicle.GetHealthPercent()<.3f?.75f:1)*dt);s.Main.Pitch=Mathf.Max(s.Main.Pitch,Rules.MinPitch(s.Main.Yaw));
            s.Main.YawNode.localRotation=Quaternion.Euler(0,s.Main.Yaw,0);s.Main.PitchNode.localRotation=Quaternion.Euler(-s.Main.Pitch,0,0);s.AAP=Mathf.MoveTowards(s.AAP,Mathf.Clamp(pitch,SecondaryRules.AAMinPitch,SecondaryRules.AAMaxPitch),45*tracking*dt);s.AAPitch.localRotation=Quaternion.Euler(-s.AAP,0,0);
            if(target==null){s.AAReason=9;return;}
            bool visible=Candidate(s,seat,target);if(!visible){if(s.LostAt<0)s.LostAt=Time.time;if(Time.time-s.LostAt>.3f)ClearLock(s);s.AAReason=10;return;}
            s.LostAt=-1;s.Lock=Mathf.Min(1.5f,s.Lock+dt);s.GoodLock=true;s.AAReason=s.Lock<1.5f?11:0;
            int tube=SecondaryRules.Tube(s.LeftAt,s.RightAt,s.Preferred,Time.time);var muzzle=tube==1?s.Right:s.Left;
            if(Vector3.Angle(muzzle.forward,Center(target)-Position(muzzle))>25||Mathf.Abs(s.AAP-Mathf.Clamp(pitch,SecondaryRules.AAMinPitch,SecondaryRules.AAMaxPitch))>2)s.AAReason=4;
            else if(!Corridor(s,s.AAPitch,muzzle,2,2))s.AAReason=2;
            else if(tube<0||Time.time<s.GlobalAt)s.AAReason=6;
            else if(Count(s,SecondaryRules.Missile)==0)s.AAReason=7;
            if(!s.FireEdge||!input.Firing(Time.time)||s.Lock<1.5f||s.AAReason!=0||missiles.Count(m=>m.Vehicle==v)>=4)return;
            if(!Spend(s,SecondaryRules.Missile))return;
            float reload=12*ModuleRules.Reload(Modules.Get(v));if(tube==0)s.LeftAt=Time.time+reload;else s.RightAt=Time.time+reload;s.Preferred=1-tube;s.GlobalAt=Time.time+1;mainShot(s);
            missiles.Add(new Missile{Vehicle=v,Epoch=s.Main.Epoch,Id=s.Shot,Actor=input.Actor,Tier=Weapons.Tier(v),Target=target.entityId,Position=Position(muzzle),Direction=muzzle.forward});Send(s,3,Position(muzzle),muzzle.forward,s.Shot);Save(s);Audio.Manager.SignalAI(v,Position(muzzle),"pzM1AANoise",1);
        }
        public static void Advance(float delta)
        {
            var w=GameManager.Instance.World;
            for(int i=missiles.Count-1;i>=0;i--){var m=missiles[i];float left=Mathf.Max(0,delta);bool done=false;
                while(left>0&&!done){float dt=Mathf.Min(.01f,Mathf.Min(left,5-m.Age));if(dt<=0){done=true;break;}left-=dt;var target=w.GetEntity(m.Target) as EntityAlive;bool armed=m.Distance>=3;
                    if(m.Guided&&armed){if(!Target(target))m.Guided=false;else{var to=Center(target)-m.Position;if(!Weapons.Trace(m.Vehicle,m.Position,to.normalized,to.magnitude,out var sight)||ItemActionAttack.FindHitEntity(sight)==target)m.Lost=0;else m.Lost+=dt;if(m.Lost>=.5f)m.Guided=false;if(m.Guided)m.Direction=Vector3.RotateTowards(m.Direction,to.normalized,90*Mathf.Deg2Rad*dt,0).normalized;}}
                    float length=Mathf.Min(180*dt,700-m.Distance);var next=m.Position+m.Direction*length;
                    if(Weapons.Trace(m.Vehicle,m.Position,m.Direction,length,out var hit)){if(armed&&ItemActionAttack.FindHitEntity(hit)==target&&Target(target))Combat.SecondaryHit(target,m.Actor,SecondaryRules.AADamage[m.Tier],true,m.Direction,hit.hit.pos);m.Position=hit.hit.pos;done=true;}
                    else if(armed&&Target(target)){var center=Center(target);float along=Mathf.Clamp(Vector3.Dot(center-m.Position,m.Direction),0,length);var closest=m.Position+m.Direction*along;var to=center-closest;
                        if(to.sqrMagnitude<=4&&(!Weapons.Trace(m.Vehicle,closest,to.normalized,to.magnitude,out var obstruction)||ItemActionAttack.FindHitEntity(obstruction)==target)){Combat.SecondaryHit(target,m.Actor,SecondaryRules.AADamage[m.Tier],true,m.Direction,center);m.Position=closest;done=true;}}
                    if(!done)m.Position=next;m.Distance+=length;m.Age+=dt;if(m.Distance>=700||m.Age>=5)done=true;
                }
                if(States.TryGetValue(m.Vehicle.entityId,out var state)&&state.Main.Epoch==m.Epoch&&(done||Time.time>=m.NextEvent)){Send(state,done?(byte)5:(byte)4,m.Position,m.Direction,m.Id);m.NextEvent=Time.time+.05f;}
                if(done)missiles.RemoveAt(i);
            }
            foreach(var id in States.Keys.Where(id=>!Weapons.States.ContainsKey(id)).ToArray())States.Remove(id);
        }
        static void Status(State s)
        {
            var p=Packet(s,1);float now=Time.time;p.F[0]=s.Yaw;p.F[1]=s.Pitch;p.F[2]=s.AAP;p.F[3]=s.Gun.Heat;p.F[4]=s.Gun.Loading?Mathf.Max(0,s.Gun.ReloadUntil-now):0;p.F[5]=Mathf.Max(0,s.LeftAt-now);p.F[6]=Mathf.Max(0,s.RightAt-now);p.F[7]=Mathf.Max(0,s.GlobalAt-now);p.F[8]=s.Lock/1.5f;p.F[9]=s.GoodLock?1:0;
            p.F[10]=s.Gun.Loading?1:0;p.F[11]=s.Gun.Overheated?1:0;p.F[12]=Mathf.Max(0,s.Gun.NextShot-now);p.F[13]=Mathf.Max(0,s.Gun.LastShot+.5f-now);
            p.F[14]=s.Inputs[0].Armed?1:0;p.F[15]=s.Inputs[1].Armed?1:0;
            p.I[0]=s.Inputs[0].Mode;p.I[1]=s.Inputs[1].Mode;p.I[2]=s.Gun.Rounds;p.I[3]=s.Target;p.I[4]=s.MGReason;p.I[5]=s.AAReason;p.I[6]=Count(s,SecondaryRules.Belt);p.I[7]=Count(s,SecondaryRules.Missile);Broadcast(p);
        }
        static NetPackageM1SecondaryEvent Packet(State s,byte kind){var p=NetPackageManager.GetPackage<NetPackageM1SecondaryEvent>();p.Vehicle=s.Main.Vehicle.entityId;p.Epoch=s.Main.Epoch;p.Serial=++s.Sequence;p.Kind=kind;p.Time=Time.time;Array.Clear(p.F,0,p.F.Length);Array.Clear(p.I,0,p.I.Length);p.A=p.B=Vector3.zero;return p;}
        static void Send(State s,byte kind,Vector3 a,Vector3 b,int id){var p=Packet(s,kind);p.A=a;p.B=b;p.I[0]=id;Broadcast(p);}
        static void Broadcast(NetPackageM1SecondaryEvent p){ConnectionManager.Instance.SendPackage(p,false,-1,-1,p.Vehicle,null,700);SecondaryPresentation.Receive(GameManager.Instance.World,p);}
        public static void InputUpdate(World w,EntityPlayerLocal p)
        {
            var v=p.AttachedToEntity as EntityVehicle;
            if(!Weapons.IsTank(v)||!Weapons.UIReady(p)){if(inputVehicle>=0&&w.GetEntity(inputVehicle) is EntityVehicle old)SendInput(p,old,3,false,false,false,true);inputVehicle=-1;localHeld=localZoom=false;Optics.Clear();return;}
            int seat=Weapons.Seat(v,p.entityId);if(seat<0)return;
            if(inputVehicle!=v.entityId||inputSeat!=seat){inputVehicle=v.entityId;inputSeat=seat;inputMode=SecondaryPresentation.Mode(v,seat);nextInput=0;localHeld=localZoom=false;}
            byte selection=3;bool alt=Input.GetKey(KeyCode.LeftAlt)||Input.GetKey(KeyCode.RightAlt);
            if(alt){for(byte j=0;j<3;j++){var key=(KeyCode)((int)KeyCode.Alpha1+j);if(v.vehicle.Properties.Values.TryGetValue("m1SelectKey"+(j+1),out var text))Enum.TryParse(text,true,out key);if(Input.GetKeyDown(key)){if(SecondaryRules.Allowed(seat,v.GetAttached(1)!=null,j)){inputMode=j;selection=j;}else GameManager.ShowTooltip(p,seat==0?"主炮/导弹由炮手控制":"机枪由驾驶员控制");}}}
            if(!SecondaryRules.Allowed(seat,v.GetAttached(1)!=null,inputMode))inputMode=seat==0?(byte)1:(byte)0;
            Optics.UpdateInput(p,v,inputMode);
            var fireKey=KeyCode.Mouse0;if(v.vehicle.Properties.Values.TryGetValue("m1FireKey",out var fireText))Enum.TryParse(fireText,true,out fireKey);
            bool fire=Input.GetKey(fireKey),zoom=Input.GetKey(KeyCode.Mouse1),ammo=inputMode==0&&Input.GetKeyDown(KeyCode.R);
            if(selection!=3||ammo||fire!=localHeld||zoom!=localZoom||Time.time>=nextInput){
                if(fire&&!localHeld){var ray=Presentation.SightRay(p);Log.Out("[M1-Input] vehicle="+v.entityId+" seat="+seat+" mode="+inputMode+" zoom="+zoom+" originOffset="+(ray.origin-v.position)+" direction="+ray.direction);}
                SendInput(p,v,inputMode,fire,zoom,ammo);nextInput=Time.time+.05f;localHeld=fire;localZoom=zoom;
            }
        }
        static void SendInput(EntityPlayerLocal player,EntityVehicle v,byte selection,bool held,bool zoom,bool ammo,bool stop=false){var ray=Presentation.SightRay(player);var p=NetPackageManager.GetPackage<NetPackageM1SecondaryIntent>();p.Version=SecondaryRules.Protocol;p.Vehicle=v.entityId;p.Serial=unchecked(++inputSerial);p.Select=selection;p.Flags=(byte)((held?1:0)|(zoom?2:0)|(ammo?4:0)|(stop?8:0));p.Origin=ray.origin;p.Direction=ray.direction;if(Weapons.Server)Request(GameManager.Instance.World,player.entityId,p);else ConnectionManager.Instance.SendToServer(p);}
        public static void Clear(){foreach(var s in States.Values)if(Weapons.Server)Save(s);States.Clear();missiles.Clear();inputVehicle=-1;inputSeat=-1;SecondaryPresentation.Clear();}
    }
}
