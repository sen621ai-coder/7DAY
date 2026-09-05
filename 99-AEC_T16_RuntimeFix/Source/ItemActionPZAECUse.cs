// Native Eat's animation-free toolbelt path consumes an item but only fires its
// end event when a UI stack controller exists. Preserve its inventory behavior
// and supply the missing end event for the toolbelt path exactly once.
public sealed class ItemActionPZAECUse : ItemActionEat
{
    public override bool ExecuteInstantAction(EntityAlive _holdingEntity, ItemStack _itemStack,
        bool _isHoldingItem, XUiC_ItemStack stackController)
    {
        if (_holdingEntity == null || _itemStack == null || _itemStack.IsEmpty()) return false;
        var value = _itemStack.itemValue.Clone();
        var context = _holdingEntity.MinEventContext;
        context.ItemValue = value;
        if (ExecutionRequirements != null && !ExecutionRequirements.IsValid(context))
        {
            if (_holdingEntity is EntityPlayerLocal local) GameManager.ShowTooltip(local, "需要同阶四件套、100共鸣，且装置冷却结束。");
            return false;
        }
        string reason;
        if (!AECT16RuntimeFix.EndgameExpansionRuntime.CanUseFieldItem(_holdingEntity as EntityPlayer, value, out reason))
        {
            if (_holdingEntity is EntityPlayerLocal local) GameManager.ShowTooltip(local, reason);
            return false;
        }
        bool used = base.ExecuteInstantAction(_holdingEntity, _itemStack, _isHoldingItem, stackController);
        if (used && stackController == null)
        {
            context.ItemValue = value;
            value.FireEvent(MinEventTypes.onSelfPrimaryActionEnd, context);
            _holdingEntity.FireEvent(MinEventTypes.onSelfPrimaryActionEnd, false);
        }
        return used;
    }
}
