using System;
namespace YFAutomation
{
    public static class TransferRules
    {
        public const int Batch=16;
        public static int Amount(int available,int existing,int limit,int budget)
        {
            if(available<=0||existing<0||limit<=0||existing>=limit||budget<=0)return 0;
            return Math.Min(Math.Min(available,limit-existing),Math.Min(Batch,budget));
        }
        public static bool SameChunk(int ax,int az,int bx,int bz)=> (ax>>4)==(bx>>4)&&(az>>4)==(bz>>4);
        public static bool SameOwner(string a,string b)=>!string.IsNullOrEmpty(a)&&string.Equals(a,b,StringComparison.Ordinal);
    }
}
