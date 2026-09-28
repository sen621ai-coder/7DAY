using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Renderer { public bool forceRenderingOff; }
    public class Transform
    {
        public readonly List<Renderer> Renderers=new List<Renderer>();
        public T[] GetComponentsInChildren<T>(bool includeInactive)
        {
            if(typeof(T)==typeof(Renderer))return (T[])(object)Renderers.ToArray();
            throw new Exception("Unexpected component query");
        }
    }
}
public class EntityPlayer { public readonly UnityEngine.Transform transform=new UnityEngine.Transform(); }
public class EntityVehicle
{
    public readonly EntityPlayer[] Seats=new EntityPlayer[2];
    public EntityPlayer GetAttached(int seat)=>Seats[seat];
}
public class World
{
    public readonly Dictionary<int,EntityVehicle> Entities=new Dictionary<int,EntityVehicle>();
    public EntityVehicle GetEntity(int id)=>Entities.TryGetValue(id,out var value)?value:null;
}
namespace PZAEC.M1
{
    public static class Weapons
    {
        public sealed class State { public EntityVehicle Vehicle; }
        public static readonly Dictionary<int,State> States=new Dictionary<int,State>();
    }
    public static class CrewVisibilityTests
    {
        static int count;
        static void Check(bool condition,string name){count++;if(!condition)throw new Exception(name);}
        public static void Run()
        {
            var world=new World();var vehicle=new EntityVehicle();world.Entities[7]=vehicle;
            Weapons.States[7]=new Weapons.State{Vehicle=vehicle};
            var driver=new EntityPlayer();var gunner=new EntityPlayer();
            var body=new UnityEngine.Renderer();var oldHidden=new UnityEngine.Renderer{forceRenderingOff=true};
            var gun=new UnityEngine.Renderer();var unrelated=new UnityEngine.Renderer();
            driver.transform.Renderers.Add(body);driver.transform.Renderers.Add(oldHidden);
            gunner.transform.Renderers.Add(gun);vehicle.Seats[0]=driver;vehicle.Seats[1]=gunner;
            CrewVisibility.Update(world);
            Check(body.forceRenderingOff&&gun.forceRenderingOff&&oldHidden.forceRenderingOff,"both seats hidden");
            Check(!unrelated.forceRenderingOff,"unrelated objects unchanged");
            var newEquipment=new UnityEngine.Renderer();driver.transform.Renderers.Add(newEquipment);
            CrewVisibility.Update(world);Check(newEquipment.forceRenderingOff,"new equipment hidden");
            vehicle.Seats[1]=null;CrewVisibility.Update(world);
            Check(!gun.forceRenderingOff&&body.forceRenderingOff,"dismount restores only gunner");
            vehicle.Seats[0]=null;CrewVisibility.Update(world);
            Check(!body.forceRenderingOff&&!newEquipment.forceRenderingOff&&oldHidden.forceRenderingOff,"driver original rendering state restored");
            vehicle.Seats[0]=driver;CrewVisibility.Update(world);Weapons.States.Clear();CrewVisibility.Update(world);
            Check(!body.forceRenderingOff,"unloaded tank restores seat occupant");
            Weapons.States[7]=new Weapons.State{Vehicle=vehicle};CrewVisibility.Update(world);CrewVisibility.Clear();
            Check(!body.forceRenderingOff&&oldHidden.forceRenderingOff,"world cleanup restores original state");
            Console.WriteLine("PASS "+count+" M1 crew visibility lifecycle checks");
        }
    }
}
