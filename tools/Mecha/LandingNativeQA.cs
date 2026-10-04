using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using PZAEC.Mecha;

// Isolated native QA only. Call once for each already-created vehicle variant.
// This exercises native damage methods and the production packet/intent route;
// airborne time is aged explicitly rather than claiming a human physics trial.
public static class MechaLandingQA
{
    static readonly List<string> results=new List<string>();
    public static int Failures {get;private set;}
    public static int Passes {get;private set;}
    public static string[] Results(){return results.ToArray();}
    static string variant;
    static int testedVehicle=-1,explosions,landingEvents;
    static float lastStrength;
    static EntityPlayerLocal localFixture;
    static bool LocalDestroySpy(Entity __instance){return !ReferenceEquals(__instance,localFixture);}
    static bool BroadcastSpy(int vehicle,int id,byte kind,Vector3 a,Vector3 b,float value,float c)
    {
        if(vehicle!=testedVehicle||kind!=Weapons.LandingEvent)return true;
        landingEvents++;lastStrength=value;return false;
    }
    static bool ExplosionSpy(){explosions++;return false;}
    static void Check(string name,bool ok)
    {
        string line=(ok?"PASS ":"FAIL ")+variant+" landing: "+name;
        results.Add(line);if(ok)Passes++;else Failures++;Debug.Log("[MechaLandingQA] "+line);
    }
    static object[] SaveFields(object state,FieldInfo[] fields)
    {var values=new object[fields.Length];for(int i=0;i<fields.Length;i++)values[i]=fields[i].GetValue(state);return values;}
    static void RestoreFields(object state,FieldInfo[] fields,object[] values)
    {for(int i=0;i<fields.Length;i++)if(!fields[i].IsInitOnly)fields[i].SetValue(state,values[i]);}
    static EntityAlive Spawn(World world,string name,Vector3 position,List<Entity> entities)
    {
        var entity=EntityFactory.CreateEntity(EntityClass.FromString(name),position);
        if(entity==null)throw new InvalidOperationException("Cannot create "+name);
        world.SpawnEntityInWorld(entity);entities.Add(entity);
        var alive=entity as EntityAlive;if(alive==null)throw new InvalidOperationException(name+" is not alive");
        alive.Stats.Health.BaseMax=1000000;alive.Health=1000000;
        alive.lastAliveTime=Time.time-2;return alive;
    }
    static DamageSource Source(EnumDamageTypes type,int actor)
    {
        var source=new DamageSourceEntity(EnumDamageSource.External,type,actor,Vector3.down)
        {canHitSpecialBodyParts=false,DismemberChance=0};
        source.SetIgnoreConsecutiveDamages(false);return source;
    }
    static void TravelPaths(EntityVehicle v,EntityPlayer player,FieldInfo seats,int attacker,string name)
    {
        seats.SetValue(v,new Entity[]{player});player.AttachedToEntity=v;
        foreach(var type in new[]{EnumDamageTypes.Falling,EnumDamageTypes.VehicleInside})
        {
            var source=Source(type,attacker);int health=player.Health;
            int returned=player.DamageEntity(source,100,false,0);
            Check(name+" DamageEntity "+type+" returns zero and preserves HP",returned==0&&player.Health==health);
            var response=new DamageResponse{Source=source,Strength=100,ModStrength=100,ImpulseScale=0,Fatal=true};
            player.ProcessDamageResponse(response);
            Check(name+" ProcessDamageResponse "+type+" preserves HP",player.Health==health&&!player.IsDead());
            player.ProcessDamageResponseLocal(response);
            Check(name+" ProcessDamageResponseLocal "+type+" preserves HP",player.Health==health&&!player.IsDead());
        }
    }
    static void Motion(World world,EntityVehicle v,EntityPlayer pilot,int flags,ref int sequence)
    {Weapons.Request(world,pilot.entityId,v.entityId,Weapons.Motion,new Vector3(flags,0,0),Vector3.zero,++sequence);}
    static void Land(World world,EntityVehicle v,EntityPlayer pilot,float airborne,ref int sequence)
    {
        var move=Locomotion.Get(v);Motion(world,v,pilot,4,ref sequence);Motion(world,v,pilot,0,ref sequence);
        move.AirSince=Time.time-airborne;Motion(world,v,pilot,4,ref sequence);
    }
    static void Stomp(World world,EntityVehicle v,EntityPlayer pilot,ref int sequence)
    {Weapons.Request(world,pilot.entityId,v.entityId,Weapons.Stomp,Vector3.down*.8f,v.position,++sequence);}

