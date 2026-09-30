using System;
using PZAEC.Fishing.Contracts;
using UnityEngine;

namespace PZAEC.Fishing.Runtime
{
    public static class NativeCoordinates
    {
        public static Vec3 FromUnity(Vector3 v) => new Vec3(v.x,v.y,v.z);
        public static Vector3 ToUnity(Vec3 v) => new Vector3(v.X,v.Y,v.Z);
        public static Vector3 ToScene(Vec3 absolute) => ToUnity(absolute)-Origin.position;
        public static Vec3 ToAbsolute(Vector3 scene) => FromUnity(scene+Origin.position);
    }

    public sealed class NativeWaterQuery : IWaterQuery
    {
        readonly World world;
        public NativeWaterQuery(World world){this.world=world??throw new ArgumentNullException(nameof(world));}
        public WaterSample SampleColumn(Vec3 position,float searchAboveMeters,float searchBelowMeters)
        {
            var result=new WaterSample {Status=WaterSampleStatus.OutOfRange,Normal=Vec3.Up,Accuracy=SurfaceAccuracy.VoxelEstimate};
            if(!position.IsFinite||!Scalar.IsFinite(searchAboveMeters)||!Scalar.IsFinite(searchBelowMeters)||searchAboveMeters<0||searchBelowMeters<0||searchAboveMeters+searchBelowMeters>256)return result;
            int x=(int)Math.Floor(position.X),z=(int)Math.Floor(position.Z);
            var chunk=world.GetChunkFromWorldPos(new Vector3i(x,(int)position.Y,z)) as Chunk;
            if(chunk==null||chunk.InProgressUnloading){result.Status=WaterSampleStatus.Unloaded;return result;}
            int high=Math.Min(254,(int)Math.Ceiling(position.Y+searchAboveMeters));
            int low=Math.Max(0,(int)Math.Floor(position.Y-searchBelowMeters));
            int top=-1,bottom=-1;float surface=0;
            for(int y=high;y>=low;y--)
            {
                var value=world.GetWater(x,y,z);
                if(top<0)
                {
                    if(value.HasMass())
                    {
                        // Require an observed dry cell above, not a truncated underwater column.
                        if(y==high && world.GetWater(x,y+1,z).HasMass())return result;
                        top=y; surface=y+value.GetMassPercent();bottom=y;
                    }
                    else if(!world.GetBlock(x,y,z).isair)
                    {result.Status=WaterSampleStatus.Occluded;return result;}
                }
                else if(value.HasMass())bottom=y;
                else {result.BottomKnown=true;break;}
            }
            if(chunk.InProgressUnloading){result.Status=WaterSampleStatus.Unloaded;return result;}
            if(top<0){result.Status=WaterSampleStatus.Dry;return result;}
            result.Status=WaterSampleStatus.Valid;result.SurfacePoint=new Vec3(position.X,surface,position.Z);
            result.BottomY=bottom;result.DepthMeters=surface-bottom;
            return result;
        }
        // M0 conservative voxel obstruction probe. Not a mesh-accurate collision replacement.
        public SegmentHit TraceSolid(Vec3 from,Vec3 to)
        {
            if(!from.IsFinite||!to.IsFinite)return new SegmentHit{Obstructed=true};
            Vec3 delta=to-from;int steps=(int)Math.Ceiling(delta.Length/0.2f);
            if(steps>512)return new SegmentHit{Obstructed=true};
            steps=Math.Max(1,steps);
            for(int i=0;i<=steps;i++)
            {
                Vec3 p=Vec3.Lerp(from,to,i/(float)steps);var cell=new Vector3i((int)Math.Floor(p.X),(int)Math.Floor(p.Y),(int)Math.Floor(p.Z));
                var chunk=world.GetChunkFromWorldPos(cell) as Chunk;
                if(chunk==null||chunk.InProgressUnloading)return new SegmentHit{Unloaded=true,Point=p};
                if(!world.GetBlock(cell).isair&&!world.IsWater(cell))return new SegmentHit{Obstructed=true,Point=p};
            }
            return default(SegmentHit);
        }
    }

