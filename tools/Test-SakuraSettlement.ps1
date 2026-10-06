#Requires -Version 7.0
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$state=Get-Content (Join-Path $root '96-SakuraPreview/Source/SakuraMissionState.cs') -Raw
$server=Get-Content (Join-Path $root '96-SakuraPreview/Source/SakuraMissionServer.cs') -Raw
function Read-Method([string]$start,[string]$next){
    $from=$server.IndexOf($start,[StringComparison]::Ordinal)
    $until=$server.IndexOf($next,$from,[StringComparison]::Ordinal)
    if($from -lt 0 -or $until -lt 0){throw "Production method boundary missing: $start"}
    $server.Substring($from,$until-$from)
}
$command=Read-Method 'public static byte Command(' 'static void StopAll('
$retire=Read-Method 'static void RetireNpc(' 'public static byte GrantRescueStatus('
# Exercise production Command/RetireNpc with only game endpoints replaced.
$fixtures=@'
namespace UnityEngine
{
    public static class Time { public static float realtimeSinceStartup; }
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public float sqrMagnitude=>x*x+y*y+z*z;
        public static Vector3 up=>new Vector3(0,1,0);
        public static Vector3 zero=>new Vector3();
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
    }
}
public class EntityPlayer
{
    public int entityId;
    public string Key;
    public object party;
    public UnityEngine.Vector3 position;
    public bool IsDead()=>false;
}
public class EntityPlayerLocal : EntityPlayer { }
public class PlayerList { public List<EntityPlayer> list=new List<EntityPlayer>(); }
public enum EnumRemoveEntityReason { Despawned }
public class MissionWorld
{
    public PlayerList Players=new PlayerList();
    public int Removals;
    public bool ThrowOnRemove;
    public void RemoveEntity(int id,EnumRemoveEntityReason reason)
    {
        if(!SakuraPreview.SakuraMissionServer.Saved.Missions[0].NpcRetired)throw new Exception("Removal before retirement persisted");
        if(ThrowOnRemove)throw new Exception("Simulated entity removal interruption");
        Removals++;
    }
}
public class ItemValue { public int type=1; }
public static class ItemClass { public static ItemValue GetItem(string id)=>new ItemValue(); }
public class ItemStack { public ItemStack(ItemValue item,int count){} }
public class GameManager
{
    public static GameManager Instance=new GameManager();
    public int Drops,XP;
    public bool ThrowOnDrop;
    public void ItemDropServer(ItemStack item,UnityEngine.Vector3 pos,UnityEngine.Vector3 velocity,int player,int lifetime)
    {
        if(!SakuraPreview.SakuraMissionServer.Saved.Missions[0].Members.Exists(m=>m.Key=="P"+player&&m.Receipt==1))throw new Exception("Reward issued before intent persisted");
        if(ThrowOnDrop)throw new Exception("Simulated reward delivery interruption");
        Drops++;
    }
}
public static class Log { public static void Out(string s){} public static void Error(string s){} }
public static class Progression { public enum XPTypes { Quest } }
public class NetPackageEntityAddExpClient
{
    public NetPackageEntityAddExpClient Setup(int player,int amount,Progression.XPTypes kind,object source)=>this;
}
public static class NetPackageManager { public static T GetPackage<T>() where T:new()=>new T(); }
public class ConnectionManager
{
    public static ConnectionManager Instance=new ConnectionManager();
    public void SendPackage(NetPackageEntityAddExpClient packet,int _attachedToEntityId){GameManager.Instance.XP++;}
}
namespace SakuraPreview
{
    public class EntitySakura
    {
        public int entityId=105235,Leader;
        public bool IsGuardian;
        public MissionWorld world;
        public UnityEngine.Vector3 position;
        public bool IsDead()=>false;
    }
    public static class SakuraMissionServer
    {
        static MissionWorld current;
        static SakuraMissionJournal journal;
        static bool blocked;
        static readonly Dictionary<int,float> requests=new Dictionary<int,float>();
        public static SakuraMissionJournal Saved;
        public static double Now;
        static bool Ready(MissionWorld world)=>!blocked;
        static string Key(EntityPlayer player)=>player.Key;
        static EntityPlayer Player(string key)=>current.Players.list.Find(p=>p.Key==key);
        static bool SameParty(EntityPlayer a,EntityPlayer b)=>a==b||a.party!=null&&a.party==b.party;
        static SakuraMissionState For(int id)=>journal.Missions.Find(m=>m.NpcId==id);
        static double Utc()=>Now;
        static UnityEngine.Vector3? NearestTrader(UnityEngine.Vector3 pos)=>pos;
        static float Distance(UnityEngine.Vector3 p,float x,float z)=>0;
        static void SendStatus(SakuraMissionState mission,EntitySakura npc,EntityPlayer player){}
        static void Cleanup(SakuraMissionState mission){}
        static void StopAll(){blocked=true;}
        static void localXP(EntityPlayerLocal p,int amount){GameManager.Instance.XP++;}
        static void Save()
        {
            var serializer=new System.Xml.Serialization.XmlSerializer(typeof(SakuraMissionJournal));
            using(var stream=new System.IO.MemoryStream())
            {
                serializer.Serialize(stream,journal);stream.Position=0;
                Saved=(SakuraMissionJournal)serializer.Deserialize(stream);
            }
        }
        public static SakuraMissionState Reset(int tier,bool guard,out EntitySakura npc,out EntityPlayerLocal a,out EntityPlayer b)
        {
            blocked=false;requests.Clear();Now=101;UnityEngine.Time.realtimeSinceStartup=0;
            current=new MissionWorld();GameManager.Instance=new GameManager();
            a=new EntityPlayerLocal{entityId=1,Key="P1"};b=new EntityPlayer{entityId=2,Key="P2"};
            current.Players.list.Add(a);current.Players.list.Add(b);
            npc=new EntitySakura{world=current,IsGuardian=guard};
            var mission=new SakuraMissionState{Tier=tier,Guard=guard,NpcId=npc.entityId,Phase=EscortPhase.Completed,Elapsed=100,TerminalUtc=100};
            mission.Members.Add(new EscortMember{Key=a.Key,NearSeconds=100});
            mission.Members.Add(new EscortMember{Key=b.Key,NearSeconds=0});
            journal=new SakuraMissionJournal();journal.Missions.Add(mission);Save();return mission;
        }
        public static byte Request(EntitySakura npc,EntityPlayer player,byte action)
        {UnityEngine.Time.realtimeSinceStartup+=1;return Command(npc,player,action);}
        public static SakuraMissionState Reload()
        {journal=Saved;blocked=false;requests.Clear();return journal.Missions[0];}
'@
$tests=@'
    }
}
public static class SakuraSettlementRegression
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    public static string Run()
    {
        foreach(bool guard in new[]{false,true})foreach(int tier in new[]{16,17,18,19})
        {
            SakuraPreview.EntitySakura npc;EntityPlayerLocal a;EntityPlayer b;
            var solo=SakuraPreview.SakuraMissionServer.Reset(tier,guard,out npc,out a,out b);
            Check(SakuraPreview.SakuraMissionServer.Request(npc,a,22)==22,"Solo reward accepted");
            Check(GameManager.Instance.Drops==2&&GameManager.Instance.XP==1&&solo.Members[0].Receipt==2,"Exactly one item/XP payout");
            Check(solo.NpcRetired&&npc.world.Removals==1,"Solo NPC removed after reward, not before");
            for(int i=0;i<3;i++)Check(SakuraPreview.SakuraMissionServer.Request(npc,a,22)==35,"Repeated stale NPC claims rejected");
            Check(SakuraPreview.SakuraMissionServer.Request(npc,a,(byte)tier)==39,"Completed NPC start rejected");
            Check(GameManager.Instance.Drops==2&&GameManager.Instance.XP==1,"Replay never delivers another reward");
            var loaded=SakuraPreview.SakuraMissionServer.Reload();
            Check(loaded.NpcRetired&&SakuraPreview.SakuraMissionServer.Request(npc,a,22)==35,"Reloaded retirement blocks stale entity claim");
            var group=SakuraPreview.SakuraMissionServer.Reset(tier,guard,out npc,out a,out b);group.Members[1].NearSeconds=100;
            Check(SakuraPreview.SakuraMissionServer.Request(npc,a,22)==22&&!group.NpcRetired&&npc.world.Removals==0,"First teammate does not remove NPC prematurely");
            Check(SakuraPreview.SakuraMissionServer.Request(npc,a,22)==35&&GameManager.Instance.Drops==2,"First teammate cannot take second teammate reward");
            Check(SakuraPreview.SakuraMissionServer.Request(npc,b,22)==22&&group.NpcRetired&&npc.world.Removals==1,"Last teammate claim retires NPC");
            Check(GameManager.Instance.Drops==4&&GameManager.Instance.XP==2,"Local and remote teammates each receive one payout");
            var expired=SakuraPreview.SakuraMissionServer.Reset(tier,guard,out npc,out a,out b);SakuraPreview.SakuraMissionServer.Now=1900;
            Check(SakuraPreview.SakuraMissionServer.Request(npc,a,22)==35&&expired.NpcRetired&&npc.world.Removals==1,"Deadline enforced on request before tick");
            Check(GameManager.Instance.Drops==0&&GameManager.Instance.XP==0,"Expired claim has no reward side effects");
            var legacy=SakuraPreview.SakuraMissionServer.Reset(tier,guard,out npc,out a,out b);legacy.Members[0].Receipt=2;
            Check(SakuraPreview.SakuraMissionServer.Request(npc,a,22)==35&&legacy.NpcRetired,"Previously claimed legacy mission retires");
            var interrupted=SakuraPreview.SakuraMissionServer.Reset(tier,guard,out npc,out a,out b);npc.world.ThrowOnRemove=true;
            Check(SakuraPreview.SakuraMissionServer.Request(npc,a,22)==30,"Removal interruption reported");
            Check(SakuraPreview.SakuraMissionServer.Saved.Missions[0].NpcRetired&&SakuraPreview.SakuraMissionServer.Saved.Missions[0].Members[0].Receipt==2,"Removal interruption keeps durable issued receipt and fence");
            SakuraPreview.SakuraMissionServer.Reload();npc.world.ThrowOnRemove=false;
            Check(SakuraPreview.SakuraMissionServer.Request(npc,a,22)==35&&GameManager.Instance.Drops==2&&npc.world.Removals==1,"Removal retry after restart does not pay again");
            var uncertain=SakuraPreview.SakuraMissionServer.Reset(tier,guard,out npc,out a,out b);GameManager.Instance.ThrowOnDrop=true;
            Check(SakuraPreview.SakuraMissionServer.Request(npc,a,22)==30&&SakuraPreview.SakuraMissionServer.Saved.Missions[0].Members[0].Receipt==1,"Interrupted reward keeps pending intent");
            SakuraPreview.SakuraMissionServer.Reload();GameManager.Instance.ThrowOnDrop=false;
            Check(SakuraPreview.SakuraMissionServer.Request(npc,a,22)==35&&GameManager.Instance.Drops==0,"Pending intent cannot replay after reload");
        }
        return "PASS: "+checks+" production settlement checks (all tiers and NPC types, local/remote teammates, replay, deadline, legacy records and interrupted delivery/removal; game endpoints stubbed).";
    }
}
'@
Add-Type -TypeDefinition ('using System.Linq; using UnityEngine;'+[Environment]::NewLine+$state+[Environment]::NewLine+$fixtures+$command+$retire+$tests)
[SakuraSettlementRegression]::Run()
