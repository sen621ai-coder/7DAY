using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using UnityEngine;

namespace PZAEC.Surveillance
{
    public enum SurveillanceDeviceKind : byte { Camera=1, Screen=2 }

    public sealed class SurveillanceDevice
    {
        public Guid Id;
        public SurveillanceDeviceKind Kind;
        public Vector3i Position;
        public string Owner="";
        public string Label="";
        public Guid[] Channels=new Guid[4];
        public int Selected;
        public bool Cycle;
        public bool Loaded;
        public bool Powered;
        public long Revision;
        public SurveillanceDevice Clone()
        {
            return new SurveillanceDevice{Id=Id,Kind=Kind,Position=Position,Owner=Owner,Label=Label,
                Channels=(Guid[])Channels.Clone(),Selected=Selected,Cycle=Cycle,Loaded=Loaded,Powered=Powered,Revision=Revision};
        }
    }

    public static class SurveillanceState
    {
        public const string CameraBlock="PZAEC_SurveillanceCamera";
        public const string ScreenBlock="PZAEC_SurveillanceScreen4x3";
        public const float WirelessRange=128f;
        static readonly Dictionary<Vector3i,SurveillanceDevice> devices=new Dictionary<Vector3i,SurveillanceDevice>();
        static readonly Dictionary<int,float> requestAt=new Dictionary<int,float>();
        static World world;
        static string path;
        static bool dirty;
        static bool loadFailed;
        static float saveAt;
        public static World World=>world;
        public static IEnumerable<SurveillanceDevice> Devices=>devices.Values;

