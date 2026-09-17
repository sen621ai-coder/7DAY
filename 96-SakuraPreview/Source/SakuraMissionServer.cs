using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using UnityEngine;

namespace SakuraPreview
{
    public static class SakuraMissionServer
    {
        static World current;
        static SakuraMissionJournal journal;
        static string file;
        static float nextTick,lastTick,nextRescueAttempt;
        static bool blocked;
        static readonly System.Random random=new System.Random();
        static readonly XmlSerializer serializer=new XmlSerializer(typeof(SakuraMissionJournal));
        static readonly Dictionary<int,float> requests=new Dictionary<int,float>();
        static readonly HashSet<string> noSafeSite=new HashSet<string>();
        public static string Key(EntityPlayer player)=>player?.PersistentPlayerData?.PrimaryId?.CombinedString;
        static string GrantKey(SakuraRescueGrant grant)=>grant.Key+":"+grant.QuestId.ToLowerInvariant()+":"+grant.Code;
        static EntityPlayer Player(string key)=>current.Players.list.FirstOrDefault(p=>p!=null && Key(p)==key);
        static SakuraMissionState For(int id)=>journal?.Missions.Find(m=>m.NpcId==id);
        static bool SameParty(EntityPlayer a,EntityPlayer b)=>a!=null&&b!=null&&(a.entityId==b.entityId || a.party!=null&&a.party==b.party);
        static float Distance(Vector3 a,float x,float z)=>Vector2.Distance(new Vector2(a.x,a.z),new Vector2(x,z));
        static double Utc()=>DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        static void Save()
        {
            var temp=file+".tmp";
            using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)){serializer.Serialize(stream,journal);stream.Flush(true);}
            if(File.Exists(file))File.Replace(temp,file,file+".bak");else File.Move(temp,file);
        }
        static bool Ready(World world)
        {
            if(world==null || world.IsRemote())return false;
            if(current==world)return !blocked;
            current=world;blocked=false;requests.Clear();noSafeSite.Clear();lastTick=Time.realtimeSinceStartup;
            file=Path.Combine(GameIO.GetSaveGameDir(),"sakura-escort-journal.xml");
            try
            {
                if(File.Exists(file)){using(var stream=File.OpenRead(file))journal=(SakuraMissionJournal)serializer.Deserialize(stream);}
                else journal=new SakuraMissionJournal{NextEncounterUtc=Utc()+120};
                if(journal.Schema!=1)throw new Exception("Unsupported escort journal schema");
                if(journal.Missions.Any(m=>m.Tier<16||m.Tier>19)||journal.Missions.Select(m=>m.NpcId).Distinct().Count()!=journal.Missions.Count)
                    throw new Exception("Invalid mission tiers or duplicate NPC journal entries");
                // Native entity saves and our journal are separate transactions. Fail closed
                // across restart rather than duplicate waves or rewards from an older save.
                foreach(var mission in journal.Missions)if(mission.Active)mission.Fail("服务器重新加载，未完成任务已取消");
                Save();return true;
            }
            catch(Exception ex){blocked=true;Log.Error("[SakuraEscort] Journal unavailable; disabled: "+ex.Message);return false;}
        }
        public static void RecordEnemyDeath(EntityAlive __instance)
        {
            // Never trust a client's death notification or initialize a journal during this hook.
            if(ConnectionManager.Instance==null || !ConnectionManager.Instance.IsServer ||
               __instance==null || current==null || __instance.world!=current || current.IsRemote() ||
               journal==null || blocked)return;
            try
            {
                foreach(var mission in journal.Missions)
                    if(mission.EnemyDied(__instance.entityId))
                    {
                        Save();
                        Log.Out("[SakuraEscort] Confirmed death: mission="+mission.Id+" enemy="+__instance.entityId+" remaining="+mission.Enemies.Count);
                        break;
                    }
            }
            catch(Exception ex){StopAll();Log.Error("[SakuraEscort] Death record failed: "+ex.Message);}
        }
        public static void Tick()
        {
            if(ConnectionManager.Instance==null || !ConnectionManager.Instance.IsServer || Time.realtimeSinceStartup<nextTick)return;
            nextTick=Time.realtimeSinceStartup+1;
            var world=GameManager.Instance?.World;
            // Do not open a save journal during early world construction.
            if(world==null || current!=world && world.Players.list.Count==0)return;
            if(!Ready(world))return;
            double dt=Mathf.Clamp(Time.realtimeSinceStartup-lastTick,.01f,5);lastTick=Time.realtimeSinceStartup;
            try
            {
                foreach(var mission in journal.Missions)
                {
                    ShareWithParty(mission);
                    var npc=world.GetEntity(mission.NpcId) as EntitySakura;
                    if(mission.Phase==EscortPhase.Searching)
                    {
                        if(npc!=null){npc.Leader=-1;if(npc.IsDead())mission.Fail("救援目标已经死亡");}
                    }
                    else if(mission.Active)
                    {
                        var leader=Player(mission.Leader);
                        bool near=npc!=null && leader!=null && !leader.IsDead() && (npc.position-leader.position).sqrMagnitude<=(mission.Guard?900:3600);
                        var members=new HashSet<string>(world.Players.list.Where(p=>p!=null&&!p.IsDead()&&npc!=null&&SameParty(p,leader)&&(p.position-npc.position).sqrMagnitude<=6400).Select(Key));
                        float distance=npc==null?float.MaxValue:Distance(npc.position,mission.TargetX,mission.TargetZ);
                        mission.Tick(dt,npc!=null&&!npc.IsDead(),near,distance,members);
                        if(npc==null && mission.Active)mission.Fail("任务人物所在区域卸载");
                        if(mission.Active && mission.Phase==EscortPhase.Ambush)
                        {
                            foreach(int id in mission.Enemies.ToArray())
                            {
                                var enemy=world.GetEntity(id) as EntityAlive;
                                mission.EnemyObserved(id,enemy!=null,enemy!=null&&enemy.IsDead());
                                if(enemy!=null&&!enemy.IsDead())enemy.SetAttackTarget(id%3==0?(EntityAlive)npc:leader,200);
                            }
                        }
                        if(mission.Active && npc!=null && mission.WantWave(distance,near))SpawnWave(mission,npc,leader);
                        if(npc!=null)npc.Leader=!mission.Guard&&mission.Active&&!mission.Paused&&mission.Phase==EscortPhase.Following&&near?leader.entityId:-1;
                    }
                    if(!mission.Active)
                    {
                        if(mission.TerminalUtc==0)
                        {
                            mission.TerminalUtc=Utc();
                            if(npc!=null)npc.Leader=-1;
                            mission.Paused=false;
                            Log.Out("[SakuraEscort] Mission ended: id="+mission.Id+" phase="+mission.Phase+" reason="+mission.Failure);
                        }
                        Cleanup(mission);
                        // Failed encounters retire immediately, including fixed test NPCs. Successful wilderness encounters retain their claim window.
                        if(npc!=null&&mission.ShouldDespawnNpc(journal.EncounterIds.Contains(npc.entityId),Utc()))
                        {npc.Leader=-1;Save();Log.Out("[SakuraEscort] Retiring NPC: entity="+npc.entityId+" mission="+mission.Id+" phase="+mission.Phase);current.RemoveEntity(npc.entityId,EnumRemoveEntityReason.Despawned);npc=null;}
                    }
                    foreach(var member in mission.Members){var player=Player(member.Key);if(player!=null)SendStatus(mission,npc,player);}
                }
                // Recover orphaned partial spawns after a crash without touching other mods' zombies.
                var tracked=new HashSet<int>(journal.Missions.Where(m=>m.Active).SelectMany(m=>m.Enemies));
                foreach(var entity in world.Entities.list.ToArray())
                    if(entity!=null && EntityClass.GetEntityClassName(entity.entityClass).StartsWith("sakuraAmbushT",StringComparison.Ordinal) && !tracked.Contains(entity.entityId))
                        world.RemoveEntity(entity.entityId,EnumRemoveEntityReason.Despawned);
                TryRescueGrants();
                Save();
            }
            catch(Exception ex)
            {
                StopAll();
                Log.Error("[SakuraEscort] Stopped to protect journal: "+ex.Message);
            }
        }
        public static bool Controls(int npcId)=>!blocked&&For(npcId)?.Active==true;
        public static byte Command(EntitySakura npc,EntityPlayer player,byte action)
        {
            if(!Ready(npc.world) || blocked)return 30;
            if(player==null||player.IsDead()||npc.IsDead()||(player.position-npc.position).sqrMagnitude>16)return 255;
            float next;if(requests.TryGetValue(player.entityId,out next)&&Time.realtimeSinceStartup<next)return 255;
            requests[player.entityId]=Time.realtimeSinceStartup+.5f;
            string key=Key(player);if(string.IsNullOrEmpty(key))return 30;
            var mission=For(npc.entityId);
            try
            {
                if(action>=16&&action<=19)
                {
                    if(mission==null || mission.Phase!=EscortPhase.Searching || mission.Tier!=action || !mission.Members.Any(m=>m.Key==key))return 39;
                    var target=npc.IsGuardian?(Vector3?)npc.position:NearestTrader(npc.position);
                    if(target==null)return 32;
                    mission.TargetX=target.Value.x;mission.TargetZ=target.Value.z;
                    mission.StartDistance=Distance(npc.position,target.Value.x,target.Value.z);
                    mission.Leader=key;
                    foreach(var p in current.Players.list.Where(p=>p!=null&&!p.IsDead()&&SameParty(player,p)&&(p.position-npc.position).sqrMagnitude<=6400 &&
                        !journal.Missions.Any(m=>m.Active&&m.Members.Any(member=>member.Key==Key(p)))))
                    {string memberKey=Key(p);if(!string.IsNullOrEmpty(memberKey))mission.Members.Add(new EscortMember{Key=memberKey});}
                    mission.Phase=EscortPhase.Following;Save();npc.Leader=npc.IsGuardian?-1:player.entityId;SendStatus(mission,npc,player);return 16;
                }
                if(mission==null)return 33;
                if(!mission.Members.Any(m=>m.Key==key))return 34;
                if(action==20){SendStatus(mission,npc,player);return 20;}
                if(action==21 && mission.Active && mission.Leader==key){mission.Fail("带领者放弃任务");npc.Leader=-1;Cleanup(mission);Save();foreach(var member in mission.Members){var participant=Player(member.Key);if(participant!=null)SendStatus(mission,npc,participant);}return 21;}
                if(action==22)
                {
                    var box=ItemClass.GetItem(mission.RewardBox);
                    if(box==null || box.type==0){Log.Error("[SakuraEscort] Missing reward item: "+mission.RewardBox);return 36;}
                    if(!mission.BeginClaim(key))return 35;
                    // Durable intent BEFORE any side effect: retries cannot mint duplicates.
                    var member=mission.Members.Find(m=>m.Key==key);Save();
                    GameManager.Instance.ItemDropServer(new ItemStack(ItemClass.GetItem("casinoCoin"),mission.Coins),player.position+Vector3.up,Vector3.zero,player.entityId,600);
                    GameManager.Instance.ItemDropServer(new ItemStack(box,1),player.position+Vector3.up+new Vector3(.4f,0,0),Vector3.zero,player.entityId,600);
                    if(player is EntityPlayerLocal)localXP((EntityPlayerLocal)player,mission.XP);
                    else ConnectionManager.Instance.SendPackage(NetPackageManager.GetPackage<NetPackageEntityAddExpClient>().Setup(player.entityId,mission.XP,Progression.XPTypes.Quest,null),_attachedToEntityId:player.entityId);
                    member.Receipt=2;Save();SendStatus(mission,npc,player);return 22;
                }
                if(action==23 && mission.Active && mission.MissingSeconds>=10){mission.Leader=key;mission.Paused=false;mission.MissingSeconds=0;Save();return 23;}
                if((action==2||action==3)&&mission.Active&&mission.Phase!=EscortPhase.Searching&&mission.Leader==key){mission.Paused=action==3;Save();return action;}
                return 34;
            }
            catch(Exception ex){StopAll();Log.Error("[SakuraEscort] Command stopped: "+ex.Message);return 30;}
        }
        static void StopAll()
        {
            blocked=true;
            foreach(var mission in journal.Missions)
            {
                mission.Fail("任务系统错误，已停止");
                var npc=current.GetEntity(mission.NpcId) as EntitySakura;if(npc!=null)npc.Leader=-1;
                try{Cleanup(mission);}catch(Exception ex){Log.Warning("[SakuraEscort] Cleanup deferred: "+ex.Message);}
            }
        }
        static void localXP(EntityPlayerLocal p,int amount)=>p.Progression.AddLevelExp(amount,"_xpFromQuest",Progression.XPTypes.Quest);
        static void ShareWithParty(SakuraMissionState mission)
        {
            if(!mission.Active)return;
            var leader=Player(mission.Leader);if(leader==null)return;
            bool changed=false;
            foreach(var player in current.Players.list)
            {
                string key=Key(player);
                bool busy=journal.Missions.Any(m=>m!=mission&&m.Active&&m.Members.Any(member=>member.Key==key));
                if(mission.TryJoin(key,SameParty(leader,player),busy))changed=true;
            }
            if(changed)Save();
        }
        static Vector3? NearestTrader(Vector3 from)
        {
            Vector3? best=null;float bestDistance=float.MaxValue;
            foreach(var area in current.TraderAreas)
            {
                if(area.ProtectSize.x<=0 || area.ProtectSize.z<=0)continue;
                // Closest point just OUTSIDE the protected rectangle, reachable even at night.
                float x=Mathf.Clamp(from.x,area.ProtectPosition.x,area.ProtectPosition.x+area.ProtectSize.x);
                float z=Mathf.Clamp(from.z,area.ProtectPosition.z,area.ProtectPosition.z+area.ProtectSize.z);
                if(x>area.ProtectPosition.x&&x<area.ProtectPosition.x+area.ProtectSize.x&&z>area.ProtectPosition.z&&z<area.ProtectPosition.z+area.ProtectSize.z)x=area.ProtectPosition.x-3;
                var point=new Vector3(x,from.y,z);var away=from-point;away.y=0;if(away.sqrMagnitude>0)point+=away.normalized*3;
                float distance=Distance(from,point.x,point.z);
                if(distance<bestDistance){bestDistance=distance;best=point;}
            }
            return best;
        }
        static bool Ground(float x,float z,float referenceY,out Vector3 position)
        {
            position=Vector3.zero;int ix=Mathf.FloorToInt(x),iz=Mathf.FloorToInt(z);
            if(current.GetChunkFromWorldPos(ix,iz)==null)return false;
            int height=current.GetHeight(ix,iz);
            for(int y=Math.Max(1,height-2);y<=Math.Min(250,height+4);y++)
            {
                var p=new Vector3i(ix,y,iz);
                if(Mathf.Abs(y-referenceY)>8 || current.IsWater(p) || current.IsWithinTraderPlacingProtection(p) || !current.GetBlock(p).isair || !current.GetBlock(new Vector3i(ix,y+1,iz)).isair || current.GetBlock(new Vector3i(ix,y-1,iz)).isair)continue;
                position=new Vector3(ix+.5f,y,iz+.5f);
                // Keep encounter spawns away from all claims and bedrolls, including offline owners.
                var players=GameManager.Instance.GetPersistentPlayerList();
                foreach(var data in players.Players.Values)
                {
                    foreach(var claim in data.LPBlocks)if(Distance(position,claim.x,claim.z)<64)return false;
                    if(data.BedrollPos.y>=0 && Distance(position,data.BedrollPos.x,data.BedrollPos.z)<40)return false;
                }
                return true;
            }
            return false;
        }
        static void SpawnWave(SakuraMissionState mission,EntitySakura npc,EntityPlayer leader)
        {
            var positions=new List<Vector3>();
            for(int attempt=0;attempt<160&&positions.Count<mission.WaveSize;attempt++)
            {
                double angle=random.NextDouble()*Math.PI*2;float radius=24+(float)random.NextDouble()*18;Vector3 p;
                if(!Ground(npc.position.x+(float)Math.Cos(angle)*radius,npc.position.z+(float)Math.Sin(angle)*radius,npc.position.y,out p))continue;
                if(current.Players.list.Any(v=>v!=null&&(v.position-p).sqrMagnitude<324)||positions.Any(v=>(v-p).sqrMagnitude<4))continue;
                positions.Add(p);
            }
            if(positions.Count!=mission.WaveSize){mission.Fail("附近没有足够安全空位生成伏击");return;}
            var roster=mission.NextRoster();
            var types=roster.Select(EntityClass.FromString).ToArray();
            if(types.Any(type=>!EntityClass.list.ContainsKey(type))){mission.Fail("混合伏击或首领配置缺失");return;}
            var ids=new List<int>();
            try
            {
                for(int slot=0;slot<positions.Count;slot++)
                {
                    var enemy=EntityFactory.CreateEntity(types[slot],positions[slot]) as EntityAlive;
                    if(enemy==null)throw new Exception("Ambush factory failure");
                    current.SpawnEntityInWorld(enemy);ids.Add(enemy.entityId);
                    enemy.SetAttackTarget(ids.Count%3==0?(EntityAlive)npc:leader,200);
                }
                mission.CommitWave(ids);Save();
            }
            catch{foreach(int id in ids)current.RemoveEntity(id,EnumRemoveEntityReason.Despawned);mission.Fail("伏击生成失败");throw;}
        }
        static void Cleanup(SakuraMissionState mission)
        {
            // Unloaded ids remain in the journal until encountered again. Never delete another entity type.
            foreach(int id in mission.Enemies.ToArray())
            {
                var entity=current.GetEntity(id);
                if(entity==null)continue;
                if(EntityClass.GetEntityClassName(entity.entityClass).StartsWith("sakuraAmbushT"+mission.Tier,StringComparison.Ordinal))current.RemoveEntity(id,EnumRemoveEntityReason.Despawned);
                mission.Enemies.Remove(id);
            }
        }
        public static byte GrantRescueStatus(EntityPlayer player,string questId,int code,int traderId)
        {
            if(player==null || !Ready(player.world) || string.IsNullOrEmpty(Key(player)))return SakuraDispatchPolicy.Rejected;
            int tier=SakuraDispatchPolicy.Tier(questId);
            if(tier==0 || !QuestClass.s_Quests.Keys.Any(id=>string.Equals(id,questId,StringComparison.OrdinalIgnoreCase)))
                return SakuraDispatchPolicy.Rejected;
            string key=Key(player);
            var grant=journal.RescueGrants.Find(g=>g.Key==key &&
                string.Equals(g.QuestId,questId,StringComparison.OrdinalIgnoreCase) && g.Code==code);
            if(grant==null && player.IsDead())return SakuraDispatchPolicy.Rejected;
            try
            {
                if(grant==null)
                {
                    grant=new SakuraRescueGrant{Key=key,QuestId=questId,Code=code,TraderId=traderId,Tier=tier};
                    journal.RescueGrants.Add(grant);Save();
                    Log.Out("[SakuraRescue] Blueprint registered: "+questId+" code="+code+" player="+player.entityId);
                }
                if(grant.Spawned)return SakuraDispatchPolicy.Spawned;
                return journal.Missions.Any(m=>m.Active&&m.Members.Any(v=>v.Key==key))
                    ?SakuraDispatchPolicy.Queued:noSafeSite.Contains(GrantKey(grant))
                        ?SakuraDispatchPolicy.NoSite:SakuraDispatchPolicy.Selecting;
            }
            catch(Exception ex){StopAll();Log.Error("[SakuraRescue] Grant failed: "+ex.Message);return SakuraDispatchPolicy.Rejected;}
        }
        static void TryRescueGrants()
        {
            if(Time.realtimeSinceStartup<nextRescueAttempt)return;nextRescueAttempt=Time.realtimeSinceStartup+5;
            foreach(var grant in journal.RescueGrants.Where(g=>!g.Spawned))
            {
                var player=Player(grant.Key);
                if(player==null || player.IsDead() || journal.Missions.Any(m=>m.Active&&m.Members.Any(v=>v.Key==grant.Key)))continue;
                // Only loaded, walkable terrain. No forced chunk loads or building replacement.
                bool spawned=false;
                for(int attempt=0;attempt<80;attempt++)
                {
                    bool guard=SakuraDispatchPolicy.IsMint(grant.QuestId);
                    double angle=random.NextDouble()*Math.PI*2;float radius=guard?35+(float)random.NextDouble()*20:90+(float)random.NextDouble()*50;Vector3 p;
                    if(!Ground(player.position.x+(float)Math.Cos(angle)*radius,player.position.z+(float)Math.Sin(angle)*radius,player.position.y,out p))continue;
                    var trader=NearestTrader(p);if(!guard&&(trader==null||Distance(p,trader.Value.x,trader.Value.z)<40))continue;
                    if(current.Entities.list.Any(e=>e is EntitySakura&&(e.position-p).sqrMagnitude<900))continue;
                    int type=EntityClass.FromString(guard?"mintGuardian":"sakuraCompanion");if(!EntityClass.list.ContainsKey(type))return;
                    var npc=EntityFactory.CreateEntity(type,p) as EntitySakura;if(npc==null)return;
                    var mission=new SakuraMissionState{Guard=guard,NpcId=npc.entityId,Tier=grant.Tier,Leader=grant.Key,Phase=EscortPhase.Searching,TargetX=p.x,TargetZ=p.z};
                    mission.Members.Add(new EscortMember{Key=grant.Key});
                    // Save intent before spawn. Restart cancels incomplete missions instead of duplicating NPCs.
                    grant.Spawned=true;journal.Missions.Add(mission);journal.EncounterIds.Add(npc.entityId);Save();
                    current.SpawnEntityInWorld(npc);SendStatus(mission,npc,player);
                    noSafeSite.Remove(GrantKey(grant));spawned=true;
                    Log.Out("[SakuraRescue] T"+grant.Tier+" target spawned at "+p+" entity="+npc.entityId);break;
                }
                if(!spawned && noSafeSite.Add(GrantKey(grant)))
                    Log.Warning("[SakuraRescue] No safe site after 80 attempts: "+grant.QuestId+" code="+grant.Code+" player="+player.entityId);
            }
        }
        static void SendStatus(SakuraMissionState mission,EntitySakura npc,EntityPlayer player)
        {
            var packet=NetPackageManager.GetPackage<NetPackageSakuraMission>().Setup(mission,npc,Key(player));
            if(player is EntityPlayerLocal){packet.DeliverLocal();NetPackageManager.FreePackage(packet);}
            else ConnectionManager.Instance.SendPackage(packet,_attachedToEntityId:player.entityId);
        }
    }
}






