using System;
using System.Linq;
using System.Text;

namespace YFAutomation.CargoDrones
{
    public enum CargoWarehouseKind : byte { All,Crate,Input,Output,Cabinet,Other }
    public enum CargoSourceKind : byte { All,Iron,Lead,Coal,Nitrate,Clay,Shale,Brass,Forestry }
    public static class CargoSourceFilter
    {
        public static CargoSourceKind Kind(string block)
        {
            switch(block){case "AutoMinerIron":return CargoSourceKind.Iron;case "AutoMinerLead":return CargoSourceKind.Lead;case "AutoMinerCoal":return CargoSourceKind.Coal;case "AutoMinerNitrate":return CargoSourceKind.Nitrate;case "AutoMinerClay":return CargoSourceKind.Clay;case "AutoMinerShale":return CargoSourceKind.Shale;case "AutoMinerBrass":return CargoSourceKind.Brass;case "yfAutoForestry":return CargoSourceKind.Forestry;default:return CargoSourceKind.All;}
        }
        public static string Label(CargoSourceKind kind)
        {switch(kind){case CargoSourceKind.Iron:return "铁矿机";case CargoSourceKind.Lead:return "铅矿机";case CargoSourceKind.Coal:return "煤矿机";case CargoSourceKind.Nitrate:return "硝石矿机";case CargoSourceKind.Clay:return "黏土矿机";case CargoSourceKind.Shale:return "油页岩矿机";case CargoSourceKind.Brass:return "黄铜矿机";case CargoSourceKind.Forestry:return "自动林场";default:return "全部采集设备";}}
        public static bool Matches(string query,CargoSourceKind kind,string name,string block)
        {
            if(!Enum.IsDefined(typeof(CargoSourceKind),kind)||!CargoRules.IsSource(block)||kind!=CargoSourceKind.All&&Kind(block)!=kind)return false;
            string text=CargoWarehouseFilter.Clean(name)+" "+Label(Kind(block));
            return CargoWarehouseFilter.Clean(query,64).Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries).All(word=>text.IndexOf(word,StringComparison.OrdinalIgnoreCase)>=0);
        }
    }
    public static class CargoWarehouseFilter
    {
        public const int PageSize=8,MaxResults=256;
        public static string Clean(string value,int limit=96)
        {
            var result=new StringBuilder();
            foreach(char c in value??"")
            {
                if(result.Length>=limit)break;
                if(char.IsWhiteSpace(c)||char.IsControl(c)){if(result.Length>0&&result[result.Length-1]!=' ')result.Append(' ');}
                else result.Append(c=='<'?'＜':c=='>'?'＞':c);
            }
            return result.ToString().Trim();
        }
        public static CargoWarehouseKind Kind(string block)
        {
            if(block=="yfAutoInput")return CargoWarehouseKind.Input;
            if(block=="yfAutoOutput")return CargoWarehouseKind.Output;
            string name=(block??"").ToLowerInvariant();
            if(name.Contains("cupboard")||name.Contains("cabinet")||name.Contains("closet"))return CargoWarehouseKind.Cabinet;
            if(name.Contains("crate")||name.Contains("storagebox")||name.Contains("chest"))return CargoWarehouseKind.Crate;
            return CargoWarehouseKind.Other;
        }
        public static string Label(CargoWarehouseKind kind)
        {switch(kind){case CargoWarehouseKind.All:return "全部类型";case CargoWarehouseKind.Crate:return "普通储物箱";case CargoWarehouseKind.Input:return "自动化输入箱";case CargoWarehouseKind.Output:return "自动化输出箱";case CargoWarehouseKind.Cabinet:return "壁橱 / 柜子";default:return "其他容器";}}
        public static bool Matches(string query,CargoWarehouseKind filter,string name,string sign,string block)
        {
            if(!Enum.IsDefined(typeof(CargoWarehouseKind),filter))return false;
            if(filter!=CargoWarehouseKind.All&&Kind(block)!=filter)return false;
            var haystack=Clean(name)+" "+Clean(sign,256)+" "+Label(Kind(block));
            return Clean(query,64).Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries).All(word=>haystack.IndexOf(word,StringComparison.OrdinalIgnoreCase)>=0);
        }
        public static string Display(string name,string sign,CargoPosition position)
        {
            string text=Clean(sign);if(text.Length==0)text=Clean(name);
            if(text.Length==0)text="储物容器";
            return text+"  ("+position.X+", "+position.Y+", "+position.Z+")";
        }
    }
}
