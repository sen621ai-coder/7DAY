using System;
namespace YFAutomation
{
    public static class InventoryTransfer
    {
        public static int MoveUnfiltered(ItemStack[] input,ItemStack[] output,Func<int,bool> inputLocked,Func<int,bool> outputLocked,Func<ItemValue,int> capacity)
        {
            if(input==null||output==null)return 0;
            for(int i=0;i<input.Length;i++)
            {
                var stack=input[i];if(stack==null||stack.IsEmpty()||inputLocked(i))continue;
                for(int pass=0;pass<2;pass++)for(int j=0;j<output.Length;j++)
                {
                    if(outputLocked(j))continue;var dst=output[j];bool empty=dst==null||dst.IsEmpty();
                    if(pass==0&&empty||pass==1&&!empty||!empty&&!StackCompatibility.Matches(dst.itemValue,stack.itemValue))continue;
                    int amount=TransferRules.Amount(stack.count,empty?0:dst.count,capacity(stack.itemValue),TransferRules.Batch);
                    if(amount==0)continue;
                    var from=stack.Clone();from.count-=amount;var to=empty?stack.Clone():dst.Clone();to.count=(empty?0:dst.count)+amount;
                    input[i]=from.count==0?ItemStack.Empty:from;output[j]=to;return amount;
                }
            }
            return 0;
        }
        // Slot zero of the output is a permanent, player-supplied filter sample.
        public static int Move(ItemStack[] input,ItemStack[] output,Func<int,bool> inputLocked,Func<int,bool> outputLocked,Func<ItemValue,int> capacity,int filterType=0)
        {
            if(input==null||output==null||output.Length<2)return 0;
            var sample=output[0];if(filterType==0){if(sample==null||sample.IsEmpty())return 0;filterType=sample.itemValue.type;}
            for(int i=0;i<input.Length;i++)
            {
                var stack=input[i];if(stack==null||stack.IsEmpty()||inputLocked(i)||stack.itemValue.type!=filterType)continue;
                for(int pass=0;pass<2;pass++)for(int j=1;j<output.Length;j++)
                {
                    if(outputLocked(j))continue;
                    var dst=output[j];bool empty=dst==null||dst.IsEmpty();
                    if(pass==0&&empty||pass==1&&!empty||!empty&&!StackCompatibility.Matches(dst.itemValue,stack.itemValue))continue;
                    int amount=TransferRules.Amount(stack.count,empty?0:dst.count,capacity(stack.itemValue),TransferRules.Batch);
                    if(amount==0)continue;
                    var from=stack.Clone();from.count-=amount;
                    var to=empty?stack.Clone():dst.Clone();to.count=(empty?0:dst.count)+amount;
                    input[i]=from.count==0?ItemStack.Empty:from;output[j]=to;return amount;
                }
            }
            return 0;
        }
    }
}
