using System.Collections.Generic;
using UnityEngine;

namespace PZAEC.M1
{
    // Seat occupants are network entities with renderers independent of the tank.
    // Suppress only their presentation on each client; leave animations, colliders
    // and the entities themselves intact for normal gameplay and dismounting.
    public static class CrewVisibility
    {
        static readonly Dictionary<Renderer, bool> original=new Dictionary<Renderer, bool>();
        static readonly HashSet<Renderer> current=new HashSet<Renderer>();
        static readonly List<Renderer> removed=new List<Renderer>();

        public static void Update(World world)
        {
            current.Clear();
            foreach(var entry in Weapons.States)
            {
                var tank=entry.Value.Vehicle;
                if(tank==null||world.GetEntity(entry.Key)!=tank)continue;
                for(int seat=0;seat<2;seat++)
                {
                    var occupant=tank.GetAttached(seat) as EntityPlayer;
                    if(occupant==null)continue;
                    foreach(var renderer in occupant.transform.GetComponentsInChildren<Renderer>(true))
                    {
                        if(renderer==null)continue;
                        current.Add(renderer);
                        if(!original.ContainsKey(renderer))original.Add(renderer,renderer.forceRenderingOff);
                        renderer.forceRenderingOff=true;
                    }
                }
            }
            removed.Clear();
            foreach(var pair in original)
                if(!current.Contains(pair.Key))
                {
                    if(pair.Key!=null)pair.Key.forceRenderingOff=pair.Value;
                    removed.Add(pair.Key);
                }
            foreach(var renderer in removed)original.Remove(renderer);
        }

        public static void Clear()
        {
            foreach(var pair in original)if(pair.Key!=null)pair.Key.forceRenderingOff=pair.Value;
            original.Clear();current.Clear();removed.Clear();
        }
    }
}
