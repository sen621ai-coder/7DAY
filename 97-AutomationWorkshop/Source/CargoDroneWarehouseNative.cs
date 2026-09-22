using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace YFAutomation.CargoDrones
{
    public sealed class CargoWarehouseRow
    {
        public CargoPosition Position;
        public string Name="",Sign="",Block="";
        public double Distance;
        public bool Bound,Available=true;
        public CargoWarehouseKind Kind{get{return CargoWarehouseFilter.Kind(Block);}}
    }
    public sealed class CargoWarehousePage
    {
        public int Page,Total;
        public bool Limited;
        public CargoWarehouseRow[] Rows=new CargoWarehouseRow[0];
    }
    public static class CargoWarehouseSearch
    {
        // Discovery reads loaded chunks only: opening a picker never creates
        // observers, loads distant terrain or registers endpoint identities.
        public static CargoWarehousePage Find(World world,CargoPosition hub,string query,CargoWarehouseKind kind,int page)
        {
            if(world==null||world.IsRemote()||!Enum.IsDefined(typeof(CargoWarehouseKind),kind)||page<0||page>=32||query==null||query.Length>64)throw new ArgumentException("Invalid warehouse search");
            var found=new List<CargoWarehouseRow>();bool limited=false;
            var rules=new CargoRules();
            for(int x=(hub.X-1000)>>4;x<=((hub.X+1000)>>4);x++)
            for(int z=(hub.Z-1000)>>4;z<=((hub.Z+1000)>>4);z++)
            {
                var chunk=world.GetChunkFromWorldPos(x*16,z*16) as Chunk;
                if(chunk==null||chunk.IsLocked||chunk.NeedsDecoration)continue;
                foreach(var tile in chunk.GetTileEntities().dict.Values.OfType<TileEntityComposite>())
                {
                    if(tile.IsRemoving||tile.block.GetBlockName()==CargoRuntime.HubBlock||!(tile.GetFeature<TEFeatureStorage>()?.bPlayerStorage??false))continue;
                    var at=tile.ToWorldPos();var position=new CargoPosition(at.x,at.y,at.z);
                    if(position.Equals(hub)||!rules.CanDeliver(hub,position))continue;
                    string block=tile.block.GetBlockName(),name=tile.block.GetLocalizedBlockName();
                    string sign=tile.GetFeature<TEFeatureSignable>()?.GetAuthoredText().Text??"";
                    if(!CargoWarehouseFilter.Matches(query,kind,name,sign,block))continue;
                    if(found.Count>=CargoWarehouseFilter.MaxResults){limited=true;continue;}
                    double dx=(double)position.X-hub.X,dy=(double)position.Y-hub.Y,dz=(double)position.Z-hub.Z;
                    found.Add(new CargoWarehouseRow{Position=position,Block=block,Name=CargoWarehouseFilter.Clean(name),Sign=CargoWarehouseFilter.Clean(sign),Distance=Math.Sqrt(dx*dx+dy*dy+dz*dz)});
                }
            }
            int actual=Math.Min(page,Math.Max(0,(found.Count-1)/CargoWarehouseFilter.PageSize));
            return new CargoWarehousePage{Page=actual,Total=found.Count,Limited=limited,Rows=found.OrderBy(r=>r.Distance).ThenBy(r=>r.Position.X).ThenBy(r=>r.Position.Y).ThenBy(r=>r.Position.Z).Skip(actual*CargoWarehouseFilter.PageSize).Take(CargoWarehouseFilter.PageSize).ToArray()};
        }
    }
    public static class CargoSourceSearch
    {
        public static CargoWarehousePage Find(World world,CargoHubConfiguration hub,string query,CargoSourceKind kind,int page,bool boundOnly=false)
        {
            if(world==null||world.IsRemote()||hub==null||!Enum.IsDefined(typeof(CargoSourceKind),kind)||page<0||page>=32||query==null||query.Length>64)throw new ArgumentException("Invalid source search");
            var found=new List<CargoWarehouseRow>();var seen=new HashSet<CargoPosition>();bool limited=false;var rules=new CargoRules();var home=hub.Position;
            var bindings=hub.Sources;
            for(int x=(home.X-64)>>4;x<=((home.X+64)>>4);x++)for(int z=(home.Z-64)>>4;z<=((home.Z+64)>>4);z++)
            {
                var chunk=world.GetChunkFromWorldPos(x*16,z*16) as Chunk;if(chunk==null||chunk.IsLocked||chunk.NeedsDecoration)continue;
                foreach(var tile in chunk.GetTileEntities().dict.Values.OfType<TileEntityCollector>())
                {
                    if(tile.IsRemoving)continue;var p=tile.ToWorldPos();var at=new CargoPosition(p.x,p.y,p.z);string block=tile.block.GetBlockName(),name=tile.block.GetLocalizedBlockName();
                    if(!rules.CanCollect(home,at)||!CargoSourceFilter.Matches(query,kind,name,block))continue;
                    bool bound=bindings.Any(b=>b.Position.Equals(at));if(boundOnly&&!bound)continue;
                    seen.Add(at);if(found.Count>=256){limited=true;continue;}
                    double dx=(double)at.X-home.X,dy=(double)at.Y-home.Y,dz=(double)at.Z-home.Z;
                    found.Add(new CargoWarehouseRow{Position=at,Block=block,Name=CargoWarehouseFilter.Clean(name),Bound=bound,Distance=Math.Sqrt(dx*dx+dy*dy+dz*dz)});
                }
            }
            // Missing/unloaded/replaced sources must still be removable through
            // the picker. They are displayed as records, never offered for loading.
            foreach(var binding in bindings.Where(b=>!seen.Contains(b.Position)))
            {
                string name=CargoSourceFilter.Label(CargoSourceFilter.Kind(binding.BlockName));if(!CargoSourceFilter.Matches(query,kind,name,binding.BlockName))continue;
                double dx=(double)binding.Position.X-home.X,dy=(double)binding.Position.Y-home.Y,dz=(double)binding.Position.Z-home.Z;
                if(found.Count>=256){limited=true;continue;}
                found.Add(new CargoWarehouseRow{Position=binding.Position,Block=binding.BlockName,Name=name,Bound=true,Available=false,Distance=Math.Sqrt(dx*dx+dy*dy+dz*dz)});
            }
            int actual=Math.Min(page,Math.Max(0,(found.Count-1)/8));
            return new CargoWarehousePage{Page=actual,Total=found.Count,Limited=limited,Rows=found.OrderByDescending(r=>r.Bound).ThenBy(r=>r.Distance).ThenBy(r=>r.Position.X).ThenBy(r=>r.Position.Y).ThenBy(r=>r.Position.Z).Skip(actual*8).Take(8).ToArray()};
        }
    }
    public sealed class NetPackageYFCargoWarehouseRequest : NetPackage
    {
        static World lastWorld;
        static readonly Dictionary<int,float> next=new Dictionary<int,float>();
        public Vector3i At;
        public Guid Hub;
        public int Request,Page;
        public CargoWarehouseKind Kind;
        public bool Sources,BoundOnly;
        public CargoSourceKind SourceKind;
        public string Query="";
        public override NetPackageDirection PackageDirection=>NetPackageDirection.ToServer;
        public override int GetLength()=>44+Encoding.UTF8.GetByteCount(Query??"");
        public override void write(PooledBinaryWriter w){base.write(w);w.Write(At.x);w.Write(At.y);w.Write(At.z);w.Write(Hub.ToByteArray());w.Write(Request);w.Write(Page);w.Write((byte)Kind);w.Write(Sources);w.Write(BoundOnly);w.Write((byte)SourceKind);ConfigurationWire.Text(w,Query,256);}
        public override void read(PooledBinaryReader r){At=new Vector3i(r.ReadInt32(),r.ReadInt32(),r.ReadInt32());Hub=new Guid(r.ReadBytes(16));Request=r.ReadInt32();Page=r.ReadInt32();Kind=(CargoWarehouseKind)r.ReadByte();Sources=r.ReadBoolean();BoundOnly=r.ReadBoolean();SourceKind=(CargoSourceKind)r.ReadByte();Query=ConfigurationWire.Text(r,256);}
        public override void ProcessPackage(World world,GameManager callbacks){if(Sender!=null&&Sender.loginDone&&Sender.bAttachedToEntity)Handle(world,Sender.entityId);}
        public void Handle(World world,int actor)
        {
            if(world==null||world.IsRemote()||!(ConnectionManager.Instance?.IsServer??false))return;
            if(lastWorld!=world){lastWorld=world;next.Clear();}
            var player=world.GetEntity(actor) as EntityPlayer;
            var reply=NetPackageManager.GetPackage<NetPackageYFCargoWarehouseReply>();reply.At=At;reply.Hub=Hub;reply.Request=Request;reply.Sources=Sources;reply.Success=false;reply.Result=new CargoWarehousePage();reply.Message="设备搜索不可用";
            try
            {
                float until;if(next.TryGetValue(actor,out until)&&Time.realtimeSinceStartup<until)throw new InvalidOperationException("搜索过快，请稍后再试");next[actor]=Time.realtimeSinceStartup+.5f;
                var runtime=CargoNativeWorld.Current;
                var state=runtime?.Service.Status().SingleOrDefault(s=>s.Configuration.HubId==Hub&&s.Configuration.Position.Equals(new CargoPosition(At.x,At.y,At.z)));
                if(state==null||player==null||player.IsDead()||(player.position-new Vector3(At.x+.5f,At.y+.5f,At.z+.5f)).sqrMagnitude>64||player.PersistentPlayerData?.PrimaryId?.CombinedString!=state.Configuration.Owner||!runtime.HubExists(state.Configuration))throw new InvalidOperationException("需要停机坪所有者在 8 格内搜索");
                reply.Result=Sources?CargoSourceSearch.Find(world,state.Configuration,Query,SourceKind,Page,BoundOnly):CargoWarehouseSearch.Find(world,state.Configuration.Position,Query,Kind,Page);reply.Success=true;
                reply.Message=reply.Result.Limited?"结果超过 256 项，请缩小名称或类型范围":reply.Result.Total==0?(Sources?"未找到匹配设备；只发现 64 格内已加载的矿机和林场":"未找到匹配仓库；远处目标区域需先加载"):Sources?"点击绑定；已绑定项点击移除。最多 8 台。":"点击条目直接设为收货仓库";
            }
            catch(Exception error){reply.Message=CargoWarehouseFilter.Clean(error.Message,128);}
            if(player is EntityPlayerLocal)reply.Deliver();else if(player!=null)ConnectionManager.Instance.SendPackage(reply,false,actor);
        }
    }
    public sealed class NetPackageYFCargoWarehouseReply : NetPackage
    {
        public Vector3i At;public Guid Hub;public int Request;public bool Success,Sources;
        public string Message="";public CargoWarehousePage Result=new CargoWarehousePage();
        public override NetPackageDirection PackageDirection=>NetPackageDirection.ToClient;
        public override int GetLength()=>48+Encoding.UTF8.GetByteCount(Message??"")+Result.Rows.Sum(r=>28+Encoding.UTF8.GetByteCount(r.Name+r.Sign+r.Block));
        public override void write(PooledBinaryWriter w)
        {
            base.write(w);w.Write(At.x);w.Write(At.y);w.Write(At.z);w.Write(Hub.ToByteArray());w.Write(Request);w.Write(Success);w.Write(Sources);ConfigurationWire.Text(w,Message,512);
            w.Write(Result.Page);w.Write(Result.Total);w.Write(Result.Limited);w.Write((byte)Result.Rows.Length);
            foreach(var row in Result.Rows){w.Write(row.Position.X);w.Write(row.Position.Y);w.Write(row.Position.Z);w.Write(row.Distance);w.Write(row.Bound);w.Write(row.Available);ConfigurationWire.Text(w,row.Name,512);ConfigurationWire.Text(w,row.Sign,512);ConfigurationWire.Text(w,row.Block,256);}
        }
        public override void read(PooledBinaryReader r)
        {
            At=new Vector3i(r.ReadInt32(),r.ReadInt32(),r.ReadInt32());Hub=new Guid(r.ReadBytes(16));Request=r.ReadInt32();Success=r.ReadBoolean();Sources=r.ReadBoolean();Message=ConfigurationWire.Text(r,512);
            var page=new CargoWarehousePage{Page=r.ReadInt32(),Total=r.ReadInt32(),Limited=r.ReadBoolean()};int count=r.ReadByte();
            if(page.Page<0||page.Page>=32||page.Total<0||page.Total>256||count>8||count>page.Total)throw new System.IO.InvalidDataException("Invalid warehouse page");
            page.Rows=new CargoWarehouseRow[count];
            for(int i=0;i<count;i++)
            {var row=new CargoWarehouseRow{Position=new CargoPosition(r.ReadInt32(),r.ReadInt32(),r.ReadInt32()),Distance=r.ReadDouble(),Bound=r.ReadBoolean(),Available=r.ReadBoolean(),Name=ConfigurationWire.Text(r,512),Sign=ConfigurationWire.Text(r,512),Block=ConfigurationWire.Text(r,256)};if(double.IsNaN(row.Distance)||double.IsInfinity(row.Distance)||row.Distance<0||row.Distance>(Sources?64:1000))throw new System.IO.InvalidDataException("Invalid device distance");page.Rows[i]=row;}
            Result=page;
        }
        public override void ProcessPackage(World world,GameManager callbacks){if(world!=null&&!(ConnectionManager.Instance?.IsServer??true))Deliver();}
        public void Deliver(){XUiC_YFCargoHub.Active?.ReceiveWarehouses(this);}
    }
}
