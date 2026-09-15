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
            new Harmony("yf.sakura.preview.placement").Patch(
                AccessTools.Method(typeof(GameManager),"Update"),
                postfix:new HarmonyMethod(typeof(PreviewPlacement),nameof(Tick)));
            Log.Out("[SakuraPreview] Model-only preview. GUA2/Navezgane, Joel west side: 1590 W 1185 S.");
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
                var marker=Path.Combine(save,"sakura-preview-placement.txt");
                if(File.Exists(marker)){done=true;return;}
                // Wait until an actual player loads this area. Never force a new
                // region or overwrite existing construction to place a preview.
                bool nearby=false;
                foreach(var player in world.Players.list)
                    if(player!=null && (player.position-new Vector3(X,player.position.y,Z)).sqrMagnitude<6400){nearby=true;break;}
                if(!nearby || world.GetChunkFromWorldPos(X,Z)==null)return;
                var block=Block.GetBlockValue(BlockName,true);
                if(block.isair){done=true;Log.Error("[SakuraPreview] Preview block was not registered; no placement.");return;}
                int surface=world.GetHeight(X,Z);
                for(int y=Math.Max(1,surface-4);y<=Math.Min(250,surface+5);y++)
                {
                    var p=new Vector3i(X,y,Z);
                    var current=world.GetBlock(p);
                    if(current.type==block.type){File.WriteAllText(marker,p.ToString());done=true;return;}
                    if(!current.isair || !world.GetBlock(new Vector3i(X,y+1,Z)).isair ||
                        world.GetBlock(new Vector3i(X,y-1,Z)).isair || world.IsWithinTraderArea(p))continue;
                    world.SetBlockRPC(new BlockValueRef(p),block);
                    // Mark only after observing the placed block on a later tick.
                    Log.Out("[SakuraPreview] Requested model preview placement at "+p+". No quest/NPC AI.");
                    return;
                }
                done=true;
                Log.Warning("[SakuraPreview] Location obstructed; nothing replaced. Clear 1590 W 1185 S and reload.");
            }
            catch(Exception ex){done=true;Log.Error("[SakuraPreview] Placement stopped: "+ex.Message);}
        }
    }
}
