using System;
using System.Collections.Generic;
using System.Linq;
namespace YFAutomation
{
    // Native composite-feature serialization keeps progress in the machine's chunk.
    // No ingredients are reserved: the complete transaction is checked again at finish.
    public sealed class TEFeatureAutomationState : TEFeatureAbs
    {
        public string Job="";
        public float Seconds;
        public override void Read(PooledBinaryReader reader,TileEntity.StreamModeRead mode)
        {
            base.Read(reader,mode);string job=reader.ReadString();float seconds=reader.ReadSingle();
            // Client container/sign updates cannot grant machine progress.
            if(mode==TileEntity.StreamModeRead.FromClient)return;
            Job=job;Seconds=seconds;if(float.IsNaN(Seconds)||float.IsInfinity(Seconds)||Seconds<0)Seconds=0;
        }
        public override void Write(PooledBinaryWriter writer,TileEntity.StreamModeWrite mode)
        {base.Write(writer,mode);writer.Write(Job??"");writer.Write(Seconds);}
        public override void CopyFromInternal(TileEntityComposite source)
        {var other=source.GetFeature<TEFeatureAutomationState>();if(other!=null){Job=other.Job;Seconds=other.Seconds;}}
    }
    public static class ProductionInventory
    {
        public static int ScrapYield(int inputWeight,int outputWeight,float modifier,bool fullReturn)
        {
            if(inputWeight<=0||outputWeight<=0||inputWeight<outputWeight||float.IsNaN(modifier)||float.IsInfinity(modifier)||modifier<0)return 0;
            return Math.Max(1,(int)((inputWeight/outputWeight)*(fullReturn?1f:0.75f)*modifier));
        }
        public static ItemStack[] Clone(ItemStack[] values)=>values.Select(v=>v==null?ItemStack.Empty:v.Clone()).ToArray();
        // Work on detached arrays; callers only publish after every operation succeeds.
        public static bool Consume(ItemStack[] items,int type,int count,Func<int,bool> locked)
        {
            for(int i=0;i<items.Length&&count>0;i++)
            {
                var s=items[i];if(locked(i)||s==null||s.IsEmpty()||s.itemValue.type!=type||s.itemValue.HasMods())continue;
                int take=Math.Min(count,s.count);s.count-=take;count-=take;
                if(s.count==0)items[i]=ItemStack.Empty;
            }
            return count==0;
        }
        public static bool Produce(ItemStack[] items,ItemStack product,Func<int,bool> locked,Func<ItemValue,int> capacity)
        {
            int left=product.count;
            for(int pass=0;pass<2;pass++)for(int i=1;i<items.Length&&left>0;i++)
            {
                if(locked(i))continue;var s=items[i];bool empty=s==null||s.IsEmpty();
                if(pass==0&&empty||pass==1&&!empty||!empty&&!s.itemValue.Equals(product.itemValue))continue;
                int amount=Math.Min(left,Math.Max(0,capacity(product.itemValue)-(empty?0:s.count)));
                if(amount==0)continue;
                if(empty){s=product.Clone();s.count=0;items[i]=s;}s.count+=amount;left-=amount;
            }
            return left==0;
        }
    }
    public static class Production
    {
        public static bool IsMachine(string name)=>name=="yfAutoKitchen"||name=="yfAutoForge"||name=="yfAutoRecycler"||name=="yfAutoSmelter"||name=="yfAutoFarm"||name=="yfAutoMiner";
        static bool Locked(TEFeatureStorage s,int i)=>s.HasSlotLocksSupport&&s.SlotLocks!=null&&s.SlotLocks[i];
        public static string Step(TileEntityComposite machine,TileEntityComposite source,TileEntityComposite target,EntityPlayer player)
        {
            var state=machine.GetFeature<TEFeatureAutomationState>();
            var input=source.GetFeature<TEFeatureStorage>();var output=target.GetFeature<TEFeatureStorage>();
            if(state==null||input==null||output==null)return "机器组件不完整";
            var config=MachineConfiguration.Get(machine);
            if(config.Paused)return "已暂停（机器配置）";
            if(output.items.Length<2)return "输出箱容量不足";
            bool internalStorage=ReferenceEquals(source,machine)&&ReferenceEquals(target,machine)&&MachineInventory.Has(machine);
            int type=config.Product==""&&internalStorage?0:config.Product==""?(output.items[0].IsEmpty()?0:output.items[0].itemValue.type):ItemClass.GetItem(config.Product).type;
            if(type==0)return internalStorage?"在面板选择目标产品并保存":"配置目标产品或输出箱首格放样品";
            string kind=machine.block.GetBlockName();
            Func<int,bool> inputLocked=i=>Locked(input,i)||internalStorage&&!MachineInventory.IsInput(i)||i==0&&source.block.GetBlockName()=="yfAutoOutput";
            Recipe recipe=null;ItemStack product=null;ItemStack[] nextInput=null;
            float duration=0;string key=null;
            Action complete=null;
            if(kind=="yfAutoFarm"||kind=="yfAutoMiner")
            {
                var work=FieldMachines.Find(GameManager.Instance.World,machine,input.items,inputLocked,type,player);
                if(work==null)return kind=="yfAutoFarm"?"等待自有领地成熟作物/种子":"等待自有领地矿点/钻头耗材";
                nextInput=work.Input;product=work.Product;duration=work.Duration;key=work.Key;complete=work.Complete;
            }
            else if(kind=="yfAutoSmelter")
            {
                string sample=ItemClass.GetForId(type).GetItemName();
                if(!sample.StartsWith("yfAutoIngot_"))return "首格放自动化冶炼料样品";
                string category=sample.Substring("yfAutoIngot_".Length);
                for(int i=0;i<input.items.Length;i++)
                {
                    var s=input.items[i];var item=s.itemValue.ItemClass;
                    if(inputLocked(i)||s.IsEmpty()||item==null||item.HasQuality||s.itemValue.HasMods()||item.GetItemName().StartsWith("yfAutoIngot_")||item.GetItemName().StartsWith("unit_")||item.MadeOfMaterial?.ForgeCategory!=category||item.GetWeight()<=0)continue;
                    nextInput=ProductionInventory.Clone(input.items);nextInput[i].count--;if(nextInput[i].count==0)nextInput[i]=ItemStack.Empty;
                    product=new ItemStack(new ItemValue(type),item.GetWeight());
                    duration=Math.Max(1,item.GetWeight()*(item.MeltTimePerUnit>0?item.MeltTimePerUnit:1));
                    key="melt:"+s.itemValue.type+":"+type+":"+product.count;break;
                }
                if(product==null)return "等待同类可冶炼材料";
            }
            else if(kind=="yfAutoRecycler")
            {
                if(player==null)return "等待机器所有者上线";
                for(int i=0;i<input.items.Length;i++)
                {
                    var s=input.items[i];
                    // Equipment only; keep top quality, modifications and locked slots safe.
                    if(inputLocked(i)||s.IsEmpty()||!s.itemValue.ItemClass.HasQuality||s.itemValue.Quality>=6||s.itemValue.HasMods()||s.itemValue.Meta>0)continue;
                    var scrap=CraftingManager.GetScrapableRecipe(s.itemValue,1);
                    if(scrap==null||scrap.itemValueType!=type||scrap.count<=0)continue;
                    var raw=s.itemValue.ItemClass;var material=ItemClass.GetForId(type);
                    int yield=ProductionInventory.ScrapYield(raw.GetWeight(),material.GetWeight(),XUiM_Recipes.ScrappingOutputModifier,
                        XUiM_Recipes.DisableSmelter&&raw.HasAnyTags(FastTags<TagGroup.Global>.Parse("scrap100")));
                    if(yield<=0)continue;
                    recipe=scrap;nextInput=ProductionInventory.Clone(input.items);
                    nextInput[i].count--;if(nextInput[i].count==0)nextInput[i]=ItemStack.Empty;
                    product=new ItemStack(new ItemValue(type),yield);
                    duration=Math.Max(1,raw.ScrapTimeOverride>0?raw.ScrapTimeOverride:
                        EffectManager.GetValue(PassiveEffects.ScrappingTime,null,material.CraftComponentTime*yield,player));
                    key="scrap:"+s.itemValue.type+":"+s.itemValue.Quality+":"+yield;break;
                }
                if(recipe==null)return "待分解装备/材料样品不符";
            }
            else
            {
                if(player==null)return "等待机器所有者上线";
                string area=kind=="yfAutoKitchen"?"campfire":"forge";
                foreach(var candidate in CraftingManager.GetRecipes(ItemClass.GetForId(type).GetItemName()))
                {
                    if(candidate.IsScrap||candidate.craftingArea!=area||candidate.itemValueType!=type||!candidate.IsUnlocked(player)||candidate.GetOutputItemClass().HasQuality)continue;
                    if(candidate.craftingToolType>0&&!input.items.Where((s,i)=>!internalStorage||MachineInventory.IsInput(i)).Any(s=>!s.IsEmpty()&&s.itemValue.type==candidate.craftingToolType))continue;
                    var trial=ProductionInventory.Clone(input.items);bool enough=true;
                    candidate.craftingTier=candidate.GetCraftingTier(player);
                    foreach(var ingredient in candidate.GetIngredientsSummedUp())
                    {
                        int count=ingredient.count;
                        if(candidate.UseIngredientModifier)
                        {
                            count=(int)EffectManager.GetValue(PassiveEffects.CraftingIngredientCount,null,count,player,candidate,
                                FastTags<TagGroup.Global>.Parse(ingredient.itemValue.ItemClass.GetItemName()),true,true,true,true,true,candidate.craftingTier,true,false);
                            if(count>0)count=Math.Max(1,(int)(count*XUiM_Recipes.GetCraftingInputModifier(candidate)));
                        }
                        int ingredientType=ingredient.itemValue.type;
                        string ingredientName=ingredient.itemValue.ItemClass.GetItemName();
                        if(ingredientName.StartsWith("unit_"))ingredientType=ItemClass.GetItem("yfAutoIngot_"+ingredientName.Substring(5)).type;
                        if(count>0&&(ingredientType==0||!ProductionInventory.Consume(trial,ingredientType,count,inputLocked))){enough=false;break;}
                    }
                    if(!enough)continue;
                    recipe=candidate;nextInput=trial;
                    float modifier=candidate.tags.Test_AnySet(XUiM_Recipes.SandboxIgnoreTag)?1:XUiM_Recipes.CraftingOutputModifier;
                    int result=Math.Max(1,(int)(EffectManager.GetValue(PassiveEffects.CraftingOutputCount,null,candidate.count,player,candidate,candidate.tags)*modifier));
                    product=new ItemStack(new ItemValue(type),result);
                    duration=Math.Max(1,EffectManager.GetValue(PassiveEffects.CraftingTime,null,candidate.craftingTime,player,candidate,candidate.tags)*XUiM_Recipes.CraftingTimeModifier);
                    key=area+":"+type+":"+result+":"+duration+":"+string.Join(";",candidate.ingredients.Select(v=>v.itemValue.type+"x"+v.count));break;
                }
                if(recipe==null)return "缺材料/工具或配方未解锁";
            }
            if(float.IsNaN(duration)||float.IsInfinity(duration)||duration<=0||product.count<=0)return "配方数据异常";
            var nextOutput=ProductionInventory.Clone(internalStorage?nextInput:output.items);
            if(!ProductionInventory.Produce(nextOutput,product,i=>Locked(output,i)||internalStorage&&!MachineInventory.IsOutput(i),v=>v.ItemClass.Stacknumber.Value))return "输出箱满，生产暂停";
            if(state.Job!=key){state.Job=key;state.Seconds=0;}
            state.Seconds=Math.Min(duration,state.Seconds+1);machine.SetChunkModified();
            if(state.Seconds<duration)return "生产中 "+(int)(state.Seconds*100/duration)+"%";
            // One chunk serialization gate is held by Logistics throughout this commit.
            complete?.Invoke();
            if(!internalStorage)Array.Copy(nextInput,input.items,nextInput.Length);
            Array.Copy(nextOutput,output.items,nextOutput.Length);
            state.Seconds=0;source.SetChunkModified();target.SetChunkModified();
            source.SetModified();target.SetModified();return "完成：产出 "+product.count;
        }
    }
}
