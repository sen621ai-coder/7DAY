// Resolved by DialogFromXml through the assembly-qualified requirement name.
// Read current GS on every menu check: no stale watcher CVar or perk cap.
public sealed class DialogRequirementAECLegendaryGameStage : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        int minimum;
        return player != null && int.TryParse(Value, out minimum) &&
            minimum >= 0 && player.gameStage >= minimum;
    }
}
