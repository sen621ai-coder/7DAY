using UnityEngine;

namespace PZAEC.Surveillance
{
    // Event-only diagnostics: distinguish actual world removal from loss of a model.
    // Never prevent destruction, force stability or recreate deleted player blocks.
    public static class ScreenLifecycle
    {
        public static void Placed(WorldBase world,Vector3i position,BlockValue value)
        {LogState("placed",world,position,value);}
        public static void Removed(WorldBase world,Vector3i position,BlockValue value)
        {LogState(value.ischild?"child-removing":"root-removing",world,position,value);}
        public static void Falling(WorldBase world,Vector3i position,BlockValue value)
        {LogState("falling",world,position,value);}
        static void LogState(string action,WorldBase world,Vector3i position,BlockValue value)
        {
            if(world==null)return;
            var block=value.Block;var parent=value.ischild?block.multiBlockPos.GetParentPos(position,value):position;
            var root=world.GetBlock(parent);int occupied=0;
            if(root.type==value.type&&!root.ischild)
            {
                var rotation=block.shape.GetRotation(root);
                foreach(var offset in block.multiBlockPos.pos)
                {
                    var delta=rotation*new Vector3(offset.x,offset.y,offset.z);
                    var cell=parent+new Vector3i(Mathf.RoundToInt(delta.x),Mathf.RoundToInt(delta.y),Mathf.RoundToInt(delta.z));
                    if(world.GetBlock(cell).type==value.type)occupied++;
                }
            }
            Log.Out("[Surveillance] Screen lifecycle "+action+" side="+(world.IsRemote()?"client":"server")+
                " cell="+position+" root="+parent+" occupied="+occupied+"/12 tile="+(world.GetTileEntity(parent)?.GetType().Name??"none"));
        }
    }
}
