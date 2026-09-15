using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace AECT16RuntimeFix
{
    // Server-owned, stateless composition: loading a save midway through a night
    // cannot reroll its theme. Only notification bookkeeping lives in memory.
    public static class BloodMoonVariety
    {
        private sealed class Notice { public int Night = -1, Phase = -1; }
        private static readonly ConditionalWeakTable<EntityPlayer, Notice> Notices = new ConditionalWeakTable<EntityPlayer, Notice>();
        private static readonly string[] Themes = { "重炮围城", "酸蚀突袭", "干扰猎场", "协同工程" };
        private static readonly string[] Phases = { "试探", "猛攻", "喘息", "终局" };
        // Each row sums to 25; theme changes roles, never enemy health or tier.
        private static readonly int[,] Weights = { { 14, 3, 3, 3, 2 }, { 4, 13, 3, 3, 2 },
            { 4, 3, 10, 5, 3 }, { 5, 4, 4, 6, 6 } };
        private static readonly string[] Roles = { "PZAECSiege_heavy_T", "PZAECSiege_acid_T",
            "PZAECSiegeDisruptorT", "PZAECSiegeSpotterT", "PZAECSiegeLinesmanT" };

        public static int Night(int day, int hour) { return hour < 12 ? day - 1 : day; }
        public static int Theme(int night)
        {
            unchecked {
                uint value = (uint)night + 0x9e3779b9u;
                value = (value ^ (value >> 16)) * 0x85ebca6bu;
                value = (value ^ (value >> 13)) * 0xc2b2ae35u;
                return (int)((value ^ (value >> 16)) % 4);
            }
        }
        public static int Phase(int hour, int minute)
        {
            if (hour < 0 || hour > 23 || minute < 0 || minute > 59) return -1;
            int elapsed = (hour * 60 + minute - 22 * 60 + 1440) % 1440;
            if (elapsed >= 360) return -1;
            if (elapsed < 60) return 0; // 22:00-23:00
            if (elapsed < 180) return 1; // 23:00-01:00
            if (elapsed < 240) return 2; // 01:00-02:00
            return 3;                  // 02:00-04:00
        }
        public static int Chance(int phase)
        {
            return phase == 0 ? 15 : phase == 1 ? 30 : phase == 2 ? 5 : phase == 3 ? 35 : 25;
        }
        public static string Variant(int tier, int theme, int phase, int chanceRoll, int roleRoll)
        {
            if (tier < 16 || tier > 19 || theme < 0 || theme > 3 || chanceRoll < 0 ||
                chanceRoll >= Chance(phase) || roleRoll < 0 || roleRoll >= 25) return null;
            for (int role = 0; role < 5; role++) {
                if (roleRoll < Weights[theme, role]) return Roles[role] + tier;
                roleRoll -= Weights[theme, role];
            }
            return null;
        }
        public static string Select(EntityPlayer target, int tier, GameRandom random)
        {
            ulong time = target.world.GetWorldTime();
            int hour = GameUtils.WorldTimeToHours(time);
            int night = Night(GameUtils.WorldTimeToDays(time), hour);
            int phase = Phase(hour, GameUtils.WorldTimeToMinutes(time));
            // Custom daytime events retain the original mix.
            if (phase < 0) return BloodMoonSiege.Variant(tier, random.RandomRange(100));
            int theme = Theme(night);
            var notice = Notices.GetValue(target, _ => new Notice());
            if (notice.Night != night || notice.Phase != phase) {
                notice.Night = night; notice.Phase = phase;
                try {
                    GameManager.Instance.ChatMessageServer(null, EChatType.Whisper, -1,
                        "[血月] " + Themes[theme] + " · " + Phases[phase] +
                        (phase == 2 ? "：新攻城单位减少，抓紧补弹修墙；现存敌人仍会进攻。" :
                         "：留意特殊攻城单位，优先处理远程威胁。"),
                        new List<int> { target.entityId }, EMessageSender.Server,
                        GeneratedTextManager.BbCodeSupportMode.NotSupported);
                } catch (Exception ex) {
                    Log.Warning("[AEC-BloodMoon] Phase notification failed: " + ex.Message);
                }
            }
            return Variant(tier, theme, phase, random.RandomRange(100), random.RandomRange(25));
        }
    }
}
