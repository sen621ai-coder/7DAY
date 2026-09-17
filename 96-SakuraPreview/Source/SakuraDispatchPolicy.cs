using System;
using System.Text.RegularExpressions;

namespace SakuraPreview
{
    // Keep dispatch recognition and retry timing independent of game state so
    // saved, already-completed blueprint quests can be reconciled after login.
    public static class SakuraDispatchPolicy
    {
        public const byte Rejected=0,Selecting=1,Queued=2,Spawned=3,NoSite=4;
        public const string Pending="sakuraRescuePending_v1";
        static readonly Regex Pattern=new Regex(@"\A(?:sakura|mint)DispatchT(16|17|18|19)\z",
            RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        public static int Tier(string id)
        {var match=Pattern.Match(id??"");return match.Success?int.Parse(match.Groups[1].Value):0;}
        public static bool IsDispatch(string id)=>Tier(id)!=0;
        public static bool IsMint(string id)=>id!=null&&id.StartsWith("mintDispatchT",StringComparison.OrdinalIgnoreCase)&&IsDispatch(id);
        public static float RetrySeconds(string pending)=>pending=="2"?30f:5f;
        public static bool ShouldRetry(string id,bool failed,string pending,float secondsSinceCheck)
            =>IsDispatch(id)&&!failed&&secondsSinceCheck>=RetrySeconds(pending);
        public static string StatusText(byte status)
        {
            switch(status)
            {
                case Selecting:return "服务器已登记图纸，正在寻找可用地点；请在开阔地面稍候。";
                case Queued:return "服务器已登记图纸；你已有同伴任务，本任务排队等待其结束。";
                case Spawned:return "此图纸已有目标生成记录；请查看同伴任务日志，若无标记请重新加入服务器。";
                case NoSite:return "服务器已登记图纸，但附近暂未找到安全地点；请离开领地、睡袋和水域，到开阔地面等待自动重试。";
                default:return "服务器尚未确认图纸，正在自动重试；请确认房主和客户端都安装了相同版本模组。";
            }
        }
    }
}
