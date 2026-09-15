using System;
using System.IO;
using UnityEngine;
namespace SakuraPreview
{
    public static class MintPlacement
    {
        static World previous;static bool done;static float next;
        public static void Tick()
        {
            if(Time.realtimeSinceStartup<next)return;next=Time.realtimeSinceStartup+5;
            if(ConnectionManager.Instance==null||!ConnectionManager.Instance.IsServer)return;
            var world=GameManager.Instance?.World;if(world==null)return;
            if(previous!=world){previous=world;done=false;}if(done)return;
            try
            {
                string save=GameIO.GetSaveGameDir().TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
                if(!string.Equals(Path.GetFileName(save),"GUA2",StringComparison.OrdinalIgnoreCase)||
                   !string.Equals(Path.GetFileName(Path.GetDirectoryName(save)),"Navezgane",StringComparison.OrdinalIgnoreCase))return;
                string marker=Path.Combine(save,"mint-guardian-placement.txt");if(File.Exists(marker)){done=true;return;}
                const int x=-1596,z=-1185;
                bool near=false;foreach(var player in world.Players.list)if(player!=null&&(player.position-new Vector3(x,player.position.y,z)).sqrMagnitude<6400){near=true;break;}
                if(!near||world.GetChunkFromWorldPos(x,z)==null)return;
                foreach(var entity in world.Entities.list)if(entity is EntitySakura npc&&npc.IsGuardian&&(npc.position-new Vector3(x,npc.position.y,z)).sqrMagnitude<100)
                {File.WriteAllText(marker,"existing "+npc.entityId);done=true;return;}
                int type=EntityClass.FromString("mintGuardian");if(!EntityClass.list.ContainsKey(type))return;
                int height=world.GetHeight(x,z);
                for(int y=Math.Max(1,height-4);y<=Math.Min(250,height+5);y++)
                {
                    var p=new Vector3i(x,y,z);
                    if(world.IsWithinTraderArea(p)||world.IsWater(p)||!world.GetBlock(p).isair||!world.GetBlock(new Vector3i(x,y+1,z)).isair||world.GetBlock(new Vector3i(x,y-1,z)).isair)continue;
                    var npc=EntityFactory.CreateEntity(type,new Vector3(x+.5f,y,z+.5f)) as EntitySakura;
                    if(npc==null)throw new Exception("Mint factory failed");
                    world.SpawnEntityInWorld(npc);File.WriteAllText(marker,"entity "+npc.entityId+" at "+p);done=true;
                    Log.Out("[MintGuardian] Spawned stationary defender at "+p);return;
                }
                done=true;Log.Warning("[MintGuardian] Preview location occupied; nothing changed.");
            }
            catch(Exception ex){done=true;Log.Error("[MintGuardian] Placement stopped: "+ex.Message);}
        }
    }
}
