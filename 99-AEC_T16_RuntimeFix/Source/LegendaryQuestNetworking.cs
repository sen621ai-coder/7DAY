using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;

namespace AECT16RuntimeFix
{
    public static class LegendaryQuestNetworking
    {
        // An impossible native difficulty makes stale clients fail closed, not
        // display unrelated vanilla offers when they do not have this DLL.
        public const int MenuMarker = -19;
        private static readonly Regex pagePattern = new Regex(
            @"^pzaec_t(16|17|18|19)_(small|medium|large|huge|massive)$", RegexOptions.CultureInvariant);
        private static readonly string[] sizes = { "small", "medium", "large", "huge", "massive" };
        private static DateTime nextWarning;

        public static void Install(Harmony harmony)
        {
            try
            {
                var ctor = AccessTools.Constructor(typeof(DialogResponseQuest), new[] {
                    typeof(string), typeof(string), typeof(string), typeof(string), typeof(Dialog), typeof(int), typeof(int) });
                if (ctor == null) throw new MissingMethodException("DialogResponseQuest constructor");
                harmony.Patch(ctor,
                    prefix: new HarmonyMethod(typeof(LegendaryQuestNetworking), nameof(ResponsePrefix)),
                    postfix: new HarmonyMethod(typeof(LegendaryQuestNetworking), nameof(ResponsePostfix)));
                Debug.Log("[AEC-Quest-NetFix] Legendary pages use server-assigned POIs and synchronized offers; per-difficulty removal indices enabled.");
            }
            catch (Exception ex) { Warn("Menu bridge installation failed: " + ex.GetBaseException().Message); }
            try
            {
                var packet = AccessTools.Method(typeof(NetPackageQuestGotoPoint), "ProcessPackage",
                    new[] { typeof(World), typeof(GameManager) });
                if (packet == null) throw new MissingMethodException("NetPackageQuestGotoPoint.ProcessPackage");
                harmony.Patch(packet, prefix: new HarmonyMethod(typeof(LegendaryQuestNetworking), nameof(PacketPrefix)));
                harmony.Patch(packet, transpiler: new HarmonyMethod(typeof(LegendaryQuestNetworking), nameof(PacketNullPoiTranspiler)));
                Debug.Log("[AEC-Quest-NetFix] Missing-player and missing-POI packet guards enabled.");
            }
            catch (Exception ex) { Warn("Packet guard installation failed: " + ex.GetBaseException().Message); }
        }

        public static string QuestIdForPage(string page)
        {
            if (string.IsNullOrEmpty(page)) return null;
            var match = pagePattern.Match(page);
            if (!match.Success) return null;
            int area = Array.IndexOf(sizes, match.Groups[2].Value) + 1;
            return "aec_quest_T" + match.Groups[1].Value + "_A" + area + "_clear";
        }

        public static int[] OfferTiers(int currentTier)
        {
            if (currentTier <= 0) return new int[0];
            if (currentTier < 16) return new[] { currentTier };
            currentTier = Math.Min(19, currentTier);
            var tiers = new int[currentTier - 15];
            for (int i = 0; i < tiers.Length; i++) tiers[i] = 16 + i;
            return tiers;
        }

        // The UI reads an absolute list index; RemoveQuest packets count all
        // quests of the selected difficulty, even special/non-AEC quests.
        public static int ResolveIndex(IList<string> ids, IList<int> difficulties, IList<bool> ready,
            string wantedId, int slot, out int removalIndex)
        {
            removalIndex = -1;
            if (ids == null || difficulties == null || ready == null || string.IsNullOrEmpty(wantedId) ||
                ids.Count != difficulties.Count || ids.Count != ready.Count || slot < 0 || slot >= 6) return -1;
            int seen = 0;
            for (int i = 0; i < ids.Count; i++)
            {
                if (!ready[i] || !LegendaryAdventure.MatchesPage(ids[i], wantedId)) continue;
                if (seen++ != slot) continue;
                int relative = 0;
                for (int j = 0; j < i; j++) if (difficulties[j] == difficulties[i]) relative++;
                if (relative > byte.MaxValue) return -1;
                removalIndex = relative;
                return i;
            }
            return -1;
        }

