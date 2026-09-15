using System;
using System.Linq;
using HarmonyLib;

namespace AECT16RuntimeFix
{
    // Native SharedKillServer validates party membership/range and transmits the
    // entity CLASS even when the victim is not streamed on the receiving client.
    // Credit before native SharedKillClient looks up that optional corpse.
    public static class RelayHuntPartyCredit
    {
        private static readonly string[] Families = { "Dumdum", "Executioner", "Mechanician" };
        private static readonly string[] Classes = { "bossAECDumdum", "AECTheExecutionerBoss", "AECTheMechanicianBoss" };

        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(GameManager), "SharedKillClient",
                new[] { typeof(int), typeof(int), typeof(EntityPlayerLocal), typeof(int), typeof(int) }),
                prefix: new HarmonyMethod(typeof(RelayHuntPartyCredit), nameof(BeforeSharedKill)));
            T16RuntimeFixMod.SafeLog("[AEC-RelayHunt] Party kill credit active; native party range; no corpse dependency.");
        }

        public static string TargetForQuest(string id)
        {
            for (int tier = 16; tier <= 19; tier++)
                for (int i = 0; i < Families.Length; i++)
                    if (id == "PZAECRelayHunt" + Families[i] + "T" + tier)
                        return Classes[i] + "T" + tier;
            return null;
        }

        public static bool Eligible(string id, string killedClass, bool active, int phase,
            int sharedOwner, int playerId, bool complete)
        {
            string target = TargetForQuest(id);
            return target != null && target == killedClass && active && phase == 4 &&
                playerId >= 0 && (sharedOwner < 0 || sharedOwner == playerId) && !complete;
        }

        public static void Credit(Quest quest, int playerId, string killedClass)
        {
            if (quest == null || !Eligible(quest.ID, killedClass, quest.Active, quest.CurrentPhase,
                quest.SharedOwnerID, playerId, false) || !quest.CheckRequirements()) return;
            foreach (var objective in quest.Objectives.OfType<ObjectiveEntityKill>().ToArray())
            {
                if (objective.Phase != 4 || objective.ID != killedClass || objective.Complete) continue;
                // These quests require exactly one target; never increment twice
                // if the original native event or a duplicate notification follows.
                objective.CurrentValue = 1;
                objective.Refresh();
            }
        }

        public static void BeforeSharedKill(GameManager __instance, int __0, EntityPlayerLocal __2)
        {
            try
            {
                var player = __2 ?? __instance.World?.GetPrimaryPlayer();
                if (player?.QuestJournal?.quests == null || player.IsDead()) return;
                string killedClass = EntityClass.list[__0]?.entityClassName;
                if (killedClass == null) return;
                foreach (var quest in player.QuestJournal.quests.ToArray())
                    Credit(quest, player.entityId, killedClass);
            }
            catch (Exception ex)
            {
                T16RuntimeFixMod.SafeLog("[AEC-RelayHunt] Party credit failed: " + ex.GetBaseException().Message);
            }
        }
    }
}
