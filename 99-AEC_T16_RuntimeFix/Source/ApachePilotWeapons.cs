using UnityEngine;

namespace AECT16RuntimeFix
{
    public static partial class ApacheWeapons
    {
        public const byte PilotAim=4, GuidedAim=5, PilotFire=6, GuidedFire=7;
        public const byte PilotStatusEvent=6, GuidedMoveEvent=7;
        private static bool localGuided;
        public static bool LocalGuided { get { return localGuided; } }
        public sealed partial class State
        {
            public bool PilotAiming,PilotGuided,GuidedSpent;
            public Vector3 PilotOffset,PilotView,PilotPoint,LockOffset;
            public byte PilotReason=3;
            public EntityAlive LockTarget;
            public float LockStarted,LockProgress,NextGuided;
        }
        private static void ResetPilot(State s)
        {
            s.PilotAiming=false;s.PilotReason=3;s.LockTarget=null;s.LockProgress=0;
            s.SalvoRemaining=0;
        }
        public static bool PilotArc(EntityVehicle v,Vector3 direction)
        {
            if(!ApacheWeaponRules.ValidDirection(direction.x,direction.y,direction.z))return false;
            var d=Quaternion.Inverse(BodyRotation(v))*direction.normalized;
            float yaw=Mathf.Atan2(d.x,d.z)*Mathf.Rad2Deg;
            float pitch=Mathf.Atan2(d.y,new Vector2(d.x,d.z).magnitude)*Mathf.Rad2Deg;
            return Mathf.Abs(yaw)<=60&&pitch>=-80&&pitch<=15;
        }
        // 0 ready, 1 arc/range, 2 blocked/too close, 3 not aiming,
        // 4 no live zombie target, 5 acquiring, 6 storage/operator, 7 stale input.
        public static byte ResolvePilot(State s,Vector3 origin,Vector3 view,out Vector3 point,out EntityAlive target)
        {
            point=s.Vehicle.position;target=null;
            if(currentWorld==null||!ValidSight(s.Vehicle,origin,view))return 1;
            view.Normalize();point=origin+view*ApacheWeaponRules.PilotRange;
            if(Trace(s.Vehicle,origin,view,ApacheWeaponRules.PilotRange,out var sight)){
                point=sight.hit.pos;
                var hit=ItemActionAttack.FindHitEntity(sight) as EntityAlive;
                if(hit is EntityZombie&&!hit.IsDead())target=hit;
            }
            for(int side=0;side<2;side++){
                var mount=side==0?s.Left:s.Right;
                var start=mount!=null?mount.position+Origin.position:s.Vehicle.position+BodyRotation(s.Vehicle)*new Vector3(side==0?-1.892f:1.87f,.924f,4.508f);
                var offset=point-start;float distance=offset.magnitude;
                if(distance<12)return 2;
                if(distance>ApacheWeaponRules.PilotRange+40||!PilotArc(s.Vehicle,offset.normalized))return 1;
                var inherited=s.Vehicle.vehicleRB!=null?Vector3.ClampMagnitude(s.Vehicle.vehicleRB.velocity,40):Vector3.zero;
                float along=Vector3.Dot(inherited,offset.normalized);
                float speed=along+Mathf.Sqrt(Mathf.Max(1,ApacheWeaponRules.RocketSpeed*ApacheWeaponRules.RocketSpeed-Mathf.Max(0,inherited.sqrMagnitude-along*along)));
                if(distance>speed*ApacheWeaponRules.RocketLifetime)return 1;
                // Includes the path from aircraft to launcher: camera visibility
                // must never permit firing from the far side of a nearby wall.
                var toMount=start-s.Vehicle.position;
                if(Trace(s.Vehicle,s.Vehicle.position,toMount.normalized,toMount.magnitude,out var near))return 2;
                if(Trace(s.Vehicle,start,offset.normalized,Mathf.Max(0,distance-.5f),out var obstacle)&&
                    (target==null||ItemActionAttack.FindHitEntity(obstacle)!=target)&&Vector3.Distance(obstacle.hit.pos,point)>1)return 2;
            }
            return 0;
        }
        private static void PilotRequest(State s,int actor,byte op,Vector3 origin,Vector3 direction,int sequence)
        {
            bool firing=op==PilotFire||op==GuidedFire;
            bool guided=op==GuidedAim||op==GuidedFire;
            var lease=s.Triggers[0];int oldActor=lease.Actor;
            if(!lease.Accept(actor,sequence,firing,Time.time))return;
            if(!ReadyOperator(s,actor,0)||!ValidSight(s.Vehicle,origin,direction)){ResetPilot(s);lease.Stop();return;}
            if(s.PilotGuided!=guided||oldActor!=actor){ResetPilot(s);s.PilotGuided=guided;s.GuidedSpent=false;}
            if(!firing)s.GuidedSpent=false;
            s.PilotAiming=true;s.PilotOffset=origin-s.Vehicle.position;s.PilotView=direction.normalized;
        }
        private static void UpdatePilot(State s,float now)
        {
            var lease=s.Triggers[0];int actor=s.Vehicle.GetAttached(0)?.entityId??-1;
            if(!s.PilotAiming||actor!=lease.Actor||now>=lease.Until||!ReadyOperator(s,actor,0)){
                ResetPilot(s);return;
            }
            s.PilotReason=ResolvePilot(s,s.Vehicle.position+s.PilotOffset,s.PilotView,out var point,out var target);
            s.PilotPoint=point;
            if(!s.PilotGuided){s.LockTarget=null;s.LockProgress=0;return;}
            if(s.PilotReason!=0||target==null){s.LockTarget=null;s.LockProgress=0;if(s.PilotReason==0)s.PilotReason=4;return;}
            if(s.LockTarget!=target){s.LockTarget=target;s.LockStarted=now;}
            // Keep the point the pilot selected on the target, including tall
            // Boss bodies; tracking the entity origin alone aims at its feet.
            s.LockOffset=point-target.position;
            s.LockProgress=Mathf.Clamp01((now-s.LockStarted)/ApacheWeaponRules.LockSeconds);
            if(s.LockProgress<1)s.PilotReason=5;
        }
        private static bool PilotCanFire(State s,float now)
        {
            return s.PilotAiming&&s.PilotReason==0&&now<s.Triggers[0].Until&&
                (!s.PilotGuided||(s.LockTarget!=null&&!s.LockTarget.IsDead()&&s.LockProgress>=1&&!s.GuidedSpent));
        }
        private static void FireGuided(State s,int actor,float now)
        {
            if(now<s.NextGuided||now<s.Gate.NextRocket||!Consume(s,ApacheWeaponRules.GuidedAmmo))return;
            s.NextGuided=now+ApacheWeaponRules.GuidedCooldown;s.Gate.NextRocket=now+ApacheWeaponRules.RocketCooldown;
            s.GuidedSpent=true;s.SalvoRemaining=0;Launch(s,actor);
        }
        private static ExplosionData GuidedExplosion()
        {
            var explosion=RocketExplosion();explosion.EntityDamage=ApacheWeaponRules.GuidedDamage;
            explosion.EntityRadius=5;explosion.BlockRadius=3;return explosion;
        }
        private static void GuideRocket(Rocket r,float dt)
        {
            if(!r.Guided||r.Target==null)return;
            if(r.Target.IsDead()||currentWorld.GetEntity(r.Target.entityId)!=r.Target){r.Target=null;return;}
            var toward=r.Target.position+r.TargetOffset-r.Position;
            if(toward.sqrMagnitude<.01f)return;
            // Collision sweeps still govern impact; guidance cannot pass through cover.
            r.Velocity=Vector3.RotateTowards(r.Velocity.normalized,toward.normalized,
                ApacheWeaponRules.GuidedTurnRate*Mathf.Deg2Rad*dt,0)*r.Velocity.magnitude;
        }
        private static void SendPilotStatus(State s,float now)
        {
            Broadcast(s.Vehicle.entityId,s.LockTarget!=null?s.LockTarget.entityId:-1,PilotStatusEvent,s.PilotPoint,
                new Vector3(Ammo(s,s.PilotGuided?ApacheWeaponRules.GuidedAmmo:ApacheWeaponRules.RocketAmmo),
                    Mathf.Max(0,(s.PilotGuided?s.NextGuided:s.Gate.NextRocket)-now),(s.GuidedSpent?8:s.PilotReason)+(s.PilotGuided?16:0)),s.LockProgress);
        }
        private static void PilotInput(EntityPlayerLocal player,EntityVehicle v)
        {
            if(Input.GetKeyDown(ApacheFlightAssist.Key(v,"pzApacheModeKey",KeyCode.R))){localGuided=!localGuided;SendIntent(player,v,Stop,SightRay(player));inputHeld=false;nextAim=0;}
            bool aiming=Input.GetKey(ApacheFlightAssist.Key(v,"pzApacheZoomKey",KeyCode.Mouse1));
            bool held=aiming&&Time.time>=nextInput&&Input.GetKey(FireKey(v,0));
            if(held!=inputHeld||Time.time>=nextAim){
                nextAim=Time.time+.1f;
                byte op=!aiming?Stop:localGuided?(held?GuidedFire:GuidedAim):(held?PilotFire:PilotAim);
                SendIntent(player,v,op,SightRay(player));inputHeld=held;
            }
        }
    }
}