    // Per-session lease. Runtime hooks call BeginFrame at PlayerMoveController.Update prefix and
    // ApplyBeforeMove at EntityPlayerLocal.MoveByInput prefix (inside native Update).
    public sealed class NativeControlLease
    {
        public EntityPlayerLocal Player {get;private set;}
        public bool Active => Player!=null;
        public bool FreeLook {get;set;}
        public MovementRequest Request {get;set;} = MovementRequest.None;
        Vector3 rotation,cameraRotation;
        bool frameCaptured;
        bool axesApplied;
        float oldRight,oldForward,appliedRight,appliedForward;
        public void Acquire(EntityPlayerLocal player)
        {
            if(player==null||player.movementInput==null)throw new ArgumentException("Local player input unavailable");
            Release();Player=player;
        }
        public bool CanReadInput()
        {
            var p=Player;
            return p!=null&&!p.IsDead()&&!p.IsSwimming()&&p.AttachedToEntity==null&&p.moveController!=null
                &&p.moveController.bAllowPlayerInput&&!p.moveController.guiOpenThisUpdate
                &&(p.moveController.windowManager==null||(!p.moveController.windowManager.IsInputActive()&&!p.moveController.windowManager.IsModalWindowOpen()))
                &&Application.isFocused&&GameManager.Instance!=null&&!GameManager.Instance.IsMouseCursorVisible;
        }
        public void BeginFrame()
        {
            frameCaptured=false;
            if(!Active)return;
            rotation=Player.movementInput.rotation;cameraRotation=Player.movementInput.cameraRotation;frameCaptured=true;
        }
        public RawInputFrame Sample(long sequence,double time,float dt)
        {
            bool allowed=CanReadInput();
            var input=new RawInputFrame{Sequence=sequence,SampleTimeSeconds=time,DurationSeconds=dt,InputAllowed=allowed};
            if(!allowed)return input;
            input.MouseRightDelta=Input.GetAxisRaw("Mouse X"); input.MouseBackDelta=-Input.GetAxisRaw("Mouse Y");
            var actions=Player.playerInput;
            if(actions!=null){input.MoveRight=actions.Move.X;input.MoveForward=actions.Move.Y;}
            return input; // NativeBindings adds configured action buttons.
        }
        public void ApplyBeforeMove(EntityPlayerLocal player)
        {
            if(!Active||player!=Player||!frameCaptured)return;
            var move=player.movementInput;
            if(!FreeLook){move.rotation=rotation;move.cameraRotation=cameraRotation;}
            float yaw=move.rotation.y*Mathf.Deg2Rad;
            var forward=new Vec3(Mathf.Sin(yaw),0,Mathf.Cos(yaw));var right=new Vec3(forward.Z,0,-forward.X);
            oldRight=move.moveStrafe;oldForward=move.moveForward;
            MovementBridge.Apply(ref move.moveStrafe,ref move.moveForward,right,forward,Request);
            appliedRight=move.moveStrafe;appliedForward=move.moveForward;axesApplied=true;
            frameCaptured=false; // at most once per native Update, never per physics substep
        }
        public bool SuppressInventoryAction(Inventory inventory,int actionIndex)
            => Active && inventory!=null && inventory.entity==Player && (actionIndex==0||actionIndex==1);
        public void RestoreAfterMove()
        {
            if(axesApplied&&Player!=null&&Player.movementInput!=null)
            {
                var move=Player.movementInput;
                if(move.moveStrafe==appliedRight)move.moveStrafe=oldRight;
                if(move.moveForward==appliedForward)move.moveForward=oldForward;
            }
            axesApplied=false;
        }
        public void Release(){RestoreAfterMove();Player=null;Request=MovementRequest.None;FreeLook=false;frameCaptured=false;}
    }
}
