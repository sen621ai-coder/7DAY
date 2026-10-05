// Minimal physics doubles: verifies patch routing/lifecycle, not Unity contact simulation.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using AECT16RuntimeFix;
using UnityEngine;

namespace UnityEngine
{
    public class Component
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
        public T GetComponentInParent<T>() where T : class => gameObject.GetComponent<T>();
        public T[] GetComponentsInChildren<T>(bool unused) where T : class => gameObject.Components.OfType<T>().ToArray();
    }
    public class GameObject
    {
        public bool activeInHierarchy = true;
        public readonly List<Component> Components = new List<Component>();
        public Transform transform;
        public GameObject() { transform = AddComponent<Transform>(); }
        public T AddComponent<T>() where T : Component, new() { var t = new T { gameObject = this }; Components.Add(t); return t; }
        public T GetComponent<T>() where T : class => Components.OfType<T>().FirstOrDefault();
    }
    public class Transform : Component { }
    public class MonoBehaviour : Component
    {
        private bool active = true;
        public bool enabled { get => active; set { bool old = active; active = value; if (old && !value) GetType().GetMethod("OnDisable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(this, null); } }
    }
    public class Collider : Component { public bool enabled = true, isTrigger; public Bounds bounds = new Bounds(); }
    public class Rigidbody : Component { }
    public class Collision { public GameObject gameObject; public Transform transform => gameObject.transform; public Rigidbody rigidbody; }
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float a,float b,float c) { x=a;y=b;z=c; }
        public static Vector3 operator +(Vector3 a,Vector3 b) => new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
    }
    public struct Quaternion
    {
        float yaw;
        public static Quaternion Euler(float x,float y,float z) => new Quaternion { yaw=y*Mathf.Rad2DegInverse };
        public static Vector3 operator *(Quaternion q,Vector3 v) => new Vector3((float)(v.x*Math.Cos(q.yaw)+v.z*Math.Sin(q.yaw)),v.y,(float)(v.z*Math.Cos(q.yaw)-v.x*Math.Sin(q.yaw)));
    }
    public class Bounds
    {
        public bool Overlap;
        public void Expand(float amount) { }
        public bool Intersects(Bounds other) => Overlap || other.Overlap;
    }
    public static class Mathf { public const float Rad2Deg=57.29578f,Rad2DegInverse=.0174532925f; public static float Atan2(float a,float b)=>(float)Math.Atan2(a,b); }
    public static class Time { public static float time; }
    public static class Physics
    {
        static HashSet<Tuple<Collider,Collider>> ignored=new HashSet<Tuple<Collider,Collider>>();
        public static bool GetIgnoreCollision(Collider a,Collider b)=>ignored.Contains(Tuple.Create(a,b));
        public static void IgnoreCollision(Collider a,Collider b,bool value){var key=Tuple.Create(a,b);if(value)ignored.Add(key);else ignored.Remove(key);}
    }
}
public enum EnumDamageTypes { Bashing, Falling, Heat, Suicide, Disease }
public class DamageSource { public EnumDamageTypes damageType;public int getEntityId()=>42; }
public class Entity : MonoBehaviour
{
    public Entity AttachedToEntity;
    public Vector3 rotation,position;
    public int entityId;
    public Vector3 GetPosition()=>position;
}
public class EntityAlive : Entity
{
    public int Health=100;
    public bool IsDead()=>Health<=0;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public virtual int damageEntityLocal(DamageSource source,int strength,bool critical,float impulse){Health-=strength;return strength;}
}
public class EntityPlayer : EntityAlive { }
public class Vehicle { public string Name; public string GetName()=>Name; }
public class AttachedToEntitySlotInfo { public List<AttachedToEntitySlotExit> exits=new List<AttachedToEntitySlotExit>(); }
public struct AttachedToEntitySlotExit { public Vector3 position,rotation; }
public class ColliderHitCallForward : Component { public Entity Entity; }
public class EntityVehicle : EntityAlive
{
    public Vehicle vehicle=new Vehicle(); public Entity Seat;public int Contacts;
    public int FindAttachSlot(Entity e)=>Seat!=null && e==Seat?0:-1;
    [MethodImpl(MethodImplOptions.NoInlining)] public void DetachEntity(Entity e){if(e==Seat)Seat=null;}
    [MethodImpl(MethodImplOptions.NoInlining)] public AttachedToEntitySlotInfo GetAttachedToInfo(int seat){var info=new AttachedToEntitySlotInfo();info.exits.Add(new AttachedToEntitySlotExit{position=new Vector3(0,3,0)});return info;}
    [MethodImpl(MethodImplOptions.NoInlining)] public void OnCollisionForward(Transform transform,Collision collision,bool enter){Contacts++;}
}
public static class Log { public static List<string> Lines=new List<string>(); public static void Out(string s){Lines.Add(s);} public static void Error(string s){throw new Exception(s);} }
public static class DismountTests
{
    static int checks;
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    static EntityVehicle Tank(string name="vehicleM1Abrams") { var v=new GameObject().AddComponent<EntityVehicle>();v.vehicle.Name=name;v.entityId=7;v.gameObject.AddComponent<Collider>();return v; }
    static EntityPlayer Player() {var p=new GameObject().AddComponent<EntityPlayer>();p.entityId=8;p.gameObject.AddComponent<Collider>();return p;}
    static VehicleDismountGuard Exit(EntityVehicle v,EntityPlayer p) {v.Seat=p;p.AttachedToEntity=null;v.DetachEntity(p);return p.GetComponent<VehicleDismountGuard>();}
    public static void Run()
    {
        VehicleDismountSafety.Install();
        foreach(var name in new[]{"vehicleMD500","vehicleApacheHelicopter","vehicleM1Abrams","vehicleM1AbramsT17","vehicleM1AbramsT18","vehicleM1AbramsT19"})
        {
            Time.time=0;var v=Tank(name.ToLowerInvariant());var p=Player();var g=Exit(v,p);
            var pc=p.GetComponent<Collider>();var vc=v.GetComponent<Collider>();
            Check(g!=null&&g.Protects(v)&&Physics.GetIgnoreCollision(pc,vc),name+" enters grace via patched detach");
            var exits=v.GetAttachedToInfo(0).exits;
            Check(exits.Count==8&&exits.All(e=>Math.Abs(e.position.x)>=3&&Math.Abs(e.position.y-.02f)<.001f),name+" side exits, no roof exit");
            var hit=new Collision{gameObject=p.gameObject};v.OnCollisionForward(null,hit,true);
            Check(v.Contacts==0,"queued own collision rejected");
            var other=Tank();other.OnCollisionForward(null,hit,true);Check(other.Contacts==1,"other vehicle still hits");
            v.OnCollisionForward(null,new Collision{gameObject=Player().gameObject},true);Check(v.Contacts==1,"bystander still collides");
            v.OnCollisionForward(null,new Collision{gameObject=new GameObject()},true);Check(v.Contacts==2,"world still collides");
            Time.time=3.99f;g.Tick();Check(g.Protects(v),"full four seconds");
            Time.time=4;g.Tick();Check(!g.Protects(v)&&!Physics.GetIgnoreCollision(pc,vc),"clear pair restored at deadline");
            v.OnCollisionForward(null,hit,true);Check(v.Contacts==3,"callback restored too");
            Time.time=10;g.Tick();Check(!g.enabled,"diagnostic expires");
        }
        foreach(var name in new[]{"vehicle4x4Truck","vehicleM1AbramsFake","vehicleMD500Extra",null})
        {
            var v=Tank(name);var p=Player();Check(Exit(v,p)==null,"non-whitelist untouched");Check(v.GetAttachedToInfo(0).exits[0].position.y==3,"unrelated exits unchanged");
        }
        {
            Time.time=0;var v=Tank();var p=Player();v.DetachEntity(p);Check(p.GetComponent<VehicleDismountGuard>()==null,"no stale-seat grace");
            p.Health=0;Check(Exit(v,p)==null,"dead player excluded");p.Health=100;
            var g=Exit(v,p);var pc=p.GetComponent<Collider>();var vc=v.GetComponent<Collider>();vc.bounds.Overlap=true;
            Time.time=4.1f;g.Tick();Check(g.Protects(v)&&Physics.GetIgnoreCollision(pc,vc),"overlap extends");
            Time.time=8;g.Tick();Check(!g.Protects(v)&&!Physics.GetIgnoreCollision(pc,vc),"overlap extension bounded");
        }
        {
            Time.time=0;var v=Tank();var p=Player();var pc=p.GetComponent<Collider>();var vc=v.GetComponent<Collider>();
            Physics.IgnoreCollision(pc,vc,true);var g=Exit(v,p);Time.time=5;g.Tick();Check(Physics.GetIgnoreCollision(pc,vc),"pre-existing ignore not restored by us");
        }
        {
            Time.time=0;var v=Tank();var p=Player();var g=Exit(v,p);var pc=p.GetComponent<Collider>();var vc=v.GetComponent<Collider>();
            Physics.IgnoreCollision(pc,vc,false);g.Tick();Check(Physics.GetIgnoreCollision(pc,vc),"controller re-enable reapplies owned pair");
            var added=v.gameObject.AddComponent<Collider>();Time.time=.3f;g.Tick();Check(Physics.GetIgnoreCollision(pc,added),"newly active collider covered");
            p.AttachedToEntity=v;Check(!g.Protects(v),"reentry ends callback grace immediately");g.Tick();Check(!Physics.GetIgnoreCollision(pc,vc)&&!g.enabled,"reentry cleanup");
            p.AttachedToEntity=null;Time.time=1;g=Exit(v,p);p.Health=0;g.Tick();Check(!Physics.GetIgnoreCollision(pc,vc),"death cleanup");
            p.Health=100;Time.time=2;g=Exit(v,p);g.enabled=false;Check(!Physics.GetIgnoreCollision(pc,vc),"disable cleanup");
            Time.time=3;g=Exit(v,p);var second=Tank();Time.time=3.5f;g=Exit(second,p);
            Check(!Physics.GetIgnoreCollision(pc,vc)&&g.Protects(second)&&!g.Protects(v),"rapid vehicle change cleans old pair");
        }
        {
            Time.time=0;var v=Tank();var p=Player();Exit(v,p);int logs=Log.Lines.Count;
            foreach(EnumDamageTypes type in Enum.GetValues(typeof(EnumDamageTypes))){p.Health=100;p.damageEntityLocal(new DamageSource{damageType=type},150,false,1);Check(p.Health==-50,"actual "+type+" remains lethal");}
            Check(Log.Lines.Count-logs==4,"damage diagnostics rate capped");
            v.position=new Vector3(10000,100,20000);v.rotation=new Vector3(80,90,45);
            var exits=v.GetAttachedToInfo(1).exits;
            Check(exits.All(e=>Math.Abs(e.position.y-100.02f)<.01f),"roll/pitch cannot elevate exits; absolute origin preserved");
            Check(exits.All(e=>Math.Abs(e.position.z-20000)>=3.29f),"yaw rotates exits");
        }
        Console.WriteLine("PASS: "+checks+" dismount patch and lifecycle assertions (physics doubles, not native simulation).");
    }
}