        public static void ResponsePrefix(string _returnStatementID, ref string _questID,
            ref string _type, ref int _listIndex, ref int _tier, out int __state)
        {
            __state = -1;
            string wanted = QuestIdForPage(_returnStatementID);
            if (_tier != MenuMarker || wanted == null) return;
            int slot = _listIndex;
            // Never enter the native CreateQuest/SetupPosition(trader, null) path.
            _questID = "";
            _type = "";
            _tier = -1;
            _listIndex = int.MaxValue;
            if (slot < 0 || slot >= 6) return;
            try
            {
                var world = GameManager.Instance == null ? null : GameManager.Instance.World;
                var player = world == null ? null : world.GetPrimaryPlayer();
                if (player == null) return;
                var ui = LocalPlayerUI.GetUIForPlayer(player);
                var trader = ui == null || ui.xui == null || ui.xui.Dialog == null
                    ? null : ui.xui.Dialog.Respondent as EntityTrader;
                if (trader == null) return;
                // A remote player's request can update the trader's shared field.
                // On a listen server restore the host's own authoritative cache.
                if (SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer && QuestEventManager.Current != null)
                {
                    var own = QuestEventManager.Current.GetQuestList(world, trader.entityId, player.entityId);
                    if (own != null) trader.activeQuests = own;
                }
                var offers = trader.activeQuests;
                if (offers == null) return;
                var ids = new List<string>();
                var tiers = new List<int>();
                var ready = new List<bool>();
                foreach (var quest in offers)
                {
                    ids.Add(quest == null ? null : quest.ID);
                    tiers.Add(quest == null || quest.QuestClass == null ? -1 : quest.QuestClass.DifficultyTier);
                    Vector3 location, size;
                    ready.Add(quest != null && quest.QuestClass != null && quest.QuestClass.QuestType == "" &&
                        quest.GetPositionData(out location, Quest.PositionDataTypes.POIPosition) &&
                        quest.GetPositionData(out size, Quest.PositionDataTypes.POISize) && size.x > 0 && size.z > 0);
                }
                int index = ResolveIndex(ids, tiers, ready, wanted, slot, out int relative);
                if (index >= 0) { _listIndex = index; __state = relative; }
            }
            catch (Exception ex) { Warn("Offer mapping failed: " + ex.GetBaseException().Message); }
        }

        public static void ResponsePostfix(DialogResponseQuest __instance, int __state)
        {
            if (__state < 0 || !__instance.IsValid) return;
            foreach (var action in __instance.Actions)
                if (action is DialogActionAddQuest add) add.ListIndex = __state;
        }

        public static bool PacketPrefix(NetPackageQuestGotoPoint __instance, World _world)
        {
            if (_world == null) return false;
            if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer) return true;
            // Do not guess another player's ID for malformed or stale requests.
            if (__instance.playerId > 0 && _world.GetEntity(__instance.playerId) is EntityPlayer) return true;
            Warn("Rejected task-location request with missing player=" + __instance.playerId +
                " questCode=" + __instance.questCode + ". Update/reconnect clients using an older menu DLL.");
            return false;
        }

        public static IEnumerable<CodeInstruction> PacketNullPoiTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var vectorCtor = AccessTools.Constructor(typeof(Vector2), new[] { typeof(float), typeof(float) });
            var pos = AccessTools.Field(typeof(PrefabInstance), "boundingBoxPosition");
            var size = AccessTools.Field(typeof(PrefabInstance), "boundingBoxSize");
            int start = -1;
            for (int i = 22; i + 3 < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Newobj || !Equals(code[i].operand, vectorCtor) ||
                    code[i + 1].opcode != OpCodes.Pop ||
                    (code[i + 3].opcode != OpCodes.Brfalse && code[i + 3].opcode != OpCodes.Brfalse_S)) continue;
                int s = i - 22;
                if (!Equals(code[s + 1].operand, pos) || !Equals(code[s + 5].operand, size) ||
                    !Equals(code[s + 12].operand, pos) || !Equals(code[s + 16].operand, size) ||
                    code[s].opcode != code[i + 2].opcode || !Equals(code[s].operand, code[i + 2].operand)) continue;
                if (start >= 0) throw new InvalidOperationException("Ambiguous unused POI center calculation.");
                start = s;
            }
            if (start < 0) throw new InvalidOperationException("Unsafe POI center calculation was not found.");
            // The discarded Vector2 dereferences prefab before the native null
            // check. Nop just that unused expression; retain the null check, retry,
            // response payload, client path, branch labels and exception regions.
            for (int i = start; i < start + 24; i++)
            {
                code[i].opcode = OpCodes.Nop;
                code[i].operand = null;
            }
            return code;
        }

        private static void Warn(string message)
        {
            if (DateTime.UtcNow < nextWarning) return;
            nextWarning = DateTime.UtcNow.AddSeconds(15);
            Debug.LogWarning("[AEC-Quest-NetFix] " + message);
        }
    }
}
