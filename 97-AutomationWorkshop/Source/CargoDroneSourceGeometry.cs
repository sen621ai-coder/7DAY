using System;
using System.Collections.Generic;
using UnityEngine;

namespace YFAutomation.CargoDrones
{
    public static class CargoSourceGeometry
    {
        // Rotate the actual XML box, rather than enclosing every possible yaw.
        public static CargoBox Bounds(BlockValue value,Vector3i position)
        {
            var b=value.Block.oversizedBounds;var q=value.Block.shape.GetRotation(value);
            var min=new Vector3(float.PositiveInfinity,float.PositiveInfinity,float.PositiveInfinity);
            var max=new Vector3(float.NegativeInfinity,float.NegativeInfinity,float.NegativeInfinity);
            for(int i=0;i<8;i++)
            {
                var v=q*new Vector3((i&1)==0?b.min.x:b.max.x,(i&2)==0?b.min.y:b.max.y,(i&4)==0?b.min.z:b.max.z);
                min=Vector3.Min(min,v);max=Vector3.Max(max,v);
            }
            return new CargoBox(new CargoPoint(position.x+.5+min.x-.2,position.y+min.y-.2,position.z+.5+min.z-.2),
                new CargoPoint(position.x+.5+max.x+.2,position.y+max.y+.2,position.z+.5+max.z+.2));
        }

        static bool HandoffBlocked(CargoBox box,CargoPoint initial,CargoPoint candidate)
        {return box.SweptHit(candidate,candidate)||box.SweptHit(initial,candidate,.05,.05);}

        // Keep pickup directly above its machine, at most four extra blocks away.
        // This is a short cargo handoff, not permission to fly through the models.
        public static bool TryApproach(World world,Vector3i source,CargoPoint initial,out CargoPoint result,out CargoHold hold,Action<string> diagnostic=null)
        {
            result=initial;hold=CargoHold.None;var models=new List<CargoBox>();
            for(int cx=(source.x-10)>>4;cx<=((source.x+10)>>4);cx++)for(int cz=(source.z-10)>>4;cz<=((source.z+10)>>4);cz++)
            {
                var chunk=world.GetChunkFromWorldPos(cx*16,cz*16) as Chunk;
                if(chunk==null||chunk.IsLocked||chunk.NeedsDecoration){hold=CargoHold.ChunkLoading;return false;}
                foreach(var tile in chunk.GetTileEntities().dict.Values)
                    if(CargoRules.IsSource(tile.block.GetBlockName())&&tile.block.isOversized)
                        models.Add(Bounds(world.GetBlock(tile.ToWorldPos()),tile.ToWorldPos()));
            }
            var boxes=new List<Bounds>();
            for(int lift=0;lift<=4;lift+=2)
            {
                var candidate=new CargoPoint(initial.X,initial.Y+lift,initial.Z);
                if(candidate.Y>253)break;
                bool blocked=false;
                foreach(var model in models)if(model.SweptHit(candidate,candidate)){blocked=true;break;}
                if(blocked){diagnostic?.Invoke("candidate="+CargoTrace.Point(candidate)+" reason=model-clearance");continue;}
                // The body stays at the selected point; only the narrow handoff column extends down.
                // Keep both clear so raising pickup cannot transfer through a roof.
                for(int x=(int)Math.Floor(initial.X-.8);x<=(int)Math.Floor(initial.X+.8)&&!blocked;x++)
                for(int z=(int)Math.Floor(initial.Z-.8);z<=(int)Math.Floor(initial.Z+.8)&&!blocked;z++)
                for(int y=(int)Math.Floor(initial.Y-.6);y<=(int)Math.Floor(candidate.Y+.6)&&!blocked;y++)
                {
                    var value=world.GetBlock(new Vector3i(x,y,z));if(value.isair)continue;
                    if(value.ischild)
                    {blocked=HandoffBlocked(new CargoBox(new CargoPoint(x,y,z),new CargoPoint(x+1,y+1,z+1)),initial,candidate);if(blocked)diagnostic?.Invoke("candidate="+CargoTrace.Point(candidate)+" reason=child at="+x+","+y+","+z);continue;}
                    if(!value.Block.IsCollideMovement)continue;
                    boxes.Clear();value.Block.GetCollisionAABB(value,x,y,z,0,boxes);
                    foreach(var box in boxes)
                        if(HandoffBlocked(new CargoBox(new CargoPoint(box.min.x,box.min.y,box.min.z),new CargoPoint(box.max.x,box.max.y,box.max.z)),initial,candidate)){blocked=true;diagnostic?.Invoke("candidate="+CargoTrace.Point(candidate)+" reason="+value.Block.GetBlockName()+" at="+x+","+y+","+z);break;}
                }
                if(!blocked){result=candidate;return true;}
            }
            hold=CargoHold.PathBlocked;return false;
        }
    }
}
