using UnityEngine;

namespace PZAEC.Surveillance
{
    // Use the wall normal, not the player's sideways viewing angle. The front is local +Z.
    public sealed class ScreenPlacement : BlockPlacementTowardsPlacerInverted
    {
        public override Result OnPlaceBlock(EnumPlacement placement,EnumRotationMode mode,int rotation,WorldBase world,BlockValue value,PropTransform prop,HitInfoDetails hit,Vector3 player)
        {
            var result=base.OnPlaceBlock(placement,mode,rotation,world,value,prop,hit,player);
            if(mode!=EnumRotationMode.Auto&&mode!=EnumRotationMode.ToFace)return result;
            Vector3 normal;
            switch(hit.blockFace)
            {
                case BlockFace.North:normal=Vector3.forward;break;
                case BlockFace.South:normal=Vector3.back;break;
                case BlockFace.East:normal=Vector3.right;break;
                case BlockFace.West:normal=Vector3.left;break;
                default:return result;
            }
            for(byte r=0;r<4;r++)
                if(Vector3.Dot(BlockShapeNew.GetRotationStatic(r)*Vector3.forward,normal)>.99f){result.blockValue.rotation=r;break;}
            return result;
        }
    }
}