        public static void Start(World value)
        {
            if(value==null||value.IsRemote()||world==value)return;
            var cameraBlock=Block.GetBlockByName(CameraBlock,true) as BlockPZAEC_SurveillanceCamera;
            var screenBlock=Block.GetBlockByName(ScreenBlock,true) as BlockPZAEC_SurveillanceScreen;
            if(cameraBlock==null||screenBlock==null||cameraBlock.requiredPower!=5||screenBlock.requiredPower!=30||!screenBlock.isMultiBlock)
                throw new InvalidOperationException("Surveillance block registry contract failed");
            if(!(cameraBlock.CreateTileEntity(null) is TileEntityPoweredTrigger)||!(screenBlock.CreateTileEntity(null) is TileEntityPoweredBlock))
                throw new InvalidOperationException("Surveillance powered tile contract failed");
            var offsets=screenBlock.multiBlockPos.pos.ToArray();
            if(offsets.Length!=12||offsets.Min(p=>p.x)!=-2||offsets.Max(p=>p.x)!=1||offsets.Min(p=>p.y)!=0||offsets.Max(p=>p.y)!=2||offsets.Any(p=>p.z!=0))
                throw new InvalidOperationException("Surveillance 4x3 footprint changed: "+string.Join(";",offsets.Select(p=>p.ToString()).ToArray()));
            Log.Out("[Surveillance] 4x3 footprint verified: "+string.Join(";",offsets.Select(p=>p.ToString()).ToArray()));
            Stop();world=value;path=Path.Combine(GameIO.GetSaveGameDir(),"pzaec-surveillance.xml");
            Load();ScanLoaded();Broadcast();
            Log.Out("[Surveillance] Wireless device registry attached; block/tile audit passed; "+devices.Count+" saved devices.");
        }
        public static void Stop()
        {
            if(world!=null&&dirty)Save();
            devices.Clear();requestAt.Clear();world=null;path=null;dirty=false;loadFailed=false;saveAt=0;
        }
        static string Key(Vector3i p)=>p.x+","+p.y+","+p.z;
        static Guid ParseGuid(string value){Guid result;return Guid.TryParse(value,out result)?result:Guid.Empty;}
        static int Int(XElement e,string name){int v;return int.TryParse((string)e.Attribute(name),out v)?v:0;}
        static bool Bool(XElement e,string name){bool v;return bool.TryParse((string)e.Attribute(name),out v)&&v;}
        static void Load()
        {
            devices.Clear();loadFailed=false;if(string.IsNullOrEmpty(path)||!File.Exists(path))return;
            try
            {
                var root=XDocument.Load(path).Root;if(root==null||Int(root,"version")!=1)throw new InvalidDataException("Unsupported surveillance save format");
                foreach(var e in root.Elements("device"))
                {
                    var d=new SurveillanceDevice{Id=ParseGuid((string)e.Attribute("id")),Kind=(SurveillanceDeviceKind)Int(e,"kind"),
                        Position=new Vector3i(Int(e,"x"),Int(e,"y"),Int(e,"z")),Owner=(string)e.Attribute("owner")??"",
                        Label=(string)e.Attribute("label")??"",Selected=Mathf.Clamp(Int(e,"selected"),0,3),Cycle=Bool(e,"cycle"),Revision=Math.Max(0,(long?)e.Attribute("revision")??0)};
                    if(d.Id==Guid.Empty||!Enum.IsDefined(typeof(SurveillanceDeviceKind),d.Kind)||Encoding.UTF8.GetByteCount(d.Label)>96)continue;
                    for(int i=0;i<4;i++)d.Channels[i]=ParseGuid((string)e.Attribute("c"+i));
                    devices[d.Position]=d;
                }
            }
            catch(Exception e){loadFailed=true;Log.Error("[Surveillance] Save retained but could not be read; writes are disabled for this session: "+e);devices.Clear();}
        }
        static void Save()
        {
            if(string.IsNullOrEmpty(path)||loadFailed)return;
            try
            {
                var root=new XElement("surveillance",new XAttribute("version",1));
                foreach(var d in devices.Values.OrderBy(v=>v.Position.x).ThenBy(v=>v.Position.z).ThenBy(v=>v.Position.y))
                {
                    var e=new XElement("device",new XAttribute("id",d.Id),new XAttribute("kind",(int)d.Kind),
                        new XAttribute("x",d.Position.x),new XAttribute("y",d.Position.y),new XAttribute("z",d.Position.z),
                        new XAttribute("owner",d.Owner??""),new XAttribute("label",d.Label??""),
                        new XAttribute("selected",d.Selected),new XAttribute("cycle",d.Cycle),new XAttribute("revision",d.Revision));
                    for(int i=0;i<4;i++)e.Add(new XAttribute("c"+i,d.Channels[i]));root.Add(e);
                }
                var document=new XDocument(root);string temporary=path+".new";document.Save(temporary);
                if(File.Exists(path))File.Replace(temporary,path,path+".bak");else File.Move(temporary,path);
                dirty=false;
            }
            catch(Exception e){Log.Error("[Surveillance] Save failed; will retry: "+e.Message);dirty=true;saveAt=Time.realtimeSinceStartup+2;}
        }
        static void Changed(){if(loadFailed)return;dirty=true;saveAt=Time.realtimeSinceStartup+.5f;}
        static SurveillanceDeviceKind Kind(string block)
        {return block==CameraBlock?SurveillanceDeviceKind.Camera:block==ScreenBlock?SurveillanceDeviceKind.Screen:0;}
        public static SurveillanceDevice Register(Vector3i position,string block,string owner)
        {
            if(world==null||world.IsRemote())return null;var kind=Kind(block);if(kind==0)return null;
            SurveillanceDevice d;
            if(devices.TryGetValue(position,out d)&&d.Kind==kind)return d;
            d=new SurveillanceDevice{Id=Guid.NewGuid(),Kind=kind,Position=position,Owner=owner??"",
                Label=kind==SurveillanceDeviceKind.Camera?"摄像头 "+Key(position):"监控屏 "+Key(position)};
            devices[position]=d;Changed();return d;
        }
        public static void Remove(Vector3i position,string block)
        {
            if(world==null||world.IsRemote())return;SurveillanceDevice d;
            if(!devices.TryGetValue(position,out d)||d.Kind!=Kind(block))return;
            devices.Remove(position);Changed();Broadcast();
        }
        public static SurveillanceDevice At(Vector3i position)
        {SurveillanceDevice d;return devices.TryGetValue(position,out d)?d:null;}
        public static SurveillanceDevice ById(Guid id)=>devices.Values.FirstOrDefault(d=>d.Id==id);
        static bool MayManage(SurveillanceDevice screen,EntityPlayer player,out string message)
        {
            message="监控设备不可用";
            if(player==null||player.IsDead()||(player.position-new Vector3(screen.Position.x+.5f,screen.Position.y+1.5f,screen.Position.z+.5f)).sqrMagnitude>64){message="需要在监控屏8格内操作";return false;}
            string actor=player.PersistentPlayerData?.PrimaryId?.CombinedString??"";
            if(!string.IsNullOrEmpty(screen.Owner)&&!string.IsNullOrEmpty(actor)&&screen.Owner!=actor&&!player.isAdmin){message="只有放置者或管理员可以修改频道";return false;}
            return true;
        }
        public static bool AllowRequest(int entityId)
        {
            float now=Time.realtimeSinceStartup,last;
            if(requestAt.TryGetValue(entityId,out last)&&now-last<.15f)return false;
            requestAt[entityId]=now;return true;
        }
        public static bool Configure(Vector3i screen,int channel,Guid camera,long expectedRevision,EntityPlayer player,out string message)
        {
            message="监控设备不可用";SurveillanceDevice s,c;
            if(world==null||world.IsRemote()||channel<0||channel>3||!devices.TryGetValue(screen,out s)||s.Kind!=SurveillanceDeviceKind.Screen)return false;
            if(!MayManage(s,player,out message))return false;
            if(expectedRevision!=s.Revision){message="配置已更新，请刷新后重试";Broadcast();return false;}
            if(camera==Guid.Empty){s.Channels[channel]=Guid.Empty;s.Selected=channel;s.Revision++;Changed();message="频道已清除";Broadcast();return true;}
            c=ById(camera);
            if(c==null||c.Kind!=SurveillanceDeviceKind.Camera){message="摄像头已经失效";return false;}
            Vector3 delta=new Vector3(c.Position.x-screen.x,c.Position.y-screen.y,c.Position.z-screen.z);
            if(delta.sqrMagnitude>WirelessRange*WirelessRange){message="摄像头超出128格无线范围";return false;}
            s.Channels[channel]=camera;s.Selected=channel;s.Revision++;Changed();message="频道 "+(channel+1)+" 已绑定 "+c.Label;Broadcast();return true;
        }
        public static bool SetMode(Vector3i screen,int selected,bool cycle,long expectedRevision,EntityPlayer player,out string message)
        {
            message="监控设备不可用";SurveillanceDevice s;
            if(world==null||world.IsRemote()||selected<0||selected>3||!devices.TryGetValue(screen,out s)||s.Kind!=SurveillanceDeviceKind.Screen)return false;
            if(!MayManage(s,player,out message))return false;
            if(expectedRevision!=s.Revision){message="配置已更新，请刷新后重试";Broadcast();return false;}
            s.Selected=selected;s.Cycle=cycle;s.Revision++;Changed();message=cycle?"已开启5秒轮巡":"已选择频道 "+(selected+1);Broadcast();return true;
        }
        public static void ScanLoaded()
        {
            if(world==null||world.IsRemote()||world.ChunkCache==null)return;
            foreach(long key in world.ChunkCache.GetChunkKeysCopySync())
            {
                var chunk=world.ChunkCache.GetChunkSync(key) as Chunk;if(chunk==null||chunk.IsLocked||chunk.NeedsDecoration)continue;
                foreach(var tile in chunk.GetTileEntities().dict.Values.ToArray())
                {
                    if(tile==null||tile.IsRemoving)continue;string block=tile.blockValue.Block.GetBlockName();if(Kind(block)==0)continue;
                    string owner=(tile as TileEntityPoweredTrigger)?.GetOwner()?.CombinedString??"";
                    Register(tile.ToWorldPos(),block,owner);
                }
            }
            foreach(var device in devices.Values)
            {
                var tile=world.GetTileEntity(device.Position) as TileEntityPowered;
                device.Loaded=tile!=null;device.Powered=tile!=null&&tile.IsPowered;
            }
        }
        public static void Tick()
        {
            if(world==null||world!=GameManager.Instance?.World){Stop();return;}
            if(dirty&&Time.realtimeSinceStartup>=saveAt)Save();
        }
        public static void Broadcast()
        {
            if(world==null||world.IsRemote()||ConnectionManager.Instance==null)return;
            SurveillanceClient.Receive(SurveillanceSnapshot.FromServer(),world);
            if(ConnectionManager.Instance.IsServer)ConnectionManager.Instance.SendPackage(NetPackageManager.GetPackage<NetPackagePZSurveillanceSnapshot>().Setup(SurveillanceSnapshot.FromServer()));
        }
    }

    public sealed class SurveillanceSnapshot
    {
        public long Revision;
        public SurveillanceDevice[] Devices=new SurveillanceDevice[0];
        static long sequence;
        public static SurveillanceSnapshot FromServer()
        {
            var list=SurveillanceState.Devices.Take(256).Select(d=>d.Clone()).ToArray();
            var world=SurveillanceState.World;
            foreach(var d in list)
            {
                var te=world?.GetTileEntity(d.Position) as TileEntityPowered;
                d.Loaded=te!=null;d.Powered=te!=null&&te.IsPowered;
            }
            return new SurveillanceSnapshot{Revision=++sequence,Devices=list};
        }
    }
}
