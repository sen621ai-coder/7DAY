namespace YFAutomation
{
    public static class StackCompatibility
    {
        public static bool Matches(ItemValue left,ItemValue right)
        {
            if(left==null||right==null||left.type!=right.type)return false;
            // ItemValue.Equals omits block paint; never merge differently painted blocks.
            if(left.TextureFullArray!=right.TextureFullArray)return false;
            if(left.Seed==right.Seed)return left.Equals(right);
            // Fresh production receives a random seed. Native resource stacking ignores
            // it, but equipment quality and installed modifications must remain distinct.
            if(left.Quality!=0||right.Quality!=0||left.ItemClass?.HasQuality==true||left.HasMods()||right.HasMods())return false;
            var comparable=right.Clone();comparable.Seed=left.Seed;
            return left.Equals(comparable);
        }
    }
}
