using System;
using System.IO;
using System.Xml;
using UnityEngine;

namespace PZAEC.Fishing.Runtime
{
    // Explicit per-save request only; normal worlds without this file receive nothing.
    public static class TestEquipmentDelivery
    {
        static float checkAt;
        static string lastSave;
        static bool disabled;
        public static void Tick(World world)
        {
            if(world==null||world.IsRemote()||GameManager.IsDedicatedServer)return;
            string save=Path.GetFullPath(GameIO.GetSaveGameDir()).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
            if(lastSave!=save){lastSave=save;disabled=false;checkAt=0;}
            if(disabled||Time.realtimeSinceStartup<checkAt)return;
            checkAt=Time.realtimeSinceStartup+2;
            string path=Path.Combine(save,"pzaec-fishing-test-kit.xml");
            if(!File.Exists(path)){disabled=true;return;}
            var player=world.GetPrimaryPlayer();
            if(player==null||player.IsDead()||player.bag==null||player.Buffs==null||player.PersistentPlayerData==null)return;
            try {
                var xml=new XmlDocument {XmlResolver=null};
                using(var reader=XmlReader.Create(path,new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null}))xml.Load(reader);
                var root=xml.DocumentElement;Guid id;
                if(root==null||root.Name!="FishingTestKit"||!Guid.TryParse(root.GetAttribute("id"),out id)||id==Guid.Empty||
                    !string.Equals(save,Path.GetFullPath(root.GetAttribute("savePath")).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Invalid test kit request or wrong save path");
                int index=0;bool complete=true;
                foreach(XmlNode node in root.ChildNodes) {
                    var gift=node as XmlElement;if(gift==null)continue;
                    int count;
                    if(gift.Name!="Item"||!int.TryParse(gift.GetAttribute("count"),out count)||count<1||count>1000)throw new InvalidDataException("Invalid kit item count");
                    var item=ItemClass.GetItem(gift.GetAttribute("name"));
                    if(item.type<=0)throw new InvalidDataException("Unknown kit item");
                    string key="pzaecFishingKit_"+id.ToString("N")+"_"+index++;
                    float stored=player.Buffs.GetCustomVar(key);
                    if(float.IsNaN(stored)||float.IsInfinity(stored)||stored<0||stored>count||stored!=(int)stored)throw new InvalidDataException("Invalid saved kit progress");
                    int delivered=(int)stored;
                    while(delivered<count) {
                        var slots=player.bag.GetSlots();int empty=-1;
                        for(int i=0;i<slots.Length;i++)if(slots[i]==null||slots[i].count<=0){empty=i;break;}
                        if(empty<0){complete=false;break;}
                        int amount=Math.Min(count-delivered,Math.Max(1,item.ItemClass.Stacknumber.Value));
                        var updated=(ItemStack[])slots.Clone();updated[empty]=new ItemStack(item.Clone(),amount);
                        player.bag.SetSlots(updated);delivered+=amount;player.Buffs.SetCustomVar(key,delivered);
                        Log.Out("[PZAEC.Fishing] Test kit delivered "+amount+" x "+gift.GetAttribute("name"));
                    }
                    if(!complete)break;
                }
                if(complete){disabled=true;Log.Out("[PZAEC.Fishing] Test kit complete for local player in "+save);}
            } catch(Exception error){disabled=true;Log.Error("[PZAEC.Fishing] Test kit paused: "+error);}
        }
        public static void Reset(){lastSave=null;disabled=false;checkAt=0;}
    }
}
