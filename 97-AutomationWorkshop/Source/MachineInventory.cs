using System;
namespace YFAutomation
{
    // Missing in pre-0.7 saves: preserve the old external-box operation until switched.
    public sealed class TEFeatureMachineInventory : TEFeatureAbs
    {
        public bool Legacy = true;
        public override void OnAdded(Vector3i position, BlockValue value)
        { base.OnAdded(position,value); Legacy=false; }
        public override void Read(PooledBinaryReader reader,TileEntity.StreamModeRead mode)
        { base.Read(reader,mode);bool legacy=reader.ReadBoolean();if(mode!=TileEntity.StreamModeRead.FromClient)Legacy=legacy; }
        public override void Write(PooledBinaryWriter writer,TileEntity.StreamModeWrite mode)
        { base.Write(writer,mode);writer.Write(Legacy); }
        public override void CopyFromInternal(TileEntityComposite source)
        { var feature=source.GetFeature<TEFeatureMachineInventory>();if(feature!=null)Legacy=feature.Legacy; }
    }
    public static class MachineInventory
    {
        public const int InputSlots=18, TotalSlots=36;
        public static bool IsInput(int slot)=>slot>=0&&slot<InputSlots;
        public static bool IsOutput(int slot)=>slot>=InputSlots&&slot<TotalSlots;
        public static bool Has(TileEntityComposite t)=>t?.GetFeature<TEFeatureMachineInventory>()!=null&&t.GetFeature<TEFeatureStorage>()?.items.Length==TotalSlots;
        public static bool UsesInternal(TileEntityComposite t)
        {
            if(!Has(t))return false;
            var c=MachineConfiguration.Get(t);
            return c.StorageMode=="internal"||(c.StorageMode==""&&!t.GetFeature<TEFeatureMachineInventory>().Legacy);
        }
        public static string PassThrough(TileEntityComposite t,string filter)
        {
            var s=t.GetFeature<TEFeatureStorage>();var input=ProductionInventory.Clone(s.items);var output=ProductionInventory.Clone(s.items);
            int type=filter==""?0:ItemClass.GetItem(filter).type;
            if(filter!=""&&type==0)return "过滤物品无效";
            int count=ConveyorTransfer.Move(input,output,i=>!IsInput(i)||Logistics.Locked(s,i),i=>!IsOutput(i)||Logistics.Locked(s,i),16,int.MaxValue,v=>v.ItemClass.Stacknumber.Value,type);
            if(count==0)return "等待原料/成品区空位";
            // Publish both partitions once, never copy one whole snapshot over another.
            for(int i=0;i<TotalSlots;i++)s.items[i]=IsInput(i)?input[i]:output[i];
            t.SetChunkModified();t.SetModified();return "输送："+count;
        }
    }
}
