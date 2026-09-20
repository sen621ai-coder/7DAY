namespace YFAutomation
{
    // One recipe policy for product selection, preview and server-side production.
    public static class RecipeMachines
    {
        public static string Area(string kind)
        {
            switch(kind)
            {
                case "yfAutoKitchen":return "campfire";
                case "yfAutoForge":return "forge";
                case "yfAutoWorkbench":return "workbench";
                default:return null;
            }
        }
        public static bool IsMachine(string kind)=>Area(kind)!=null;
        public static bool Supports(string kind,Recipe recipe)
        {
            string area=Area(kind);
            if(area==null||recipe==null||recipe.IsScrap||recipe.GetOutputItemClass()==null||recipe.GetOutputItemClass().HasQuality)return false;
            // Include handcrafting (e.g. gunpowder), but not chemistry-station recipes.
            return recipe.craftingArea==area||kind=="yfAutoWorkbench"&&string.IsNullOrEmpty(recipe.craftingArea);
        }
    }
}
