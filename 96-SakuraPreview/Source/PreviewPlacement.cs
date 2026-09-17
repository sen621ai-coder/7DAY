using System;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace SakuraPreview
{
    public sealed class PreviewPlacement : IModApi
    {
        static World lastWorld;
        static bool done;
        static float nextCheck;
        const int X=-1590, Z=-1185;
        const string BlockName="sakuraNpcPreview";
        public void InitMod(Mod mod)
        {
            SakuraVisual.ModPath=mod.Path;
            new Harmony("yf.sakura.escort.death.events").Patch(
                AccessTools.Method(typeof(EntityAlive),nameof(EntityAlive.OnEntityDeath)),
                prefix:new HarmonyMethod(typeof(SakuraMissionServer),nameof(SakuraMissionServer.RecordEnemyDeath)));
            SakuraFriendlyProtection.Install(new Harmony("yf.sakura.friendly.protection"));
            SakuraTraderRescue.Install(new Harmony("yf.sakura.trader.rescue"));
            SakuraVoucherRewards.Install(new Harmony("yf.sakura.voucher.rewards"));
            new Harmony("yf.sakura.escort.missions").Patch(AccessTools.Method(typeof(GameManager),"Update"),
                postfix:new HarmonyMethod(typeof(SakuraMissionServer),nameof(SakuraMissionServer.Tick)));
            Log.Out("[SakuraPreview] 0.8.3 dispatch reconciliation enabled for Sakura and Mint T16-T19 blueprints.");
        }
        static void Tick()
        {
            if(Time.realtimeSinceStartup<nextCheck)return;
            nextCheck=Time.realtimeSinceStartup+5;
            try
            {
                if(ConnectionManager.Instance==null || !ConnectionManager.Instance.IsServer)return;
                var world=GameManager.Instance.World;
                if(world==null)return;
                if(lastWorld!=world){lastWorld=world;done=false;}
                if(done)return;
                var save=GameIO.GetSaveGameDir().TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
                if(!string.Equals(Path.GetFileName(save),"GUA2",StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(Path.GetFileName(Path.GetDirectoryName(save)),"Navezgane",StringComparison.OrdinalIgnoreCase))return;
                var marker=Path.Combine(save,"sakura-companion-placement.txt");
                if(File.Exists(marker)){done=true;return;}
                // Wait until an actual player loads this area. Never force a new
                // region or overwrite existing construction to place a preview.
                bool nearby=false;
                foreach(var player in world.Players.list)
                    if(player!=null && (player.position-new Vector3(X,player.position.y,Z)).sqrMagnitude<6400){nearby=true;break;}
                if(!nearby || world.GetChunkFromWorldPos(X,Z)==null)return;
                foreach(var existing in world.Entities.list)
                    if(existing is EntitySakura existingNpc && !existingNpc.IsGuardian){File.WriteAllText(marker,"existing entity "+existing.entityId);done=true;return;}
                int entityType=EntityClass.FromString("sakuraCompanion");
                if(!EntityClass.list.ContainsKey(entityType)){done=true;Log.Error("[SakuraPreview] Companion class missing; placement cancelled.");return;}
                var block=Block.GetBlockValue(BlockName,true);
                if(block.isair){done=true;Log.Error("[SakuraPreview] Preview block was not registered; no placement.");return;}
                int surface=world.GetHeight(X,Z);
                for(int y=Math.Max(1,surface-4);y<=Math.Min(250,surface+5);y++)
                {
                    var p=new Vector3i(X,y,Z);
                    var current=world.GetBlock(p);
                    bool oldPreview=current.type==block.type;
                    if((!current.isair && !oldPreview) || !world.GetBlock(new Vector3i(X,y+1,Z)).isair ||
                        world.GetBlock(new Vector3i(X,y-1,Z)).isair || world.IsWithinTraderArea(p))continue;
                    var npc=EntityFactory.CreateEntity(entityType,new Vector3(X+.5f,y,Z+.5f)) as EntitySakura;
                    if(npc==null)throw new Exception("Companion factory returned wrong entity type");
                    world.SpawnEntityInWorld(npc);
                    // Remove only our own old decorative block after the real entity exists.
                    if(oldPreview)world.SetBlockRPC(new BlockValueRef(p),BlockValue.Air);
                    File.WriteAllText(marker,"entity "+npc.entityId+" at "+p);
                    done=true;
                    Log.Out("[SakuraPreview] Spawned companion at "+p+". Dialogue/follow prototype; no escort quest rewards.");
                    return;
                }
                done=true;
                Log.Warning("[SakuraPreview] Location obstructed; nothing replaced. Clear 1590 W 1185 S and reload.");
            }
            catch(Exception ex){done=true;Log.Error("[SakuraPreview] Placement stopped: "+ex.Message);}
        }
    }
}


