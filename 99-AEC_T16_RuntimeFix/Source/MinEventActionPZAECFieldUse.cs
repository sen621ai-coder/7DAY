using AECT16RuntimeFix;

// The native factory prefixes MinEventAction and resolves the assembly suffix.
// PrimaryActionEnd covers both toolbelt use and inventory ExecuteInstantAction.
public sealed class MinEventActionPZAECFieldUse : MinEventActionBase
{
    public override void Execute(MinEventParams context)
    {
        var player = context.Self as EntityPlayer;
        if (player == null || (player.isEntityRemote && !context.IsLocal)) return;
        EndgameExpansionRuntime.UseFieldItem(player, context.ItemValue);
    }
}
