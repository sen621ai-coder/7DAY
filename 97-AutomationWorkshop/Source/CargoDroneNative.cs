using System;
using System.IO;
using System.Text;

namespace YFAutomation.CargoDrones
{
    // Metadata and snapshot helpers do not themselves acknowledge durable saves.
    // The fenced write adapter lives in CargoDroneNativeEndpoint.
    public sealed class CargoEndpointMarker
    {
        public readonly Guid WorldId,EndpointId,Incarnation,LastTransaction;
        public readonly string Owner,BlockName;
        public readonly long Revision;
        public CargoEndpointMarker(Guid world,Guid endpoint,Guid incarnation,string owner,string block,long revision,Guid lastTransaction)
        {
            if(world==Guid.Empty||endpoint==Guid.Empty||incarnation==Guid.Empty||string.IsNullOrEmpty(owner)||string.IsNullOrEmpty(block)||revision<0)
                throw new ArgumentException("Invalid endpoint marker");
            WorldId=world;EndpointId=endpoint;Incarnation=incarnation;Owner=owner;BlockName=block;Revision=revision;LastTransaction=lastTransaction;
        }
    }
    public static class CargoNativeItems
    {
        public static CargoItem Encode(ItemStack stack)
        {
            if(stack==null||stack.IsEmpty())return null;
            using(var memory=new MemoryStream())using(var writer=new BinaryWriter(memory))
            {
                stack.itemValue.Write(writer);writer.Flush();
                if(memory.Length>CargoRules.MaxItemBytes)throw new InvalidDataException("Complete ItemValue exceeds logistics limit");
                return new CargoItem(memory.ToArray(),stack.count,stack.itemValue.ItemClass.Stacknumber.Value);
            }
        }
        public static ItemStack Decode(CargoItem item)
        {
            if(item==null)return ItemStack.Empty;
            using(var memory=new MemoryStream(item.Value,false))using(var reader=new BinaryReader(memory))
            {
                var value=new ItemValue();value.Read(reader);
                if(memory.Position!=memory.Length||value.IsEmpty()||value.ItemClass==null)throw new InvalidDataException("Unknown/incomplete native ItemValue");
                return new ItemStack(value,item.Count);
            }
        }
    }
    public static class CargoNativeMarkers
    {
        const int Format=1;
        static string Key(TileEntity tile){var p=tile.localChunkPos;return "yf.cargo.endpoint/"+p.x+"/"+p.y+"/"+p.z;}
        static void Text(BinaryWriter writer,string value)
        {var bytes=Encoding.UTF8.GetBytes(value);if(bytes.Length<1||bytes.Length>512)throw new InvalidDataException("Marker text too long");writer.Write((ushort)bytes.Length);writer.Write(bytes);}
        static string Text(BinaryReader reader)
        {int n=reader.ReadUInt16();if(n<1||n>512)throw new InvalidDataException("Invalid marker text length");var b=reader.ReadBytes(n);if(b.Length!=n)throw new EndOfStreamException();return new UTF8Encoding(false,true).GetString(b);}
        static Guid Id(BinaryReader reader){var b=reader.ReadBytes(16);if(b.Length!=16)throw new EndOfStreamException();return new Guid(b);}
        public static byte[] Encode(CargoEndpointMarker marker)
        {
            using(var memory=new MemoryStream())using(var writer=new BinaryWriter(memory))
            {
                writer.Write(Format);writer.Write(marker.WorldId.ToByteArray());writer.Write(marker.EndpointId.ToByteArray());writer.Write(marker.Incarnation.ToByteArray());
                writer.Write(marker.LastTransaction.ToByteArray());writer.Write(marker.Revision);Text(writer,marker.Owner);Text(writer,marker.BlockName);writer.Flush();
                // ChunkCustomData stores data.Length as UInt16. Never silently truncate it.
                if(memory.Length>ushort.MaxValue)throw new InvalidDataException("ChunkCustomData payload overflow");return memory.ToArray();
            }
        }
        public static CargoEndpointMarker Decode(byte[] bytes)
        {
            if(bytes==null||bytes.Length>2048)throw new InvalidDataException("Invalid marker size");
            using(var memory=new MemoryStream(bytes,false))using(var reader=new BinaryReader(memory))
            {
                if(reader.ReadInt32()!=Format)throw new InvalidDataException("Unsupported endpoint marker version");
                Guid world=Id(reader),endpoint=Id(reader),incarnation=Id(reader),tx=Id(reader);long revision=reader.ReadInt64();string owner=Text(reader),block=Text(reader);
                if(memory.Position!=memory.Length)throw new InvalidDataException("Trailing endpoint marker data");
                return new CargoEndpointMarker(world,endpoint,incarnation,owner,block,revision,tx);
            }
        }
        public static CargoEndpointMarker Read(TileEntity tile,Guid world)
        {
            if(tile==null||tile.IsRemoving||tile.GetChunk()==null)return null;
            ChunkCustomData data;if(!tile.GetChunk().ChunkCustomData.dict.TryGetValue(Key(tile),out data))return null;
            var marker=Decode(data.data);
            if(marker.WorldId!=world||marker.BlockName!=tile.block.GetBlockName())throw new InvalidDataException("Endpoint world/type mismatch");
            return marker;
        }
        // Caller must hold the world's main-thread/serialization fence. Not auto-installed.
        public static void Remove(TileEntity tile)
        {
            if(tile==null||tile.GetChunk()==null)return;
            var data=tile.GetChunk().ChunkCustomData;
            if(data.dict.ContainsKey(Key(tile))){data.Remove(Key(tile));tile.SetChunkModified();}
        }
        public static void Write(TileEntity tile,CargoEndpointMarker marker)
        {
            if(tile==null||tile.IsRemoving||tile.GetChunk()==null||tile.block.GetBlockName()!=marker.BlockName)throw new InvalidOperationException("Invalid native endpoint");
            string key=Key(tile);var data=new ChunkCustomData(key,ulong.MaxValue,false);data.data=Encode(marker);
            tile.GetChunk().ChunkCustomData.Set(key,data);tile.SetChunkModified();
        }
        public static CargoInventory Snapshot(TileEntity tile,CargoEndpointMarker marker,bool includeCargoFence=true)
        {
            if(tile==null||marker==null||tile.IsRemoving||tile.block.GetBlockName()!=marker.BlockName)throw new InvalidOperationException("Endpoint unavailable");
            var collector=tile as TileEntityCollector;
            if(collector!=null)
            {
                if(!CargoRules.IsSource(marker.BlockName))throw new InvalidOperationException("Unsupported source");
                var native=collector.Items;var items=new CargoItem[native.Length];var allowed=new bool[native.Length];var locks=new bool[native.Length];
                for(int i=0;i<native.Length;i++)
                {
                    items[i]=CargoNativeItems.Encode(native[i]);var output=collector.GetSlotOutputType(i);
                    if(items[i]==null)continue;string name=native[i].itemValue.ItemClass.GetItemName();
                    allowed[i]=output!=null&&(name==output.OutputItem||name==output.OutputItemModded);
                }
                bool busy=(includeCargoFence&&CargoNativeValidationEndpoint.IsFenced(tile))||tile.bUserAccessing||LockManager.Instance==null||LockManager.Instance.IsLockedServer(tile,0);
                return new CargoInventory(marker.EndpointId,marker.Incarnation,marker.Revision,marker.Owner,items,locks,allowed,busy);
            }
            var composite=tile as TileEntityComposite;var storage=composite==null?null:composite.GetFeature<TEFeatureStorage>();
            if(storage==null||!storage.bPlayerStorage)throw new InvalidOperationException("Unsupported target");
            var values=new CargoItem[storage.items.Length];var slotLocks=new bool[values.Length];var writable=new bool[values.Length];
            for(int i=0;i<values.Length;i++){values[i]=CargoNativeItems.Encode(storage.items[i]);slotLocks[i]=Logistics.Locked(storage,i);writable[i]=true;}
            return new CargoInventory(marker.EndpointId,marker.Incarnation,marker.Revision,marker.Owner,values,slotLocks,writable,Logistics.Busy(composite,includeCargoFence));
        }
    }
}
