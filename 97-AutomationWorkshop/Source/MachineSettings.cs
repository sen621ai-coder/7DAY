using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using UnityEngine;

namespace YFAutomation
{
    public sealed class MachineSettings
    {
        public string Position="", Owner="", Kind="", Source="", Target="", Product="", StorageMode="";
        public bool Paused;
        public int Revision;
        public MachineSettings Clone() => (MachineSettings)MemberwiseClone();
    }
    public sealed class MachineSettingsFile
    {
        public int Schema=1;
        public List<MachineSettings> Machines=new List<MachineSettings>();
    }
    public static class MachineSettingsStorage
    {
        static readonly XmlSerializer serializer=new XmlSerializer(typeof(MachineSettingsFile));
        public static MachineSettingsFile Load(string path)
        {
            if(!File.Exists(path))return new MachineSettingsFile();
            MachineSettingsFile data;
            using(var stream=File.OpenRead(path))data=(MachineSettingsFile)serializer.Deserialize(stream);
            if(data.Schema!=1||data.Machines==null)throw new InvalidDataException("Unknown settings schema");
            var positions=new HashSet<string>();
            foreach(var s in data.Machines)
                if(s==null||s.Position==null||s.Owner==null||s.Source==null||s.Target==null||s.Product==null||!ValidStorageMode(s.StorageMode)||
                   !MachineConfiguration.Supported(s.Kind)||s.Revision<0||!positions.Add(s.Position))throw new InvalidDataException("Invalid machine settings");
            return data;
        }
        public static bool ValidStorageMode(string value)=>value==""||value=="internal"||value=="external";
        public static void Save(string path,IEnumerable<MachineSettings> machines)
        {
            var data=new MachineSettingsFile{Machines=machines.ToList()};
            using(var stream=new FileStream(path+".tmp",FileMode.Create,FileAccess.Write,FileShare.None))
            {serializer.Serialize(stream,data);stream.Flush(true);}
            if(File.Exists(path))File.Replace(path+".tmp",path,path+".bak");else File.Move(path+".tmp",path);
        }
    }
    // Separate, atomic world-local settings. Native feature order and progress bytes stay unchanged.
    public static class MachineConfiguration
    {
        static World current;
        static string path;
        static bool blocked;
        static readonly Dictionary<string,MachineSettings> settings=new Dictionary<string,MachineSettings>();
        static readonly Dictionary<TileEntityComposite,string> tokens=new Dictionary<TileEntityComposite,string>();
        public static string Key(Vector3i p)=>p.x+","+p.y+","+p.z;
        public static string Owner(TileEntityComposite t)=>(t?.GetFeature<TEFeatureLockable>()?.GetOwner()??t?.Owner)?.CombinedString??"";
        public static bool Supported(string k)=>Production.IsMachine(k)||k=="yfAutoSorter"||k=="yfAutoTransfer"||k=="yfAutoWaterPump"||k=="yfAutoAmmoFeed";
        public static bool HasBoxes(string k)=>Production.IsMachine(k)||k=="yfAutoSorter"||k=="yfAutoTransfer";
        public static bool HasProduct(string k)=>Production.IsMachine(k)||k=="yfAutoSorter";
        static bool Ready(World world)
        {
            if(world==null||world.IsRemote())return false;
            if(current==world)return !blocked;
            current=world;settings.Clear();tokens.Clear();blocked=false;
            path=Path.Combine(GameIO.GetSaveGameDir(),"automation-machine-settings.xml");
            try
            {
                foreach(var s in MachineSettingsStorage.Load(path).Machines)settings.Add(s.Position,s);
                return true;
            }
            catch(Exception ex){blocked=true;Log.Error("[YFAutomation] Settings unavailable; machines paused: "+ex.Message);return false;}
        }
        static void Save()
        {
            MachineSettingsStorage.Save(path,settings.Values);
        }
        public static MachineSettings Get(TileEntityComposite t)
        {
            if(!Ready(GameManager.Instance?.World))return new MachineSettings{Paused=true};
            MachineSettings s;
            if(settings.TryGetValue(Key(t.ToWorldPos()),out s)&&s.Owner==Owner(t)&&s.Kind==t.block.GetBlockName())return s;
            return new MachineSettings{Position=Key(t.ToWorldPos()),Owner=Owner(t),Kind=t.block.GetBlockName()};
        }
        public static bool Paused(TileEntityComposite t)=>Get(t).Paused;
        public static string Token(TileEntityComposite t)
        {string value;if(!tokens.TryGetValue(t,out value))tokens[t]=value=Guid.NewGuid().ToString("N");return value;}
        public static void Removed(TileEntityComposite __instance,World _world)
        {
            if(_world==null||_world.IsRemote()||!Supported(__instance.TeData.Block.GetBlockName())||!Ready(_world))return;
            tokens.Remove(__instance);
            if(!settings.Remove(Key(__instance.ToWorldPos())))return;
            try{Save();}catch(Exception ex){blocked=true;Log.Error("[YFAutomation] Settings removal failed: "+ex.Message);}
        }
        public static void Unloaded(TileEntityComposite __instance){tokens.Remove(__instance);}
        public static void Placed(WorldBase _world,Vector3i _blockPos,BlockValue _blockValue)
        {
            if(_blockValue.ischild||!Supported(_blockValue.Block.GetBlockName())||!Ready(_world as World))return;
            // A newly placed block must not inherit an orphaned sidecar entry after a crash/rollback.
            if(!settings.Remove(Key(_blockPos)))return;
            try{Save();}catch(Exception ex){blocked=true;Log.Error("[YFAutomation] New machine settings reset failed: "+ex.Message);}
        }
        public static bool CanAccess(World world,TileEntityComposite t,EntityPlayer player)
        {
            if(t==null||world.GetTileEntity(t.ToWorldPos())!=t||t.IsRemoving||player==null||player.IsDead()||!Supported(t.block.GetBlockName()))return false;
            var p=t.ToWorldPos();if((player.position-new Vector3(p.x+.5f,p.y+.5f,p.z+.5f)).sqrMagnitude>64)return false;
            var id=player.PersistentPlayerData?.PrimaryId;
            var l=t.GetFeature<TEFeatureLockable>();
            return id!=null&&l!=null&&(l.IsOwner(id)||l.GetUsers().Contains(id));
        }
        public static List<TileEntityComposite> Boxes(World w,TileEntityComposite machine,bool input)
        {
            var result=new List<TileEntityComposite>();var at=machine.ToWorldPos();string kind=machine.block.GetBlockName();
            if(!HasBoxes(kind))return result;
            bool adjacent=input||kind=="yfAutoTransfer";
            for(int y=-1;y<=1;y++)for(int x=-4;x<=4;x++)for(int z=-4;z<=4;z++)
            {
                if(adjacent?Math.Abs(x)+Math.Abs(y)+Math.Abs(z)!=1:x*x+z*z>16)continue;
                var p=Logistics.Add(at,new Vector3i(x,y,z));if(!TransferRules.SameChunk(at.x,at.z,p.x,p.z))continue;
                var t=w.GetTileEntity(p) as TileEntityComposite;if(t==null||t.IsRemoving||Owner(t)!=Owner(machine))continue;
                string k=t.block.GetBlockName();
                bool allowed=kind=="yfAutoTransfer"?k==(input?"yfAutoOutput":"yfAutoInput"):
                    input?k=="yfAutoInput"||Production.IsMachine(kind)&&k=="yfAutoOutput":k=="yfAutoOutput";
                if(allowed&&t.GetFeature<TEFeatureStorage>()!=null)result.Add(t);
            }
            return result.OrderBy(t=>Key(t.ToWorldPos()),StringComparer.Ordinal).ToList();
        }
        public static List<string> Products(string kind)
        {
            IEnumerable<string> names=Enumerable.Empty<string>();
            if(kind=="yfAutoKitchen"||kind=="yfAutoForge")
                names=CraftingManager.GetAllRecipes().Where(r=>!r.IsScrap&&r.craftingArea==(kind=="yfAutoKitchen"?"campfire":"forge")&&!r.GetOutputItemClass().HasQuality).Select(r=>r.GetOutputItemClass().GetItemName());
            else if(kind=="yfAutoSmelter")names=ItemClass.list.Where(i=>i!=null&&i.GetItemName().StartsWith("yfAutoIngot_")).Select(i=>i.GetItemName());
            else if(kind=="yfAutoMiner")names=new[]{"resourceScrapIron","resourceScrapLead","resourceCoal","resourcePotassiumNitratePowder","resourceOilShale"};
            else if(kind=="yfAutoFarm")names=Block.list.Where(b=>b!=null&&b.GetBlockName().EndsWith("3HarvestPlayer")&&b.itemsToDrop.ContainsKey(EnumDropEvent.Harvest)).SelectMany(b=>b.itemsToDrop[EnumDropEvent.Harvest]).Where(d=>d.tag=="cropHarvest"&&d.prob>=1&&d.minCount>0).Select(d=>d.name);
            else if(kind=="yfAutoRecycler")names=ItemClass.list.Where(i=>i!=null&&i.HasQuality).Select(i=>CraftingManager.GetScrapableRecipe(new ItemValue(i.Id),1)).Where(r=>r!=null).Select(r=>r.GetOutputItemClass().GetItemName());
            else if(kind=="yfAutoSorter")names=ItemClass.list.Where(i=>i!=null).Select(i=>i.GetItemName());
            return names.Where(n=>ItemClass.GetItem(n).type>0).Distinct().OrderBy(n=>Localization.Get(n),StringComparer.Ordinal).ToList();
        }
        public static string Apply(World w,TileEntityComposite t,EntityPlayer player,MachineSettings proposed,string token)
        {
            if(!Ready(w))return "配置文件不可用，设备已暂停；请查看日志";
            if(!CanAccess(w,t,player))return "无配置权限或距离过远";
            var old=Get(t);
            string conflict=ValidateRevision(old,proposed,Token(t),token);if(conflict!=null)return conflict;
            if(Logistics.Busy(t)&&!MachineInventoryUI.OwnsStorageLock(t,player.entityId))return "设备正在被使用，请稍后保存";
            string kind=t.block.GetBlockName();
            if(!MachineSettingsStorage.ValidStorageMode(proposed.StorageMode))return "库存模式无效";
            if((proposed.Source!=""&&!Boxes(w,t,true).Any(b=>Key(b.ToWorldPos())==proposed.Source))||
               (proposed.Target!=""&&!Boxes(w,t,false).Any(b=>Key(b.ToWorldPos())==proposed.Target)))return "所选箱子不存在、越界或不属于设备所有者";
            if(proposed.Source!=""&&proposed.Source==proposed.Target)return "输入和输出不能是同一个箱子";
            if(proposed.Product!=""&&!Products(kind).Contains(proposed.Product))return "该设备不支持所选产品/物品";
            var updated=proposed.Clone();updated.Position=old.Position;updated.Owner=old.Owner;updated.Kind=old.Kind;
            if(old.Revision==int.MaxValue)return "配置版本已达上限";
            updated.Revision=old.Revision+1;
            string key=Key(t.ToWorldPos());MachineSettings previous;bool existed=settings.TryGetValue(key,out previous);
            settings[key]=updated;
            try{Save();}
            catch(Exception ex){if(existed)settings[key]=previous;else settings.Remove(key);Log.Error("[YFAutomation] Settings save failed: "+ex.Message);return "保存失败，原配置未改变";}
            if(old.StorageMode!=updated.StorageMode||old.Source!=updated.Source||old.Target!=updated.Target||old.Product!=updated.Product)
            {var progress=t.GetFeature<TEFeatureAutomationState>();if(progress!=null){progress.Job="";progress.Seconds=0;t.SetChunkModified();t.SetModified();}}
            return "已保存";
        }
        public static string ValidateRevision(MachineSettings current,MachineSettings proposed,string currentToken,string token)
        {
            if(string.IsNullOrEmpty(currentToken)||token!=currentToken)return "设备已重新加载，请关闭后重开配置";
            if(current.Revision!=proposed.Revision)return "其他玩家已修改，请刷新后重试";
            return null;
        }
    }
}