    public static void Run(World world,EntityVehicle v)
    {
        variant=Rules.Complete(v)?"complete":"prototype";
        int startingFailures=Failures,startingPasses=Passes;
        if(world==null||v==null||!Weapons.Server){Check("requires live server fixture",false);return;}
        var h=new Harmony("mecha.landing.qa."+v.entityId);
        var entities=new List<Entity>();GameObject localObject=null;
        var seats=AccessTools.Field(typeof(Entity),"attachedEntities");var oldSeats=seats.GetValue(v);
        bool oldDriver=v.hasDriver,oldRemote=v.isEntityRemote,oldEngine=v.IsEngineRunning;
        var worldField=AccessTools.Field(typeof(Weapons),"currentWorld");var oldWorld=worldField.GetValue(null);
        var serialField=AccessTools.Field(typeof(Weapons),"serial");var oldSerial=serialField.GetValue(null);
        var moveMap=(IDictionary)AccessTools.Field(typeof(Locomotion),"moves").GetValue(null);
        var weaponMap=(IDictionary)AccessTools.Field(typeof(Weapons),"states").GetValue(null);
        bool hadMove=moveMap.Contains(v.entityId),hadWeapon=weaponMap.Contains(v.entityId);
        var move=Locomotion.Get(v);var weapon=Weapons.GetState(v);
        var moveFields=typeof(Locomotion.MoveState).GetFields(BindingFlags.Instance|BindingFlags.Public);
        var weaponFields=typeof(Weapons.State).GetFields(BindingFlags.Instance|BindingFlags.Public);
        var oldMove=SaveFields(move,moveFields);var oldWeapon=SaveFields(weapon,weaponFields);
        testedVehicle=v.entityId;explosions=landingEvents=0;lastStrength=0;
        try
        {
            Check("fixture is outside boarding ceremony",!Boarding.Active(v));
            if(Boarding.Active(v))return;
            worldField.SetValue(null,world);
            h.Patch(AccessTools.Method(typeof(Weapons),"Broadcast"),prefix:new HarmonyMethod(typeof(MechaLandingQA),nameof(BroadcastSpy)));
            // The entrypoint-only local component never acquired prefab assets;
            // native Entity.OnDestroy unconditionally releases that asset lease.
            h.Patch(AccessTools.Method(typeof(Entity),"OnDestroy"),prefix:new HarmonyMethod(typeof(MechaLandingQA),nameof(LocalDestroySpy)));
            h.Patch(AccessTools.Method(typeof(GameManager),"ExplosionServer",new[]{typeof(Vector3),typeof(Vector3i),typeof(Quaternion),typeof(ExplosionData),typeof(int),typeof(float),typeof(bool),typeof(ItemValue)}),prefix:new HarmonyMethod(typeof(MechaLandingQA),nameof(ExplosionSpy)));
            var pilot=(EntityPlayer)Spawn(world,"playerMale",v.position,entities);
            var bystander=(EntityPlayer)Spawn(world,"playerMale",v.position+Vector3.right*2,entities);
            var foe=Spawn(world,"zombieBoe",v.position+Vector3.forward*2,entities);
            var ally=Spawn(world,"zombieBoe",v.position-Vector3.forward*2,entities);ally.factionId=pilot.factionId;
            var animal=Spawn(world,"animalDoe",v.position+new Vector3(-2,0,-1),entities);
            var other=(EntityVehicle)Spawn(world,Rules.VehicleName,v.position-Vector3.right*2,entities);
            other.vehicle.SetItemValue(ItemClass.GetItem(Rules.PlaceableItem,false));other.Health=other.vehicle.GetMaxHealth();
            if(other.vehicleRB!=null){other.vehicleRB.isKinematic=true;other.vehicleRB.gameObject.SetActive(false);}
            seats.SetValue(v,new Entity[]{pilot});pilot.AttachedToEntity=v;v.hasDriver=true;v.IsEngineRunning=true;v.isEntityRemote=true;
            move.Grounded=true;move.FlightMode=Flight.Phase.Ground;move.HoverOn=move.Boost=false;move.Actor=pilot.entityId;move.Sequence=0;move.AirSince=-1;move.LandingPendingUntil=-100;move.LandingEventExpected=false;
            weapon.NextStomp=-100;int sequence=100000;
            Check("nearby native zombie is eligible",Weapons.LandingTarget(v,pilot.entityId,foe));
            Check("same-faction hostile-class ally is excluded",Weapons.Hostile(ally)&&!Weapons.LandingTarget(v,pilot.entityId,ally));
            Check("driver / player / animal / other mecha are excluded",!Weapons.LandingTarget(v,pilot.entityId,pilot)&&!Weapons.LandingTarget(v,pilot.entityId,bystander)&&!Weapons.LandingTarget(v,pilot.entityId,animal)&&!Weapons.LandingTarget(v,pilot.entityId,other));

            int pilotHP=pilot.Health,bystanderHP=bystander.Health,allyHP=ally.Health,animalHP=animal.Health,otherHP=other.Health,hullHP=v.Health,hullCondition=v.vehicle.GetHealth(),foeHP=foe.Health;
            Land(world,v,pilot,.6f,ref sequence);
            Check("actual Motion Receive air-to-ground grants one event",move.Grounded&&move.Sequence==sequence&&move.LandingPendingUntil>=Time.time&&move.LandingEventExpected);
            Stomp(world,v,pilot,ref sequence);
            int expected=(int)(Rules.StompDamage*Rules.AttributeScale(v));
            Check("authorized landing delivers native Bashing damage amount="+(foeHP-foe.Health),foeHP-foe.Health==expected);
            Check("authorized landing broadcasts exactly once and consumes permission",landingEvents==1&&move.LandingPendingUntil<Time.time&&lastStrength>=.7f);
            Check("landing preserves hull / driver / bystander / ally / animal / other mecha",v.Health==hullHP&&v.vehicle.GetHealth()==hullCondition&&pilot.Health==pilotHP&&bystander.Health==bystanderHP&&ally.Health==allyHP&&animal.Health==animalHP&&other.Health==otherHP);
            Check("surviving enemy receives outward/upward mechanical impulse",foe.motion.y>0&&Vector3.Dot(foe.motion,Vector3.forward)>0);
            int after=foe.Health;Stomp(world,v,pilot,ref sequence);
            Check("duplicate landing request is rejected",landingEvents==1&&foe.Health==after);
            float cooldown=weapon.NextStomp;Land(world,v,pilot,.6f,ref sequence);Stomp(world,v,pilot,ref sequence);
            Check("cooldown contact broadcasts one light cue without damage or cooldown extension",landingEvents==2&&Mathf.Abs(lastStrength-.25f)<.001f&&foe.Health==after&&weapon.NextStomp==cooldown);
            Stomp(world,v,pilot,ref sequence);Check("cooldown contact duplicate is rejected",landingEvents==2&&foe.Health==after);

            Land(world,v,pilot,.1f,ref sequence);Stomp(world,v,pilot,ref sequence);
            Check("short airborne interval has no damaging/event permission",landingEvents==2&&foe.Health==after&&!move.LandingEventExpected);
            Land(world,v,pilot,.6f,ref sequence);move.LandingPendingUntil=Time.time-.1f;Stomp(world,v,pilot,ref sequence);
            Check("expired landing permission is rejected",landingEvents==2&&foe.Health==after);
            move.LandingPendingUntil=-100;weapon.NextStomp=-100;Stomp(world,v,pilot,ref sequence);
            Check("grounded request without landing is rejected",landingEvents==2&&foe.Health==after);
            Land(world,v,pilot,.6f,ref sequence);int events=landingEvents;
            Weapons.Request(world,pilot.entityId,v.entityId,Weapons.Stomp,new Vector3(0,float.NaN,0),v.position,++sequence);
            Check("invalid landing scalar is rejected",landingEvents==events&&foe.Health==after);
            pilot.AttachedToEntity=null;seats.SetValue(v,new Entity[0]);Stomp(world,v,pilot,ref sequence);
            Check("request without seated operator is rejected",landingEvents==events&&foe.Health==after);
            seats.SetValue(v,new Entity[]{pilot});pilot.AttachedToEntity=v;

            if(Rules.Complete(v))
            {
                move.Grounded=true;move.FlightMode=Flight.Phase.Ground;
                Motion(world,v,pilot,8,ref sequence);move.AirSince=Time.time-.8f;
                Motion(world,v,pilot,24,ref sequence);Motion(world,v,pilot,4,ref sequence);Stomp(world,v,pilot,ref sequence);
                Check("controlled flight landing does not authorize enemy shock",landingEvents==events&&foe.Health==after&&!move.LandingEventExpected&&move.LandingPendingUntil<Time.time);
                var trial=new Locomotion.MoveState{Vehicle=v,Wheels=new WheelCollider[0],Grounded=false,FlightMode=Flight.Phase.Landing,ControlledLanding=true,LandingEventExpected=true,AirSince=Time.time-.8f};
                float fuel=v.vehicle.GetFuelLevel();v.vehicle.SetFuelLevel(100);
                try{Flight.Step(v,trial,true,false,.4f);Check("production Flight.Step controlled contact clears event expectation",!trial.LandingEventExpected&&trial.LandingAt>=Time.time-.01f&&trial.AirSince<0);}
                finally{v.vehicle.SetFuelLevel(fuel);}
            }

            TravelPaths(v,pilot,seats,foe.entityId,"EntityPlayer");
            // A native EntityPlayerLocal component exercises its actual override
            // and installed Harmony hooks without registering a client/UI player.
            // Shared initialized data is sufficient because protected entrypoints
            // must return before camera, model or local-player UI processing.
            localObject=new GameObject("QA Landing Local Driver");localObject.SetActive(false);
            var local=localObject.AddComponent<EntityPlayerLocal>();localFixture=local;local.world=world;local.entityClass=pilot.entityClass;local.entityId=pilot.entityId;
            foreach(var fieldName in new[]{"entityStats","emodel","inventory","bodyDamage"}){var field=AccessTools.Field(typeof(EntityAlive),fieldName)??AccessTools.Field(typeof(Entity),fieldName);field.SetValue(local,field.GetValue(pilot));}
            TravelPaths(v,local,seats,foe.entityId,"EntityPlayerLocal");local.AttachedToEntity=null;
            seats.SetValue(v,new Entity[]{pilot});pilot.AttachedToEntity=v;
            foreach(var type in new[]{EnumDamageTypes.Bashing,EnumDamageTypes.Heat})
            {
                int health=pilot.Health;pilot.DamageEntity(Source(type,foe.entityId),100,false,0);
                Check("seated external "+type+" combat still loses HP",pilot.Health<health&&!pilot.IsDead());
            }
            pilot.AttachedToEntity=null;seats.SetValue(v,new Entity[0]);
            foreach(var type in new[]{EnumDamageTypes.Falling,EnumDamageTypes.VehicleInside})
            {
                var source=Source(type,foe.entityId);int health=pilot.Health;
                pilot.DamageEntity(source,100,false,0);Check("outside DamageEntity "+type+" still loses HP",pilot.Health<health);
                health=pilot.Health;pilot.lastAliveTime=Time.time-2;
                var response=new DamageResponse{Source=source,Strength=100,ModStrength=100,ImpulseScale=0};
                pilot.ProcessDamageResponse(response);Check("outside ProcessDamageResponse "+type+" still loses HP",pilot.Health<health);
                health=pilot.Health;pilot.ProcessDamageResponseLocal(response);Check("outside ProcessDamageResponseLocal "+type+" still loses HP",pilot.Health<health);
            }
            Check("all landing and safety paths call ExplosionServer zero times",explosions==0);
        }
        catch(Exception ex){Check("unexpected native fixture exception: "+ex,false);}
        finally
        {
            testedVehicle=-1;
            foreach(var entity in entities)if(entity!=null)entity.AttachedToEntity=null;
            seats.SetValue(v,oldSeats);v.hasDriver=oldDriver;v.isEntityRemote=oldRemote;v.IsEngineRunning=oldEngine;
            RestoreFields(move,moveFields,oldMove);RestoreFields(weapon,weaponFields,oldWeapon);
            if(!hadMove)moveMap.Remove(v.entityId);if(!hadWeapon)weaponMap.Remove(v.entityId);
            worldField.SetValue(null,oldWorld);serialField.SetValue(null,oldSerial);
            if(localObject!=null){var local=localObject.GetComponent<EntityPlayerLocal>();if(local!=null){local.AttachedToEntity=null;local.world=null;foreach(var fieldName in new[]{"entityStats","emodel","inventory","bodyDamage"}){var field=AccessTools.Field(typeof(EntityAlive),fieldName)??AccessTools.Field(typeof(Entity),fieldName);field.SetValue(local,null);}}UnityEngine.Object.DestroyImmediate(localObject);}
            foreach(var entity in entities)if(entity!=null)world.RemoveEntity(entity.entityId,EnumRemoveEntityReason.Despawned);
            h.UnpatchSelf();localFixture=null;
            Debug.Log("[MechaLandingQA] COMPLETE "+variant+" passes="+(Passes-startingPasses)+" failures="+(Failures-startingFailures)+"; native packet/damage paths with aged airborne timers, no human input/physics claim");
        }
    }
}
