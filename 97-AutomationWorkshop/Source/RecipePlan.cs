using System;
using System.Collections.Generic;
using System.Linq;
namespace YFAutomation
{
    public static class ForgeMaterials
    {
        public static string RawName(string category)
        {
            switch(category){case "iron":return "resourceScrapIron";case "brass":return "resourceScrapBrass";
                case "lead":return "resourceScrapLead";case "glass":return "resourceBrokenGlass";
                case "stone":return "resourceRockSmall";case "clay":return "resourceClayLump";default:return "";}
        }
        public static int Count(ItemStack[] items,int type,Func<int,bool> locked)
        {return type<=0?0:items.Where((s,i)=>!locked(i)&&s!=null&&!s.IsEmpty()&&s.itemValue.type==type&&!s.itemValue.HasMods()).Sum(s=>s.count);}
        // A whole raw item may contain several furnace units. Return the exact change
        // as existing intermediate material, rather than destroying fractional value.
        public static bool Consume(ItemStack[] items,int refined,int raw,int weight,int units,Func<int,bool> locked,Func<ItemValue,int> capacity,out int rawUsed)
        {
            rawUsed=0;int use=Math.Min(units,Count(items,refined,locked));
            if(use>0&&!ProductionInventory.Consume(items,refined,use,locked))return false;
            int remaining=units-use;if(remaining<=0)return true;
            if(raw<=0||weight<=0)return false;
            int needed=(int)(((long)remaining+weight-1)/weight);
            if(!ProductionInventory.Consume(items,raw,needed,locked))return false;
            int change=(int)((long)needed*weight-remaining);
            if(change>0&&(refined<=0||!ProductionInventory.Produce(items,new ItemStack(new ItemValue(refined),change),locked,capacity,0)))return false;
            rawUsed=needed;return true;
        }
    }
    public sealed class RecipeMaterial
    {
        public string Name="";public int Need,Have;public bool Tool;
    }
    public sealed class RecipePlan
    {
        public Recipe Recipe;
        public ItemStack[] Input;
        public bool Ready;
        public float MeltSeconds;
        public readonly List<string> Lines=new List<string>();
        public readonly List<RecipeMaterial> Materials=new List<RecipeMaterial>();
        public static int Required(Recipe recipe,ItemStack ingredient,EntityPlayer player)
        {
            int count=ingredient.count;
            if(recipe.UseIngredientModifier)
            {
                count=(int)EffectManager.GetValue(PassiveEffects.CraftingIngredientCount,null,count,player,recipe,
                    FastTags<TagGroup.Global>.Parse(ingredient.itemValue.ItemClass.GetItemName()),true,true,true,true,true,recipe.craftingTier,true,false);
                if(count>0)count=Math.Max(1,(int)(count*XUiM_Recipes.GetCraftingInputModifier(recipe)));
            }
            return Math.Max(0,count);
        }
        public static RecipePlan Build(Recipe recipe,ItemStack[] input,Func<int,bool> locked,EntityPlayer player)
        {
            var plan=new RecipePlan{Recipe=recipe,Input=ProductionInventory.Clone(input),Ready=player!=null&&recipe.IsUnlocked(player)};
            if(player==null){plan.Lines.Add("等待设备所有者上线，才能计算实际配方");return plan;}
            recipe.craftingTier=recipe.GetCraftingTier(player);
            if(!plan.Ready)plan.Lines.Add("设备所有者尚未解锁此配方");
            if(recipe.craftingToolType>0)
            {
                bool tool=ForgeMaterials.Count(input,recipe.craftingToolType,locked)>0;
                plan.Materials.Add(new RecipeMaterial{Name=ItemClass.GetForId(recipe.craftingToolType).GetItemName(),Need=1,Have=tool?1:0,Tool=true});
                plan.Lines.Add("工具："+Localization.Get(ItemClass.GetForId(recipe.craftingToolType).GetItemName())+(tool?"  已放入（不消耗）":"  缺少，请放原料区"));
                plan.Ready&=tool;
            }
            foreach(var ingredient in recipe.GetIngredientsSummedUp())
            {
                int count=Required(recipe,ingredient,player);if(count==0)continue;
                string name=ingredient.itemValue.ItemClass.GetItemName();
                if(name.StartsWith("unit_"))
                {
                    string category=name.Substring(5),rawName=ForgeMaterials.RawName(category);
                    int refined=ItemClass.GetItem("yfAutoIngot_"+category).type;
                    var raw=rawName==""?null:ItemClass.GetItem(rawName).ItemClass;
                    int weight=raw!=null&&raw.MadeOfMaterial?.ForgeCategory==category?raw.GetWeight():0;
                    int old=ForgeMaterials.Count(plan.Input,refined,locked),rawType=weight>0?raw.Id:0;
                    int needed=weight>0?(int)(((long)Math.Max(0,count-old)+weight-1)/weight):0;
                    int have=ForgeMaterials.Count(plan.Input,rawType,locked);
                    plan.Materials.Add(new RecipeMaterial{Name=weight>0?rawName:"yfAutoIngot_"+category,Need=weight>0?needed:count,Have=weight>0?have:old});
                    plan.Lines.Add(weight>0?Line(Localization.Get(rawName),needed,have):Line(Localization.Get("yfAutoIngot_"+category),count,old));
                    if(old>0)plan.Lines.Add("  兼容冶炼料抵用 "+Math.Min(old,count)+" 单位");
                    int used;bool enough=ForgeMaterials.Consume(plan.Input,refined,rawType,weight,count,locked,v=>v.ItemClass.Stacknumber.Value,out used);
                    if(!enough&&weight>0&&have>=needed)plan.Lines.Add("  原料区需留空格保存冶炼余料");
                    plan.Ready&=enough;
                    if(enough&&used>0)plan.MeltSeconds+=used*weight*(raw.MeltTimePerUnit>0?raw.MeltTimePerUnit:1);
                }
                else
                {
                    int have=ForgeMaterials.Count(plan.Input,ingredient.itemValue.type,locked);
                    plan.Materials.Add(new RecipeMaterial{Name=name,Need=count,Have=have});
                    plan.Lines.Add(Line(Localization.Get(name),count,have));
                    plan.Ready&=ProductionInventory.Consume(plan.Input,ingredient.itemValue.type,count,locked);
                }
            }
            return plan;
        }
        static string Line(string name,int need,int have)=>name+"：需要 "+need+" / 已有 "+have+(have<need?" / 缺 "+(need-have):" / 已齐");
        public static RecipePlan Select(string product,string kind,ItemStack[] input,Func<int,bool> locked,EntityPlayer player)
        {
            RecipePlan first=null;
            foreach(var r in CraftingManager.GetRecipes(product))
            {
                if(r.IsScrap||r.craftingArea!=(kind=="yfAutoKitchen"?"campfire":"forge")||r.GetOutputItemClass().HasQuality)continue;
                var plan=Build(r,input,locked,player);if(plan.Ready)return plan;
                if(first==null||player!=null&&!first.Recipe.IsUnlocked(player)&&r.IsUnlocked(player))first=plan;
            }
            return first;
        }
    }
}
