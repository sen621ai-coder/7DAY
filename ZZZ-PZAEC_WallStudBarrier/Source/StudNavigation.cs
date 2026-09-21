using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using GamePath;
using Pathfinding;

namespace PZAEC.WallStudBarrier
{
    public static class RouteRules
    {
        public static bool Occupied(Vector3 feet, float radius, float height, Func<int,int,int,bool> stud)
        {
            int left=Mathf.FloorToInt(feet.x-radius), right=Mathf.FloorToInt(feet.x+radius);
            int back=Mathf.FloorToInt(feet.z-radius), front=Mathf.FloorToInt(feet.z+radius);
            int low=Mathf.FloorToInt(feet.y+.05f), high=Mathf.FloorToInt(feet.y+height-.05f);
            for(int x=left;x<=right;x++) for(int z=back;z<=front;z++) for(int y=low;y<=high;y++)
                if(stud(x,y,z)) return true;
            return false;
        }
    }
    // One wrapper per path. The worker reads through the game's chunk access cache,
    // never Unity physics or live entity properties, and never mutates shared graph nodes.
    public sealed class StudTraversal : ITraversalProvider
    {
        private readonly ITraversalProvider original;
        private readonly ChunkCache blocks;
        private readonly Vector3 origin;
        private readonly float radius,height;
        private readonly Dictionary<GraphNode,bool> allowed=new Dictionary<GraphNode,bool>();
        public StudTraversal(ITraversalProvider original, ChunkCache blocks, Vector3 origin, float radius, float height)
        {this.original=original;this.blocks=blocks;this.origin=origin;this.radius=radius;this.height=height;}
        private bool Allowed(Path path,GraphNode node)
        {
            // An entity already touching a stud must be able to leave its start
            // node. Only the actual native start node is exempt, not its neighbours.
            var ab=path as ABPath;
            if(ab!=null && ReferenceEquals(ab.startNode,node)) return true;
            bool value;
            if(allowed.TryGetValue(node,out value)) return value;
            Vector3 feet=(Vector3)node.position+origin;
            value=!RouteRules.Occupied(feet,radius,height,(x,y,z)=>Barrier.Stud(blocks.GetBlock(x,y,z)));
            allowed[node]=value;return value;
        }
        public bool CanTraverse(Path path,GraphNode node,int direction)
        {return original.CanTraverse(path,node,direction)&&Allowed(path,node);}
        public bool CanTraverseConnection(Path path,Connection connection)
        {return original.CanTraverseConnection(path,connection)&&Allowed(path,connection.node);}
        public uint GetTraversalCost(Path path,GraphNode node) {return original.GetTraversalCost(path,node);}
    }
    public static class Navigation
    {
        private static readonly AccessTools.FieldRef<GamePath.PathFinder,PathInfo> Info = AccessTools.FieldRefAccess<GamePath.PathFinder,PathInfo>("pathInfo");
        public static void Start(Path path,bool pushToFront,ASPPathFinder finder)
        {
            var info=Info(finder);var enemy=info.entity;
            if(Barrier.Enemy(enemy)&&enemy.world!=null&&path.traversalProvider!=null)
            {
                // This cache belongs to this search, not a mutable reused PathInfo.
                var blocks=new ChunkCache(1);
                int x=Mathf.FloorToInt(enemy.position.x),z=Mathf.FloorToInt(enemy.position.z);
                blocks.Init(enemy.world,x,0,z,x,255,z);
                path.traversalProvider=new StudTraversal(path.traversalProvider,blocks,Origin.position,
                    Mathf.Clamp(enemy.physicsColliderRadius,.1f,2f),Mathf.Clamp(enemy.physicsHeight,.2f,8f));
            }
            AstarPath.StartPath(path,pushToFront);
        }
        public static IEnumerable<CodeInstruction> BeforeQueue(IEnumerable<CodeInstruction> instructions)
        {
            var start=AccessTools.Method(typeof(AstarPath),"StartPath",new[]{typeof(Path),typeof(bool)});
            int replacements=0;
            foreach(var instruction in instructions)
            {
                if(instruction.Calls(start))
                {
                    var context=new CodeInstruction(OpCodes.Ldarg_0);
                    context.labels.AddRange(instruction.labels);context.blocks.AddRange(instruction.blocks);
                    yield return context;
                    yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(Navigation),nameof(Start)));
                    replacements++;
                }
                else yield return instruction;
            }
            if(replacements!=1)throw new InvalidOperationException("WallStudBarrier: expected exactly one native path queue call.");
        }
        public static void SmoothPostfix(EntityAlive ___entity,Vector3 __0,Vector3 __1,ref bool __result)
        {
            if(!__result||!Barrier.Enemy(___entity)||___entity.world==null)return;
            var world=___entity.world;
            Vector3 start=__0+Origin.position,end=__1+Origin.position;
            float radius=Mathf.Clamp(___entity.physicsColliderRadius,.1f,2f);
            float height=Mathf.Clamp(___entity.physicsHeight,.2f,8f);
            // Sample the swept body, including both sides of corners and tall studs.
            int steps=Math.Max(1,(int)Math.Ceiling(height/.5f));
            for(int i=0;i<=steps;i++)
            {
                float y=.05f+(height-.1f)*i/steps;
                foreach(var offset in new[]{new Vector3(0,y,0),new Vector3(radius,y,radius),new Vector3(-radius,y,radius),new Vector3(radius,y,-radius),new Vector3(-radius,y,-radius)})
                    if(Barrier.Crosses(start+offset,end+offset,(x,by,z)=>Barrier.Stud(world.GetBlock(x,by,z)))){__result=false;return;}
            }
        }
        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(ASPPathFinder),"Calculate"),transpiler:new HarmonyMethod(typeof(Navigation),nameof(BeforeQueue)));
            harmony.Patch(AccessTools.Method(typeof(ASPPathFinder),"IsLineClear"),postfix:new HarmonyMethod(typeof(Navigation),nameof(SmoothPostfix)));
        }
    }
}
